import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { Spinner } from '@/components/ReportFrame';
import {
  IconBuilding,
  IconEye,
  IconEyeOff,
  IconLock,
  IconUser,
} from '@/components/icons';
import { ApiError, getStoredTenantCode } from '@/lib/api';
import { useSession } from '@/stores/session';

/** The sign-in screen. */
export function LoginPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const signIn = useSession((s) => s.signIn);

  // Prefilled from the last successful sign-in. A company code is an identifier,
  // not a secret, and retyping it every morning is pure friction.
  const [tenantCode, setTenantCode] = useState(getStoredTenantCode() ?? '');
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
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
        <div className="absolute -top-40 -start-32 size-[36rem] rounded-full bg-brand-500/15 blur-3xl" />
        <div className="absolute -bottom-48 -end-32 size-[38rem] rounded-full bg-sky-500/10 blur-3xl" />
        <div className="absolute top-1/2 start-1/2 -translate-x-1/2 -translate-y-1/2 size-96 rounded-full bg-indigo-500/5 blur-2xl" />
      </div>

      <form
        onSubmit={(event) => void submit(event)}
        className="card animate-rise relative w-full max-w-sm rounded-2xl border border-line p-6 shadow-float sm:p-8"
      >
        <div className="mb-7 flex items-center gap-3">
          <div className="relative grid size-12 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-brand-500 via-brand-600 to-brand-700 font-bold text-white shadow-card ring-1 ring-white/20">
            AE
          </div>
          <div className="min-w-0">
            <div className="flex items-center gap-1.5">
              <h1 className="truncate text-lg font-bold tracking-tight text-ink">
                {t('app.name')}
              </h1>
              <span className="rounded bg-brand-500/10 px-1.5 py-0.5 text-[9px] font-bold text-brand-600 uppercase tracking-wider dark:bg-brand-500/20 dark:text-brand-300">
                ERP
              </span>
            </div>
            <p className="text-xs text-ink-muted">{t('signIn.subtitle')}</p>
          </div>
        </div>

        {error !== null && (
          <p role="alert" className="alert-error mb-4">
            {error}
          </p>
        )}

        {error === null && searchParams.get('passwordChanged') === '1' && (
          <p role="status" className="alert-success mb-4">
            {t('signIn.passwordChanged')}
          </p>
        )}

        <div className="space-y-4">
          <div>
            <label htmlFor="tenantCode" className="field-label">
              {t('signIn.company')}
            </label>
            <div className="relative">
              <span
                aria-hidden="true"
                className="pointer-events-none absolute inset-y-0 start-3 flex items-center text-ink-subtle"
              >
                <IconBuilding className="size-4" />
              </span>
              <input
                id="tenantCode"
                className="field-input ps-9"
                value={tenantCode}
                onChange={(e) => setTenantCode(e.target.value)}
                autoComplete="organization"
                spellCheck={false}
              />
            </div>
            <p className="field-hint">{t('signIn.companyHint')}</p>
          </div>

          <div>
            <label htmlFor="userName" className="field-label">
              {t('signIn.userName')}
            </label>
            <div className="relative">
              <span
                aria-hidden="true"
                className="pointer-events-none absolute inset-y-0 start-3 flex items-center text-ink-subtle"
              >
                <IconUser className="size-4" />
              </span>
              <input
                id="userName"
                className="field-input ps-9"
                value={userName}
                onChange={(e) => setUserName(e.target.value)}
                autoComplete="username"
                spellCheck={false}
                required
              />
            </div>
          </div>

          <div>
            <label htmlFor="password" className="field-label">
              {t('signIn.password')}
            </label>
            <div className="relative">
              <span
                aria-hidden="true"
                className="pointer-events-none absolute inset-y-0 start-3 flex items-center text-ink-subtle"
              >
                <IconLock className="size-4" />
              </span>
              <input
                id="password"
                type={showPassword ? 'text' : 'password'}
                className="field-input ps-9 pe-10"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                required
              />
              <button
                type="button"
                onClick={() => setShowPassword((v) => !v)}
                className="absolute inset-y-0 end-2 my-auto grid size-7 place-items-center rounded text-ink-subtle transition hover:text-ink"
                aria-label={showPassword ? 'Hide password' : 'Show password'}
                title={showPassword ? 'Hide password' : 'Show password'}
              >
                {showPassword ? <IconEyeOff className="size-4" /> : <IconEye className="size-4" />}
              </button>
            </div>
          </div>
        </div>

        <button type="submit" disabled={busy} className="btn-primary mt-6 w-full py-2.5">
          {busy && <Spinner />}
          {busy ? t('signIn.working') : t('signIn.submit')}
        </button>

        <div className="mt-6 border-t border-line/60 pt-4 text-center">
          <p className="flex items-center justify-center gap-1.5 text-[11px] text-ink-subtle">
            <span className="size-1.5 rounded-full bg-emerald-500" />
            256-bit TLS encrypted session
          </p>
        </div>
      </form>
    </div>
  );
}
