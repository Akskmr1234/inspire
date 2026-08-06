import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { useSession } from '@/stores/session';
import { fetchGridLayout, resetGridLayout, saveGridLayout } from '@/lib/grid';

/**
 * One column of a grid.
 *
 * `value` returns what the cell holds; `render` decides how it looks. They are
 * separate because sorting, searching and export all need the value and none of them
 * can do anything useful with a React element - a column rendering a coloured badge
 * still has to sort and export as the word behind it.
 */
export interface GridColumn<TRow> {
  readonly key: string;
  readonly header: string;
  /** The underlying value, used for sorting, searching, and export. */
  readonly value: (row: TRow) => string | number | null;
  /** How the cell is drawn. Defaults to the value. */
  readonly render?: (row: TRow) => React.ReactNode;
  /** Right-aligns and uses tabular figures, for money and quantities. */
  readonly numeric?: boolean;
  /** Hidden until a user turns it on. */
  readonly hiddenByDefault?: boolean;
  /**
   * A `module:resource:verb` code the user must hold to see this column at all.
   *
   * The specification's role-based column visibility. Unlike hiding a column, this is
   * not a preference the user can override — though it is still a courtesy rather
   * than a security boundary, since the row was already sent to the browser. Genuinely
   * sensitive fields must be left out of the response, not merely out of the table.
   */
  readonly requiredPermission?: string;
}

/**
 * The data grid.
 *
 * Sorting, searching and column arrangement all happen in the browser. That is the
 * right call for the lists this is used on — a chart of accounts is a few hundred rows
 * — and it makes typing in the search box instant rather than a round trip per
 * keystroke. A list that genuinely outgrows the browser needs server-side paging, and
 * this component is deliberately not pretending to provide it.
 */
export function DataGrid<TRow>({
  gridKey,
  rows,
  columns,
  rowKey,
  emptyMessage,
}: {
  /** Identifies the grid, so a user's arrangement is remembered against it. */
  readonly gridKey: string;
  readonly rows: readonly TRow[];
  readonly columns: readonly GridColumn<TRow>[];
  readonly rowKey: (row: TRow) => string;
  readonly emptyMessage?: string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const { can } = useSession();

  const [search, setSearch] = useState('');
  const [columnSearch, setColumnSearch] = useState<Record<string, string>>({});
  const [sortKey, setSortKey] = useState<string | null>(null);
  const [sortDescending, setSortDescending] = useState(false);
  const [hidden, setHidden] = useState<ReadonlySet<string>>(new Set());
  const [order, setOrder] = useState<readonly string[]>([]);
  const [frozen, setFrozen] = useState(0);
  const [showPicker, setShowPicker] = useState(false);
  const [saved, setSaved] = useState<string | null>(null);

  // Columns the user is not entitled to never enter the arrangement at all, so they
  // cannot be turned on from the picker or restored by a stale saved layout.
  const permitted = useMemo(
    () => columns.filter((column) => !column.requiredPermission || can(column.requiredPermission)),
    [columns, can],
  );

  // Applied once on mount. A layout saved when the grid had different columns is
  // reconciled rather than trusted: unknown keys are dropped and new columns appear
  // at the end, so a release that adds a column does not leave it invisible to
  // everybody who had already arranged the grid.
  useEffect(() => {
    let cancelled = false;

    void (async () => {
      const state = await fetchGridLayout(gridKey).catch(() => null);

      if (cancelled || !state) {
        return;
      }

      const known = new Set(permitted.map((column) => column.key));

      if (state.order) {
        setOrder(state.order.filter((key) => known.has(key)));
      }

      if (state.hidden) {
        setHidden(new Set(state.hidden.filter((key) => known.has(key))));
      }

      if (state.sortKey && known.has(state.sortKey)) {
        setSortKey(state.sortKey);
        setSortDescending(state.sortDescending ?? false);
      }

      if (typeof state.frozen === 'number') {
        setFrozen(state.frozen);
      }
    })();

    return () => {
      cancelled = true;
    };
    // Deliberately mount-only: re-reading the saved layout whenever the columns array
    // is rebuilt would overwrite whatever the user has just done to the grid.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [gridKey]);

  const visible = useMemo(() => {
    const byKey = new Map(permitted.map((column) => [column.key, column]));
    const ordered: GridColumn<TRow>[] = [];

    for (const key of order) {
      const column = byKey.get(key);

      if (column) {
        ordered.push(column);
        byKey.delete(key);
      }
    }

    // Anything the saved order did not mention keeps its declared position, which is
    // what makes a newly added column show up rather than disappear.
    ordered.push(...permitted.filter((column) => byKey.has(column.key)));

    return ordered.filter(
      (column) => !hidden.has(column.key) && !(column.hiddenByDefault && !order.includes(column.key)),
    );
  }, [permitted, order, hidden]);

  const shown = useMemo(() => {
    const needle = search.trim().toLowerCase();

    const matches = rows.filter((row) => {
      // The global search reads the visible columns only. Searching hidden ones would
      // return rows with nothing on screen explaining why they matched.
      const matchesGlobal =
        !needle ||
        visible.some((column) =>
          String(column.value(row) ?? '')
            .toLowerCase()
            .includes(needle),
        );

      if (!matchesGlobal) {
        return false;
      }

      return Object.entries(columnSearch).every(([key, term]) => {
        if (!term.trim()) {
          return true;
        }

        const column = visible.find((candidate) => candidate.key === key);

        return (
          !column ||
          String(column.value(row) ?? '')
            .toLowerCase()
            .includes(term.trim().toLowerCase())
        );
      });
    });

    const column = visible.find((candidate) => candidate.key === sortKey);

    if (!column) {
      return matches;
    }

    // Copied before sorting: the rows belong to the caller, and sorting in place
    // would reorder their state behind their back.
    return [...matches].sort((left, right) => {
      const a = column.value(left);
      const b = column.value(right);

      // Blanks sort last whichever way the column is pointed, so turning a sort
      // around never fills the top of the screen with empty cells.
      if (a === null || a === '') {
        return b === null || b === '' ? 0 : 1;
      }

      if (b === null || b === '') {
        return -1;
      }

      const comparison =
        typeof a === 'number' && typeof b === 'number'
          ? a - b
          : String(a).localeCompare(String(b), undefined, { numeric: true });

      return sortDescending ? -comparison : comparison;
    });
  }, [rows, visible, search, columnSearch, sortKey, sortDescending]);

  const toggleSort = (key: string): void => {
    if (sortKey === key) {
      setSortDescending((previous) => !previous);
    } else {
      setSortKey(key);
      setSortDescending(false);
    }
  };

  const toggleHidden = (key: string): void =>
    setHidden((previous) => {
      const next = new Set(previous);

      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }

      return next;
    });

  const move = (key: string, delta: number): void => {
    const current = visible.map((column) => column.key);
    const index = current.indexOf(key);
    const target = index + delta;

    if (index < 0 || target < 0 || target >= current.length) {
      return;
    }

    const next = [...current];
    [next[index], next[target]] = [next[target]!, next[index]!];
    setOrder(next);
  };

  const persist = async (): Promise<void> => {
    await saveGridLayout(gridKey, {
      order: visible.map((column) => column.key),
      hidden: [...hidden],
      sortKey,
      sortDescending,
      frozen,
    });

    setSaved(t('grid.layoutSaved'));
    window.setTimeout(() => setSaved(null), 2000);
  };

  const restoreDefaults = async (): Promise<void> => {
    await resetGridLayout(gridKey);
    setOrder([]);
    setHidden(new Set());
    setSortKey(null);
    setSortDescending(false);
    setFrozen(0);
    setColumnSearch({});
    setSaved(t('grid.layoutReset'));
    window.setTimeout(() => setSaved(null), 2000);
  };

  const exportCsv = (): void => {
    const escape = (raw: string): string =>
      // Quote anything holding a delimiter, a quote, or a newline, doubling internal
      // quotes. A ledger called "Smith, J" otherwise becomes two columns.
      /[",\n\r]/.test(raw) ? `"${raw.replaceAll('"', '""')}"` : raw;

    const lines = [
      visible.map((column) => escape(column.header)).join(','),
      ...shown.map((row) =>
        visible.map((column) => escape(String(column.value(row) ?? ''))).join(','),
      ),
    ];

    // A BOM, so Excel opens the file as UTF-8 rather than guessing at the code page
    // and turning every Arabic name into punctuation.
    const blob = new Blob([`﻿${lines.join('\r\n')}`], {
      type: 'text/csv;charset=utf-8;',
    });

    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `${gridKey}.csv`;
    anchor.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <input
          type="search"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder={t('grid.search')}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-900"
        />

        <span className="text-xs text-slate-500">
          {t('grid.rowCount', { shown: shown.length, total: rows.length })}
        </span>

        <div className="ms-auto flex flex-wrap items-center gap-2">
          {saved && <span className="text-xs text-emerald-600">{saved}</span>}

          <GridButton onClick={() => setShowPicker((value) => !value)}>
            {t('grid.columns')}
          </GridButton>
          <GridButton onClick={() => setFrozen((value) => (value === 0 ? 1 : 0))}>
            {frozen > 0 ? t('grid.unfreeze') : t('grid.freeze')}
          </GridButton>
          <GridButton onClick={exportCsv}>{t('grid.exportCsv')}</GridButton>
          <GridButton onClick={() => void persist()}>{t('grid.saveLayout')}</GridButton>
          <GridButton onClick={() => void restoreDefaults()}>
            {t('grid.resetLayout')}
          </GridButton>
        </div>
      </div>

      {showPicker && (
        <div className="flex flex-wrap gap-2 rounded-lg border border-slate-200 p-3 dark:border-slate-800">
          {permitted.map((column) => (
            <div
              key={column.key}
              className="flex items-center gap-1 rounded border border-slate-200 px-2 py-1 text-xs dark:border-slate-700"
            >
              <label className="flex items-center gap-1">
                <input
                  type="checkbox"
                  checked={visible.some((candidate) => candidate.key === column.key)}
                  onChange={() => toggleHidden(column.key)}
                />
                {column.header}
              </label>
              <button
                type="button"
                onClick={() => move(column.key, -1)}
                className="px-1 text-slate-400 hover:text-slate-700"
                title={t('grid.moveLeft')}
              >
                ‹
              </button>
              <button
                type="button"
                onClick={() => move(column.key, 1)}
                className="px-1 text-slate-400 hover:text-slate-700"
                title={t('grid.moveRight')}
              >
                ›
              </button>
            </div>
          ))}
        </div>
      )}

      <div className="max-h-[70vh] overflow-auto rounded-xl border border-slate-200 dark:border-slate-800">
        <table className="w-full border-collapse text-sm">
          <thead className="sticky top-0 z-10 bg-slate-100 dark:bg-slate-800">
            <tr>
              {visible.map((column, index) => (
                <th
                  key={column.key}
                  onClick={() => toggleSort(column.key)}
                  className={clsx(
                    'cursor-pointer px-3 py-2 font-semibold select-none',
                    column.numeric ? 'text-end' : 'text-start',
                    // A frozen column sticks to the logical start edge, so Arabic
                    // freezes from the right without a second rule.
                    index < frozen && 'sticky start-0 z-20 bg-slate-100 dark:bg-slate-800',
                  )}
                >
                  {column.header}
                  {sortKey === column.key && (
                    <span className="ms-1 text-slate-400">{sortDescending ? '▾' : '▴'}</span>
                  )}
                </th>
              ))}
            </tr>
            <tr>
              {visible.map((column) => (
                <th key={column.key} className="px-2 pb-2">
                  <input
                    type="search"
                    value={columnSearch[column.key] ?? ''}
                    onChange={(event) =>
                      setColumnSearch((previous) => ({
                        ...previous,
                        [column.key]: event.target.value,
                      }))
                    }
                    className="w-full rounded border border-slate-300 bg-white px-1 py-0.5 text-xs font-normal dark:border-slate-700 dark:bg-slate-900"
                  />
                </th>
              ))}
            </tr>
          </thead>

          <tbody>
            {shown.length === 0 ? (
              <tr>
                <td
                  colSpan={visible.length}
                  className="px-3 py-6 text-center text-sm text-slate-500"
                >
                  {emptyMessage ?? t('grid.noRows')}
                </td>
              </tr>
            ) : (
              shown.map((row) => (
                <tr
                  key={rowKey(row)}
                  className="border-t border-slate-100 hover:bg-slate-50 dark:border-slate-900 dark:hover:bg-slate-800/50"
                >
                  {visible.map((column, index) => (
                    <td
                      key={column.key}
                      className={clsx(
                        'px-3 py-1.5',
                        column.numeric && 'text-end font-mono whitespace-nowrap',
                        index < frozen &&
                          'sticky start-0 bg-white dark:bg-slate-950',
                      )}
                    >
                      {column.render ? column.render(row) : (column.value(row) ?? '')}
                    </td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function GridButton({
  onClick,
  children,
}: {
  readonly onClick: () => void;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      className="rounded border border-slate-300 px-2 py-1 text-xs text-slate-600 transition hover:bg-slate-100 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800"
    >
      {children}
    </button>
  );
}
