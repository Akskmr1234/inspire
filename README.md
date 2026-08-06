# Inspire ERP

A multi-tenant, multi-firm, multi-branch ERP platform: accounting, inventory with batch and serial tracking, sales, purchase, manufacturing, and mobile-device service management — with configurable menus, dashboards, reports, print layouts, and workflows.

Built for both Gulf (VAT) and Indian (GST) tax regimes, in English and Arabic with full RTL support.

> **Specification:** [`docs/SPEC.md`](docs/SPEC.md) is the canonical functional spec, distilled from `Inspire_web.docx` and the 14 reference screenshots of the legacy *Easy Retail* Windows application and the *mysalebooks.com* web reference. Where the prose and the screenshots disagreed, both are recorded along with the resolution. Read it before writing a feature.

---

## Current status

This is an in-progress build. What follows is accurate as of the last commit — no module is claimed complete unless it is.

| Area | State |
|---|---|
| Canonical specification | **Done** — `docs/SPEC.md`, incl. 8 open questions for the business |
| Monorepo + solution scaffold (15 projects) | **Done** — builds clean, 0 warnings |
| Dependency set, security-scanned & licence-audited | **Done** — see [ADR 0002](docs/adr/0002-third-party-licensing.md) |
| Shared Kernel — `Result`/`Error`, `Entity`, `AggregateRoot`, domain events, tenancy & audit contracts, `Money`, `CurrencyCode` | **Done** — 36 tests |
| Domain — tenancy identifiers, `FinancialYear` | **Done** — 25 tests |
| Tax engine — GCC VAT + India GST concurrently, inclusive/exclusive, CGST/SGST/IGST | **Done** — 38 tests |
| API bootstrap — Serilog, ProblemDetails, versioning, Swagger, health checks | **Done** |
| Domain — Tenant, Firm, Branch aggregates | **Done** |
| Multi-tenancy — EF query filters + PostgreSQL RLS, verified on real Postgres | **Done** — 17 integration tests |
| Auth — JWT sign-in, refresh rotation with theft detection, DB-driven RBAC | **Done** — 20 tests |
| Domain — chart of accounts, double-entry vouchers, numbering series | **Done** |
| Application layer — CQRS pipeline, validation, repositories | **Done** |
| Seeding — permission catalogue, roles, chart of accounts, first administrator | **Done** |
| Accounting reports — Trial Balance, P&L, Balance Sheet, Ledger Statement | **Done** |
| Frontend — React shell, sign-in, voucher entry, report screens | **Done** |
| Docker Compose, API container image, GitHub Actions CI | **Done** |
| Accounting reports — Day Book, Cash Book, Bank Book | **Done** — 33 application tests |
| Accounting — bill-wise settlement (domain + persistence) | **Done** — 25 tests |
| Accounting — bills raised and settled by voucher posting | **Done** — 19 tests |
| Accounting — Debtors/Creditors and age-wise reports | **Done** — 23 application + 9 integration tests |
| Accounting — cheque lifecycle incl. PDC (domain + persistence) | **Done** — 34 tests |
| Accounting — cheques recorded on posting; bank, clear, bounce, stop, void | **Done** — 31 tests |
| Accounting — PDC report, PDC calendar, cheque register | **Done** — 15 application + 11 integration tests |
| Accounting — account group summary, voucher report, transaction summary | **Done** — 23 application + 15 integration tests |
| Accounting — cash flow statement (direct method) | **Done** — 15 application + 11 integration tests |
| Deployment — container images for API and web, platform configuration | **Done** — see [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) |
| Accounting — automatic ledger reversal for a bounced cheque | Not started — see below |
| Dynamic menus — DB-driven tree, permission-filtered, seeded per firm | **Done** — 15 domain + 9 application + 5 API tests |
| Report builder, print designer, workflow engine | Not started |
| Inventory, Sales, Purchase, Manufacturing, Service modules | Not started |
| Keycloak / SSO (deferred by request — plain JWT in its place) | Deferred |

**Test suite:** 619 passing, 0 failing (300 domain + 182 application + 54 API + 20 identity + 63 integration).

> **Coverage note:** every layer now has tests of its own — domain invariants, persistence and tenant isolation, use-case handlers, and the HTTP edge through a real in-memory host. The API suite boots the application against a PostgreSQL container and exercises authentication, refresh rotation, permission enforcement, and the ProblemDetails contract end to end.

Integration tests need a running Docker daemon.

> ### A bounced cheque does not yet reverse its ledger postings
>
> When a received cheque bounces, the bills its receipt settled **are** released — automatically, in the same transaction, and listed in the response. The ledger postings are **not** reversed.
>
> This is deliberate rather than overlooked. Which control account a bounced cheque comes back out of, and where the bank's charge for it goes, are a firm's own choice of chart; there is no configuration for either yet, and inventing one would mean posting into somebody's books on a guess. The `POST /cheques/{id}/bounce` response therefore carries `ledgerReversalRequired: true`, so a caller cannot mistake silence for completeness — a reversing journal is still owed.
>
> Closing this needs one of two things, and it is a decision for the business rather than for the code: a per-firm control-account map (cheques in hand, bank charges, dishonour suspense), or an operator-supplied reversing voucher passed with the bounce, the way `clear` already takes the voucher that posts the bank movement.

---

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | **10.0.302+** | The brief specified ASP.NET Core 9; only the .NET 10 SDK was present and 10 is current LTS, so projects target `net10.0`. |
| Node.js | 22+ | 24.14.1 verified |
| Docker | 29+ | Required for integration tests (Testcontainers) and local infrastructure |
| PostgreSQL | 16+ | Supplied by Docker Compose; no local install needed |

`pnpm` is **not** required — the frontend uses **npm workspaces**, which works with the npm you already have. Switching to pnpm later is a drop-in change.

---

## Getting started

```bash
git clone <repo> && cd Inspire
```

Build and test the backend:

```bash
dotnet test backend/ERP.slnx
```

Run the API:

```bash
dotnet run --project backend/src/ERP.Api
```

Swagger UI is then at `https://localhost:7001/swagger` (development only). Health endpoints:

- `GET /health/live` — process liveness, no dependencies checked
- `GET /health/ready` — readiness, including database and cache

---

## Repository layout

```
Inspire/
├── apps/
│   ├── web/                  React ERP client
│   └── admin/                platform/tenant administration
├── backend/
│   ├── src/
│   │   ├── ERP.SharedKernel      framework-free primitives; zero package refs
│   │   ├── ERP.Domain            aggregates, invariants, domain events
│   │   ├── ERP.Application       CQRS use cases, validation, abstractions
│   │   ├── ERP.Infrastructure    EF Core, PostgreSQL, Redis, Hangfire
│   │   ├── ERP.Identity          Keycloak/OIDC, permission engine, MFA
│   │   ├── ERP.Reporting         dynamic report builder, Excel/CSV/PDF export
│   │   ├── ERP.Notifications     in-app, email, SMS, WhatsApp, SignalR
│   │   ├── ERP.DynamicForms      role-based grid/field configuration
│   │   ├── ERP.PrintDesigner     drag-and-drop print templates
│   │   ├── ERP.Workflow          configurable document state machines
│   │   └── ERP.Api               composition root, /api/v1
│   └── tests/                    unit, integration, API
├── packages/                 shared TS: ui, types, hooks, utils
├── infrastructure/           nginx, keycloak realm, k8s manifests
├── docs/
│   ├── SPEC.md               canonical functional specification
│   └── adr/                  architecture decision records
└── scripts/
```

### Dependency direction

```
SharedKernel  ←  Domain  ←  Application  ←  Infrastructure
                                        ←  Identity / Reporting / Notifications
                                        ←  DynamicForms / PrintDesigner / Workflow
                                                                      ↑
                                                                    Api
```

Enforced by project references. `ERP.SharedKernel` has **zero** package references by design — nothing in it may depend on EF Core, MediatR, or ASP.NET Core, which is what keeps the domain model testable in isolation.

---

## Decisions worth knowing before you contribute

Full reasoning lives in [`docs/adr/`](docs/adr/). The short version:

**Tenant isolation is enforced twice** — an EF Core global query filter *and* a PostgreSQL row-level-security policy. One layer would be a single point of failure, and the failure mode (one customer reading another's financial data) is the worst outcome this system can produce.

> ### ⚠️ The application must never connect to PostgreSQL as a superuser
>
> PostgreSQL exempts superusers — and any role holding `BYPASSRLS` — from row-level security entirely. **`FORCE ROW LEVEL SECURITY` does not bind them.** Point the application at a superuser connection string and every isolation policy silently stops applying: no error, no warning, no visible change, until one customer sees another's books.
>
> This was caught by an integration test that initially reported 14 rows where 1 was expected, because Testcontainers' bootstrap user is a superuser.
>
> **Deployment requirement:** the app connects as a dedicated role created `NOSUPERUSER NOBYPASSRLS`, holding only `SELECT/INSERT/UPDATE/DELETE`. Schema ownership and migrations use a *separate*, more privileged role. `SchemaTests.The_application_role_cannot_bypass_row_level_security` fails the build if this is ever violated.

**AutoMapper is not used.** Every freely-licensed version carries an unpatched high-severity advisory (`GHSA-rvv3-g6hj-g44x`); every patched version requires a paid licence. Mapping is hand-written, which is also compile-time checked. See [ADR 0002](docs/adr/0002-third-party-licensing.md).

**MediatR is pinned to 12.5.0** — the last Apache-2.0 release. Feature code depends on our own messaging abstractions, so replacing it is mechanical.

**Money is never a bare `decimal`.** `Money` carries its currency and refuses cross-currency arithmetic. Currency scale is not assumed to be 2 — KWD, BHD, and OMR use 3 decimals and JPY uses 0. `Money.Allocate` distributes remainders so an apportioned discount always re-sums to the document total.

**Financial years are arbitrary date ranges.** The legacy system's year runs 01-10-2021 to 31-12-2026. Nothing may assume a 12-month period or a particular start month.

**The tax engine is jurisdiction-pluggable.** The screenshots show Qatari Riyal with VAT reports *and* Indian CGST/SGST/IGST ledgers. Tax components are data, selected by a per-firm regime — not hardcoded.

**Configuration must never require a redeploy.** Menus, permissions, grid columns, dashboards, print layouts, numbering series, and workflow transitions are all rows in the database. If a feature would need a code change to reconfigure, it is not finished.

---

## Code quality

`dotnet build` must produce **zero warnings**. CI sets `TreatWarningsAsErrors`, so a warning breaks the build.

Analyzers: .NET built-in (`latest-recommended`), SonarAnalyzer, and StyleCop. Where a rule is disabled, [`backend/.editorconfig`](backend/.editorconfig) states *why* — so nobody has to guess later whether it was deliberate.

Conventions: nullable reference types enabled everywhere; XML documentation on public API (`CS1591` is a warning); British English in prose and comments.
