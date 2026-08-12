# Inspire ERP — Canonical Functional Specification

> **Sources of truth**
> 1. `Inspire_web.docx` — *Enterprise Resource Planning (ERP) System Requirements Specification* (prose).
> 2. The 14 reference screenshots embedded in that document (legacy Windows app **“Easy Retail : STARTECH 011”** and the web reference **mysalebooks.com**).
> 3. The engagement brief (technology stack, architecture, code-quality mandate).
>
> Where the prose and the screenshots disagree, **this document records both and states the resolution.** Anything marked **[ASSUMPTION]** was not specified anywhere and is an engineering decision that the business should confirm.

---

## 1. Product summary

A multi-tenant, multi-firm, multi-branch ERP platform covering accounting, inventory, sales, purchase, manufacturing, and mobile-device service management, with configurable menus, dashboards, reports, print layouts, and workflows. Configuration changes must never require a source-code change or redeploy.

Deployment targets both **cloud and on-premises**.

---

## 2. Deployment context established from screenshots

These materially shape the design and are **not** stated in the prose.

| Observation | Evidence | Consequence for design |
|---|---|---|
| Currency is **Qatari Riyal (QR)**; reports include **VAT Input / Output / Summary** | `image2`, `image4`, `image6`, `image3` | GCC/Qatar deployment |
| Yet ledgers include **Output CGST / SGST / IGST**, **CST**, **HEDCess**, **Food Cess** | `image9`, sales-grid column list | Indian GST deployment |
| **Both** must be supported | — | **Resolution: the tax engine is jurisdiction-pluggable.** A `TaxRegime` per firm selects a rule set (`GccVat`, `IndiaGst`, `None`). Tax components are data, not code. See §9. |
| Legacy financial year runs **01.10.2021 → 31.12.2026** | `image13` status bar | Financial years are **arbitrary date ranges**, not calendar or Apr–Mar years. Do not hardcode. |
| Live tenant example: “Marasim flowers and events”, store location “INSTORE” | `image4`, `image6` | Branch is surfaced to users as **“Stock Location” / “Store Location”** |
| Subscription state is shown in-app (“Your Subscription has expired”) | `image4` | Tenant lifecycle/billing state is a first-class tenant attribute |
| **RMA** (Return Merchandise Authorization) is a full screen | `image14` | Service module scope is larger than the prose describes — see §14.3 |
| **Payroll** exists in the legacy app (ribbon tabs *Pay Roll*, *Pay Roll Reports*) | `image1` | Prose lists HR/Payroll as *future*. **Resolution: treat as future (Phase 4); reserve the module slot.** |
| **PDC** (post-dated cheques): PDC Report, PDC Calendar, Cheque Register | `image3` | Cheque lifecycle is in Phase 1 accounting scope |

---

## 3. Tenancy model

```
Tenant (subscription boundary)
  └── Firm            — independent books: own COA, financial data, numbering, users
        └── Branch    — “Stock Location” / “Store Location”
              └── FinancialYear
```

Each **Firm** maintains separate financial data, inventory data, users/permissions, and configuration.
Each **Branch** may override: print formats, financial settings, inventory settings, dashboard layouts, user permissions, themes, and numbering series.

**Isolation strategy (decided):** shared schema, `TenantId` discriminator on every tenant-scoped entity, enforced by
1. EF Core global query filters (application layer), and
2. PostgreSQL **Row-Level Security** policies keyed off a session GUC (database layer, defence-in-depth).

A user may be granted access to multiple firms and multiple branches, and switches between them in-session.

---

## 4. Security

**Authentication** — Keycloak (OIDC/OAuth2) issuing JWTs; refresh tokens; SSO.
**Second factor** — email OTP, SMS OTP, TOTP authenticator app. Per-firm policy decides whether 2FA is required.
**Tracking** — login history, device tracking, active session management, password policy.

**Authorization** — fully database-driven RBAC. No permission string is hardcoded in a controller attribute in a way that requires redeploy to change.

Seeded roles: Super Administrator, Firm Administrator, Branch Manager, Accountant, Sales Executive, Store Keeper.
Permission verbs: **Create, Edit, Delete, Approve, Print, Export, View**.

A permission grant is `(Role × Module × Resource × Verb)` scoped optionally to Firm and Branch.

---

## 5. Dynamic menu system

Menus are rows in the database, resolved per **(User, Role, Firm, Branch)**.

Administrators can show/hide menus and submenus, reorder them, create custom groups, and **move, copy, or share a menu entry across modules** — e.g. a report living under Inventory can be surfaced under Accounts — with no source-code change.

Menu structure observed in the web reference (`image3`, `image10`, `image11`):

```
Dashboard
Masters ──── General Masters
        ├─── Inventory Masters ─── Items · Categories · Manufacturer · Units ·
        │                          Pricelists · Selling Price Updation
        └─── Accounts Masters
Transactions
Accounting
Inventory Reports
Accounts Reports
Tools
Settings
Support
```

---

## 6. Masters inventory (from legacy ribbon, `image1`)

| Group | Masters |
|---|---|
| Product | Category, Sub Class, Brand, Origin, UOM, Product, Grade |
| Godown | Godown, Type, Branch |
| Clients & Employees | Customer, Temp. Customer, Supplier, Employee, Agent, Salesman, Service Centre, Delivery Man, Language |
| Accounts | Schedule, Ledger, Additional Ledgers, Bank |
| Costing | Centre, Class |
| Area | Route, Delivery Territory, SS Pending Product |
| Other | Order Mode, Mode, Currency, Warranty, Label, Manufacturer, Rack, Bin |

---

## 7. Accounting module (Phase 1)

### 7.1 Masters
Chart of Accounts · Account Groups · Financial Schedules · Ledgers · Sub-ledgers (Customers, Suppliers, Employees, Other Parties).

### 7.2 Transactions
Cash Receipt · Bank Receipt · Cash Payment · Bank Payment · Journal · Contra · Opening Balance · Cheque management (incl. PDC).

**Voucher entry shape** (`image2` — the UI reference):
- Voucher No, Date, Currency selector, **Exchange Rate (editable)**
- Line grid: `Debit/Credit` · `Ledger Name` · `Add Narration` (per line) · `Debit Amount` · `Credit Amount`, with Add Row / delete
- Running **TOTAL** per column with the invariant *“Every debit has a corresponding credit”*
- Ref/Inv No · Narration · Payment Mode · **file attachments (multiple, 2 MB total)**
- Actions: Back · Save & Print · Save; Grand Total displayed in tenant currency

**Bill-wise settlement** — when the *Bill-wise Payment & Receipt* system setting is enabled, Payment and Receipt screens list all outstanding bills and allow allocating against individual bills, closing each **fully or partially**. Applies to both Sales and Purchase.

### 7.3 Reports
From the prose: Ledger, Day Book, Cash Book, Bank Book, Trial Balance, P&L, Balance Sheet, Customer/Supplier Outstanding, Aging Analysis, Cash Flow.

Complete list observed in the web reference (`image3`) — **this is the delivery target**:

Debtors Report · Debtors Report (Age Wise) · Creditors Report · Creditors Report (Age Wise) · Ledgers (GL) · Voucher Report · Statement of Accounts · Account Group Report · Account Group Consolidated Report · **VAT Reports** (Input Tax, Output Tax, VAT Summary) · Final Accounts · Bill Wise Pending – Purchase · Bill Wise Pending – Sales · Bill Wise Pending – Service Invoices · PDC Report · Day Book · Cash Book · Bank Book · Transaction Summary · Salesman/Executive Wise Payment Collection · Over Due Sales Invoice · Over Due Service Invoice · Over Due Purchase Invoice · PDC Calendar · Forex Transactions · Chart of Accounts · Purchase (Fixed Assets) · Cheque Register · Custom

---

## 8. Inventory module

### 8.1 Product master — three tabs (`image7`, `image8`)

**Tab 1 — Description**
`Code` (auto-generate next numeric if blank, e.g. PRO-1004 → PRO-1005; duplicates rejected) · Description · Short Description · Category · Sub Class · Label · Manufacture · ItemName · EANCode · Size · Origin · UOM · Brand
Device attributes (mobile-service oriented): **Device · Colour · Battery · RAM · Storage**
Rate block: **Costing Method** (Last Purchase Rate | Average Rate) · Cost · Profit Percentage · COR% · Retail Sales Rate · WholeSale Rate · Other Sales Rate · MRP
`Is Packing` flag · Serial No/Warranty grid
**Opening Stock grid:** Quantity · UOM · Date · Rate · Brand · Margin · Godown · SalesRate

**Tab 2 — Details**
Purchase UOM · Sales UOM (with automatic conversion) · Item Type · Supplier · Rack · Bin · `Discontinued`
Order Level: Min · Reorder · Max · Moving Level: Slow · Fast
Movement classification: Fast / Slow / Normal / Dead Stock
**Multiple Rate Barcode grid:** Cost · Retail · WholeSale · MRP · UPC · Other · Brand
**Multiple Unit Price grid:** Cost · Retail · WholeSale · MRP · Other · Quantity · UOM

**Tab 3 — Images** — one or more images, surfaced in Sales, Purchase, and Inventory transactions.

### 8.2 Master screens
Product · Category · Sub-Class · Brand · Unit · Warehouse (Godown) · Rack · Bin · Manufacturer · Label.

### 8.3 Stock operations & reports
Stock Transfer · Stock Adjustment · Physical Stock Verification · Material Issue · Material Receipt · Damaged Stock · Delivery Note · Receipt Note.
Reports: Stock Ledger · Stock Valuation · Item Movement · Godown-Wise Stock · Batch-Wise Stock · Expiry Report.

---

## 9. Tax engine (jurisdiction-pluggable)

Driven by a per-firm `TaxRegime`. Tax components are data.

**Modes** (`Mode` field on sales/service documents): `NT` (Non-Tax) · `Tax` · `GST`.

> **Defaulted from the firm's regime (answered 2026-08-10):** a GST firm's documents open in
> `GST`, a VAT firm's in `Tax`. Nobody is offered a mode that does not apply where they
> trade, and a non-tax sale is the exception somebody selects deliberately. A default from
> the *customer* was offered and declined, so no mode field goes on the customer ledger.
Under GST, if the customer's state differs from the company's state, **IGST applies automatically**; otherwise CGST + SGST.

**Reverse (inclusive) tax** — separate settings for Sales and Purchase:
- disabled → entered rate is **tax-exclusive**: rate 100 @ 18% → taxable 100, tax 18, total 118
- enabled → entered rate is **tax-inclusive**: rate 118 @ 18% → taxable 100, tax 18, total 118

Tax percentage defaults from the Product Master.

**Additional Ledgers** (`image9`) — charges such as Delivery, Packing, Loading, Insurance, Freight, Service Charge, Discount Allowed, Round Off, and the tax ledgers themselves. Mapping is a matrix of **transaction type × ledger** with flags:

| Flag | Meaning |
|---|---|
| `Tax` | applies when document mode = Tax |
| `CST` | applies under CST |
| `NonTax` | applies when document mode = NT |
| `Addition` | adds to the total (vs. deducts) |
| `Default` | auto-loads onto a new document |

Transaction types carrying additional ledgers: DeliveryNoteTransaction, Manufacture, Production, Purchase, PurchaseOrder, PurchaseReturn, Sales, SalesOrder, SalesQuotation, SalesReturn, Service, ServiceSales.

A defaulted ledger auto-appears on the document; users may modify or remove it **subject to permission**.

---

## 10. Batch management

Enabled by system setting. When on, a Batch column appears in Purchase, Sales, Stock Adjustment and all applicable inventory transactions, and **purchase rate, expiry date, and stock quantity are all maintained per batch**.

- **Auto batch generation** — if no batch number is entered on purchase/stock-increment, generate the next one; numbering is **per product** (A001, A002, …).
- **Selection on sale** — single batch in stock → auto-selected; multiple → user must choose, with available quantity and purchase rate displayed per batch.
- **Costing & profit** — valuation is per batch; profit always uses **actual batch cost**. (Batch A001 @ 5.00 and A002 @ 6.00 sold at 7.00 yield 2.00 and 1.00 respectively.)
- **Expiry** — per batch, auto-populated on batch selection.
- **Returns/adjustments by batch** — purchase return, sales return, stock adjustment, damaged stock, expired-stock removal. Stock identifiable by both Batch No and Expiry Date.

**Built 2026-08-08, with two departures from the prose, both deliberate:**

- **Per product, not per system setting.** The prose turns batch management on for the whole
  installation. It is built per product instead, on the `TracksBatches` flag the product
  master already carries: a firm selling both pharmaceuticals and hardware needs lots on the
  first and would not thank anybody for a batch column on the second. A firm that wants it
  everywhere sets the flag on every product; a firm that wants it nowhere sets it on none,
  which is the system-wide switch the prose asked for. Turning the flag on over stock that is
  already on hand is refused — that stock belongs to no batch, and the position would carry a
  quantity its batches could never account for.

- **A batch is one lot, wherever it is.** The number, the expiry date and the purchase rate
  belong to the goods, so they are held once per product; how much of it sits in each godown
  is held per warehouse, exactly as the product's own position is. The product's position is
  kept equal to the sum of its batches' positions — same quantity, same value, movement by
  movement — so the stock valuation and the batch-wise valuation are two views of one number
  rather than two numbers that drift.

Not yet built: **expired stock is not refused on the way out**. Expired goods leave through an
issue or a write-off like any other goods, so the position cannot be the thing that refuses
them; whether a *sale* may draw on an expired lot is a rule for the sales document, and is
recorded there when that document exists.

---

## 11. Document numbering

Per **transaction type**, per **branch**, per **financial year**:
`Prefix` · `Suffix` · `Starting Number` · `Number Length` · FY-wise toggle · branch-wise toggle.

Examples: `SL001` · `001-SL` · `SL001A` · `SL/2026/0001`.

---

## 12. Sales module

### 12.1 Header
Invoice No · Ref No (auto-increment) · Customer · Salesman (from Employee master) · Billingman · Date & Time · **Mode** (NT / Tax / GST) · Cash/Credit.

**Payment:** Cash · Credit · Card (with Card Number, Bank Name) · UPI · **Partial** (any combination). The web reference exposes this as a `Multiple Mode ON` toggle (`image6`).

**Customer:** mobile-number lookup auto-filling Name, Address 1, Address 2, Phone · Privilege Card No · **Loyalty Points** (earned per configurable rules, redeemable at sale, running balance maintained).

> **Built 2026-08-12: the customer master.** At `/api/v1/sales/customers`, with the
> mobile-number lookup this section calls for. Decisions now binding:
>
> - **A customer is a sub-ledger, not a record beside one.** §7.1 already calls them
>   sub-ledgers and the rest of the system assumes it: an invoice is billed to a ledger, a
>   receipt settles against one, the debtors report sums them. A parallel customer record
>   would be a second thing to keep in step and a second answer to what a customer owes.
> - **The lookup is one search across code, name and mobile**, not a parameter each,
>   because it is somebody typing whatever they have into one box while a customer waits.
> - **Withdrawn, never deleted.** Every past invoice and the debtors report point at a
>   customer; withdrawing stops new documents naming them and leaves the trail whole.
> - **The code cannot be changed.** It is what a firm's own records and any imported
>   history refer to a customer by, and renaming it would leave both pointing at nothing.
> - **An update changes only the blocks it names.** A screen carrying just an address must
>   not silently drop the credit terms somebody agreed.
> - **A customer with no group named lands under the seeded Sundry Debtors code.** That is a
>   convenience at creation time, and the one place a code is read this way: getting it
>   wrong misfiles a customer visibly and is fixed by editing them, where a *posting* made
>   to an account chosen by code is found at a reconciliation months later — which is why
>   the tax and stock accounts sit on a per-firm map instead. A firm that has reshaped its
>   chart names the group.
>
> **Privilege card and loyalty points are not built** — loyalty was deferred by the business
> on 2026-08-10 (open question 2), and the privilege card is the same feature's front end.

**Other:** Narration (rich text in the web reference) · file attachments (2 MB) · **barcode scanning** — scanning adds the product; re-scanning the same product increments quantity.

### 12.2 Document conversion

> **Scoped 2026-08-10: order → invoice → return.** The business raises a sales order,
> converts it to an invoice, and raises a credit note when goods come back. **Quotation and
> delivery note are deferred** — the conversion machinery is built to be a chain, so adding
> either later is a new link rather than a redesign, but neither is built now.
Load from an existing document: Sales Return Ref No · Quotation Ref No · Order Ref No · Purchase Ref No · Delivery SR No. (`image6`: “Create Invoice From”.)

> **Built 2026-08-12: what posting an invoice produces.** Four things, in one transaction —
> the invoice posts, an **issue** takes the goods off the shelf, a **bill** puts the debt
> into the customer's outstanding, and a **journal** states the sale in the nominal ledger.
> Any subset of those four is a discrepancy somebody would have to find by hand, so all of
> them land or none does. Decisions now binding:
>
> - The goods leave through an ordinary stock issue rather than by reaching into positions
>   directly, so average costing, batch positions, serial transitions and the refusal to go
>   below zero all apply to a sale unchanged. The issue carries **no rate**: stock leaves at
>   the firm's own average cost, and passing the selling price would turn every sale into a
>   stock gain equal to its own margin.
> - The issue draws from a **numbering series of its own** (`inventory.sales-issue`, prefixed
>   `SI`), so a stock ledger distinguishes goods that went to a customer from goods that went
>   to a department.
> - The bill points at the **journal voucher**, which is what every other bill in the system
>   points at and what the settlement machinery already understands. Credit terms come from
>   the **customer's ledger** unless the caller states them: a figure typed on one invoice is
>   an exception, and the ledger is where a firm records what it agreed.
> - **Posting is its own step**, separate from entering the invoice. A draft can be corrected;
>   a posted invoice has moved stock and raised a debt, and is cancelled rather than edited.

> **Built 2026-08-12: cancelling one.** The mirror of posting, in one transaction: the goods
> go back on the shelf, the debt leaves the customer's outstanding, and both journals leave
> the balances. Decisions now binding:
>
> - **Cancelling is for an invoice that should never have been raised.** Goods a customer
>   actually took away come back as a **sales return**, which is a document of its own. The
>   difference is whether anything really happened, and a stock ledger that cannot tell the
>   two apart is one nobody can reconcile against a shelf.
> - **An invoice the customer has paid against cannot be cancelled**, in part or in full. A
>   receipt has to stay where it was made; withdrawing the debt underneath it would leave a
>   payment allocated to nothing. That case is a **credit note**, and the refusal says so.
> - **The bill is withdrawn, not deleted** — a fourth status on it. It stops counting as a
>   receivable everywhere at once, because the debtors report, the aging analysis, the
>   customer's credit position and the dashboard all read the same reader.
> - **Both journals are cancelled rather than reversed by contras**, which is how every
>   other cancelled voucher here behaves: the number and the lines stay, the balances do
>   not. A contra would say the same thing twice and leave a day book with two entries for
>   one mistake.
> - **The goods go back at what they left at**, not at today's average, so a cancellation
>   cannot restate the value of everything else on the shelf.

> **Answered 2026-08-12: a return is the same document, running the other way.** Two
> decisions, both taken deliberately rather than inherited:
>
> - **One document type with a kind, not two aggregates.** An invoice and a credit note
>   have the same shape — lines, tax per component, charges, a rounded total — and differ
>   only in which way the goods and the money move. §12.9's chain reads as links of one
>   kind of document, and keeping them together keeps the tax recording, the line checks
>   and the rounding in one place. A return may name the invoice it is against; an invoice
>   may not. **Amounts stay positive on both** — the kind decides the direction, because a
>   negative quantity is a second spelling of the same fact that every report would then
>   have to normalise before it could sum.
> - **Returns post to their own contra-revenue account**, a ninth kind of posting on the
>   per-firm map, seeded as `SALES-RETURN` under Income. Net revenue is the same as
>   debiting sales directly; what differs is whether "what did we sell" and "what came
>   back" stay separately answerable from the chart, and §12.10's Sales Return Report is
>   somebody asking for exactly that.
>
> The journal is one piece of arithmetic with a sign, not two: a return credits the
> customer, debits the returns account and debits each tax head back out of the liability.
> A journal that balances in one direction balances in the other by construction.

> **Answered 2026-08-12, posting one.** Three more decisions, all put to the business:
>
> - **Returned goods come back at the cost they left at**, read from the original sale's
>   own stock movements, so the cost-of-goods-sold reversal matches to the fils however far
>   the average has moved since. Where the return names no invoice there is no such cost
>   and the **current average** is used — receiving at the average cannot move the average,
>   so the rest of the shelf is left alone.
> - **Naming the invoice is optional.** Goods turn up without their paperwork, and a
>   counter that could not record them would turn customers away. Naming it is what lets
>   the credit find the debt and the goods find their cost.
> - **The credit is allocated against the sale's bill**, exactly as a receipt would be, and
>   **capped at what is still owing** rather than refused for exceeding it — a customer who
>   has part-paid is the ordinary case. Whatever cannot be matched stays as an unallocated
>   credit on the account: the journal has credited the ledger either way, so the balance is
>   right immediately, and what is missing is only the link to a document.
> - **A return raises no bill of its own**, and records none. A bill is something a document
>   raised; which bill a return settled is one hop away through the invoice it names.
> - **Its own numbering series**, prefixed `SR`, for the document and for the stock receipt
>   alike. A credit note is not a gap in the invoice sequence.

> **Built 2026-08-12: entering one.** `POST /api/v1/sales/invoices` takes a draft, and the
> tax engine is asked its question here rather than on the aggregate — the invoice records
> what the engine answered, so a reprint years later shows the tax that was charged rather
> than the tax today's rates would produce. Decisions now binding:
>
> - **The mode defaults from the firm's regime**, per the answer of 2026-08-10: a firm with
>   a regime opens in `Tax`, a firm with none can only sell without it. A non-tax sale stays
>   the exception somebody selects deliberately.
> - **Place of supply decides IGST**, comparing the customer's state with the firm's. A
>   customer whose state nobody has recorded is treated as an **intra-state** supply — the
>   safer of the two readings, because it keeps the tax in the state the firm is registered
>   in, which is recoverable, rather than charging IGST that is not.
> - **The tax rate is supplied per line.** §12.4 says it defaults from the product master,
>   and the product master carries no tax rate — so until it does, the caller states it. See
>   the README note.
> - **§9's `Default` flag makes a charge appear on the screen, not on the document.** The
>   command adds only charges somebody entered an amount for, and the only seeded default —
>   Round Off — is computed by the invoice rather than entered at all.

### 12.3 Item grid — full column set
`Code` · `Product ID` · `Product` · `Quantity` · `Rate` · `Total` · `Product Code` · `Godown` · `Measurement` · `Free Quantity` · `Free Measurement` · `Expiry Date` · `Tax Percent` · `Gross` · `Discount Percentage` · `Discount Amount` · `Net` · `Tax` · `Remarks` · `Barcode` · `Batch` · `Detail Description` · `CGST Amount` · `CGST Rate` · `SGST Amount` · `SGST Rate` · `IGST Amount` · `IGST Rate` · `Food Cess` · `ERate` (exchange rate)

**Dynamic column configuration** — visibility *and* order are configurable per role from Settings. Worked examples from the spec: a Cashier sees Barcode/Product/Quantity/Rate/Total; a Manager sees Code/Product/Stock/Quantity/Tax/Net; Store Staff see Product/Quantity/Godown/Batch.

**Scoped 2026-08-10, three answers that bind the sales build:**

- **A line defaults to the retail rate, and may be typed over** subject to permission. Price
  levels per customer and customer-specific price lists were both offered and declined, so
  no price-level field goes on the customer ledger and no price-list master is built. The
  rate block on the product already carries Retail, Wholesale, Other and MRP; the invoice
  reads one of them and does not choose between them.
- **Only `Round Off` defaults onto a new invoice.** Delivery, freight, packing, insurance and
  discount-allowed remain in the additional-ledger matrix of §9 and are added by hand on the
  documents that carry them, rather than loading onto every invoice at zero.
- **A credit limit warns rather than blocks.** A customer ledger gains a limit and the
  invoice reports the exposure it would create, but it still posts. Salespeople keep working
  and management gets a report; refusing at the counter was offered and declined.

### 12.4 Product selection
Search by Product Code, Product Name, or Barcode. Dropdown columns are **configurable per Settings** (Code, Product Name, Stock, Purchase Rate, Sales Rate, Barcode).
Auto-filled from Product Master: Code, Product Name, Barcode, Default Measurement, Tax %, Retail Rate, MRP, Product Code.
**Rate source** is configurable: Retail | MRP | Wholesale.
Default quantity = 1, user-modifiable.

### 12.5 Godown & measurement
Default godown from Settings, auto-populated, user-overridable. Godown dropdown shows **name + current stock in that godown**.
Only measurements in the product's measurement group may be selected — e.g. base `No` with alternatives `Pack (12 No)` and `Box (24 No)`; `Litre`/`Kg` are rejected.

### 12.6 Amount summary
Previous Balance · Advance Amount · Current Sales Amount · Total Balance · Cash Amount · Paid Amount · Grand Total · Balance Return.

```
Total Balance  = Previous Balance + Current Sales Amount
Balance Return = Paid Amount - Grand Total        (70 grand total, 100 paid → 30)
```

### 12.7 Serial number & warranty
Purchase: enter serial numbers individually with a warranty per serial (from Warranty Master).
Sales: available serials listed, user selects; sold serials never reappear.
Sales return → serial becomes available again. Purchase return → serial removed from stock.
Serial status: **Available · Sold · Returned to Supplier · Returned from Customer**.

**Built 2026-08-10, against stock documents rather than against sales.** Sales and purchase
do not exist yet, so units are received, issued, transferred and written off through the
stock operations of §8.3; the transitions are the ones named above, and the sales document
will reuse them unchanged when it arrives — a sale is an issue with a customer on it.

- **A fifth state, `Recorded`.** The four above describe where a real unit is. A number
  written on a draft, or on a document that was posted and then cancelled, is in none of
  them: the unit is not in stock and never was. Leaving such a unit available would offer a
  draft's goods for sale; deleting the row on cancellation would lose the trail of a receipt
  that was posted and reversed.
- **One number per unit of quantity, and whole quantities only.** A line for three handsets
  names three IMEIs — no more, because the extra names a unit that did not move, and no
  fewer, because the shortfall goes untracked for ever.
- **Warranty is per unit and arrives with the goods.** The Warranty Master the prose names
  does not exist yet, so the term is entered on the line that receives the units. A unit with
  no term recorded is *not* under warranty: an unknown term is not a term, and treating a
  blank as cover would have a service desk giving away repairs.
- **Serials do not carry the valuation.** What goods are worth stays with the stock position
  and, where batched, the batch position. The cost held per unit is what that unit came in
  at, kept so a margin can be measured against the actual machine — a third valuation layer
  would be a third figure able to disagree with the other two.

### 12.8 Printing
Print types: Non-Tax Invoice · Tax Invoice · GST Invoice. Multiple layouts per type.
Toggles: Company Logo · Barcode · Tax Details · Customer Address · Product Code · Batch Details · Expiry Date · Discount · Salesman · Loyalty Points · Balance Amount.

### 12.9 Workflows (configurable)
```
Sales:    Quotation → Sales Order → Delivery Note → Sales Invoice → Sales Return
Purchase: Requisition → Purchase Order → Goods Receipt → Purchase Invoice → Purchase Return
```
> The document states: *“I want change the flow if need”* — therefore these sequences are **workflow definitions stored as data**, editable by an administrator without a code change.

### 12.10 Sales & inventory reports (`image11`)
Purchase Reports · **Sales Reports** (Sales Report, Sales Return Report, Sales Order Report, Quotation Report, Delivery Note Report, Sales Report Detailed, Monthly Sales Analysis, Monthly Sales Summary, Daily Sales Report, Employee Wise Sales Report, Category Wise Sales Report, Stock Location Wise Sales Summary) · Service Reports · Stock Reports · Bill Wise Margin – Sales Invoices · Bill Wise Margin – Service Invoices · Item Wise Margin · Item Wise Transactions · Z Report · Adjustment Report · Cancelled Transactions · **Serial No/IMEI No Transactions** · Item Wise Sales Order Summary · Item Wise Sales Margin Summary · Tax On Margin/Profit · Top Items Sold & Purchased · Top Selling Customers · Top Supplying Vendors · Delivery & Receipt Report · Service Hold Duration Report · Sales Discount Report

---

## 13. Manufacturing

Bill of Materials — e.g. *School Kit* = 2 Pens + 2 Pencils + 3 Erasers.
Production entry increases finished goods and decreases raw materials **through inventory transactions**.
The production screen is dynamic: modify BOM quantities, replace components, add extra materials, and retain production history.

---

## 14. Mobile service management

### 14.1 Customer lookup
By mobile number or customer code, displaying previous service history, outstanding balance, and previous repairs.

### 14.2 Job card (`image13`)
Invoice/Ref No · Salesman · Customer · Cash/Credit · Mode · Date · Time · Address 1/2
**Mobile 1 (searchable) · Mobile 2 · Mail ID**
Device: **Item · Brand · ModelNo · Colour · SerialNo/IMEI**
**Technician** · Service Type · Accessories
**E Date · E Rate · E Time** (estimate date, rate, time)
Free text: **Fault · Remarks · Notes · Problem** (from Problems master)
Parts/labour grid: Product · Godown · Code · Qty · Rate · UOM · Total
Ledger grid: Total · Service Charge · Discount → GTotal

**Status set (as shown, colour-coded):** `Processing` · `Ready` · `Not Ready` · `Informed` · `Delivered` · `Is Dead`
The prose gives a different set: Received · Under Inspection · Waiting for Parts · In Progress · Ready for Delivery · Delivered.
> **Resolution:** service statuses are **configurable per firm** via the workflow engine; the legacy set is seeded as the default because it matches the working system.

### 14.3 RMA — Return Merchandise Authorization (`image14`) *[not in prose]*
No · Ref No · Date · Time · Salesman · Supplier · Customer · Mobile · Address · **Expected Date** · Barcode
Checkpoint flags: **Receive · Supplier · Passed · Ready · Return** · `Stock` indicator
Grid: Code · Product · **batch** · Quantity · Rate · Total
Fault · Remarks · Supplier block (addresses, mobile, phone, narration, Mode) · SerialNo/Warranty/Return grid · Ledger grid

### 14.4 Financial integration
Service Estimate · Advance Receipt · Final Invoice · Outstanding tracking.

---

## 15. Cross-cutting platform features

**Data grid framework** — global search across visible columns; per-column search; sorting; grouping; column reorder/hide/freeze; **saved personal layouts**; role-based column visibility; export to Excel/PDF/CSV; virtualised scrolling.

**Dashboards** — dynamic builder with KPI cards, charts, graphs, tables, and custom SQL widgets; assignable per role. The document's worked example: *of ten dashboards, 1–4 to Accountant, 3–7 to Sales, all to Admin* — i.e. **many-to-many role↔dashboard assignment with overlap**, not a single dashboard per role.
Observed dashboard (`image4`): quick actions (POS, Sales, Service, Purchase, Payments, Receipts) · KPI cards (Sales, Purchase, Sales Return, Purchase Return, Total Payments, Total Receipts, Total Receivables, Total Payables — each with count, period comparison, and sparkline) · Daily Sales · Monthly Sales · Payment Mode Wise Sales · Top Customers · Top Items · Top Employees.

**Report builder** — drag-and-drop columns, filters, grouping, calculated fields, charts, drill-down, scheduling, PDF/Excel/CSV export, email delivery.

**Print designer** — drag-and-drop layout for Invoice, Receipt, Voucher, Job Card; logo placement, barcode/QR, dynamic fields, conditional sections, RTL printing, branch-specific templates. No backend change for a layout change.

**Localisation** — English and Arabic, RTL, dynamic switching, localised labels, Arabic numerals, regional date formats.

**Multi-currency** — enable/disable, currency master, exchange-rate management, automatic conversion, realised/unrealised gain-loss, currency-wise reports. Per-document exchange rate is editable (`image2`, `image6`).

**Notifications** — in-app, email, SMS, WhatsApp, plus SignalR real-time push. Triggers: invoice created, payment received, approval pending, stock shortage.

**Audit** — every login, create, update, delete, and approval recorded with User, Date/Time, Branch, Action, **Old Value, New Value**.

**Workflow engine** — sales, purchase, approval, and manufacturing flows configurable without code changes.

---

## 16. Non-functional requirements

| Area | Requirement |
|---|---|
| Performance | 1,000+ concurrent users; < 3 s response for normal transactions |
| Scalability | horizontal scaling; cloud-ready |
| Security | JWT, 2FA, role-based security, encryption at rest and in transit |
| Availability | daily backup, disaster recovery, HA deployment |
| Quality | 80%+ test coverage; TypeScript strict; C# nullable reference types |

---

## 17. Future modules (architecture must absorb without change)

POS (retail billing, barcode, cash counter) · HR & Payroll (employees, attendance, leave, payroll) · School Management (students, admissions, fees, examinations) · CRM (leads, opportunities, follow-ups) · Asset Management (register, depreciation, maintenance).

---

## 18. Open questions for the business

1. ~~**Tax regime per deployment**~~ — **ANSWERED (2026-08-05): both are required, concurrently.**
   A single platform instance must serve GCC VAT firms and Indian GST firms at the same time. Consequences, now binding:
   - `TaxRegime` is a **per-firm** setting, not a build-time or deployment-time constant.
   - Both component sets are seeded: VAT (input/output) **and** CGST / SGST / IGST / Cess.
   - Place-of-supply comparison (which drives IGST vs CGST+SGST) applies only under the GST regime and must be inert under VAT.
   - Every tax figure is stored per component on the document line, never as a single collapsed `TaxAmount`, so both the VAT return and the GST return can be produced from the same posting.
   - Report menus are regime-filtered: a VAT firm must not be shown GST returns, and vice versa.
2. ~~**Loyalty rules**~~ — **ANSWERED (2026-08-10): not in the first release.**
   Sales ships without loyalty, and the loyalty-points toggle of §12.8 is left off the print layouts. The feature is designed when somebody can state the earn rate, the redemption value and the expiry; building it on a guess would put a liability on the balance sheet that nobody had agreed to.
3. ~~**Aging buckets**~~ — **ANSWERED (2026-08-10): 0-30 / 31-60 / 61-90 / 90+ confirmed.**
   The assumption the debtors and creditors reports were built on is the business's actual answer, so it stops being an assumption. The buckets are fixed for every firm; making them configurable was offered and declined, and nothing should be built that reads them from a setting.
4. ~~**Rounding**~~ — **ANSWERED (2026-08-10): round the document total, not the components.**
   Consequences, now binding:
   - Tax is computed **per component at full precision** and stored per component on the line, unrounded beyond the engine's own scale. Both the VAT return and the GST return are produced from those figures.
   - Only the **document total** is rounded to the currency's own precision, once, at the end.
   - The difference goes to the **`Round Off` additional ledger** the spec already names in §9, so the rounding is visible in the books as a posting rather than hidden inside a total.
   - No cash rounding to 0.05. If a firm needs it later it is a per-firm setting on the same rounding step, not a change to how tax is computed.

   > **Implementation note, 2026-08-12.** The rounding difference is currently **nil on every
   > invoice that can be raised**, so `Round Off` is a posting nothing yet produces. The tax
   > engine returns the taxable amount and the tax already rounded to the currency's own
   > scale, and a line whose price implies finer precision than that is refused as
   > inconsistent with its own assessment. The difference is therefore structural rather
   > than a defect in the rounding step, and it will become reachable the moment either the
   > engine keeps sub-unit precision or the line check is relaxed. **Tax-inclusive entry is
   > blocked by the same check** — an inclusive assessment's taxable amount is by definition
   > not the quantity times the entered rate, so §9's reverse-tax setting cannot be switched
   > on for sales until the check distinguishes the two modes. Neither is invented here:
   > both are recorded so whoever builds the sales entry screen meets them as decisions
   > rather than as surprises.
5. ~~**`COR%`** on the product master~~ — **ANSWERED (2026-08-10): cost of retail — the margin on the retail rate.**
   A second margin figure kept beside `Profit Percentage`, which applies to the wholesale or default sales rate. Both are held on the product's rate block; neither drives a posting on its own.
6. ~~**Costing methods**~~ — **ANSWERED (2026-08-06): average costing. FIFO is not required.**
   Consequences, now binding:
   - Stock is valued at **weighted average cost**, recomputed on every receipt into a location.
   - `CostingMethod` remains a per-product field, since the prose names Last Purchase Rate beside it, but **Average Rate is the default** and the only method the valuation engine must support for the first release.
   - Batch-wise costing (§10) still holds where batches are enabled: a batch carries its own purchase rate, and profit on a batched item uses that actual rate rather than the running average. The two coexist — the average is the item's position across locations, the batch rate is what a specific unit cost.
   - No FIFO queue is modelled. Nothing should be built that depends on issue order, because reintroducing FIFO later would then be a data migration rather than a new strategy.
7. ~~**Approval matrix**~~ — **ANSWERED (2026-08-10): no approvals in the first release.**
   Consequences, now binding:
   - Every document posts directly, as they do today. The **workflow engine of §12.9 is deferred**, and the modules that would have waited on it are not blocked.
   - Nothing is to be built that *assumes* approval-free posting, though: a document's transition to posted stays a single guarded step on the aggregate, so an approval gate can be added in front of it later without reworking the documents themselves.
   - Permission-based control still applies. "No approval" means no second person signs a document off, not that anybody may post anything.
8. ~~Whether **Payroll** and **POS** are needed in the first release~~ — **ANSWERED (2026-08-10): both genuinely deferred.**
   They stay in §17 as future modules the architecture must absorb without change. Nothing is built for either now, and nothing may be built that assumes they never arrive: a till is a sales document with a different screen, and payroll posts journals like anything else.
   
   **8a. Which accounts a stock movement posts to.** Raised 2026-08-07, while building §8.3.
   Stock operations move goods and value, and the stock ledger they write is complete and
   self-consistent — but nothing bridges it to the nominal ledger, because the mapping does
   not exist anywhere in the source document. A material issue debits *something* (works in
   progress, a consumption account, a department); damaged stock debits a loss account; an
   opening stock document credits opening equity. Each is a business decision, and inventing
   defaults would put figures in the accounts that nobody asked for and that would be found
   at the first reconciliation.
   
   **ANSWERED (2026-08-10): a per-firm control-account map.** Consequences, now binding:
   - Each firm names an **inventory control account** — the asset the value of stock sits in —
     and one **counter-account per movement type**: consumption for a material issue, a loss
     account for damaged stock and for a shortfall found on a count, opening equity for an
     opening-stock document, and a variance account for an adjustment.
   - **Per firm, not per product category.** A category-level map was offered and declined, so
     the accounts are chosen once per firm rather than per category. Nothing in the design
     forecloses the finer grain later: the map is a lookup taking a movement, and a category
     can be added to what it looks up without changing a posting.
   - A stock document that posts now raises a **balanced journal alongside its movements, in
     the same transaction** — inventory debited on the way in, credited on the way out, and
     the counter-account taking the other side. A transfer posts **nothing**: the goods have
     not changed hands or value, only shelves.
   - A firm that has not set the map up **cannot post stock**, rather than posting it into
     nowhere. The refusal names the account that is missing. Seeding gives a new firm sensible
     defaults from the standard chart, so a fresh installation is not born broken.
   - **Extended 2026-08-10 again, for sales.** Revenue is credited to **one sales account per
     firm**, a seventh kind of posting on the same map - per category and per document type
     were both offered and declined, so revenue by line of business is a report over the
     invoice lines rather than a split in the chart. **Output tax is mapped head by head**:
     a component-to-ledger map per firm, seeded from the chart per regime, so a VAT firm
     credits Output VAT and a GST firm credits CGST, SGST or IGST as the engine assessed
     them. Reading ledgers by code convention was declined for the reason that decided it -
     a firm that renames an account would silently break its own tax postings.

   - **Extended 2026-08-12 to rounding.** An eighth kind of posting: the account the
     rounding difference of Q4 lands in. On the map for the same reason as the tax heads -
     a firm that renames its Round Off ledger would otherwise break its own postings
     silently. Section 9 still lists Round Off among the additional ledgers, and it stays
     there for the charge somebody adds by hand; this is where the difference the system
     computes goes, which is a different question. A sale's journal therefore debits the
     customer, credits revenue and each tax head, posts each charge to its own ledger, and
     puts whatever is left into Round Off - so it balances by construction rather than by
     arithmetic that happens to agree.

   - **Extended 2026-08-10 to bounced cheques.** The same map gains three more accounts —
     cheques in hand, bank charges, and dishonour suspense — so a dishonoured cheque raises
     its own reversing journal automatically instead of waiting for somebody to write one.
     The operator-supplied route stays: a bounce may still name a journal, and one already
     named is not overwritten. That closes the last accounting gap the README carried.
   - Inventory therefore appears in the trial balance and the balance sheet, and the two
     reconcile against the stock valuation by construction.
9. ~~**Custom SQL dashboard widgets**~~ — **ANSWERED (2026-08-06): required, all of them.**
   Arbitrary SQL reaching the database from a browser is the largest deliberate attack surface in the platform, so it is built with the guards named when the question was raised rather than without them:
   - **Read-only by construction.** Every custom query runs inside a `READ ONLY` transaction, so a statement that slipped past validation still cannot write.
   - **One statement, and it must read.** Only a single `SELECT` or `WITH` is accepted; batching, data-modifying CTEs, and anything else are refused before the database sees them.
   - **Bounded.** A per-statement timeout and a hard row cap, so a widget cannot exhaust a connection or return a million rows into a dashboard panel.
   - **Tenant isolation is not on trust.** Custom queries run as the ordinary application role, so PostgreSQL row-level security applies to them exactly as to everything else. A query naming another tenant's rows returns nothing — the guard is in the database, not in the parser.
   - **Defining one is privileged.** Authoring a custom widget requires `reporting:dashboard:create`; *reading* a dashboard does not let anybody write SQL.
