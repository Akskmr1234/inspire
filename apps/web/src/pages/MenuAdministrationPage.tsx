import { Fragment, useMemo, useState } from 'react';
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
import { IconChevron, IconSearch } from '@/components/icons';

function IconGrip({ className }: { readonly className?: string }): React.JSX.Element {
  return (
    <svg className={className} viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
      <circle cx="5" cy="3" r="1.5" />
      <circle cx="11" cy="3" r="1.5" />
      <circle cx="5" cy="8" r="1.5" />
      <circle cx="11" cy="8" r="1.5" />
      <circle cx="5" cy="13" r="1.5" />
      <circle cx="11" cy="13" r="1.5" />
    </svg>
  );
}

interface DragTargetInfo {
  readonly id: string;
  readonly position: 'before' | 'after' | 'inside';
}

/**
 * Editing the navigation menu.
 *
 * Allows administrators to show, hide, reorder (via intuitive drag-and-drop or arrows),
 * regroup, and extend the navigation menu seamlessly.
 */
export function MenuAdministrationPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [collapsedIds, setCollapsedIds] = useState<Set<string>>(new Set());

  // Drag-and-drop state
  const [draggedItem, setDraggedItem] = useState<{
    id: string;
    parentId: string | null;
    sortOrder: number;
    label: string;
  } | null>(null);
  const [dragTarget, setDragTarget] = useState<DragTargetInfo | null>(null);

  const query = useQuery<MenuAdmin, ApiError>({
    queryKey: ['admin-menu'],
    queryFn: fetchMenuAdmin,
  });

  const refresh = async (): Promise<void> => {
    setError(null);
    await queryClient.invalidateQueries({ queryKey: ['admin-menu'] });
    await queryClient.invalidateQueries({ queryKey: ['menu'] });
  };

  const mutation = useMutation<void, ApiError, () => Promise<void>>({
    mutationFn: (action) => action(),
    onSuccess: refresh,
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const run = (action: () => Promise<void>): void => {
    setError(null);
    mutation.mutate(action);
  };

  const toggleCollapse = (id: string): void => {
    setCollapsedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const handleDropOnItem = (targetEntry: MenuAdminEntry, targetParentId: string | null): void => {
    if (!draggedItem || !dragTarget || draggedItem.id === targetEntry.id) {
      setDraggedItem(null);
      setDragTarget(null);
      return;
    }

    const { position } = dragTarget;
    const sourceId = draggedItem.id;

    run(async () => {
      if (position === 'inside') {
        const newOrder = (targetEntry.children.length + 1) * 10;
        await moveMenuItem(sourceId, targetEntry.id, newOrder);
      } else if (position === 'before') {
        await moveMenuItem(sourceId, targetParentId, Math.max(1, targetEntry.sortOrder - 1));
      } else {
        await moveMenuItem(sourceId, targetParentId, targetEntry.sortOrder + 1);
      }
    });

    setDraggedItem(null);
    setDragTarget(null);
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

          {/* User Guide & Search Bar */}
          <div className="flex flex-col gap-3 rounded-xl border border-line bg-surface-2/60 p-3 sm:flex-row sm:items-center sm:justify-between">
            <div className="flex items-center gap-2 text-xs text-ink-muted">
              <span className="flex size-6 items-center justify-center rounded-md bg-accent/15 text-accent font-bold">
                ⠿
              </span>
              <span>
                <strong>Drag & Drop to reorder:</strong> Grab any menu row using the{' '}
                <strong className="text-ink">⠿ grip icon</strong> to move it up, down, or into a heading.
              </span>
            </div>

            <div className="relative min-w-56">
              <IconSearch className="pointer-events-none absolute start-2.5 top-2.5 size-3.5 text-ink-muted" />
              <input
                type="search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search menu items…"
                className="field-input-sm ps-8 w-full"
              />
            </div>
          </div>

          <div className="table-wrap rounded-xl border border-line shadow-sm overflow-hidden">
            <table className="table">
              <thead className="bg-surface-3">
                <tr>
                  <th className="w-10 px-2 py-2.5 text-center font-semibold text-xs text-ink-muted">
                    #
                  </th>
                  <th className="px-3 py-2.5 text-start font-semibold text-xs text-ink">
                    {t('menuAdmin.entry')}
                  </th>
                  <th className="px-3 py-2.5 text-start font-semibold text-xs text-ink">
                    {t('menuAdmin.route')}
                  </th>
                  <th className="px-3 py-2.5 text-start font-semibold text-xs text-ink">
                    {t('menuAdmin.permission')}
                  </th>
                  <th className="px-3 py-2.5 text-end font-semibold text-xs text-ink">
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
                    search={search.trim().toLowerCase()}
                    collapsedIds={collapsedIds}
                    toggleCollapse={toggleCollapse}
                    draggedItem={draggedItem}
                    dragTarget={dragTarget}
                    setDraggedItem={setDraggedItem}
                    setDragTarget={setDragTarget}
                    onDropOnItem={handleDropOnItem}
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
 * One entry's row, and the rows of everything beneath it with drag-and-drop capabilities.
 */
function EntryRows({
  entry,
  siblings,
  index,
  parentId,
  depth,
  busy,
  search,
  collapsedIds,
  toggleCollapse,
  draggedItem,
  dragTarget,
  setDraggedItem,
  setDragTarget,
  onDropOnItem,
  run,
}: {
  readonly entry: MenuAdminEntry;
  readonly siblings: readonly MenuAdminEntry[];
  readonly index: number;
  readonly parentId: string | null;
  readonly depth: number;
  readonly busy: boolean;
  readonly search: string;
  readonly collapsedIds: ReadonlySet<string>;
  readonly toggleCollapse: (id: string) => void;
  readonly draggedItem: { id: string; parentId: string | null; sortOrder: number; label: string } | null;
  readonly dragTarget: DragTargetInfo | null;
  readonly setDraggedItem: (item: { id: string; parentId: string | null; sortOrder: number; label: string } | null) => void;
  readonly setDragTarget: (target: DragTargetInfo | null) => void;
  readonly onDropOnItem: (entry: MenuAdminEntry, parentId: string | null) => void;
  readonly run: (action: () => Promise<void>) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const isHeading = entry.route === null;
  const hasChildren = entry.children.length > 0;
  const isCollapsed = collapsedIds.has(entry.id);
  const isDraggingThis = draggedItem?.id === entry.id;
  const isTargetOfDrop = dragTarget?.id === entry.id;

  const matchesSearch = useMemo(() => {
    if (!search) return true;
    const matchSelf =
      entry.label.toLowerCase().includes(search) ||
      entry.code.toLowerCase().includes(search) ||
      (entry.route && entry.route.toLowerCase().includes(search));
    const matchChildren = entry.children.some(
      (c) =>
        c.label.toLowerCase().includes(search) ||
        c.code.toLowerCase().includes(search) ||
        (c.route && c.route.toLowerCase().includes(search)),
    );
    return matchSelf || matchChildren;
  }, [entry, search]);

  if (!matchesSearch) {
    return <Fragment />;
  }

  const swapWith = (other: MenuAdminEntry): void =>
    run(async () => {
      await moveMenuItem(entry.id, parentId, other.sortOrder);
      await moveMenuItem(other.id, parentId, entry.sortOrder);
    });

  const handleDragStart = (e: React.DragEvent): void => {
    e.dataTransfer.setData('text/plain', entry.id);
    e.dataTransfer.effectAllowed = 'move';
    setDraggedItem({
      id: entry.id,
      parentId,
      sortOrder: entry.sortOrder,
      label: entry.label,
    });
  };

  const handleDragOver = (e: React.DragEvent): void => {
    e.preventDefault();
    e.stopPropagation();
    e.dataTransfer.dropEffect = 'move';

    if (!draggedItem || draggedItem.id === entry.id) {
      return;
    }

    const rect = e.currentTarget.getBoundingClientRect();
    const relY = (e.clientY - rect.top) / rect.height;

    let position: 'before' | 'after' | 'inside';
    if (isHeading) {
      if (relY < 0.25) position = 'before';
      else if (relY > 0.75) position = 'after';
      else position = 'inside';
    } else {
      position = relY < 0.5 ? 'before' : 'after';
    }

    if (!dragTarget || dragTarget.id !== entry.id || dragTarget.position !== position) {
      setDragTarget({ id: entry.id, position });
    }
  };

  const handleDragLeave = (e: React.DragEvent): void => {
    e.preventDefault();
    if (dragTarget?.id === entry.id) {
      setDragTarget(null);
    }
  };

  const handleDrop = (e: React.DragEvent): void => {
    e.preventDefault();
    e.stopPropagation();
    onDropOnItem(entry, parentId);
  };

  const dropClass = isTargetOfDrop
    ? dragTarget?.position === 'before'
      ? 'border-t-2 border-t-accent bg-accent/5'
      : dragTarget?.position === 'after'
        ? 'border-b-2 border-b-accent bg-accent/5'
        : 'ring-2 ring-accent ring-inset bg-accent/10'
    : '';

  return (
    <Fragment>
      <tr
        draggable={!busy}
        onDragStart={handleDragStart}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        className={clsx(
          'border-t border-line transition-all duration-150',
          !entry.isEnabled && 'opacity-50',
          isDraggingThis && 'opacity-30 bg-surface-2 border-dashed border-accent',
          dropClass,
        )}
      >
        {/* Drag Handle */}
        <td className="w-10 px-2 py-2 text-center">
          <div
            title="Drag to reorder"
            className="flex items-center justify-center cursor-grab active:cursor-grabbing text-ink-muted hover:text-accent p-1 rounded transition"
          >
            <IconGrip className="size-4" />
          </div>
        </td>

        {/* Menu Label & Hierarchy */}
        <td
          className="px-3 py-2"
          style={{ paddingInlineStart: `${0.75 + depth * 1.5}rem` }}
        >
          <div className="flex items-center gap-2">
            {hasChildren && (
              <button
                type="button"
                onClick={() => toggleCollapse(entry.id)}
                className="p-0.5 text-ink-muted hover:text-ink transition"
                title={isCollapsed ? 'Expand' : 'Collapse'}
              >
                <IconChevron
                  className={clsx(
                    'size-3.5 transition-transform duration-150',
                    !isCollapsed && 'rotate-90',
                  )}
                />
              </button>
            )}

            <span className="flex flex-wrap items-baseline gap-2">
              <span
                className={clsx(
                  'font-medium',
                  isHeading ? 'text-ink font-semibold' : 'text-ink',
                  !entry.isEnabled && 'line-through text-ink-muted',
                )}
              >
                {entry.label}
              </span>
              <span className="font-mono text-[11px] text-ink-subtle">{entry.code}</span>
              {entry.isSystem && (
                <span className="rounded bg-surface-3 px-1.5 py-0.5 text-[10px] font-semibold text-ink-muted">
                  {t('menuAdmin.system')}
                </span>
              )}
            </span>
          </div>
        </td>

        {/* Route / Heading Badge */}
        <td className="px-3 py-2 text-ink-muted">
          {entry.route ? (
            <span className="font-mono text-xs rounded bg-surface-2 px-1.5 py-0.5 text-ink">
              {entry.route}
            </span>
          ) : (
            <span className="rounded-full bg-accent/10 px-2 py-0.5 text-[11px] font-medium text-accent">
              {t('menuAdmin.heading')}
            </span>
          )}
        </td>

        {/* Required Permission */}
        <td className="px-3 py-2 text-xs text-ink-muted">
          {entry.requiredPermission ? (
            <span className="font-mono text-[11px]">{entry.requiredPermission}</span>
          ) : (
            <span className="text-ink-subtle italic">{t('menuAdmin.everyone')}</span>
          )}
        </td>

        {/* Actions */}
        <td className="px-3 py-2 text-end">
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

      {!isCollapsed &&
        entry.children.map((child, childIndex) => (
          <EntryRows
            key={child.id}
            entry={child}
            siblings={entry.children}
            index={childIndex}
            parentId={entry.id}
            depth={depth + 1}
            busy={busy}
            search={search}
            collapsedIds={collapsedIds}
            toggleCollapse={toggleCollapse}
            draggedItem={draggedItem}
            dragTarget={dragTarget}
            setDraggedItem={setDraggedItem}
            setDragTarget={setDragTarget}
            onDropOnItem={onDropOnItem}
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

      <button type="submit" disabled={busy} className="btn-primary btn-sm">
        {t('menuAdmin.add')}
      </button>
    </form>
  );
}
