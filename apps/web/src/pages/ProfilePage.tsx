import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import clsx from 'clsx';
import { PageHeading } from '@/components/PageHeading';
import { Spinner } from '@/components/ReportFrame';
import { ApiError, fetchCurrentUserProfile, type UserProfile } from '@/lib/api';
import { useSession, setDefaultAppTheme, type Theme } from '@/stores/session';
import { useSettings } from '@/stores/settings';
import {
  IconCheck,
  IconClose,
  IconMoon,
  IconSun,
  IconLogout,
} from '@/components/icons';

function initials(name: string | null): string {
  if (!name) return 'SA';
  const parts = name.trim().split(/\s+/);
  if (parts.length >= 2 && parts[0] && parts[1]) {
    return ((parts[0][0] ?? '') + (parts[1][0] ?? '')).toUpperCase();
  }
  return name.slice(0, 2).toUpperCase() || 'SA';
}

type ProfileTab = 'overview' | 'security' | 'preferences' | 'permissions' | 'session';

export function ProfilePage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();

  const {
    displayName,
    tenantCode,
    permissions,
    theme,
    language,
    changePassword,
    signOut,
    setDisplayName,
  } = useSession();

  const settings = useSettings();

  const [activeTab, setActiveTab] = useState<ProfileTab>('overview');

  // Change password form state
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showCurrentPw, setShowCurrentPw] = useState(false);
  const [showNewPw, setShowNewPw] = useState(false);
  const [showConfirmPw, setShowConfirmPw] = useState(false);
  const [pwError, setPwError] = useState<string | null>(null);
  const [pwSuccess, setPwSuccess] = useState(false);
  const [pwBusy, setPwBusy] = useState(false);

  // Edit display name state
  const [editingName, setEditingName] = useState(false);
  const [nameInput, setNameInput] = useState(displayName ?? '');
  const [nameSaved, setNameSaved] = useState(false);

  // Default theme applied toast
  const [themeNotice, setThemeNotice] = useState<string | null>(null);

  // Fetch live user profile from server
  const profileQuery = useQuery<UserProfile, ApiError>({
    queryKey: ['auth', 'profile'],
    queryFn: fetchCurrentUserProfile,
    staleTime: 60 * 1000,
    retry: false,
  });

  const profile = profileQuery.data;

  useEffect(() => {
    if (profile?.displayName && profile.displayName !== displayName) {
      setDisplayName(profile.displayName);
    }
  }, [profile?.displayName, displayName, setDisplayName]);

  useEffect(() => {
    if (displayName) {
      setNameInput(displayName);
    } else if (profile?.displayName) {
      setNameInput(profile.displayName);
    }
  }, [displayName, profile?.displayName]);

  const handlePasswordSubmit = async (event: React.FormEvent): Promise<void> => {
    event.preventDefault();
    setPwError(null);
    setPwSuccess(false);

    if (newPassword !== confirmPassword) {
      setPwError(t('changePassword.mismatch'));
      return;
    }

    setPwBusy(true);

    try {
      await changePassword(currentPassword, newPassword);
      setPwSuccess(true);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      setTimeout(() => {
        navigate('/login?passwordChanged=1', { replace: true });
      }, 1500);
    } catch (cause) {
      setPwError(
        cause instanceof ApiError ? cause.detail : t('changePassword.unreachable'),
      );
    } finally {
      setPwBusy(false);
    }
  };

  const handleSaveDisplayName = (e: React.FormEvent): void => {
    e.preventDefault();
    if (nameInput.trim()) {
      setDisplayName(nameInput.trim());
      setEditingName(false);
      setNameSaved(true);
      setTimeout(() => setNameSaved(false), 3000);
    }
  };

  const handleApplyTheme = (newTheme: Theme): void => {
    settings.update({ defaultTheme: newTheme });
    setDefaultAppTheme(newTheme);
    setThemeNotice(
      newTheme === 'dark'
        ? 'Dark theme applied as application default for all users.'
        : 'Light theme applied as application default for all users.',
    );
    setTimeout(() => setThemeNotice(null), 4000);
  };

  const isSuperAdmin = permissions.has('*');
  const effectiveDisplayName = profile?.displayName || displayName || 'System Administrator';
  const effectiveUserName = profile?.userName || 'admin';
  const effectiveEmail = profile?.email || `${effectiveUserName.toLowerCase()}@inspire-erp.local`;
  const effectiveTenant = tenantCode || (profile?.tenantId ? `Tenant #${profile.tenantId.slice(0, 8)}` : 'Inspire ERP Main');

  return (
    <div className="page space-y-6">
      <PageHeading
        title={t('profile.title')}
        subtitle={t('profile.subtitle')}
      />

      {/* Hero Profile Banner Card */}
      <div className="card animate-rise overflow-hidden shadow-float">
        <div aria-hidden="true" className="relative h-28 sm:h-36">
          <div className="absolute inset-0 bg-gradient-to-r from-brand-700 via-indigo-600 to-sky-600" />
          <div className="absolute inset-0 bg-[radial-gradient(circle_at_20%_40%,rgba(255,255,255,0.18),transparent_60%)]" />
          <div className="absolute inset-0 bg-[radial-gradient(circle_at_80%_80%,rgba(255,255,255,0.12),transparent_50%)]" />
        </div>

        <div className="relative px-5 pb-5 sm:px-6 sm:pb-6">
          <div className="-mt-14 mb-4 flex flex-col sm:flex-row sm:items-end sm:justify-between gap-4">
            <div className="flex items-end gap-4">
              <div className="relative grid size-20 sm:size-24 shrink-0 place-items-center rounded-2xl border-4 border-surface bg-gradient-to-br from-brand-500 to-indigo-800 text-2xl sm:text-3xl font-bold tracking-tight text-white shadow-raised">
                {initials(effectiveDisplayName)}
                <span
                  title={t('profile.active')}
                  className="absolute -bottom-1 -end-1 size-4 rounded-full border-2 border-surface bg-emerald-500 shadow-xs"
                />
              </div>

              <div className="min-w-0 pb-1">
                <div className="flex items-center gap-2">
                  <h2 className="truncate text-xl sm:text-2xl font-bold tracking-tight text-ink">
                    {effectiveDisplayName}
                  </h2>
                  <span className="badge-brand text-xs">
                    {isSuperAdmin ? t('profile.adminRole') : 'User'}
                  </span>
                </div>
                <p className="text-xs sm:text-sm text-ink-muted">
                  @{effectiveUserName} • {effectiveEmail}
                </p>
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={() => setActiveTab('security')}
                className="btn-secondary text-xs sm:text-sm py-1.5 px-3 flex items-center gap-1.5"
              >
                <IconKey className="size-4" />
                <span>{t('changePassword.title')}</span>
              </button>
              <button
                type="button"
                onClick={() => void signOut()}
                className="flex items-center gap-1.5 rounded-lg border border-red-200 bg-red-50/70 px-3 py-1.5 text-xs sm:text-sm font-semibold text-red-600 transition hover:border-red-300 hover:bg-red-100 hover:text-red-700 active:scale-95 dark:border-red-900/40 dark:bg-red-950/20 dark:text-red-400 dark:hover:bg-red-950/40"
              >
                <IconLogout className="size-4" />
                <span>{t('nav.signOut')}</span>
              </button>
            </div>
          </div>

          {/* Quick Info Badges */}
          <div className="flex flex-wrap items-center gap-2 pt-3 border-t border-line/60">
            <span className="badge-brand inline-flex items-center gap-1.5 py-1 px-2.5 text-xs">
              <IconShield className="size-3.5" />
              <span>{t('profile.active')}</span>
            </span>
            <span className="badge-neutral inline-flex items-center gap-1.5 py-1 px-2.5 text-xs font-medium">
              <IconBuilding className="size-3.5 text-ink-muted" />
              <span>{effectiveTenant}</span>
            </span>
            <span className="badge-neutral inline-flex items-center gap-1.5 py-1 px-2.5 text-xs font-medium">
              <IconKey className="size-3.5 text-ink-muted" />
              <span>{isSuperAdmin ? t('profile.allPermissions') : t('profile.permissionCount', { count: permissions.size })}</span>
            </span>
            <span className="badge-neutral inline-flex items-center gap-1.5 py-1 px-2.5 text-xs font-medium">
              <span className="size-2 rounded-full bg-brand-500" />
              <span>Theme: {theme.toUpperCase()}</span>
            </span>
          </div>
        </div>
      </div>

      {themeNotice && (
        <div role="status" className="alert-success flex items-center justify-between">
          <span className="flex items-center gap-2">
            <IconCheck className="size-4 shrink-0 text-emerald-600" />
            <span>{themeNotice}</span>
          </span>
          <button
            type="button"
            onClick={() => setThemeNotice(null)}
            className="text-ink-muted hover:text-ink"
          >
            <IconClose className="size-4" />
          </button>
        </div>
      )}

      {/* Tabs Navigation */}
      <div className="flex overflow-x-auto border-b border-line gap-2 scrollbar-none pb-px">
        <TabButton
          active={activeTab === 'overview'}
          onClick={() => setActiveTab('overview')}
          icon={<IconUser className="size-4" />}
          label={t('profile.accountDetails')}
        />
        <TabButton
          active={activeTab === 'security'}
          onClick={() => setActiveTab('security')}
          icon={<IconLock className="size-4" />}
          label={t('profile.security')}
        />
        <TabButton
          active={activeTab === 'preferences'}
          onClick={() => setActiveTab('preferences')}
          icon={<IconSparkle className="size-4" />}
          label={t('profile.preferences')}
        />
        <TabButton
          active={activeTab === 'permissions'}
          onClick={() => setActiveTab('permissions')}
          icon={<IconShield className="size-4" />}
          label={t('profile.rolesAndPermissions')}
        />
        <TabButton
          active={activeTab === 'session'}
          onClick={() => setActiveTab('session')}
          icon={<IconClock className="size-4" />}
          label={t('profile.sessionInfo')}
        />
      </div>

      {/* TAB 1: Account Overview */}
      {activeTab === 'overview' && (
        <div className="grid gap-6 md:grid-cols-2 animate-fade-in">
          <div className="card p-5 space-y-4">
            <div className="flex items-center justify-between pb-3 border-b border-line">
              <div className="flex items-center gap-2.5">
                <div className="grid size-8 place-items-center rounded-lg bg-brand-500/10 text-brand-600 dark:text-brand-400">
                  <IconUser className="size-4" />
                </div>
                <h3 className="text-sm font-semibold text-ink">
                  Personal & Login Information
                </h3>
              </div>
              {!editingName && (
                <button
                  type="button"
                  onClick={() => {
                    setNameInput(effectiveDisplayName);
                    setEditingName(true);
                  }}
                  className="rounded-lg border border-line bg-surface px-2.5 py-1 text-xs font-semibold text-brand-600 hover:bg-surface-2 transition"
                >
                  Edit Name
                </button>
              )}
            </div>

            {nameSaved && (
              <p className="text-xs text-emerald-600 font-medium">
                ✓ Display name updated successfully.
              </p>
            )}

            {editingName ? (
              <form onSubmit={handleSaveDisplayName} className="space-y-3">
                <div>
                  <label htmlFor="editDisplayName" className="field-label">
                    {t('profile.displayName')}
                  </label>
                  <input
                    id="editDisplayName"
                    type="text"
                    className="field-input text-base sm:text-sm"
                    value={nameInput}
                    onChange={(e) => setNameInput(e.target.value)}
                    required
                  />
                </div>
                <div className="flex gap-2">
                  <button type="submit" className="btn-primary text-xs py-1.5 px-3">
                    Save
                  </button>
                  <button
                    type="button"
                    onClick={() => setEditingName(false)}
                    className="btn-secondary text-xs py-1.5 px-3"
                  >
                    Cancel
                  </button>
                </div>
              </form>
            ) : (
              <dl className="grid grid-cols-1 gap-3.5 sm:grid-cols-2 text-xs">
                <div>
                  <dt className="text-ink-muted mb-1">{t('profile.displayName')}</dt>
                  <dd className="rounded-lg border border-line/60 bg-surface-2/40 px-3 py-2 font-medium text-ink">
                    {effectiveDisplayName}
                  </dd>
                </div>
                <div>
                  <dt className="text-ink-muted mb-1">{t('profile.userName')}</dt>
                  <dd className="rounded-lg border border-line/60 bg-surface-2/40 px-3 py-2 font-mono text-ink">
                    @{effectiveUserName}
                  </dd>
                </div>
                <div>
                  <dt className="text-ink-muted mb-1">{t('profile.email')}</dt>
                  <dd className="rounded-lg border border-line/60 bg-surface-2/40 px-3 py-2 text-ink truncate">
                    {effectiveEmail}
                  </dd>
                </div>
                <div>
                  <dt className="text-ink-muted mb-1">Account Status</dt>
                  <dd className="rounded-lg border border-emerald-500/20 bg-emerald-500/10 px-3 py-2 font-medium text-emerald-600 dark:text-emerald-400">
                    Active & Verified
                  </dd>
                </div>
              </dl>
            )}

            <div className="pt-2">
              <span className="text-ink-muted mb-1 block text-xs">User ID</span>
              <CopyableValue value={profile?.userId ?? 'local-system-admin'} mono />
            </div>
          </div>

          <div className="card p-5 space-y-4">
            <div className="flex items-center gap-2.5 pb-3 border-b border-line">
              <div className="grid size-8 place-items-center rounded-lg bg-indigo-500/10 text-indigo-600 dark:text-indigo-400">
                <IconBuilding className="size-4" />
              </div>
              <h3 className="text-sm font-semibold text-ink">
                Tenancy & Organization Context
              </h3>
            </div>
            <dl className="grid grid-cols-1 gap-3.5 sm:grid-cols-2 text-xs">
              <div>
                <dt className="text-ink-muted mb-1">{t('profile.tenantCode')}</dt>
                <dd className="rounded-lg border border-line/60 bg-surface-2/40 px-3 py-2 font-semibold text-ink">
                  {effectiveTenant}
                </dd>
              </div>
              <div>
                <dt className="text-ink-muted mb-1">Active Branch</dt>
                <dd className="rounded-lg border border-line/60 bg-surface-2/40 px-3 py-2 font-medium text-ink">
                  {profile?.branchId ? `Branch #${profile.branchId}` : 'All Branches (HQ)'}
                </dd>
              </div>
              <div className="sm:col-span-2">
                <span className="text-ink-muted mb-1 block text-xs">Tenant Identifier</span>
                <CopyableValue value={profile?.tenantId ?? 'default-tenant'} mono />
              </div>
              <div className="sm:col-span-2">
                <span className="text-ink-muted mb-1 block text-xs">Firm ID</span>
                <CopyableValue value={profile?.firmId ?? 'Primary Operating Firm'} mono />
              </div>
            </dl>
          </div>
        </div>
      )}

      {/* TAB 2: Security & Change Password */}
      {activeTab === 'security' && (
        <div className="card p-6 max-w-2xl animate-fade-in space-y-5">
          <div className="flex items-center gap-3">
            <div className="grid size-10 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-600 dark:bg-brand-500/15 dark:text-brand-300">
              <IconLock className="size-5" />
            </div>
            <div>
              <h3 className="text-base font-semibold text-ink">
                {t('changePassword.title')}
              </h3>
              <p className="text-xs text-ink-muted">
                {t('profile.securitySubtitle')}
              </p>
            </div>
          </div>

          {pwError !== null && (
            <p role="alert" className="alert-error">
              {pwError}
            </p>
          )}

          {pwSuccess && (
            <p role="status" className="alert-success">
              {t('changePassword.success')}
            </p>
          )}

          <form onSubmit={(e) => void handlePasswordSubmit(e)} className="space-y-4">
            <div>
              <label htmlFor="currentPw" className="field-label">
                {t('changePassword.current')}
              </label>
              <div className="relative">
                <input
                  id="currentPw"
                  type={showCurrentPw ? 'text' : 'password'}
                  className="field-input text-base sm:text-sm pe-10"
                  value={currentPassword}
                  onChange={(e) => setCurrentPassword(e.target.value)}
                  autoComplete="current-password"
                  required
                />
                <button
                  type="button"
                  onClick={() => setShowCurrentPw((v) => !v)}
                  className="btn-icon absolute end-1.5 top-1/2 -translate-y-1/2 size-7 text-ink-subtle hover:text-ink"
                  aria-label={showCurrentPw ? 'Hide password' : 'Show password'}
                >
                  {showCurrentPw ? <IconEyeOff className="size-4" /> : <IconEye className="size-4" />}
                </button>
              </div>
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <label htmlFor="newPw" className="field-label">
                  {t('changePassword.new')}
                </label>
                <div className="relative">
                  <input
                    id="newPw"
                    type={showNewPw ? 'text' : 'password'}
                    className="field-input text-base sm:text-sm pe-10"
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    autoComplete="new-password"
                    minLength={12}
                    required
                  />
                  <button
                    type="button"
                    onClick={() => setShowNewPw((v) => !v)}
                    className="btn-icon absolute end-1.5 top-1/2 -translate-y-1/2 size-7 text-ink-subtle hover:text-ink"
                    aria-label={showNewPw ? 'Hide password' : 'Show password'}
                  >
                    {showNewPw ? <IconEyeOff className="size-4" /> : <IconEye className="size-4" />}
                  </button>
                </div>
                <p className="field-hint">{t('changePassword.policy')}</p>
              </div>

              <div>
                <label htmlFor="confirmPw" className="field-label">
                  {t('changePassword.confirm')}
                </label>
                <div className="relative">
                  <input
                    id="confirmPw"
                    type={showConfirmPw ? 'text' : 'password'}
                    className="field-input text-base sm:text-sm pe-10"
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    autoComplete="new-password"
                    minLength={12}
                    required
                  />
                  <button
                    type="button"
                    onClick={() => setShowConfirmPw((v) => !v)}
                    className="btn-icon absolute end-1.5 top-1/2 -translate-y-1/2 size-7 text-ink-subtle hover:text-ink"
                    aria-label={showConfirmPw ? 'Hide password' : 'Show password'}
                  >
                    {showConfirmPw ? <IconEyeOff className="size-4" /> : <IconEye className="size-4" />}
                  </button>
                </div>
              </div>
            </div>

            <div className="pt-2 flex flex-col sm:flex-row gap-3">
              <button
                type="submit"
                disabled={pwBusy}
                className="btn-primary flex-1 py-2"
              >
                {pwBusy && <Spinner />}
                {pwBusy ? t('changePassword.working') : t('changePassword.submit')}
              </button>
            </div>
          </form>
        </div>
      )}

      {/* TAB 3: Preferences & Appearance */}
      {activeTab === 'preferences' && (
        <div className="card p-6 max-w-2xl animate-fade-in space-y-6">
          <div>
            <div className="flex items-center gap-2">
              <h3 className="text-base font-semibold text-ink">
                {t('common.theme')} & Appearance (Application Default)
              </h3>
              <span className="rounded bg-brand-500/10 px-2 py-0.5 text-xs font-semibold text-brand-600 dark:bg-brand-500/20 dark:text-brand-300">
                All Users
              </span>
            </div>
            <p className="text-xs text-ink-muted mt-1">
              Select the appearance theme. When updated here, this theme is set as the application-wide default theme for all users and workstations.
            </p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <button
              type="button"
              onClick={() => handleApplyTheme('light')}
              className={clsx(
                'flex flex-col items-start gap-3 rounded-xl border p-4 text-start transition',
                theme === 'light'
                  ? 'border-brand-600 bg-brand-500/10 ring-2 ring-brand-500 text-brand-700 dark:text-brand-300 font-semibold shadow-xs'
                  : 'border-line bg-surface hover:bg-surface-2 text-ink',
              )}
            >
              <div className="flex items-center justify-between w-full">
                <span className="flex items-center gap-2 text-sm font-semibold">
                  <IconSun className="size-5 text-amber-500" />
                  <span>Light Theme</span>
                </span>
                {theme === 'light' && (
                  <span className="badge-brand text-[10px] py-0.5 px-1.5">Active Default</span>
                )}
              </div>
              <div className="h-16 w-full rounded border border-line/60 bg-slate-50 p-2 flex flex-col gap-1.5 justify-center shadow-inner">
                <div className="h-2 w-3/4 bg-slate-300 rounded" />
                <div className="h-2 w-1/2 bg-slate-200 rounded" />
              </div>
              <span className="text-xs text-ink-muted font-normal">
                Clean, high-contrast light mode designed for day-time clarity.
              </span>
            </button>

            <button
              type="button"
              onClick={() => handleApplyTheme('dark')}
              className={clsx(
                'flex flex-col items-start gap-3 rounded-xl border p-4 text-start transition',
                theme === 'dark'
                  ? 'border-brand-600 bg-brand-500/10 ring-2 ring-brand-500 text-brand-700 dark:text-brand-300 font-semibold shadow-xs'
                  : 'border-line bg-surface hover:bg-surface-2 text-ink',
              )}
            >
              <div className="flex items-center justify-between w-full">
                <span className="flex items-center gap-2 text-sm font-semibold">
                  <IconMoon className="size-5 text-indigo-400" />
                  <span>Dark Theme</span>
                </span>
                {theme === 'dark' && (
                  <span className="badge-brand text-[10px] py-0.5 px-1.5">Active Default</span>
                )}
              </div>
              <div className="h-16 w-full rounded border border-slate-700 bg-slate-900 p-2 flex flex-col gap-1.5 justify-center shadow-inner">
                <div className="h-2 w-3/4 bg-slate-700 rounded" />
                <div className="h-2 w-1/2 bg-slate-800 rounded" />
              </div>
              <span className="text-xs text-ink-muted font-normal">
                Sleek, low-glare dark mode optimized for eye comfort.
              </span>
            </button>
          </div>

          <div className="pt-4 border-t border-line flex items-center justify-between">
            <span className="text-xs text-ink-muted">
              More system-wide master defaults, rounding, numbering and series options are available in System Settings.
            </span>
            <button
              type="button"
              onClick={() => navigate('/settings')}
              className="btn-secondary text-xs py-1.5 px-3 shrink-0"
            >
              Open System Settings
            </button>
          </div>
        </div>
      )}

      {/* TAB 4: Roles & Permissions */}
      {activeTab === 'permissions' && (
        <div className="card p-6 animate-fade-in space-y-4">
          <div className="flex items-center justify-between pb-3 border-b border-line">
            <div>
              <h3 className="text-base font-semibold text-ink">
                Effective Access Privileges
              </h3>
              <p className="text-xs text-ink-muted mt-0.5">
                Permissions assigned to your account for this tenant and firm.
              </p>
            </div>
            <span className="badge-brand">
              {isSuperAdmin ? 'Full Access' : `${permissions.size} Granted`}
            </span>
          </div>

          {isSuperAdmin ? (
            <div className="rounded-xl border border-brand-200 bg-brand-50/50 p-4 dark:border-brand-900/40 dark:bg-brand-950/20 space-y-2">
              <div className="flex items-center gap-2 text-sm font-semibold text-brand-700 dark:text-brand-300">
                <IconShield className="size-5 text-brand-600" />
                <span>Super Administrator Privilege (*)</span>
              </div>
              <p className="text-xs text-ink-muted">
                Your role carries wildcard system permissions. You have complete authority to manage inventory, accounting, sales, purchasing, menu structures, and user configurations.
              </p>
            </div>
          ) : (
            <div className="flex flex-wrap gap-2 pt-2">
              {Array.from(permissions).map((perm) => (
                <span
                  key={perm}
                  className="rounded-lg border border-line bg-surface-2 px-2.5 py-1 font-mono text-xs text-ink"
                >
                  {perm}
                </span>
              ))}
            </div>
          )}
        </div>
      )}

      {/* TAB 5: Session & Activity */}
      {activeTab === 'session' && (
        <div className="card p-6 animate-fade-in space-y-5">
          <div className="flex items-center justify-between pb-3 border-b border-line">
            <div>
              <h3 className="text-base font-semibold text-ink">
                Current Session Information
              </h3>
              <p className="text-xs text-ink-muted">
                Details about your active browser session and security posture.
              </p>
            </div>
            <span className="badge-brand">Online</span>
          </div>

          <dl className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-xs">
            <div className="rounded-lg border border-line p-3">
              <dt className="text-ink-muted">Browser / Client Platform</dt>
              <dd className="font-medium text-ink mt-1 truncate">
                {typeof navigator !== 'undefined' ? navigator.userAgent : 'Web Browser'}
              </dd>
            </div>
            <div className="rounded-lg border border-line p-3">
              <dt className="text-ink-muted">Active Language</dt>
              <dd className="font-medium text-ink mt-1">
                {language === 'ar' ? 'Arabic (العربية) • RTL' : 'English • LTR'}
              </dd>
            </div>
            <div className="rounded-lg border border-line p-3">
              <dt className="text-ink-muted">Token Rotation Policy</dt>
              <dd className="font-medium text-emerald-600 mt-1">
                Short-lived Access Token with Family Revocation Active
              </dd>
            </div>
            <div className="rounded-lg border border-line p-3">
              <dt className="text-ink-muted">Currency Precision</dt>
              <dd className="font-medium text-ink mt-1">
                {settings.decimals} Decimals
              </dd>
            </div>
          </dl>

          <div className="pt-4 border-t border-line flex justify-end">
            <button
              type="button"
              onClick={() => void signOut()}
              className="flex items-center gap-2 rounded-lg border border-red-200 bg-red-50/70 px-4 py-2 text-xs font-semibold text-red-600 transition hover:border-red-300 hover:bg-red-100 hover:text-red-700 active:scale-95 dark:border-red-900/40 dark:bg-red-950/20 dark:text-red-400 dark:hover:bg-red-950/40"
            >
              <IconLogout className="size-4" />
              <span>{t('profile.signOutDevice')}</span>
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function TabButton({
  active,
  onClick,
  icon,
  label,
}: {
  readonly active: boolean;
  readonly onClick: () => void;
  readonly icon: React.ReactNode;
  readonly label: string;
}): React.JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      className={clsx(
        'group flex items-center gap-2 border-b-2 px-4 py-2.5 text-xs sm:text-sm font-medium whitespace-nowrap transition duration-150',
        active
          ? 'border-brand-600 text-brand-600 dark:border-brand-400 dark:text-brand-400 font-semibold'
          : 'border-transparent text-ink-muted hover:border-line-strong hover:text-ink',
      )}
    >
      <span className={clsx(active ? 'text-brand-600 dark:text-brand-400' : 'text-ink-subtle group-hover:text-ink')}>
        {icon}
      </span>
      <span>{label}</span>
    </button>
  );
}

function CopyableValue({
  value,
  mono = false,
}: {
  readonly value: string;
  readonly mono?: boolean;
}): React.JSX.Element {
  const [copied, setCopied] = useState(false);
  const handleCopy = (): void => {
    if (navigator?.clipboard?.writeText) {
      void navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  return (
    <div className="flex items-center justify-between gap-2 rounded-lg border border-line/60 bg-surface-2/60 px-3 py-1.5 transition hover:border-line-strong">
      <span className={clsx('truncate text-xs text-ink select-all', mono && 'font-mono text-[11px]')}>
        {value}
      </span>
      <button
        type="button"
        onClick={handleCopy}
        title="Copy"
        aria-label="Copy"
        className="btn-icon size-6 shrink-0 text-ink-subtle hover:text-brand-600 active:scale-95"
      >
        {copied ? (
          <IconCheck className="size-3 text-emerald-600" />
        ) : (
          <IconCopy className="size-3" />
        )}
      </button>
    </div>
  );
}

function IconCopy(props: { className?: string }): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={props.className ?? 'size-3.5'}
    >
      <rect x="9" y="9" width="13" height="13" rx="2" ry="2" />
      <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
    </svg>
  );
}

function IconEye(props: { className?: string }): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={props.className ?? 'size-4'}
    >
      <path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z" />
      <circle cx="12" cy="12" r="3" />
    </svg>
  );
}

function IconEyeOff(props: { className?: string }): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={props.className ?? 'size-4'}
    >
      <path d="M9.88 9.88a3 3 0 1 0 4.24 4.24" />
      <path d="M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68" />
      <path d="M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61" />
      <line x1="2" x2="22" y1="2" y2="22" />
    </svg>
  );
}

/* ---------------------------------------------------------------------------
   Inline glyphs
   --------------------------------------------------------------------------- */

function IconShield(props: { className?: string }): React.JSX.Element {
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

function IconKey(props: { className?: string }): React.JSX.Element {
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
      <circle cx="8" cy="15" r="5" />
      <path d="M11.6 11.4 21 2" />
      <path d="M17 6h4v4" />
    </svg>
  );
}

function IconLock(props: { className?: string }): React.JSX.Element {
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
      <rect x="5" y="11" width="14" height="10" rx="2" />
      <path d="M8 11V7a4 4 0 0 1 8 0v4" />
      <circle cx="12" cy="16" r="1.2" />
    </svg>
  );
}

function IconBuilding(props: { className?: string }): React.JSX.Element {
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
      <path d="M6 22V4a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v18Z" />
      <path d="M6 12H4a2 2 0 0 0-2 2v8h4" />
      <path d="M18 9h2a2 2 0 0 1 2 2v11h-4" />
      <path d="M10 6h4" />
      <path d="M10 10h4" />
      <path d="M10 14h4" />
      <path d="M10 18h4" />
    </svg>
  );
}

function IconUser(props: { className?: string }): React.JSX.Element {
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
      <circle cx="12" cy="8" r="5" />
      <path d="M20 21a8 8 0 0 0-16 0" />
    </svg>
  );
}

function IconClock(props: { className?: string }): React.JSX.Element {
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
      <circle cx="12" cy="12" r="10" />
      <polyline points="12 6 12 12 16 14" />
    </svg>
  );
}

function IconSparkle(props: { className?: string }): React.JSX.Element {
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
      <path d="m12 3-1.9 5.8a2 2 0 0 1-1.3 1.3L3 12l5.8 1.9a2 2 0 0 1 1.3 1.3L12 21l1.9-5.8a2 2 0 0 1 1.3-1.3L21 12l-5.8-1.9a2 2 0 0 1-1.3-1.3Z" />
    </svg>
  );
}
