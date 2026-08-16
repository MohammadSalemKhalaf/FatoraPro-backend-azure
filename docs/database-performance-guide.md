# Database Performance Pattern: Indexing, Pagination, Computed Columns, and Cache Invalidation

## Where this pattern comes from

This document extracts a database-performance pattern from a production offline-first business app: a .NET 8 / EF Core / PostgreSQL backend paired with a Flutter / SQLite offline-first client. The domain in the source project is invoicing (customers, products, orders/invoices) — this document treats those as **illustrative examples of general shapes** ("a paginated list endpoint", "a parent entity with computed money totals", "a sync-delta query"), not as the point of the pattern. Map the shapes onto your own entities.

The core thesis this project demonstrates: **most business CRUD applications never need a caching layer, a CQRS split, or a read-replica.** They need three cheap things done correctly — the right composite indexes for the queries you actually run, a clear rule for when a "computed" value is allowed to live only in application code versus when it must become a real column, and a pagination contract that is honest about what "more" means. This project earned that conclusion the hard way (see the `Total` computed-property story in Section 3) rather than assuming it up front, which is why it's worth copying.

---

## 1. Indexing strategy

### The rule this project follows

Index for the queries you actually run, not for "every foreign key" reflexively (EF Core already does that part for you — see below) and not for every column that *could* be filtered on. Every `HasIndex` call in this codebase maps to one identifiable, real query shape.

### EF Core's automatic part: every FK gets an index for free

By convention, EF Core creates a non-unique index on every foreign-key shadow/scalar property it discovers via `HasOne().WithMany()`, without any explicit configuration. This project's `OrderItem` entity has no `HasIndex` calls in its `IEntityTypeConfiguration<OrderItem>` at all, yet the migration snapshot shows:

```csharp
// AppDbContextModelSnapshot.cs — generated, not hand-written
b.HasKey("Id");
b.HasIndex("OrderId");
b.HasIndex("ProductId");
```

Those two indexes exist purely because `OrderItem.OrderId` and `OrderItem.ProductId` are FK columns. **Lesson: don't hand-write `HasIndex` for a bare foreign key just to "cover the join" — EF Core convention already does it.** Only add an explicit index when you need something the convention doesn't give you: a composite, a uniqueness constraint, or a filtered/partial index.

### The explicit, deliberate indexes — and the query each one exists for

This project's equivalent of `Fatora.DAL/Data/Configuration/*Configuration.cs` — one `IEntityTypeConfiguration<T>` class per entity — is where every deliberate index lives, each with a comment or an obvious call site tying it to a real query:

**1. Composite `(TenantId, UpdatedAt)` for delta-sync pulls.** Every syncable entity (this project's equivalent of `Order`, `Product`, `Customer`) carries the same shape:

```csharp
// OrderConfiguration.cs
builder.HasIndex(x => new { x.UserId, x.UpdatedAt });
```

This exists because the mobile client's pull endpoint runs exactly this filter on every sync round-trip:

```csharp
// SyncService.PullAsync — this project's equivalent of "give me everything
// changed since my last checkpoint"
var products = await dbContext.Products
    .Where(p => p.UserId == userId && p.UpdatedAt > since)
    .ToListAsync();
```

Without the composite index, this degrades to a full scan of one tenant's rows per sync call, on every device, every few seconds. The general shape: **any "give me what changed since timestamp X, scoped to tenant/owner Y" query needs a composite index with the scope column first and the timestamp column second** — that column order lets Postgres use the index for both the equality filter and the range filter in one pass.

**2. Composite unique index for a human-facing sequence number, scoped per tenant.**

```csharp
// OrderConfiguration.cs
builder.HasIndex(x => new { x.UserId, x.InvoiceNumber }).IsUnique();
```

`InvoiceNumber` (e.g. `INV/2026/0001`) is unique *per user*, not globally — two different tenants can both have invoice `0001`. A single-column unique index on `InvoiceNumber` alone would be wrong (it would reject a second tenant's `0001`); a composite unique index scoped by tenant is the correct shape whenever "unique" really means "unique within this owner's data," which is the common case in any multi-tenant-by-row (not multi-tenant-by-schema) system.

**3. Filtered/partial unique index — uniqueness that only applies to non-null values.**

```csharp
// ProductConfiguration.cs
// Filtered so multiple products can still have a NULL barcode -
// uniqueness only matters once a barcode is actually set, since the
// scanner needs it to resolve to exactly one product.
builder.HasIndex(x => new { x.UserId, x.Barcode })
    .IsUnique()
    .HasFilter("\"Barcode\" IS NOT NULL");
```

Postgres treats every `NULL` as distinct for uniqueness purposes by default, so a plain unique index on a nullable column would technically already allow unlimited `NULL`s — but a **filtered index** is still the right call here for two reasons beyond that: it keeps the index smaller (rows with no barcode never enter it) and it makes the intent self-documenting in the schema itself, rather than relying on incidental NULL-handling semantics. General rule: whenever a uniqueness constraint is conditional ("unique once set", "unique among active rows"), reach for a filtered index rather than either (a) a plain unique index relying on NULL semantics, or (b) enforcing uniqueness only in application code, which races under concurrent writes.

**4. Plain single-column index on an FK that EF's convention wouldn't otherwise catch.** `Customer.CreatedByRepId`, `Product.CreatedByRepId`, `Order.CreatedByRepId` are all explicitly indexed even though they're also FK columns — because in this case they're **nullable, optional attribution FKs** (who on the tenant's team created this row), and every one of them is queried directly ("show me everything Rep X created") outside of any join. The pattern: even when EF's automatic FK index would technically cover it, indexing explicitly next to a documented reasoning comment is worth the one extra line when the column is queried standalone, not just joined.

**5. Single-column unique index for a lookup/credential value.**

```csharp
// RepConfiguration.cs / RefreshTokenConfiguration.cs / UserConfiguration.cs
builder.HasIndex(x => x.QrToken).IsUnique();
builder.HasIndex(x => x.Token).IsUnique();
builder.HasIndex(x => x.UserName).IsUnique();
```

Any column that is both (a) the sole predicate of a lookup query (`WHERE Token = @t`) and (b) supposed to be unique gets a unique index — this both enforces the invariant at the database layer (never trust application code alone for uniqueness under concurrent writes) and makes the lookup itself O(log n) instead of a scan.

**6. Composite index for an audit/reporting time-range query.**

```csharp
// SubAdminAccountActionConfiguration.cs
builder.HasIndex(x => new { x.PerformedBySubAdminId, x.CreatedAt });
```

Same shape as #1 (scope column, then time column) but for "show me what admin X did between date A and B" instead of a sync delta — the general lesson is the same composite-index shape recurring for a different query with the same structure: **equality-filter column first, range-filter/sort column second.**

### What's conspicuously *not* indexed

`Order.CreatedAt` has no index (composite or otherwise) even though the paginated invoice list and every report-building query sort or filter by it (`OrderByDescending(o => o.CreatedAt)`, date-range grouping in `ReportService`). Only `UpdatedAt` is indexed. This is a real, honest gap in the current schema, not an oversight to hide: it works today because the FK index on `UserId` narrows a query to one tenant's row count (typically hundreds to low thousands of invoices), and Postgres can sort that filtered slice in memory cheaply without ever touching a `CreatedAt` index. Section 9 revisits this as the concrete "signal you've outgrown this" example — the day a tenant's invoice count is large enough that this sort shows up in `EXPLAIN ANALYZE`, add `HasIndex(x => new { x.UserId, x.CreatedAt })` next to the existing `UpdatedAt` one. Don't add it preemptively; add it when a query plan says to.

---

## 2. AutoInclude vs. explicit Include vs. lazy loading

### The default: AutoInclude for navigations that are *always* needed together with their parent

This project never enables EF Core's lazy-loading proxies at all — lazy loading is rejected globally, because it turns "read one entity" into an unbounded number of hidden queries the moment any code touches a navigation property, and that N+1 risk is invisible at the call site. Instead, the default loading strategy is **eager loading via `AutoInclude()`, applied per-navigation in the entity's own configuration class** — not per query call site:

```csharp
// ItemConfiguration.cs (this project's equivalent of an OrderLine → Product FK)
public void Configure(EntityTypeBuilder<OrderItem> builder)
{
    builder.Navigation(x => x.Product).AutoInclude();
    builder.Ignore(x => x.TotalPrice);
}
```

```csharp
// OrderConfiguration.cs
builder.Navigation(x => x.OrderItems).AutoInclude();
```

```csharp
// PurchaseRequestConfiguration.cs
builder.Navigation(x => x.Items).AutoInclude();
```

The reasoning: `OrderItem` is never meaningful without knowing which `Product` it refers to (you can't render an invoice line item without a product name/price), and `Order` is never meaningful without its line items (an invoice without line items isn't an invoice, it's a header). These are cases where "loading the parent without the child" is not actually a valid use case anywhere in the application — so making it the *default*, opt-out-if-needed behavior removes an entire class of N+1 bugs (forgetting one `.Include()` on one of ten call sites) without having to audit every query.

**Why not AutoInclude everything?** Because AutoInclude is a blunt, global setting per navigation — it applies to *every* query against that entity type, including ones that only need a projection of a few scalar columns. `Order.Customer` is deliberately **not** AutoIncluded, because plenty of call sites only need order-level fields and would pay for a needless join. Instead, `Customer` is pulled in with an **explicit `Include()`** exactly where it's actually displayed:

```csharp
// OrderService.GetPagedAsync — this project's equivalent of a paginated
// invoice list that needs to show the customer's name in each row
var orders = await dbContext.Orders
    .Include(o => o.Customer)
    .Where(o => o.UserId == userId && (scopeToRepId == null || o.CreatedByRepId == scopeToRepId))
    .OrderByDescending(o => o.CreatedAt)
    .Skip(skip)
    .Take(take)
    .ToListAsync();
```

```csharp
// SyncService — same explicit Include, same reasoning, different call site
var existing = await dbContext.Orders.Include(o => o.Customer).FirstOrDefaultAsync(...);
```

### The rule to copy

- A navigation is a candidate for **AutoInclude** only when there is no legitimate query in the whole application that wants the parent *without* that child — i.e., the child is conceptually part of the parent's identity, not an optional enrichment.
- A navigation that's needed by *some* call sites but not others stays **explicit `Include()`**, decided per query, so call sites that don't need it don't pay a join for it.
- **Lazy loading is not used anywhere** — every data-access need is either satisfied by AutoInclude, an explicit Include, or (for read-only reporting queries) `AsNoTracking()` plus a materializing `ToListAsync()`. This project's equivalent of a reporting service (`RepActivityService`, `SubAdminActivityService`) exclusively uses `AsNoTracking()` for its read-only aggregation queries — tracking would build a change-tracker graph for entities that are only ever displayed, never mutated, which is pure overhead:

```csharp
// RepActivityService — read-only, so no change tracking needed
var orders = await dbContext.Orders
    .AsNoTracking()
    .Where(o => o.CreatedByRepId == repId && o.CreatedAt >= startOfDay && o.CreatedAt < endOfDay)
    .ToListAsync();
```

The general rule for `AsNoTracking`: any query whose result will be mapped to a response DTO and returned, never mutated and saved back through the same `DbContext`, should carry `AsNoTracking()`. The CRUD services in this codebase (`OrderService.CreateAsync`, `UpdateAsync`) deliberately *omit* it, because those queries load an entity specifically to mutate and `SaveChangesAsync()` it in the same unit of work.

---

## 3. Computed properties vs. stored columns — the EF translation trap

This is the single most instructive real incident in this codebase, because the team hit the actual bug, diagnosed it, and fixed it with a schema migration plus a service-layer refactor. It is worth reproducing exactly.

### The trap, precisely

A C# get-only property with an expression body and no backing field —

```csharp
// OrderItem.cs
public decimal TotalPrice => UnitPrice * Quantity;
```

— has no way for EF Core to persist or read a value *for* it in SQL, because there is nothing to write into a column: it's pure computation over other CLR properties, evaluated only when accessed in memory. EF Core's model-building convention only maps properties that have a setter (or a matching backing field); a getter-only expression-bodied property fails that check and is excluded from the model automatically. `OrderItem.TotalPrice` never becomes a column — proven by the actual migration snapshot, whose `OrderItem` mapping lists only `Id`, `IsEdited`, `OrderId`, `ProductId`, `Quantity`, `UnitPrice`; `TotalPrice` is absent. The codebase still calls `builder.Ignore(x => x.TotalPrice)` explicitly in `ItemConfiguration` — belt-and-suspenders, making the exclusion an explicit, self-documenting decision in the schema config rather than relying silently on convention, which matters because EF Core's exact convention behavior here is a genuinely easy thing to get subtly wrong across EF Core versions, and an explicit `Ignore()` fails loudly at model-build time if that assumption ever stops holding, instead of failing at query time.

**The actual risk this creates:** the moment *any* LINQ query tries to push a computed, unmapped property into a `Where`, `OrderBy`, or aggregate that has to run **inside the SQL translation** (i.e., on an `IQueryable<T>` before `ToListAsync()`/materialization), EF Core (3.0+) throws `InvalidOperationException: could not be translated` at runtime — because there is no column for Postgres to filter, sort, or sum on. Older EF Core versions (and some other ORMs) don't throw; they silently fall back to full client-side evaluation, which means pulling the *entire unfiltered table* into application memory and filtering/sorting it in .NET — a correctness trap disguised as a performance trap, since it still "works," just catastrophically slower and invisible until the table grows.

### The exact rule this project follows

**A computed property is safe exactly when it is only ever read after the owning entity has already been materialized into memory** (i.e., always downstream of a `ToListAsync()`/`FirstOrDefaultAsync()`/`ToList()`, on a plain `IEnumerable<T>` or a single loaded object) — **never inside an expression that EF still has to translate into SQL** (`Where`, `OrderBy`, `Select` projections evaluated at the DB, or `Sum`/`GroupBy` called directly on an `IQueryable<T>`).

Every real usage in this codebase honors that rule. `Order.Subtotal`, `Order.DiscountAmount`, and `Order.RemainingBalance` are all still plain get-only computed properties today (not stored columns) —

```csharp
// Order.cs
public decimal Subtotal => OrderItems.Sum(o => o.TotalPrice);
public decimal DiscountAmount => Subtotal * (Discount / 100m);
public decimal RemainingBalance => IsReturned ? 0 : Total - PaidAmount;
```

— and every place they're read is *after* materialization:

```csharp
// OrderService.ToResponse — mapping an already-loaded Order to a DTO
internal static OrderResponse ToResponse(Order order) => new()
{
    ...
    Subtotal = order.Subtotal,
    DiscountAmount = order.DiscountAmount,
    RemainingBalance = order.RemainingBalance,
    ...
};
```

```csharp
// ReportService.GetSummaryAsync — the query itself only filters on real
// columns (UserId, CreatedAt); the Sum() over RemainingBalance runs after
// ToListAsync(), against a List<Order> in memory, not against IQueryable<Order>
var orders = await query.ToListAsync();
var summary = new OrderSummaryResponse
{
    TotalAmount = orders.Sum(o => o.Total),
    TotalOutstanding = orders.Sum(o => o.RemainingBalance)   // safe: LINQ-to-Objects
};
```

This pattern — filter/sort on real, indexed columns at the database layer, materialize the (already-bounded) result set, *then* compute derived values in .NET — is what makes the computed properties safe at this application's scale. It only remains safe because the upstream `Where` already bounds the row count to "one tenant's orders in a date range," never "every order in the whole database."

### When a computed value *must* become a real stored column instead — the `Order.Total` story

`Order.Total` used to be exactly this kind of computed, unmapped property — and it was rounded to the nearest half-unit in the getter. The team needed to remove that rounding for full decimal precision, and discovered the actual danger of computed properties isn't only "can EF translate this into SQL" — it's also **"can this value silently change out from under an already-issued, already-printed invoice the instant the formula changes,"** because a computed property has no independent existence: change the code, and every historical row's displayed value changes retroactively, with no audit trail and no way to freeze what a customer was already shown.

The fix: `Total` became a real, stored, non-nullable `decimal` column —

```csharp
// Order.cs — the comment documents both reasons at once
// A real, stored column (not computed) - true decimal precision, no
// rounding. Explicitly (re)assigned by OrderService.ComputeTotal at the
// exact moments an order is genuinely created or edited ... This is what
// lets an already-issued invoice's Total stay frozen forever once set,
// instead of drifting if the formula ... ever changes.
public decimal Total { get; set; }
```

— assigned explicitly, once, by a single shared formula function at every genuine create/edit call site (`OrderService.CreateAsync`, `UpdateAsync`, and `SyncService.PushOrderAsync`'s create/edit branches), and **never** recomputed by read paths like `RecordPaymentAsync` or `ReturnAsync`, which only ever read it.

```csharp
// OrderService.cs — single source of truth for the formula, shared by
// every write path so none of them can drift from another
internal static decimal ComputeTotal(IEnumerable<OrderItem> items, decimal discountPercent, decimal cashDiscount)
{
    var subtotal = items.Sum(i => i.TotalPrice);
    var discountAmount = subtotal * (discountPercent / 100m);
    return subtotal - discountAmount - cashDiscount;
}
```

**The general, portable rule to take from this incident:**

| Situation | Keep as computed C# property | Convert to a real stored column |
|---|---|---|
| Only ever read after the entity is already loaded (mapping to a DTO, summing an already-materialized list) | ✅ Safe, zero schema cost | Unnecessary |
| Ever filtered, sorted, or aggregated *at the database level* (`.Where(x => x.Computed > n)`, `.OrderBy(x => x.Computed)`, `.Sum()`/`.GroupBy()` on an `IQueryable`) | ❌ Throws at runtime (or silently full-scans, on older EF/other ORMs) | ✅ Required — there's no other way to push it into SQL |
| The formula behind it might ever change, and past rows must **not** silently change value when it does (financial totals, anything that was ever shown/printed/emailed to an end user) | ❌ Every historical row retroactively "changes" the moment the code changes | ✅ Required — a stored column is the only way to freeze history |
| The value needs to be independently queryable by an external system, export, or index | ❌ Not a column, can't be indexed or selected in raw SQL | ✅ Required |

Converting a value that fails any one of these criteria is not "premature optimization" — it's a correctness requirement, not a performance one, even though it also happens to be the performance-correct choice.

---

## 4. Pagination pattern

### The shape: offset (skip/take) pagination, not cursor-based

This project uses plain offset pagination end-to-end — deliberately simple, appropriate for the row counts involved (Section 9 covers when this stops being true).

**Backend EF query shape** — this project's equivalent of a paginated products/invoices list:

```csharp
// ProductService.cs
public async Task<List<ProductResponse>> GetPagedAsync(Guid userId, int skip, int take, Guid? scopeToRepId = null)
{
    var query = dbContext.Products.Where(p => p.UserId == userId && p.IsActive);
    query = await ApplyRepScopeAsync(query, scopeToRepId);

    var products = await query.OrderByDescending(p => p.CreatedAt).Skip(skip).Take(take).ToListAsync();

    return products.Select(ToResponse).ToList();
}
```

```csharp
// OrderService.cs — same shape, same comment worth repeating verbatim:
// Newest-first, matching the order the invoice list has always shown -
// a stable sort is required for Skip/Take to mean the same "page" twice.
public async Task<List<OrderResponse>> GetPagedAsync(Guid userId, int skip, int take, Guid? scopeToRepId = null)
{
    var orders = await dbContext.Orders
        .Include(o => o.Customer)
        .Where(o => o.UserId == userId && (scopeToRepId == null || o.CreatedByRepId == scopeToRepId))
        .OrderByDescending(o => o.CreatedAt)
        .Skip(skip)
        .Take(take)
        .ToListAsync();

    return orders.Select(ToResponse).ToList();
}
```

**The subtlety worth calling out explicitly, straight from the comment:** offset pagination is only meaningful — page 2 only means "the next 20 rows after page 1," not "some overlapping or gapped set" — if the `ORDER BY` is over a column (or tuple) that produces a **total, stable ordering**. `CreatedAt` alone is a reasonable practical choice here (collisions are rare enough in this domain), but the general-purpose-safe version of this pattern is to order by `(CreatedAt, Id)` or any column plus a tiebreaker guaranteed unique, so that two rows with an identical timestamp can never silently swap pages or get skipped/duplicated across two page fetches.

### The API contract: optional, additive, backward-compatible

```csharp
// ProductsController.cs
// skip/take are optional and additive - omitting both preserves the
// original "return everything" behavior existing callers (invoice
// product picker, data export, barcode lookups) still rely on.
[HttpGet]
public async Task<IActionResult> GetAll([FromQuery] int? skip, [FromQuery] int? take, [FromQuery] Guid? repId)
{
    var scopeToRepId = User.GetRepIdOrNull() ?? repId;

    if (skip is null && take is null)
    {
        var all = await productService.GetAllAsync(User.GetEffectiveOwnerId(), scopeToRepId);
        return Ok(all);
    }

    var result = await productService.GetPagedAsync(
        User.GetEffectiveOwnerId(), skip ?? 0, Math.Clamp(take ?? 20, 1, 100), scopeToRepId);
    return Ok(result);
}
```

Two details worth copying directly:

1. **`skip`/`take` are nullable query params, and their *absence* (both null) is a distinct code path from "paginate with defaults"** — it falls through to the pre-existing "return everything" endpoint behavior, so adding pagination to an endpoint that already had callers (a product picker, a CSV export, a barcode lookup — all of which legitimately want the whole set) doesn't break them. Don't force every existing caller to learn pagination just because one new screen needs it.
2. **`Math.Clamp(take ?? 20, 1, 100)` on the server, not just the client.** A default page size when the caller doesn't specify one, and a hard ceiling regardless of what the caller asks for — a client bug (or a malicious client) requesting `take=50000` cannot force an unbounded query. This clamp belongs at the API boundary, not trusted to the client.

### The Flutter side: consuming the same contract, plus the local-cache mirror

This project's equivalent of a repository's local-cache pagination method, reading from on-device SQLite (not the network) for the normal case:

```dart
// products_repository.dart — this project's equivalent of a paginated
// local-cache read for a lazily-rendered list widget
Future<List<Product>> getPage({required int skip, required int take}) async {
  final db = await LocalDatabase.instance.database;
  final rows = await db.query(
    'products',
    where: 'isActive = 1',
    orderBy: 'createdAt DESC',
    limit: take,
    offset: skip,
  );
  return rows.map(_rowToProduct).toList();
}
```

### How the client tracks "is there more?" — and the real off-by-one subtlety in this codebase

The client does **not** issue a separate `COUNT(*)` query, and does **not** trust a `hasMore` flag from the server. It infers it from the page it just received, with the standard, cheap heuristic: **if the page came back full-sized, assume there might be another page; if it came back short, there is definitely nothing left.**

```dart
// items_page.dart / visible_page_loader.dart
hasMore = next.length == pageSize;
```

The genuinely instructive part is a real bug this project hit and fixed: a purely client-side "hidden" concept (rows the user bulk-deleted, tombstoned locally) is invisible to the SQL-level `skip`/`take` query — a page can come back non-empty in SQL terms yet be **entirely** hidden once filtered client-side, especially right after bulk-deleting a whole page's worth of rows at once. A naive implementation that fetches one page, filters out hidden rows, and stops if the *filtered* result is empty will silently render "nothing" and never try the next page again — even though real, visible rows exist two pages further in. The fix, preserved in a real code comment because the reasoning isn't obvious from the code alone:

```dart
/// This keeps fetching forward until a FULL page's worth of visible rows
/// has accumulated, or there is genuinely nothing left ... Stopping at the
/// first visible row (an earlier version of this function did) isn't
/// enough: the caller's list only becomes scrollable - and only a scroll
/// ever triggers loading another page - once it's actually long enough to
/// overflow the viewport. A batch with just one or two survivors after a
/// big bulk-hide renders too short to scroll, silently stranding
/// `hasMore: true` forever with nothing left to ever check it again.
Future<({List<T> items, bool hasMore})> loadVisiblePage<T>({
  required Future<List<T>> Function({required int skip, required int take}) fetchPage,
  required bool Function(T item) isHidden,
  required int skip,
  required int pageSize,
}) async {
  var items = <T>[];
  var hasMore = true;
  var visibleCount = 0;
  while (hasMore && visibleCount < pageSize) {
    final next = await fetchPage(skip: skip + items.length, take: pageSize);
    items = [...items, ...next];
    visibleCount += next.where((item) => !isHidden(item)).length;
    hasMore = next.length == pageSize;
  }
  return (items: items, hasMore: hasMore);
}
```

**The general, portable lesson:** whenever "how many rows exist" is answered by two different, independently-evolving definitions — a SQL-level filter (`skip`/`take` over the raw table) and an application-level filter layered on top (a soft-hide, a permissions filter, a client-only exclusion list) — your "did I get a full page" check must be evaluated **after** the application-level filter, and your loop must be willing to fetch multiple raw pages to fill one logical page. Checking "is the raw page non-empty" is not the same question as "is there a full page of what the user should actually see," and conflating them produces exactly this kind of silent, hard-to-reproduce dead end.

---

## 5. PostgreSQL numeric precision choices

Every money-related column in this schema — `Order.Total`, `Order.Discount`, `Order.CashDiscount`, `Order.PaidAmount`, `OrderItem.UnitPrice`, `Product.PurchasePrice`, `Product.SellPrice`, `Receipt.Amount` — is mapped to Postgres's **unconstrained `numeric`** type, confirmed directly in the migration snapshot (`.HasColumnType("numeric")`, with no precision/scale arguments anywhere in the schema). None of them use `decimal(18,2)` or any other fixed precision/scale.

**Why unconstrained `numeric` and not a fixed `decimal(p,s)`:** a fixed scale (e.g. `decimal(18,2)`) silently *truncates or rounds* any value with more decimal digits than the declared scale allows, at the database layer, on write — invisibly, with no exception, no warning, just a smaller number landing in the row than the application thought it was persisting. In a domain like invoicing where a per-unit price genuinely might need three decimal places (this codebase's own commit history references removing a bug class it calls "0.5 rounding" / fixing "unit-price silent truncation"), an unconstrained `numeric` column is the only way to guarantee the database is never the thing quietly losing precision the application layer computed carefully. `numeric` in Postgres stores exact, arbitrary-precision values (unlike `float`/`double precision`, which is why none of these columns use `double precision` either — that reintroduces binary floating-point rounding error on money, the classic "$19.99 stored as 19.990000000000002" class of bug), and paying whatever marginal storage/comparison cost it carries over a fixed-scale column is the correct trade for financial correctness.

**The database-side rounding-function verification.** When `Order.Total` was converted from a rounded computed property to a stored column (Section 3), the historical rows had to be backfilled with *exactly* the value they already displayed under the old rounding rule — not the new unrounded one — so an already-issued invoice's total could never silently change the moment the migration ran. That meant reproducing C#'s `Math.Round(x, MidpointRounding.AwayFromZero)`-based half-unit rounding as a single SQL `UPDATE`, and the migration author had to confirm Postgres's own `ROUND()` function resolves a midpoint (`x.5`) the same direction the application layer did:

```sql
-- 20260816162146_AddOrderStoredTotal.cs, Migration.Up()
-- Backfill every existing row using the OLD formula (rounded to the
-- nearest half-unit, MidpointRounding.AwayFromZero - Postgres numeric
-- ROUND() rounds half away from zero too, matching it exactly)
UPDATE "Orders" o
SET "Total" = ROUND(
    (
        COALESCE((SELECT SUM(oi."UnitPrice" * oi."Quantity") FROM "OrderItems" oi WHERE oi."OrderId" = o."Id"), 0)
        * (1 - o."Discount" / 100)
        - o."CashDiscount"
    ) * 2, 0
) / 2;
```

The general, portable lesson: **`ROUND()` on Postgres's `numeric` type rounds half-away-from-zero** (matching .NET's `MidpointRounding.AwayFromZero`, *not* .NET's `decimal`-default banker's rounding `MidpointRounding.ToEven`) — but this is exactly the kind of cross-system-behavior assumption that must be verified against real values before a backfill migration ships, never assumed. Whenever a data migration needs to reproduce an application-layer numeric formula as raw SQL, treat the two engines' rounding/truncation semantics as unverified until you've checked them against a handful of known midpoint cases, because "the database's math function behaves like the language's math function" is not a safe default assumption — it happened to hold here, and the code comment records that it was checked, not assumed.

---

## 6. Caching strategy

### What this project deliberately does **not** have

There is no Redis, no `IDistributedCache`, no `IMemoryCache`, no ASP.NET Core response caching, no output caching middleware anywhere in the backend (`Fatora.API/Program.cs` registers none of it). Every read hits PostgreSQL directly, every time. This is a deliberate omission, not an oversight — see Section 9 for why it's the right level of engineering for this app's actual read pattern.

### What it has instead: a local, in-memory "go check again" signal on the client

The real source of truth for anything the UI displays is **already local** — the Flutter client is offline-first, reading exclusively from on-device SQLite (`LocalDatabase`), never directly from the network for a normal screen render:

```dart
// local_database.dart
/// Every read in the app comes from here first, never from the network
/// directly — the server is a background sync target, not a dependency
/// for showing data.
```

Given that, a distributed server-side cache would be solving a problem this app doesn't have (repeated identical network reads) while missing the problem it actually has: **one part of the app changes local data, and other already-open screens showing that same data have no way to know it's now stale**, since there's no reactive query layer (no Room-style `Flow`, no reactive SQL observers) wired into raw `sqflite` calls. The fix is the cheapest possible thing that solves exactly that: a process-local `ValueNotifier<int>` version counter per aggregate, bumped on any write, that already-open widgets subscribe to.

```dart
// products_cache_signal.dart
/// Bumped whenever something outside the Items page could change what it
/// shows - specifically, a sale decrementing tracked stock ... [ItemsPage]
/// listens for this to know its already-loaded list is stale ... a purely
/// local notification, so it fires (and the page refreshes) regardless of
/// connectivity.
class ProductsCacheSignal {
  ProductsCacheSignal._();
  static final instance = ProductsCacheSignal._();

  final version = ValueNotifier<int>(0);

  void invalidate() => version.value++;
}
```

```dart
// orders_cache_signal.dart — identical shape, different aggregate
/// Bumped whenever an order changes anywhere - created, edited, a payment
/// recorded, deleted, settled ..., or a background sync pull brings in a
/// change from another device. [InvoicesPage] and a customer's own invoice
/// history ... listen for this ... without the user having to leave and
/// reopen the page.
class OrdersCacheSignal { /* same shape */ }
```

Every mutating repository call (`create`, `update`, `delete`, and the background sync pull that merges in another device's changes) calls `.invalidate()` on the relevant signal after committing to SQLite; every screen that displays that aggregate wraps itself (or the relevant subtree) in a listener on `.version` and silently re-runs its local SQLite query when it fires — no spinner, no visible reload, because the read is already local and cheap.

### Why this is the right fit — not a "cache," an invalidation *signal*

The critical distinction: **this is not a cache** in the conventional sense (it stores no data, has no TTL, no eviction policy, no serialization). It is a pure invalidation *signal* layered on top of a data store (SQLite) that is already the fast, authoritative, local source of truth. That's the correct shape specifically because:

- The thing that's slow/networked (the Postgres backend) is never in the read path for a normal screen at all — sync is a background push/pull, not a per-render dependency — so there is nothing on the hot path for a cache to speed up.
- The thing that *does* need solving — "did something else just change what I'm showing" — is a **notification** problem, not a **data-fetching** problem, and a `ValueNotifier` is the minimal tool that solves exactly that, with zero new infrastructure, zero serialization format, and trivial testability (it's a plain integer).
- A heavier cache (in-memory LRU, or worse, a server-side distributed cache) would add invalidation complexity — cache-key design, TTL tuning, stampede handling — to solve a problem (slow repeated reads) that doesn't exist here, while doing nothing for the actual problem (stale in-memory widget state), which the signal solves directly.

**General, portable rule:** if your application's real source of truth for reads is already fast and local (embedded SQLite/Room/CoreData, or even an already-loaded in-memory object graph), don't reach for a cache — reach for an **invalidation signal**: something cheap and process-local that already-rendered views can subscribe to, to know when to silently re-run their already-fast local query. Reach for an actual cache (with TTL/eviction/keying concerns) only when the thing being repeatedly read is itself slow or remote — which is a different problem with a different, heavier correct answer (see Section 9).

---

## 7. N+1 avoidance in bulk operations

The general shape to copy: **whenever you need to know something about "many of X, given many IDs" before proceeding, issue one query with `Contains`/`IN`, never a query-per-ID inside a loop.** Three real examples from this codebase, in increasing order of subtlety.

### 7.1 — Bulk existence + ownership check before a batch write

This project's equivalent of "validate that every line item in this batch operation references a product this tenant actually owns, in one query":

```csharp
// SyncService.LoadOwnedProducts — resolves every product referenced by an
// incoming batch of order line items in a single round trip, not one
// query per line item
private async Task<Dictionary<Guid, Product>?> LoadOwnedProducts(
    Guid userId, List<OrderItemSyncItem> items, Guid? scopeToRepId)
{
    var productIds = items.Select(i => i.ProductId).Distinct().ToList();

    var query = dbContext.Products.Where(p => productIds.Contains(p.Id) && p.UserId == userId);

    if (scopeToRepId is { } repId)
    {
        var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == repId);
        if (rep is { ProductAccessMode: ProductAccessMode.RepOwnedOnly })
        {
            query = query.Where(p => p.CreatedByRepId == repId);
        }
        // ... other access-mode branches, same query, more filters
    }

    var products = await query.ToListAsync();

    // Distinct() up front means Count comparison is a valid
    // "did every referenced id resolve" check, not an approximation.
    return products.Count == productIds.Count ? products.ToDictionary(p => p.Id) : null;
}
```

Notice the `.Distinct()` on the id list before the query — without it, a batch containing the same product twice would inflate `productIds.Count` past what a correct, deduplicated `products.Count` could ever match, breaking the "did everything resolve" check. This method processes an entire incoming sync batch (which can contain dozens of order line items) with exactly one `SELECT ... WHERE Id IN (...)` query, rather than one `FirstOrDefaultAsync` per line item — the difference between O(1) and O(n) round-trips against the exact same data.

### 7.2 — Single existence check before a batch replace ("is this already dirty")

This project's equivalent of "before I overwrite this row's cached copy from a bulk server pull, check whether it has a local pending write I must not clobber" — done for the *whole incoming batch at once*, not per row:

```csharp
// products_repository.dart — _replaceCache, called once per full-catalog
// pull, for potentially hundreds of rows
Future<void> _replaceCache(List<Product> products) async {
  final db = await LocalDatabase.instance.database;
  await db.transaction((txn) async {
    await txn.delete('products', where: 'dirty = 0');

    // One query for every locally-dirty id, not one query per incoming
    // product — this is the batch-existence-check pattern applied on the
    // SQLite side of the stack, not just the EF side.
    final dirtyIds = (await txn.query(
      'products',
      columns: ['id'],
      where: 'dirty = 1',
    )).map((row) => row['id'] as String).toSet();

    final batch = txn.batch();
    for (final product in products) {
      if (dirtyIds.contains(product.id)) continue;   // in-memory Set lookup, O(1)
      batch.insert('products', _productToRow(product, dirty: false),
          conflictAlgorithm: ConflictAlgorithm.replace);
    }
    await batch.commit(noResult: true);
  });
}
```

One `SELECT id FROM products WHERE dirty = 1` populates an in-memory `Set<String>`, and every one of the (potentially hundreds of) incoming rows is checked against that set with an O(1) lookup — instead of a `SELECT ... WHERE id = ? AND dirty = 1` issued once per incoming row. The batched `sqflite` `Batch` object for the inserts themselves is the same idea applied to the write side: one prepared-statement batch commit, not N individual `insert()` calls each paying their own transaction overhead.

### 7.3 — Barcode-uniqueness check, scoped correctly to avoid a false positive

A smaller but real instance of the same discipline — checking "is this value already taken by *someone else*" with one query that excludes the current row, rather than fetching a candidate and comparing in application code:

```csharp
// SyncService.ResolveSyncBarcodeAsync
var taken = await dbContext.Products
    .AnyAsync(p => p.UserId == userId && p.Barcode == barcode && p.Id != productId);
```

`AnyAsync` here also matters for a second reason beyond N+1: it translates to `EXISTS (...)`/a bounded existence check at the database layer, rather than pulling a full row back to check whether it's non-null in .NET — the smallest, cheapest form of "does at least one match exist."

---

## 8. Migration safety for large/live tables

### The rule: `Down()` is always implemented — including when it's a deliberate no-op

Every single migration file in this codebase's history (55+ migrations) implements `Down()`. When a migration performs a genuinely irreversible data operation (data loss on `Up()` that cannot be reconstructed), `Down()` is still present, but as an intentionally empty method **with a comment explaining why it's empty**, never silently omitted:

```csharp
// FloorNegativeProductStock.cs
protected override void Down(MigrationBuilder migrationBuilder)
{
    // Irreversible by design - the original negative values aren't
    // recoverable, and reintroducing them would defeat the point.
}
```

```csharp
// ClearLocalDiskImageUrls.cs — same discipline, different reason
protected override void Down(MigrationBuilder migrationBuilder)
{
    // Irreversible by design - the original local-disk paths aren't
    // meaningful to restore, since those files no longer exist.
}
```

The rule to copy: **never leave `Down()` empty by omission.** If it's genuinely a one-way door, say so explicitly in the method body, so the next engineer who has to roll back a deploy knows immediately (from the migration file itself, without git-archaeology) that this particular step can't be undone and needs a different remediation plan — instead of discovering it only when `dotnet ef database update <previous>` silently does nothing for that step.

### The additive-column-with-backfill pattern

For a schema change that also needs existing rows populated, the pattern used throughout this codebase is a **single migration** that (a) adds the column with a database-computed default, (b) lets every existing row get that default applied inline by Postgres itself, then (c) drops the default so it doesn't linger as implicit behavior for future inserts (which should always set the column explicitly in application code):

```csharp
// AddCreatedAtToUser.cs
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Existing users never had a real signup date recorded - backfill to
    // "now" since there's no historical value to recover
    migrationBuilder.AddColumn<DateTime>(
        name: "CreatedAt", table: "Users", type: "timestamp with time zone",
        nullable: false, defaultValueSql: "now()");

    migrationBuilder.Sql(@"ALTER TABLE ""Users"" ALTER COLUMN ""CreatedAt"" DROP DEFAULT;");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(name: "CreatedAt", table: "Users");
}
```

This project never uses the more cautious three-phase "add nullable → backfill in a separate deploy → tighten to `NOT NULL` in a third deploy" approach — because at this application's actual scale (single-digit-thousands of rows per table, a maintenance window that tolerates a few hundred milliseconds of table lock), a single-migration `AddColumn` with `defaultValueSql` executed as one transaction is fast enough that the extra deploy phases would be pure process overhead, not a real safety improvement. **Section 9 names the row-count/lock-duration signal that would flip this recommendation** — the three-phase approach exists precisely for tables large enough that rewriting every row to add a `NOT NULL` column takes long enough to matter under production load, which this project's tables never approach.

For a schema change that needs a **computed, formula-driven backfill** (not just a constant default), the pattern is the same shape with a hand-written `UPDATE ... SET` in place of `defaultValueSql` — see the `AddOrderStoredTotal` migration in Section 3/5, which backfills every row with a value computed from its *own* related `OrderItems`, `Discount`, and `CashDiscount`, using the exact old formula, so no historical row's displayed value moves even a cent.

### Data-shape migrations that also carry a real, comment-justified data transform

One migration renames an enum value already persisted as a string (`HasConversion<string>()`) across every existing row, and also **backfills a new join-table row set** implied by the semantics change, entirely inside the migration rather than deferring it to application code on next read:

```sql
-- RedesignProductAccessMode.cs, Up()
UPDATE "Reps" SET "ProductAccessMode" = 'Selected' WHERE "ProductAccessMode" = 'Restricted';

INSERT INTO "RepProductAccesses" ("RepId", "ProductId")
SELECT p."CreatedByRepId", p."Id"
FROM "Products" p
JOIN "Reps" r ON r."Id" = p."CreatedByRepId"
WHERE p."CreatedByRepId" IS NOT NULL
  AND r."ProductAccessMode" = 'Selected'
  AND NOT EXISTS (
      SELECT 1 FROM "RepProductAccesses" rpa
      WHERE rpa."RepId" = p."CreatedByRepId" AND rpa."ProductId" = p."Id"
  );
```

The `NOT EXISTS` guard makes this migration idempotent-safe against being (accidentally) run twice, and the accompanying `Down()` deliberately does **not** try to fully reverse the `INSERT` — it reverts the enum rename, but leaves the backfilled access-grant rows in place, with a comment explaining that reversing them would require guessing which grants were backfilled versus deliberately made by a user through the UI in the meantime, which isn't reliably knowable. **Lesson: a `Down()` doesn't have to reverse every side effect of `Up()` to be a legitimate, honest `Down()` — it has to reverse what's safely reversible, and say explicitly, in a comment, what it's deliberately leaving alone and why.**

---

## 9. What "fast enough" looks like here, and why heavier tools weren't needed

### The actual shape of this application's read/write load

- **Offline-first, local-read-first**: the client's primary data source for every screen render is on-device SQLite, not the network — the backend is a sync target hit in the background, not a per-render dependency. This alone removes the single biggest reason most apps reach for a cache (repeated, latency-sensitive reads of the same remote data).
- **Row counts are bounded per tenant, not global**: every hot query (`GetPagedAsync`, `GetSummaryAsync`, the sync delta pull) filters by `UserId` (the tenant) first. A single business's invoice/product/customer counts are realistically in the hundreds to low thousands, not millions — so even an imperfectly-indexed sort (Section 1's `CreatedAt` gap) stays cheap, because Postgres is sorting a few hundred already-filtered rows, not the whole table.
- **Writes are low-concurrency per tenant**: one business's staff (owner plus a handful of sales reps) writing to that business's own rows. There's no cross-tenant write contention, and no single row is being hammered by many concurrent writers the way a global counter or a shared inventory row in a high-traffic multi-tenant SaaS might be.
- **Reporting queries stay bounded by the same tenant + date-range filter** before any aggregation happens in memory (Section 3) — they never aggregate across the whole table.

### Why "indexes + AutoInclude + simple offset pagination + no cache layer" is the right amount of engineering, not under-engineering

Every technique in this document is the cheapest tool that correctly solves the actual problem observed in this codebase's own history:

- Composite `(TenantId, timestamp)` indexes exist because a specific, measured, repeatedly-executed query pattern (delta sync) needed one — not speculatively, on every column that theoretically could be filtered.
- AutoInclude exists on exactly the navigations that are *never* legitimately loaded without their parent, eliminating a real, previously-shipped-and-fixed class of N+1 bug, without paying a blanket eager-load cost on every navigation everywhere.
- Offset pagination with a server-side `Math.Clamp` ceiling is enough because no client here needs to page through a genuinely enormous, constantly-mutating result set where offset drift (rows shifting between page fetches) would visibly corrupt the user's experience — that's the specific failure mode cursor-based pagination exists to solve, and it isn't this application's failure mode today.
- No cache layer exists because the thing a cache speeds up (a slow, remote data source on the hot read path) isn't in this application's hot read path at all — the hot read path is already local SQLite, and the actual problem (stale in-memory UI state after a local write) is solved more cheaply and more correctly by an invalidation signal than by a cache.
- Computed properties stay as plain C# getters everywhere they're safe to (Section 3's rule), and were converted to a real stored column in the one specific case where a genuine correctness requirement (frozen historical invoices) — not a performance concern — demanded it.

### The concrete signals that would mean this project has outgrown this approach

Any one of these, observed for real (via `EXPLAIN ANALYZE`, production latency metrics, or an actual incident) — not guessed at in advance — is the trigger to add the next tier of tooling, and each maps to a specific upgrade:

1. **A single tenant's row count in a hot table (orders, products) grows past what an index-narrowed-but-unindexed-sort can handle in memory cheaply.** Signal: `GetPagedAsync`'s `OrderByDescending(CreatedAt)` shows up as a non-trivial sort step in a query plan. Fix: add the missing `(UserId, CreatedAt)` composite index called out as a gap in Section 1 — a schema change, not an architecture change.
2. **The same expensive read (a report, a dashboard aggregate) is requested by many different users/requests within a short window, and recomputing it every time is measurably expensive.** Signal: repeated identical (or near-identical) query plans hitting the database for data that changes far less often than it's read. Fix: *now* introduce `IMemoryCache` (single-instance backend) or `IDistributedCache`/Redis (multi-instance backend) — but scoped narrowly, to that specific expensive read, with an explicit TTL or invalidation trigger tied to the write that would make it stale. Don't cache everything; cache the one thing that's actually hot.
3. **The backend is horizontally scaled to multiple instances, and per-instance `IMemoryCache` would serve different, inconsistent cached values to different requests hitting different instances.** Signal: you've deployed a second backend instance for the first time. Fix: this is the point an in-process cache stops being sufficient and a shared, distributed cache (Redis) becomes necessary — not before.
4. **A single query needs to aggregate across *all* tenants, not one tenant at a time** (a genuine platform-wide analytics/admin query, as opposed to this codebase's existing admin reports, which still scope to one subject at a time). Signal: an admin dashboard query's `WHERE` clause stops having a tenant-scoping predicate at all. Fix: this is when a dedicated reporting/read-replica database, or a pre-aggregated summary table refreshed on a schedule, starts being worth the operational cost — running that query against the same OLTP primary that's serving live tenant writes is the point offset pagination and per-tenant indexing genuinely stop being enough.
5. **Offset pagination's UX failure mode actually surfaces**: a user scrolling a list while rows are being concurrently inserted/deleted by someone else notices duplicated or skipped rows across page loads. Signal: a real, reported UX bug — not a hypothetical. Fix: migrate that specific list to cursor-based (keyset) pagination (`WHERE (CreatedAt, Id) < (@lastCreatedAt, @lastId) ORDER BY CreatedAt DESC, Id DESC LIMIT @take`), which is immune to offset drift — but only for the list(s) that actually exhibit the problem, not a wholesale rewrite of every paginated endpoint pre-emptively.

The unifying principle behind all five: **every one of these upgrades is triggered by an observed, specific bottleneck or bug, never adopted speculatively "because a bigger app would need it."** That's the actual pattern worth copying — not "always add indexes/caching/cursor pagination," but the discipline of matching the tool to a measured problem, exactly as this codebase's own `git log` shows it doing (the `Order.Total` migration exists because of a real precision bug, not a hypothetical one; the `loadVisiblePage` retry loop exists because of a real bulk-delete UX bug, not a hypothetical one).
