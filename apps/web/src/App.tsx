import { useEffect } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { AppShell } from '@/components/AppShell';
import { LoginPage } from '@/pages/LoginPage';
import { TrialBalancePage } from '@/pages/TrialBalancePage';
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
        <Route path="/accounting/trial-balance" element={<TrialBalancePage />} />
        <Route
          path="/accounting/profit-and-loss"
          element={<Placeholder title="Profit and loss" />}
        />
        <Route
          path="/accounting/balance-sheet"
          element={<Placeholder title="Balance sheet" />}
        />
        <Route path="*" element={<Navigate to="/accounting/trial-balance" replace />} />
      </Route>
    </Routes>
  );
}

/**
 * A visible marker for a route whose screen is not built yet.
 *
 * Deliberately says so rather than rendering an empty page: an unfinished screen
 * that looks finished is worse than one that admits it.
 */
function Placeholder({ title }: { readonly title: string }): React.JSX.Element {
  return (
    <section className="space-y-2">
      <h1 className="text-xl font-semibold">{title}</h1>
      <p className="rounded-lg border border-dashed border-slate-300 px-4 py-6 text-sm text-slate-500 dark:border-slate-700">
        The API endpoint for this report is implemented and tested; this screen is
        not built yet.
      </p>
    </section>
  );
}
