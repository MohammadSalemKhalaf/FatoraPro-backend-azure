# Offline-First Sync Pattern (Mobile ↔ REST Backend)

This document extracts a general, reusable pattern from a production Flutter + .NET codebase (an
invoicing app called "Fatora"): a mobile client whose UI runs entirely off a local SQLite database,
with a background push/pull sync loop reconciling it against a server. The entity names below
(Customer, Product, Order, Receipt) are this project's domain — copy the *mechanism*, not the
domain. It generalizes to any "sales rep in the field with unreliable connectivity" or
"multi-device, occasionally-connected" app: field service, POS, inventory, CRM.

Source repos referenced below: this project's backend (`Fatora.API`, `Fatora.BL`, `Fatora.DAL`)
and its Flutter frontend (`fatora-frontend`). All file paths are given as "this project's
equivalent of `<path>`" — treat them as illustrative locations, not literal requirements for a new
project's folder structure.

---

## 1. The core offline-first principle

**Every read in the app comes from local SQLite. The server is a background sync target, not a
dependency for the UI to function.**

Concretely, in this project's equivalent of `lib/features/orders/data/orders_repository.dart`,
every list/detail method (`getAll`, `getPage`, `getByCustomer`, `getById`) does a plain
`db.query('orders', ...)` against the local database — never an HTTP call. The only exception is a
narrow, explicitly-online-first read (`getAllForRep`, used by an owner filtering another user's
data) that *tries* the network first and falls back to the local cache on `ApiException.isOffline`.
Every write (`create`, `update`, `recordPayment`, `delete`, `returnOrder`) does a local `INSERT`/
`UPDATE` inside a SQLite transaction, marks the row `dirty = 1`, and **returns immediately** —
before any network request has even been attempted:

```dart
Future<Order> create({...}) async {
  final db = await LocalDatabase.instance.database;
  await db.transaction((txn) async {
    await txn.insert('orders', { ...'dirty': 1... });
    // ...compute and store the total locally, adjust local stock...
  });
  SyncManager.triggerOpportunisticSync();   // fire-and-forget, not awaited
  return getById(id);                       // reads back from SQLite
}
```

**Why this, and not "call the API, fall back to a local queue on failure":**

- *Works with no connection at all*, not just flaky connection — there is no code path where a
  write can fail because the network is down, because no write ever waits on the network.
- *Instant UI, no loading spinners for reads or writes* — a list screen renders the moment SQLite
  answers (single-digit milliseconds), and a "Save" button returns instantly whether the device is
  on 4G, in a basement, or in airplane mode. The alternative (optimistic UI + rollback-on-failure)
  requires every screen to handle a write silently reverting later; this design never reverts a
  write the user believes succeeded (see §4 for what *does* happen to a write the server refuses).
- A single mechanism (`dirty` + background sync) replaces per-screen retry/queue logic. Every
  repository (`OrdersRepository`, `CustomersRepository`, `ProductsRepository`, a receipts
  repository) follows the identical shape, so a new syncable entity is "add a table, add four
  mapping functions to the sync manager" (§9), not "design a new offline strategy."

The trade-off this accepts deliberately: a read can be *stale* (this device hasn't pulled another
device's recent change yet). The codebase treats staleness as acceptable and correctness (never
silently losing a write, never showing data that contradicts what the user just did) as
non-negotiable — see §2 and §5.

---

## 2. The dirty-flag protocol

`dirty` is a plain `INTEGER NOT NULL DEFAULT 0` column on every syncable table (`orders`,
`customers`, `products`, `receipts`, and — added later, following the exact same pattern — a
`purchase_requests` table). Its contract:

- **Set to `1`** the instant a local write commits — every `create`/`update`/`delete`/
  `recordPayment`/`returnOrder` method sets `'dirty': 1` in the same SQLite transaction as the
  actual field change. There is no "mark dirty later" step; the flag is inseparable from the write.
- **Cleared to `0`** only in one place: `SyncManager._applyPushResults`, and only for a row whose
  server response says the write was accepted (see §4) — **and** only if the row hasn't moved again
  since (see §5).
- **Never cleared by a pull.** This is the rule the whole protocol exists to protect, stated
  directly in the pull code:

```dart
// This is what made a manual stock adjustment silently snap back to
// its previous number: every tap of the stepper triggers a sync, a
// second tap during the first round joins that same in-flight run
// (see syncNow) rather than starting its own, so its new value was
// never in the push - and then the pull overwrote it.
final dirtyIds = (await txn.query(table, columns: ['id'], where: 'dirty = 1'))
    .map((row) => row['id'] as String).toSet();
for (final json in items) {
  if (dirtyIds.contains(json['id'] as String)) continue;   // <-- skip
  await txn.insert(table, toRow(json), conflictAlgorithm: ConflictAlgorithm.replace);
}
```

**The exact rule:** if a pull response contains a row whose local copy is still `dirty`, **that row
is skipped entirely** for this pull cycle — the server's copy is discarded, not merged, not
compared by timestamp. The reasoning, stated in the code: a row still dirty at this point is a
local write the server has not yet confirmed. It is *definitionally* older information than the
pending local write, regardless of what timestamp it carries, because the local write either (a)
hasn't been pushed yet this round, (b) was pushed but the response hasn't come back yet, or (c) was
pushed and the server explicitly *rejected* it (§4) — none of those states justify overwriting the
device's own copy of its own not-yet-resolved edit.

**The concrete bug this protects against** (documented directly in the source, and covered by a
dedicated regression test, `test/sync_pull_dirty_test.dart`): a stock-quantity stepper button
triggers an opportunistic sync on every tap. A user taps twice quickly. Tap 1 pushes `stock=21`,
gets accepted, clears dirty. Before tap 1's *pull* runs, tap 2 writes `stock=22, dirty=1`. If pull
did not check the dirty flag, the pull would land with the server's still-21 snapshot, `REPLACE`
the row, and the user's second tap — which the server has never even seen yet — silently vanishes.
The row would also lose its `dirty=1` marker in the process, so nothing would ever retry it either.
The fix is exactly the guard above: pull always defers to a still-dirty local row and lets the
*next* sync round's push carry it forward.

The same "skip if dirty" check is duplicated for orders (`_applyPulledOrders`) and reused verbatim
by a second, independent full-refresh path (`ProductsRepository.sync()`, used when a stock page
re-opens) — both hit the same table, so both had to be given the identical guard independently;
one is not a wrapper around the other.

---

## 3. Push-then-pull ordering, and why

A sync round is a strict two-step sequence, never interleaved, never reordered:

```dart
Future<void> _run() async {
  try {
    await _push();
    await _pull();
  } on ApiException {
    // Unreachable server - must never surface as an error; next call retries.
  }
}
```

**Why push must go first:** the pull's whole purpose is to bring down whatever changed
*server-side* since the last successful pull, including changes this same device just made. If
`_pull` ran before `_push`, every dirty row's stale server copy (the one from before this device's
edit) would be sitting in the pull response — and would have to be specially ignored (which is
exactly what the `dirty` check in §2 does, incidentally, so pull-before-push wouldn't corrupt data)
but it would also mean the device's own just-created records (e.g. a brand-new order, whose
server-assigned invoice number doesn't exist until the push creates it) could never appear with
their authoritative server data in the *same* round — an extra round would always be needed. Push
gets the device's outgoing state onto the server first; pull then naturally picks up: (a) that same
write coming back with server-authoritative fields filled in (e.g. a real invoice number replacing
a local placeholder — see the comment in `_applyPulledOrders`: *"the order this device just pushed
comes right back in this same sync round's pull ... landing here with dirty: 0 and the real
number"*), and (b) anything genuinely new from other devices/sessions.

Both steps share one HTTP round trip's worth of latency tolerance: if push fails outright (offline,
or the server is unreachable), the whole `_run()` catches `ApiException` and does nothing further —
pull is simply not attempted this round. This is intentional, not an oversight: attempting a pull
after a failed push would produce a pull whose `since` watermark is unrelated to whether the push
succeeded, and the dirty rows are already safe (§2) regardless of whether pull runs at all this
round. The next opportunistic trigger (§7) retries the whole sequence from the top.

---

## 4. Conflict resolution: push response statuses

The backend's push endpoint processes each item **independently** and returns one status per item,
never one status for the whole batch:

```csharp
public sealed record SyncItemResult(Guid Id, string Status, string? Message = null);
```

Three possible `Status` values, decided per-row in `SyncService.Push<Entity>Async`:

| Status | Meaning | Server-side condition | Client-side effect |
|---|---|---|---|
| `Applied` | The row was inserted or updated as sent. | New row, or `item.UpdatedAt > existing.UpdatedAt` and every permission/business check passed. | `dirty` cleared (subject to §5's timestamp guard). |
| `Conflict` | The server already has an equal-or-newer version. | `item.UpdatedAt <= existing.UpdatedAt` — last-write-wins by timestamp, decided first, before any other check. | `dirty` cleared — the row is *not* re-pushed; the following pull in the same round brings down the server's newer copy and overwrites the local one (safe, because the row is no longer dirty by then). |
| `Rejected` | The write was refused outright — a validation failure, a permission boundary (e.g. a sales rep trying to archive a customer, which is owner-only), a business-rule violation (editing an invoice that already has a payment), or an unhandled server exception caught per-item. | Explicit rule failed, or `catch (Exception ex)` around the whole per-item handler. | `dirty` is **left set to 1**. The client keeps this row exactly as the user last left it, keeps showing it locally, and **retries it on every subsequent sync round indefinitely** — there is no retry limit, no backoff, no "give up after N attempts." |

The client's handling of all three, in one place:

```dart
// Applied (ours won) or Conflict (server's was newer) both mean this
// device's copy is no longer "pending" - either it was saved, or the
// upcoming pull will overwrite it with the server's newer version.
// Rejected (e.g. a validation failure) must NOT clear dirty - the
// server never saved it, so clearing the flag here would make the
// device think it's synced while the row silently never made it,
// permanently losing it. Leaving it dirty keeps retrying and keeps it
// visible locally instead.
if (result['status'] == 'Rejected') continue;
```

**A `Rejected` row retries forever** by design — this is a deliberate choice, not an oversight.
The alternative (give up after N tries) risks silently discarding real user work (a legitimately
recorded sale, a payment) because of a transient server-side issue. The codebase's own commit
history documents a real incident this exact rule caught: a payment-recording write was, for a
period, being routed through a generic "edit" path server-side that rejected any second payment on
the same invoice (the first slipped through only because `PaidAmount` was still 0). Because
`Rejected` correctly kept the row dirty, the payment was retried every round and never silently
lost — the visible symptom was "this payment never syncs," not "the money vanished," which is what
made the bug diagnosable and fixable server-side (see §5 and §10) without any client-side data loss
having already happened. A batch containing one bad row also never blocks the rest of the batch —
each item is validated and pushed independently, at both the request-validator level (`SyncPushRequestValidator`,
which only rejects the whole request for gross size, e.g. >500 items) and the per-item level, so one
malformed line among a day's worth of offline work can never stall everything else that *is* valid.

A whole-item try/catch around each push handler additionally clears the shared `DbContext`'s
change tracker on any exception before continuing to the next item:

```csharp
private SyncItemResult Reject(Guid id, Exception ex)
{
    dbContext.ChangeTracker.Clear();
    return new SyncItemResult(id, "Rejected", ex.Message);
}
```
This matters because all four push loops (customers, products, orders, receipts) share one
`DbContext` for the whole batch — without clearing the tracker, a half-applied failed edit would
still be pending in `Added`/`Modified` state and would get flushed by the *next* item's
`SaveChangesAsync`, either cascading the failure to unrelated rows or silently committing a change
the client was just told had been rejected.

---

## 5. The "second write lands mid-round" race

**The hazard:** a sync round is not instantaneous — it's at minimum one push round trip plus one
pull round trip. Nothing stops the user from making a *second* local write (another tap, another
edit) while a round triggered by the *first* write is still in flight. If the sync mechanism naively
read "all dirty rows" once at the start of `_push`, sent them, and cleared `dirty` for everything in
the response by `id` alone, the second write would be silently and permanently erased: its new value
sits in SQLite, but the row is now marked `dirty = 0` (clobbered by the first write's own successful
push confirmation) and the following pull is now free to overwrite it with the server's — still
missing the second write — copy.

**The exact mechanism that prevents this: matching on `id` AND the row's `updatedAt` at read time,
not `id` alone.** When `_push` reads the dirty rows, it captures each row's `updatedAt` at that
exact moment:

```dart
final pushedUpdatedAtById = {
  for (final row in pushedRows) row['id'] as String: row['updatedAt'],
};
```

When the push response comes back, the `UPDATE ... SET dirty = 0` is conditioned on **both** columns
matching what was actually sent:

```dart
batch.update(
  table,
  {'dirty': 0},
  where: 'id = ? AND updatedAt = ?',
  whereArgs: [id, pushedUpdatedAtById[id]],
);
```

If a second write landed in between — anywhere from "after the dirty rows were read" to "before this
`UPDATE` runs" — its `updatedAt` no longer matches what was captured, the `UPDATE` touches **zero
rows**, and the row is left exactly as the second write left it: new value, still `dirty = 1`. The
comment in the source states the invariant this produces directly:

```
// Matching on updatedAt too (not just id) means a row that moved on
// again since this push read it simply doesn't match here - the
// update below then touches zero rows, leaving it dirty so this same
// round's pull (which only skips rows still marked dirty) and the
// next round's push both still see it as pending.
```

This composes with §2's dirty-checked pull: because the row is still `dirty = 1` after the failed
`UPDATE`, the same round's *pull* also skips it (it would otherwise reintroduce the server's
first-write-only copy), and it survives untouched into the next round's push. Two independent
regression tests in `test/products_sync_race_test.dart` and `test/sync_pull_dirty_test.dart` pin
both flavors of the timing window: a second write landing between push-response and pull
(`onPull` callback in the fake API client fires the second write), and a tighter window where the
second write lands *before the first push's own response has even resolved* (`onPush` callback,
simulating the write landing mid-flight over the wire). Both assert the same outcome: the second
value survives, and the row is still `dirty = 1` afterward.

**Why `id` alone would be wrong, concretely:** it is tempting to treat "the push said Applied for
this id" as sufficient. It is not, because "Applied" only proves the *value the server received* was
saved — it says nothing about whether the local row still holds that same value by the time the
response arrives. The `updatedAt` comparison is what ties the confirmation back to the specific
*version* of the row that was actually sent, which is the same principle the server applies for
last-write-wins conflict detection (§4) applied symmetrically on the client for its own optimistic
local state.

---

## 6. Client-side sync orchestration (single-flight coordinator)

Sync can be triggered from many independent places at once: app boot, a reconnect event, an
opportunistic call right after any local write, and a manual pull-to-refresh. Without coordination,
two concurrent sync rounds could both read the same dirty rows, both push them (harmless but
wasteful), and — worse — interleave their push/pull phases unpredictably. The fix is a **static,
class-level in-flight `Future`**, shared across every `SyncManager()` instance (`SyncManager` is
cheap to construct — it's not a singleton class itself, but its coordination state is process-wide
static state):

```dart
static Future<void>? _inFlight;
static bool _followUpQueued = false;

Future<void> syncNow() {
  final running = _inFlight;
  if (running != null) {
    _followUpQueued = true;
    return running;                 // join the existing run, don't start a new one
  }
  return _inFlight = _run().whenComplete(() {
    _inFlight = null;
    if (!_followUpQueued) return;
    _followUpQueued = false;
    unawaited(syncNow());           // one more round, for whatever arrived mid-run
  });
}
```

Two distinct mechanisms working together:

1. **The in-flight `Future` itself** — any caller that invokes `syncNow()` while a round is already
   running gets *the same `Future`* handed back (`return running`), rather than kicking off a
   second, overlapping round. This alone prevents concurrent pushes/pulls from racing on the
   database.
2. **The follow-up flag** — joining the in-flight run is not sufficient by itself. The comment in
   the source explains why: *"its push has already gone out, so whatever write just triggered this
   call isn't in it and would sit dirty until some unrelated write or a reconnect happened to start
   the next round."* Setting `_followUpQueued = true` guarantees that once the current round
   finishes, **exactly one more round runs immediately after**, which will pick up anything that
   arrived during the first round (including multiple writes — they all collapse into that one
   follow-up round rather than each demanding their own).

This is a coalescing pattern, not a queue: an unbounded number of `syncNow()` calls during one round
produce at most one extra round afterward, not one extra round per call. That bound matters — a busy
screen (e.g. rapid stepper taps, each calling `SyncManager.triggerOpportunisticSync()`) cannot cause
sync rounds to pile up.

`syncAllAfterLogin()` reuses the exact same coordinator — it doesn't run a separate sync path, it
only resets the pull watermark first (see §9) and then calls the same `syncNow()`:

```dart
Future<void> syncAllAfterLogin() async {
  final db = await LocalDatabase.instance.database;
  await db.delete('sync_meta', where: 'key = ?', whereArgs: [_lastPullAtKey]);
  await syncNow();
}
```

---

## 7. Connectivity-aware triggering

A **cheap, process-lifetime interface check** (not a real reachability probe) gates whether a sync
is even attempted, backed by a `connectivity_plus`-style OS-level listener:

```dart
class ConnectivityService {
  ConnectivityService._() {
    _connectivity.onConnectivityChanged.listen(_handleChange);
    unawaited(_seedInitialStatus());
  }
  static final ConnectivityService instance = ConnectivityService._();
  bool _isOnline = true;   // optimistic default, see below
  bool get isOnline => _isOnline;
  Stream<void> get onReconnected => _reconnectedController.stream;
  ...
}
```

Two properties worth calling out explicitly:

- **It is explicitly documented as *not* proof of real internet reachability** — a Wi-Fi interface
  can be "associated" while sitting behind a captive portal or a router with no WAN. The class
  comment states its actual purpose plainly: *"exists purely so callers can skip an obviously-
  pointless sync attempt and react to 'just came back online'; the real online/offline
  determination for correctness always comes from whether the actual HTTP call succeeds."* In other
  words, this check is a fast-path optimization and a retry trigger, never the source of truth for
  whether a write is safe — the source of truth is always whether the request itself threw
  `ApiException` (see `ApiException.isOffline`, true exactly when the server was never reached at
  all — no status code).
- **It defaults to `true` ("online")** until the first real platform check resolves, specifically
  so a sync attempt in that brief startup window isn't skipped for no reason.

Two call sites use this signal:

```dart
static void triggerOpportunisticSync() {
  if (!ConnectivityService.instance.isOnline) return;   // skip a doomed attempt
  unawaited(SyncManager().syncNow());
}

static void startAutoSync() {
  _reconnectSubscription ??= ConnectivityService.instance.onReconnected
      .listen((_) => unawaited(SyncManager().syncNow()));
}
```

`triggerOpportunisticSync()` is what every repository write calls right after committing locally
(§1) — cheap enough to call unconditionally, and it short-circuits instead of paying for a network
call the interface check already knows will fail. `startAutoSync()` is called once, at app boot
(this project's equivalent of `lib/bootstrap.dart`), and subscribes to the *edge* (off→on
transition only, not "on" repeatedly) via `onReconnected` — so the instant the OS reports the
interface came back up, one sync round fires automatically, picking up anything that accumulated
dirty while offline. Idempotent by construction (`_reconnectSubscription ??= ...`), so calling
`startAutoSync()` more than once is harmless.

Note that neither of these two triggers is the *only* line of defense: even if connectivity checking
were wrong in either direction, a genuinely offline HTTP attempt just throws `ApiException`
(`isOffline: true`), which `SyncManager._run()` swallows silently (§3) — the dirty flag protocol
means nothing is lost either way. Connectivity checking is a performance/UX optimization layered on
top of a mechanism that is already safe without it.

---

## 8. Local schema and migrations (frontend)

The local SQLite database owns **its own independent version number and migration mechanism** —
entirely separate from the backend's EF Core migrations, and not required to stay in lockstep with
them. It uses `sqflite`'s built-in `version` + `onUpgrade` hook:

```dart
Future<Database> _open() async {
  final path = join(await getDatabasesPath(), 'fatora_local.db');
  return openDatabase(path, version: 25, onCreate: _onCreate, onUpgrade: _onUpgrade);
}
```

`_onCreate` defines the schema for a brand-new install at the *current* version, in full — new
installs never replay history. `_onUpgrade(db, oldVersion, newVersion)` is a **linear ladder of
`if (oldVersion < N)` blocks**, one per historical version bump, each doing exactly one migration
step and nothing else:

```dart
if (oldVersion < 6) {
  await db.execute('ALTER TABLE products ADD COLUMN stockQuantity INTEGER');
}
if (oldVersion < 8) {
  await db.execute('ALTER TABLE orders ADD COLUMN isEdited INTEGER NOT NULL DEFAULT 0');
}
```

**The established, safe pattern for adding a plain new column:** a single `ALTER TABLE ... ADD
COLUMN` guarded by `if (oldVersion < <newVersion>)`, always nullable or with an explicit `DEFAULT`
(SQLite requires this for `ADD COLUMN` on a populated table) — this runs in a fraction of a second
regardless of table size and never touches existing rows' other data. Bump the schema `version`
integer, add one new `if` block at the bottom, done.

**The escape hatch for a change SQLite's `ALTER TABLE` cannot express** (e.g. dropping a `NOT NULL`
constraint — SQLite has no `ALTER COLUMN`) is documented and used consistently: rename the old
table, create the new one with the desired schema, copy the data across with an explicit column
list, drop the renamed table:

```dart
if (oldVersion < 2) {
  await db.execute('ALTER TABLE customers RENAME TO customers_v1');
  await db.execute('CREATE TABLE customers (... phoneNumber TEXT, ...)');  // now nullable
  await db.execute('''
    INSERT INTO customers (id, name, storeName, phoneNumber, street, city, isActive, createdAt, updatedAt, dirty)
    SELECT id, name, storeName, phoneNumber, street, city, isActive, createdAt, updatedAt, dirty
    FROM customers_v1
  ''');
  await db.execute('DROP TABLE customers_v1');
}
```

Several hard-won rules are visible directly in the comments and worth stating explicitly, because
each corresponds to a real incident:

- **`oldVersion` is fixed for the entire upgrade call** — it is *not* re-read after each block runs.
  A block gated on `oldVersion >= 15` when it should have been unconditional (`oldVersion < N`)
  silently skipped every device jumping more than one version at once (e.g. 14 → 19 in one go, which
  is the *normal* case for any device that hasn't opened the app in a while) — the fix was
  documented in-line specifically so it isn't reintroduced:
  > "Must NOT be gated on `oldVersion >= 15` ... oldVersion is fixed for the whole upgrade call, so
  > a normal 14->19 jump ... always has oldVersion == 14, which never satisfies `>= 15`."
- Migration blocks are **additive and idempotent-by-guard only**, never reordered or merged — each
  stays exactly as it originally shipped, because a device can be upgrading from *any* historical
  version, and the ladder must reproduce the exact sequence of changes that a device already on a
  newer version already applied one-by-one.
- **A backfill for a brand-new column defaults to a value that means "unknown," not a guessed real
  value**, and downstream read code is written to treat that default as "not yet resolved" rather
  than as fact (see the `total` column added at version 25, and how `_hydrateOrders` in §10 treats
  a `null` there as "fall back to the old computed formula," never as "zero" or the new formula).
- The very first migration for a table sometimes ships **without** the `dirty` column (because dirty
  tracking hadn't been designed in yet for that table), and a later migration adds it with
  `DEFAULT 0` — which is a lie for any row whose one-shot, pre-dirty-tracking push had already
  silently failed. The established fix pattern is a **second**, immediately-following migration
  block that does a one-time `UPDATE ... SET dirty = 1` backfill for every existing row, trading a
  guaranteed-harmless re-push (the server-side stale-write guard makes a redundant push a no-op) for
  the certainty that nothing already-broken stays invisible to the retry mechanism forever.

`wipeForAccountSwitch()` is the deliberate exception to "never delete dirty rows": when the signed-in
account itself changes (a device handed from one sales rep to another, or to the owner), every table
— *including* dirty rows — is wiped, because a dirty row from the *previous* account's session can
never legitimately belong to the next account's data and must not be pushed under its identity.

---

## 9. Backend sync endpoint shape

Two endpoints, both under one controller, both scoped to the authenticated caller
(`[Authorize(Roles = "SalesRep,Rep")]`; the effective account and, where relevant, the specific
sales rep are derived from JWT claims, never from a request parameter):

### Push — `POST /api/sync/push`

**Request** — one entity type per array, all four sent together in a single call:

```csharp
public sealed record SyncPushRequest(
    List<CustomerSyncItem> Customers,
    List<ProductSyncItem> Products,
    List<OrderSyncItem> Orders,
    List<ReceiptSyncItem> Receipts
);
```

Each item record carries the entity's own writable fields plus, critically, `Id` (client-generated —
see below) and `UpdatedAt` (the device's own edit timestamp, not "now"):

```csharp
public sealed record OrderSyncItem(
    [Required] Guid Id, [Required] Guid CustomerId, DateOnly? DueDate,
    [Range(0, 100)] decimal Discount, string? Notes,
    [Range(0, double.MaxValue)] decimal PaidAmount, DateTime UpdatedAt,
    [Required, MinLength(1)] List<OrderItemSyncItem> Items,
    ...
);
```

**Response** — one `SyncItemResult { Id, Status, Message? }` per submitted item, grouped the same
way as the request, per §4:

```csharp
public class SyncPushResponse {
    public List<SyncItemResult> Customers { get; set; } = new();
    public List<SyncItemResult> Products { get; set; } = new();
    public List<SyncItemResult> Orders { get; set; } = new();
    public List<SyncItemResult> Receipts { get; set; } = new();
}
```

**IDs are client-generated (UUIDv4), never server-assigned.** Every `create()` on the client
generates its own `Guid`/`uuid` before ever touching the network (`final id = _uuid.v4();`), and that
same id is the primary key both locally and on the server. This is what makes offline creation
possible at all — a server-assigned auto-increment id would require a round trip before the local
row could even be inserted. The one field that genuinely *is* server-authoritative (an invoice
number, drawn from a per-account sequence) is deliberately handled differently: the client computes
a plausible *placeholder* locally (continuing its own best-known local sequence) so the UI never
shows a blank, and the authoritative value silently replaces it on the next pull (§3, §10).

**Timestamp preservation is what makes last-write-wins meaningful.** A normal REST create/update
(`AppDbContext.SaveChanges`) auto-stamps `CreatedAt`/`UpdatedAt` to `DateTime.UtcNow` on every save
via `ISyncableEntity` — but the sync push path explicitly turns that off for the duration of the
whole batch:

```csharp
// Preserve the device's real edit timestamps (possibly recorded hours ago while offline)
// instead of stamping "now" on arrival - last-write-wins depends on this being accurate.
dbContext.SuppressAutoTimestamps = true;
```

Without this, every pushed item would receive the *server's* receipt time as `UpdatedAt`, which
would make the `Conflict` check in §4 (`item.UpdatedAt <= existing.UpdatedAt`) meaningless — a
batch of edits made hours apart offline would all appear to have happened simultaneously, at
whatever moment the sync round happened to run.

### Pull — `GET /api/sync/pull?since={timestamp}`

A **delta pull keyed on a single watermark timestamp**, `since`, supplied by the client:

```csharp
public async Task<SyncPullResponse> PullAsync(Guid userId, DateTime since, Guid? scopeToRepId = null)
{
    var serverTime = DateTime.UtcNow;                       // captured once, at the start
    var customers = await dbContext.Customers
        .Where(c => c.UserId == userId && c.UpdatedAt > since).ToListAsync();
    ...
    return new SyncPullResponse { ServerTime = serverTime, Customers = ..., Products = ..., Orders = ..., Receipts = ... };
}
```

The response echoes back `ServerTime` — the moment the server *started* answering this pull, not
`DateTime.UtcNow` computed after every query ran. The client stores exactly this value as its next
watermark:

```dart
await _writeLastPullAt(response['serverTime'] as String);
```

This detail matters for correctness: capturing the server's clock *before* querying (rather than the
client's own clock, or the server's clock *after* the queries finish) closes the gap between "data
that changed while this pull's queries were running" and "the next pull's `since`" — using the
client's own clock would be vulnerable to clock skew between devices and the server; using the
server's post-query time would risk missing a row that was updated in the exact window between the
query running and the response being sent.

**Batching every syncable entity type into one round trip**, rather than one call per entity type,
is a deliberate choice stated implicitly by the shared endpoint shape and explicitly in the client
comment:

```dart
// Customers, products and orders all push/pull through the same combined
// /sync/push and /sync/pull calls (the backend accepts/returns every
// syncable entity in one request) - a per-entity round trip would just
// be redundant network chatter for no benefit.
```

For a mobile client on a metered or high-latency connection, N separate round trips (each paying
full TCP/TLS/auth overhead) for N entity types is strictly worse than one request carrying N arrays
— especially since `Order` here has a foreign-key dependency on `Customer`/`Product` from the *same*
batch (a newly created invoice, referencing a newly created customer, both made offline in the same
session) and must be processed after them within that one request:

```csharp
foreach (var item in request.Customers) response.Customers.Add(await PushCustomerAsync(...));
foreach (var item in request.Products)  response.Products.Add(await PushProductAsync(...));
// Orders may reference customers/products from this same batch, so they must be
// processed (and individually saved) after the two loops above complete.
foreach (var item in request.Orders)    response.Orders.Add(await PushOrderAsync(...));
foreach (var item in request.Receipts)  response.Receipts.Add(await PushReceiptAsync(...));
```

Each item is still saved with its own `SaveChangesAsync()` (not batched into one giant transaction)
— this is what keeps per-item success/failure independent (§4): one entity type's loop running after
another's is an *ordering* guarantee for cross-references, not a shared-transaction guarantee, and
a rejected item's tracker state is explicitly cleared (§4) so it can't contaminate the next item's
save even within the same request.

A generic entity marker interface, `ISyncableEntity { DateTime CreatedAt; DateTime UpdatedAt; }`,
is what every syncable entity implements — it's the seam the auto-timestamp logic in
`AppDbContext.SaveChanges` hooks into (`ChangeTracker.Entries<ISyncableEntity>()`), so adding a new
syncable entity to the whole system starts with implementing this interface, which alone gets it
correct create/update timestamp behavior for every *non*-sync write path (its normal REST
controller, if it has one) for free.

---

## 10. Worked example: editing an order's payment offline, end to end

Concrete walkthrough of one entity (`Order`) through the entire loop, tracing the codebase's own
scenario for a partial payment recorded while offline.

1. **User edits offline.** A sales rep, with no connectivity, opens an invoice and records a
   payment: `OrdersRepository.recordPayment(id, amount)` runs.

2. **Local write.** Inside one call, this repository method reads the current order from SQLite,
   validates the amount against the locally-known remaining balance, then writes directly to the
   local table:
   ```dart
   await db.update('orders', {
     'paidAmount': existing.paidAmount + amount,
     'updatedAt': DateTime.now().toUtc().toIso8601String(),
     'dirty': 1,
   }, where: 'id = ?', whereArgs: [id]);
   ```
   This commits instantly, regardless of connectivity. The method then returns
   `getById(id)` — a fresh read straight back out of SQLite — to the caller; the UI updates
   immediately, no spinner.

3. **What triggers a sync attempt.** The same method calls
   `SyncManager.triggerOpportunisticSync()` right after the write (fire-and-forget, not awaited).
   Since the device is offline, `ConnectivityService.instance.isOnline` is `false`, so this call
   returns immediately without even attempting a network request (§7). The order sits `dirty = 1`
   in SQLite. Later, the device regains connectivity; `ConnectivityService`'s OS-level listener
   fires the off→on transition, and `SyncManager.startAutoSync()`'s subscription (wired once at
   boot) calls `SyncManager().syncNow()` automatically (§7) — no user action needed.

4. **Push payload.** `syncNow()` finds no other sync in flight, so it runs `_run()` directly
   (§6). `_push()` queries `where: 'dirty = 1'` across all four tables; this order is included.
   Its row (plus its `order_items` rows) is serialized into the batch:
   ```json
   {
     "orders": [{
       "id": "…", "customerId": "…", "discount": 0, "paidAmount": 150.0,
       "updatedAt": "2026-08-15T14:03:00.000Z", "items": [...], "isReturned": false, "isDeleted": false
     }],
     "customers": [], "products": [], "receipts": []
   }
   ```
   sent as a single `POST /api/sync/push`.

5. **Server processing.** `SyncController.Push` runs the batch/per-item validators first (§4);
   assuming it passes, `SyncService.PushAsync` calls `PushOrderAsync`. It loads the existing order
   by id, confirms `item.UpdatedAt > existing.UpdatedAt` (not a `Conflict`), then reaches the
   dedicated payment-only branch — checked *before* the generic edit-rejection rules
   (`CoveredByReceipt` / `PaidAmount > 0` guards) specifically so a payment is never mistaken for a
   disallowed content edit:
   ```csharp
   var isPaymentOnlyChange = existing.CustomerId == item.CustomerId && existing.Discount == item.Discount
       && ... && OrderService.OrderItemsMatch(existing.OrderItems, item.Items...);
   if (isPaymentOnlyChange) {
       existing.PaidAmount = Math.Min(Math.Max(existing.PaidAmount, item.PaidAmount), existing.Total);
       existing.UpdatedAt = item.UpdatedAt;   // SuppressAutoTimestamps keeps this the device's own time
       await dbContext.SaveChangesAsync();
       return new SyncItemResult(item.Id, "Applied");
   }
   ```
   `Math.Max` against the existing value (rather than assignment) is deliberate: it merges a
   possible concurrent payment from a second device (e.g. the owner collecting cash against the same
   invoice from their own app) instead of one overwriting the other.

6. **Push response.** The server returns `{"orders": [{"id": "…", "status": "Applied"}], ...}`.

7. **Client applies the response.** `_applyPushResults` matches on `id` **and** the `updatedAt`
   this row actually carried when it was read for this push (§5). Assuming nothing edited this same
   order again in the meantime, the match succeeds:
   ```dart
   batch.update('orders', {'dirty': 0}, where: 'id = ? AND updatedAt = ?', whereArgs: [id, sentUpdatedAt]);
   ```
   `dirty` is now `0` — the row is considered confirmed. (Had the status been `Rejected`, this row
   would be skipped entirely, staying `dirty = 1` forever until it succeeds — §4.)

8. **The following pull.** Still within the same `_run()`, `_pull()` fires
   `GET /api/sync/pull?since=<lastPullAt>`. Because this order's `UpdatedAt` on the server is now
   after `since`, the server's `PullAsync` includes it in the `orders` array of the response,
   already reflecting the merged `PaidAmount` and a fresh `UpdatedAt`.

9. **Final local state.** `_applyPulledOrders` checks the dirty set first (§2) — the row is no
   longer dirty (step 7 cleared it), so it is **not** skipped; the server's row overwrites the local
   one via `conflictAlgorithm: ConflictAlgorithm.replace`, and its nested `order_items` are replaced
   from the pull payload too. `_writeLastPullAt` stores the new `serverTime` watermark. The order in
   local SQLite now exactly mirrors the server's authoritative record — `dirty = 0` — and the next
   time `OrdersRepository._hydrateOrders` reads this row (e.g. reopening the invoice), the UI shows
   the confirmed payment with no further action needed. Had a *second* edit landed on this same
   order between steps 4 and 7 (§5's race), step 7's `id + updatedAt` match would simply have failed
   silently, the row would still be `dirty = 1`, step 9 would skip it entirely, and the whole
   sequence above would repeat on the next round with the newer value — never losing it, never
   showing a half-applied state.

---

## Summary of the generalizable rules

1. Local storage is the only thing the UI ever reads from; network is one-directional background
   input into that store.
2. A dirty flag is set atomically with every local write and is the *only* thing a pull is allowed
   to check before deciding to overwrite a row.
3. Push always precedes pull within one round, and a failed push aborts the round rather than
   running a pull with stale context.
4. Every pushed item gets its own independent accept/reject/conflict verdict; only a genuinely
   confirmed write may clear its dirty flag; a rejected write stays dirty and retries indefinitely.
5. Clearing a dirty flag must be conditioned on the row not having changed since it was read for
   that push (match on id *and* a version/timestamp captured at read time), or a write made
   mid-round is invisibly lost.
6. All sync triggers funnel through one coordinator with an in-flight guard plus a coalesced
   follow-up flag, so concurrent triggers never race the same rows.
7. Connectivity checking is a cheap optimization to skip doomed attempts and to retry on reconnect —
   never the actual source of truth for correctness, which always comes from whether the request
   itself succeeded.
8. The local database has its own versioned migration ladder, independent of the server's; every
   step is additive, one-purpose, gated on a fixed `oldVersion`, and default values for new columns
   must be treated downstream as "unknown," never as fact.
9. Multiple entity types batch into one push/pull round trip, keyed by a single server-clock
   watermark, with server-preserved (not server-stamped) edit timestamps making last-write-wins
   meaningful; entity ids are client-generated so creation never needs a round trip first.
