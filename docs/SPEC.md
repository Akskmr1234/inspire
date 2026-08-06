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

**Other:** Narration (rich text in the web reference) · file attachments (2 MB) · **barcode scanning** — scanning adds the product; re-scanning the same product increments quantity.

### 12.2 Document conversion
Load from an existing document: Sales Return Ref No · Quotation Ref No · Order Ref No · Purchase Ref No · Delivery SR No. (`image6`: “Create Invoice From”.)

### 12.3 Item grid — full column set
`Code` · `Product ID` · `Product` · `Quantity` · `Rate` · `Total` · `Product Code` · `Godown` · `Measurement` · `Free Quantity` · `Free Measurement` · `Expiry Date` · `Tax Percent` · `Gross` · `Discount Percentage` · `Discount Amount` · `Net` · `Tax` · `Remarks` · `Barcode` · `Batch` · `Detail Description` · `CGST Amount` · `CGST Rate` · `SGST Amount` · `SGST Rate` · `IGST Amount` · `IGST Rate` · `Food Cess` · `ERate` (exchange rate)

**Dynamic column configuration** — visibility *and* order are configurable per role from Settings. Worked examples from the spec: a Cashier sees Barcode/Product/Quantity/Rate/Total; a Manager sees Code/Product/Stock/Quantity/Tax/Net; Store Staff see Product/Quantity/Godown/Batch.

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
2. **Loyalty rules** — earn rate, redemption value, expiry. Only “configurable settings” is stated.
3. **Aging buckets** — 0-30/31-60/61-90/90+ assumed. **[ASSUMPTION]**
4. **Rounding** — currency precision and rounding rule per tax component (a `Round` additional ledger exists, implying document-level rounding).
5. **`COR%`** on the product master — meaning not defined in the document.
6. ~~**Costing methods**~~ — **ANSWERED (2026-08-06): average costing. FIFO is not required.**
   Consequences, now binding:
   - Stock is valued at **weighted average cost**, recomputed on every receipt into a location.
   - `CostingMethod` remains a per-product field, since the prose names Last Purchase Rate beside it, but **Average Rate is the default** and the only method the valuation engine must support for the first release.
   - Batch-wise costing (§10) still holds where batches are enabled: a batch carries its own purchase rate, and profit on a batched item uses that actual rate rather than the running average. The two coexist — the average is the item's position across locations, the batch rate is what a specific unit cost.
   - No FIFO queue is modelled. Nothing should be built that depends on issue order, because reintroducing FIFO later would then be a data migration rather than a new strategy.
7. **Approval matrix** — which documents require approval, and at what thresholds.
8. Whether **Payroll** and **POS** are needed in the first release or genuinely deferred.
9. ~~**Custom SQL dashboard widgets**~~ — **ANSWERED (2026-08-06): required, all of them.**
   Arbitrary SQL reaching the database from a browser is the largest deliberate attack surface in the platform, so it is built with the guards named when the question was raised rather than without them:
   - **Read-only by construction.** Every custom query runs inside a `READ ONLY` transaction, so a statement that slipped past validation still cannot write.
   - **One statement, and it must read.** Only a single `SELECT` or `WITH` is accepted; batching, data-modifying CTEs, and anything else are refused before the database sees them.
   - **Bounded.** A per-statement timeout and a hard row cap, so a widget cannot exhaust a connection or return a million rows into a dashboard panel.
   - **Tenant isolation is not on trust.** Custom queries run as the ordinary application role, so PostgreSQL row-level security applies to them exactly as to everything else. A query naming another tenant's rows returns nothing — the guard is in the database, not in the parser.
   - **Defining one is privileged.** Authoring a custom widget requires `reporting:dashboard:create`; *reading* a dashboard does not let anybody write SQL.
