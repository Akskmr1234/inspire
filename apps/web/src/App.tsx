import { Suspense, useEffect } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { AppShell } from '@/components/AppShell';
import { HomeRedirect } from '@/components/HomeRedirect';
import { lazyScreen } from '@/lib/screens';
import { ReportSkeleton } from '@/components/ReportFrame';
import { ChangePasswordPage } from '@/pages/ChangePasswordPage';
import { LoginPage } from '@/pages/LoginPage';
import { applyPresentation, useSession } from '@/stores/session';

/*
  Every screen but the sign-in page is fetched on demand.

  Loaded eagerly the application is a single ~570 kB script, so opening the trial
  balance downloads the product editor, the stock ledger, the tax return and thirty
  others first. That is paid for on the slowest connection by the person least able
  to afford it — the branch user on a phone — and paid again on every deployment,
  because one changed line invalidates the whole bundle.

  `AppShell` and `LoginPage` stay eager: they are what the first paint needs in
  either state, and deferring them would only add a spinner before the spinner.
*/

const TrialBalancePage = lazyScreen(
  () => import('@/pages/TrialBalancePage'),
  'TrialBalancePage',
);
const VoucherEntryPage = lazyScreen(
  () => import('@/pages/VoucherEntryPage'),
  'VoucherEntryPage',
);
const PaymentEntryPage = lazyScreen(
  () => import('@/pages/VoucherEntryPage'),
  'PaymentEntryPage',
);
const ReceiptEntryPage = lazyScreen(
  () => import('@/pages/VoucherEntryPage'),
  'ReceiptEntryPage',
);
const ProfitAndLossPage = lazyScreen(
  () => import('@/pages/ProfitAndLossPage'),
  'ProfitAndLossPage',
);
const BalanceSheetPage = lazyScreen(
  () => import('@/pages/BalanceSheetPage'),
  'BalanceSheetPage',
);
const DayBookPage = lazyScreen(() => import('@/pages/DayBookPage'), 'DayBookPage');
const VoucherReportPage = lazyScreen(
  () => import('@/pages/VoucherReportPage'),
  'VoucherReportPage',
);
const TransactionSummaryPage = lazyScreen(
  () => import('@/pages/TransactionSummaryPage'),
  'TransactionSummaryPage',
);
const CashFlowPage = lazyScreen(() => import('@/pages/CashFlowPage'), 'CashFlowPage');
const LedgerStatementPage = lazyScreen(
  () => import('@/pages/LedgerStatementPage'),
  'LedgerStatementPage',
);
const OutstandingPage = lazyScreen(
  () => import('@/pages/OutstandingPage'),
  'OutstandingPage',
);
const MenuAdministrationPage = lazyScreen(
  () => import('@/pages/MenuAdministrationPage'),
  'MenuAdministrationPage',
);
const UsersPage = lazyScreen(() => import('@/pages/UsersPage'), 'UsersPage');
const RolesPage = lazyScreen(() => import('@/pages/RolesPage'), 'RolesPage');
const DataResetPage = lazyScreen(() => import('@/pages/DataResetPage'), 'DataResetPage');
const ImportPage = lazyScreen(() => import('@/pages/ImportPage'), 'ImportPage');
const PrintSettingsPage = lazyScreen(
  () => import('@/pages/PrintSettingsPage'),
  'PrintSettingsPage',
);
const PrintTemplatesPage = lazyScreen(
  () => import('@/pages/PrintTemplatesPage'),
  'PrintTemplatesPage',
);
const LedgersPage = lazyScreen(() => import('@/pages/LedgersPage'), 'LedgersPage');
const EmployeesPage = lazyScreen(() => import('@/pages/EmployeesPage'), 'EmployeesPage');
const CountriesPage = lazyScreen(() => import('@/pages/CountriesPage'), 'CountriesPage');
const TerritoriesPage = lazyScreen(
  () => import('@/pages/TerritoriesPage'),
  'TerritoriesPage',
);
const TaxRatesPage = lazyScreen(() => import('@/pages/TaxRatesPage'), 'TaxRatesPage');
const DashboardPage = lazyScreen(() => import('@/pages/DashboardPage'), 'DashboardPage');
const UnitsPage = lazyScreen(() => import('@/pages/UnitsPage'), 'UnitsPage');
const CategoriesPage = lazyScreen(
  () => import('@/pages/CategoriesPage'),
  'CategoriesPage',
);
const BrandsPage = lazyScreen(() => import('@/pages/CategoriesPage'), 'BrandsPage');
const WarehousesPage = lazyScreen(
  () => import('@/pages/WarehousesPage'),
  'WarehousesPage',
);
const CustomersPage = lazyScreen(() => import('@/pages/CustomersPage'), 'CustomersPage');
const ProductsPage = lazyScreen(() => import('@/pages/ProductsPage'), 'ProductsPage');
const SalesPage = lazyScreen(() => import('@/pages/SalesPage'), 'SalesPage');
const SalesReturnsPage = lazyScreen(
  () => import('@/pages/SalesPage'),
  'SalesReturnsPage',
);
const PurchasePage = lazyScreen(() => import('@/pages/PurchasePage'), 'PurchasePage');
const PurchaseReturnsPage = lazyScreen(
  () => import('@/pages/PurchasePage'),
  'PurchaseReturnsPage',
);
const PurchaseOrdersPage = lazyScreen(
  () => import('@/pages/OrdersPage'),
  'PurchaseOrdersPage',
);
const SalesOrdersPage = lazyScreen(() => import('@/pages/OrdersPage'), 'SalesOrdersPage');
const SalesQuotationsPage = lazyScreen(
  () => import('@/pages/OrdersPage'),
  'SalesQuotationsPage',
);
const SettingsPage = lazyScreen(() => import('@/pages/SettingsPage'), 'SettingsPage');
const OpeningStockPage = lazyScreen(
  () => import('@/pages/StockOperationsPage'),
  'OpeningStockPage',
);
const SuppliersPage = lazyScreen(() => import('@/pages/SuppliersPage'), 'SuppliersPage');
const TaxReturnsPage = lazyScreen(
  () => import('@/pages/TaxReturnsPage'),
  'TaxReturnsPage',
);
const BomsPage = lazyScreen(() => import('@/pages/ProductionPage'), 'BomsPage');
const JobCardsPage = lazyScreen(() => import('@/pages/ServicePage'), 'JobCardsPage');
const RmasPage = lazyScreen(() => import('@/pages/RmaPage'), 'RmasPage');
const AuditLogPage = lazyScreen(() => import('@/pages/AuditLogPage'), 'AuditLogPage');
const ApprovalsPage = lazyScreen(() => import('@/pages/ApprovalsPage'), 'ApprovalsPage');
const ProductionPage = lazyScreen(
  () => import('@/pages/ProductionPage'),
  'ProductionPage',
);
const DeliveryNotesPage = lazyScreen(
  () => import('@/pages/StockOperationsPage'),
  'DeliveryNotesPage',
);
const ReceiptNotesPage = lazyScreen(
  () => import('@/pages/StockOperationsPage'),
  'ReceiptNotesPage',
);
const StockAdjustmentPage = lazyScreen(
  () => import('@/pages/StockAdjustmentPage'),
  'StockAdjustmentPage',
);
const DamagedStockPage = lazyScreen(
  () => import('@/pages/StockAdjustmentPage'),
  'DamagedStockPage',
);
const StockTransfersPage = lazyScreen(
  () => import('@/pages/StockTransfersPage'),
  'StockTransfersPage',
);
const StockOperationsPage = lazyScreen(
  () => import('@/pages/StockOperationsPage'),
  'StockOperationsPage',
);
const AccountGroupSummaryPage = lazyScreen(
  () => import('@/pages/AccountGroupSummaryPage'),
  'AccountGroupSummaryPage',
);
const PostDatedChequesPage = lazyScreen(
  () => import('@/pages/PostDatedChequesPage'),
  'PostDatedChequesPage',
);
const ChequeCalendarPage = lazyScreen(
  () => import('@/pages/ChequeCalendarPage'),
  'ChequeCalendarPage',
);
const ChequeRegisterPage = lazyScreen(
  () => import('@/pages/ChequeRegisterPage'),
  'ChequeRegisterPage',
);

const BatchStockPage = lazyScreen(
  () => import('@/pages/StockReportPages'),
  'BatchStockPage',
);
const BatchProfitPage = lazyScreen(
  () => import('@/pages/StockReportPages'),
  'BatchProfitPage',
);
const ExpiryReportPage = lazyScreen(
  () => import('@/pages/StockReportPages'),
  'ExpiryReportPage',
);
const SerialStockPage = lazyScreen(
  () => import('@/pages/StockReportPages'),
  'SerialStockPage',
);
const ItemMovementPage = lazyScreen(
  () => import('@/pages/StockReportPages'),
  'ItemMovementPage',
);
const StockLedgerPage = lazyScreen(
  () => import('@/pages/StockReportPages'),
  'StockLedgerPage',
);
const StockValuationPage = lazyScreen(
  () => import('@/pages/StockReportPages'),
  'StockValuationPage',
);
const ProfilePage = lazyScreen(() => import('@/pages/ProfilePage'), 'ProfilePage');

/* The cash and bank books are one component told which of the two it is. */
const CashBankBookPage = lazyScreen(
  () => import('@/pages/CashBankBookPage'),
  'CashBankBookPage',
);

/**
 * What a screen shows while its code is on the way.
 *
 * The report skeleton rather than a spinner: nearly every route under here is a
 * table, so the shape is right, and on a fast connection it flashes for a frame
 * instead of announcing itself.
 */
function RouteFallback(): React.JSX.Element {
  return (
    <div className="page">
      <div className="skeleton h-7 w-56 rounded" />
      <ReportSkeleton rows={5} />
    </div>
  );
}

/** Routing and the signed-in gate. */
export function App(): React.JSX.Element {
  const { status, mustChangePassword, theme, language, restore } = useSession();

  useEffect(() => {
    applyPresentation(theme, language);
  }, [theme, language]);

  useEffect(() => {
    // A stored refresh token is exchanged before anything renders, so a reload
    // does not bounce a signed-in user back to the sign-in screen.
    if (status === 'unknown') {
      void restore();
    }
  }, [status, restore]);

  if (status === 'unknown') {
    return (
      <div className="grid min-h-screen place-items-center bg-canvas">
        <div className="flex items-center gap-3 text-sm text-ink-muted">
          <span className="size-4 animate-spin rounded-full border-2 border-line border-t-brand-600" />
          Loading…
        </div>
      </div>
    );
  }

  if (status === 'signedOut') {
    return (
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    );
  }

  return (
    <Suspense fallback={<RouteFallback />}>
      <Routes>
        <Route element={<AppShell />}>
          <Route path="/change-password" element={<ChangePasswordPage />} />
          <Route path="/profile" element={<ProfilePage />} />
          {mustChangePassword && (
            <Route path="*" element={<Navigate to="/change-password" replace />} />
          )}
          {!mustChangePassword && (
            <>
              <Route path="/accounting/vouchers/new" element={<VoucherEntryPage />} />
              {/*
                Payments and receipts have routes of their own, and menu entries to
                match. They were the journal screen with a voucher type chosen from
                a dropdown — which is how the two commonest documents in the system
                ended up being entered as double entries by whoever was at the
                counter.
              */}
              <Route path="/accounting/payments/new" element={<PaymentEntryPage />} />
              <Route path="/accounting/receipts/new" element={<ReceiptEntryPage />} />
              <Route path="/accounting/trial-balance" element={<TrialBalancePage />} />
              <Route
                path="/accounting/account-group-summary"
                element={<AccountGroupSummaryPage />}
              />
              <Route path="/accounting/profit-and-loss" element={<ProfitAndLossPage />} />
              <Route path="/accounting/balance-sheet" element={<BalanceSheetPage />} />
              <Route path="/accounting/day-book" element={<DayBookPage />} />
              <Route path="/accounting/voucher-report" element={<VoucherReportPage />} />
              <Route
                path="/accounting/transaction-summary"
                element={<TransactionSummaryPage />}
              />
              <Route
                path="/accounting/cash-book"
                element={<CashBankBookPage book="cash-book" />}
              />
              <Route
                path="/accounting/bank-book"
                element={<CashBankBookPage book="bank-book" />}
              />
              <Route path="/accounting/cash-flow" element={<CashFlowPage />} />
              <Route
                path="/accounting/ledger-statement"
                element={<LedgerStatementPage />}
              />
              <Route
                path="/accounting/debtors"
                element={<OutstandingPage key="debtors" side="debtors" />}
              />
              <Route
                path="/accounting/creditors"
                element={<OutstandingPage key="creditors" side="creditors" />}
              />
              <Route path="/accounting/tax-returns" element={<TaxReturnsPage />} />
              <Route path="/settings/menu" element={<MenuAdministrationPage />} />
              <Route path="/settings/users" element={<UsersPage />} />
              <Route path="/settings/roles" element={<RolesPage />} />
              <Route path="/settings/data" element={<DataResetPage />} />
              <Route path="/settings/import" element={<ImportPage />} />
              <Route path="/settings/printing" element={<PrintSettingsPage />} />
              <Route path="/settings/print-templates" element={<PrintTemplatesPage />} />
              <Route path="/settings" element={<SettingsPage />} />
              <Route path="/accounting/ledgers" element={<LedgersPage />} />
              <Route path="/accounting/employees" element={<EmployeesPage />} />
              <Route path="/sales/countries" element={<CountriesPage />} />
              <Route path="/sales/territories" element={<TerritoriesPage />} />
              <Route path="/dashboard" element={<DashboardPage />} />
              <Route path="/sales/invoices" element={<SalesPage />} />
              <Route path="/sales/returns" element={<SalesReturnsPage />} />
              <Route path="/sales/customers" element={<CustomersPage />} />
              <Route path="/purchase/invoices" element={<PurchasePage />} />
              {/*
                Purchase returns get their own route rather than a Kind dropdown on
                the purchases screen, and the two order screens get the routes the
                seeded menu has been pointing at since the orders module was
                written — before these lines existed, both fell through the
                catch-all below and landed on the trial balance.
              */}
              <Route path="/purchase/returns" element={<PurchaseReturnsPage />} />
              <Route path="/purchase/orders" element={<PurchaseOrdersPage />} />
              <Route path="/sales/orders" element={<SalesOrdersPage />} />
              <Route path="/sales/quotations" element={<SalesQuotationsPage />} />
              <Route path="/purchase/suppliers" element={<SuppliersPage />} />
              <Route path="/inventory/products" element={<ProductsPage />} />
              <Route path="/inventory/tax-rates" element={<TaxRatesPage />} />
              <Route path="/inventory/stock" element={<StockOperationsPage />} />
              <Route path="/inventory/stock-transfers" element={<StockTransfersPage />} />
              <Route
                path="/inventory/stock-adjustment"
                element={<StockAdjustmentPage />}
              />
              <Route path="/inventory/damaged-stock" element={<DamagedStockPage />} />
              <Route path="/sales/delivery-notes" element={<DeliveryNotesPage />} />
              <Route path="/inventory/boms" element={<BomsPage />} />
              <Route path="/inventory/production" element={<ProductionPage />} />
              <Route path="/service/job-cards" element={<JobCardsPage />} />
              <Route path="/service/rmas" element={<RmasPage />} />
              <Route path="/settings/audit-log" element={<AuditLogPage />} />
              <Route path="/settings/approvals" element={<ApprovalsPage />} />
              <Route path="/purchase/receipt-notes" element={<ReceiptNotesPage />} />
              <Route path="/inventory/opening-stock" element={<OpeningStockPage />} />
              <Route path="/inventory/valuation" element={<StockValuationPage />} />
              <Route path="/inventory/stock-ledger" element={<StockLedgerPage />} />
              <Route path="/inventory/item-movement" element={<ItemMovementPage />} />
              <Route path="/inventory/batch-stock" element={<BatchStockPage />} />
              <Route path="/inventory/batch-profit" element={<BatchProfitPage />} />
              <Route path="/inventory/expiry" element={<ExpiryReportPage />} />
              <Route path="/inventory/serials" element={<SerialStockPage />} />
              <Route path="/inventory/units" element={<UnitsPage />} />
              <Route path="/inventory/categories" element={<CategoriesPage />} />
              <Route path="/inventory/brands" element={<BrandsPage />} />
              <Route path="/inventory/warehouses" element={<WarehousesPage />} />
              <Route
                path="/accounting/post-dated-cheques"
                element={<PostDatedChequesPage />}
              />
              <Route
                path="/accounting/cheque-calendar"
                element={<ChequeCalendarPage />}
              />
              <Route
                path="/accounting/cheque-register"
                element={<ChequeRegisterPage />}
              />
              <Route path="*" element={<HomeRedirect />} />
            </>
          )}
        </Route>
      </Routes>
    </Suspense>
  );
}
