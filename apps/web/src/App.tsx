import { useEffect } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { AppShell } from '@/components/AppShell';
import { LoginPage } from '@/pages/LoginPage';
import { TrialBalancePage } from '@/pages/TrialBalancePage';
import { VoucherEntryPage } from '@/pages/VoucherEntryPage';
import { ProfitAndLossPage } from '@/pages/ProfitAndLossPage';
import { BalanceSheetPage } from '@/pages/BalanceSheetPage';
import { DayBookPage } from '@/pages/DayBookPage';
import { VoucherReportPage } from '@/pages/VoucherReportPage';
import { TransactionSummaryPage } from '@/pages/TransactionSummaryPage';
import { CashBankBookPage } from '@/pages/CashBankBookPage';
import { AccountGroupSummaryPage } from '@/pages/AccountGroupSummaryPage';
import { PostDatedChequesPage } from '@/pages/PostDatedChequesPage';
import { ChequeCalendarPage } from '@/pages/ChequeCalendarPage';
import { ChequeRegisterPage } from '@/pages/ChequeRegisterPage';
import { applyPresentation, useSession } from '@/stores/session';

/** Routing and the signed-in gate. */
export function App(): React.JSX.Element {
  const { status, theme, language, restore } = useSession();

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
      <div className="grid min-h-screen place-items-center text-sm text-slate-500">
        Loading…
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
    <Routes>
      <Route element={<AppShell />}>
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
        <Route path="/accounting/cash-book" element={<CashBankBookPage book="cash-book" />} />
        <Route path="/accounting/bank-book" element={<CashBankBookPage book="bank-book" />} />
        <Route path="/accounting/post-dated-cheques" element={<PostDatedChequesPage />} />
        <Route path="/accounting/cheque-calendar" element={<ChequeCalendarPage />} />
        <Route path="/accounting/cheque-register" element={<ChequeRegisterPage />} />
        <Route path="*" element={<Navigate to="/accounting/trial-balance" replace />} />
      </Route>
    </Routes>
  );
}
