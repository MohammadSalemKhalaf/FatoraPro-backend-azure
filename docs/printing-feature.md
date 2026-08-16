# The Dual-Pipeline Printing Pattern (PDF + Direct Thermal ESC/POS)

Source project: a Flutter invoicing app ("this project"). This document describes a **general, reusable pattern** for shipping two independent print paths from one document model — an OS-native PDF path and a direct-to-thermal-printer ESC/POS path — plus the specific, hard-won techniques for getting Arabic (or any complex/RTL script) to render correctly in both. Everything below is written so it can be replicated in an unrelated project with a different domain model; this project's own entities (`Order`, `Customer`) are only the illustrative example data being printed.

All code snippets are copied verbatim from the files read for this document, with real paths given as "this project's equivalent of `lib/...`".

---

## 1. The two print pipelines, and why both exist

Two completely separate rendering pipelines exist, sharing almost nothing at the code level, because they solve different problems and have different physical constraints:

| | PDF pipeline | Thermal pipeline |
|---|---|---|
| Purpose | Share-as-file, OS print dialog (any printer/AirPrint/network printer), archival document | Fast one-tap print straight to a paired receipt printer, no dialog |
| Page shape | Fixed A4 (`PdfPageFormat.a4`) | Unknown width — 384 dots (58mm) to 576 dots (80mm) or custom, unbounded height |
| Library | `pdf` (vector PDF builder) + `printing` (OS print/share integration) | Hand-rolled: `dart:ui` `Canvas`/`TextPainter` → `image` package bitmap → `esc_pos_utils_plus` raster encoder → raw socket bytes |
| Text engine | `pdf` package's own PDF text layer (does not shape Arabic — see §2) | Flutter's own `TextPainter` (shapes Arabic correctly, same engine used on-screen) |
| Entry points | `lib/features/orders/data/invoice_pdf_builder.dart` → `lib/features/orders/presentation/widgets/share_invoice.dart` | `lib/core/printing/thermal_printing_service.dart` |

**Why not one pipeline for both?** The project's git history documents an entire failed generation of the thermal pipeline that tried to reuse the PDF path: render the invoice as a PDF, then rasterize that PDF page to a bitmap via `Printing.raster()`, then feed the bitmap to the ESC/POS encoder ("Phase 0"). This produced two back-to-back bugs from the same root cause — `Printing.raster()`'s alpha channel didn't behave as assumed, so unpainted PDF background came out as black (RGB `0,0,0` at alpha `0`, and the ESC/POS raster encoder ignores alpha entirely, reading raw RGB), and the first attempted fix (alpha-blending toward white) blew away real black text along with the background, producing blank prints. The commit that replaced this approach states the lesson directly:

> "The PDF-rasterize approach ... produced a black background, then a blank page after the fix, both from `Printing.raster()`'s alpha channel not behaving as assumed. Two bugs from one root cause meant the approach itself needed to go, not another patch." (commit `593d6ae`)

The fix was architectural, not a patch: **stop going through a PDF for thermal output at all.** Draw straight onto a `dart:ui.Canvas` with `TextPainter`, producing an RGB bitmap directly — no PDF, no `Printing.raster()`, no alpha ambiguity possible by construction. This is the single most important lesson in this pattern: **when a library's output format doesn't match your actual target (raster bytes, not a page description), don't round-trip through the wrong format and patch the leaks — swap in the primitive that natively produces what you need.**

### The printer-agnostic intermediate model

Both branches of the thermal pipeline (but *not* the PDF pipeline — see below) share a single content-only intermediate model: `PrintDocument` (this project's equivalent of `lib/core/printing/model/print_document.dart`). Its own doc comment states the design intent plainly:

```dart
/// Generic, printer-agnostic receipt content. This type has no idea what an
/// [Order] or a Bluetooth printer is - [SmartLayoutEngine] is the only thing
/// that turns it into a bitmap. [PrintDocument.fromOrder]/[PrintDocument.
/// testReceipt] are the only two places that know about Fatora's actual data
/// model; everything else in lib/core/printing stays reusable as-is in a
/// future project.
```

`PrintDocument` is a plain data holder: an optional logo, and typed lists of `PrintLine` (text + a **semantic** style enum, not a font size — see §5), `PrintTableRow`, `PrintTotalRow`. It knows nothing about Bluetooth, ESC/POS, dots, or fonts. The **only** two places that translate a real domain object (`Order`) into this model are `PrintDocument.fromOrder(...)` and `PrintDocument.testReceipt(...)` — static factories on the model itself. This is the seam a reader should copy exactly: **one file (or one small factory method) is the sole place that knows "my business entity X maps to a receipt like this"; every other file in the printing subsystem is domain-blind and portable to a different app unchanged.**

The pipeline downstream of `PrintDocument` is a strict four-stage chain, each stage behind its own interface so a stage can be swapped without touching its neighbors:

```
PrintDocument (content)
   -> SmartLayoutEngine.render()   produces an img.Image bitmap at a target dot width
   -> PrinterDriver.encode()       turns the bitmap into printer-language bytes (ESC/POS GS v 0)
   -> PrinterTransport.send()      moves the bytes over Bluetooth RFCOMM or a raw TCP socket
```

`ThermalPrintingService._print()` (this project's equivalent of `lib/core/printing/thermal_printing_service.dart`) is the only file that wires all four stages together, selecting the concrete `PrinterDriver`/`PrinterTransport` implementation from saved preferences:

```dart
static PrinterTransport _transportFor(PrinterProfile profile) =>
    switch (profile.transportType) {
      PrinterTransportType.bluetooth => BluetoothTransport(profile.bluetoothMac!),
      PrinterTransportType.wifi => WifiSocketTransport(profile.wifiHost!, port: profile.wifiPort),
    };

static PrinterDriver _driverFor(PrinterProfile profile) =>
    switch (profile.language) {
      PrinterLanguage.escPos => const EscPosDriver(),
      PrinterLanguage.cpcl => throw const ThermalPrintException('طباعة CPCL غير مدعومة بعد.'),
    };
```

Adding a new printer command language later (CPCL, ZPL) means writing one more `PrinterDriver` implementation — nothing upstream (layout, model) changes. Adding a new transport (e.g. USB) means one more `PrinterTransport` implementation. This is the concrete payoff of the interface-per-stage design: `abstract class PrinterDriver { Future<List<int>> encode(img.Image bitmap, {...}); }` and `abstract class PrinterTransport { Future<bool> connect(); Future<bool> send(List<int> bytes); Future<void> disconnect(); }` are each under 20 lines.

### Where the two pipelines diverge (no shared model)

The PDF pipeline does **not** go through `PrintDocument`. `buildInvoicePdf(Order order, {...})` (this project's equivalent of `lib/features/orders/data/invoice_pdf_builder.dart`) builds `pw.Widget` trees directly from the domain object, because the PDF path has requirements the thermal model was never designed for: multiple visual templates (minimal/executive/detailed), a colored gradient sidebar, embedded product images, a fixed A4 page format with CSS-Grid-like `pw.Table` column widths in fixed point units. Trying to force both an A4 multi-column document *and* an unbounded-height single-column receipt through one shared model would have made the model's abstraction leak in both directions. **Lesson: share a model between two output pipelines only up to the point where their actual layouts diverge — past that point, a shared abstraction costs more than it saves.** Two small independent builders were the right call here; the only thing actually shared between them is the Arabic-rasterization technique (§2), which is a leaf utility, not a document model.

The caller-facing choice between the two pipelines is a single boolean check, made once, at the UI layer (this project's equivalent of `lib/features/orders/presentation/invoice_review_page.dart`):

```dart
Future<void> _print() async {
  if (ThermalPrintingService.isConfigured) {
    try {
      await ThermalPrintingService.printThermal(order: _order, profile: _profile);
    } catch (error) { /* show a localized error notice */ }
    return;
  }
  await printInvoice(_order, template: _template, profile: _profile, /* ... */);
}
```

`ThermalPrintingService.isConfigured` simply forwards to `PrinterPreferences.instance.isConfigured` (§4) — whether a printer has ever been saved. No printer configured → OS print dialog, no behavior change for users who never opt in. This is a deliberately dumb, unconditional fallback: **the "new" pipeline never has to prove itself works before the "old" one is removed; it is purely additive until a user explicitly sets it up.**

---

## 2. Font setup and the Arabic-rasterization workaround

### Bundling

`pubspec.yaml` declares one custom font family with two weights, plus two more weight files that ship as assets but are loaded programmatically rather than via the `fonts:` block:

```yaml
flutter:
  assets:
    - assets/fonts/
  fonts:
    - family: NotoNaskhArabicPdf
      fonts:
        - asset: assets/fonts/NotoNaskhArabic-Regular.ttf
        - asset: assets/fonts/NotoNaskhArabic-Bold.ttf
          weight: 700
```

Two fonts are bundled: **Noto Naskh Arabic** (registered as the Flutter font family `NotoNaskhArabicPdf`, used both on-screen for Arabic UI text and as the rasterization source font for PDF/thermal output) and **Noto Sans** (`assets/fonts/NotoSans-Regular.ttf` / `-Bold.ttf`, loaded ad hoc into the `pdf` package for the PDF's Latin/numeric text — invoice numbers, currency figures, dates). Why two families: Noto Naskh Arabic is an Arabic *script* font — it doesn't cover Latin glyphs comprehensively enough to reason about — while Noto Sans is a clean, complete Latin-only body font. Splitting by script rather than trying to find one font that does both cleanly, then choosing per-string which one applies, is the standard mixed-script-document approach.

### Loading

The PDF pipeline loads TTF bytes into the `pdf` package's own font type via a one-line helper (this project's equivalent of `lib/core/pdf/arabic_pdf_text.dart`):

```dart
Future<pw.Font> loadPdfFont(String assetPath) async {
  final bytes = await rootBundle.load(assetPath);
  return pw.Font.ttf(bytes);
}
```

The thermal pipeline never calls this — it never touches the `pdf` package at all (§1) — and instead references the Flutter-registered family name (`'NotoNaskhArabicPdf'`) directly in a `flutter.TextStyle`, since Flutter's own text engine already has it loaded via the `pubspec.yaml` `fonts:` declaration.

### The exact problem, and the exact workaround

The `pdf` package's own text widget (`pw.Text`) cannot correctly render Arabic. The reasoning, preserved verbatim in the source because it documents two rejected fixes before the real one, in `arabic_pdf_text.dart`:

```dart
/// Renders Arabic text as a crisp raster image using Flutter's own text
/// engine instead of the `pdf` package's `pw.Text`.
///
/// The `pdf` package never applies Arabic letter-joining shaping on its
/// own - every letter draws in its standalone form. Pre-shaping the string
/// first (substituting each letter for its correct initial/medial/final
/// glyph) looked like the fix, but it isn't: the `pdf` package always runs
/// text through the `bidi` package for right-to-left layout, and `bidi`'s
/// internal Unicode composition step throws a `RangeError` the moment it
/// sees Arabic presentation-form characters - it was only ever built to
/// accept plain, unshaped Arabic. So there's no way to get `pw.Text` to
/// render joined Arabic without crashing PDF generation outright.
/// Rasterizing the text with Flutter's `TextPainter` sidesteps the `pdf`
/// package's text pipeline entirely for Arabic content - Flutter's own
/// renderer already shapes Arabic correctly, which is exactly why it has
/// always looked right on screen in the app itself.
```

This is worth restating as a general principle, because it applies to any "print a PDF with a PDF-generation library that lacks a real text-shaping engine" situation: **there are two independent failure modes to distinguish. (1) Glyph joining/shaping (does "س" pick the correct medial form next to its neighbors) — solvable in isolation by pre-shaping. (2) Bidi/layout composition (does the library's line-breaking and directional-run logic accept the *already-shaped* Unicode presentation-form characters at all) — this project found that fixing (1) triggers a hard crash in (2), because the shaped characters are outside what the bidi step was built to accept.** When a "pre-shape the string" fix produces a crash somewhere else in the same library's pipeline, that is a sign the workaround needs to leave the library's text pipeline entirely, not iterate on the string transform.

The actual workaround — render Arabic with Flutter's own `TextPainter` onto an offscreen canvas, capture it as a PNG, and place that PNG as a `pw.Image` instead of `pw.Text`:

```dart
Future<pw.Image> arabicTextImage(
  String text, {
  required double fontSize,
  required PdfColor color,
  bool bold = false,
}) async {
  const scale = 3.0; // Render well above PDF point size for print crispness.
  final painter = flutter.TextPainter(
    text: flutter.TextSpan(
      text: text,
      style: flutter.TextStyle(
        fontFamily: 'NotoNaskhArabicPdf',
        fontSize: fontSize * scale,
        fontWeight: bold ? flutter.FontWeight.bold : flutter.FontWeight.normal,
        color: pdfColorToFlutterColor(color),
      ),
    ),
    textDirection: flutter.TextDirection.rtl,
  )..layout();

  final recorder = ui.PictureRecorder();
  final canvas = ui.Canvas(recorder);
  painter.paint(canvas, ui.Offset.zero);
  final picture = recorder.endRecording();
  final width = painter.width.ceil().clamp(1, 4096);
  final height = painter.height.ceil().clamp(1, 4096);
  final image = await picture.toImage(width, height);
  final byteData = await image.toByteData(format: ui.ImageByteFormat.png);

  return pw.Image(
    pw.MemoryImage(byteData!.buffer.asUint8List()),
    width: width / scale,
    height: height / scale,
  );
}
```

Two details matter for anyone copying this technique: (1) render at `scale` (3x here) *pixels-per-PDF-point* so the raster is crisp when printed/zoomed, then divide the placed `pw.Image` width/height back down by that same scale — the same split is used in `returned_stamp_pdf.dart` for a rotated stamp graphic, whose own comment names the exact bug that happens if you forget the divide-back-down step:

> "Forgetting that split (drawing at N pixels and then also placing at N *points*) is exactly what made the stamp render page-sized-or-bigger in a printed/shared PDF while looking right in the Flutter preview, which uses `size` as logical pixels directly with no separate raster-resolution concept at all."

(2) a single dispatcher function (`pdfLabel`) hides the Arabic/non-Arabic branch from every call site, so callers never special-case scripts themselves:

```dart
Future<pw.Widget> pdfLabel(String text, {required double fontSize, PdfColor color = PdfColors.black,
    pw.Font? font, pw.Font? boldFont, pw.TextAlign align = pw.TextAlign.left}) async {
  if (!isArabicText(text)) {
    return pw.Text(text, textAlign: align, style: pw.TextStyle(fontSize: fontSize, color: color, font: font));
  }
  final image = await arabicTextImage(text, fontSize: fontSize, color: color, bold: font != null && font == boldFont);
  final alignment = switch (align) {
    pw.TextAlign.right => pw.Alignment.centerRight,
    pw.TextAlign.center => pw.Alignment.center,
    _ => pw.Alignment.centerLeft,
  };
  return pw.Align(alignment: alignment, child: image);
}
```

`isArabicText` is a plain regex over the Arabic Unicode block: `RegExp(r'[؀-ۿݐ-ݿࢠ-ࣿ]').hasMatch(text)`. This is the general shape to copy for *any* script the PDF library can't shape: one script-detection predicate, one raster-fallback renderer, one dispatcher function that every text-producing call site goes through — never duplicate the branch at each call site.

Crucially, the entire thermal pipeline sidesteps this problem class by construction (§1): since `SmartLayoutEngine` draws with Flutter's `TextPainter` directly onto a `Canvas` — never through the `pdf` package — Arabic text needs **zero special-casing** there. Its own doc comment calls this out explicitly: "there is no PDF, no `Printing.raster()`, and no alpha-channel ambiguity to reason about — the two bugs from the previous rasterize-a-PDF approach cannot recur here by construction." This is the strongest argument in the whole codebase for the architectural choice in §1: picking the right primitive doesn't just fix one bug, it makes a whole bug *class* structurally impossible.

---

## 3. RTL / non-Latin-script layout technique

The naive `Directionality(textDirection: TextDirection.rtl, child: ...)` (or the `pdf` package's `pw.Directionality`) wrapping an entire document is **insufficient** here for two independent reasons documented in the source:

1. In the thermal pipeline, right-aligned lines painted with `TextAlign.right` inside a full-width box measured the box's width, not the text's own rendered width, and landed hugging the *left* margin on real hardware — confirmed against physical output, not just simulator preview. The fix bypasses `TextAlign` positioning for these lines entirely:

```dart
/// Right-aligned lines are positioned by measuring the text's own
/// rendered width (`TextWidthBasis.longestLine`) and painting flush
/// against the right margin directly, rather than trusting
/// `TextAlign.right`'s placement within a full-page-width box - confirmed
/// on real hardware that the latter left these lines hugging the left
/// margin instead. The item table's own per-cell `TextAlign.right` (a
/// much narrower box) prints correctly and is untouched.
static _Block _textBlock(PrintLine line, double contentWidth) {
  final painter = _layout(line.text, _styleFor(line.style), maxWidth: contentWidth,
      align: ..., widthBasis: flutter.TextWidthBasis.longestLine);
  return _Block(
    height: painter.height + 2,
    paint: (canvas, marginLeft, y, width) {
      final dx = centered ? marginLeft : marginLeft + width - painter.width;
      painter.paint(canvas, ui.Offset(dx, y));
    },
  );
}
```

The general lesson: `TextAlign` positions a glyph run **within the box it was laid out against**, which is a statement about the box, not about where the ink ends up relative to a *different* reference edge (the physical margin) once that box is wider than the text. When "confirmed working in preview, wrong on real hardware" happens for an alignment bug, measure the rendered width yourself (`TextWidthBasis.longestLine`) and position the paint origin directly against the edge you actually care about — don't keep trusting the alignment enum to do it.

2. Both the PDF and the on-screen invoice-preview widget use a different, complementary technique: **the document's own physical layout stays LTR, and RTL is applied per-element, plus columns are physically reordered to match reading order.** This is deliberate, not an oversight — RTL is not a single global flag flipped once; it is a per-row, per-column decision baked into how each widget tree is composed. From the PDF item table (`invoice_pdf_builder.dart`):

```dart
// The item table is laid out physically from left to right in reverse
// column order so it reads correctly from the right edge in Arabic:
// # | description | price | quantity | total.
...
pw.Table(
  // Physical LTR widths are the reverse of the RTL reading order:
  // total, price, quantity, description, number.
  columnWidths: const {
    0: pw.FixedColumnWidth(58),  // total     (rightmost when read RTL)
    1: pw.FixedColumnWidth(62),  // price
    2: pw.FixedColumnWidth(40),  // quantity
    3: pw.FlexColumnWidth(),     // description
    4: pw.FixedColumnWidth(20),  // # (leftmost physically, rightmost when read)
  },
  ...
)
```

The row's `children` list is built in that same physically-reversed order (`_cell(total)`, `_cell(price)`, `_cell(quantity)`, description column, `_cell('#')`) — the table never uses `pw.Directionality(textDirection: rtl)` around itself at all. The outer page instead sets `pw.Directionality(textDirection: pw.TextDirection.ltr)` around the whole document body, and only the top-level sidebar (the "executive" template's colored panel) gets its own `pw.Directionality(textDirection: rtl)` wrapper for its Column of stacked labels, because a vertically-stacked column of full-width lines has no left/right ordering problem to begin with.

The on-screen preview widget (this project's equivalent of `lib/features/orders/presentation/widgets/invoice_document.dart`) uses the identical trick for `Row`s of table cells, but instead of physically reordering `children`, it sets `textDirection: TextDirection.rtl` **on the individual `Row` widget itself** (Flutter's `Row.textDirection` parameter reverses which end `start`/`end`-relative children are laid out from) while the parent `Container` around the whole document stays `Directionality(textDirection: TextDirection.ltr)`:

```dart
/// Shared by all templates. The row itself is explicitly RTL while the
/// rest of the document keeps its existing physical page layout.
Widget _simpleTableHeaderRow() {
  return Row(
    textDirection: TextDirection.rtl,
    children: [
      SizedBox(width: 24, child: Text('#', ...)),
      Expanded(flex: 5, child: ... Text('الوصف', textDirection: TextDirection.rtl, ...)),
      Expanded(child: ... Text('الكمية', textDirection: TextDirection.rtl, ...)),
      ...
    ],
  );
}
```

So the pattern has **two different concrete implementations of the same idea**, chosen per-layer:
- **Flutter widgets** (on-screen preview): set `textDirection: TextDirection.rtl` on the individual `Row`/`Text` that needs it — Flutter honors this per-widget, so children are written in natural/logical order and Flutter reverses the paint order for that one row.
- **The `pdf` package** (`pw.Table`, which has no per-widget `textDirection` override that affects column order): physically write `children`/`columnWidths` in the reversed (visual) order yourself, and reserve `pw.Directionality` for widgets where it actually changes something (vertical stacks, single lines).

**Why "just set `textDirection: rtl` on the whole page" was insufficient**: doing that once at the document root does correctly mirror *simple* vertical stacks of full-width lines, but it does nothing to fix per-cell alignment inside a fixed-width-column table (the `pdf` package's `pw.Table` column widths are physical/positional, not logical, regardless of the ambient `Directionality`), and it does nothing about the `TextWidthBasis` alignment bug in §3.1 above, which is orthogonal to direction entirely. RTL in a mixed-script commercial document is not "flip one flag" — it is a per-component decision, and the source code documents exactly which components needed which technique and why, rather than assuming one blanket fix would cover every widget.

---

## 4. Print target discovery/connection

### Bluetooth: OS-level pairing, in-app connection only

Printer *pairing* deliberately happens in the phone's own Bluetooth settings, never inside the app — the setup page only lists devices the OS has already paired and lets the user pick one to save as the active printer:

```dart
/// Already-OS-paired Bluetooth devices only - see printer_setup_page.dart
/// for why pairing a new device happens in system Bluetooth settings,
/// never in-app.
static Future<List<PairedPrinter>> listPaired() async {
  if (!await _ensurePermission()) return const [];
  final result = await _channel.invokeMethod<List<Object?>>('pairedBluetooths');
  return (result ?? const []).whereType<String>().map((entry) {
    final separator = entry.indexOf('#');
    if (separator < 0) return null;
    return PairedPrinter(name: entry.substring(0, separator), mac: entry.substring(separator + 1));
  }).whereType<PairedPrinter>().toList();
}
```

This offloads all of the actual Bluetooth pairing UX (PIN entry, device scanning, trust prompts) to the platform, which already has a mature, familiar flow for it — the app only needs a `PairedPrinter { name, mac }` value type and a picker list.

**Connection**, however, does *not* go through a third-party Flutter Bluetooth-printing plugin. The project tried `print_bluetooth_thermal` first and hit two hardware-specific failures documented directly in commit messages: that plugin opens a *secure* RFCOMM socket (`createRfcommSocketToServiceRecord`), which this printer family's Bluetooth stack never completes the pairing handshake for, even though the same device connects fine to a known-working third-party app; and separately, the plugin's own native "already connected" guard had what the commit calls "a stray comparison, not the assignment it clearly meant to be" (`outputStream == null` instead of clearing it), so a previous failed attempt permanently wedged all future ones. Both were root-caused by reading the plugin's own source and a known-working reference app's config — not guessed. The fix was to drop the plugin and write a small **native platform-channel bridge** (`ThermalPrinterBridge.kt`) that opens the *insecure* RFCOMM variant instead:

```dart
/// Talks to ThermalPrinterBridge.kt (android/app/.../ThermalPrinterBridge.kt)
/// directly over our own MethodChannel, instead of the print_bluetooth_thermal
/// plugin. That plugin opens a SECURE RFCOMM socket
/// (createRfcommSocketToServiceRecord) - confirmed on real hardware that
/// this specific generic ESC/POS printer (and evidently many clone
/// printers) never completes Android's secure-pairing handshake over that
/// socket type, even though the identical paired device connects fine to
/// third-party printing apps. Our own native bridge opens the INSECURE
/// variant instead, which is what those apps use.
class BluetoothPrinterConnection {
  static const _channel = MethodChannel('fatora/thermal_bluetooth');
  ...
}
```

`connect()` also retries once after a short delay, because "cheap ESC/POS printer clones are commonly flaky on the very first RFCOMM attempt right after pairing/a permission change":

```dart
static Future<bool> connect(String mac) async {
  if (!await _ensurePermission()) return false;
  if (await _channel.invokeMethod<bool>('bluetoothEnabled') != true) return false;
  if (await _attemptConnect(mac)) return true;
  await disconnect();
  await Future.delayed(const Duration(milliseconds: 700));
  return _attemptConnect(mac);
}
```

**Lesson for anyone integrating a third-party Bluetooth/hardware plugin**: if it fails only against specific real devices despite looking correct on paper, read the plugin's own native source before assuming your usage is wrong — a stale-state bug or a wrong socket-security mode in the plugin itself is a real, previously-encountered failure mode, and a thin custom native bridge that owns exactly the one RFCOMM call needed is a reasonable, small escape hatch.

### Wi-Fi: raw TCP socket, no plugin

The Wi-Fi transport needs no package at all — `dart:io Socket` is the SDK's own TCP client, connecting to port 9100 ("raw"/JetDirect), "the de-facto standard virtually every network ESC/POS and label printer listens on":

```dart
class WifiSocketTransport extends PrinterTransport {
  WifiSocketTransport(this.host, {this.port = 9100});
  Socket? _socket;
  @override
  Future<bool> connect() async {
    try {
      _socket = await Socket.connect(host, port, timeout: const Duration(seconds: 5));
      return true;
    } catch (error) { _socket = null; return false; }
  }
  ...
}
```

There is no discovery step for Wi-Fi at all — the user types the printer's IP directly. The source is explicit that this transport is "code-complete and exercised by its own unit tests for lifecycle correctness, but not physically print-verified — there is no Wi-Fi/TCP printer available to test against." **This is worth copying as a documentation habit**: when a code path can't be hardware-verified, say so directly in the source rather than implying parity with the verified path.

### What gets persisted, and the printer-profile model

`PrinterPreferences` (this project's equivalent of `lib/core/printing/printer_preferences.dart`) is a plain singleton backed by `flutter_secure_storage`, storing exactly the fields needed to reconstruct a connection and a layout target — nothing else:

```dart
PrinterTransportType transportType = PrinterTransportType.bluetooth;
PrinterLanguage language = PrinterLanguage.escPos;
String? bluetoothMac;
String? bluetoothName;
String? wifiHost;
int wifiPort = 9100;
String? _widthKind;        // 'mm80' | 'mm58' | 'custom' | null(=mm80)
int? _widthCustomDots;
```

each field mapped to its own secure-storage key (e.g. `thermal_printer_bluetooth_mac`, `thermal_printer_wifi_host`), loaded in one batched `Future.wait` on app start. `isConfigured` is the single predicate every print call site checks: `bluetoothMac != null` for Bluetooth, non-empty `wifiHost` for Wi-Fi.

The **printer profile** — everything downstream code needs to render and send a receipt — is a separate, ephemeral, immutable value type built fresh at print time from the persisted preferences (`PrinterPreferences.toProfile()`), never stored directly:

```dart
class PrinterProfile {
  const PrinterProfile({
    required this.transportType, required this.language, required this.paperWidth,
    this.bluetoothMac, this.bluetoothName, this.wifiHost, this.wifiPort = 9100,
  });
  final PrinterTransportType transportType;
  final PrinterLanguage language;
  final PaperWidthPreset paperWidth;
  ...
}
```

Paper width is deliberately **not** a free-typed number as the primary control — it's one of a small set of fixed, verified presets, because a mismatched raster width silently produces broken alignment (see §8's `bd340de`):

```dart
/// A fixed, known paper-width choice - deliberately not a free-typed
/// number. [printableDots] is the number that actually matters for
/// rendering; it is NOT `paperWidthMm * dpi / 25.4` - real thermal print
/// heads have a printable area narrower than the paper itself (the
/// industry-standard combinations are 80mm paper -> 576 dots and 58mm paper
/// -> 384 dots, both at 203 DPI), so each preset states its own verified
/// dot width rather than deriving one that wouldn't match real hardware.
class PaperWidthPreset {
  final String label;
  final double paperWidthMm;
  final int printableDots;   // the number that actually matters
  final int dpi;
  final bool verified;       // true only for presets confirmed on real hardware

  static const mm80 = PaperWidthPreset(label: '80mm', paperWidthMm: 80, printableDots: 576, dpi: 203, verified: true);
  static const mm58 = PaperWidthPreset(label: '58mm', paperWidthMm: 58, printableDots: 384, dpi: 203);
  factory PaperWidthPreset.custom(int printableDots, {int dpi = 203}) => ...;
}
```

Two details worth copying: (1) the preset stores `printableDots` as ground truth, not a formula the reader is trusted to derive correctly from `paperWidthMm` — printable width is a hardware property, not a computable geometric fact from paper width alone. (2) a `verified: bool` field distinguishes "confirmed against real hardware" from "believed correct by spec" — this is a small, cheap way to encode confidence level directly in a data model rather than only in a comment.

`PrinterLanguage` is likewise a closed enum (`escPos`, `cpcl`) rather than a string, gated by the requirement that "every value here must have raster/graphics support" — the model only admits printer languages the driver layer (§1) actually knows how to encode a bitmap into.

---

## 5. Centralizing text style/typography

Font size and weight are **not** scattered as literals across call sites in the thermal pipeline. `PrintLine`/`PrintTotalRow` carry a **semantic** style — an enum describing the text's *role* in the document, not its rendering:

```dart
/// The semantic role of a line of text - [SmartLayoutEngine] maps this to an
/// actual font size/weight for whatever printer profile it's rendering
/// against. Kept semantic (not "14pt bold") so the same [PrintDocument] can
/// be rendered at any paper width/DPI without callers thinking in pixels.
enum PrintTextStyle { title, regular, bold, muted, accent }
```

Every concrete pixel value lives in exactly one place, a single switch expression mapping the enum to a concrete `flutter.TextStyle`:

```dart
static flutter.TextStyle _styleFor(PrintTextStyle style) => switch (style) {
  PrintTextStyle.title => const flutter.TextStyle(
    fontFamily: _fontFamily, fontSize: 25, fontWeight: flutter.FontWeight.w900, color: flutter.Color(0xFF000000)),
  PrintTextStyle.bold => const flutter.TextStyle(
    fontFamily: _fontFamily, fontSize: 23, fontWeight: flutter.FontWeight.w900, color: flutter.Color(0xFF000000)),
  PrintTextStyle.accent => const flutter.TextStyle(
    fontFamily: _fontFamily, fontSize: 23, fontWeight: flutter.FontWeight.w900, color: flutter.Color(0xFF000000)),
  // Deliberately stays a step below the other roles (still up from 13)
  // rather than also matching body text exactly - this is secondary
  // information (address/phone/footer/copyright), and matching the
  // body-text size exactly would erase the one piece of visual
  // hierarchy this layout has left, on top of risking wrap/clipping on
  // narrow receipt paper for lines that are often the longest on the page.
  PrintTextStyle.muted => const flutter.TextStyle(
    fontFamily: _fontFamily, fontSize: 19, fontWeight: flutter.FontWeight.w800, color: flutter.Color(0xFF000000)),
  PrintTextStyle.regular => const flutter.TextStyle(
    fontFamily: _fontFamily, fontSize: 23, fontWeight: flutter.FontWeight.w900, color: flutter.Color(0xFF000000)),
};
```

**Why this shape, and what a clean implementation should do**: with this design, every call site (`_textBlock`, `_totalRowBlock`, `_tableBlock`) reads `_styleFor(line.style)` and never writes a numeric font size itself — a global "make everything darker/bolder" change (which happened at least twice in this project's history, see §8) is a one-function edit, not a project-wide grep-and-replace. The alternative rejected implicitly here — scattering `TextStyle(fontSize: 23, fontWeight: FontWeight.w900)` literals at each of a dozen call sites — would make the same change require finding and editing every literal individually, with the real risk of missing one and ending up with a visually inconsistent document. The tradeoff is a small one: callers must think in terms of semantic roles (`title`/`muted`/`accent`) rather than "I want it a bit bigger here", which occasionally requires adding a new enum value rather than just bumping a number — a worthwhile constraint, since it keeps the total number of distinct text treatments in the document small and intentional rather than ad hoc.

Note something almost every role in this table currently resolves to nearly the same size/weight (`w900`, 19–25pt) — a direct consequence of the thermal-hardware finding in §8 that anything less than heavy/bold washes out under the printer's 1-bit threshold. The semantic-enum design is exactly what made that global correction cheap to apply and keep applied.

The **PDF pipeline does not have an equivalent central style function** — `invoice_pdf_builder.dart` passes `fontSize:`/`color:`/`font:` literals directly at each of ~25 call sites to its local `label(...)` helper (itself a thin wrapper around `pdfLabel`, see §2). This is a real inconsistency between the two pipelines worth calling out rather than glossing over: the PDF side has three visual templates with genuinely different, closely-tuned per-element sizes (title is 22pt on `executive` but 28pt on the other two templates; dates are 7pt labels / 11-13pt values depending on template), so a single semantic-role table would need a size *per template per role* — a two-dimensional lookup — to fully replace the current literals. A reader replicating this pattern in a document with only one visual template should still centralize; a reader replicating it with several visually distinct templates should consider a `Map<(Template, Role), TextStyle>` or a `styleFor(Template, Role)` function rather than leaving literals scattered as this project currently does on the PDF side.

---

## 6. Layout math for an unknown-width target

`SmartLayoutEngine.render(PrintDocument document, PrinterProfile profile)` is the file responsible for this. Its own doc comment states the goal directly:

> "That's what makes it 'smart': the exact same document renders correctly at 384 dots or 576 dots or any future custom width, because every column width, wrap point, and divider length is computed from the target width, never hardcoded."

The technique, concretely:

**1. Everything derives from one starting number, the target dot width**, taken from the printer profile, with a fixed margin subtracted once to get a `contentWidth` that every subsequent block lays out against:

```dart
static const double _marginDots = 16;
final width = profile.paperWidth.printableDots.toDouble();
final contentWidth = width - _marginDots * 2;
```

**2. Column widths are proportional flex weights, not pixel counts**, summed to exactly fill `contentWidth` so table borders land flush with the page margins at any width:

```dart
/// Column widths for the items table, left-to-right on paper: total | price
/// | qty | name | #. Proportional to [contentWidth] (summing to it exactly,
/// so the table's grid lines land flush with the receipt margins) - nothing
/// here is a fixed pixel count.
factory _TableColumns.forWidth(double contentWidth) {
  const idxWidth = 34.0;                          // the one fixed-width column
  final remaining = contentWidth - idxWidth;
  const flexes = [1.7, 1.5, 1.2, 4.6];             // total, price, qty, name
  final flexSum = flexes.reduce((a, b) => a + b);
  final flexWidths = flexes.map((f) => remaining * f / flexSum).toList();
  return _TableColumns([...flexWidths, idxWidth]);
}
```

Even the one genuinely fixed-width column (the row-number index, which only ever needs to fit 1-2 digits) is commented with its own reasoning for staying fixed rather than flexed, and for its specific value tracking font-size changes elsewhere: "a fixed dot count fits fewer characters as the font grows, so this widens by roughly the same ~1.3x the bold/regular styles just did." This is the right level of rigor for a "magic number" in a layout function: it isn't actually magic, the comment derives it from a concrete, checkable relationship (font size ratio) rather than "looked right."

**3. Page/paper height is never set upfront — it's computed as the sum of every block's own measured height**, since a receipt (unlike an A4 page) has no fixed height at all:

```dart
final contentHeight = blocks.fold<double>(0, (sum, b) => sum + b.height);
final canvasHeight = contentHeight + _marginDots * 2;
```

Every logical section of the receipt (company header, meta lines, from/to, item table, totals, bank block, footer, copyright) is turned into a list of `_Block { height, paint(canvas, marginLeft, y, width) }` values *before* any canvas exists — layout (measuring) and painting are two separate passes, and the canvas is only created once total height is known:

```dart
final recorder = ui.PictureRecorder();
final canvas = ui.Canvas(recorder);
canvas.drawRect(ui.Rect.fromLTWH(0, 0, width, canvasHeight), ui.Paint()..color = const ui.Color(0xFFFFFFFF));
var y = _marginDots;
for (final block in blocks) {
  block.paint(canvas, _marginDots, y, contentWidth);
  y += block.height;
}
```

This measure-then-paint split is the general technique for "layout must fit an a-priori-unknown total extent": never guess a canvas/page size and clip or overflow if wrong — measure every piece first (`TextPainter.layout(maxWidth: ...)` gives an authoritative `.height` per line/paragraph, accounting for wrapping), sum those measurements to get the real required extent, *then* allocate the drawing surface at exactly that size.

**4. Individual text blocks wrap against `contentWidth`**, so long product names or long lines never overflow the paper width — `TextPainter(...).layout(maxWidth: maxWidth)` is called per line, and the returned `.height` (which already reflects however many lines the text wrapped to) feeds directly into the block-height sum in step 3. No block anywhere hardcodes an expected number of lines.

**5. Everything scales relative to `contentWidth`, not just columns** — even the company logo's max size is computed as a fraction of content width clamped by an absolute pixel ceiling: `final scale = (contentWidth * 0.5 / logo.width).clamp(0.0, maxLogoHeight / logo.height);`. Nothing in the file references `profile.paperWidth.printableDots` a second time after the initial `contentWidth` computation — every downstream measurement flows from that one value, which is exactly what makes the "same document, any width" claim in the file's own doc comment true rather than aspirational.

---

## 7. Testing strategy

There is **no automated visual/bitmap-inspection test** for either print pipeline in this project — no test renders a `PrintDocument` or an invoice PDF and inspects pixel output. The `test/` directory has 37 test files covering business logic (sync, precision, imports, purchase-request lifecycle, etc.) but none targeting `lib/core/printing/`, `lib/core/pdf/`, or the PDF/receipt builders.

What exists instead is a **manual, on-device verification discipline with structured diagnostics**, which is worth describing as the fallback pattern when true golden-image testing isn't set up:

- **A dedicated test-print path exercises the full real pipeline with real data.** `ThermalPrintingService.printTestReceipt()` and the printer-setup UI's "طباعة تجريبية" (test print) button build a `PrintDocument.testReceipt(...)` using the account's *real* business name and real support contacts — not placeholder text — specifically so a successful print confirms Arabic shaping, layout, the driver, and the transport all at once, per its own comment: "a successful print confirms Arabic content and layout, not just that bytes reached the printer."

- **A diagnostics snapshot is captured after every print attempt** (`ThermalPrintDiagnostics`), success or failure, and surfaced in a copyable dialog immediately after the test print:

```dart
class ThermalPrintDiagnostics {
  final String transport, language, paperWidthLabel;
  final int width, height, byteCount;
  final String? address, error;
  @override
  String toString() => 'THERMAL_PRINT:\ntransport=$transport\nlanguage=$language\n'
      'paperWidth=$paperWidthLabel\nbitmap=${width}x$height\nbytes=$byteCount'
      '${address != null ? '\naddress=$address' : ''}${error != null ? '\nerror=$error' : ''}';
}
```

The comment explaining why: "this app has no way to read the device's own logcat remotely, so this is the practical channel for relaying real numbers back." This is a pragmatic substitute for a debugger/log stream when the failure surface is real Bluetooth hardware the developer cannot always physically access.

- **A synthetic, deterministic calibration pattern preceded (and, per the git history, was later merged into) the real-data test print**, specifically to isolate which pipeline stage a problem was in: an earlier iteration added "a deterministic TEST 1 pattern (border/center-line/rectangle/LEFT-CENTER-RIGHT labels, built directly with the `image` package - no PDF, no rasterization step) ... isolating whether a problem is in ESC/POS encoding/transport versus the PDF pipeline, per the staged TEST1/TEST2/real-invoice methodology." This is a genuinely reusable idea even outside this project: when debugging a hardware output path with several independent stages, build a synthetic fixture that skips as many upstream stages as possible (no font shaping, no dynamic content, no PDF) so a failure with the synthetic fixture localizes to the encode/transport stages specifically, separate from a failure that only appears with the real content pipeline.

**What a reader replicating this pattern should add that this project didn't**: the layout math in §6 (`SmartLayoutEngine`, `_TableColumns.forWidth`) is pure and deterministic — it takes a `PrintDocument` and a `PrinterProfile` and returns an `img.Image`, with no I/O, no printer, no Bluetooth. That purity is exactly what makes it unit-testable without hardware: a `flutter_test` widget test (or a plain Dart test with `TestWidgetsFlutterBinding.ensureInitialized()`, the same binding-initialization pattern already used in this project's `customer_statement_test.dart` for non-print but similarly platform-channel-dependent code) could call `SmartLayoutEngine.render(...)` directly and assert on the returned bitmap's `width`/`height`, or decode specific pixels to confirm e.g. "the divider line is drawn," without any physical printer. This project's `bd340de` commit's own synthetic-pattern approach is close to this in spirit but was run manually, not wired into `flutter test`. Doing so would have been strictly better and cost little, given the render function was already side-effect-free by design — this is the one clear gap in an otherwise well-reasoned testing story.

---

## 8. Gotchas and lessons learned

These are the most instructive verbatim comments and commit messages found in this codebase — each documents a real bug, its root cause, and why the fix looks the way it does. They are presented roughly in the chronological order they were discovered (per git history), because later fixes build on lessons from earlier ones.

**1. Alpha channel silently poisoning a "transparent" background into solid black** (commit `a6cd782`):

> "Root-caused by reading esc_pos_utils_plus's own source: `Generator._toRasterFormat()` converts straight to grayscale then inverts, and never reads the alpha channel at all. A PDF page has no background of its own, so `Printing.raster()` returns unpainted regions as RGBA with alpha 0 and RGB left at the renderer's default clear value - commonly (0,0,0). Since the ESC/POS converter ignores alpha, that (0,0,0) 'transparent' background read identically to genuine black text/ink."

General lesson: when a rasterizer/encoder silently drops a channel your source format relies on for meaning (transparency), don't fix it downstream — flatten explicitly at the source, before the data ever reaches the channel-blind consumer.

**2. The "fix" for #1 broke real content, because it assumed alpha meant the opposite of what it did** (commit `16c5aeb`):

> "`_flattenOntoWhite()` alpha-blended every raster pixel toward white using `Printing.raster()`'s alpha byte, on the assumption it reads 255 for real content and 0 only for background. It doesn't hold for this renderer, so genuine black text washed toward white right along with the background." Fixed by "a plain RGB copy that drops alpha entirely."

General lesson: a fix based on an assumption about a third-party renderer's undocumented alpha behavior is itself a guess — verify the assumption (or route around needing it at all) rather than building a second layer of logic on top of it. This exact tension (two sequential bugs from trusting the same unverified alpha assumption) is what ultimately motivated abandoning the PDF-rasterize approach entirely (§1, commit `593d6ae`).

**3. `SmartLayoutEngine`'s own doc comment names the specific ESC/POS constraint that dictates the "everything renders bold" typography rule** (§5):

> "Every text role renders bold: after ESC/POS's 1-bit threshold (a pixel prints only past a fixed brightness cutoff - see esc_pos_utils_plus's `Generator._packBitsIntoBytes`), a regular-weight stroke loses enough edge pixels to look thin/light on thermal paper even though it looked fine on screen. Bold strokes survive that threshold with a solid, dark result - confirmed against real on-device output."

General lesson: a 1-bit (dithered/thresholded) output device is not just "the same image but lower-fidelity" — thin strokes can vanish below the threshold entirely. A design decision made purely for that hardware constraint (bold-everywhere) would be wrong advice for a full-color/greyscale target; don't port typography rules across output-device classes without re-deriving them for the new device's actual constraints.

**4. Third-party Bluetooth plugin picked the wrong RFCOMM socket security mode for this hardware family** (commit `31ff8ec`):

> "`print_bluetooth_thermal` opens a SECURE RFCOMM socket ... which this printer - a generic ESC/POS clone - and evidently many others in its family never complete Android's secure-pairing handshake over, even though the identical paired device connects and prints fine through a third-party test app. Confirmed by inspecting that app's own working config."

**5. The same plugin also had a native state bug that permanently wedged reconnection** (commit `dea628d`):

> "its 'already connected' branch has `outputStream == null` (a stray comparison, not the assignment it clearly meant to be), so a reference left over from an interrupted attempt never clears itself and every later `connect()` silently fails without trying."

General lesson (4+5 together): when a well-known plugin fails against specific real hardware in a way that looks environmental, reading its actual native source is sometimes the fastest path to a real root cause — "stray comparison instead of assignment" is an ordinary typo bug, not a hardware incompatibility, and no amount of retrying or permission-fiddling on the Flutter side would ever have found it.

**6. A raster width that doesn't match the physical printer's own configured width breaks silently, not loudly** (commit `bd340de`):

> "the ESC/POS 'center' command sent before the raster image tells the printer's OWN firmware to center within whatever paper width IT thinks it has configured, which is completely independent of our bitmap's actual width - so a mismatched raster width ... produces unpredictable alignment that has nothing to do with the bitmap's own content."

General lesson: a physical device can hold its own independent, invisible configuration state (the firmware's own idea of paper width) that your software has no way to query — matching it has to be a verified, hardcoded/preset choice (§4's `PaperWidthPreset.verified`), not something inferred from your own bitmap or assumed correct because it "looks like it should work."

**7. `TextAlign.right` positions relative to the box, not the physical edge you want** (commit `c5c3024`, quoted in full in §3) — trusting an alignment enum's placement inside an oversized box is a recurring, generalizable trap, not specific to Arabic or to this printer.

**8. A successful `send()` does not mean the printer has finished printing** (`thermal_printing_service.dart`, matching a setting name from a known-working reference app):

> "A successful write only means the bytes reached the transport's send buffer, not that the printer's print head has actually caught up yet - disconnecting immediately can cut a job off mid-print. Matches the reference ESC/POS app's own 'Printer Disconnect Delay: 4 Seconds' default."

```dart
static Future<void> _disconnectAfterDelay(PrinterTransport transport) async {
  await Future.delayed(const Duration(seconds: 4));
  await transport.disconnect();
}
```

called via `unawaited(...)` specifically so "the caller doesn't wait an extra 4s for something only the printer needs" — the delay is real and necessary, but it must not be on the critical path the user is waiting on.

**9. Forgetting to divide a raster back down by its own supersampling scale silently breaks placement, not just quality** (`returned_stamp_pdf.dart`, quoted in full in §2) — draw-at-N-pixels/place-at-N-points is a pattern that fails invisibly in exactly the environment where you're least likely to catch it (on-screen preview, where "logical pixels" and "points" happen to coincide) and only shows up in the actually-printed/shared output.

**10. A missing/broken remote image (logo, product photo) must never abort the whole document** — this defensive pattern repeats verbatim across every place a network image is fetched for printing, in both pipelines:

```dart
} catch (_) {
  // A missing/broken logo must not prevent printing the receipt.
  return null;
}
```

General lesson: for a document-generation pipeline, any single optional decorative asset (as opposed to core required content) should degrade gracefully to "omit it" rather than propagate an exception that blocks the entire print/share action — and this should be stated as an explicit comment at each such call site, not left to be inferred from a bare `catch`.

**11. A filename derived from user-facing content can silently break the OS share sheet** (`share_invoice.dart`):

```dart
// Invoice numbers look like "INV/2026/0012" - the slashes are fine for
// display, but read as path separators when used as a filename, silently
// breaking share/print (share_plus has to write the bytes to a temp file
// under that name first, and the intermediate "folders" don't exist).
String _sanitizeForFilename(String invoiceNumber) => invoiceNumber.replaceAll('/', '-');
```

General lesson: any user-facing identifier that gets reused as a filesystem path component (temp file name, export name) needs its own explicit sanitization step — the display format and the filesystem-safe format are different contracts, and a value that's valid in one is not automatically valid in the other.
