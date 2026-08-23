import { Suspense, lazy, useEffect, type ComponentType } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { AppShell } from '@/components/AppShell';
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

/**
 * Wraps a named export for `lazy`, which understands default exports only.
 *
 * Several of these modules deliberately export more than one screen — the five
 * stock reports share their column definitions, and brands and categories are the
 * same master with a different noun — so requiring a default export per screen
 * would mean splitting files that belong together.
 */
function named<TProps extends object, TName extends string>(
  load: () => Promise<Record<TName, ComponentType<TProps>>>,
  name: TName,
): React.LazyExoticComponent<ComponentType<TProps>> {
  return lazy(() => load().then((module) => ({ default: module[name] })));
}

const TrialBalancePage = named(
  () => import('@/pages/TrialBalancePage'),
  'TrialBalancePage',
);
const VoucherEntryPage = named(
  () => import('@/pages/VoucherEntryPage'),
  'VoucherEntryPage',
);
const ProfitAndLossPage = named(
  () => import('@/pages/ProfitAndLossPage'),
  'ProfitAndLossPage',
);
const BalanceSheetPage = named(
  () => import('@/pages/BalanceSheetPage'),
  'BalanceSheetPage',
);
const DayBookPage = named(() => import('@/pages/DayBookPage'), 'DayBookPage');
const VoucherReportPage = named(
  () => import('@/pages/VoucherReportPage'),
  'VoucherReportPage',
);
const TransactionSummaryPage = named(
  () => import('@/pages/TransactionSummaryPage'),
  'TransactionSummaryPage',
);
const CashFlowPage = named(() => import('@/pages/CashFlowPage'), 'CashFlowPage');
const MenuAdministrationPage = named(
  () => import('@/pages/MenuAdministrationPage'),
  'MenuAdministrationPage',
);
const LedgersPage = named(() => import('@/pages/LedgersPage'), 'LedgersPage');
const DashboardPage = named(() => import('@/pages/DashboardPage'), 'DashboardPage');
const UnitsPage = named(() => import('@/pages/UnitsPage'), 'UnitsPage');
const CategoriesPage = named(() => import('@/pages/CategoriesPage'), 'CategoriesPage');
const BrandsPage = named(() => import('@/pages/CategoriesPage'), 'BrandsPage');
const WarehousesPage = named(() => import('@/pages/WarehousesPage'), 'WarehousesPage');
const CustomersPage = named(() => import('@/pages/CustomersPage'), 'CustomersPage');
const ProductsPage = named(() => import('@/pages/ProductsPage'), 'ProductsPage');
const SalesPage = named(() => import('@/pages/SalesPage'), 'SalesPage');
const PurchasePage = named(() => import('@/pages/PurchasePage'), 'PurchasePage');
const SuppliersPage = named(() => import('@/pages/SuppliersPage'), 'SuppliersPage');
const TaxReturnsPage = named(() => import('@/pages/TaxReturnsPage'), 'TaxReturnsPage');
const StockOperationsPage = named(
  () => import('@/pages/StockOperationsPage'),
  'StockOperationsPage',
);
const AccountGroupSummaryPage = named(
  () => import('@/pages/AccountGroupSummaryPage'),
  'AccountGroupSummaryPage',
);
const PostDatedChequesPage = named(
  () => import('@/pages/PostDatedChequesPage'),
  'PostDatedChequesPage',
);
const ChequeCalendarPage = named(
  () => import('@/pages/ChequeCalendarPage'),
  'ChequeCalendarPage',
);
const ChequeRegisterPage = named(
  () => import('@/pages/ChequeRegisterPage'),
  'ChequeRegisterPage',
);

const BatchStockPage = named(() => import('@/pages/StockReportPages'), 'BatchStockPage');
const ExpiryReportPage = named(
  () => import('@/pages/StockReportPages'),
  'ExpiryReportPage',
);
const ItemMovementPage = named(
  () => import('@/pages/StockReportPages'),
  'ItemMovementPage',
);
const StockLedgerPage = named(
  () => import('@/pages/StockReportPages'),
  'StockLedgerPage',
);
const StockValuationPage = named(
  () => import('@/pages/StockReportPages'),
  'StockValuationPage',
);

/* The cash and bank books are one component told which of the two it is. */
const CashBankBookPage = named(
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
          {mustChangePassword && (
            <Route path="*" element={<Navigate to="/change-password" replace />} />
          )}
          {!mustChangePassword && (
            <>
              <Route path="/accounting/vouchers/new" element={<VoucherEntryPage />} />
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
              <Route path="/accounting/tax-returns" element={<TaxReturnsPage />} />
              <Route path="/settings/menu" element={<MenuAdministrationPage />} />
              <Route path="/accounting/ledgers" element={<LedgersPage />} />
              <Route path="/dashboard" element={<DashboardPage />} />
              <Route path="/sales/invoices" element={<SalesPage />} />
              <Route path="/sales/customers" element={<CustomersPage />} />
              <Route path="/purchase/invoices" element={<PurchasePage />} />
              <Route path="/purchase/suppliers" element={<SuppliersPage />} />
              <Route path="/inventory/products" element={<ProductsPage />} />
              <Route path="/inventory/stock" element={<StockOperationsPage />} />
              <Route path="/inventory/valuation" element={<StockValuationPage />} />
              <Route path="/inventory/stock-ledger" element={<StockLedgerPage />} />
              <Route path="/inventory/item-movement" element={<ItemMovementPage />} />
              <Route path="/inventory/batch-stock" element={<BatchStockPage />} />
              <Route path="/inventory/expiry" element={<ExpiryReportPage />} />
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
              <Route
                path="*"
                element={<Navigate to="/accounting/trial-balance" replace />}
              />
            </>
          )}
        </Route>
      </Routes>
    </Suspense>
  );
}
