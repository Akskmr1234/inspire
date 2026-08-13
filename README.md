# Inspire ERP

A multi-tenant, multi-firm, multi-branch ERP platform: accounting, inventory with batch and serial tracking, sales, purchase, manufacturing, and mobile-device service management — with configurable menus, dashboards, reports, print layouts, and workflows.

Built for both Gulf (VAT) and Indian (GST) tax regimes, in English and Arabic with full RTL support.

> **Specification:** [`docs/SPEC.md`](docs/SPEC.md) is the canonical functional spec, distilled from `Inspire_web.docx` and the 14 reference screenshots of the legacy *Easy Retail* Windows application and the *mysalebooks.com* web reference. Where the prose and the screenshots disagreed, both are recorded along with the resolution. Read it before writing a feature.

---

## Current status

This is an in-progress build. What follows is accurate as of the last commit — no module is claimed complete unless it is.

| Area | State |
|---|---|
| Canonical specification | **Done** — `docs/SPEC.md`, incl. 8 open questions for the business, all now answered |
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
| Accounting — bounced cheque reversal, raised automatically or supplied | **Done** — 11 tests; the journal is written by the bounce itself |
| Dynamic menus — DB-driven tree, permission-filtered, seeded per firm | **Done** — 15 domain + 9 application + 5 API tests |
| Dynamic menus — administration: add, rename, reorder, regroup, hide, delete | **Done** — 8 API tests, with a screen at `/settings/menu` |
| Data grid — sort, search, columns, freeze, CSV export, saved layouts | **Done** — 8 API tests; first screen is the chart of accounts |
| Dashboards — role-assigned, KPI/trend/ranked panels, seeded accounting overview | **Done** — 8 API tests |
| Dashboards — custom SQL widgets, read-only and RLS-confined | **Done** — 20 guard + 11 API tests |
| Report builder, print designer, workflow engine | Not started |
| Inventory — masters: units with conversion groups, categories, brands, warehouses | **Done** — 33 domain + 10 API tests, with screens on the data grid |
| Inventory — product master: rates, units, tracking, barcodes (domain + persistence) | **Done** — 45 tests |
| Inventory — product master screens and API | **Done** — 13 API tests, with a three-tab editor |
| Inventory — stock operations and average costing | **Done** — 34 domain + 19 API tests, with screens and 3 reports |
| Inventory — batch tracking: per-batch cost, expiry, generated numbers | **Done** — 34 domain + 13 API tests, with a batch column and 2 reports |
| Inventory — serial numbers: per-unit identity, warranty, selection on sale | **Done** — 12 domain + 8 API tests, with a unit column on the entry grid |
| Inventory — per-firm account map for stock postings (answer to Q8a) | **Done** — 7 domain tests, seeded per firm |
| Inventory — stock movements posted to the nominal ledger | **Done** — 5 API tests; inventory now appears in the trial balance |
| Accounting — additional-charge matrix (§9), seeded per firm | **Done** — 8 domain tests; Round Off is the only default |
| Accounting — credit position of a party, read from open bills | **Done** — 4 API tests; warns rather than blocks |
| Sales — invoice aggregate: lines, tax per component, charges, rounding | **Done** (domain + persistence) — 15 tests |
| Accounting — per-firm tax account map, head by head, output and input | **Done** — 5 domain + 5 integration tests, seeded per regime |
| Sales — the journal a posted invoice raises, under either tax regime | **Done** — 11 domain tests; see the note below on rounding |
| Sales — posting: goods issued, bill raised, books written, in one transaction | **Done** — 13 application tests |
| Sales — entering an invoice, and the API for both | **Done** — 10 application + 7 API tests, at `/api/v1/sales/invoices` |
| Sales — customer master, with §12.1's mobile-number lookup | **Done** — 11 application + 7 API tests, at `/api/v1/sales/customers` |
| Sales — cancelling a posted invoice: goods back, debt withdrawn, books reversed | **Done** — 4 domain + 8 application + 3 API tests |
| Sales — the return document and its journal, with its own contra-revenue account | **Done** — 9 domain tests |
| Sales — posting a return: goods back at their own cost, credit against the bill | **Done** — 10 application + 1 API test, on the same endpoints |
| Sales — a filtered, paged list of documents; first paged list in the API | **Done** — 8 integration + 2 API tests |
| Sales — screens: document list, entry, posting, cancellation, returns | **Done** — verified end to end in a browser against a real database |
| Sales — batch and serial selection on the entry grid | **Done** — a batched sale driven through the UI to the stock ledger |
| Sales — customer master screen, on the existing master pattern | **Done** — created, listed and withdrawn through the UI |
| Data grid — server-side paging, for lists that outgrow the browser | **Done** — sorting and search withdraw in paged mode |
| Accounting — VAT and GST returns: output tax, input tax, summary (§7.3) | **Done** — 16 integration + 7 API tests; the input side reads purchase documents |
| Accounting — the returns screen, one for both regimes | **Done** — verified in a browser, English and Arabic |
| Purchase — invoice aggregate: lines, input tax per component, charges, rounding | **Done** (domain + persistence) — 26 domain tests |
| Purchase — the journal a posted purchase raises, through a goods-received account | **Done** — 21 domain tests; see the note below |
| Purchase — posting: goods received, bill raised, books written, in one transaction | **Done** — driven end to end in the API suite |
| Purchase — entering a purchase, and the API for both | **Done** — 8 API tests, at `/api/v1/purchase/invoices` |
| Purchase — supplier master | **Done** — 3 API tests, at `/api/v1/purchase/suppliers` |
| Purchase — a filtered, paged list of documents | **Done** — the same shape the sales list takes |
| Purchase — cancelling a posted purchase: goods off the shelf, debt withdrawn, books reversed | **Done** — 5 API tests, driven through the screen in both languages |
| Purchase — screens: document list, entry, posting, cancellation; supplier master | **Done** — verified end to end in a browser against a real database |
| Manufacturing, Service modules | Not started |
| Keycloak / SSO (deferred by request — plain JWT in its place) | Deferred |

**Test suite:** 1,145 passing, 0 failing (567 domain + 261 application + 205 API + 20 identity + 92 integration).

> **Coverage note:** every layer now has tests of its own — domain invariants, persistence and tenant isolation, use-case handlers, and the HTTP edge through a real in-memory host. The API suite boots the application against a PostgreSQL container and exercises authentication, refresh rotation, permission enforcement, and the ProblemDetails contract end to end.

Integration tests need a running Docker daemon.

> ### A bounced cheque now reverses itself, and an operator may still write the entry instead
>
> When a received cheque bounces, three things happen in one transaction: the bills its receipt settled are released, the cheque is marked dishonoured, and a reversing journal is raised — the party debited again, `Cheques in Hand` credited. Where the bank's charge is stated on the bounce, the same journal debits `Bank Charges` and credits the account the cheque was deposited to, so the whole event reaches the books at once rather than in two pieces of which the second is easily forgotten.
>
> This was deliberately left manual until 2026-08-10, on the grounds that which control account a dishonour comes out of is a firm's own choice of chart. The business then made that choice, and the accounts joined the per-firm map that stock and sales already post through — at which point leaving it manual stopped being caution and became an unfinished job.
>
> **The operator's route is untouched.** `reversalVoucherId` on `POST /cheques/{id}/bounce` still names an entry somebody has already written, and where it is given nothing is raised: a second journal appearing beside a hand-written one helps nobody. `POST /cheques/{id}/reversal` also still exists, for cheques bounced before this landed and left with no reversal named.
>
> **`dishonourSuspense` is not among the accounts**, though the business named it. A cheque can only bounce from *deposited* — paid in, not yet cleared — so the money is still in cheques in hand and the reversal is simply the receipt backwards. There is no moment where it sits in neither place, and an account that would only ever hold zero is one more line on a trial balance to wonder about. If a firm's own receipt posting moves cheques somewhere else on deposit, that is the case suspense would serve, and it can be added then.


> ### The returns can be filed on, and the input side has no taxable value to state
>
> `GET /api/v1/accounting/reports/{output-tax,input-tax,tax-summary}` produce §7.3's three reports, and one set of endpoints serves both regimes: a Qatar firm is answered in VAT, an Indian firm in CGST, SGST, IGST and cess. Only posted documents count; credit notes net off the period they fall in; the taxable value is counted **once per line**, so a GST supply carrying two heads is not reported as twice the sales.
>
> **The input side now has a base**, read from the purchase documents the way the output side reads the sales ones: the supplier, their own tax invoice number, the rate, and the value the tax was charged on. Counted once per line, so a GST purchase carrying CGST and SGST reports its base once and its tax twice — the same trap the output side avoids and the same fix. A debit note reduces the period it falls in.
>
> **Tax booked by hand is still listed, and says it has no base.** Replacing the ledger-posting route outright would have been the tidy change and the wrong one: a journal written into an input account is still input tax sitting in the ledger, and dropping it from the listing while it stays in the accounts would make the return understate what is reclaimable. So both are read — the postings a purchase's own journal made are excluded by identity, and whatever is left is listed with the base absent rather than filled with a figure derived from the rate. The screen says which rows those are.
>
> **The summary reconciles both directions now.** `InputDifference` should always be nought, because the input listing counts hand-written postings as well as purchases; anything left over is tax on a ledger the return cannot see at all — an account mapped to a head after the postings were made, most likely.
>
> **Driven through the screen against a real database**, with every case on the books at once: a purchase of 500 at 5%, a debit note taking 100 of it back, a zero-rated purchase of 300, a sale of 480, and 45 of customs VAT booked straight into the input account by hand. The return read taxable supplies 480, taxable purchases 400, zero-rated purchases 300, output 24, input 65, net **−41** reclaimable — and the input listing showed all three rows: the purchase with its supplier invoice number and a base of 500, the debit note at −100, and the journal with its narration where a supplier would be, its base an em dash. Input on the ledger 65, input difference **0** — the purchase's own journal counted once, not twice. In English and Arabic.
>
> **The summary reconciles itself against the ledger.** Each head shows what the documents charged beside what that head's account actually moved by. `isReconciled: false` means output tax reached the books by some route other than a sales document, and a return built from the documents alone would understate it. It is surfaced, not corrected — only a person can say which figure is right.
>
> **The screen is at `/accounting/tax-returns`**, one page for both regimes with the summary and the two listings behind a tab each. One menu entry, not one per regime: Q1 asked for report menus filtered by regime so a VAT firm never sees a GST return, and a report that answers in the heads a firm actually charges leaves nothing to filter.

> ### A purchase can be made end to end over HTTP, and the goods wait in a clearing account
>
> `POST /api/v1/purchase/suppliers` creates somebody to buy from; `POST /api/v1/purchase/invoices` enters a draft; `POST /api/v1/purchase/invoices/{id}/post` receives the goods, raises the bill and writes the books. The API suite drives all of it against a real database — a supplier created, a purchase entered, posted, and the trial balance afterwards showing stock up 100, input VAT 5, the supplier owed 105, and `Goods Received Not Invoiced` back at nothing.
>
> **The same supplier invoice twice is refused by name**, not by a unique index. Keying it twice is reclaiming its input tax twice, which is caught by an assessor rather than by a reader of the creditors report; the handler asks first and answers `409`, and the index stays as the backstop.
>
> **A batched purchase opens the batch.** This is where a purchase stops looking like a sale: the batch number is what the supplier printed on the carton and does not exist in the register until the receipt posts. The line carries it as text, and the receipt opens the batch, records what it cost, and puts the goods in it — through the same section 10 machinery a material receipt already uses, rather than a second implementation of it.
>
> **All of it is now reachable from the app**, at `/purchase/invoices` and `/purchase/suppliers`, in English and Arabic. The whole chain was driven through a browser against a real database before this was written: a supplier created on its own screen, a purchase of four widgets at 25 entered as a draft, posted, and the trial balance afterwards showing stock 100, input VAT 5, the supplier owed 105 — and `Goods Received Not Invoiced` at nothing, which is the model's own claim about itself.
>
> **The entry screen types batches rather than choosing them**, which is where it stops looking like the sales one. A sale offers the batches a warehouse holds; a purchase is usually the moment a batch comes into existence, so the number is keyed off the carton and an expiry date sits beside it. Serial numbers are keyed the same way, one per line, and the count is shown against the quantity so a short list is visible before the server refuses it.
>
> **Both paths were driven through the screen** against a real database. A purchase of four widgets at 25 entered and posted in Arabic, then cancelled with a reason typed in Arabic — status `ملغاة`, the shelf empty, the supplier owed nothing, and the trial balance back to no rows at all. Then a second purchase whose goods were issued away, where the cancel button produces the server's own refusal in the screen's error banner: *"WIDGET: Only 0.000000 of the 4.000000 received is still on hand, so the receipt can no longer be reversed."* The document stayed posted and the books were untouched, which is the transaction doing its job.
>
> **Cancelling can be refused because the goods are gone**, which is where it stops mirroring a sale's. Cancelling a sale puts stock back on a shelf and always can; cancelling a purchase takes stock off one, and a purchase whose goods have since been sold has nothing left to remove. The stock document's own reversal refuses it, and that is the right answer — what the firm has is a return or a write-off, not a mistake to undo. A purchase already paid against is refused too, by name, for the reason a paid sale is: a payment has to stay where it was made.

> ### A cancelled credit note used to leave the sale it settled looking paid
>
> Found on 2026-08-13 while building the purchase side's mirror of it, and fixed in both. A return raises no bill of its own — it allocates against the bill of the document it names. Cancelling one cancelled its journal, which took the party's ledger back, but nothing removed the allocation: the original sale went on reading as settled by a credit note that no longer existed, and the debtors report understated what was owed. `Bill.ReleaseAllocationsFrom` already existed for exactly this and had one caller, the bounced cheque.
>
> The two cancellations now do the same thing, which is the point of them being mirrors — and the reason the purchase side was worth writing as a mirror rather than as a variation.

> ### Two journals in one posting used to fight over the same numbering series
>
> Found by the first purchase ever posted in a fresh firm. Posting one raises two journals — the receipt's, which moves inventory, and the invoice's, which moves the supplier — and both take their number from the firm's journal series. Where that series did not exist yet, neither reservation could see the other's row, because the first had only been added to the change tracker and not written. Both created it, and the save failed on `ix_numbering_series_scope` with a message about an index.
>
> It was never a purchase problem. The first sale posted in a firm whose stock had arrived by some route that raised no journal would have hit it in exactly the same way; the existing tests missed it because their fixtures receive stock first, which creates the series and saves it. `FindForUpdateAsync` now reads the change tracker as well as the database, so the second reservation takes a number from the series the first one opened.

> ### The purchase document and its journal
>
> **The specification gives purchase no section of its own.** It appears throughout §7, §9, §10 and §12 as the counterpart of sales — the charge matrix lists `Purchase`, `PurchaseOrder` and `PurchaseReturn`; §10 describes batches arriving on it; §7.3 asks for input tax and overdue purchase invoices — but there is no numbered chapter to build from. (§13 is Manufacturing.) So the module was built as the mirror of §12 wherever the two are one thing pointed two ways, and diverges only where the direction genuinely changes what a document means.
>
> The purchase document exists as its own aggregate — its own tables, its own numbering, invoice and return in one type as the sales side has — and `PurchaseJournal` turns a posted one into what it did to the accounts. The document is deliberately a mirror of `SalesInvoice` rather than a direction flag on it, which is roughly seven hundred lines of shape said twice. The two read the same at a distance and diverge in every detail that matters: a sale selects goods that exist and a purchase brings goods into existence, a sale is numbered by the firm and a purchase carries the supplier's number as well, a sale credits revenue and a purchase debits a clearing account. Sharing them would have produced one type where half the fields are always null.
>
> **The goods sit in `Goods Received Not Invoiced` between arriving and being billed.** The receipt debits inventory and credits that account; the invoice debits it back, debits input tax head by head, and credits the supplier. The two halves cancel, so a firm that receives and invoices everything ends the period with nothing in it — and whatever *is* in it is exactly the goods received and not yet invoiced, which is a figure an accountant otherwise rebuilds from delivery notes at a year end. It is a current liability, not a suspense account, because that is what it is.
>
> **A purchase return needs no contra account**, which is where the mirror stops. A sales return has one because gross sales and returns are both figures somebody reports; a purchase return credits back the same clearing account it debited, because netting that account to zero is the whole point of it.
>
> **Freight on a purchase is charged, not capitalised.** Landed costing — spreading a carriage charge across the units it carried — is a separate thing the specification does not ask for yet, and doing it halfway would put a figure into stock valuation no report could explain.
>
> **Freight is charged, not capitalised, and a purchase return leaves at the average.** Goods going back to a supplier are valued the way every other issue is - at what the position says they cost - so a return whose agreed price has drifted from the average leaves that difference in the clearing account. It is a purchase price variance, and it is left visible there rather than swept into an account nobody asked for.

> ### Round Off is wired up, and nothing yet produces a figure to put in it
>
> A sale's journal debits the customer, credits revenue and each tax head, posts each charge to its own ledger, and puts whatever is left over into the firm's `Round Off` account — which is what makes it balance by construction rather than by arithmetic that happens to agree.
>
> **That residual is currently always nil.** The tax engine returns both the taxable amount and the tax already rounded to the currency's own scale, and `SalesInvoice.AddLine` refuses a line whose price implies finer precision than its assessment carries. So there is nothing left to round, and `Round Off` is a posting no invoice yet produces. The line stays because the guarantee it provides is the point, and because the day either of those two things changes is the day a journal would otherwise stop balancing.
>
> **Tax-inclusive entry is blocked by the same check.** An inclusive assessment's taxable amount is, by definition, not the quantity times the entered rate — so §9's reverse-tax setting cannot be switched on for sales until that check tells the two modes apart. Recorded rather than fixed here: it is a decision about what an invoice line means, and it belongs with the screen that enters one.
>
> Separately, the invoice total is now rounded to **its own currency's precision** rather than to two places. A dinar has three, and the previous arithmetic gave away a fils on every Kuwaiti, Bahraini or Omani invoice whose third decimal was not a zero.

> ### A sale can be made end to end over HTTP, and one thing it asks of the caller is not yet defaulted
>
> `POST /api/v1/sales/customers` creates somebody to bill and finds them again by the number on their phone; `POST /api/v1/sales/invoices` enters a draft; `POST /api/v1/sales/invoices/{id}/post` issues the goods, raises the bill and writes the books; `POST /api/v1/sales/invoices/{id}/cancel` puts all three back. That is the counter flow, and the API suite drives all of it against a real database — a customer created, goods received, an invoice raised, posted, and cancelled, with the trial balance and the customer's outstanding checked at each step.
>
> **All of it is now reachable from the app**, at `/sales/invoices` and `/sales/customers`, in English and Arabic. The whole chain was driven through a browser against a real database before this was written: a customer created, goods received, an invoice entered as a draft, posted, and the trial balance afterwards showing the customer debited 315, sales credited 300, output VAT 15, cost of goods sold 75 and stock down 75 — debits equal to credits.
>
> **Cancelling is for an invoice that should never have been raised.** Goods a customer actually took away come back as a **sales return** instead — the same two endpoints with `Kind: 2` and the invoice it is against on the body. It puts the goods back at the cost they left at, credits the customer, and settles the bill the sale raised. An invoice the customer has already paid against cannot be cancelled at all; that case is a return.
>
> **The tax rate is supplied per line rather than defaulted from the product.** §12.4 says it comes from the product master, which carries no tax rate at all. The caller states it, and the engine still decides which heads that rate splits into — so a GST firm gets CGST and SGST, or IGST across a state line, from the same figure. Adding a rate to the product master is a small change to it and a smaller one here; it has not been made because nobody has said whether the rate belongs to the product, its category, or both.

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

**Lists are paged from the sales list onwards.** `PagedResult<T>` — items, page, size, a counted total, and a 200-row ceiling — is the shape every list built after 2026-08-12 takes. The earlier ones (chart of accounts, stock documents) are unpaged and stay that way: one is small, the other is bounded by its date range, and retrofitting paging into them would change a contract screens already depend on.

**Configuration must never require a redeploy.** Menus, permissions, grid columns, dashboards, print layouts, numbering series, and workflow transitions are all rows in the database. If a feature would need a code change to reconfigure, it is not finished.

---

## Code quality

`dotnet build` must produce **zero warnings**. CI sets `TreatWarningsAsErrors`, so a warning breaks the build.

Analyzers: .NET built-in (`latest-recommended`), SonarAnalyzer, and StyleCop. Where a rule is disabled, [`backend/.editorconfig`](backend/.editorconfig) states *why* — so nobody has to guess later whether it was deliberate.

Conventions: nullable reference types enabled everywhere; XML documentation on public API (`CS1591` is a warning); British English in prose and comments.
