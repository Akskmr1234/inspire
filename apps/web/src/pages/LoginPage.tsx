import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Spinner } from '@/components/ReportFrame';
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
    <div className="relative grid min-h-screen place-items-center overflow-hidden p-4">
      {/*
        Two soft colour fields behind the card. Purely decorative, so they are
        hidden from assistive technology and sit under a `pointer-events-none`
        layer — a sign-in screen must not have a div intercepting the tap that was
        meant for the password box.
      */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 overflow-hidden"
      >
        <div className="absolute -top-40 -start-32 size-[32rem] rounded-full bg-brand-500/15 blur-3xl" />
        <div className="absolute -bottom-48 -end-32 size-[34rem] rounded-full bg-sky-500/10 blur-3xl" />
      </div>

      <form
        onSubmit={(event) => void submit(event)}
        className="card animate-rise relative w-full max-w-sm p-6 shadow-float sm:p-8"
      >
        <div className="mb-7 flex items-center gap-3">
          <div className="grid size-11 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 font-bold text-white shadow-card">
            IE
          </div>
          <div className="min-w-0">
            <h1 className="truncate text-lg font-semibold tracking-tight text-ink">
              {t('app.name')}
            </h1>
            <p className="text-sm text-ink-muted">{t('signIn.subtitle')}</p>
          </div>
        </div>

        {error !== null && (
          <p role="alert" className="alert-error mb-4">
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
            <p className="field-hint">{t('signIn.companyHint')}</p>
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

        <button type="submit" disabled={busy} className="btn-primary mt-7 w-full">
          {busy && <Spinner />}
          {busy ? t('signIn.working') : t('signIn.submit')}
        </button>
      </form>
    </div>
  );
}
