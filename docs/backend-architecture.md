# .NET Backend Architecture Pattern (API → BL → DAL, EF Core, JWT)

This document describes a battle-tested three-project layering pattern for a .NET
backend (ASP.NET Core Web API + EF Core), extracted from a real production codebase
(a multi-tenant SaaS invoicing system, hereafter "the source project"). It is written
so that another AI assistant or engineer can replicate the pattern in an unrelated
domain. Every example below is real code read from the source project, generalized
in prose but left verbatim in code blocks. Where a concrete entity name appears
(`Customer`, `Order`, `Product`), read it as "a typical owned resource" — the pattern
is what matters, not the entity.

The stack: ASP.NET Core Web API on .NET, EF Core with Npgsql (PostgreSQL), FluentValidation,
JWT bearer auth with refresh tokens, `IExceptionHandler`-based global error handling,
`Microsoft.AspNetCore.RateLimiting`.

---

## 1. Project/layer structure

Three projects, one solution, a strict one-directional dependency chain:

```
MyApp.API   (ASP.NET Core Web App, Microsoft.NET.Sdk.Web)
  -> references MyApp.BL

MyApp.BL    (class library, Microsoft.NET.Sdk)
  -> references MyApp.DAL

MyApp.DAL   (class library, Microsoft.NET.Sdk)
  -> references nothing else in the solution (leaf project)
```

Confirmed directly from the `.csproj` files:

- `Fatora.API.csproj` → `<ProjectReference Include="..\Fatora.BL\Fatora.BL.csproj" />`
- `Fatora.BL.csproj` → `<ProjectReference Include="..\Fatora.DAL\Fatora.DAL.csproj" />`
- `Fatora.DAL.csproj` → no `ProjectReference` at all.

The dependency direction is enforced structurally, not by convention: DAL cannot see
BL or API types even if someone tried, because there is no reference path back up.
This is the real value of splitting into projects instead of just folders inside one
project — a folder convention can be violated by an errant `using`; a missing project
reference is a compile error.

**What lives in each layer, and what is confirmed to NOT belong there:**

- **DAL (Data Access Layer)** — `Entites/` (entity POCOs — the misspelling is the
  project's own, kept intentionally rather than "fixed" mid-project, per the "never
  rename... without approval" convention below), `Data/AppDbContext.cs`,
  `Data/Configuration/*Configuration.cs` (EF Core Fluent API, one file per entity),
  `Data/Migrations/`. It references only `Microsoft.EntityFrameworkCore`,
  `Npgsql.EntityFrameworkCore.PostgreSQL`, and (notably)
  `Microsoft.AspNetCore.Authentication.JwtBearer` — the JWT package is pulled in here
  only because `TokenValidationParameters`-adjacent types are needed by BL's token
  service; DAL itself contains **zero** business logic, zero HTTP concepts, zero
  validation rules. It knows how data is shaped and stored — nothing else.

- **BL (Business Layer)** — `Services/Abstractions/I*Service.cs` (interfaces),
  `Services/Classes/*Service.cs` (implementations, all business rules and
  authorization-adjacent scoping logic), `DTOs/Requests` and `DTOs/Responses`
  (the request/response contracts — DTOs live in BL, not API, because both the API
  controllers and, e.g., a background/sync service in the same layer need them),
  `Exceptions/` (the custom exception hierarchy), `Utils/` (seed data, cross-cutting
  helpers). This is where all business rules, ownership/visibility scoping, and
  orchestration across multiple entities happen. It is the only layer that is allowed
  to know "what a valid state transition is."

- **API (Presentation/Host Layer)** — `Controllers/` (thin — see below),
  `Validators/` (FluentValidation validator classes, organized in one subfolder per
  resource, e.g. `Validators/CustomerValidators/`), `Exceptions/GlobalExceptionsHandler.cs`,
  `Filters/` (cross-cutting `IAsyncAuthorizationFilter`s), `Extensions/`
  (`ClaimsPrincipalExtensions` — reading identity out of the JWT), `Services/`
  (host-specific infrastructure services that talk to the outside world, e.g.
  `IFileStorageService`), `GroupRegistrations.cs` (DI wiring), `Program.cs`.

The controller convention, confirmed from every controller read (`CustomersController`,
`AccountController`, etc.): a controller method does exactly three things — run a
validator, call one service method passing the caller's identity extracted from
`ClaimsPrincipal`, and translate the result into an `IActionResult`/status code. No
`if` branching on business rules, no direct `DbContext` access, no cross-entity
orchestration. Quoting the actual pattern from `CustomersController.Create`:

```csharp
[HttpPost]
public async Task<IActionResult> Create(CreateCustomerRequest request)
{
    var validationResult = await createValidator.ValidateAsync(request);
    if (!validationResult.IsValid)
    {
        return BadRequest(validationResult.Errors);
    }

    var result = await customerService.CreateAsync(User.GetEffectiveOwnerId(), request, User.GetRepIdOrNull());
    return StatusCode(StatusCodes.Status201Created, result);
}
```

There is no repository layer between BL services and EF Core — BL services inject
`AppDbContext` directly (see §4). This is a deliberate simplification, not an
oversight: EF Core's `DbContext`/`DbSet<T>` with `IQueryable` **is** the repository +
unit-of-work abstraction; wrapping it in a hand-rolled `IRepository<T>` would just be
an abstraction over an abstraction with no independent value, since the project only
ever targets one database technology and tests would still need a real (or in-memory)
`DbContext` to exercise LINQ translation correctly anyway. If your project anticipates
swapping persistence technology, or needs a query surface EF's `IQueryable` cannot
express, that tradeoff should be revisited — but do not add a repository layer purely
"for testability," because a service class taking `AppDbContext` in its constructor is
already trivially mockable/fake-able for tests via EF Core's in-memory or SQLite
providers.

The project's own house rules (from its committed `CLAUDE.md`-equivalent guidance
file) state this explicitly and generally, worth carrying into any new project's own
guidelines verbatim:

> Business logic must stay outside controllers.
> Avoid unnecessary layers. Do not introduce patterns unless they solve a real problem.
> Keep dependencies intentional.
> Never rename files, folders, namespaces, or classes without approval.

---

## 2. Entity ID strategy

**`Guid`, client-generated at construction time, for every entity in the system.**
Confirmed across every entity read (`Customer`, `Order`, `Product`, `Rep`, `User`,
`SubAdmin`, ...) — the pattern is uniform, no entity uses `int` identity or `string`.

```csharp
public class Customer : ISyncableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    ...
}
```

The ID is assigned **in the property initializer**, not by the database
(`GENERATED ALWAYS AS IDENTITY` / `SERIAL`), and not by an EF Core value generator
configured in Fluent API — it is a plain default-value expression on the POCO, so the
entity already has a real, final ID the instant `new Customer { ... }` runs, before
`SaveChangesAsync` is ever called.

Why this matters here specifically, inferred from the domain (an offline-first mobile
client that syncs to the server later — see `ISyncableEntity`, `SyncService`,
`PendingRepSync`): a sales rep's phone can create a `Customer` or `Order` while
completely offline, and that record needs a permanent, final, collision-free
identity **the moment it's created on-device**, because the UI immediately links other
offline-created rows to it by that ID (e.g. an `Order.CustomerId` pointing at a
`Customer` created three screens ago, before either has ever reached the server). An
auto-incrementing integer PK cannot support that — it doesn't exist until the INSERT
commits, and two offline devices would collide on the same next integer anyway.
`Guid.NewGuid()` gives every device a globally unique ID with no coordination, so the
sync push endpoint can accept a whole graph of client-generated IDs (customer, then an
order referencing it) in one batch without a server round-trip in between to learn the
real ID.

If your new project has no offline/multi-writer requirement, this tradeoff may not
apply to you — a `SERIAL`/`IDENTITY` integer PK is smaller (4/8 bytes vs 16), indexes
better, and is more human-debuggable in raw SQL. Adopt GUIDs specifically when: (a)
IDs must be assignable before the row is persisted (offline-first, event sourcing,
distributed writers), or (b) IDs must not leak sequence/cardinality information to
clients (e.g. a public API where enumerable integer IDs would let a caller guess
`/orders/1002` exists because `/orders/1001` did). This project's own code comments
don't spell out the "why GUID" reasoning inline, so the reasoning above is inferred
from the sync architecture (`SyncService`, `ISyncableEntity`, `PendingRepSync`) rather
than quoted — flag this inference to the reader rather than presenting it as
documented fact.

One place a different key shape appears: `RefreshToken` stores the **raw random
refresh token as a hashed value**, not a `Guid`, as the lookup key:

```csharp
var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
var refreshToken = new RefreshToken
{
    Token = HashToken(rawToken),   // SHA-256, base64 — this is the lookup key
    UserId = user.Id,
    ExpiresOnUtc = now.AddDays(7)
};
```

This is not an ID strategy exception so much as confirmation of the rule "use the
right primitive for the right job" — a refresh token is a bearer secret, not an
identity, so it's modeled as a hashed opaque string column, while the row itself still
carries its own `Guid Id` like every other entity.

---

## 3. EF Core Fluent API configuration pattern

**Convention confirmed: zero Data Annotations on entities. Every entity has a
matching `IEntityTypeConfiguration<T>` class in `DAL/Data/Configuration/`, one file
per entity, named `{Entity}Configuration.cs`.** Grep across the DAL project for
`[Required]`, `[MaxLength]`, `[Column]`, etc. on entity classes returns nothing — the
entities read (`Customer.cs`, `Order.cs`) are plain POCOs with no attributes beyond
the base `ISyncableEntity` interface and C# nullable-reference-type annotations
(`string?`, `required string`).

**Entity (the "before" — pure shape, no schema/persistence concerns):**

```csharp
// Fatora.DAL/Entites/Customer.cs
public class Customer : ISyncableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? StoreName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; }

    public Guid? CreatedByRepId { get; set; }
    public Rep? CreatedByRep { get; set; }
}
```

**Configuration (the "after" — every schema/constraint/relationship concern, isolated):**

```csharp
// Fatora.DAL/Data/Configuration/CustomerConfiguration.cs
public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(x => x.Name).IsRequired().HasColumnType("VARCHAR").HasMaxLength(100);
        builder.Property(x => x.PhoneNumber).IsRequired(false);
        builder.Property(x => x.StoreName).IsRequired(false);
        builder.HasIndex(x => new { x.UserId, x.UpdatedAt });
        builder.HasIndex(x => x.CreatedByRepId);

        builder.HasOne(x => x.CreatedByRep)
            .WithMany()
            .HasForeignKey(x => x.CreatedByRepId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

Wired up with a single line in `AppDbContext.OnModelCreating` — no manual per-entity
registration, so a new `{X}Configuration.cs` file is picked up automatically the
moment it exists:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
```

**Why Fluent API over Data Annotations, inferred from the actual code shape (not
stated in a comment anywhere, so treat this as reasoned inference):**

1. **The entity stays a clean POCO usable by BL/API with no DAL/EF concern leaking
   upward.** `Customer` in the code above has no `using Microsoft.EntityFrameworkCore`
   at all — it is a plain data shape any layer could reference without pulling in an
   ORM dependency. Attributes like `[Column(TypeName = "numeric(18,2)")]` or
   `[ForeignKey]` would force every consumer of the entity type to at least
   transitively reference EF Core attribute types.
2. **Fluent API can express things Data Annotations cannot**, and this project
   actually needs them: composite/multi-column indexes (`HasIndex(x => new {
   x.UserId, x.UpdatedAt })`), explicit per-relationship `DeleteBehavior` (see
   `OrderConfiguration`/`CustomerConfiguration`'s `.OnDelete(DeleteBehavior.Restrict)`
   with a comment explaining exactly why cascade would be wrong there), and
   `HasColumnType("VARCHAR")` precision. There is no Data Annotation equivalent for
   composite indexes or fine-grained delete behavior.
3. **Single source of truth for schema, colocated by concern, not by class.** All
   constraints for one table live in one file, separate from — and reviewable
   independently of — the shape of the domain object. A schema-only PR touches only
   `Data/Configuration/`, never the entity itself.
4. **Self-documenting FK-cascade reasoning lives next to the config, not scattered as
   attributes.** E.g. `OrderConfiguration`:

```csharp
// Restrict, not cascade: a Rep is only ever soft-deactivated (see
// Rep.cs), never actually removed, so this never fires in practice -
// but it documents that historical invoices must keep resolving
// even if that ever changes.
builder.HasOne(x => x.CreatedByRep)
    .WithMany()
    .HasForeignKey(x => x.CreatedByRepId)
    .OnDelete(DeleteBehavior.Restrict);
```

Adopt this rule as: **entities are dumb data shapes; every persistence rule
(required-ness, length, index, cascade behavior, column type) is declared exactly
once, in exactly one file per entity, under a `Data/Configuration/` folder, applied
via `ApplyConfigurationsFromAssembly`.** Never mix in `[Required]`/`[MaxLength]` even
for the "simple" cases — mixing conventions means a future reader has to check two
places to know an entity's real constraints.

---

## 4. Service/interface pattern

**Strict rule, confirmed by exhaustive listing: every service in BL has a matching
interface, and there is a 1:1 file per interface/implementation.**

```
Fatora.BL/Services/Abstractions/ICustomerService.cs   -> interface
Fatora.BL/Services/Classes/CustomerService.cs         -> implementation
```

21 interfaces in `Services/Abstractions/`, 21 matching classes in `Services/Classes/`
— no orphans either direction (`IAdminRecoveryService`/`AdminRecoveryService`,
`IJwtTokenProviderService`/`JwtTokenProviderService`, `ISyncService`/`SyncService`,
etc.). Interfaces and implementations are separated by **folder**, not by project —
both live in BL, just in sibling directories, which keeps the public contract
(`Abstractions/`) visually distinct from the logic (`Classes/`) while letting them
share the same DTOs and exception types without an extra project reference.

```csharp
// Fatora.BL/Services/Abstractions/ICustomerService.cs
public interface ICustomerService
{
    Task<CustomerResponse> CreateAsync(Guid userId, CreateCustomerRequest request, Guid? createdByRepId = null);
    Task<List<CustomerResponse>> GetAllAsync(Guid userId, Guid? scopeToRepId = null);
    Task<CustomerResponse> GetByIdAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
    Task<CustomerResponse> UpdateAsync(Guid userId, Guid id, UpdateCustomerRequest request, Guid? scopeToRepId = null);
    Task DeleteAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
    Task<List<CustomerResponse>> GetArchivedAsync(Guid userId, Guid? scopeToRepId = null);
    Task<CustomerResponse> RestoreAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
    Task PermanentDeleteAsync(Guid userId, Guid id, Guid? scopeToRepId = null);
}
```

Implementations use **primary constructors** (C# 12) to receive their dependency —
almost always just `AppDbContext`, sometimes another BL service interface (e.g.
`JwtTokenProviderService` also takes `IRepAuthService` and `ISubAdminAuthService`,
since token refresh needs to try all three session kinds):

```csharp
public class CustomerService(AppDbContext dbContext) : ICustomerService
{
    public async Task<CustomerResponse> CreateAsync(Guid userId, CreateCustomerRequest request, Guid? createdByRepId = null)
    {
        var customer = new Customer { Name = request.Name, ..., UserId = userId, CreatedByRepId = createdByRepId };
        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();
        return ToResponse(customer);
    }
    // ...
}
```

**Why every service gets an interface even when there's currently only one
implementation:** it is the seam DI relies on (see §5 — controllers and other
services depend on `ICustomerService`, never `CustomerService`), and it is what makes
a service replaceable in a unit test with a hand-written fake or a mocking framework
without touching the consumer. Note there is a stray, deliberately-excluded
`Services/Implementation/` folder referenced in `Fatora.BL.csproj`
(`<Compile Remove="Services\Implementation\**" />`) — this is a leftover from a
naming-convention change (`Implementation` → `Classes`) kept out of the build rather
than deleted mid-project, consistent with the "never delete/rename without approval"
house rule; a new project should just pick one name (`Classes` or `Implementation`)
once and stay with it.

DTOs (`CreateCustomerRequest`, `CustomerResponse`, etc.) live in BL under
`DTOs/Requests` / `DTOs/Responses`, never reusing entity types directly on the wire —
services always map entity → response DTO explicitly (see `CustomerService.ToResponse`
below), so a schema change to the entity can never silently change the API's public
shape.

```csharp
internal static CustomerResponse ToResponse(Customer customer) => new()
{
    Id = customer.Id,
    Name = customer.Name,
    StoreName = customer.StoreName,
    PhoneNumber = customer.PhoneNumber,
    Street = customer.Street,
    City = customer.City,
    IsActive = customer.IsActive,
    CreatedAt = customer.CreatedAt,
    UpdatedAt = customer.UpdatedAt,
    CreatedByRepId = customer.CreatedByRepId
};
```

That mapper is `internal static`, not `private` — deliberately reused by a second
call site in the same assembly (the sync-push service) that needs to build the exact
same response shape from the exact same entity, without duplicating the mapping.

---

## 5. DI registration

**One static extension method, one file, explicit registration — no assembly
scanning.** `Fatora.API/GroupRegistrations.cs`:

```csharp
public static class GroupRegistrations
{
    public static IServiceCollection AddGroupedServices(this IServiceCollection Services)
    {
        Services.AddScoped<IJwtTokenProviderService, JwtTokenProviderService>();
        Services.AddScoped<IPasswordHasherService, PasswordHasherService>();
        Services.AddScoped<ILoginService, LoginService>();
        Services.AddScoped<IUserService, UserService>();
        Services.AddScoped<IProductService, ProductService>();
        Services.AddScoped<ICustomerService, CustomerService>();
        // ... one line per service interface, ~21 total

        Services.AddScoped<LoginRequestValidator>();
        Services.AddScoped<CreateCustomerRequestValidator>();
        Services.AddScoped<UpdateCustomerRequestValidator>();
        // ... one line per FluentValidation validator class, ~28 total

        return Services;
    }
}
```

Called once from `Program.cs`:

```csharp
builder.Services.AddGroupedServices();
```

Every service is registered `AddScoped` — one instance per HTTP request, matching
`AppDbContext`'s own default scoped lifetime (a scoped service may safely depend on
another scoped service or on the scoped `DbContext`; it must never be registered
singleton if it captures a scoped dependency, which would throw at runtime in
Development via ASP.NET Core's built-in scope-validation check).

Validators are registered the same way, as **concrete classes**, not behind an
interface — `AddScoped<CreateCustomerRequestValidator>()` rather than
`AddScoped<IValidator<CreateCustomerRequest>, CreateCustomerRequestValidator>()` —
because controllers inject the concrete validator type directly in their primary
constructor (see §7) rather than resolving `IValidator<T>` generically. This is a
simplicity tradeoff worth calling out: it means validation is invoked manually per
action (`await createValidator.ValidateAsync(request)`) instead of automatically via
an MVC filter/pipeline — see §7 for the actual consequence of that choice.

**Why explicit registration over assembly-scanning (e.g. Scrutor):** with ~21 services
and ~28 validators, a `.csproj`-visible flat list is fully greppable — "is
`IReceiptService` registered, and with what lifetime?" is answered by one `Ctrl+F` in
one file, with no reflection-based scanning convention (naming pattern, marker
interface, assembly attribute) to keep in anyone's head. This does not scale
gracefully past a few hundred registrations, at which point grouping into several
`Add{Area}Services()` extension methods (still explicit, just partitioned) is the next
step — not a switch to scanning. Only reach for scanning when the manual list has
demonstrably become the bottleneck, not preemptively.

---

## 6. Authentication and authorization

**Scheme: JWT bearer access token (short-lived) + opaque refresh token (long-lived,
stored server-side, hashed).** Configured in `Program.cs`:

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;   // keep claim type names exactly as issued, e.g. "role" not the long MS URI form

    var secretKey = builder.Configuration.GetSection("JwtSettings")["SecretKey"];
    // ... fail fast at startup if missing, never silently default

    options.TokenValidationParameters = new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,          // no grace window on expiry
        ValidateIssuerSigningKey = true,
        ValidIssuer = JwtSettings["Issuer"],
        ValidAudience = JwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!)),
        RoleClaimType = "role"
    };
});
```

Two secrets/config values are read from configuration and **fail startup loudly**
(throw `InvalidOperationException`) if missing, rather than falling back to a default
— the connection string and the JWT signing key. Both are documented (in the same
throw message) as coming from an environment variable in production
(`JwtSettings__SecretKey`), never a committed `appsettings.json`.

**Claims issued at token generation** (`JwtTokenProviderService.GenerateToken`):

```csharp
var claims = new List<Claim>()
{
   new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
   new Claim(ClaimTypes.Role, user.Role.ToString()),
};
```

There are **three distinct principal "shapes"** issued by three different auth
services in this system (owner `User`, sub-account `Rep`, platform `SubAdmin`), each
adding claims beyond the base two depending on what that principal needs to carry:

- A `Rep` token additionally carries `"ownerId"` (the business account it belongs to)
  and `"sessionVersion"` (an integer, bumped to invalidate all of that rep's
  outstanding tokens at once — a forced-logout mechanism cheaper than a token
  blocklist).
- A `SubAdmin` token carries its own `"sessionVersion"` the same way.

**Role-based authorization on controller actions** uses the standard
`[Authorize(Roles = "...")]` attribute, at both the controller and per-action level,
with the more specific per-action attribute narrowing (never widening) the
controller-level default:

```csharp
[Authorize(Roles = "SalesRep,Rep")]     // class-level default: either role may call anything below
public class CustomersController(...) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(...) { ... }   // inherits SalesRep,Rep

    [Authorize(Roles = "SalesRep")]      // this action only: owner, never a Rep sub-account
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) { ... }
}
```

Every narrowing has an explanatory comment justifying *why* that specific action is
owner-only (e.g. hard-delete cascades through FK-linked financial history that a
sub-account must never be able to destroy) — worth carrying forward as a rule: **a
role restriction that isn't obviously derivable from the action's own name should say
why in a comment**, because six months later "why is Delete owner-only but Update
isn't?" is a legitimate question a reviewer will ask.

**Row-level, "which records can this user see" scoping** (the multi-tenant-like part)
is implemented as **plain LINQ predicates inside each BL service**, driven by claims
extracted through controller-layer extension methods — not a global EF Core query
filter (`HasQueryFilter`), and not a separate authorization-policy-handler layer.
`ClaimsPrincipalExtensions`:

```csharp
// The one thing every Orders/Customers/Products/Sync/Reports controller
// action should call instead of GetUserId() directly: for a normal
// User session this is just their own id, but for a Rep session it
// resolves to the owning business's id (the "ownerId" claim), so every
// existing query/write that filters or sets UserId keeps landing in the
// right place without knowing Reps exist at all.
public static Guid GetEffectiveOwnerId(this ClaimsPrincipal user) =>
    user.IsInRole("Rep") ? Guid.Parse(user.FindFirstValue("ownerId")!) : user.GetUserId();

public static Guid? GetRepIdOrNull(this ClaimsPrincipal user) =>
    user.IsInRole("Rep") ? user.GetUserId() : null;
```

The controller always passes both `GetEffectiveOwnerId()` (which tenant's data) and
`GetRepIdOrNull()` (which sub-account, if any, further narrows it) into the service
call; the service then applies a three-tier visibility rule per record —
`CustomerAccessMode.Own` (only what this rep created) / `.Restricted` (an
admin-curated allow-list, via a join table) / `.All` (everything under the tenant):

```csharp
private async Task<IQueryable<Customer>> ApplyRepScopeAsync(IQueryable<Customer> query, Guid? scopeToRepId)
{
    if (scopeToRepId is null) return query;   // the owner: never filtered

    var rep = await dbContext.Reps.FirstOrDefaultAsync(r => r.Id == scopeToRepId);
    if (rep is null || rep.CustomerAccessMode == CustomerAccessMode.All) return query;

    if (rep.CustomerAccessMode == CustomerAccessMode.Restricted)
    {
        var allowedIds = dbContext.RepCustomerAccesses
            .Where(a => a.RepId == scopeToRepId)
            .Select(a => a.CustomerId);
        return query.Where(c => c.CreatedByRepId == scopeToRepId || allowedIds.Contains(c.Id));
    }

    return query.Where(c => c.CreatedByRepId == scopeToRepId);   // Own (default)
}
```

Why per-service LINQ instead of a global `HasQueryFilter`: the visibility rule here is
not a single boolean per tenant (which is what `HasQueryFilter` is best at — "always
add `WHERE TenantId = @current`") but a three-way mode that also depends on a *second*
join table and differs for reads vs. writes (`FindOwnedActiveCustomer` for normal
lookups vs. the stricter `IsOwnRepCreation` check used only for `Update`, deliberately
different from the read-visibility check). A global filter can't express "different
predicate for edit than for view" without per-query filter toggling, which is more
magic, not less, than just writing the predicate explicitly where it's used.

**A separate always-on authorization filter** (`AccountStatusFilter`, an
`IAsyncAuthorizationFilter` registered globally via `options.Filters.Add<AccountStatusFilter>()`
in `AddControllers`) re-checks account standing (expired/suspended subscription,
forced-logout `sessionVersion`) on **every** authenticated request, not just at
sign-in — because a JWT, once issued, is valid until it expires regardless of what
happens to the account afterward; this filter is what makes a mid-session
suspension/forced-logout actually take effect immediately instead of waiting up to the
access token's full lifetime.

**Refresh token handling**, in `JwtTokenProviderService`:

- The raw refresh token is a 32-byte CSPRNG value (`RandomNumberGenerator.GetBytes(32)`),
  base64-encoded, returned to the client once. Only its SHA-256 hash is persisted —
  `SHA256` deliberately, not a slow password hash (bcrypt/Argon2), because a refresh
  token is already 256 bits of uniform randomness (needs collision resistance only),
  not a low-entropy user-chosen secret (which would need brute-force resistance).
- Refresh tokens are **rotated** on every use: `RefreshTokenAsync` issues a brand new
  access+refresh pair and the old refresh token row is marked superseded
  (`ReplacedAtUtc`) rather than deleted outright, kept alive for a short
  `RefreshRotationGrace` (90 seconds) — specifically to tolerate a client that
  retried a timed-out refresh call and is now presenting a token the server already
  rotated out from under it. This detail (documented at length in a code comment on
  `RefreshRotationGrace`) is a good illustration of "don't just rotate-and-delete
  blindly — think about what a legitimate retry looks like."
- Logout deletes all of that user's refresh token rows outright (`LogoutAsync`),
  making every outstanding refresh token immediately unusable.

---

## 7. Validation pipeline

**FluentValidation, invoked manually at the top of each controller action — not
wired into the MVC filter pipeline via `AddFluentValidationAutoValidation()`.**

One validator class per request DTO, in `API/Validators/{Resource}Validators/`:

```csharp
// Fatora.API/Validators/CustomerValidators/CreateCustomerRequestValidator.cs
public class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("customer Name can not be empty");
    }
}
```

Registered as a concrete scoped type (§5) and injected directly into the controller's
primary constructor, then explicitly invoked at the top of the action:

```csharp
public class CustomersController(
    ICustomerService customerService,
    CreateCustomerRequestValidator createValidator,
    UpdateCustomerRequestValidator updateValidator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateCustomerRequest request)
    {
        var validationResult = await createValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);   // 400, FluentValidation's ValidationFailure[] as the body
        }

        var result = await customerService.CreateAsync(User.GetEffectiveOwnerId(), request, User.GetRepIdOrNull());
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
```

This is a deliberate tradeoff, not an oversight — the alternative (automatic
validation via an ASP.NET Core filter/middleware that short-circuits before the action
runs) is more "magic": it's not visible in the action method that validation
happened, and the failure response shape is whatever the library's default filter
produces rather than something the team controls line-by-line. The manual-call
version costs two repeated lines per action but keeps the entire request lifecycle
(validate → authorize-scope → call service → shape response) visible in one method
body with nothing happening off-screen. The real cost of this choice: it is easy to
*forget* the validator call in a new action, since nothing enforces it structurally —
if you adopt this pattern, treat "does every action with a body parameter call its
validator first" as a checklist item in code review, or accept the auto-validation
filter's magic in exchange for that guarantee.

A validation failure becomes `400 Bad Request` with FluentValidation's own
`ValidationFailure` list serialized directly as the body (via ASP.NET Core's default
`BadRequest(object)` helper) — **not** funneled through the `ProblemDetails` shape
that the global exception handler produces for exceptions (§8). This is an
inconsistency worth flagging if replicating this pattern: a client has to know that a
`400` from a bad `[FromBody]` model binding or an unhandled `ArgumentException` looks
like a `ProblemDetails` object, while a `400` from FluentValidation looks like a bare
array of `{ PropertyName, ErrorMessage, ... }` objects. A cleaner version of this same
pattern would either wrap `validationResult.Errors` into a `ProblemDetails` before
returning it, or move to automatic validation with a custom
`InvalidModelStateResponseFactory` that produces the same shape as the exception
handler. Note the gap rather than copy it uncritically.

---

## 8. Exception handling

**Custom sealed exception hierarchy, all deriving from one `abstract class AppException`,
mapped centrally to HTTP status + a `ProblemDetails` body by a single `IExceptionHandler`.**

```csharp
// Fatora.BL/Exceptions/AppException.cs
public abstract class AppException : Exception
{
    public HttpStatusCode StatusCode { get; }

    // Machine-readable discriminator for cases where the frontend needs to react
    // to *why* a request failed, not just display the message.
    public string? ErrorCode { get; }

    // Extra structured data merged into the response body alongside message/code.
    public IReadOnlyDictionary<string, object>? Extensions { get; }

    protected AppException(string message, HttpStatusCode statusCode = HttpStatusCode.InternalServerError,
        string? errorCode = null, IReadOnlyDictionary<string, object>? extensions = null) : base(message)
    { StatusCode = statusCode; ErrorCode = errorCode; Extensions = extensions; }
}
```

Five concrete leaf exceptions, each `sealed`, each hard-wired to one HTTP status via
`base(...)`, living in `Fatora.BL/Exceptions/`:

| Exception | Status | Notes |
|---|---|---|
| `NotFoundException` | 404 | two ctors: a raw safe message, or `(resourceName, key)` → `"{resourceName} with identifier '{key}' was not found."` |
| `ConflictException` | 409 | same two-ctor shape as `NotFoundException` |
| `BadRequestException` | 400 | single message ctor |
| `UnauthorizedException` | 401 | message + optional `errorCode`/`extensions` |
| `ForbiddenException` | 403 | message + optional `errorCode`/`extensions` |

Thrown directly from BL services wherever a business rule is violated — e.g.
`CustomerService.PermanentDeleteAsync`:

```csharp
if (hasHistory)
{
    throw new ConflictException("لا يمكن حذف عميل له فواتير أو سندات قبض نهائيًا - قم بأرشفته بدلًا من ذلك.");
}
```

(Note the message is in the tenant-facing locale/language directly — this project's
messages are safe-by-design to show the end user, which is exactly why `AppException`
messages, and only `AppException` messages, are allowed to leak to the client in
production; see below.)

**Global handler**, `Fatora.API/Exceptions/GlobalExceptionsHandler.cs`, implements
ASP.NET Core's `IExceptionHandler` (the modern replacement for exception-handling
middleware, registered via `builder.Services.AddExceptionHandler<GlobalExceptionHandler>()`
+ `app.UseExceptionHandler()`, alongside `builder.Services.AddProblemDetails()`):

```csharp
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IOptions<HttpJsonOptions> jsonOptions) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception, "Unhandled exception occured. TraceId: {TraceId}", httpContext.TraceIdentifier);

        var (statusCode, title) = MapException(exception);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode, Title = title, Type = GetProblemType(statusCode),
            Instance = httpContext.Request.Path, Detail = GetSafeErrorMessage(exception, httpContext)
        };
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
        problemDetails.Extensions["timestamp"] = DateTime.UtcNow;
        if (exception is AppException { ErrorCode: { } errorCode }) problemDetails.Extensions["code"] = errorCode;
        if (exception is AppException { Extensions: { } extra })
            foreach (var (key, value) in extra) problemDetails.Extensions[key] = value;

        httpContext.Response.StatusCode = statusCode;
        // Written directly, not via IProblemDetailsService - its default writer
        // resets Content-Type to "application/problem+json" with no charset,
        // which makes strict clients fall back to Latin-1 and garble non-ASCII text.
        httpContext.Response.ContentType = "application/problem+json; charset=utf-8";
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, jsonOptions.Value.SerializerOptions), ct);
        return true;
    }

    private static (int StatusCode, string Title) MapException(Exception exception) => exception switch
    {
        ArgumentNullException or ArgumentException => (StatusCodes.Status400BadRequest, "Invalid argument provided"),
        NotFoundException => (StatusCodes.Status404NotFound, "Resource Not Found"),
        ConflictException => (StatusCodes.Status409Conflict, "Resource Already Exists"),
        UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
        ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
        BadRequestException => (StatusCodes.Status400BadRequest, "Invalid Request"),
        AppException appException => ((int)appException.StatusCode, "Application Error"),  // fallback for any future subclass
        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
    };

    private static string? GetSafeErrorMessage(Exception exception, HttpContext context)
    {
        var env = context.RequestServices.GetRequiredService<IHostEnvironment>();
        if (env.IsDevelopment()) return exception.Message;          // full detail locally
        return exception is AppException ? exception.Message : null; // only known-safe exceptions leak a message in prod
    }
}
```

The critical security property here: **only `AppException` (and subclasses) messages
are ever returned to the client in production.** Any other exception — a null
reference, a DB constraint violation, an unhandled third-party library exception —
returns a generic `"Internal Server Error"` with `Detail: null`, while the full
message and stack trace are still logged server-side via `ILogger`. This is the
correct default: an `AppException` is, by construction, an exception a developer
*chose* to throw with a message meant for the end user; anything else is an
unanticipated failure whose message might contain internal details (SQL fragments,
file paths, type names) that should never reach a client.

The switch expression's `_ => 500` fallback plus the `AppException appException`
catch-all arm (for any future subclass that doesn't match the five explicitly listed
ones) means **adding a sixth `AppException` subclass never results in an unhandled
crash of the handler itself** — it still gets *a* status code (whatever
`StatusCode` was passed to its base constructor) even before anyone updates the
`switch`. Good defensive design to replicate: make the fallback arm read the
exception's own carried status rather than hardcoding 500 for anything unrecognized.

---

## 9. Migrations convention

Standard `dotnet ef migrations add <Name>` workflow, applied automatically at startup
in `Program.cs` rather than requiring a manual `dotnet ef database update` step per
environment:

```csharp
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
    await SeedData.seedAuthDataAysnc(services);
}
```

This is appropriate for a single-instance/simple-deployment target (confirmed by
`Program.cs`'s own comments referencing Render as the host) — for a multi-instance
production deployment, auto-migrate-on-boot has a well-known race (N instances booting
simultaneously all try to run the same migration), so treat this as "fine for this
project's actual deployment shape," not a universal recommendation; a project
deploying multiple concurrent instances should run migrations as a separate release
step instead.

Migration filenames are timestamp-prefixed (`20260816162146_AddOrderStoredTotal.cs`),
EF Core's default. Names describe **intent**, not mechanism (`AddOrderStoredTotal`,
`ClearLocalDiskImageUrls`, `FloorNegativeProductStock`, `HardenPasswordResetOtp`) —
readable as a changelog on their own.

**Two distinct migration shapes are both present and both treated as first-class:**

1. **Schema migrations** — add/alter a column, index, or constraint. Both `Up()` and
   `Down()` are fully implemented, symmetric, reversible.

2. **Data-only / backfill migrations** — a `migrationBuilder.Sql(...)` call correcting
   or seeding row data, sometimes alongside a schema change in the same migration (see
   `AddOrderStoredTotal` below), sometimes standing alone (`ClearLocalDiskImageUrls`,
   `FloorNegativeProductStock`). **`Down()` is deliberately left empty, with a comment
   explaining why reversal is not meaningful — not silently omitted.**

```csharp
// 20260812130302_ClearLocalDiskImageUrls.cs
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Image storage moved from the container's local disk (wiped by
    // Render's ephemeral filesystem on every redeploy/restart) to
    // Cloudinary. Any row still holding an old "/uploads/..." path
    // points at a file that is already gone and will never come
    // back - clearing it lets the app fall back to its normal
    // no-image placeholder instead of a permanently-broken image.
    migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrl\" = NULL WHERE \"ImageUrl\" LIKE '/uploads/%';");
    migrationBuilder.Sql("UPDATE \"Users\" SET \"LogoUrl\" = NULL WHERE \"LogoUrl\" LIKE '/uploads/%';");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    // Irreversible by design - the original local-disk paths aren't
    // meaningful to restore, since those files no longer exist.
}
```

A schema+backfill combination migration (`AddOrderStoredTotal`) shows the boundary
precisely: the **schema** part (`AddColumn`) is trivially reversible
(`DropColumn`), so `Down()` implements exactly that — even though the *data* the
`Up()` backfill computed cannot be un-derived, the column itself can still be cleanly
dropped, which is all a schema-level rollback needs to guarantee.

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<decimal>(name: "Total", table: "Orders", type: "numeric", nullable: false, defaultValue: 0m);
    // Backfill every existing row using the OLD (pre-migration) formula, computed
    // from that row's own OrderItems/Discount/CashDiscount - freezes each
    // already-issued invoice's Total at exactly what it displayed before this
    // migration; only a future create/edit uses the new formula from here on.
    migrationBuilder.Sql(@"UPDATE ""Orders"" o SET ""Total"" = ROUND(( ... ), 0) / 2;");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(name: "Total", table: "Orders");
}
```

**The rule to replicate:** a no-op `Down()` is acceptable exactly when the forward
operation destroys information that cannot be reconstructed (overwriting/deleting
data based on an external fact, like "these files no longer exist on disk") — and it
must always carry a comment saying so, so a future reader doesn't mistake it for a
lazy omission. When only the schema shape needs to roll back cleanly (even if the data
computed by `Up()` can't be un-derived), implement that much of `Down()` — don't blank
it out just because the migration also happens to contain a data step.

---

## 10. Rate limiting / cross-cutting middleware

**`Microsoft.AspNetCore.RateLimiting`, policy-based, fixed-window, IP-partitioned,**
configured once in `Program.cs` and applied per-endpoint via `[EnableRateLimiting]`.

Policy name constants are centralized specifically to keep the registration and the
attribute usage from drifting apart silently:

```csharp
// Fatora.API/RateLimitPolicies.cs
// Policy names shared between the registrations in Program.cs and the
// [EnableRateLimiting] attributes on the endpoints they protect, so a rename
// can never silently leave an endpoint unlimited.
public static class RateLimitPolicies
{
    public const string SignIn = "sign-in";
    public const string PasswordRecovery = "password-recovery";
}
```

Two distinct policies, deliberately tuned to *very* different tightness, each with a
long code comment explaining the reasoning — worth reading as a model for how to
actually justify a rate limit instead of picking a round number:

```csharp
options.AddPolicy(RateLimitPolicies.SignIn, context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 200, Window = TimeSpan.FromMinutes(5) }));

options.AddPolicy(RateLimitPolicies.PasswordRecovery, context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(15) }));
```

`SignIn` is generous (200/5min) specifically because sign-in is a *shared* IP bucket
in this deployment (mobile clients behind carrier NAT) and the client itself
silently auto-retries a request that timed out against a slow-to-wake host — the
comment walks through the exact multiplier (one human login can cost 2-3 real
requests) that justifies the number **not** being tight. `PasswordRecovery` is
deliberately strict (5/15min) because it's a low-frequency, high-value action (a
6-digit OTP guess) where a generous limit would defeat the OTP's own keyspace
protection. **The lesson to replicate is not the specific numbers — it's "derive the
limit from the real request-amplification behavior of your actual client, and write
down the arithmetic," not "pick a limit that feels safe."**

Applied per-action:

```csharp
[EnableRateLimiting(RateLimitPolicies.SignIn)]
[HttpPost("login")]
public async Task<IActionResult> login(LoginRequest request) { ... }
```

**Middleware ordering is explicit and commented for exactly the two lines where order
is load-bearing** — worth quoting as a general lesson about `UseForwardedHeaders`:

```csharp
// Both lines are order-critical. UseForwardedHeaders must come first, because
// the limiter partitions on the client IP and that is only the real caller once
// the forwarded headers have been applied - without it every request behind the
// platform's edge proxy would share a single bucket. UseRouting must come
// before the limiter for the [EnableRateLimiting] endpoint metadata to be
// resolved at all; it is stated explicitly rather than relying on the one
// WebApplication auto-inserts, because the day anyone adds a UseRouting of
// their own further down the pipeline, all five policies would silently stop
// applying - no error, no failing test.
app.UseForwardedHeaders(forwardedHeadersOptions);
app.UseRouting();
app.UseRateLimiter();
```

Full pipeline order, for reference (`Program.cs`, bottom half):

```
UseForwardedHeaders  → UseRouting → UseRateLimiter
→ (Development: MapOpenApi | else: UseHttpsRedirection)
→ UseStaticFiles
→ UseExceptionHandler → UseStatusCodePages
→ UseAuthentication → UseAuthorization
→ MapControllers
```

`AddControllers(options => options.Filters.Add<AccountStatusFilter>())` registers a
**global MVC authorization filter** (not middleware) as the other cross-cutting
mechanism in this codebase — see §6 for what it does (per-request account-standing
re-check). The project uses only one of these two extension mechanisms per concern:
middleware for infrastructure-level cross-cutting behavior that needs to run before
routing/auth even resolves (forwarded headers, rate limiting), and an MVC filter for
behavior that needs the already-authenticated `ClaimsPrincipal` and per-action
metadata to make its decision. Don't reach for a custom middleware when what you
actually need is "run after auth, before the action, with access to `User`" — that's
exactly what an `IAsyncAuthorizationFilter` is for.

Logging is the built-in `ILogger<T>` (Microsoft.Extensions.Logging), used directly —
`GlobalExceptionHandler` logs every unhandled exception with the request's
`TraceIdentifier` before mapping it to a response, so a `traceId` returned to the
client in the `ProblemDetails` body can be correlated back to the exact server-side
log entry. No third-party structured-logging framework (Serilog, NLog) is present in
either `.csproj` — the built-in provider is used as-is.
