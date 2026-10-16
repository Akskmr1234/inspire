import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { PageHeading } from '@/components/PageHeading';
import { Spinner } from '@/components/ReportFrame';
import { TextField } from '@/components/Form';
import type { ApiError } from '@/lib/api';
import { fetchMenuAdmin, type MenuAdmin, type MenuAdminEntry } from '@/lib/menu';
import {
  createRole,
  deleteRole,
  listPermissions,
  listRoles,
  updateRole,
  type PermissionDefinition,
  type RoleSummary,
} from '@/lib/users';

const VERBS = ['view', 'create', 'edit', 'delete', 'approve', 'print', 'export'] as const;

/**
 * Roles: which menus each shows, and what it may do.
 *
 * Two halves, because they answer two questions. The menu is what somebody sees when they
 * sign in; the permissions are what the server will let them read and change. Ticking a
 * menu grants the permission it needs to open, so a role is never given a menu entry that
 * leads to a refusal; the grid below adds what they may do once there - create, edit,
 * cancel, print.
 */
export function RolesPage(): React.JSX.Element {
  const { t } = useTranslation();

  const roles = useQuery<readonly RoleSummary[], ApiError>({
    queryKey: ['platform-roles'],
    queryFn: listRoles,
  });

  const permissions = useQuery<readonly PermissionDefinition[], ApiError>({
    queryKey: ['platform-permissions'],
    queryFn: listPermissions,
  });

  const menu = useQuery<MenuAdmin, ApiError>({
    queryKey: ['menu-admin'],
    queryFn: fetchMenuAdmin,
  });

  const [selected, setSelected] = useState<string | 'new' | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const current =
    selected === 'new'
      ? null
      : (roles.data?.find((role) => role.roleId === selected) ?? null);

  const failure = roles.error ?? permissions.error ?? menu.error;

  return (
    <section className="page">
      <PageHeading
        title={t('roles.title')}
        actions={
          <button
            type="button"
            onClick={() => setSelected('new')}
            className="btn-primary btn-sm"
          >
            {t('roles.new')}
          </button>
        }
      />

      <p className="text-sm text-ink-muted">{t('roles.hint')}</p>

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

      {failure && <p className="alert-error">{failure.detail || failure.code}</p>}
      {(roles.isPending || permissions.isPending || menu.isPending) && <Spinner />}

      {roles.data && permissions.data && menu.data && (
        <div className="grid gap-4 lg:grid-cols-[16rem_minmax(0,1fr)]">
          <ul className="panel h-fit divide-y divide-line p-0">
            {roles.data.map((role) => (
              <li key={role.roleId}>
                <button
                  type="button"
                  onClick={() => setSelected(role.roleId)}
                  className={clsx(
                    'w-full px-3 py-2 text-start text-sm hover:bg-surface-2',
                    selected === role.roleId &&
                      'bg-brand-50 font-semibold dark:bg-brand-500/10',
                  )}
                >
                  <span className="block">{role.name}</span>
                  <span className="block text-xs text-ink-muted">
                    {t('roles.userCount', { count: role.userCount })}
                    {role.grantsAllPermissions && ` · ${t('roles.everything')}`}
                  </span>
                </button>
              </li>
            ))}
          </ul>

          {selected === null ? (
            <p className="panel text-sm text-ink-muted">{t('roles.choose')}</p>
          ) : (
            <RoleEditor
              key={selected}
              role={current}
              permissions={permissions.data}
              menu={menu.data.items}
              onSaved={(message, roleId) => {
                setNotice(message);
                setSelected(roleId);
              }}
              onDeleted={(message) => {
                setNotice(message);
                setSelected(null);
              }}
            />
          )}
        </div>
      )}
    </section>
  );
}

function RoleEditor({
  role,
  permissions,
  menu,
  onSaved,
  onDeleted,
}: {
  readonly role: RoleSummary | null;
  readonly permissions: readonly PermissionDefinition[];
  readonly menu: readonly MenuAdminEntry[];
  readonly onSaved: (message: string, roleId: string) => void;
  readonly onDeleted: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  const [name, setName] = useState(role?.name ?? '');
  const [description, setDescription] = useState(role?.description ?? '');
  const [granted, setGranted] = useState<ReadonlySet<string>>(
    () => new Set(role?.permissionCodes ?? []),
  );
  const [restricts, setRestricts] = useState(role ? role.restrictsMenu : true);
  const [menus, setMenus] = useState<ReadonlySet<string>>(
    () => new Set(role?.menuCodes ?? []),
  );

  const everything = role?.grantsAllPermissions ?? false;
  const known = useMemo(() => new Set(permissions.map((p) => p.code)), [permissions]);

  // Resources by module, each with the verbs the catalogue offers for it.
  const grid = useMemo(() => {
    const modules = new Map<
      string,
      Map<string, { description: string; verbs: Set<string> }>
    >();

    for (const permission of permissions) {
      const resources = modules.get(permission.module) ?? new Map();
      const resource = resources.get(permission.resource) ?? {
        description: permission.description,
        verbs: new Set<string>(),
      };

      resource.verbs.add(permission.verb);
      resources.set(permission.resource, resource);
      modules.set(permission.module, resources);
    }

    return modules;
  }, [permissions]);

  const toggle = (code: string, on: boolean): void =>
    setGranted((current) => {
      const next = new Set(current);
      if (on) next.add(code);
      else next.delete(code);
      return next;
    });

  // A menu ticked brings the permission it needs to open with it.
  const toggleMenu = (entry: MenuAdminEntry, on: boolean): void => {
    const codes = collect(entry);

    setMenus((current) => {
      const next = new Set(current);
      for (const item of codes) {
        if (on) next.add(item.code);
        else next.delete(item.code);
      }
      return next;
    });

    if (on) {
      setGranted((current) => {
        const next = new Set(current);
        for (const item of codes) {
          const needed = item.requiredPermission?.toLowerCase();
          if (needed && known.has(needed)) next.add(needed);
        }
        return next;
      });
    }
  };

  const save = useMutation<RoleSummary, ApiError>({
    mutationFn: () => {
      const body = {
        name: name.trim(),
        description: description.trim() || null,
        permissionCodes: [...granted],
        menuCodes: restricts ? [...menus] : null,
      };

      return role ? updateRole(role.roleId, body) : createRole(body);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ['platform-roles'] });
      void queryClient.invalidateQueries({ queryKey: ['menu'] });
      onSaved(t('roles.savedNotice', { name: saved.name }), saved.roleId);
    },
  });

  const remove = useMutation<void, ApiError>({
    mutationFn: () => deleteRole(role?.roleId ?? ''),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['platform-roles'] });
      onDeleted(t('roles.deletedNotice', { name: role?.name ?? '' }));
    },
  });

  return (
    <div className="space-y-4">
      <div className="panel grid gap-3 sm:grid-cols-2">
        <TextField
          label={t('roles.name')}
          value={name}
          onChange={setName}
          required
          maxLength={100}
        />
        <TextField
          label={t('roles.description')}
          value={description}
          onChange={setDescription}
          maxLength={256}
        />
        {everything && (
          <p className="alert-warn text-xs sm:col-span-2">{t('roles.everythingHint')}</p>
        )}
      </div>

      <div className="panel space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h3 className="text-sm font-semibold text-ink">{t('roles.menus')}</h3>
          <label className="field-check text-sm">
            <input
              type="checkbox"
              checked={!restricts}
              onChange={(event) => setRestricts(!event.target.checked)}
            />
            <span>{t('roles.allMenus')}</span>
          </label>
        </div>
        <p className="text-xs text-ink-muted">{t('roles.menusHint')}</p>

        <div
          className={clsx(
            'grid gap-3 md:grid-cols-2 xl:grid-cols-3',
            !restricts && 'opacity-50',
          )}
        >
          {menu.map((entry) => (
            <MenuBranch
              key={entry.id}
              entry={entry}
              chosen={menus}
              disabled={!restricts}
              onToggle={toggleMenu}
            />
          ))}
        </div>
      </div>

      {!everything && (
        <div className="panel space-y-3">
          <h3 className="text-sm font-semibold text-ink">{t('roles.permissions')}</h3>
          <p className="text-xs text-ink-muted">{t('roles.permissionsHint')}</p>

          <div className="table-wrap">
            <table className="table min-w-[44rem] text-sm">
              <thead>
                <tr>
                  <th className="text-start">{t('roles.resource')}</th>
                  {VERBS.map((verb) => (
                    <th key={verb} className="text-center">
                      {t(`roles.verbs.${verb}`)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {[...grid.entries()].map(([module, resources]) => (
                  <ModuleRows
                    key={module}
                    module={module}
                    resources={resources}
                    granted={granted}
                    onToggle={toggle}
                  />
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {(save.error ?? remove.error) && (
        <p className="alert-error">
          {(save.error ?? remove.error)?.detail || (save.error ?? remove.error)?.code}
        </p>
      )}

      <div className="flex flex-wrap justify-end gap-2">
        {role && !role.isSystemRole && (
          <button
            type="button"
            disabled={remove.isPending}
            onClick={() => remove.mutate()}
            className="btn-secondary btn-sm text-red-600"
          >
            {t('roles.delete')}
          </button>
        )}
        <button
          type="button"
          disabled={save.isPending || name.trim() === ''}
          onClick={() => save.mutate()}
          className="btn-primary btn-sm"
        >
          {save.isPending ? t('common.saving') : t('common.save')}
        </button>
      </div>
    </div>
  );
}

/** An entry and everything beneath it. */
function collect(entry: MenuAdminEntry): readonly MenuAdminEntry[] {
  return [entry, ...entry.children.flatMap(collect)];
}

function MenuBranch({
  entry,
  chosen,
  disabled,
  onToggle,
}: {
  readonly entry: MenuAdminEntry;
  readonly chosen: ReadonlySet<string>;
  readonly disabled: boolean;
  readonly onToggle: (entry: MenuAdminEntry, on: boolean) => void;
}): React.JSX.Element {
  const leaves = collect(entry).filter((item) => item.route);
  const all = leaves.length > 0 && leaves.every((item) => chosen.has(item.code));
  const some = leaves.some((item) => chosen.has(item.code));

  return (
    <div className="rounded-lg border border-line p-2">
      <label className="field-check font-semibold">
        <input
          type="checkbox"
          checked={all}
          ref={(input) => {
            if (input) input.indeterminate = some && !all;
          }}
          disabled={disabled}
          onChange={(event) => onToggle(entry, event.target.checked)}
        />
        <span>{entry.label}</span>
      </label>
      {entry.children.length > 0 && (
        <ul className="ms-5 mt-1 space-y-0.5">
          {entry.children.map((child) => (
            <li key={child.id}>
              <label className="field-check text-sm">
                <input
                  type="checkbox"
                  checked={collect(child)
                    .filter((item) => item.route)
                    .every((item) => chosen.has(item.code))}
                  disabled={disabled}
                  onChange={(event) => onToggle(child, event.target.checked)}
                />
                <span>{child.label}</span>
              </label>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function ModuleRows({
  module,
  resources,
  granted,
  onToggle,
}: {
  readonly module: string;
  readonly resources: Map<string, { description: string; verbs: Set<string> }>;
  readonly granted: ReadonlySet<string>;
  readonly onToggle: (code: string, on: boolean) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <>
      <tr className="bg-surface-2/60">
        <td
          colSpan={VERBS.length + 1}
          className="text-xs font-bold tracking-wider uppercase"
        >
          {t(`roles.modules.${module}`, { defaultValue: module })}
        </td>
      </tr>
      {[...resources.entries()].map(([resource, { description, verbs }]) => (
        <tr key={resource}>
          <td>
            <span className="block">{description}</span>
            <span className="font-mono text-[11px] text-ink-muted">{resource}</span>
          </td>
          {VERBS.map((verb) => {
            const code = `${module}:${resource}:${verb}`;

            return (
              <td key={verb} className="text-center">
                {verbs.has(verb) && (
                  <input
                    type="checkbox"
                    checked={granted.has(code)}
                    onChange={(event) => onToggle(code, event.target.checked)}
                    aria-label={`${description} — ${verb}`}
                  />
                )}
              </td>
            );
          })}
        </tr>
      ))}
    </>
  );
}
