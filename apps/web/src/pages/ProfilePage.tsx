import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useSearchParams } from 'react-router-dom';
import clsx from 'clsx';
import i18next from '@/i18n';
import { PageHeading } from '@/components/PageHeading';
import { Spinner } from '@/components/ReportFrame';
import { ApiError } from '@/lib/api';
import {
  describeDevice,
  endOtherSessions,
  endSession,
  fetchMyAccount,
  groupPermissions,
  humanise,
  passwordStrength,
  updateMyAccount,
  type MyAccount,
  type MySession,
} from '@/lib/account';
import { useSession, type Language, type Theme } from '@/stores/session';
import {
  IconBuilding,
  IconCheck,
  IconEye,
  IconEyeOff,
  IconLock,
  IconLogout,
  IconMoon,
  IconSun,
  IconUser,
} from '@/components/icons';

type ProfileTab = 'details' | 'security' | 'preferences' | 'access';

const TABS: readonly ProfileTab[] = ['details', 'security', 'preferences', 'access'];

const ACCOUNT_KEY = ['auth', 'account'] as const;

/**
 * The signed-in user's own page: who they are, how they sign in, how the application
 * looks for them, and what their roles let them do.
 *
 * Everything on it is read from the server and everything editable is saved there.
 * The tab is in the address, so the menu's "Change password" can open this page at
 * the security tab and a reload keeps the user where they were.
 */
export function ProfilePage(): React.JSX.Element {
  const { t } = useTranslation();
  const [params, setParams] = useSearchParams();
  const requested = params.get('tab') as ProfileTab | null;
  const tab: ProfileTab = requested && TABS.includes(requested) ? requested : 'details';

  const account = useQuery<MyAccount, ApiError>({
    queryKey: ACCOUNT_KEY,
    queryFn: fetchMyAccount,
    staleTime: 30 * 1000,
  });

  const setTab = (next: ProfileTab): void => {
    setParams(next === 'details' ? {} : { tab: next }, { replace: true });
  };

  return (
    <div className="page space-y-5">
      <PageHeading title={t('profile.title')} />

      {account.isPending && (
        <div className="card card-body flex items-center gap-2 text-sm text-ink-muted">
          <Spinner /> {t('common.loading')}
        </div>
      )}

      {account.isError && (
        <p role="alert" className="alert-error">
          {account.error.detail}
        </p>
      )}

      {account.data && (
        <>
          <ProfileHeader account={account.data} onSecurity={() => setTab('security')} />

          <div
            role="tablist"
            aria-label={t('profile.title')}
            className="flex gap-1 overflow-x-auto border-b border-line"
          >
            {TABS.map((name) => (
              <button
                key={name}
                type="button"
                role="tab"
                aria-selected={tab === name}
                onClick={() => setTab(name)}
                className={clsx(
                  '-mb-px flex shrink-0 items-center gap-2 border-b-2 px-3 py-2.5 text-sm font-medium whitespace-nowrap transition',
                  tab === name
                    ? 'border-brand-600 text-brand-700 dark:border-brand-400 dark:text-brand-300'
                    : 'border-transparent text-ink-muted hover:border-line-strong hover:text-ink',
                )}
              >
                {t(`profile.tabs.${name}`)}
              </button>
            ))}
          </div>

          {tab === 'details' && <DetailsTab account={account.data} />}
          {tab === 'security' && <SecurityTab account={account.data} />}
          {tab === 'preferences' && <PreferencesTab />}
          {tab === 'access' && <AccessTab account={account.data} />}
        </>
      )}
    </div>
  );
}

function ProfileHeader({
  account,
  onSecurity,
}: {
  readonly account: MyAccount;
  readonly onSecurity: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const signOut = useSession((s) => s.signOut);
  const passwordAge = daysSince(account.passwordChangedAtUtc);
  const stalePassword = passwordAge === null || passwordAge > 180;

  return (
    <section className="card overflow-hidden">
      <div
        aria-hidden="true"
        className="h-20 bg-gradient-to-r from-brand-700 via-brand-600 to-sky-500 sm:h-24"
      />
      <div className="px-4 pb-4 sm:px-6 sm:pb-5">
        {/*
          The avatar rides up into the banner; the name stays below it. On a phone the
          two stack, because beside an 80px avatar the name would have to start on
          the banner to line up with its foot.
        */}
        <div className="-mt-10 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
          <div className="flex min-w-0 flex-col gap-3 sm:flex-row sm:items-end sm:gap-4">
            <span className="grid size-20 shrink-0 place-items-center rounded-2xl border-4 border-surface bg-gradient-to-br from-brand-500 to-indigo-700 text-2xl font-bold text-white shadow-raised sm:size-24 sm:text-3xl">
              {initialsOf(account.displayName)}
            </span>
            <div className="min-w-0 sm:pb-1">
              <h2 className="truncate text-xl font-bold tracking-tight text-ink sm:text-2xl">
                {account.displayName}
              </h2>
              <p className="truncate text-sm text-ink-muted">
                @{account.userName}
                {account.email && (
                  <span className="hidden sm:inline"> · {account.email}</span>
                )}
              </p>
            </div>
          </div>

          <div className="flex flex-wrap gap-2">
            <button type="button" onClick={onSecurity} className="btn-secondary btn-sm">
              <IconLock className="size-4" />
              {t('changePassword.title')}
            </button>
            <button
              type="button"
              onClick={() => void signOut()}
              className="btn-secondary btn-sm text-red-600 hover:text-red-700 dark:text-red-400"
            >
              <IconLogout className="size-4" />
              {t('nav.signOut')}
            </button>
          </div>
        </div>

        <div className="mt-4 flex flex-wrap gap-2 border-t border-line/70 pt-3">
          {account.roles.map((role) => (
            <span key={role.name} className="badge-brand" title={role.description}>
              {role.name}
            </span>
          ))}
          {account.roles.length === 0 && (
            <span className="badge-neutral">{t('profile.noRoles')}</span>
          )}
          <span className="badge-neutral inline-flex items-center gap-1.5">
            <IconBuilding className="size-3.5" />
            {account.tenantName || account.tenantCode}
          </span>
        </div>

        <dl className="mt-4 grid grid-cols-1 gap-3 text-sm sm:grid-cols-3">
          <Fact
            label={t('profile.lastSignIn')}
            value={formatWhen(account.lastLoginAtUtc, t('profile.never'))}
          />
          <Fact
            label={t('profile.passwordChanged')}
            value={formatWhen(account.passwordChangedAtUtc, t('profile.never'))}
            warn={stalePassword}
          />
          <Fact
            label={t('profile.signedInOn')}
            value={t('profile.deviceCount', { count: account.sessions.length })}
          />
        </dl>

        {account.mustChangePassword && (
          <p className="alert-warn mt-4">{t('common.mustChangePassword')}</p>
        )}
      </div>
    </section>
  );
}

function Fact({
  label,
  value,
  warn = false,
}: {
  readonly label: string;
  readonly value: string;
  readonly warn?: boolean;
}): React.JSX.Element {
  return (
    <div className="rounded-lg border border-line/70 bg-surface-2/50 px-3 py-2">
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd
        className={clsx(
          'mt-0.5 font-medium',
          warn ? 'text-amber-700 dark:text-amber-300' : 'text-ink',
        )}
      >
        {value}
      </dd>
    </div>
  );
}

/* --------------------------------------------------------------------------
   Details
   -------------------------------------------------------------------------- */

function DetailsTab({ account }: { readonly account: MyAccount }): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const setDisplayName = useSession((s) => s.setDisplayName);
  const [name, setName] = useState(account.displayName);
  const [email, setEmail] = useState(account.email);
  const [mobile, setMobile] = useState(account.mobileNumber ?? '');
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    setName(account.displayName);
    setEmail(account.email);
    setMobile(account.mobileNumber ?? '');
  }, [account.displayName, account.email, account.mobileNumber]);

  const dirty =
    name.trim() !== account.displayName ||
    email.trim().toLowerCase() !== account.email ||
    mobile.trim() !== (account.mobileNumber ?? '');

  const save = useMutation<MyAccount, ApiError>({
    mutationFn: () =>
      updateMyAccount({
        displayName: name.trim(),
        email: email.trim(),
        mobileNumber: mobile.trim() || null,
      }),
    onSuccess: (updated) => {
      queryClient.setQueryData(ACCOUNT_KEY, updated);
      setDisplayName(updated.displayName);
      setSaved(true);
      window.setTimeout(() => setSaved(false), 3000);
    },
  });

  return (
    <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-3">
      <form
        className="card card-body space-y-4 lg:col-span-2"
        onSubmit={(event) => {
          event.preventDefault();
          setSaved(false);
          save.mutate();
        }}
      >
        <SectionTitle
          icon={<IconUser className="size-4" />}
          title={t('profile.personalDetails')}
          hint={t('profile.personalHint')}
        />

        {save.isError && (
          <p role="alert" className="alert-error">
            {save.error.detail}
          </p>
        )}
        {saved && (
          <p role="status" className="alert-success">
            {t('profile.detailsSaved')}
          </p>
        )}

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div className="sm:col-span-2">
            <label htmlFor="profileName" className="field-label">
              {t('profile.displayName')}
            </label>
            <input
              id="profileName"
              className="field-input"
              value={name}
              maxLength={150}
              required
              onChange={(e) => setName(e.target.value)}
            />
          </div>
          <div>
            <label htmlFor="profileEmail" className="field-label">
              {t('profile.email')}
            </label>
            <input
              id="profileEmail"
              type="email"
              className="field-input"
              value={email}
              required
              autoComplete="email"
              onChange={(e) => setEmail(e.target.value)}
            />
          </div>
          <div>
            <label htmlFor="profileMobile" className="field-label">
              {t('profile.mobile')}
            </label>
            <input
              id="profileMobile"
              type="tel"
              className="field-input"
              value={mobile}
              autoComplete="tel"
              onChange={(e) => setMobile(e.target.value)}
            />
          </div>
        </div>

        <div className="flex flex-wrap items-center justify-end gap-2 border-t border-line pt-4">
          <button
            type="button"
            className="btn-secondary btn-sm"
            disabled={!dirty || save.isPending}
            onClick={() => {
              setName(account.displayName);
              setEmail(account.email);
              setMobile(account.mobileNumber ?? '');
            }}
          >
            {t('common.cancel')}
          </button>
          <button
            type="submit"
            className="btn-primary btn-sm"
            disabled={!dirty || save.isPending}
          >
            {save.isPending && <Spinner />}
            {save.isPending ? t('common.saving') : t('common.save')}
          </button>
        </div>
      </form>

      <section className="card card-body space-y-3">
        <SectionTitle
          icon={<IconBuilding className="size-4" />}
          title={t('profile.signInDetails')}
        />
        <dl className="space-y-3 text-sm">
          <ReadOnly label={t('profile.userName')} value={`@${account.userName}`} mono />
          <ReadOnly
            label={t('profile.company')}
            value={
              account.tenantName
                ? `${account.tenantName} (${account.tenantCode})`
                : account.tenantCode
            }
          />
          <ReadOnly
            label={t('profile.memberSince')}
            value={formatDate(account.createdAtUtc)}
          />
        </dl>
      </section>
    </div>
  );
}

function ReadOnly({
  label,
  value,
  mono = false,
}: {
  readonly label: string;
  readonly value: string;
  readonly mono?: boolean;
}): React.JSX.Element {
  return (
    <div>
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd
        className={clsx('mt-0.5 break-words font-medium text-ink', mono && 'font-mono')}
      >
        {value}
      </dd>
    </div>
  );
}

/* --------------------------------------------------------------------------
   Security
   -------------------------------------------------------------------------- */

function SecurityTab({ account }: { readonly account: MyAccount }): React.JSX.Element {
  return (
    <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-2">
      <PasswordCard />
      <SessionsCard sessions={account.sessions} />
    </div>
  );
}

function PasswordCard(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const changePassword = useSession((s) => s.changePassword);
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [show, setShow] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);

  const strength = passwordStrength(next);
  const mismatch = confirm.length > 0 && confirm !== next;

  const submit = async (event: React.FormEvent): Promise<void> => {
    event.preventDefault();
    setError(null);
    if (next !== confirm) {
      setError(t('changePassword.mismatch'));
      return;
    }
    if (next === current) {
      setError(t('profile.samePassword'));
      return;
    }
    setBusy(true);
    try {
      await changePassword(current, next);
      setDone(true);
      window.setTimeout(
        () => navigate('/login?passwordChanged=1', { replace: true }),
        1500,
      );
    } catch (cause) {
      setError(
        cause instanceof ApiError ? cause.detail : t('changePassword.unreachable'),
      );
    } finally {
      setBusy(false);
    }
  };

  const type = show ? 'text' : 'password';

  return (
    <form className="card card-body space-y-4" onSubmit={(e) => void submit(e)}>
      <SectionTitle
        icon={<IconLock className="size-4" />}
        title={t('changePassword.title')}
        hint={t('profile.passwordHint')}
      />

      {error && (
        <p role="alert" className="alert-error">
          {error}
        </p>
      )}
      {done && (
        <p role="status" className="alert-success">
          {t('profile.passwordChangedSignIn')}
        </p>
      )}

      <div>
        <label htmlFor="currentPassword" className="field-label">
          {t('changePassword.current')}
        </label>
        <input
          id="currentPassword"
          type={type}
          className="field-input"
          value={current}
          autoComplete="current-password"
          required
          onChange={(e) => setCurrent(e.target.value)}
        />
      </div>
      <div>
        <label htmlFor="newPassword" className="field-label">
          {t('changePassword.new')}
        </label>
        <input
          id="newPassword"
          type={type}
          className="field-input"
          value={next}
          autoComplete="new-password"
          minLength={12}
          required
          onChange={(e) => setNext(e.target.value)}
        />
        <div className="mt-2 flex items-center gap-2" aria-live="polite">
          <div className="grid flex-1 grid-cols-4 gap-1">
            {[1, 2, 3, 4].map((step) => (
              <span
                key={step}
                className={clsx(
                  'h-1.5 rounded-full',
                  strength >= step
                    ? strength <= 1
                      ? 'bg-red-500'
                      : strength === 2
                        ? 'bg-amber-500'
                        : 'bg-emerald-500'
                    : 'bg-surface-3',
                )}
              />
            ))}
          </div>
          <span className="w-20 text-end text-xs text-ink-muted">
            {strength > 0 ? t(`profile.strength.${strength}`) : ''}
          </span>
        </div>
        <p className="field-hint">{t('changePassword.policy')}</p>
      </div>
      <div>
        <label htmlFor="confirmPassword" className="field-label">
          {t('changePassword.confirm')}
        </label>
        <input
          id="confirmPassword"
          type={type}
          className={clsx('field-input', mismatch && 'field-invalid')}
          value={confirm}
          autoComplete="new-password"
          minLength={12}
          required
          onChange={(e) => setConfirm(e.target.value)}
        />
        {mismatch && (
          <p className="field-hint text-red-600">{t('changePassword.mismatch')}</p>
        )}
      </div>

      <label className="flex items-center gap-2 text-sm text-ink-muted">
        <input
          type="checkbox"
          className="field-check"
          checked={show}
          onChange={(e) => setShow(e.target.checked)}
        />
        {show ? <IconEyeOff className="size-4" /> : <IconEye className="size-4" />}
        {t('profile.showPasswords')}
      </label>

      <p className="text-xs text-ink-muted">{t('profile.passwordSignsOut')}</p>

      <div className="flex justify-end border-t border-line pt-4">
        <button type="submit" className="btn-primary btn-sm" disabled={busy || done}>
          {busy && <Spinner />}
          {busy ? t('changePassword.working') : t('changePassword.submit')}
        </button>
      </div>
    </form>
  );
}

function SessionsCard({
  sessions,
}: {
  readonly sessions: readonly MySession[];
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const signOut = useSession((s) => s.signOut);
  const [notice, setNotice] = useState<string | null>(null);

  const refresh = (): Promise<void> =>
    queryClient.invalidateQueries({ queryKey: ACCOUNT_KEY });

  const end = useMutation<void, ApiError, MySession>({
    mutationFn: (session) => endSession(session.sessionId),
    onSuccess: async (_, session) => {
      if (session.isCurrent) {
        await signOut();
        return;
      }
      setNotice(t('profile.sessionEnded'));
      await refresh();
    },
  });

  const others = useMutation<{ readonly ended: number }, ApiError>({
    mutationFn: endOtherSessions,
    onSuccess: async ({ ended }) => {
      setNotice(t('profile.othersEnded', { count: ended }));
      await refresh();
    },
  });

  const otherCount = sessions.filter((s) => !s.isCurrent).length;
  const failure = end.error ?? others.error;

  return (
    <section className="card card-body space-y-4">
      <SectionTitle
        icon={<IconShield className="size-4" />}
        title={t('profile.whereSignedIn')}
        hint={t('profile.whereSignedInHint')}
      />

      {failure && (
        <p role="alert" className="alert-error">
          {failure.detail}
        </p>
      )}
      {notice && (
        <p role="status" className="alert-success">
          {notice}
        </p>
      )}

      <ul className="divide-y divide-line rounded-lg border border-line">
        {sessions.map((session) => {
          const device = describeDevice(session.userAgent);
          const name =
            [device.browser, device.system].filter(Boolean).join(t('profile.on')) ||
            t('profile.unknownDevice');
          return (
            <li key={session.sessionId} className="flex items-start gap-3 p-3">
              <span className="mt-0.5 grid size-9 shrink-0 place-items-center rounded-lg bg-surface-2 text-ink-muted">
                {device.mobile ? <IconPhone /> : <IconDesktop />}
              </span>
              <div className="min-w-0 flex-1">
                <p className="flex flex-wrap items-center gap-2 text-sm font-medium text-ink">
                  <span className="truncate">{name}</span>
                  {session.isCurrent && (
                    <span className="badge-success">{t('profile.thisDevice')}</span>
                  )}
                </p>
                <p className="text-xs text-ink-muted">
                  {t('profile.sessionTimes', {
                    signedIn: formatWhen(session.signedInAtUtc, ''),
                    active: formatRelative(session.lastActiveAtUtc),
                  })}
                </p>
                {session.ipAddress && (
                  <p className="font-mono text-[11px] text-ink-subtle">
                    {session.ipAddress}
                  </p>
                )}
              </div>
              <button
                type="button"
                className="btn-ghost btn-xs shrink-0 text-red-600 dark:text-red-400"
                disabled={end.isPending}
                onClick={() => end.mutate(session)}
              >
                {t('nav.signOut')}
              </button>
            </li>
          );
        })}
        {sessions.length === 0 && (
          <li className="p-3 text-sm text-ink-muted">{t('profile.noSessions')}</li>
        )}
      </ul>

      <div className="flex justify-end">
        <button
          type="button"
          className="btn-secondary btn-sm"
          disabled={otherCount === 0 || others.isPending}
          onClick={() => others.mutate()}
        >
          {others.isPending && <Spinner />}
          {t('profile.signOutOthers', { count: otherCount })}
        </button>
      </div>
    </section>
  );
}

/* --------------------------------------------------------------------------
   Preferences
   -------------------------------------------------------------------------- */

function PreferencesTab(): React.JSX.Element {
  const { t } = useTranslation();
  const { theme, language, setTheme, setLanguage } = useSession();

  const changeLanguage = (next: Language): void => {
    setLanguage(next);
    void i18next.changeLanguage(next);
  };

  return (
    <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
      <section className="card card-body space-y-4">
        <SectionTitle title={t('common.theme')} hint={t('profile.themeHint')} />
        <div className="grid grid-cols-2 gap-3">
          {(['light', 'dark'] as const).map((option: Theme) => (
            <button
              key={option}
              type="button"
              aria-pressed={theme === option}
              onClick={() => setTheme(option)}
              className={clsx(
                'flex flex-col gap-2 rounded-xl border p-3 text-start transition',
                theme === option
                  ? 'border-brand-600 ring-2 ring-brand-500/40'
                  : 'border-line hover:border-line-strong',
              )}
            >
              <span
                aria-hidden="true"
                className={clsx(
                  'flex h-14 flex-col justify-center gap-1.5 rounded-lg border p-2',
                  option === 'light'
                    ? 'border-slate-200 bg-slate-50'
                    : 'border-slate-700 bg-slate-900',
                )}
              >
                <span
                  className={clsx(
                    'h-2 w-3/4 rounded',
                    option === 'light' ? 'bg-slate-300' : 'bg-slate-600',
                  )}
                />
                <span
                  className={clsx(
                    'h-2 w-1/2 rounded',
                    option === 'light' ? 'bg-slate-200' : 'bg-slate-700',
                  )}
                />
              </span>
              <span className="flex items-center gap-2 text-sm font-medium text-ink">
                {option === 'light' ? (
                  <IconSun className="size-4 text-amber-500" />
                ) : (
                  <IconMoon className="size-4 text-indigo-400" />
                )}
                {t(`profile.${option}`)}
                {theme === option && (
                  <IconCheck className="ms-auto size-4 text-brand-600" />
                )}
              </span>
            </button>
          ))}
        </div>
      </section>

      <section className="card card-body space-y-4">
        <SectionTitle title={t('common.language')} hint={t('profile.languageHint')} />
        <div className="grid grid-cols-2 gap-3">
          {(
            [
              ['en', 'English', 'LTR'],
              ['ar', 'العربية', 'RTL'],
            ] as const
          ).map(([code, label, direction]) => (
            <button
              key={code}
              type="button"
              aria-pressed={language === code}
              onClick={() => changeLanguage(code)}
              className={clsx(
                'flex items-center justify-between gap-2 rounded-xl border p-3 text-start transition',
                language === code
                  ? 'border-brand-600 ring-2 ring-brand-500/40'
                  : 'border-line hover:border-line-strong',
              )}
            >
              <span>
                <span className="block text-sm font-medium text-ink">{label}</span>
                <span className="block text-xs text-ink-muted">{direction}</span>
              </span>
              {language === code && <IconCheck className="size-4 text-brand-600" />}
            </button>
          ))}
        </div>
      </section>
    </div>
  );
}

/* --------------------------------------------------------------------------
   Access
   -------------------------------------------------------------------------- */

function AccessTab({ account }: { readonly account: MyAccount }): React.JSX.Element {
  const { t } = useTranslation();
  const permissions = useSession((s) => s.permissions);
  const everything =
    permissions.has('*') || account.roles.some((role) => role.grantsAllPermissions);
  const grouped = useMemo(() => groupPermissions(permissions), [permissions]);

  return (
    <div className="space-y-5">
      <section className="card card-body space-y-3">
        <SectionTitle
          icon={<IconShield className="size-4" />}
          title={t('profile.yourRoles')}
          hint={t('profile.rolesHint')}
        />
        <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          {account.roles.map((role) => (
            <li key={role.name} className="rounded-lg border border-line p-3">
              <p className="flex items-center gap-2 text-sm font-semibold text-ink">
                {role.name}
                {role.grantsAllPermissions && (
                  <span className="badge-brand">{t('profile.fullAccess')}</span>
                )}
              </p>
              {role.description && (
                <p className="mt-1 text-xs text-ink-muted">{role.description}</p>
              )}
            </li>
          ))}
          {account.roles.length === 0 && (
            <li className="text-sm text-ink-muted">{t('profile.noRoles')}</li>
          )}
        </ul>
      </section>

      <section className="card card-body space-y-3">
        <SectionTitle title={t('profile.whatYouCanDo')} />
        {everything ? (
          <p className="alert-info">{t('profile.everything')}</p>
        ) : grouped.size === 0 ? (
          <p className="text-sm text-ink-muted">{t('profile.nothingGranted')}</p>
        ) : (
          <div className="space-y-4">
            {[...grouped.entries()].map(([module, resources]) => (
              <div key={module}>
                <h4 className="mb-2 text-xs font-semibold tracking-wide text-ink-muted uppercase">
                  {t(`roles.modules.${module}`, { defaultValue: humanise(module) })}
                </h4>
                <ul className="divide-y divide-line rounded-lg border border-line">
                  {[...resources.entries()].map(([resource, verbs]) => (
                    <li
                      key={resource}
                      className="flex flex-col gap-1.5 px-3 py-2 sm:flex-row sm:items-center sm:justify-between"
                    >
                      <span className="text-sm text-ink">{humanise(resource)}</span>
                      <span className="flex flex-wrap gap-1">
                        {verbs.map((verb) => (
                          <span key={verb} className="badge-neutral">
                            {t(`roles.verbs.${verb}`, { defaultValue: humanise(verb) })}
                          </span>
                        ))}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

/* --------------------------------------------------------------------------
   Pieces
   -------------------------------------------------------------------------- */

/** A section heading. The hint is accepted and not shown: labels say enough. */
function SectionTitle({
  icon,
  title,
  hint: _hint,
}: {
  readonly icon?: React.ReactNode;
  readonly title: string;
  readonly hint?: string;
}): React.JSX.Element {
  return (
    <div className="flex items-start gap-3">
      {icon && (
        <span className="grid size-8 shrink-0 place-items-center rounded-lg bg-brand-500/10 text-brand-600 dark:text-brand-300">
          {icon}
        </span>
      )}
      <div className="min-w-0">
        <h3 className="text-sm font-semibold text-ink">{title}</h3>
      </div>
    </div>
  );
}

function initialsOf(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  const letters = words
    .slice(0, 2)
    .map((word) => word[0]?.toUpperCase() ?? '')
    .join('');
  return letters || '?';
}

function locale(): string {
  return i18next.language === 'ar' ? 'ar' : 'en-GB';
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(locale(), {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  });
}

function formatWhen(iso: string | null, fallback: string): string {
  if (!iso) return fallback;
  return new Date(iso).toLocaleString(locale(), {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function formatRelative(iso: string): string {
  const seconds = Math.round((new Date(iso).getTime() - Date.now()) / 1000);
  const format = new Intl.RelativeTimeFormat(locale(), { numeric: 'auto' });
  const abs = Math.abs(seconds);
  if (abs < 60) return format.format(0, 'second');
  if (abs < 3600) return format.format(Math.round(seconds / 60), 'minute');
  if (abs < 86400) return format.format(Math.round(seconds / 3600), 'hour');
  return format.format(Math.round(seconds / 86400), 'day');
}

function daysSince(iso: string | null): number | null {
  if (!iso) return null;
  return Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000);
}

function IconShield(props: { readonly className?: string }): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={props.className ?? 'size-4'}
    >
      <path d="M12 3 4 7v5c0 5.3 3.4 10 8 11 4.6-1 8-5.7 8-11V7z" />
      <path d="m9 12 2 2 4-4" />
    </svg>
  );
}

function IconPhone(): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      aria-hidden="true"
      className="size-5"
    >
      <rect x="7" y="2" width="10" height="20" rx="2" />
      <path d="M11 18h2" />
    </svg>
  );
}

function IconDesktop(): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      aria-hidden="true"
      className="size-5"
    >
      <rect x="2" y="4" width="20" height="13" rx="2" />
      <path d="M8 21h8M12 17v4" />
    </svg>
  );
}
