import { Fragment, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { IconPlus } from '@/components/icons';
import { useSession } from '@/stores/session';
import { useSettings } from '@/stores/settings';
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
   * Given whatever width the other columns leave.
   *
   * For the one column carrying prose — a product description, a narration. Without
   * it every column shares the table's width equally, so a forty-character
   * description is wrapped to three lines beside a column holding "EA".
   */
  readonly wide?: boolean;
  /**
   * Dropped where the grid draws cards rather than a table.
   *
   * For an action the card already offers another way: every list here leads its
   * card with the identifying column, which is the link into the record, so a
   * button captioned "Open" beneath it is the same action twice and a row of
   * height on the screen that can least afford one.
   */
  readonly hideOnNarrow?: boolean;
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

/** Where a server-paged grid stands, and how to ask it for another page. */
export interface GridPaging {
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly onPageChange: (page: number) => void;
}

/**
 * The data grid.
 *
 * Sorting, searching and column arrangement happen in the browser, which is the right
 * call for the lists this began with — a chart of accounts is a few hundred rows — and
 * makes typing in the search box instant rather than a round trip per keystroke.
 *
 * A list that outgrows the browser passes `paging`, and the grid then draws a pager and
 * says plainly that it is showing one page of a longer list. Sorting and the search box
 * are withdrawn in that mode rather than left working on the page in hand: a search that
 * quietly looked at fifty rows out of four thousand would answer "no such invoice" about
 * an invoice that exists, which is worse than not offering the box. The screen owns the
 * filters instead, because only the server can apply them to the whole list.
 *
 * Everything else — column picking, ordering, freezing, saved layouts, CSV export of
 * what is on screen — works the same either way.
 */
export function DataGrid<TRow>({
  gridKey,
  rows,
  columns,
  rowKey,
  emptyMessage,
  filters,
  actions,
  paging,
  pageSize,
  hideSearch = false,
}: {
  /** Identifies the grid, so a user's arrangement is remembered against it. */
  readonly gridKey: string;
  readonly rows: readonly TRow[];
  readonly columns: readonly GridColumn<TRow>[];
  readonly rowKey: (row: TRow) => string;
  readonly emptyMessage?: string;
  /**
   * A screen's own filters, for the ones with too few to earn a filter bar.
   *
   * A master has one — include withdrawn — and a bar of its own for it costs the
   * list a strip of the screen to hold a single checkbox. Beside the search box is
   * also where somebody narrowing a list looks for it.
   */
  readonly filters?: React.ReactNode;
  /**
   * What the screen does to the list rather than to a row of it — in practice the
   * button that opens its add form.
   *
   * It leads the toolbar's own controls rather than sitting up in the application
   * bar. Adding to a list is something you do while looking at the list, and the
   * hand that reaches for the column picker or the export is the one that reaches
   * for this.
   */
  readonly actions?: React.ReactNode;
  /** Supplied when the server holds the list and this is one page of it. */
  readonly paging?: GridPaging;
  /**
   * Withdraws the grid's own search box.
   *
   * For the screens that search on the server: two search boxes a hand's width
   * apart, narrowing the same list by different rules, is one more than anybody can
   * use. The screen's own box is the one that reaches the whole master.
   */
  readonly hideSearch?: boolean;
  /**
   * How many rows a page holds when the grid is paging itself.
   *
   * Defaults to whatever the installation is set to. A grid that is genuinely short
   * — a document's own lines — passes `0` to draw everything on one page.
   */
  readonly pageSize?: number;
}): React.JSX.Element {
  const { t } = useTranslation();
  const { can } = useSession();
  const narrow = useIsNarrow();
  const settingsPageSize = useSettings((state) => state.pageSize);

  const [search, setSearch] = useState('');
  const [clientPage, setClientPage] = useState(1);
  const [columnSearch, setColumnSearch] = useState<Record<string, string>>({});
  const [sortKey, setSortKey] = useState<string | null>(null);
  const [sortDescending, setSortDescending] = useState(false);
  const [hidden, setHidden] = useState<ReadonlySet<string>>(new Set());
  const [order, setOrder] = useState<readonly string[]>([]);
  const [frozen, setFrozen] = useState(0);
  const [showPicker, setShowPicker] = useState(false);
  const [saved, setSaved] = useState<string | null>(null);
  /** Held while the arrangement is being written, so it cannot be written twice. */
  const [saving, setSaving] = useState(false);

  // Columns the user is not entitled to never enter the arrangement at all, so they
  // cannot be turned on from the picker or restored by a stale saved layout.
  const permitted = useMemo(
    () =>
      columns.filter(
        (column) => !column.requiredPermission || can(column.requiredPermission),
      ),
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
      (column) =>
        !hidden.has(column.key) &&
        !(column.hiddenByDefault && !order.includes(column.key)) &&
        !(column.hideOnNarrow && narrow),
    );
  }, [permitted, order, hidden, narrow]);

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

  /*
    The grid pages itself when the server has not already.

    Every list in the application used to draw every row it held, however many that
    was, and said how many in one line of small type at the top of the toolbar. Two
    thousand rows of chart of accounts is a page nobody scrolls to the bottom of and
    a browser that stutters while they try. Paging in the browser costs nothing —
    the rows are already here — and gives the reader the same footer, with the same
    page numbers, as the lists the server pages.
  */
  const rowsPerPage = pageSize ?? settingsPageSize;
  const clientPaged = !paging && rowsPerPage > 0 && shown.length > rowsPerPage;
  const clientPages = clientPaged ? Math.ceil(shown.length / rowsPerPage) : 1;

  // Clamped rather than reset. Narrowing a search while on page nine should land on
  // the last page of what is left, not silently on a page that no longer exists.
  const currentClientPage = Math.min(Math.max(clientPage, 1), clientPages);

  const visibleRows = clientPaged
    ? shown.slice((currentClientPage - 1) * rowsPerPage, currentClientPage * rowsPerPage)
    : shown;

  const toggleSort = (key: string): void => {
    // Withdrawn while the server holds the list: sorting the page in hand would put
    // the largest row on screen at the top of fifty and call it the largest of four
    // thousand.
    if (paging) {
      return;
    }

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
    setSaving(true);

    try {
      await saveGridLayout(gridKey, {
        order: visible.map((column) => column.key),
        hidden: [...hidden],
        sortKey,
        sortDescending,
        frozen,
      });
    } finally {
      setSaving(false);
    }

    /*
      The picker closes on a save. Keeping the arrangement is the end of arranging
      it, and a panel of checkboxes left standing over the list afterwards invites
      the next click to be another column rather than the record somebody came for —
      the confirmation below says the arrangement is kept, and the list is what
      should be under it.
    */
    setShowPicker(false);
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
      {/*
        The toolbar scrolls sideways on a phone rather than wrapping. Six buttons
        wrapping to three rows would push the table itself below the fold on every
        screen in the application.
      */}
      <div className="-mx-1 flex items-center gap-2 overflow-x-auto px-1 pb-1 sm:flex-wrap sm:overflow-visible">
        {!paging && !hideSearch && (
          <div className="no-print relative shrink-0">
            <svg
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth={1.8}
              strokeLinecap="round"
              className="pointer-events-none absolute inset-y-0 start-2.5 my-auto size-4 text-ink-subtle"
              aria-hidden="true"
            >
              <circle cx="11" cy="11" r="7" />
              <path d="m20 20-3.6-3.6" />
            </svg>
            <input
              type="search"
              value={search}
              onChange={(event) => {
                setSearch(event.target.value);
                setClientPage(1);
              }}
              placeholder={t('grid.search')}
              className="field-input-sm w-44 ps-8 sm:w-56"
            />
          </div>
        )}

        {filters}

        {/*
          Sorting lives in the column headers, and the card view has none — so
          without this a phone can search and export a list but never order it.
          Offered only where the headers are gone, and only where sorting applies
          at all: a server-paged grid sorts nothing on the client, for the same
          reason it hides the search box.
        */}
        {narrow && !paging && (
          <label className="flex shrink-0 items-center gap-1.5">
            <span className="sr-only">{t('grid.sortBy')}</span>
            <select
              value={sortKey ?? ''}
              onChange={(event) => {
                const next = event.target.value;
                setSortKey(next === '' ? null : next);
                setSortDescending(false);
              }}
              className="field-input-sm w-auto py-1 text-xs"
            >
              <option value="">{t('grid.sortNone')}</option>
              {visible
                .filter((column) => column.header.trim() !== '')
                .map((column) => (
                  <option key={column.key} value={column.key}>
                    {column.header}
                  </option>
                ))}
            </select>

            {sortKey !== null && (
              <GridButton
                onClick={() => setSortDescending((value) => !value)}
                label={t('grid.sortDirection')}
              >
                {sortDescending ? '▾' : '▴'}
              </GridButton>
            )}
          </label>
        )}

        {/*
          The controls, in two groups with a rule between them.

          Every list in the application carried the same six buttons in the same row
          as the button that adds a record, all outlined, all the same size, in the
          order they happened to be written in — so the one action that changes the
          data sat in a row of five that only rearrange the view of it, and no two
          screens agreed on where it was. The add button leads, filled; the view
          controls follow as one group, in one order, on every screen.
        */}
        <div className="ms-auto flex shrink-0 items-center gap-1.5">
          {saved && <span className="badge-success animate-pop">{saved}</span>}

          {actions}

          {actions && <span aria-hidden="true" className="h-5 w-px bg-line" />}

          {/*
            Columns, then Save layout, then the rest. Arranging the columns and
            keeping that arrangement are one task done in two steps, and the button
            for the second step used to sit two buttons away from the first, past
            Freeze and Export — so the arrangement was made and then lost, because
            nothing beside the picker said it could be kept.
          */}
          <GridButton
            onClick={() => setShowPicker((value) => !value)}
            pressed={showPicker}
            disabled={saving}
            title={t('grid.columnsHint')}
          >
            {t('grid.columns')}
          </GridButton>

          <GridButton
            onClick={() => void persist()}
            disabled={saving}
            title={t('grid.saveLayoutHint')}
          >
            {t('grid.saveLayout')}
          </GridButton>

          {/* Freezing a column means nothing once the columns are gone. */}
          {!narrow && (
            <GridButton
              onClick={() => setFrozen((value) => (value === 0 ? 1 : 0))}
              pressed={frozen > 0}
              title={t('grid.freezeHint')}
            >
              {frozen > 0 ? t('grid.unfreeze') : t('grid.freeze')}
            </GridButton>
          )}

          <GridButton onClick={exportCsv} title={t('grid.exportHint')}>
            {t('grid.exportCsv')}
          </GridButton>
          <GridButton
            onClick={() => void restoreDefaults()}
            title={t('grid.resetLayoutHint')}
          >
            {t('grid.resetLayout')}
          </GridButton>
        </div>
      </div>

      {showPicker && (
        <div className="panel animate-drop flex flex-wrap gap-1.5">
          {permitted.map((column) => {
            const isVisible = visible.some((candidate) => candidate.key === column.key);

            return (
              <div
                key={column.key}
                className={clsx(
                  'flex items-center gap-1 rounded-lg border px-2 py-1 text-xs transition',
                  isVisible
                    ? 'border-brand-500/40 bg-brand-50 text-brand-800 dark:bg-brand-500/12 dark:text-brand-200'
                    : 'border-line bg-surface text-ink-muted',
                )}
              >
                <label className="field-check cursor-pointer gap-1.5 text-xs">
                  <input
                    type="checkbox"
                    checked={isVisible}
                    onChange={() => toggleHidden(column.key)}
                  />
                  {column.header}
                </label>
                {/*
                  The labels say "earlier" and "later" rather than left and right,
                  which is right for both a table's columns and a card's fields. The
                  glyphs have to follow the reading direction to agree with them:
                  earlier is leftward in English and rightward in Arabic, so a fixed
                  `‹` would point at the wrong end of the order under Arabic.
                */}
                <button
                  type="button"
                  onClick={() => move(column.key, -1)}
                  className="rounded px-1 text-ink-subtle transition hover:bg-surface-3 hover:text-ink"
                  title={t('grid.moveLeft')}
                >
                  <span className="inline-block rtl:rotate-180">‹</span>
                </button>
                <button
                  type="button"
                  onClick={() => move(column.key, 1)}
                  className="rounded px-1 text-ink-subtle transition hover:bg-surface-3 hover:text-ink"
                  title={t('grid.moveRight')}
                >
                  <span className="inline-block rtl:rotate-180">›</span>
                </button>
              </div>
            );
          })}
        </div>
      )}

      {/*
        Below `sm` the grid stops being a table.

        A twelve-column ledger on a 375px screen is a horizontal scroll where every
        row has to be dragged across to be read, and the headings are off-screen for
        most of that journey — so a figure in the fourth column is a number with no
        label attached. As cards each row carries its own labels and nothing
        scrolls sideways.

        One or the other is rendered, not both hidden with a breakpoint class: a
        four-thousand-row chart of accounts would otherwise build twice.
      */}
      {narrow ? (
        <CardList
          rows={visibleRows}
          columns={visible}
          rowKey={rowKey}
          emptyMessage={emptyMessage ?? t('grid.noRows')}
        />
      ) : (
        <div className="table-wrap table-wrap-tall">
          <table className="table">
            {/*
            `z-20` on the header against `z-10` on a frozen body cell, so a frozen
            first column slides *under* the header rather than over it when the
            table is scrolled in both directions at once.
          */}
            <thead className="sticky top-0 z-20">
              <tr>
                {visible.map((column, index) => (
                  <th
                    key={column.key}
                    aria-sort={
                      sortKey === column.key
                        ? sortDescending
                          ? 'descending'
                          : 'ascending'
                        : undefined
                    }
                    className={clsx(
                      'select-none',
                      // Three alignments, decided by what the column holds rather
                      // than by the order it was declared in: figures end-aligned
                      // so a column of them shares a decimal point, an actions
                      // column end-aligned because its buttons belong against the
                      // edge of the row, and everything else on the reading edge.
                      // The headings follow their cells, which is the half that was
                      // missing — a right-aligned column of money under a
                      // left-aligned heading is a heading pointing at the wrong
                      // column.
                      column.numeric || column.header.trim() === ''
                        ? 'text-end'
                        : 'text-start',
                      column.wide && 'w-[35%] min-w-64',
                      // A frozen column sticks to the logical start edge, so Arabic
                      // freezes from the right without a second rule.
                      index < frozen && 'sticky start-0 z-30 bg-surface-3',
                    )}
                  >
                    {/*
                    A real button, not a click handler on the cell. A `<th>` with an
                    onClick is reachable by mouse only — it takes no focus, answers
                    no Enter or Space, and is announced as a plain heading — so
                    sorting a grid was impossible from the keyboard. `aria-sort`
                    stays on the cell, which is where the specification puts it.
                  */}
                    {paging ? (
                      <span className="inline-flex items-center gap-1">
                        {column.header}
                      </span>
                    ) : (
                      <button
                        type="button"
                        onClick={() => toggleSort(column.key)}
                        className={clsx(
                          '-mx-1 inline-flex items-center gap-1 rounded px-1 py-0.5',
                          'font-semibold tracking-wide uppercase transition-colors',
                          'hover:text-ink focus-visible:ring-2 focus-visible:ring-brand-500/40',
                        )}
                      >
                        {column.header}
                        <span
                          aria-hidden="true"
                          className={clsx(
                            'transition-opacity',
                            sortKey === column.key
                              ? 'text-brand-600 opacity-100 dark:text-brand-300'
                              : // Held in the layout at zero opacity rather than
                                // absent, so the header does not jump sideways the
                                // first time a column is sorted.
                                'opacity-0',
                          )}
                        >
                          {sortKey === column.key && sortDescending ? '▾' : '▴'}
                        </span>
                      </button>
                    )}
                  </th>
                ))}
              </tr>
              {/*
                The per-column filters do not print: on a sheet they are a row of
                empty boxes under the headings, and nothing the reader can type in.

                Nor are they drawn over an empty grid. A row of boxes above the
                words "no documents in this range" offers a way to narrow nothing,
                and reads as furniture the screen forgot to take away.
              */}
              {!paging && rows.length > 0 && (
                <tr className="no-print">
                  {visible.map((column, index) => (
                    <th
                      key={column.key}
                      className={clsx(
                        'bg-surface-3 px-2 pt-0 pb-2',
                        index < frozen && 'sticky start-0 z-30',
                      )}
                    >
                      {/* Nothing to narrow an actions column by: its cells hold
                          buttons, and its `value` is the empty string on every
                          row. A box there was a box that did nothing. */}
                      {column.header.trim() !== '' && (
                        <input
                          type="search"
                          aria-label={`${t('grid.search')} — ${column.header}`}
                          value={columnSearch[column.key] ?? ''}
                          onChange={(event) => {
                            setColumnSearch((previous) => ({
                              ...previous,
                              [column.key]: event.target.value,
                            }));
                            setClientPage(1);
                          }}
                          /*
                            `min-w-16`, not `min-w-24`. The filter box is the widest
                            thing in a narrow column's heading, so its minimum became
                            every column's minimum: at 96px plus padding, a ten-column
                            report needed 1,120px and had 1,134px to live in, so the
                            last column was shaved by a sliver — which reads as a
                            rendering fault rather than as a table that scrolls. Four
                            characters is what anybody types into one of these.
                          */
                          className="w-full min-w-16 rounded-md border border-line bg-surface px-1.5 py-0.5 text-xs font-normal text-ink normal-case outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/15"
                        />
                      )}
                    </th>
                  ))}
                </tr>
              )}
            </thead>

            <tbody>
              {visibleRows.length === 0 ? (
                <tr>
                  <td colSpan={visible.length} className="px-3 py-10 text-center">
                    <p className="text-sm text-ink-muted">
                      {emptyMessage ?? t('grid.noRows')}
                    </p>
                  </td>
                </tr>
              ) : (
                visibleRows.map((row) => (
                  <tr key={rowKey(row)} className="group">
                    {visible.map((column, index) => (
                      <td
                        key={column.key}
                        className={clsx(
                          'py-1.5',
                          column.numeric &&
                            'text-end font-mono whitespace-nowrap tabular-nums',
                          // An actions column, kept against the end of the row so
                          // the buttons line up down the table however wide the
                          // cells before them are.
                          column.header.trim() === '' && 'text-end whitespace-nowrap',
                          column.wide && 'min-w-64',
                          // The frozen cell repaints its own background on hover:
                          // it sits above the row, so the row's hover colour does
                          // not show through it and the stripe would otherwise
                          // break at the first column.
                          index < frozen &&
                            'sticky start-0 z-10 bg-surface group-hover:bg-surface-2',
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
      )}

      {/*
        The footer.

        Where the count and the pager both belong, and where neither of them was.
        The count was a line of small type wedged between the filters and the column
        picker at the top of the toolbar — read by nobody, because the answer to
        "how many are there" is looked for at the end of the list — and the pager
        was two buttons with a sentence between them, offered only when the server
        had already paged the list. Now every list ends the same way: how many rows
        there are, which of them is on screen, and numbered pages to reach the rest.
      */}
      <GridFooter
        totalCount={paging ? paging.totalCount : shown.length}
        unfilteredCount={paging ? paging.totalCount : rows.length}
        page={paging ? paging.page : currentClientPage}
        pageSize={paging ? paging.pageSize : rowsPerPage}
        totalPages={paging ? Math.max(paging.totalPages, 1) : clientPages}
        onPageChange={paging ? paging.onPageChange : setClientPage}
      />
    </div>
  );
}

/**
 * Which page numbers a pager draws.
 *
 * Never more than seven buttons: the first, the last, the current and its
 * neighbours, with a gap standing in for whatever is skipped. A hundred-page list
 * with a hundred buttons is a pager that wraps to four rows and hides the table.
 */
export function pageWindow(page: number, total: number): readonly (number | 'gap')[] {
  if (total <= 7) {
    return Array.from({ length: total }, (_, index) => index + 1);
  }

  const pages = new Set<number>([1, total, page, page - 1, page + 1]);

  // The ends get a second number so the row does not change width as the current
  // page moves off them, which is what makes a pager feel like it is jumping.
  if (page <= 3) {
    pages.add(2).add(3).add(4);
  }

  if (page >= total - 2) {
    pages
      .add(total - 1)
      .add(total - 2)
      .add(total - 3);
  }

  const sorted = [...pages]
    .filter((candidate) => candidate >= 1 && candidate <= total)
    .sort((left, right) => left - right);

  const window: (number | 'gap')[] = [];
  let previous = 0;

  for (const candidate of sorted) {
    if (previous !== 0 && candidate - previous > 1) {
      window.push('gap');
    }

    window.push(candidate);
    previous = candidate;
  }

  return window;
}

/** How many rows there are, which of them are on screen, and the numbered pages. */
function GridFooter({
  totalCount,
  unfilteredCount,
  page,
  pageSize,
  totalPages,
  onPageChange,
}: {
  readonly totalCount: number;
  /** What the list holds before the grid's own search narrowed it. */
  readonly unfilteredCount: number;
  readonly page: number;
  readonly pageSize: number;
  readonly totalPages: number;
  readonly onPageChange: (page: number) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const first = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const last = pageSize > 0 ? Math.min(page * pageSize, totalCount) : totalCount;

  return (
    <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-t border-line pt-3 text-xs text-ink-muted">
      <p className="whitespace-nowrap">
        {totalPages > 1
          ? t('grid.showingRange', { first, last, total: totalCount })
          : t('grid.totalRows', { count: totalCount })}
        {/* Said only when the two differ, so an unfiltered list is not told that it
            is showing all of what it holds. */}
        {unfilteredCount !== totalCount && (
          <span className="ms-1 text-ink-subtle">
            {t('grid.ofUnfiltered', { total: unfilteredCount })}
          </span>
        )}
      </p>

      {totalPages > 1 && (
        <nav
          aria-label={t('grid.pagination')}
          className="no-print flex flex-wrap items-center gap-1"
        >
          <GridButton
            onClick={() => onPageChange(page - 1)}
            disabled={page <= 1}
            label={t('grid.previousPage')}
          >
            <span className="inline-block rtl:rotate-180">‹</span>
          </GridButton>

          {pageWindow(page, totalPages).map((entry, index) =>
            entry === 'gap' ? (
              <span key={`gap-${index}`} className="px-1 text-ink-subtle">
                …
              </span>
            ) : (
              <GridButton
                key={entry}
                onClick={() => onPageChange(entry)}
                current={entry === page}
                label={t('grid.goToPage', { page: entry })}
              >
                {entry}
              </GridButton>
            ),
          )}

          <GridButton
            onClick={() => onPageChange(page + 1)}
            disabled={page >= totalPages}
            label={t('grid.nextPage')}
          >
            <span className="inline-block rtl:rotate-180">›</span>
          </GridButton>
        </nav>
      )}
    </div>
  );
}

/** Tailwind's `sm`. Below it the grid is a list of cards rather than a table. */
const NARROW = '(max-width: 639px)';

/** Tracks whether the viewport is below the table breakpoint. */
function useIsNarrow(): boolean {
  const [narrow, setNarrow] = useState(() => window.matchMedia(NARROW).matches);

  useEffect(() => {
    const query = window.matchMedia(NARROW);
    const onChange = (event: MediaQueryListEvent): void => setNarrow(event.matches);

    query.addEventListener('change', onChange);
    return () => query.removeEventListener('change', onChange);
  }, []);

  return narrow;
}

/**
 * The grid as a list of cards, for screens too narrow to hold a table.
 *
 * The first column leads each card. It is the one that identifies the row — a code,
 * a document number, a name — and on every grid in this application it is also the
 * one carrying the link into the record, so it has to keep whatever `render` gave
 * it rather than being flattened to text.
 *
 * The rest become labelled pairs — but only the ones this row has something to
 * say for. A table can afford an empty cell, because the column above it supplies
 * the meaning; a card cannot, because the label travels with the row. A customer
 * with no mobile, no credit terms and no tax registration was drawing five labels
 * against five blanks, so the card was mostly the shape of the schema rather than
 * the record.
 *
 * A column with no header is an actions column, and its buttons go at the foot of
 * the card with nothing captioning them.
 */
function CardList<TRow>({
  rows,
  columns,
  rowKey,
  emptyMessage,
}: {
  readonly rows: readonly TRow[];
  readonly columns: readonly GridColumn<TRow>[];
  readonly rowKey: (row: TRow) => string;
  readonly emptyMessage: string;
}): React.JSX.Element {
  if (rows.length === 0) {
    return (
      <div className="card px-4 py-10 text-center">
        <p className="text-sm text-ink-muted">{emptyMessage}</p>
      </div>
    );
  }

  const [lead, ...rest] = columns;
  const labelled = rest.filter((column) => column.header.trim() !== '');
  const actions = rest.filter((column) => column.header.trim() === '');

  return (
    <ul className="space-y-2">
      {rows.map((row) => (
        <li key={rowKey(row)} className="card card-body space-y-3 py-3">
          {lead && (
            <div className="text-sm font-semibold text-ink">
              {lead.render ? lead.render(row) : (lead.value(row) ?? '')}
            </div>
          )}

          <dl className="grid grid-cols-[minmax(0,auto)_minmax(0,1fr)] gap-x-3 gap-y-1.5 text-sm">
            {labelled
              // `render` is kept even where the value is blank: a column drawing a
              // badge or a control has something to show that the value does not
              // carry. Zero is a figure, not a blank.
              .filter((column) => {
                const value = column.value(row);
                return column.render !== undefined || (value !== null && value !== '');
              })
              .map((column) => (
                <Fragment key={column.key}>
                  <dt className="text-xs tracking-wide text-ink-muted uppercase">
                    {column.header}
                  </dt>
                  <dd
                    className={clsx(
                      'min-w-0 text-end text-ink',
                      column.numeric && 'font-mono tabular-nums',
                    )}
                  >
                    {column.render ? column.render(row) : (column.value(row) ?? '')}
                  </dd>
                </Fragment>
              ))}
          </dl>

          {actions.length > 0 && (
            <div className="flex flex-wrap gap-2 border-t border-line pt-3">
              {actions.map((column) => (
                <Fragment key={column.key}>
                  {column.render ? column.render(row) : (column.value(row) ?? '')}
                </Fragment>
              ))}
            </div>
          )}
        </li>
      ))}
    </ul>
  );
}

/**
 * The button that opens a screen's add form, at the head of the grid's controls.
 *
 * Filled where the controls beside it are outlined: they arrange the list and this
 * one adds to it, and a row of six identical buttons is a row with no primary
 * action in it. Sized to match them all the same, so the toolbar stays one row.
 */
export function GridAction({
  label,
  onClick,
}: {
  readonly label: string;
  readonly onClick: () => void;
}): React.JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      className="btn-primary btn-sm shrink-0 max-sm:min-h-9 max-sm:px-3 max-sm:text-sm"
    >
      <IconPlus className="size-3.5" />
      {label}
    </button>
  );
}

function GridButton({
  onClick,
  children,
  disabled,
  pressed,
  label,
  title,
  current,
}: {
  readonly onClick: () => void;
  readonly children: React.ReactNode;
  /** Greyed and inert, so the ends of a pager cannot be walked past. */
  readonly disabled?: boolean;
  /** Held down, for the toggles — the column picker and the freeze. */
  readonly pressed?: boolean;
  /** Spoken name, for the buttons whose whole content is a glyph. */
  readonly label?: string;
  /** What the button does, on hover. Six two-word captions need the sentence. */
  readonly title?: string;
  /** The page being shown, for the numbered pager. */
  readonly current?: boolean;
}): React.JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      {...(label === undefined ? {} : { 'aria-label': label })}
      {...(title === undefined ? {} : { title })}
      {...(pressed === undefined ? {} : { 'aria-pressed': pressed })}
      {...(current === true ? { 'aria-current': 'page' as const } : {})}
      className={clsx(
        'shrink-0 rounded-lg border px-2.5 py-1 text-xs font-medium whitespace-nowrap transition duration-150',
        'active:scale-95 disabled:pointer-events-none disabled:opacity-40',
        // The toolbar is the same six controls at every width, and on a phone a
        // 24px button is a target you aim at rather than press.
        'max-sm:min-h-9 max-sm:px-3 max-sm:text-sm',
        pressed || current
          ? 'border-brand-500/40 bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-200'
          : 'border-line bg-surface text-ink-muted hover:border-line-strong hover:bg-surface-3 hover:text-ink',
      )}
    >
      {children}
    </button>
  );
}
