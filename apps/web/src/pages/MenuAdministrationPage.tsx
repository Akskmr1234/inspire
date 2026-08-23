import { Fragment, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { ReportFrame } from '@/components/ReportFrame';
import {
  createMenuItem,
  deleteMenuItem,
  fetchMenuAdmin,
  moveMenuItem,
  setMenuItemVisibility,
  updateMenuItem,
  type MenuAdmin,
  type MenuAdminEntry,
} from '@/lib/menu';
import type { ApiError } from '@/lib/api';

/**
 * Editing the navigation menu.
 *
 * The specification's claim is that an administrator can show, hide, reorder, regroup,
 * and extend the menu with no source-code change. This is the screen where that
 * happens.
 *
 * Every edit invalidates the sidebar's own query as well as this one, so a change is
 * visible in the navigation immediately rather than after a reload. That is not
 * polish: an administrator hiding an entry needs to see it go, or they will hide it
 * twice and then wonder which of the two took effect.
 */
export function MenuAdministrationPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);

  const query = useQuery<MenuAdmin, ApiError>({
    queryKey: ['admin-menu'],
    queryFn: fetchMenuAdmin,
  });

  const refresh = async (): Promise<void> => {
    setError(null);
    await queryClient.invalidateQueries({ queryKey: ['admin-menu'] });
    await queryClient.invalidateQueries({ queryKey: ['menu'] });
  };

  // One mutation per action rather than one per row, so the list can be rebuilt
  // without tearing down and recreating a mutation for every entry on every render.
  const mutation = useMutation<void, ApiError, () => Promise<void>>({
    mutationFn: (action) => action(),
    onSuccess: refresh,
    // The server's rules are the real ones - a system entry refusing deletion, a
    // heading that still holds screens - so its message is what gets shown rather
    // than a guess made here.
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const run = (action: () => Promise<void>): void => {
    setError(null);
    mutation.mutate(action);
  };

  const controls = (
    <AddEntryForm
      busy={mutation.isPending}
      onCreate={(code, label, route) =>
        run(async () => {
          await createMenuItem({
            code,
            label,
            module: 'accounting',
            route: route || null,
          });
        })
      }
    />
  );

  return (
    <ReportFrame title={t('nav.menuAdministration')} controls={controls} query={query}>
      {(data) => (
        <div className="space-y-4">
          {error && (
            <div role="alert" className="alert-error">
              {error}
            </div>
          )}

          <div className="table-wrap">
            <table className="table">
              <thead className="bg-surface-3">
                <tr>
                  <th className="px-3 py-2 text-start font-semibold">
                    {t('menuAdmin.entry')}
                  </th>
                  <th className="px-3 py-2 text-start font-semibold">
                    {t('menuAdmin.route')}
                  </th>
                  <th className="px-3 py-2 text-start font-semibold">
                    {t('menuAdmin.permission')}
                  </th>
                  <th className="px-3 py-2 text-end font-semibold">
                    {t('menuAdmin.actions')}
                  </th>
                </tr>
              </thead>

              <tbody>
                {data.items.map((entry, index) => (
                  <EntryRows
                    key={entry.id}
                    entry={entry}
                    siblings={data.items}
                    index={index}
                    parentId={null}
                    depth={0}
                    busy={mutation.isPending}
                    run={run}
                  />
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </ReportFrame>
  );
}

/**
 * One entry's row, and the rows of everything beneath it.
 *
 * Rendered as sibling rows rather than a nested table so every entry lines up in the
 * same columns however deep it sits - a nested table would indent the columns too,
 * and the route of a third-level entry would no longer be under the route heading.
 */
function EntryRows({
  entry,
  siblings,
  index,
  parentId,
  depth,
  busy,
  run,
}: {
  readonly entry: MenuAdminEntry;
  readonly siblings: readonly MenuAdminEntry[];
  readonly index: number;
  readonly parentId: string | null;
  readonly depth: number;
  readonly busy: boolean;
  readonly run: (action: () => Promise<void>) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  // Reordering swaps sort orders with the neighbour rather than nudging one of them,
  // which keeps the numbers stable however many times a level is rearranged.
  const swapWith = (other: MenuAdminEntry): void =>
    run(async () => {
      await moveMenuItem(entry.id, parentId, other.sortOrder);
      await moveMenuItem(other.id, parentId, entry.sortOrder);
    });

  return (
    <Fragment>
      <tr className={clsx('border-t border-line', !entry.isEnabled && 'opacity-50')}>
        <td
          className="px-3 py-2"
          style={{ paddingInlineStart: `${0.75 + depth * 1.25}rem` }}
        >
          <span className={clsx('font-medium', !entry.isEnabled && 'line-through')}>
            {entry.label}
          </span>
          <span className="ms-2 text-xs text-ink-subtle">{entry.code}</span>
          {entry.isSystem && (
            <span className="ms-2 rounded bg-surface-3 px-1.5 py-0.5 text-xs text-ink-muted">
              {t('menuAdmin.system')}
            </span>
          )}
        </td>

        <td className="px-3 py-2 text-ink-muted">
          {entry.route ?? (
            <span className="text-ink-subtle italic">{t('menuAdmin.heading')}</span>
          )}
        </td>

        <td className="px-3 py-2 text-xs text-ink-muted">
          {entry.requiredPermission ?? (
            <span className="text-ink-subtle italic">{t('menuAdmin.everyone')}</span>
          )}
        </td>

        <td className="px-3 py-2">
          <div className="flex flex-wrap justify-end gap-1">
            <ActionButton
              label="↑"
              title={t('menuAdmin.moveUp')}
              disabled={busy || index === 0}
              onClick={() => swapWith(siblings[index - 1]!)}
            />
            <ActionButton
              label="↓"
              title={t('menuAdmin.moveDown')}
              disabled={busy || index === siblings.length - 1}
              onClick={() => swapWith(siblings[index + 1]!)}
            />
            <ActionButton
              label={entry.isEnabled ? t('menuAdmin.hide') : t('menuAdmin.show')}
              title={entry.isEnabled ? t('menuAdmin.hide') : t('menuAdmin.show')}
              disabled={busy}
              onClick={() =>
                run(async () => {
                  await setMenuItemVisibility(entry.id, !entry.isEnabled);
                })
              }
            />
            <ActionButton
              label={t('menuAdmin.rename')}
              title={t('menuAdmin.rename')}
              disabled={busy}
              onClick={() => {
                const label = window.prompt(t('menuAdmin.renamePrompt'), entry.label);

                if (label && label.trim()) {
                  run(async () => {
                    await updateMenuItem(entry.id, {
                      label: label.trim(),
                      route: entry.route,
                      labelArabic: entry.labelArabic,
                      icon: entry.icon,
                      requiredPermission: entry.requiredPermission,
                    });
                  });
                }
              }}
            />
            <ActionButton
              label={t('menuAdmin.delete')}
              title={
                entry.isSystem ? t('menuAdmin.systemCannotDelete') : t('menuAdmin.delete')
              }
              // Seeded entries are refused by the server too; disabling the control
              // is the courtesy of not offering an action that will be refused.
              disabled={busy || entry.isSystem}
              danger
              onClick={() =>
                run(async () => {
                  await deleteMenuItem(entry.id);
                })
              }
            />
          </div>
        </td>
      </tr>

      {entry.children.map((child, childIndex) => (
        <EntryRows
          key={child.id}
          entry={child}
          siblings={entry.children}
          index={childIndex}
          parentId={entry.id}
          depth={depth + 1}
          busy={busy}
          run={run}
        />
      ))}
    </Fragment>
  );
}

function ActionButton({
  label,
  title,
  disabled,
  danger = false,
  onClick,
}: {
  readonly label: string;
  readonly title: string;
  readonly disabled: boolean;
  readonly danger?: boolean;
  readonly onClick: () => void;
}): React.JSX.Element {
  return (
    <button
      type="button"
      title={title}
      disabled={disabled}
      onClick={onClick}
      className={clsx(
        'rounded-md border px-2 py-1 text-xs font-medium transition duration-150 active:scale-95 disabled:pointer-events-none disabled:opacity-40',
        danger
          ? 'border-red-200 text-red-700 hover:bg-red-50 dark:border-red-500/30 dark:text-red-400 dark:hover:bg-red-500/10'
          : 'border-line text-ink-muted hover:border-line-strong hover:bg-surface-3 hover:text-ink',
      )}
    >
      {label}
    </button>
  );
}

function AddEntryForm({
  busy,
  onCreate,
}: {
  readonly busy: boolean;
  readonly onCreate: (code: string, label: string, route: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState('');
  const [label, setLabel] = useState('');
  const [route, setRoute] = useState('');

  return (
    <form
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();

        if (!code.trim() || !label.trim()) {
          return;
        }

        onCreate(code.trim(), label.trim(), route.trim());
        setCode('');
        setLabel('');
        setRoute('');
      }}
    >
      <label className="field">
        <span className="field-label">{t('menuAdmin.code')}</span>
        <input
          value={code}
          onChange={(event) => setCode(event.target.value)}
          placeholder="custom.my-link"
          className="field-input-sm"
        />
      </label>

      <label className="field">
        <span className="field-label">{t('menuAdmin.label')}</span>
        <input
          value={label}
          onChange={(event) => setLabel(event.target.value)}
          className="field-input-sm"
        />
      </label>

      <label className="field">
        <span className="field-label">{t('menuAdmin.routeOptional')}</span>
        <input
          value={route}
          onChange={(event) => setRoute(event.target.value)}
          placeholder="/accounting/day-book"
          className="field-input-sm"
        />
      </label>

      <button type="submit" disabled={busy} className="btn-primary">
        {t('menuAdmin.add')}
      </button>
    </form>
  );
}
