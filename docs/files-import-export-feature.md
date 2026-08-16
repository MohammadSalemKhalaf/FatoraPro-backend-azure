# Pattern: Hand-Rolled CSV/XLSX Import, Export, Preview, and Download (Flutter, fully client-side)

## Where this pattern lives in the source project

This document describes a pattern extracted from a Flutter invoicing app ("this project's equivalent" placeholders are used below for the reader's own domain). The concrete files it was extracted from:

- `lib/features/settings/data/csv_utils.dart` — CSV read/write primitives, the Excel-text-forcing convention, BOM handling.
- `lib/features/settings/data/xlsx_utils.dart` — hand-rolled `.xlsx` reader (no spreadsheet package).
- `lib/features/settings/data/data_export_service.dart` — builds the export file content (a `String` of CSV text) from repositories.
- `lib/features/settings/data/data_import_service.dart` — column-name matching, section-boundary detection, per-row validation, actual writes.
- `lib/features/settings/presentation/import_data_page.dart` and the `ExportDataPage` class embedded in `lib/features/settings/presentation/settings_page.dart` — the two mirror-image UI screens.
- `lib/core/widgets/file_preview_page.dart` — the shared "preview, then download or share" screen used by every generated-file feature in the app (CSV exports, PDF invoices, PDF customer statements).
- `lib/core/utils/phone_validator.dart`, `lib/core/utils/decimal_input_formatter.dart` (specifically `plainDecimalText`) — supporting validators/formatters referenced by the import/export services.

Packages involved (from `pubspec.yaml`): `file_selector` (pick a file from disk), `share_plus` (OS share sheet), `file_saver` (direct "save to" file picker), `open_filex` (open a file in whatever app the OS associates with its type), `printing` (in-app PDF preview widget), `archive` + `xml` (raw `.xlsx` zip/XML parsing — no dedicated spreadsheet package).

This whole feature is **100% client-side**. There is no backend endpoint involved: export reads local repositories (which wrap a local SQLite store that syncs with a backend elsewhere in the app, but the export/import code itself never talks to the network). The backend was checked and has no `ImportController`/`ExportController`/CSV-handling code — confirming this is a pattern for *local file generation and parsing*, not a file-upload API. If your own project's import/export genuinely round-trips through a server endpoint, this pattern still applies to the client-side file-building/parsing half; you'd add a plain multipart upload/download on top of it.

---

## 1. Export: building a spreadsheet-compatible file from scratch without a spreadsheet library

### Why hand-roll CSV instead of using a package

The reasoning documented in `csv_utils.dart`'s file header is the crux of the whole pattern: the writer and the reader **must agree on one exact quoting contract**, because this app both produces the file and later has to consume it back. Pulling in an external CSV package for one side (or both) risks a mismatch the moment the package's defaults differ from what you assumed — e.g. quotes only non-simple fields, uses `\n` instead of `\r\n`, or escapes differently. Writing ~15 lines yourself, in one shared file both directions import, makes the contract impossible to drift.

The contract, verbatim:

```dart
// this project's equivalent of csv_utils.dart
String escapeCsvField(Object? value) =>
    '"${(value ?? '').toString().replaceAll('"', '""')}"';

String csvRow(List<Object?> fields) =>
    '${fields.map(escapeCsvField).join(',')}\r\n';
```

Rules, and why each one:
- **Every field is quoted, unconditionally** — not just fields containing a comma or quote. This removes an entire class of "did I need to quote this?" decisions and edge cases (a field that starts empty, a field that's purely numeric today but might contain a comma tomorrow). It costs a few bytes; it buys total simplicity.
- **A literal `"` inside a field is escaped as `""`** — the RFC 4180 convention, and the same one Excel itself uses both writing and reading CSV. Match the tool your users will actually open the file in, not just a spec.
- **Row terminator is `\r\n`**, not `\n` — again, matches what Excel writes, so a file that's exported, opened, saved, and re-imported doesn't silently pick up mixed line endings.
- **`null` becomes `''`** at the field-escaping level (`value ?? ''`), so every call site can pass a nullable field directly without an `?? ''` at each use.

The reader is a **hand-rolled RFC 4180 state machine** (`parseCsv`, ~60 lines), not a package, for the same reason as the writer: the format it has to round-trip is entirely the app's own convention (or Excel's save-as-CSV, which follows the identical convention), so there's no benefit to a general-purpose parser and a real cost — one more transitive dependency, one more version-pinning conflict to manage. The parser is a simple character-by-character loop tracking an `inQuotes` boolean, handling `""` inside quotes as an escaped quote, and treating `\r` as a no-op / `\n` as the row terminator so it tolerates either line-ending style on input even though it always writes `\r\n`.

### The template and the validator share one source of truth

This is the detail most naive implementations get wrong: a "download blank template" button is easy to implement as a hardcoded string of column headers, entirely separate from wherever the import validator's column list lives. The moment someone adds a column to one and forgets the other, the downloadable template silently stops matching what the importer accepts.

This project instead defines the header list **once**, as private constants on the import service itself, and derives both the template and the validator's column lookups from it:

```dart
// this project's equivalent of data_import_service.dart
static const _nameHeader = 'الاسم';
static const _customerOnlyHeaders = [
  'اسم المتجر',
  'رقم الهاتف',
  'الشارع',
  'المدينة',
];
static const _productOnlyHeaders = [
  'الوصف',
  'سعر الشراء',
  'سعر البيع',
  'الكمية',
];

static const customersTemplateHeader = [_nameHeader, ..._customerOnlyHeaders];
static const productsTemplateHeader = [_nameHeader, ..._productOnlyHeaders];

String buildCustomersTemplate() => csvRow(customersTemplateHeader);
String buildProductsTemplate() => csvRow(productsTemplateHeader);
```

`validateCustomerRows`/`validateProductRows` (section 4 below) look up columns by these exact same constant strings (`_nameHeader`, `_customerOnlyHeaders[0]`, etc.) — so the template and the validator physically cannot drift apart; changing a column name means editing one constant, and both the downloadable template and the parser's expectations update together. A comment on the header list warns future maintainers about ordering fragility: `_productOnlyHeaders` is documented as "appended, not inserted" specifically because some call sites index into it positionally (`_productOnlyHeaders[0]`/`[1]`/`[2]`) even though the *file's* columns are matched by name — inserting a header in the middle of that list would silently point those fixed indices at the wrong header text.

**Takeaway for a new project**: define your column-header list as one constant collection. Generate the downloadable template from it directly (`csvRow(header)`). Have your row-validator's column lookups reference the exact same constant list, never a re-typed copy.

### Downloadable template delivery

The template itself is generated in-memory and handed to the OS share sheet rather than saved to a fixed path — see section 8 for why. One detail worth calling out here: the template bytes get a **leading UTF-8 BOM** prepended before sharing:

```dart
// this project's equivalent of import_data_page.dart
final bytes = utf8.encode('﻿$csv');
```

This is required because Excel's CSV importer, absent a BOM, guesses the file's encoding using the legacy system codepage rather than assuming UTF-8 — for a file with non-ASCII headers (Arabic in this app's case; anything outside plain ASCII in general — accented Latin, CJK, Cyrillic) that guess is wrong and the headers render as mojibake. The BOM removes the ambiguity. Every export/template/statement path in this app applies the same prefix.

---

## 2. The Excel numeric-mangling problem and its fix

### The exact problem

Excel auto-detects a cell's type from its *displayed text* whenever it opens a CSV — it does not trust the file to tell it "this is text." Two concrete failure modes documented in the source:

1. **Leading zero dropped.** A phone number like `0599067888` or a zero-padded ID becomes `599067888` — Excel decides it's a number and a number has no leading zero.
2. **Scientific notation for long digit strings.** A long numeric-looking string (a barcode, a long invoice number) gets rendered as e.g. `5.99067888E+08` — Excel decided it's a number too large to show in full and switched to scientific notation, permanently losing the original digit sequence for anyone who doesn't know to reformat the cell.

Critically, the code comment is explicit that **CSV quoting alone does not prevent this**. Wrapping the field in `"..."` (the normal CSV escaping from section 1) only protects commas/quotes from being misread as delimiters — it says nothing to Excel about the cell's intended *type*, and Excel still reinterprets a quoted numeric-looking string as a number once parsed.

### The exact fix: Excel's own text-forcing formula convention

The fix is to wrap the value in Excel's own `="..."` text-literal formula syntax, which forces Excel to treat the cell as literal text regardless of what it looks like:

```dart
// this project's equivalent of csv_utils.dart — export direction
String forceExcelText(String? value) {
  if (value == null || value.isEmpty) return '';
  return '="$value"';
}
```

Applied at every call site in the export service where a numeric-looking-but-not-actually-numeric field is written — phone numbers, invoice numbers:

```dart
// this project's equivalent of data_export_service.dart
buffer.write(_csvRow([
  forceExcelText(order.invoiceNumber),
  order.customerName,
  forceExcelText(order.customerPhoneNumber),
  formatDateForExport(order.createdAt),
  ...
]));
```

The reverse direction — without which **this app couldn't re-import a file it had itself exported** — strips that wrapper back off before validation ever sees the value:

```dart
// this project's equivalent of csv_utils.dart — import direction
String unwrapExcelText(String value) {
  final match = RegExp(r'^="(.*)"$').firstMatch(value);
  if (match != null) return match.group(1)!;
  return value.startsWith("'") ? value.substring(1) : value;
}
```

(The second branch of that function is the *other* text-marking convention — see section 3.) Every field read during import passes through this unwrap step before anything else touches it:

```dart
// this project's equivalent of data_import_service.dart
String _fieldOrEmpty(List<String> fields, int? index) {
  if (index == null || index >= fields.length) return '';
  return unwrapExcelText(fields[index].trim());
}
```

**Generalizable rule**: any column whose values are digit-strings that must never be treated as arithmetic numbers — phone numbers, zero-padded IDs, barcodes, invoice/order numbers, ZIP/postal codes — needs this treatment on export, and the importer must strip it back off before validating, or the app becomes unable to re-import its own output.

---

## 3. Supporting the OTHER "keep this as text" convention

The `="..."` formula wrapper is *this app's own* export convention. But an import file is not guaranteed to have been produced by this app — a real-world import file is commonly hand-assembled or edited directly in Excel by the end user, and Excel has its own, older, completely different native mechanism for marking a cell as text: a **literal leading apostrophe** (`'0599067888`), applied either by typing it directly into a cell or via *Format Cells → Text*. Excel stores the apostrophe as part of the cell's raw text but hides it from display — so a `.csv` **save-as** from such a sheet writes the apostrophe out as a literal leading character in the field.

Why both conventions matter simultaneously: if the importer only recognized its own `="..."` wrapper, a phone number a user typed by hand in Excel with a leading `'` to preserve its zero would arrive at the importer still carrying that literal apostrophe character. That breaks the numeric-shaped downstream validator (a phone-number regex expects the first character to be a digit) — silently discarding, or erroring on, a value the user entered *correctly by their own spreadsheet's own convention*. This was flagged explicitly as a real bug fixed in this project ("Fix phone number silently becoming empty on import").

The single `unwrapExcelText` function (section 2) handles both conventions in one pass, in priority order — check for the formula wrapper first, then fall back to a bare leading apostrophe, and otherwise pass the value through completely unchanged (a file from neither this app nor manually-typed Excel text-formatting, e.g. a plain export from some other system, has neither marker and must not be touched):

```dart
String unwrapExcelText(String value) {
  final match = RegExp(r'^="(.*)"$').firstMatch(value);
  if (match != null) return match.group(1)!;
  return value.startsWith("'") ? value.substring(1) : value;
}
```

**Generalizable rule**: an export-side "force as text" convention only has to satisfy your own round-trip. An import-side "recognize as text" step has to satisfy *every* convention a real spreadsheet tool in the wild might have produced, because you don't control how the user built the file you're receiving. Write the export side once; write the import-side unwrap to recognize every convention you might plausibly receive, not just your own.

---

## 4. Smart header/column detection on import

Two independent problems, both real-world failure modes for hand-edited spreadsheets, solved by two separate techniques.

### Problem A: the header row isn't necessarily row 0

A file may have a title/preamble row above the real header (this app's own combined multi-section export is exactly such a file — see section 5). A naive importer that assumes `rows[0]` is the header breaks the instant there's anything above it.

### Problem B: columns must be matched by name, not position

A file with reordered columns, or extra columns the template doesn't define, must still import correctly — reading "whatever's in column index 2" is brittle against any manual editing at all. The fix is a tiny header-to-index lookup map, built once per import:

```dart
// this project's equivalent of data_import_service.dart
class _ColumnMap {
  _ColumnMap(List<String> headerRow) {
    for (var i = 0; i < headerRow.length; i++) {
      final header = headerRow[i].trim();
      // First occurrence wins on a duplicate header - arbitrary but
      // deterministic, and a duplicated column name is a malformed file
      // either way.
      _indexByHeader.putIfAbsent(header, () => i);
    }
  }
  final _indexByHeader = <String, int>{};
  int? indexOf(String header) => _indexByHeader[header];
}
```

Every field read downstream calls `columns.indexOf('Some Header')` and then reads `fields[index]` — never a hardcoded position. A column this template doesn't recognize simply has no lookup performed against it and is ignored; a missing column returns `null` from `indexOf`, which callers treat as "this field wasn't provided" (empty/optional) or, for a *required* column, as grounds to fail validation entirely up front (see section 7).

### Finding the real header row

The actual algorithm, and — importantly — *why* a naive "search for the row containing the shared name-column header" is insufficient once you support a combined multi-entity file (section 5): if two entity types share one header column (e.g. both a Customers section and a Products section have a "Name" column), searching only for that shared header would lock onto whichever section's header appears *first* in the file — wrong entity, wrong row, for whichever type you're actually trying to import.

```dart
// this project's equivalent of data_import_service.dart
int _findHeaderRowIndex(List<List<String>> rows, List<String> ownHeaders) {
  for (var i = 0; i < rows.length; i++) {
    final cells = rows[i].map((cell) => cell.trim()).toSet();
    if (!cells.contains(_nameHeader)) continue;
    if (ownHeaders.any(cells.contains)) return i;
  }
  return 0;
}
```

The candidate row must contain **both** the shared column name **and** at least one column unique to the entity being validated (`ownHeaders` — e.g. `_productOnlyHeaders` when validating products). A different entity's header, which shares the name column but has none of this entity's own columns, is correctly skipped in favor of the real header further down. The scan covers the *whole* file (not "just the first few rows"), because a large earlier section can push this entity's header arbitrarily far down. If nothing in the file qualifies at all, it falls back to row 0 — preserving the old fixed-position assumption as a last resort, so a genuinely malformed file still produces the existing clear "this doesn't look like the right template" error rather than behaving unpredictably.

**Generalizable rule for a reader designing this from scratch**: (1) never assume the header is the first row — scan for it; (2) never assume a "looks like the header" match is unambiguous when multiple record types can share a column name — require at least one column unique to the type you're actually looking for; (3) match every subsequent field access by the header's text through a name→index map, never a fixed position.

---

## 5. Handling a combined multi-entity file safely

### Why this matters

The export service can write more than one entity's section into a single file — e.g. invoices, then customer debts, then clients, then items, each with its own title row, its own header row, and its own data rows, separated by a blank line. A user who downloads that combined export and later tries to re-import just the "customers" portion of it is handing the importer a file where a *different* entity's header and data rows appear later in the same sequence. Left unguarded, that importer would read straight past the end of the customers data into the items section's title row, header row, and data rows — misreading all of it as more customer rows, including the bare section-title cell itself importing as a garbage phantom row.

### Section-boundary detection

Three independent signals, checked every row, any one of which ends the current entity's data:

```dart
// this project's equivalent of data_import_service.dart — _dataRows
Iterable<(int, List<String>)> _dataRows(
  List<List<String>> rows,
  int headerRowIndex,
  List<String> otherHeaders,
) sync* {
  for (var i = headerRowIndex + 1; i < rows.length; i++) {
    final fields = rows[i];
    final trimmedNonEmpty = fields.map((f) => f.trim()).where((f) => f.isNotEmpty).toList();

    // 1. A genuinely blank row.
    if (trimmedNonEmpty.isEmpty) return;

    // 2. A bare section-title row (e.g. "Customers", written on its own
    //    with every other column empty) — this app's own section
    //    separator, which may not survive a re-save through Excel as a
    //    truly blank <row> element, so it can't be relied on alone.
    if (trimmedNonEmpty.length == 1 && _sectionTitles.contains(trimmedNonEmpty.single)) {
      return;
    }

    // 3. A DIFFERENT entity's own header row appearing here.
    final cells = fields.map((c) => c.trim()).toSet();
    if (cells.contains(_nameHeader) && otherHeaders.any(cells.contains)) {
      return;
    }

    yield (i + 1, fields);
  }
}
```

Note it *stops* scanning (returns) rather than merely skipping the offending row — once any boundary signal fires, everything after it belongs to a different section and must not be considered at all for this entity.

### The distinct, narrower problem: stray rows of the OTHER entity interleaved under ONE shared header

Section boundaries handle a file with clearly separated blocks. A different scenario — the one this project specifically had a bug filed against ("Fix Smart Import failing on a real cross-account export") — is a file where customer rows and product rows are interleaved *under a single combined header* with no boundary at all (e.g. a raw export from some other system that just concatenated two tables under one wide header row). Here, `_dataRows`'s boundary detection never fires (there's no blank row, no repeated header), so every row reaches per-row validation — and a product row, read as a customer row, would try to read its price field as a phone/street value and either import garbage or fail with a confusing price-shaped error on what is actually a fine customer row.

The fix is a narrow, deliberately conservative heuristic checked per-row, *within* the data loop:

```dart
// this project's equivalent of data_import_service.dart
bool _rowBelongsToOtherEntity(
  List<String> fields, {
  required List<int?> ownColumns,
  required List<int?> otherColumns,
}) {
  final hasOwnData = ownColumns.any((c) => _fieldOrEmpty(fields, c).isNotEmpty);
  if (hasOwnData) return false;
  return otherColumns.any((c) => _fieldOrEmpty(fields, c).isNotEmpty);
}
```

A row is recognized as belonging to the other entity, and silently skipped rather than reported as an error, **only** when it has nothing at all in any of this entity's own fields, but does have data in a field that only the *other* entity's template defines. This is deliberately narrow: a row that's simply missing its own required field (a real product row where the price was forgotten, with nothing filled in from the customer-only columns either) has no positive evidence pointing at the other entity, so it is **not** silently dropped — it falls through to normal validation and is reported as a row-level error. The code comment is explicit about the tradeoff: *"Silently discarding a genuine mistake would be worse than the confusing error it replaces."*

The project's own regression tests for this fix assert both directions of the same file: importing the products-and-customers-mixed file as "customers" imports only the customer row (product row silently skipped); importing it as "products" imports only the product row (customer row silently skipped); and a genuinely broken product row (real product data, invalid price) is still reported as an error in both runs — proving the skip logic isn't swallowing real mistakes along with the cross-entity noise.

**Generalizable rule**: distinguish "this row belongs to a different section entirely" (boundary detection — stop scanning) from "this row belongs to a different *type*, interleaved with no boundary" (per-row heuristic — skip just this row, provably narrow so it never masks a real validation failure).

---

## 6. Multi-sheet spreadsheet files

Because `.xlsx` is a genuine multi-tab workbook format (unlike CSV), a hand-built cross-account or cross-system import file is just as likely to put two entity types on two separate *worksheets* as it is to stack them in one sheet as two sections. A naive `.xlsx` reader implementation — "open the archive, find `sheet1.xml`, parse it, done" — silently drops every worksheet after the first. The specific, easy-to-make bug this produces: importing such a file surfaces as "no matching template columns found" for whichever entity type happened not to be on the first tab, with no indication that the *file itself* had the data, just on a sheet the importer never looked at.

The fix reads every worksheet found in the archive, not just the first, discovering them by filename pattern rather than assuming a fixed count:

```dart
// this project's equivalent of xlsx_utils.dart
final sheetNumbers = archive.files
    .map((file) => file.name)
    .where((name) => RegExp(r'^xl/worksheets/sheet\d+\.xml$').hasMatch(name))
    .map((name) => int.parse(RegExp(r'\d+').firstMatch(name)!.group(0)!))
    .toSet()
    .toList()
  ..sort();

final rows = <List<String>>[];
for (final sheetNumber in sheetNumbers) {
  final sheet = archive.findFile('xl/worksheets/sheet$sheetNumber.xml');
  if (sheet == null) continue;
  // A blank row is the same section-boundary marker the single-sheet CSV
  // export already writes between sections — see section 5's _dataRows,
  // which stops scanning a section the moment it hits one — so a second
  // sheet's title/header/data never gets misread as more of the first
  // sheet's rows.
  if (rows.isNotEmpty) rows.add(const []);
  rows.addAll(_parseSheetRows(sheet, sharedStrings));
}
return rows;
```

Every worksheet's parsed rows are concatenated into the **exact same flat row/field grid shape `parseCsv` already produces**, with a blank-row separator inserted between sheets — deliberately reusing the same section-boundary mechanism from section 5 rather than inventing a second, sheet-aware code path through validation. This is the single most valuable design choice in this whole feature: **the validator has no notion of file format or worksheet structure at all** — it only ever sees `List<List<String>>`, so a CSV file, a single-sheet `.xlsx`, and a multi-sheet `.xlsx` are all validated by identical code.

Sheets are read in numeric filename order (`sheet1.xml`, `sheet2.xml`, ...) rather than the workbook's visual tab order — the code comment notes visual tab order isn't guaranteed to match the underlying file numbering anyway, and since downstream section detection already has to handle multiple sections in one sequence regardless of order, sheet order turned out not to matter for correctness.

Two more OOXML-specific correctness details worth carrying into any hand-rolled `.xlsx` reader:
- **Sparse rows.** Excel omits a genuinely empty cell from the XML entirely rather than writing an empty `<c>` element — a row with only columns A and C filled has no `<c>` for B at all. Reading cells "in the order they appear in the XML" without checking each cell's own declared reference (`r="C4"`) would shift C's value left into B's position. The reader instead parses each cell's own column letter from its `r` attribute and pads with empty strings up to that column index before appending the value.
- **Shared strings vs. inline/rich text.** Excel usually stores repeated text values once in a workbook-level `sharedStrings.xml` table and references them by index (`t="s"`, `<v>` holds the index) — but a cell can also hold inline string content directly (`t="inlineStr"`), and any cell where even one character has its own distinct formatting (e.g. partial bolding) gets split into multiple `<r><t>` runs that must be concatenated. Handling only the plain shared-string case silently produces empty cells for anything rich-text-formatted.

---

## 7. Row-level validation with partial success vs. all-or-nothing

Two independent axes, and this project deliberately answers them differently.

### Axis 1: does the whole FILE succeed or fail as one unit?

**All-or-nothing**, by explicit design. `ImportValidationResult` carries both a `validRows` list and an `errors` list, and importing only ever proceeds when `errors` is empty:

```dart
class ImportValidationResult<T> {
  const ImportValidationResult({required this.validRows, required this.errors});
  final List<T> validRows;
  final List<ImportRowError> errors;
  bool get isValid => errors.isEmpty;
}
```

The UI layer enforces this directly — a non-empty error list shows every error and writes nothing:

```dart
// this project's equivalent of import_data_page.dart
final result = _importService.validateCustomerRows(rows);
if (!result.isValid) {
  await _showErrorsDialog(result.errors);
  return; // nothing imported
}
```

The stated reasoning (doc comment on `ImportValidationResult`): *"a file with a mistake on row 40 never silently imports rows 1-39 and drops the rest."* This matters because a bulk import is typically a one-shot action a non-technical user runs once; partial success followed by "here's what went wrong on the rows that failed" would leave the data in a half-imported state that's confusing to reconcile and easy to accidentally duplicate on retry (re-running the "fixed" file would re-import rows 1-39 a second time unless the user manually deletes them first).

### Axis 2: within a single row, does one bad FIELD fail the whole row, or just degrade that field?

This is where the required/optional distinction the task asks about actually lives, and the two are handled with genuinely different code paths:

**A required field failing rejects the entire row** (added to `errors`, excluded from `validRows`, and this failure is what trips axis 1 and blocks the whole file):

```dart
// this project's equivalent of data_import_service.dart — name is required
final name = _fieldOrEmpty(fields, nameColumn);
if (name.isEmpty) {
  errors.add(ImportRowError(row: rowIndex, message: 'الاسم مطلوب')); // "Name is required"
  continue;
}
```

```dart
// sell price is required for a product row
final sellPriceText = _fieldOrEmpty(fields, sellPriceColumn);
if (sellPriceText.isEmpty) {
  errors.add(ImportRowError(row: rowIndex, message: 'سعر البيع مطلوب')); // "Sell price is required"
  continue;
}
if (sellPrice == null || sellPrice < 0) {
  errors.add(ImportRowError(row: rowIndex, message: 'سعر البيع غير صالح')); // "Sell price is invalid"
  continue;
}
```

**An optional field being malformed degrades silently to "no value" for that field alone — the row still imports.** The clearest example is phone number on a customer row:

```dart
// this project's equivalent of data_import_service.dart
String? _validPhoneOrNull(String? raw) =>
    raw != null && isValidPhoneNumber(raw) ? raw : null;
```

The doc comment states the rule explicitly: *"A phone number that doesn't pass validation degrades to no phone at all, exactly like an empty cell - phone is optional on Customer, so a malformed value blocks only itself, never the whole row ... Never rewrites a value that IS valid - it's stored exactly as typed, leading zero and all."* Similarly, `purchasePrice` and `quantity` on a product row default to `0` when the cell is empty rather than failing the row — they're optional, unlike `sellPrice`.

**Generalizable rule**: classify every column as required or optional up front. A required column's validation failure is a *row-level* error (contributes to axis 1's file-wide reject). An optional column's validation failure is a *field-level* degrade-to-null/default (never touches axis 1) — and degrading must never silently rewrite a value that *did* pass validation; the raw user-entered text is stored byte-for-byte when it's valid.

### The other all-or-nothing tier: a template that's missing a required column entirely

One more case handled distinctly from a per-row error: if the file's header is missing a column the entity genuinely requires (no "Sell Price" column anywhere in the header row), validation doesn't produce one error per data row — it stops immediately with a single, clear "this doesn't look like the right template" message:

```dart
final nameColumn = columns.indexOf(_nameHeader);
final sellPriceColumn = columns.indexOf(_productOnlyHeaders[2]);
if (nameColumn == null || sellPriceColumn == null) {
  return ImportValidationResult(
    validRows: const [],
    errors: [ImportRowError(row: 1, message: _missingTemplateMessage('الأصناف'))],
  );
}
```

Without this short-circuit, a structurally wrong file (wrong template entirely, or a required column simply never included) would otherwise produce a wall of N identical "sell price is required" errors, one per data row — technically correct but useless to the user trying to understand what actually went wrong. The UI layer even special-cases rendering this: a single-error result is shown as one plain sentence rather than a numbered "row N:" list, specifically because this whole-template error is always alone and always attributed to row 1 (the header), which reads oddly with a per-row prefix stapled onto an overarching problem.

---

## 8. Preview and download UX

### One shared preview screen for every generated file type

Rather than each feature (CSV export, PDF invoice, PDF customer statement) inventing its own "here's your file" screen, this project has a single `FilePreviewPage` / `showFilePreview(...)` entry point used by all of them:

```dart
// this project's equivalent of file_preview_page.dart
enum FilePreviewKind { pdf, text }

Future<void> showFilePreview({
  required BuildContext context,
  required String title,
  required Uint8List bytes,
  required String filename,
  required String mimeType,
  required String shareSubject,
  FilePreviewKind kind = FilePreviewKind.pdf,
});
```

The `kind` parameter is the only real branch point, because the two file types genuinely need different in-app rendering:
- **`pdf`** gets a real, paginated in-app preview via the `printing` package's `PdfPreview` widget — the user can flip through pages before deciding to save/share.
- **`text`** (CSV) has no meaningful in-app rendering (a raw comma-delimited text dump means little to most users) — so instead of trying to render a table, the file is written to a temp directory and handed straight to whatever app the OS associates with that file type (Excel, Google Sheets, a text editor) via `open_filex`'s `OpenFilex.open(path)`, fired automatically once the screen opens, with a manual "open file" retry button and a friendlier "you don't have an app installed for this" message if `OpenFilex.open` reports `ResultType.noAppToOpen`.

Both kinds share the same bottom action bar — "Download" and "Share" — regardless of what's rendered above it.

### Why both a share sheet AND a direct file save are needed

The two buttons solve genuinely different user intents, and the code comments explain why *neither alone* would have sufficed:

- **Share (`share_plus`)** hands the file to the OS share sheet — AirDrop, Messages, Mail, WhatsApp, "send a copy to another app." This is the right mechanism when the user's actual goal is to get the file to someone or something else, not necessarily to keep a local copy at all.
- **Download (`file_saver`)** is for the user who wants the file to simply exist, findable, on their own device's storage (their Downloads folder / Files app) — no recipient involved. The implementation detail matters here and is explicitly called out in a code comment: the package exposes both a `saveFile` and a `saveAs` method, and only `saveAs` is correct —

  > *"`saveAs`, not `saveFile`: `saveFile` silently writes into this app's own private external-files directory (invisible in Downloads/the Files app — the actual cause of the notice firing with nothing to show for it). `saveAs` opens a real SAF 'save to' picker and writes through the platform's own content-resolver stream, landing wherever the user actually picks — the only path that reaches a location the user can really find the file in afterward."*

  This is a real, previously-shipped bug class worth calling out on its own: a "save" button that reports success but writes to a location the OS-level file picker can't see is *worse* than no save button, because the user has no way to discover their mistake — they just conclude the app is broken. `saveAs`'s return value is also `null` specifically when the user cancels the platform picker, which the code treats as a normal, silent no-op (no error toast) rather than a failure.

```dart
// this project's equivalent of file_preview_page.dart
final savedPath = await FileSaver.instance.saveAs(
  name: _baseName,
  bytes: widget.bytes,
  fileExtension: _extension,
  mimeType: _fileSaverMimeType,
);
if (savedPath != null && mounted) {
  showAppNotice(context, 'File saved successfully');
}
```

### The old pattern this replaced, and why

A prior version of this feature jumped straight to the OS share sheet with no preview step at all — the commit that introduced the current `FilePreviewPage` describes this explicitly as the problem being fixed: *"instead of the old pattern of jumping straight to the OS share sheet with no chance to see the file first or save it locally without going through share."* Two distinct gaps in the old flow: (1) no chance to *see* the file before committing to an action on it, and (2) no way to save a local copy without routing through a share target (which on some platforms means picking some other app just to bounce the file back into local storage). The unified preview screen fixes both by making "look at it," "keep a local copy," and "hand it to someone else" three clearly separate, always-available actions.

---

## 9. What NOT to silently "clean" during import

The design principle, stated directly in code comments in multiple places, is: **preserve exactly what the user entered whenever it's valid — even if it looks unusual — and only ever normalize a value transiently, for the purposes of *validating* it, never for what actually gets stored.**

Concrete instances of this principle in the code:

- **Leading zeros in phone numbers are never stripped.** The doc comment on the phone validator's normalization helper is explicit that country-code stripping exists *only* to decide whether a raw value is shaped like a real number, never to rewrite what's stored:

  > *"Never used to rewrite what's actually stored — see `validatePhoneNumber`'s doc — only to decide whether the raw value is shaped like a real number."*

  And on the field-level validator itself:

  > *"Never rewrites or normalizes `value` — only ever returns an error message or null, so the leading `0` and everything else about what the user actually typed is preserved exactly for the caller to store as-is."*

- **A phone number that fails validation degrades to absent, it is never auto-corrected into something that *would* pass.** (Section 7.) The system will not guess that a malformed number was "probably" missing a digit or "probably" meant a different area code — it simply declines to store it as a phone number and imports the rest of the row.

- **Excel's own leading-apostrophe text marker is stripped only because it is a delivery-format artifact, not user data** (section 3) — the apostrophe was never something the user was trying to store as part of the phone number; it's Excel's own metadata for "treat literally." Stripping it is not "cleaning" the user's actual value, it's *un-wrapping the format's own envelope* around that value — the same category of operation as `unwrapExcelText`'s formula-wrapper case, not a data-correction heuristic.

- **`_rowBelongsToOtherEntity`'s skip logic is deliberately narrow** (section 5) specifically so it never reclassifies a row that's genuinely just missing a required field as "belongs to the other entity" — the comment states plainly that silently discarding a real mistake would be worse than surfacing a confusing-looking error. The system would rather show the user an error they have to think about than guess on their behalf and be wrong.

**Generalizable rule for a reader implementing this from scratch**: any transformation applied to an imported value should be classified as either (a) *unwrapping a known delivery-format envelope* (BOM, Excel's `="..."` formula wrapper, Excel's leading apostrophe) — always safe, because it's about the container, not the content — or (b) *reformatting/auto-correcting the actual data* — never do this silently. When a value can't be validated as-is, the two only acceptable outcomes are: reject the whole row with a clear message (required field), or drop just that field to empty/default and keep the rest of the row (optional field). Never guess a "fixed" version of what the user meant.
