import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError, getStoredTenantCode } from '@/lib/api';
import { useSession } from '@/stores/session';

/** The sign-in screen. */
export function LoginPage(): React.JSX.Element {
  const { t } = useTranslation();
  const signIn = useSession((s) => s.signIn);

  // Prefilled from the last successful sign-in. A company code is an identifier,
  // not a secret, and retyping it every morning is pure friction.
  const [tenantCode, setTenantCode] = useState(getStoredTenantCode() ?? '');
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: React.FormEvent): Promise<void> => {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      await signIn(userName, password, tenantCode);
    } catch (cause) {
      // The API deliberately returns the same message for an unknown user, a
      // wrong password, a disabled account, and a locked one - so it is shown
      // verbatim rather than being interpreted into something more specific.
      setError(
        cause instanceof ApiError
          ? cause.detail
          : 'Could not reach the server. Check your connection and try again.',
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="grid min-h-screen place-items-center p-4">
      <form
        onSubmit={(event) => void submit(event)}
        className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-8 shadow-sm dark:border-slate-800 dark:bg-slate-900"
      >
        <div className="mb-6 flex items-center gap-3">
          <div className="grid size-10 place-items-center rounded-xl bg-brand-600 font-bold text-white">
            IE
          </div>
          <div>
            <h1 className="text-lg font-semibold">{t('app.name')}</h1>
            <p className="text-sm text-slate-500 dark:text-slate-400">
              {t('signIn.subtitle')}
            </p>
          </div>
        </div>

        {error !== null && (
          <p
            role="alert"
            className="mb-4 rounded-lg border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200"
          >
            {error}
          </p>
        )}

        <div className="space-y-4">
          <div>
            <label htmlFor="tenantCode" className="field-label">
              {t('signIn.company')}
            </label>
            <input
              id="tenantCode"
              className="field-input"
              value={tenantCode}
              onChange={(e) => setTenantCode(e.target.value)}
              autoComplete="organization"
              spellCheck={false}
            />
            <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">
              {t('signIn.companyHint')}
            </p>
          </div>

          <div>
            <label htmlFor="userName" className="field-label">
              {t('signIn.userName')}
            </label>
            <input
              id="userName"
              className="field-input"
              value={userName}
              onChange={(e) => setUserName(e.target.value)}
              autoComplete="username"
              spellCheck={false}
              required
            />
          </div>

          <div>
            <label htmlFor="password" className="field-label">
              {t('signIn.password')}
            </label>
            <input
              id="password"
              type="password"
              className="field-input"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              required
            />
          </div>
        </div>

        <button type="submit" disabled={busy} className="btn-primary mt-6 w-full">
          {busy ? t('signIn.working') : t('signIn.submit')}
        </button>
      </form>
    </div>
  );
}
