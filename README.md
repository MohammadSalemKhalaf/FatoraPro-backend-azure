# Fatora — Invoicing & Sales Tracking API

Fatora is a backend API for small businesses and their sales representatives to manage customers, products, invoices, and payment collection — without the overhead of a full accounting/ERP system.

It is intentionally simple: fast to use in the field, easy to reason about, and free of features (tax engines, multi-currency, double-entry bookkeeping) that a small business selling in a single currency, in person, does not need.

---

## Table of Contents

- [Who this is for](#who-this-is-for)
- [Tech Stack](#tech-stack)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Key Business Rules & Design Decisions](#key-business-rules--design-decisions)
- [Getting Started](#getting-started)
- [Authentication & Roles](#authentication--roles)
- [Features](#features)
- [API Reference](#api-reference)
- [End-to-End Example Flow](#end-to-end-example-flow)
- [Maintaining This Document](#maintaining-this-document)

---

## Who this is for

Fatora targets a specific, concrete business model — not a generic invoicing product:

- An **Admin** owns the business relationship. They visit shops in person, create a **Sales Rep** account for each one, and get paid directly by that shop (cash, outside the app) — there is no in-app subscription billing or payment processing.
- Each **Sales Rep** runs their own book of customers, products, and invoices. Data is strictly scoped per rep — one rep never sees another rep's data, and the Admin's role is account management (create/suspend/reactivate), not day-to-day data access.
- All money is in a single currency (**ILS**) with **no tax** — this was a deliberate, explicit decision, not an oversight. Do not reintroduce currency or tax fields without a new, explicit business decision.
- The app favors **speed and simplicity** over completeness. When a design choice had to pick between "accounting-grade correctness" and "fast, simple, good enough for a small shop," it picked the latter — see [Key Business Rules](#key-business-rules--design-decisions) below for the concrete trade-offs this produced.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| API | ASP.NET Core Web API (controllers, not Minimal API) |
| ORM | Entity Framework Core 10 |
| Database | PostgreSQL (via Npgsql) |
| Auth | JWT Bearer tokens + rotating refresh tokens |
| Password hashing | `PasswordHasher<T>` (ASP.NET Core Identity primitive, not the full Identity system) |
| Validation | FluentValidation (explicitly invoked per action) + DataAnnotations |
| Error handling | Custom exception hierarchy + `IExceptionHandler` → RFC 7807 `ProblemDetails` |
| File storage | Local disk (`wwwroot/uploads`), not cloud |

---

## Architecture

Fatora follows a strict 3-layer architecture with a one-way dependency rule:

```
Fatora.API  →  Fatora.BL  →  Fatora.DAL
(HTTP layer)   (business logic)  (data access / entities)
```

- **Fatora.DAL** — EF Core entities, `AppDbContext`, entity type configurations, migrations. Knows nothing about HTTP or business rules.
- **Fatora.BL** — Services (business logic), DTOs (requests/responses), custom exceptions. Depends on DAL, never on API.
- **Fatora.API** — Controllers, validators, file storage, JWT configuration, `Program.cs`. The only layer that knows about HTTP.

This direction is enforced deliberately — DAL must never reference BL. (Earlier in the project, `SeedData` was moved from DAL to `Fatora.BL/Utils` specifically to fix a circular reference that violated this rule.)

**Multi-tenancy**: every domain entity carries a `UserId`. Every service method takes the authenticated user's ID and filters/verifies ownership on every read and write. Cross-tenant access returns `404 Not Found`, never `403 Forbidden` — this avoids leaking whether a record exists at all to someone who doesn't own it.

**Error handling**: business-rule violations throw typed exceptions (`NotFoundException`, `ConflictException`, `UnauthorizedException`, `BadRequestException`, all deriving from `AppException`), caught centrally by `GlobalExceptionHandler` and translated into `ProblemDetails` responses with the correct HTTP status code. Controllers do not catch exceptions themselves.

**Authentication**: login issues a short-lived JWT access token plus a longer-lived refresh token. Only one active refresh token exists per user at a time (rotation, not multiple concurrent sessions). Roles (`Admin`, `SalesRep`) are enforced via `[Authorize(Roles = "...")]`.

---

## Project Structure

```
Fatora.API/
  Controllers/         HTTP endpoints — thin, delegate to BL services
  Validators/           FluentValidation rules per request DTO, grouped by domain
  Services/              API-layer infrastructure (e.g. local file storage)
  Extensions/            ClaimsPrincipal helpers (GetUserId, etc.)
  Exceptions/            GlobalExceptionHandler (AppException → ProblemDetails)
  GroupRegistrations.cs  DI container wiring for services + validators
  Program.cs             Pipeline configuration, migrations-on-startup, seeding

Fatora.BL/
  Services/
    Abstractions/         Interfaces (IOrderService, IUserService, ...)
    Classes/               Implementations — where business rules actually live
  DTOs/
    Requests/              Inbound request shapes
    Responses/             Outbound response shapes
  Exceptions/              AppException hierarchy
  Utils/                   SeedData (roles + initial admin)

Fatora.DAL/
  Entites/                 EF Core entity classes
  Data/
    AppDbContext.cs
    Configuration/          IEntityTypeConfiguration<T> per entity
    Migrations/             EF Core migration history — never edit past migrations
```

---

## Key Business Rules & Design Decisions

These are decisions that were deliberately made after discussion, not defaults — future work should not silently reverse them without a new explicit conversation:

- **No tax, single currency (ILS).** Rejected explicitly and repeatedly. Do not add tax fields.
- **Username/password auth only — no email required, no social login.** Many users don't have/use email.
- **Orders (invoices) have no separate `Payment` ledger entity.** `Order.PaidAmount` is a single stored, mutable field, directly incremented by `POST /api/orders/{id}/record-payment`. There is intentionally **no history of individual payment events** (no dates/notes per payment) — this app is not an accounting system. `Status` (`Sent` / `PartiallyPaid` / `Paid` / `Overdue`) is computed on read from `PaidAmount`, `Total`, and `DueDate` — never stored.
- **Orders can be permanently deleted by the owning Sales Rep**, cascading to their line items. This is a conscious trade-off: the rep is trusted and responsible for this decision; the app prioritizes an uncluttered invoice list over permanent audit history. (`Customer`/`Product` deletion is soft — see below — but `Order` deletion is hard, because there is no longer any child ledger record whose integrity needs protecting.)
- **`Customer` and `Product` use soft-delete** (`IsActive` flag, with `archive` / `restore` / `permanent-delete` endpoints). `Order` does not need this pattern since it has no undeletable children anymore.
- **Self-account-deletion vs. Admin-suspend are different, deliberate concepts:** a Sales Rep can permanently delete their *own* account (password-confirmed, full cascade — a security/exit decision). An Admin can only *suspend/reactivate* a rep's account (blocks login, keeps all data) — reserved for future subscription/access-control needs, not a substitute for deletion.
- **User-enumeration is explicitly guarded against** on login: a wrong username and a wrong password return the identical `401` message. Do not "improve" this by giving more specific error messages per case — that was a fixed vulnerability, not an oversight.
- **Money-sensitive fields are frozen at creation time.** `OrderItem.UnitPrice` is a snapshot of the product's price *at the moment the invoice was created* — it must never silently track later changes to `Product.SellPrice`.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL (local instance or reachable server)

### Setup

1. Clone the repository.
2. Configure the connection string in `Fatora.API/appsettings.json` (`ConnectionStrings:DefaultConnection`) and, in a real deployment, replace `JwtSettings:SecretKey` with a secret pulled from environment/secret storage rather than committed to source.
3. Apply database migrations and seed the initial Admin account + roles — this happens automatically on startup (`Program.cs` calls `dbContext.Database.MigrateAsync()` and `SeedData.seedAuthDataAysnc`), so you just need to run the app:
   ```bash
   cd Fatora.API
   dotnet run
   ```
4. By default this binds to `http://0.0.0.0:5050` (all network interfaces), so it's reachable both from `localhost` and from another device on the same network (e.g. a phone running the Flutter app over Wi-Fi) — useful for mobile development without HTTPS certificate friction. `GET /health` should return `{"message":"all up"}`.

### Testing endpoints

`Fatora.API/Fatora.API.http` contains a maintained, numbered list of example requests for every endpoint in the API — use it (via the REST Client extension in VS Code, or Rider's built-in HTTP client) as the fastest way to explore or manually verify behavior.

---

## Authentication & Roles

Two roles exist: `Admin` and `SalesRep`.

- There is exactly **one** Admin account, created by database seed on first run.
- The Admin creates `SalesRep` accounts via `POST /api/users/sales-reps` — sales reps cannot self-register.
- Every other business endpoint (`Customers`, `Products`, `Orders`, `Reports`, `Profile`) is `[Authorize(Roles = "SalesRep")]` — the Admin does not use these day-to-day; their job is account lifecycle management only.

Login (`POST /api/account/login`) returns a JWT access token (short-lived) and a refresh token (longer-lived). Use `POST /api/account/refresh-token` to get a new pair when the access token expires. `POST /api/account/logout` revokes the current refresh token immediately.

---

## Features

- **Auth**: login, refresh-token rotation, logout, change-password (revokes refresh tokens), self-delete-account (password-confirmed, full cascade).
- **Admin**: create Sales Rep accounts, suspend/reactivate accounts.
- **Products**: CRUD, image upload, soft-delete/archive/restore/permanent-delete.
- **Customers**: CRUD, soft-delete/archive/restore/permanent-delete.
- **Orders (Invoices)**: create/update/delete (cascade), computed `Status`, record payments, sequential per-rep invoice numbering (`INV-0001`, ...).
- **Dashboard**: order summary (count/total/paid/outstanding by status) filterable by Week/Month/Year/All.
- **Reports**: sales-over-time, items breakdown (most sold / top value), clients breakdown (most invoices / top value).
- **Profile**: business name, logo upload, bank details (for customers to transfer payment to).

---

## API Reference

This is a quick map of controllers and their base routes — see `Fatora.API.http` for the full, concrete, numbered request list.

| Controller | Base route | Notes |
|---|---|---|
| `AccountController` | `/api/account` | `login`, `refresh-token` (anonymous); `logout`, `change-password` (authenticated) |
| `UsersController` | `/api/users` | Admin-only: `sales-reps` (create), `{id}/suspend`, `{id}/activate` |
| `ProfileController` | `/api/profile` | Get profile, logo upload, bank details, self-delete |
| `CustomersController` | `/api/customers` | Full CRUD + archive/restore/permanent-delete |
| `ProductsController` | `/api/products` | Full CRUD + image upload + archive/restore/permanent-delete |
| `OrdersController` | `/api/orders` | Create/list/get/update/delete, `summary`, `{id}/record-payment` |
| `ReportsController` | `/api/reports` | `sales`, `items`, `clients` |

---

## End-to-End Example Flow

A concrete walkthrough of the whole system, start to finish — this is what "using Fatora" actually looks like.

**1. Admin creates a Sales Rep** (one-time, in person, when onboarding a new shop):
```
POST /api/users/sales-reps
Authorization: Bearer <admin-token>
{ "UserName": "khalil_shop", "Password": "...", "Name": "Khalil", "PhoneNumber": "...", "City": "Amman", "Street": "..." }
```

**2. The rep logs in** on their phone:
```
POST /api/account/login
{ "UserName": "khalil_shop", "Password": "..." }
→ { "accessToken": "...", "refreshToken": "...", "expires": "..." }
```

**3. The rep adds a customer:**
```
POST /api/customers
{ "Name": "Abu Ahmad", "PhoneNumber": "...", "Street": "...", "City": "..." }
```

**4. The rep adds a product to their catalog:**
```
POST /api/products
{ "Name": "Widget", "PurchasePrice": 5, "SellPrice": 15 }
```

**5. The rep creates an invoice** for that customer with line items:
```
POST /api/orders
{ "CustomerId": 1, "DueDate": "2026-08-15", "Discount": 0, "Items": [ { "ProductId": 1, "Quantity": 3 } ] }
→ Status: "Sent", Total: 45, PaidAmount: 0
```
`OrderItem.UnitPrice` is frozen at 15 the moment this is created — later changes to the product's `SellPrice` never retroactively affect this invoice.

**6. The customer pays part of it later; the rep records it:**
```
POST /api/orders/{id}/record-payment
{ "Amount": 20 }
→ Status: "PartiallyPaid", PaidAmount: 20, RemainingBalance: 25
```

**7. The customer pays the rest:**
```
POST /api/orders/{id}/record-payment
{ "Amount": 25 }
→ Status: "Paid", RemainingBalance: 0
```

**8. The rep checks their dashboard** at any point:
```
GET /api/orders/summary?period=Month
→ { "count": ..., "totalAmount": ..., "totalPaid": ..., "totalOutstanding": ..., "paidCount": ..., "overdueCount": ... }
```

**9. If an invoice was created by mistake or is no longer needed, the rep deletes it** — permanently, cascading to its items:
```
DELETE /api/orders/{id}
→ 204 No Content
```

**10. At the end of a busy week, the rep pulls a report:**
```
GET /api/reports/clients?period=Week&sortBy=TopValue
→ [ { "customerId": 1, "customerName": "Abu Ahmad", "invoiceCount": 4, "totalValue": 320 }, ... ]
```

---

## Maintaining This Document

**This README must be kept up to date.** Whenever a feature is added, changed, or removed, the relevant section here (Features, API Reference, Key Business Rules, and the example flow if it's affected) should be updated in the same body of work — not as a separate, later cleanup task.

The goal is that this file alone — read top to bottom — gives an accurate, current picture of what the system does and why, for a new engineer or an AI coding assistant picking up the project cold.
