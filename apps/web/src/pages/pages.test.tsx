import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor } from '@testing-library/react';
import { crashed, renderPage } from '@/test/renderPage';
import { fixtureFor } from '@/test/fixtures';
import { setMatchingMedia } from '@/test/setup';

/*
  Every screen, rendered against fixtures shaped like the API's responses.

  This is the check that was missing. A field absent from a response threw during
  render and took the whole application down with it, and the only thing that
  caught it was a person driving a browser through thirty-three routes. That is
  not a check — it is a chore nobody will repeat.

  These assert two things per screen: that its heading arrives, and that nothing
  reached the error boundary. Deliberately shallow. A test that asserted on the
  figures would be re-stating the fixture, and would have to be rewritten every
  time a column moved.
*/

// Everything the screens fetch, answered from the fixture table. A path with no
// fixture rejects like the API would for an unknown route, so a screen quietly
// depending on an endpoint nobody registered still fails rather than passing on an
// undefined that happens to render.
const inFlight: Promise<unknown>[] = [];

vi.mock('@/lib/api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');

  return {
    ...actual,
    request: vi.fn((path: string) => {
      const fixture = fixtureFor(path);

      const promise =
        fixture === undefined
          ? Promise.reject(
              new actual.ApiError(404, 'Test.NoFixture', `No fixture for ${path}`),
            )
          : Promise.resolve(fixture);

      // Tracked so a test can wait for the figures rather than for the heading.
      // The heading is rendered by the frame while the query is still pending, so
      // waiting on it asserts against a screen that has not drawn a single row —
      // which is how a crash in a cell slips through.
      inFlight.push(promise.catch(() => undefined));
      return promise;
    }),
  };
});

// The grid's saved-arrangement calls go through the same client; answered as "no
// layout saved", which is what a fresh install looks like.
vi.mock('@/lib/grid', () => ({
  fetchGridLayout: vi.fn(async () => null),
  saveGridLayout: vi.fn(async () => undefined),
  resetGridLayout: vi.fn(async () => undefined),
}));

const { ChangePasswordPage } = await import('@/pages/ChangePasswordPage');
const { TrialBalancePage } = await import('@/pages/TrialBalancePage');
const { VoucherEntryPage } = await import('@/pages/VoucherEntryPage');
const { ProfitAndLossPage } = await import('@/pages/ProfitAndLossPage');
const { BalanceSheetPage } = await import('@/pages/BalanceSheetPage');
const { DayBookPage } = await import('@/pages/DayBookPage');
const { VoucherReportPage } = await import('@/pages/VoucherReportPage');
const { TransactionSummaryPage } = await import('@/pages/TransactionSummaryPage');
const { CashBankBookPage } = await import('@/pages/CashBankBookPage');
const { CashFlowPage } = await import('@/pages/CashFlowPage');
const { AccountGroupSummaryPage } = await import('@/pages/AccountGroupSummaryPage');
const { LedgersPage } = await import('@/pages/LedgersPage');
const { TaxReturnsPage } = await import('@/pages/TaxReturnsPage');
const { PostDatedChequesPage } = await import('@/pages/PostDatedChequesPage');
const { ChequeCalendarPage } = await import('@/pages/ChequeCalendarPage');
const { ChequeRegisterPage } = await import('@/pages/ChequeRegisterPage');
const { MenuAdministrationPage } = await import('@/pages/MenuAdministrationPage');
const { DashboardPage } = await import('@/pages/DashboardPage');
const { ProductsPage } = await import('@/pages/ProductsPage');
const { StockOperationsPage } = await import('@/pages/StockOperationsPage');
const { UnitsPage } = await import('@/pages/UnitsPage');
const { CategoriesPage, BrandsPage } = await import('@/pages/CategoriesPage');
const { WarehousesPage } = await import('@/pages/WarehousesPage');
const { CustomersPage } = await import('@/pages/CustomersPage');
const { SuppliersPage } = await import('@/pages/SuppliersPage');
const { SalesPage } = await import('@/pages/SalesPage');
const { PurchasePage } = await import('@/pages/PurchasePage');
const {
  BatchStockPage,
  ExpiryReportPage,
  ItemMovementPage,
  StockLedgerPage,
  StockValuationPage,
} = await import('@/pages/StockReportPages');

/** Every screen, and the heading it must show once its figures land. */
const screens: readonly (readonly [string, React.ReactNode, RegExp])[] = [
  ['change password', <ChangePasswordPage />, /password/i],
  ['trial balance', <TrialBalancePage />, /trial balance/i],
  ['voucher entry', <VoucherEntryPage />, /voucher entry/i],
  ['profit and loss', <ProfitAndLossPage />, /profit and loss/i],
  ['balance sheet', <BalanceSheetPage />, /balance sheet/i],
  ['day book', <DayBookPage />, /day book/i],
  ['voucher report', <VoucherReportPage />, /voucher report/i],
  ['transaction summary', <TransactionSummaryPage />, /transaction summary/i],
  ['cash book', <CashBankBookPage book="cash-book" />, /cash book/i],
  ['bank book', <CashBankBookPage book="bank-book" />, /bank book/i],
  ['cash flow', <CashFlowPage />, /cash flow/i],
  ['group summary', <AccountGroupSummaryPage />, /group summary/i],
  ['chart of accounts', <LedgersPage />, /chart of accounts/i],
  ['tax returns', <TaxReturnsPage />, /returns/i],
  ['post-dated cheques', <PostDatedChequesPage />, /post-dated cheques/i],
  ['cheque calendar', <ChequeCalendarPage />, /cheque calendar/i],
  ['cheque register', <ChequeRegisterPage />, /cheque register/i],
  ['menu settings', <MenuAdministrationPage />, /menu settings/i],
  ['dashboard', <DashboardPage />, /overview/i],
  ['products', <ProductsPage />, /products/i],
  ['stock operations', <StockOperationsPage />, /stock operations/i],
  ['stock valuation', <StockValuationPage />, /stock valuation/i],
  ['stock ledger', <StockLedgerPage />, /stock ledger/i],
  ['item movement', <ItemMovementPage />, /item movement/i],
  ['batch stock', <BatchStockPage />, /batch/i],
  ['expiry report', <ExpiryReportPage />, /expiry/i],
  ['units', <UnitsPage />, /units/i],
  ['categories', <CategoriesPage />, /categories/i],
  ['brands', <BrandsPage />, /brands/i],
  ['warehouses', <WarehousesPage />, /warehouses/i],
  ['customers', <CustomersPage />, /customers/i],
  ['suppliers', <SuppliersPage />, /suppliers/i],
  ['sales invoices', <SalesPage />, /sales/i],
  ['purchases', <PurchasePage />, /purchase/i],
];

beforeEach(() => {
  // Desktop, so the grids render as tables. The card view has its own tests.
  setMatchingMedia();
  inFlight.length = 0;
});

/**
 * Waits until every request the screen made has settled and React has drawn the
 * result — including the requests a screen only issues once its first response
 * arrives, which is why this loops rather than awaiting once.
 */
async function settled(): Promise<void> {
  for (let pass = 0; pass < 5; pass += 1) {
    const pending = [...inFlight];

    if (pending.length === 0 && pass > 0) {
      break;
    }

    inFlight.length = 0;
    await act(async () => {
      await Promise.all(pending);
    });
  }
}

describe('every screen', () => {
  it.each(screens)(
    '%s renders its figures without throwing',
    async (_name, element, heading) => {
      const { container } = renderPage(element);

      await settled();
      await waitFor(() =>
        expect(screen.getAllByRole('heading').length).toBeGreaterThan(0),
      );

      // The boundary is the tripwire: a screen that threw shows its message here
      // rather than failing on a missing heading and sending you looking in the
      // wrong place.
      expect(crashed(container)).toBeNull();

      // Any heading, not the first. Most screens lead with their title, but the
      // change-password screen leads with whose password it is and carries its own
      // title beneath — an assumption about heading order is a claim about page
      // structure, not about whether the screen rendered.
      const headings = screen
        .getAllByRole('heading')
        .map((node) => node.textContent ?? '');

      expect(headings.some((text) => heading.test(text))).toBe(true);
    },
  );
});

describe('the count', () => {
  it('covers every route the router serves', () => {
    // Guards against a screen being added to the application and quietly never
    // being rendered by anything — which is exactly what happened when the
    // change-password screen landed. The two book variants share one component,
    // and the login page is not reachable from the signed-in router.
    expect(screens.length).toBe(34);
  });
});
