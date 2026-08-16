# Pattern: Multi-Tier Roles, QR-Paired Sub-Accounts, and Scoped Permissions

## What this pattern is for

Any multi-tenant B2B app eventually needs three things that are easy to conflate but should be
modeled as three separate concerns:

1. **A coarse platform role** (who am I at the highest level — platform operator vs. paying
   tenant).
2. **Sub-accounts under a tenant** (a tenant's own staff/employees who need to log in and do
   real work, but must never be full tenant accounts with a username/password of their own).
3. **Fine-grained data scoping** for those sub-accounts (does this specific employee see
   everything the tenant owns, or only a curated subset — and is that curation per-employee or
   per-resource-type).

This document describes a concrete, shipped implementation of that pattern — a .NET 10 /
EF Core / PostgreSQL backend (`Fatora.API` / `Fatora.BL` / `Fatora.DAL`) paired with a Flutter
frontend — extracted from a real invoicing app. The business nouns (Rep, Owner, Product,
Customer) are illustrative; the mechanism generalizes to any "tenant + tenant's own staff"
domain (a clinic with front-desk staff, a warehouse with pickers, a store with cashiers).

The single most important design decision in this whole feature: **a sub-account is not a
`User` row with a role.** It is a completely separate entity (`Rep`) with its own auth
mechanism (QR-token bearer login, not username/password), its own JWT claim shape, its own
session-management table, and its own authorization gate. The alternative — giving the `User`
table a `Role` enum with values like `Owner`, `Employee` — was rejected because it conflates
"which platform tier is this account" with "is this a real login-with-password principal or a
device-paired sub-identity issued by someone else," two orthogonal questions that need
different data shapes, different revocation semantics, and different authentication endpoints.
Below, every section explains where that separation shows up and why it was worth the extra
entity.

---

## 1. The role hierarchy

There are actually **two independent hierarchies layered on top of each other**, and confusing
them is the single easiest mistake when adopting this pattern.

### 1a. The platform-tier `Role` enum (on the tenant's own `User` row)

```csharp
// this project's equivalent of Fatora.DAL/Entites/User.cs
public enum Role
{
    Admin,
    SalesRep   // misleadingly named: this is the tenant/business-owner tier, not a sub-account
}

public class User
{
    public Role Role { get; set; } = Role.SalesRep;
    ...
}
```

- `Admin` — exactly one seeded platform operator account (see `SeedData.seedAuthDataAysnc`,
  which refuses to boot without an `AdminSeed__Password` env var). Manages every tenant,
  has no subscription concept, is exempt from every account-status check.
- `SalesRep` — a normal, self-registering **tenant** (a shop owner). Despite the name, this is
  not a sub-account at all — it is the top of a business's own data tree. Every `Product`,
  `Customer`, `Order` in the system is ultimately owned (`UserId`) by a `SalesRep`-tier `User`.

This enum is deliberately tiny (two values) because it only distinguishes "the platform" from
"a tenant." Anything narrower than that (a tenant's own staff) is **not** added as a third enum
value — see 1b.

### 1b. Sub-account roles, expressed as JWT role-claim strings, not enum values

Two more principal kinds exist, each with its own entity and its own literal string used only
as the ASP.NET Core `ClaimTypes.Role` value (`"role"` claim, per `RoleClaimType = "role"` in
`Program.cs`'s JWT bearer options):

| Claim role  | Entity      | Belongs to                                   | Purpose |
|-------------|-------------|-----------------------------------------------|---------|
| `"Rep"`     | `Rep`       | one `SalesRep`-tier `User` (`OwnerUserId`)    | a tenant's own field/sales staff |
| `"SubAdmin"`| `SubAdmin`  | the platform directly (no owner FK — there is only one Admin) | the Admin's own delegated staff |
| `"RepPendingSync"` | *(no entity — a narrow-purpose short-lived token)* | — | see §7 |

Why not fold these into the `Role` enum too? Because `Rep`/`SubAdmin` are not alternate values
of "what kind of `User` is this" — a `Rep` **is not a `User` row at all**. It has no
`UserName`/`Password`, no subscription, no independent existence outside its owner. Modeling it
as a `User` with `Role.Rep` would force nullable-everything on `User` (no password, no
subscription dates) and would make "is this session even allowed to exist independently of its
owner" a runtime check instead of a schema fact. Two dedicated tables with narrow, honest
columns were chosen over one wide table with a role-dependent meaning per column.

### 1c. Authorization wiring

Every controller declares its **broadest allowed role set** at the class level, then narrows
per action — deliberately relying on ASP.NET Core's behavior that multiple stacked
`[Authorize]` attributes are ANDed, so a class-level restriction can never be *widened* by a
looser method-level one:

```csharp
// this project's equivalent of Fatora.API/Controllers/RepsController.cs
[Route("api/reps")]
[Authorize(Roles = "SalesRep,Rep")]     // broadest: either principal can call something here
public class RepsController(...) : ControllerBase
{
    [Authorize(Roles = "SalesRep")]     // narrows: only the owner may create a sub-account
    [HttpPost]
    public async Task<IActionResult> Create(CreateRepRequest request) { ... }

    [Authorize(Roles = "Rep")]          // narrows the other way: only the sub-account itself
    [HttpGet("me")]
    public async Task<IActionResult> GetMyInfo() { ... }
}
```

The same shape repeats for `SubAdminsController` (`"Admin,SubAdmin"` at class level, narrowed
per action) and `UsersController`. **Recommendation for a new project:** adopt this "declare
the union, narrow per action" convention from day one — it makes the authorization surface of
an entire controller readable from its top three lines, and it makes "can a sub-account ever
reach this action" a compile-adjacent question instead of something you have to trace through
service-layer `if`s.

### 1d. `IsSalesManager` — a third, independent axis

One more boolean worth calling out because it is easy to conflate with the role hierarchy but
isn't part of it:

```csharp
// User.cs
public bool IsSalesManager { get; set; } = false;
```

This is a tenant opting into the sub-account feature at all (`AccountController.EnableSalesManagerAsync`,
a one-way switch). It gates *UI visibility* of rep-management screens for a `SalesRep`-tier
owner — it is not a security boundary (a tenant with sub-accounts already created keeps working
even if this were somehow false; nothing server-side re-checks it on every request). Keep this
kind of "has this tenant opted into an optional module" flag clearly separate from your actual
authorization checks — it answers "should we show the button," not "is this request allowed."

---

## 2. How a sub-account is created

**Who:** Only the owning tenant itself (`[Authorize(Roles = "SalesRep")]`), scoped to its own
data by `User.GetUserId()` from the JWT `sub` claim — never another tenant, never a `Rep`
session. The mirror endpoint for the platform tier, `SubAdminsController.Create`, is
`[Authorize(Roles = "Admin")]` only.

**Required fields — deliberately minimal:**

```csharp
// this project's equivalent of Fatora.BL/DTOs/Requests/CreateRepRequest.cs
public class CreateRepRequest
{
    public string Name { get; set; } = string.Empty;
}
```

That's it. No username, no password, no email — because a sub-account never authenticates with
credentials (see §6). This is the clearest signal that this is a genuinely different kind of
principal, not "a `User` with fewer fields filled in."

**Server-side (`RepService.CreateAsync`):**

```csharp
public async Task<RepResponse> CreateAsync(Guid ownerUserId, CreateRepRequest request)
{
    var rep = new Rep
    {
        OwnerUserId = ownerUserId,
        Name = request.Name,
        QrToken = GenerateQrToken(),      // Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        CreatedAt = DateTime.UtcNow
    };
    dbContext.Reps.Add(rep);
    await dbContext.SaveChangesAsync();
    return ToResponse(rep, includeQrToken: true);   // only creation/regenerate/reactivate expose the raw token
}
```

A brand-new `Rep` row, FK'd to the owner, with a cryptographically random bearer token
generated immediately (there is no separate "activate" step — the QR is live the instant the
row exists) and sane defaults: `ProductAccessMode = All`, `CustomerAccessMode = Own`,
`CanEditStock = true`. This matters for the "principle of least surprise" on an existing
system: adding sub-accounts to a system that never had them should default every existing
tenant behavior to unchanged (see §3's comment on why `Own` — not `Restricted` — is
`CustomerAccessMode`'s default).

**The exact authorization check gating this endpoint** is the combination of
`[Authorize(Roles = "SalesRep")]` at the ASP.NET Core pipeline level (rejects the request before
the controller action even runs if the caller isn't tenant-tier) plus every subsequent
owner-scoped `Rep` operation re-deriving `ownerUserId` from `User.GetUserId()` server-side and
filtering by it (`FindOwnedRep`, below) — the client never gets to say which owner a `Rep`
belongs to, only the token does.

```csharp
private async Task<Rep> FindOwnedRep(Guid ownerUserId, Guid repId)
{
    var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == repId && r.OwnerUserId == ownerUserId);
    if (rep is null) throw new NotFoundException(nameof(Rep), repId);
    return rep;
}
```

Every mutating action on `RepsController` (`Logout`, `Deactivate`, `Delete`, `Reactivate`,
`RegenerateQr`, `SetProductAccess`, `SetCustomerAccess`, `SetStockAccess`) routes through this
same helper — returning **404, not 403**, when the `repId` exists but belongs to a different
owner, so the endpoint doesn't leak the existence of other tenants' sub-accounts.

Lifecycle is one-way and layered, not a single "delete" action:

- `LogoutAsync` — bumps `SessionVersion` only (see §6); the row and its history are untouched.
- `DeactivateAsync` — `IsActive = false` + `SessionVersion++`; blocks future login, keeps the
  row visible in management lists, reversible via `ReactivateAsync`.
- `DeleteAsync` — a **superset** of deactivate: also sets `IsHidden = true`. Hides the row from
  the default management list (`GetAllAsync(includeHidden: false)`) without touching a single
  `Order`/`Customer`/`Product`/`Receipt` the sub-account ever created — those keep resolving and
  stay filterable via `includeHidden: true`. **Deliberately no "undelete."** The row itself is
  never physically removed until a separate periodic sweep
  (`AnnualInventoryService`'s post-wipe pass) finds literally nothing left pointing at it.

This "soft states stack, hard delete is a background reaper, never a synchronous cascade" shape
is worth copying independent of the roles feature — it is what lets you safely let a sub-account
"go" without a data-integrity landmine on every table that has a `CreatedBy` FK to it.

---

## 3. Permission scoping — All vs. Restricted (and friends)

Every scoped resource has an access-mode enum on the `Rep` entity, and the **enforcement lives
in exactly one place per resource type: a private `ApplyRepScopeAsync` (list) /
`IsVisibleToRepAsync` (single-row) pair inside that resource's own service class** — never
repeated ad hoc per controller action.

### 3a. The two independent access-mode enums

```csharp
// this project's equivalent of Fatora.DAL/Entites/Rep.cs
public enum ProductAccessMode { RepOwnedOnly, Selected, All }
public enum CustomerAccessMode { Own, All, Restricted }
```

These are **not the same shape**, and the code comments explain exactly why: `Product` has no
inherent "belongs to the rep" default worth defaulting to (a product catalog is naturally
shared), so its narrowest mode (`RepOwnedOnly`) is opt-in scaffolding for a rep building its own
mini-catalog. `Customer` has real historical meaning for "my own" — every rep's actual behavior
*before* shared/restricted modes existed was "only customers I personally added" — so
`CustomerAccessMode.Own` is the default, preserving old behavior exactly for every
already-existing sub-account when the feature was extended. **Lesson: don't force every scoped
resource type into one shared enum just for symmetry — model each one's actual default/history
honestly, even if that means two similarly-shaped-but-different enums.**

The three `ProductAccessMode` values are **mutually exclusive, never layered** — `Selected`
does not also implicitly include what the rep personally created, because a `Selected`-mode rep
is structurally barred from creating products at all (enforced in `ProductService.CreateAsync`,
not just hidden in the UI):

```csharp
if (createdByRepId is not null)
{
    var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == createdByRepId);
    if (rep is null || rep.ProductAccessMode == ProductAccessMode.Selected)
        throw new ForbiddenException("لا يمكن إنشاء أصناف في وضع الأصناف المحددة.");
}
```

`CustomerAccessMode.Restricted`, by contrast, **is** additive — a restricted rep sees its
admin-granted subset *plus* whatever it created itself, because a customer it personally
onboarded should never become invisible to the person who onboarded it.

### 3b. The grant itself: a join table, one row per (sub-account, resource)

```csharp
// this project's equivalent of Fatora.DAL/Entites/RepProductAccess.cs
public class RepProductAccess
{
    public Guid RepId { get; set; }
    public Guid ProductId { get; set; }
}
// composite primary key (RepId, ProductId) — see RepProductAccessConfiguration
```

`RepCustomerAccess` is the identical shape for customers. A join table was chosen over a
JSON/CSV column of ids on `Rep` itself for the ordinary relational reasons (indexable,
FK-enforced, trivially diffable via `IQueryable.Contains`) — and, concretely, because the
"set access" endpoint always replaces the whole set atomically:

```csharp
// this project's equivalent of RepService.SetProductAccessAsync
var existing = await dbContext.RepProductAccesses.Where(a => a.RepId == repId).ToListAsync();
dbContext.RepProductAccesses.RemoveRange(existing);
rep.ProductAccessMode = request.Mode;
if (request.Mode == ProductAccessMode.Selected)
{
    var ownedIds = await dbContext.Products
        .Where(p => p.UserId == ownerUserId && requestedIds.Contains(p.Id))
        .Select(p => p.Id).ToListAsync();     // silently drop ids that aren't actually the owner's
    dbContext.RepProductAccesses.AddRange(ownedIds.Select(id => new RepProductAccess { RepId = repId, ProductId = id }));
}
```

Rows are **cleared whenever the mode changes**, even into a mode that ignores them (`All`,
`RepOwnedOnly`) — so a mode switch never leaves orphaned grants a future "switch back" would
silently resurrect.

### 3c. Enforcement: one shared method per resource, called by every read path

This is the answer to "shared scope-filter method vs. repeated per-endpoint checks" — the
project uses **one method pair per resource-owning service**, never inline checks scattered
across controllers:

```csharp
// this project's equivalent of Fatora.BL/Services/Classes/ProductService.cs
private async Task<IQueryable<Product>> ApplyRepScopeAsync(IQueryable<Product> query, Guid? scopeToRepId)
{
    if (scopeToRepId is null) return query;                       // the owner: never filtered
    var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == scopeToRepId);
    if (rep is null || rep.ProductAccessMode == ProductAccessMode.All) return query;
    if (rep.ProductAccessMode == ProductAccessMode.RepOwnedOnly)
        return query.Where(p => p.CreatedByRepId == scopeToRepId);
    var allowedIds = dbContext.RepProductAccesses.Where(a => a.RepId == scopeToRepId).Select(a => a.ProductId);
    return query.Where(p => allowedIds.Contains(p.Id));
}

private async Task<bool> IsVisibleToRepAsync(Guid productId, Guid? scopeToRepId) { /* same rule, single-row form */ }
```

Every list/get/update/delete method in `ProductService` — `GetAllAsync`, `GetPagedAsync`,
`GetByIdAsync`, `UpdateAsync`, `GetArchivedAsync` — funnels through one of these two methods.
`CustomerService` has the exact same pair, shaped for its own enum. `SyncService` (the offline
push/pull path) calls the **same** logic again rather than re-implementing it — e.g.
`SyncService.PushCustomerAsync` explicitly delegates the "did this rep create it" check to
`CustomerService.IsOwnRepCreation` (marked `internal`, not `private`, specifically so the sync
service can reuse it) instead of re-deriving the rule.

**Why one shared method instead of a repeated per-endpoint check:** a permission rule
(`Restricted` = granted-subset-plus-own; `Own` = only-what-I-created) is a business rule, not
UI plumbing, and it has exactly one correct definition. Every additional call site that
re-implements it (even "just this once, it's simple") is a second place that rule can drift out
of sync with the first the next time someone tweaks it — and this codebase has *two* write
paths per resource (the normal REST endpoint and the offline sync-push endpoint) that must
agree byte-for-byte on who can see/touch what, which is precisely the scenario where duplicated
logic silently diverges. A `Selected`-mode rep who could push through the sync path something
the REST path would have rejected is a real, exploitable bug class; centralizing the check
closes it structurally rather than by vigilance.

**How the controller supplies `scopeToRepId`:** it never trusts a client-sent rep id for a
`Rep`'s own session — it derives it from the JWT:

```csharp
// this project's equivalent of ProductsController.GetAll
[HttpGet]
public async Task<IActionResult> GetAll([FromQuery] int? skip, [FromQuery] int? take, [FromQuery] Guid? repId)
{
    var scopeToRepId = User.GetRepIdOrNull() ?? repId;   // a Rep session: always its own id, ignoring any repId query param
    ...
}
```

`repId` as a query parameter only ever takes effect for an **owner** session (letting the owner
preview "what does this specific rep actually see" from their own management UI) — for an
actual `Rep` session, `User.GetRepIdOrNull()` (populated from the JWT `sub` claim only when
`IsInRole("Rep")`) always wins, so a compromised or buggy client can never widen its own scope
by passing someone else's `repId`.

---

## 4. Per-capability permission flags — narrower than the access mode

Beyond "which rows can this sub-account see," there's a second, orthogonal axis: **within a row
it can already see and is allowed to touch, is there one specific field it may or may not
change.** Two real examples exist in this codebase, one per sub-account tier:

### 4a. `Rep.CanEditStock`

```csharp
public bool CanEditStock { get; set; } = true;
```

Independent of `ProductAccessMode` — that controls *which* products a rep can see/touch at all;
this controls one narrower thing *within* that: whether it may change a touchable product's
`StockQuantity`, or only view it. Enforced in **two** places that must agree (the normal REST
update and the offline sync-push), both re-deriving the same rule rather than trusting a flag
on the incoming request:

```csharp
// ProductService.UpdateAsync — a rep with CanEditStock=false editing a product it didn't create:
product.StockQuantity = request.StockQuantity;   // reached only via a separate "own vs. not own" branch
// ... and further down, even for a product the rep DID create:
if (scopeToRepId is null || request.StockQuantity == product.StockQuantity)
    product.StockQuantity = request.StockQuantity;
else
{
    callerRep ??= await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == scopeToRepId);
    if (callerRep is null || callerRep.CanEditStock)
        product.StockQuantity = request.StockQuantity;
    // else: the quantity change in the request is silently dropped, every OTHER field still saves
}
```

Note the shape: this is not "reject the whole write," it's "apply every field the caller is
allowed to change, and silently ignore the one it isn't" — a deliberate UX choice (a rep
editing a product's description shouldn't get a hard error just because it also, harmlessly,
resubmitted the same-looking quantity) matched by an equally deliberate one for a *changed*
quantity from a caller without the flag: also silently dropped, not rejected — because the
alternative (reject the whole update) would block the legitimate field edits over a permission
boundary on an unrelated field.

### 4b. `SubAdmin`'s four independent boolean flags

```csharp
// this project's equivalent of Fatora.DAL/Entites/SubAdmin.cs
public bool CanActivateMonthly { get; set; }
public bool CanActivateAnnual { get; set; }
public bool CanActivateCustomMonths { get; set; }
public bool CanManageAccount { get; set; }     // suspend / reactivate a subscriber
public bool CanResetPassword { get; set; }     // reset a subscriber's password
```

Each is checked by its own tiny `Ensure...Async` guard right before the action it gates
(`EnsureSubAdminCanActivateAsync`, `EnsureSubAdminCanManageAccountAsync`,
`EnsureSubAdminCanResetPasswordAsync`), called only when the caller is actually a `SubAdmin`
(`actingSubAdminId is not null`) — the top `Admin` bypasses every one of them unconditionally.
**Deliberately split into separate booleans rather than one combined "can manage" flag**, per
the code's own comment: *"the Admin must be able to grant one without the other."* This is the
general rule for capability flags — one boolean per independently-grantable action, resist the
urge to bundle two capabilities that are *usually* granted together into one flag, because the
day they need to be granted separately, a bundled flag is a breaking schema change instead of
just ticking a different box.

**General shape to copy:** a capability flag lives on the sub-account entity itself (not a
separate permissions table) when the set of capabilities is small, fixed, and known at compile
time — a join-table/claims model only earns its complexity when the capability set is large,
tenant-defined, or needs to be queried/audited independently of the entity that holds it. Here,
five booleans on `SubAdmin` and one on `Rep` were simpler and just as correct as a generic
`RepPermission { RepId, PermissionKey }` table would have been, with none of that table's extra
join cost on every request.

---

## 5. The QR feature — precisely

**Correcting the starting assumption:** this app does have an *unrelated* product-barcode
scanner (`BarcodeScannerPage` reused for the product form and for invoice line-item entry,
scanning UPC/EAN-style codes into `Product.Barcode`) — but that is not what "QR" means in the
roles/sub-accounts context, and the two are not the same feature even though they share one
Flutter widget. The roles-relevant QR feature is **device pairing / passwordless login for a
sub-account**, and it is the *only* authentication mechanism a `Rep` or `SubAdmin` has — there
is no fallback username/password for either.

### What generates it

`Rep.QrToken` / `SubAdmin.QrToken` — a 32-byte cryptographically random value, base64-encoded,
generated server-side and stored **as plaintext, not hashed**:

```csharp
private static string GenerateQrToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
```

This is a deliberate departure from how the codebase treats every other credential (passwords
are hashed via `IPasswordHasherService`; refresh tokens are SHA-256-hashed before storage in
`RepRefreshToken`/`RefreshToken`). The reasoning, from the entity's own doc comment: *"stored
as-is, not hashed, since it has to be re-presentable on demand rather than consumed once like a
refresh token."* A refresh token is a secret you compare a hash of; a QR code is a secret you
have to **re-display**, potentially days later, when the owner reopens that rep's detail screen
to re-share it after a lost device — hashing it would make that literally impossible (you can't
reverse a hash back into the original image). This is the correct trade-off specifically because
the token's blast radius is bounded: it grants exactly one sub-account's scoped session, not
platform-wide access, and can be invalidated instantly and unilaterally by the owner
(`RegenerateQrAsync`) without needing the old value.

### What's encoded in it

Nothing structured — no JSON, no URL, no embedded rep id. **The QR image's payload is just the
raw random token string itself.** `qr_flutter`'s `QrImageView(data: rep.qrToken!)` renders it
directly:

```dart
// this project's equivalent of lib/features/reps/presentation/rep_detail_page.dart
QrImageView(data: rep.qrToken!, size: 200, backgroundColor: Colors.white)
```

The server resolves whichever sub-account owns that exact token at login time
(`dbContext.Reps.FirstOrDefaultAsync(r => r.QrToken == qrToken)`, backed by a unique index) — so
the QR is purely a **bearer credential encoded as a scannable image**, structurally identical
to "a very long random password nobody has to type."

### What scanning it does

A companion device's login screen offers "ربط جهاز مصاحب" (link a companion device), which opens
the shared `BarcodeScannerPage` (using `mobile_scanner` for the live camera feed, with a
gallery-image fallback via `image_picker` for a rep who saved a screenshot of their own QR
instead of having a second device physically present it). The decoded string is POSTed to
`/api/reps/login-by-qr`:

```csharp
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.SignIn)]
[HttpPost("login-by-qr")]
public async Task<IActionResult> LoginByQr(RepLoginRequest request) =>
    Ok(await repAuthService.LoginByQrAsync(request.QrToken));
```

`RepAuthService.LoginByQrAsync` looks the token up, checks `IsActive` and the **owning
business's** subscription status (`UserService.ComputeAccountStatus`), and — if both pass —
issues a normal JWT access/refresh token pair exactly like a password login would, just with
different claims (see §6). The frontend's login page tries `/reps/login-by-qr` first and falls
through to `/subadmins/login-by-qr` only on a clean 401 ("wrong kind of QR"), never on a network
failure — one scanner UI, two possible principal kinds, resolved client-side by trying the more
common case first.

### Why QR instead of a plain code or a link

- **No typing on a possibly-shared or low-end device.** These sessions are meant to be handed to
  field/warehouse staff who may not have the owner's patience for typing a 43-character base64
  string correctly.
- **The token itself is already the full credential** — a plain numeric code would need to be
  short enough to type, which means either weak entropy or a second server-side lookup step
  (pair-then-confirm) that a long random bearer token avoids entirely. A QR code is simply the
  natural encoding for "hand over a long random secret via a camera instead of a keyboard."
- **Re-presentable, not one-time.** Unlike a magic link (typically single-use, time-boxed), this
  QR is meant to be scanned by possibly multiple devices over its lifetime and to keep working
  indefinitely until the owner explicitly regenerates it — a plain "email me a login link" flow
  doesn't fit that "physically show this image to whichever device needs it, whenever" use case
  as naturally.
- **A visual, device-to-device handoff matches the real workflow**: the owner's phone displays
  the rep's detail screen; the rep's own phone scans it. No email, no SMS, no separate
  out-of-band channel required at all — which matters for a market where a field rep may not
  reliably have a monitored inbox.

A second, deliberately separate QR usage exists on `SubAdmin`: `SubAdminsController.ClaimSubscriber`
lets a `SubAdmin` scan a **subscriber's** own "account activation confirmation" QR to attribute
that subscriber to itself for filtering/reporting purposes only (`User.ManagedBySubAdminId`) —
explicitly **not an access-control gate** (every `Admin`/`SubAdmin` can already manage every
subscriber regardless). Worth knowing this second flow exists so it isn't confused with the
login-pairing one: same *widget*, same *scanning code*, structurally unrelated *purpose*.

---

## 6. Authentication for a sub-account

**No username/password, ever, for either sub-account tier.** The `Rep`/`SubAdmin` entities have
no password column at all — QR-token bearer login (§5) is the only way in. This is enforced
architecturally, not just by omission: `RepAuthService` is a **parallel** class to the normal
`JwtTokenProviderService`/`LoginService` pair, not a retrofit of it — per its own doc comment,
*"Reuses the exact same JwtSettings (key/issuer/audience) so the existing `[Authorize]` pipeline
needs no changes at all; only the claims differ."*

### Distinguishing an Owner's session from a Rep's in the JWT

```csharp
// this project's equivalent of RepAuthService.GenerateTokenAsync
var claims = new List<Claim>
{
    new(JwtRegisteredClaimNames.Sub, rep.Id.ToString()),   // "sub" is the REP's id, never the owner's
    new(ClaimTypes.Role, "Rep"),
    new("ownerId", rep.OwnerUserId.ToString()),
    new("sessionVersion", rep.SessionVersion.ToString()),
};
```

vs. a normal tenant login's `sub` claim, which is the `User.Id` directly. The consuming code
never has to branch on "which kind of session is this" ad hoc — two extension methods on
`ClaimsPrincipal` do it once, centrally:

```csharp
// this project's equivalent of Fatora.API/Extensions/ClaimsPrincipalExtensions.cs
public static Guid GetUserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);

// The one thing every Orders/Customers/Products/Sync/Reports action calls instead of GetUserId():
public static Guid GetEffectiveOwnerId(this ClaimsPrincipal user) =>
    user.IsInRole("Rep") ? Guid.Parse(user.FindFirstValue("ownerId")!) : user.GetUserId();

// Null for a normal User session; for a Rep session, the "sub" claim IS the rep's own id.
public static Guid? GetRepIdOrNull(this ClaimsPrincipal user) =>
    user.IsInRole("Rep") ? user.GetUserId() : null;
```

This is the crux of the whole pattern's maintainability: **every existing query/write that
filters or sets `UserId` keeps working unmodified** once it's changed to call
`GetEffectiveOwnerId()` instead of `GetUserId()` — it never needs to know sub-accounts exist.
The *narrower* scoping (which subset of the owner's data this specific caller may touch) is a
second, separate lookup (`GetRepIdOrNull()`, fed into `ApplyRepScopeAsync` — see §3), kept
deliberately apart from "whose data bucket is this" so the two concerns (whose data / how much
of it) can vary independently without one leaking into the other's derivation.

### Session revocation without server-side token storage of the access token itself

A stateless JWT can't be revoked by deleting a row — the access token is still cryptographically
valid until it expires on its own. This is solved with a version counter embedded as a claim at
issue time and re-checked on every request:

```csharp
public int SessionVersion { get; set; }   // bumped by LogoutAsync / DeactivateAsync / DeleteAsync
```

```csharp
// this project's equivalent of Fatora.API/Filters/AccountStatusFilter.cs, runs on every authenticated request
if (httpContext.User.IsInRole("Rep"))
{
    var rep = await dbContext.Reps.AsNoTracking().Include(r => r.OwnerUser)
        .FirstOrDefaultAsync(r => r.Id == httpContext.User.GetRepIdOrNull());
    var tokenSessionVersion = int.Parse(httpContext.User.FindFirstValue("sessionVersion") ?? "0");
    if (!rep.IsActive || tokenSessionVersion != rep.SessionVersion)
        throw new ForbiddenException("This rep session has ended.", AccountStatusErrorCodes.RepSessionEnded, ...);

    var ownerStatus = UserService.ComputeAccountStatus(rep.OwnerUser);   // the OWNER's subscription, not the rep's own
    if (ownerStatus is "Expired" or "Suspended") throw new ForbiddenException(...);
}
```

An already-issued access token becomes worthless the instant the owner logs the sub-account out
or deactivates it — not just on its next refresh, on its **very next API call** — because the
filter compares the claim's snapshot of `SessionVersion` against the live database value every
single time. The refresh-token row is deliberately *not deleted* on logout (only on genuine
expiry) specifically so a later refresh attempt can still find it and return the distinguishing
`REP_SESSION_ENDED` error code instead of a generic "token not found" — this is what lets the
frontend show "your session ended, re-scan your QR" instead of a silent, unexplained logout.

This same `SessionVersion` + `AccountStatusFilter` mechanism is reused verbatim for `SubAdmin`
sessions, and the filter additionally layers the **owning business's own** subscription status
on top for a `Rep` (a rep's session is only as good as its owner's subscription — a `SubAdmin`,
belonging to the platform directly, has no such second layer to check).

---

## 7. Auditing who did what

Every resource a sub-account can create carries a nullable `CreatedByRepId` (or the `SubAdmin`
equivalent, `PerformedBySubAdminId`/`ManagedBySubAdminId` depending on the action), set once at
creation and **never reassigned**:

```csharp
// this project's equivalent of Fatora.DAL/Entites/Order.cs
// Which sub-account (if any) actually created this invoice - UserId above still always
// resolves to the owning business, unchanged... Null for anything created directly by the
// owner. Never set on delete/reassignment - see Rep.cs.
public Guid? CreatedByRepId { get; set; }
```

The same field exists on `Product`, `Customer`, `Receipt`, `PurchaseRequest`. The convention is
consistent everywhere it appears:

- The resource's **primary owning FK stays the tenant** (`UserId` — unchanged, so every
  existing report/export/PDF path that only ever knew about `UserId` keeps working without
  modification).
- `CreatedByRepId` is a **second, purely-attributional** FK, null when the owner created the row
  directly.
- It survives the creator sub-account being deactivated, hidden, or (eventually) hard-deleted —
  every relevant FK uses `DeleteBehavior.Restrict`, not cascade, specifically so a rep's past
  work keeps resolving and stays filterable by that rep's identity forever, even after the rep
  itself is gone (`RepsController.GetAll(includeHidden: true)` is exactly the escape hatch that
  lets a "filter by rep" picker still resolve a deleted rep for this reason).

**Where it's read back out:**

- **The activity feed** — `RepActivityService.GetActivityFeedAsync` pulls every `Order`/
  `Receipt`/`PurchaseRequest` with a non-null `CreatedByRepId` from the last rolling 24 hours
  across *all* of an owner's reps, attaching `RepName`/`RepId` to each item, so the owner gets a
  "what has my sales team been doing" glance without opening each rep individually.
- **The route/map view** — `GetRouteAsync` filters the same tables down to *one* rep's
  `CreatedByRepId` for a given calendar day, plotting each located action in creation order —
  the audit trail doubling as a literal field-visit itinerary.
- **The "filter by rep" picker** (`RepFilterRow` in the frontend) — any list screen (invoices,
  customers, products) can be narrowed to `repId=<id>`, which server-side becomes exactly the
  same `CreatedByRepId`/access-mode filter described in §3, just invoked by the owner's own
  session instead of the rep's.
- **The offline-sync write path** re-derives and stamps the same field on insert
  (`CreatedByRepId = scopeToRepId` in `SyncService.PushOrderAsync` etc.) — so a rep working
  offline for hours and syncing later is attributed identically to one working online in
  real time; attribution is a property of *who authenticated the write*, not of *which endpoint
  carried it*.

`SubAdminAccountAction` (a small append-only log: `{ UserId, ActionType (Suspended/Reactivated),
PerformedBySubAdminId, CreatedAt, Latitude, Longitude }`) is the same convention applied to
events that don't otherwise have a natural row to hang the attribution on — when the "thing that
happened" isn't itself a data row (suspending an account changes a boolean, it doesn't create
one), a dedicated one-row-per-event audit table is the right shape instead of trying to bolt
`PerformedBySubAdminId` onto the `User` row itself (which could only ever remember the *last*
actor, not the history).

---

## 8. Worked example — end to end

**Scenario:** an owner (`SalesRep`-tier `User`) creates a `Restricted`-mode sales rep, grants it
three specific products, the rep logs in on a separate phone, and requests its product list.

**1. Owner creates the sub-account** — `POST /api/reps { "name": "Ahmad" }`, JWT with
`role=SalesRep`, `sub=<ownerUserId>`.
`[Authorize(Roles = "SalesRep")]` passes → `RepsController.Create` →
`repService.CreateAsync(User.GetUserId(), request)` → new `Rep` row: `OwnerUserId = <owner>`,
`QrToken = <32 random bytes, base64>`, `ProductAccessMode = All` (default, not yet what the
owner wants), `CustomerAccessMode = Own`. Response includes the raw `QrToken` (creation is one
of the few calls that exposes it). The owner's app renders it as a QR image via
`QrImageView(data: rep.qrToken)`.

**2. Owner narrows product access** — `PUT /api/reps/{id}/product-access`
`{ "mode": "Selected", "productIds": [p1, p2, p3] }`. `RepsController.SetProductAccess` (still
`[Authorize(Roles = "SalesRep")]`) → `RepService.SetProductAccessAsync` → `FindOwnedRep` confirms
this rep really belongs to this caller → clears any pre-existing `RepProductAccess` rows for
this rep → sets `rep.ProductAccessMode = Selected` → re-validates each requested id is actually
one of *this owner's own* products (`Where(p => p.UserId == ownerUserId && requestedIds.Contains(p.Id))`,
silently dropping anything that isn't) → inserts three `RepProductAccess { RepId, ProductId }`
rows.

**3. Rep logs in on its own device** — owner's phone displays the QR; rep's phone opens
`LoginPage` → "ربط جهاز مصاحب" → `BarcodeScannerPage` (mobile_scanner) decodes the raw token
string → `RepsRepository.loginByQr(token)` → `POST /api/reps/login-by-qr { "qrToken": "<token>" }`
(`[AllowAnonymous]`, rate-limited) → `RepAuthService.LoginByQrAsync` looks the `Rep` up by its
unique `QrToken` index, confirms `IsActive`, confirms the **owner's** subscription isn't
`Expired`/`Suspended`, and issues a JWT: `sub=<repId>`, `role=Rep`, `ownerId=<ownerUserId>`,
`sessionVersion=<rep.SessionVersion>` — plus a hashed `RepRefreshToken` row snapshotting that
same `SessionVersion`. The frontend stores the token pair, stores `rep.name` in the "username"
slot for display (a rep has no username), and persists the raw QR token via `LastQrStorage` so
this device can re-authenticate with one tap next time without re-scanning.

**4. Rep requests its product list** — `GET /api/products` with `Authorization: Bearer <rep JWT>`.
Pipeline order:
   - `AccountStatusFilter` runs first (every authenticated request): resolves `Rep` by
     `GetRepIdOrNull()`, compares the token's `sessionVersion` claim against the live DB value
     (equal — nothing has logged this session out since) and `rep.IsActive` (true), then checks
     the **owner's** `ComputeAccountStatus` (not expired/suspended) — passes.
   - `[Authorize(Roles = "SalesRep,Rep")]` on `ProductsController` passes (`Rep` is in the set).
   - `ProductsController.GetAll` computes `scopeToRepId = User.GetRepIdOrNull() ?? repId` →
     resolves to the rep's own id (the `repId` query param, if any, is ignored for a `Rep`
     session) — and `User.GetEffectiveOwnerId()` resolves to `ownerId` from the `ownerId` claim,
     i.e. the owner's `UserId`, exactly as if the owner had called this themselves.
   - `ProductService.GetAllAsync(ownerUserId, scopeToRepId)` → base query
     `Where(p => p.UserId == ownerUserId && p.IsActive)` → `ApplyRepScopeAsync`: rep is found,
     mode is `Selected` (not `All`, not `RepOwnedOnly`) → query narrowed to
     `RepProductAccesses.Where(a => a.RepId == repId).Select(a => a.ProductId)` → final result:
     **exactly the three granted products**, regardless of how many others the owner's catalog
     actually has.

**What the rep does and doesn't see, concretely:** the three granted products, by id — full
detail on each (name, price, stock quantity, images). Every *other* product in the owner's
catalog is invisible to every list/get call this rep makes, not merely hidden in the UI — a
direct `GET /api/products/{otherProductId}` for one it wasn't granted returns **404** (via
`IsVisibleToRepAsync` returning false inside `GetByIdAsync`), not 403, so the rep's app can't
even confirm such a product exists. If this rep tries `POST /api/products` to create a new one,
`ProductService.CreateAsync` rejects it outright (`ForbiddenException`) because `Selected` mode
structurally forbids creation — the owner would need to switch it to `All` or `RepOwnedOnly`
first. Every one of these checks re-derives from the single `ApplyRepScopeAsync`/
`IsVisibleToRepAsync` pair described in §3 — there is no second, parallel implementation of
"what can this rep see" anywhere else in the codebase for this resource type, including the
offline sync-pull endpoint, which narrows its own `Products` query through the identical rule.
