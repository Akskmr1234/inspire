import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { PageHeading } from '@/components/PageHeading';
import { Modal, ModalButton } from '@/components/Modal';
import { Spinner } from '@/components/ReportFrame';
import { CheckField, TextField } from '@/components/Form';
import { ActiveBadge } from '@/components/StatusBadge';
import type { ApiError } from '@/lib/api';
import {
  createUser,
  listRoles,
  listUsers,
  resetPassword,
  updateUser,
  type RoleSummary,
  type UserSummary,
} from '@/lib/users';

/**
 * Who may sign in, and with which roles.
 *
 * A user is created with a starting password they have to change at their first
 * sign-in, so whoever typed it is never left holding a working one. The roles are what
 * decide what they may do and which menus they see; those are set on the roles screen.
 */
export function UsersPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  const [editing, setEditing] = useState<UserSummary | 'new' | null>(null);
  const [resetting, setResetting] = useState<UserSummary | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const users = useQuery<readonly UserSummary[], ApiError>({
    queryKey: ['platform-users'],
    queryFn: listUsers,
  });

  const roles = useQuery<readonly RoleSummary[], ApiError>({
    queryKey: ['platform-roles'],
    queryFn: listRoles,
  });

  const roleName = (id: string): string =>
    roles.data?.find((role) => role.roleId === id)?.name ?? '…';

  const done = (message: string): void => {
    setEditing(null);
    setResetting(null);
    setNotice(message);
    void queryClient.invalidateQueries({ queryKey: ['platform-users'] });
    void queryClient.invalidateQueries({ queryKey: ['platform-roles'] });
  };

  const lastLogin = (user: UserSummary): string =>
    user.lastLoginAtUtc ? new Date(user.lastLoginAtUtc).toLocaleString() : '—';

  const actions = (user: UserSummary): React.JSX.Element => (
    <>
      <button
        type="button"
        onClick={() => setEditing(user)}
        className="row-action row-action-neutral"
      >
        {t('common.edit')}
      </button>
      <button
        type="button"
        onClick={() => setResetting(user)}
        className="row-action row-action-neutral"
      >
        {t('users.resetPassword')}
      </button>
    </>
  );

  return (
    <section className="page">
      <PageHeading
        title={t('users.title')}
        actions={
          <button
            type="button"
            onClick={() => setEditing('new')}
            className="btn-primary btn-sm"
          >
            {t('users.new')}
          </button>
        }
      />

      {notice && (
        <p className="alert-success flex items-center gap-3">
          <span>{notice}</span>
          <button
            type="button"
            onClick={() => setNotice(null)}
            className="ms-auto text-xs font-medium underline underline-offset-2"
          >
            {t('common.dismiss')}
          </button>
        </p>
      )}

      {users.isPending && <Spinner />}
      {users.isError && (
        <p className="alert-error">{users.error.detail || users.error.code}</p>
      )}

      {users.data && (
        <>
          {/*
            A card a user below 1280px, the table only where its seven columns fit.
            Scrolled sideways, they left the roles, the status and both buttons off
            the screen on a phone and still the buttons on a laptop, so the one thing
            anybody comes here to do — edit somebody or reset their password — took a
            sideways drag to find.
          */}
          <ul className="grid gap-2 sm:grid-cols-2 xl:hidden">
            {users.data.map((user) => (
              <li key={user.userId} className="card card-body space-y-3 py-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="text-sm font-semibold text-ink [overflow-wrap:anywhere]">
                      {user.displayName}
                    </p>
                    <p className="font-mono text-xs text-ink-muted [overflow-wrap:anywhere]">
                      {user.userName}
                    </p>
                  </div>
                  <ActiveBadge isActive={user.isActive} />
                </div>

                <dl className="grid grid-cols-[minmax(0,auto)_minmax(0,1fr)] gap-x-3 gap-y-1.5 text-sm">
                  {user.email && (
                    <>
                      <dt className="text-xs tracking-wide text-ink-muted uppercase">
                        {t('users.email')}
                      </dt>
                      <dd className="min-w-0 text-end text-ink [overflow-wrap:anywhere]">
                        {user.email}
                      </dd>
                    </>
                  )}
                  <dt className="text-xs tracking-wide text-ink-muted uppercase">
                    {t('users.roles')}
                  </dt>
                  <dd className="min-w-0 text-end text-ink [overflow-wrap:anywhere]">
                    {user.roleIds.map(roleName).join(', ') || '—'}
                  </dd>
                  <dt className="text-xs tracking-wide text-ink-muted uppercase">
                    {t('users.lastLogin')}
                  </dt>
                  <dd className="min-w-0 text-end text-ink-muted">{lastLogin(user)}</dd>
                </dl>

                {(user.mustChangePassword || user.isLockedOut) && <Flags user={user} />}

                <div className="flex flex-wrap gap-2 border-t border-line pt-3">
                  {actions(user)}
                </div>
              </li>
            ))}
          </ul>

          <div className="table-wrap hidden xl:block">
            <table className="table min-w-[48rem] text-sm">
              <thead>
                <tr>
                  <th className="text-start">{t('users.userName')}</th>
                  <th className="text-start">{t('users.displayName')}</th>
                  <th className="text-start">{t('users.email')}</th>
                  <th className="text-start">{t('users.roles')}</th>
                  <th className="text-start">{t('users.status')}</th>
                  <th className="text-start">{t('users.lastLogin')}</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {users.data.map((user) => (
                  <tr key={user.userId}>
                    <td className="font-mono">{user.userName}</td>
                    <td>{user.displayName}</td>
                    <td className="text-ink-muted">{user.email}</td>
                    <td>{user.roleIds.map(roleName).join(', ')}</td>
                    <td>
                      <span className="inline-flex flex-wrap items-center gap-1">
                        <ActiveBadge isActive={user.isActive} />
                        <Flags user={user} />
                      </span>
                    </td>
                    <td className="text-ink-muted whitespace-nowrap">
                      {lastLogin(user)}
                    </td>
                    <td className="text-end whitespace-nowrap">
                      <span className="inline-flex gap-1">{actions(user)}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      {editing && roles.data && (
        <UserDialog
          user={editing === 'new' ? null : editing}
          roles={roles.data}
          onClose={() => setEditing(null)}
          onSaved={done}
        />
      )}

      {resetting && (
        <ResetPasswordDialog
          user={resetting}
          onClose={() => setResetting(null)}
          onDone={done}
        />
      )}
    </section>
  );
}

/** What else stands in the way of a user signing in, beside the account being active. */
function Flags({ user }: { readonly user: UserSummary }): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <span className="inline-flex flex-wrap items-center gap-x-2 gap-y-1">
      {user.mustChangePassword && (
        <span className="text-[11px] text-ink-muted">{t('users.mustChange')}</span>
      )}
      {user.isLockedOut && (
        <span className="text-[11px] text-red-600">{t('users.lockedOut')}</span>
      )}
    </span>
  );
}

function UserDialog({
  user,
  roles,
  onClose,
  onSaved,
}: {
  readonly user: UserSummary | null;
  readonly roles: readonly RoleSummary[];
  readonly onClose: () => void;
  readonly onSaved: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const [userName, setUserName] = useState(user?.userName ?? '');
  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [email, setEmail] = useState(user?.email ?? '');
  const [mobile, setMobile] = useState(user?.mobileNumber ?? '');
  const [password, setPassword] = useState('');
  const [roleIds, setRoleIds] = useState<readonly string[]>(user?.roleIds ?? []);
  const [active, setActive] = useState(user?.isActive ?? true);

  const save = useMutation<UserSummary, ApiError>({
    mutationFn: () => {
      const body = {
        userName: userName.trim(),
        email: email.trim(),
        displayName: displayName.trim(),
        mobileNumber: mobile.trim() || null,
        password: user ? null : password,
        roleIds,
        isActive: active,
      };

      return user ? updateUser(user.userId, body) : createUser(body);
    },
    onSuccess: (saved) =>
      onSaved(
        t(user ? 'users.updatedNotice' : 'users.createdNotice', { name: saved.userName }),
      ),
  });

  const toggleRole = (id: string): void =>
    setRoleIds((current) =>
      current.includes(id)
        ? current.filter((candidate) => candidate !== id)
        : [...current, id],
    );

  return (
    <Modal
      title={user ? t('users.editTitle', { name: user.userName }) : t('users.new')}
      size="form"
      onClose={onClose}
    >
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <TextField
          label={t('users.userName')}
          value={userName}
          onChange={setUserName}
          disabled={user !== null}
          required
          maxLength={100}
        />
        <TextField
          label={t('users.displayName')}
          value={displayName}
          onChange={setDisplayName}
          required
          maxLength={100}
        />
        <TextField
          label={t('users.email')}
          type="email"
          value={email}
          onChange={setEmail}
          required
          maxLength={256}
        />
        <TextField
          label={t('users.mobile')}
          type="tel"
          value={mobile}
          onChange={setMobile}
          maxLength={30}
        />
        {!user && (
          <label className="field sm:col-span-2">
            <span className="field-label">{t('users.password')}</span>
            <input
              type="password"
              autoComplete="new-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              className="field-input"
            />
            <span className="field-hint">{t('users.passwordHint')}</span>
          </label>
        )}
      </div>

      <fieldset className="space-y-1.5">
        <legend className="field-label">{t('users.roles')}</legend>
        <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-2">
          {roles.map((role) => (
            <label key={role.roleId} className="field-check items-start">
              <input
                type="checkbox"
                checked={roleIds.includes(role.roleId)}
                onChange={() => toggleRole(role.roleId)}
              />
              <span>
                <span className="font-medium">{role.name}</span>
                {role.description && (
                  <span className="block text-xs text-ink-muted">{role.description}</span>
                )}
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      <CheckField label={t('users.active')} checked={active} onChange={setActive} />

      {save.isError && (
        <p className="alert-error">{save.error.detail || save.error.code}</p>
      )}

      <div className="flex justify-end gap-2 border-t border-line pt-3">
        <ModalButton onClick={onClose}>{t('common.cancel')}</ModalButton>
        <ModalButton
          primary
          disabled={
            save.isPending ||
            userName.trim() === '' ||
            email.trim() === '' ||
            roleIds.length === 0 ||
            (!user && password.length === 0)
          }
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.saving') : t('common.save')}
        </ModalButton>
      </div>
    </Modal>
  );
}

function ResetPasswordDialog({
  user,
  onClose,
  onDone,
}: {
  readonly user: UserSummary;
  readonly onClose: () => void;
  readonly onDone: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [password, setPassword] = useState('');

  const reset = useMutation<void, ApiError>({
    mutationFn: () => resetPassword(user.userId, password),
    onSuccess: () => onDone(t('users.resetNotice', { name: user.userName })),
  });

  return (
    <Modal
      title={t('users.resetTitle', { name: user.userName })}
      size="form"
      onClose={onClose}
    >
      <label className="field">
        <span className="field-label">{t('users.newPassword')}</span>
        <input
          type="password"
          autoComplete="new-password"
          autoFocus
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          className="field-input"
        />
        <span className="field-hint">{t('users.passwordHint')}</span>
      </label>

      {reset.isError && (
        <p className="alert-error">{reset.error.detail || reset.error.code}</p>
      )}

      <div className="flex justify-end gap-2 border-t border-line pt-3">
        <ModalButton onClick={onClose}>{t('common.cancel')}</ModalButton>
        <ModalButton
          primary
          disabled={reset.isPending || password.length === 0}
          onClick={() => reset.mutate()}
        >
          {t('users.resetPassword')}
        </ModalButton>
      </div>
    </Modal>
  );
}
