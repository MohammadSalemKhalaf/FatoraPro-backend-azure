# Fatora — Invoicing & Sales Tracking API

Fatora is a backend API for small businesses and their sales representatives to manage customers, products, invoices, and payment collection — without the overhead of a full accounting/ERP system.

It is intentionally simple: fast to use in the field, and free of features (tax engines, multi-currency, double-entry bookkeeping) that a small business selling in a single currency, in person, does not need.

---

## Table of Contents

- [Who This Is For](#who-this-is-for)
- [Tech Stack](#tech-stack)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Key Business Rules](#key-business-rules)
- [Getting Started](#getting-started)
- [Authentication & Roles](#authentication--roles)
- [Features](#features)
- [API Reference](#api-reference)
- [Offline-First Sync](#offline-first-sync)
- [End-to-End Example](#end-to-end-example)

---

## Who This Is For

- An **Admin** owns the business relationship: creates a **Sales Rep** account for each shop in person, and is paid directly by that shop (cash, outside the app) — there is no in-app billing.
- Each **Sales Rep** manages their own customers, products, and invoices. Data is strictly scoped per rep; the Admin's role is account management only, not day-to-day data access.
- All money is a single currency (**ILS**), with no tax support.
- The app favors speed and simplicity over full accounting-grade correctness.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| API | ASP.NET Core Web API |
| ORM | Entity Framework Core 10 |
| Database | PostgreSQL (via Npgsql) |
| Auth | JWT Bearer tokens + rotating refresh tokens |
| Password hashing | `PasswordHasher<T>` |
| Validation | FluentValidation + DataAnnotations |
| Error handling | Custom exception hierarchy → RFC 7807 `ProblemDetails` |
| File storage | Local disk (`wwwroot/uploads`) |

---

## Architecture

A strict 3-layer architecture with a one-way dependency rule:

```
Fatora.API  →  Fatora.BL  →  Fatora.DAL
(HTTP layer)   (business logic)  (data access / entities)
```

- **Fatora.DAL** — EF Core entities, `AppDbContext`, entity configurations, migrations.
- **Fatora.BL** — Services (business logic), DTOs, custom exceptions. Depends on DAL, never on API.
- **Fatora.API** — Controllers, validators, file storage, JWT configuration.

**Multi-tenancy**: every domain entity carries a `UserId`. Every service call filters and verifies ownership on every read and write. Cross-tenant access returns `404`, never `403`, to avoid confirming a record's existence to someone who doesn't own it.

**Error handling**: business-rule violations throw typed exceptions (`NotFoundException`, `ConflictException`, `UnauthorizedException`, `BadRequestException`), caught centrally by `GlobalExceptionHandler` and translated into `ProblemDetails`. Controllers never catch exceptions themselves.

**Authentication**: login issues a short-lived JWT access token plus a longer-lived refresh token. Only one active refresh token exists per user (rotation, not concurrent sessions). Roles (`Admin`, `SalesRep`) are enforced via `[Authorize(Roles = "...")]`.

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
    Classes/               Implementations
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

## Key Business Rules

- **No tax, single currency (ILS).**
- **Username/password auth only** — no email, no social login.
- **No separate `Payment` ledger.** `Order.PaidAmount` is a single stored field, updated via `POST /api/orders/{id}/record-payment`. No history of individual payment events is kept. `Status` (`Sent` / `PartiallyPaid` / `Paid` / `Overdue`) is computed on read from `PaidAmount`, `Total`, and `DueDate`.
- **Orders can be permanently deleted** by the owning Sales Rep, cascading to their line items — the app favors an uncluttered invoice list over permanent audit history.
- **`Customer` and `Product` use soft-delete** (`IsActive`, with archive/restore/permanent-delete). `Order` deletion is hard, since it has no child records to protect.
- **Self-delete vs. Admin-suspend are distinct.** A Sales Rep can permanently delete their own account (password-confirmed, full cascade). An Admin can only suspend/reactivate a rep's account (blocks login, keeps all data) — reserved for future access-control needs.
- **Login is enumeration-safe**: a wrong username and a wrong password return the identical `401` message.
- **Money fields are frozen at creation time.** `OrderItem.UnitPrice` snapshots the product's price at invoice creation and never tracks later `Product.SellPrice` changes.
- **`Customer.Id`, `Product.Id`, and `OrderItem.Id` are `Guid`**, generated client-side, to support offline creation. `Order.Id` was already `Guid`. `User.Id`/`RefreshToken.Id` remain server-only concerns.
- **`Customer`, `Product`, and `Order` track `CreatedAt`/`UpdatedAt`**, stamped centrally by `AppDbContext.SaveChanges` — never set manually in a service.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL (local instance or reachable server)

### Setup

1. Clone the repository.
2. Configure the connection string in `Fatora.API/appsettings.json` (`ConnectionStrings:DefaultConnection`). In production, `JwtSettings:SecretKey` should come from environment/secret storage, not source control.
3. Run the app — migrations and seed data (roles + initial Admin account) apply automatically on startup:
   ```bash
   cd Fatora.API
   dotnet run
   ```
4. Binds to `http://0.0.0.0:5050` by default (all network interfaces), reachable from `localhost` or another device on the same network. `GET /health` should return `{"message":"all up"}`.

### Testing Endpoints

`Fatora.API/Fatora.API.http` contains a numbered example request for every endpoint — usable directly via the REST Client extension in VS Code, or Rider's built-in HTTP client.

---

## Authentication & Roles

Two roles exist: `Admin` and `SalesRep`.

- There is exactly one Admin account, created by database seed on first run.
- The Admin creates `SalesRep` accounts via `POST /api/users/sales-reps` — reps cannot self-register.
- Every other business endpoint (`Customers`, `Products`, `Orders`, `Reports`, `Profile`, `Sync`) is `[Authorize(Roles = "SalesRep")]`.

Login (`POST /api/account/login`) returns a JWT access token and a refresh token. `POST /api/account/refresh-token` rotates them. `POST /api/account/logout` revokes the current refresh token immediately.

---

## Features

- **Auth**: login, refresh-token rotation, logout, change-password, self-delete-account.
- **Admin**: create Sales Rep accounts, suspend/reactivate accounts.
- **Products**: CRUD, image upload, soft-delete/archive/restore/permanent-delete.
- **Customers**: CRUD, soft-delete/archive/restore/permanent-delete.
- **Orders (Invoices)**: create/update/delete, computed status, record payments, sequential per-rep invoice numbering (`INV-0001`, ...).
- **Dashboard**: order summary (count/total/paid/outstanding by status), filterable by period.
- **Reports**: sales-over-time, items breakdown, clients breakdown.
- **Profile**: business name, logo, bank details.
- **Offline sync**: batch push/pull so the mobile app works with no connection.

---

## API Reference

Quick map of controllers — see `Fatora.API.http` for the full request list.

| Controller | Base route | Notes |
|---|---|---|
| `AccountController` | `/api/account` | `login`, `refresh-token` (anonymous); `logout`, `change-password` |
| `UsersController` | `/api/users` | Admin-only: `sales-reps` (create), `{id}/suspend`, `{id}/activate` |
| `ProfileController` | `/api/profile` | Get profile, logo upload, bank details, self-delete |
| `CustomersController` | `/api/customers` | Full CRUD + archive/restore/permanent-delete |
| `ProductsController` | `/api/products` | Full CRUD + image upload + archive/restore/permanent-delete |
| `OrdersController` | `/api/orders` | Create/list/get/update/delete, `summary`, `{id}/record-payment` |
| `ReportsController` | `/api/reports` | `sales`, `items`, `clients` |
| `SyncController` | `/api/sync` | `push` (batch upsert), `pull?since=` (delta download) |

---

## Offline-First Sync

**Why `Guid` ids**: a device generates its own id for anything created offline, since it cannot ask the server for the next id without a connection. That's why `Customer`, `Product`, `Order`, and `OrderItem` all use `Guid` keys.

**Push** — `POST /api/sync/push`: the device sends every locally-pending change in one batch, each item carrying its client-generated id and the real device-side edit timestamp (`UpdatedAt`). The server responds per-item (`Applied`, `Conflict`, `Rejected`, or `Deleted`) — one bad record never blocks the rest of the batch.

**Pull** — `GET /api/sync/pull?since={timestamp}`: returns everything changed for that user since the given time, plus the server's own clock (`serverTime`), which the client stores as the watermark for its next pull.

**Conflicts resolve last-write-wins by `UpdatedAt`.** An incoming edit older than or equal to the server's copy is rejected as a `Conflict`; the device should pull to reconcile. This assumes one device per rep, not conflict-free multi-device merging.

**Invoice numbers are always assigned server-side**, at the moment an order is pushed — never by the device.

**Orders deleted offline** are sent via `DeletedOrderIds` in the push payload and hard-deleted server-side. There is no delete-tombstone in the pull response — this would need revisiting if multi-device-per-rep is ever supported.

---

## End-to-End Example

**1. Admin creates a Sales Rep:**
```
POST /api/users/sales-reps
Authorization: Bearer <admin-token>
{ "UserName": "khalil_shop", "Password": "...", "Name": "Khalil", "PhoneNumber": "...", "City": "Amman", "Street": "..." }
```

**2. The rep logs in:**
```
POST /api/account/login
{ "UserName": "khalil_shop", "Password": "..." }
→ { "accessToken": "...", "refreshToken": "...", "expires": "..." }
```

**3. The rep adds a customer and a product:**
```
POST /api/customers
{ "Name": "Abu Ahmad", "PhoneNumber": "...", "Street": "...", "City": "..." }

POST /api/products
{ "Name": "Widget", "PurchasePrice": 5, "SellPrice": 15 }
```

**4. The rep creates an invoice** (`CustomerId`/`ProductId` are the `Guid`s from above):
```
POST /api/orders
{ "CustomerId": "<customer-guid>", "DueDate": "2026-08-15", "Discount": 0, "Items": [ { "ProductId": "<product-guid>", "Quantity": 3 } ] }
→ Status: "Sent", Total: 45, PaidAmount: 0
```

**5. Payments are recorded as they come in:**
```
POST /api/orders/{id}/record-payment
{ "Amount": 20 }
→ Status: "PartiallyPaid", RemainingBalance: 25
```

**6. The rep checks their dashboard or pulls a report:**
```
GET /api/orders/summary?period=Month
GET /api/reports/clients?period=Week&sortBy=TopValue
```
