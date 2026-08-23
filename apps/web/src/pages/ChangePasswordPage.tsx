import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { Spinner } from '@/components/ReportFrame';
import { ApiError } from '@/lib/api';
import { useSession } from '@/stores/session';

/**
 * Extracts up to two initials from a display name.
 *
 * "Khalid Al-Rashidi" → "KA", "admin" → "AD". Falls back to a generic
 * placeholder when there is no name at all — which happens when the session
 * was restored from a refresh token rather than a sign-in, since the
 * refresh response does not carry the display name.
 */
function initials(name: string | null): string {
  if (!name) return '?';
  const parts = name.trim().split(/\s+/);
  if (parts.length >= 2 && parts[0] && parts[1]) {
    return ((parts[0][0] ?? '') + (parts[1][0] ?? '')).toUpperCase();
  }
  return name.slice(0, 2).toUpperCase();
}

/** Profile and password change screen. */
export function ChangePasswordPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const displayName = useSession((s) => s.displayName);
  const changePassword = useSession((s) => s.changePassword);
  const signOut = useSession((s) => s.signOut);

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const [busy, setBusy] = useState(false);

  const submit = async (event: React.FormEvent): Promise<void> => {
    event.preventDefault();
    setError(null);
    setSuccess(false);

    if (newPassword !== confirmPassword) {
      setError(t('changePassword.mismatch'));
      return;
    }

    setBusy(true);

    try {
      await changePassword(currentPassword, newPassword);
      navigate('/login?passwordChanged=1', { replace: true });
    } catch (cause) {
      setError(
        cause instanceof ApiError ? cause.detail : t('changePassword.unreachable'),
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="page">
      {/* ----------------------------------------------------------------- *
       *  Profile header                                                     *
       * ----------------------------------------------------------------- */}
      <div className="card animate-rise overflow-hidden shadow-float">
        {/* Decorative gradient band across the top of the card. Purely visual,
            so hidden from assistive tech. */}
        <div aria-hidden="true" className="relative h-28 sm:h-32">
          <div className="absolute inset-0 bg-gradient-to-br from-brand-600 via-brand-500 to-sky-400" />
          {/* Subtle noise texture overlay for depth */}
          <div className="absolute inset-0 bg-[radial-gradient(circle_at_30%_50%,rgba(255,255,255,0.12),transparent_70%)]" />
          <div className="absolute inset-0 bg-[radial-gradient(circle_at_80%_20%,rgba(255,255,255,0.08),transparent_50%)]" />
        </div>

        <div className="relative px-5 pb-5 sm:px-6 sm:pb-6">
          {/* Avatar — pulled up to overlap the gradient band */}
          <div className="-mt-12 mb-3 flex items-end gap-4 sm:-mt-14">
            <div className="grid size-20 shrink-0 place-items-center rounded-2xl border-4 border-surface bg-gradient-to-br from-brand-500 to-brand-700 text-2xl font-bold tracking-tight text-white shadow-raised sm:size-24 sm:text-3xl">
              {initials(displayName)}
            </div>
            <div className="min-w-0 pb-1">
              <h1 className="truncate text-lg font-semibold tracking-tight text-ink sm:text-xl">
                {displayName ?? t('profile.anonymous')}
              </h1>
              <p className="text-sm text-ink-muted">{t('profile.subtitle')}</p>
            </div>
          </div>

          {/* Quick stats / info chips */}
          <div className="mt-2 flex flex-wrap gap-2">
            <span className="badge-brand">
              <IconShield />
              {t('profile.active')}
            </span>
            <span className="badge-neutral">
              <IconKey />
              {t('profile.passwordManaged')}
            </span>
          </div>
        </div>
      </div>

      {/* ----------------------------------------------------------------- *
       *  Change password form                                               *
       * ----------------------------------------------------------------- */}
      <div
        className="card animate-rise p-5 shadow-card sm:p-6"
        style={{ animationDelay: '60ms' }}
      >
        <div className="mb-5">
          <div className="flex items-center gap-2.5">
            <div className="grid size-9 shrink-0 place-items-center rounded-lg bg-brand-50 text-brand-600 dark:bg-brand-500/15 dark:text-brand-300">
              <IconLock />
            </div>
            <div>
              <h2 className="text-base font-semibold tracking-tight text-ink">
                {t('changePassword.title')}
              </h2>
              <p className="text-sm text-ink-muted">{t('changePassword.subtitle')}</p>
            </div>
          </div>
        </div>

        {error !== null && (
          <p role="alert" className="alert-error mb-4">
            {error}
          </p>
        )}

        {success && (
          <p role="status" className="alert-success mb-4">
            {t('changePassword.success')}
          </p>
        )}

        <form onSubmit={(event) => void submit(event)}>
          <div className="space-y-4">
            <div>
              <label htmlFor="currentPassword" className="field-label">
                {t('changePassword.current')}
              </label>
              <input
                id="currentPassword"
                type="password"
                className="field-input"
                value={currentPassword}
                onChange={(event) => setCurrentPassword(event.target.value)}
                autoComplete="current-password"
                required
              />
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <label htmlFor="newPassword" className="field-label">
                  {t('changePassword.new')}
                </label>
                <input
                  id="newPassword"
                  type="password"
                  className="field-input"
                  value={newPassword}
                  onChange={(event) => setNewPassword(event.target.value)}
                  autoComplete="new-password"
                  minLength={12}
                  required
                />
                <p className="field-hint">{t('changePassword.policy')}</p>
              </div>

              <div>
                <label htmlFor="confirmPassword" className="field-label">
                  {t('changePassword.confirm')}
                </label>
                <input
                  id="confirmPassword"
                  type="password"
                  className="field-input"
                  value={confirmPassword}
                  onChange={(event) => setConfirmPassword(event.target.value)}
                  autoComplete="new-password"
                  minLength={12}
                  required
                />
              </div>
            </div>
          </div>

          <hr className="divider my-5" />

          <div className="flex flex-col gap-3 sm:flex-row-reverse">
            <button type="submit" disabled={busy} className="btn-primary flex-1">
              {busy && <Spinner />}
              {busy ? t('changePassword.working') : t('changePassword.submit')}
            </button>
            <button
              type="button"
              onClick={() => void signOut()}
              disabled={busy}
              className="btn-secondary flex-1"
            >
              {t('nav.signOut')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

/* ---------------------------------------------------------------------------
   Inline icons — small, purposeful glyphs used only on this page. Drawn
   inline to avoid widening the shared icon module for decorative chips that
   nobody else uses.
   --------------------------------------------------------------------------- */

function IconShield(): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className="size-3.5 shrink-0"
    >
      <path d="M12 3 4 7v5c0 5.3 3.4 10 8 11 4.6-1 8-5.7 8-11V7z" />
      <path d="m9 12 2 2 4-4" />
    </svg>
  );
}

function IconKey(): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className="size-3.5 shrink-0"
    >
      <circle cx="8" cy="15" r="5" />
      <path d="M11.6 11.4 21 2" />
      <path d="M17 6h4v4" />
    </svg>
  );
}

function IconLock(): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className="size-[18px] shrink-0"
    >
      <rect x="5" y="11" width="14" height="10" rx="2" />
      <path d="M8 11V7a4 4 0 0 1 8 0v4" />
      <circle cx="12" cy="16" r="1.2" />
    </svg>
  );
}
