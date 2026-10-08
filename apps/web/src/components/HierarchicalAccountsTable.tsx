import { Fragment, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { IconChevron } from '@/components/icons';
import { money, moneyAlways } from '@/lib/money';
import { fetchLedgerStatement } from '@/lib/ledgers';

export interface ReportTransaction {
  readonly id: string;
  readonly date: string;
  readonly voucherNumber: string;
  readonly voucherType?: string | number | undefined;
  readonly referenceNumber?: string | null | undefined;
  readonly particulars?: string | readonly string[] | undefined;
  readonly narration?: string | null | undefined;
  readonly debit?: number | undefined;
  readonly credit?: number | undefined;
  readonly amount?: number | undefined;
  readonly balance?: number | undefined;
}

export interface ReportLedger {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly currency?: string | undefined;
  /** Numerical values keyed by column key. */
  readonly figures: Record<string, number | undefined>;
  /** Pre-loaded transaction lines, if available. */
  readonly transactions?: readonly ReportTransaction[] | undefined;
  /** Opening balance specifically for transactions table. */
  readonly openingBalance?: number | undefined;
  /** Closing balance specifically for transactions table. */
  readonly closingBalance?: number | undefined;
}

export interface ReportGroup {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly ledgers: readonly ReportLedger[];
  /** Subtotals keyed by column key. Recomputed if omitted. */
  readonly subtotals?: Record<string, number | undefined> | undefined;
}

export interface ReportHead {
  readonly id: string;
  readonly headId: number | string;
  readonly name: string;
  readonly nameArabic?: string | undefined;
  readonly badgeTone?: 'asset' | 'liability' | 'equity' | 'income' | 'expense' | 'neutral' | undefined;
  readonly groups: readonly ReportGroup[];
  /** Subtotals keyed by column key. Recomputed if omitted. */
  readonly subtotals?: Record<string, number | undefined> | undefined;
}

export interface ReportColumn {
  readonly key: string;
  readonly header: string;
  readonly align?: 'start' | 'center' | 'end' | undefined;
  readonly width?: string | undefined;
  /** Formats zero as blank instead of 0.00 (standard for Dr/Cr accounting ledgers). */
  readonly blankZero?: boolean | undefined;
  /** Custom renderer for cell values at any level. */
  readonly render?: (
    value: number | undefined,
    row: unknown,
    level: 'head' | 'group' | 'ledger' | 'total',
  ) => React.ReactNode;
}

export interface HierarchicalAccountsTableProps {
  readonly heads: readonly ReportHead[];
  readonly columns: readonly ReportColumn[];
  readonly currency?: string | undefined;
  readonly fromDate?: string | undefined;
  readonly toDate?: string | undefined;
  readonly emptyMessage?: string | undefined;
  /** Whether trial balance check / isBalanced banner should display. */
  readonly isBalanced?: boolean | undefined;
  /** Custom grand totals overrides (if pre-computed by backend). */
  readonly grandTotals?: Record<string, number | undefined> | undefined;
  /** Callback when user clicks a voucher number. */
  readonly onVoucherClick?: (voucherNumber: string, voucherId?: string) => void;
  /** Default expand level: 1 (Heads), 2 (Groups), 3 (Ledgers), 4 (Transactions). Default: 2. */
  readonly initialExpandLevel?: 1 | 2 | 3 | 4 | undefined;
  /** Extra summary or note above table. */
  readonly extraHeader?: React.ReactNode | undefined;
  /** Bottom note or card. */
  readonly bottomNote?: React.ReactNode | undefined;
}

const DEFAULT_TONE = {
  badge: 'bg-slate-100 text-slate-800 dark:bg-slate-800 dark:text-slate-200 border-slate-300 dark:border-slate-700',
  border: 'border-line',
  bg: 'bg-surface-2/40',
};

const TONE_CLASSES: Record<string, { badge: string; border: string; bg: string }> = {
  asset: {
    badge: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300 border-emerald-300 dark:border-emerald-700',
    border: 'border-emerald-200 dark:border-emerald-800/40',
    bg: 'bg-emerald-50/50 dark:bg-emerald-950/20',
  },
  liability: {
    badge: 'bg-amber-100 text-amber-800 dark:bg-amber-950/60 dark:text-amber-300 border-amber-300 dark:border-amber-700',
    border: 'border-amber-200 dark:border-amber-800/40',
    bg: 'bg-amber-50/50 dark:bg-amber-950/20',
  },
  equity: {
    badge: 'bg-purple-100 text-purple-800 dark:bg-purple-950/60 dark:text-purple-300 border-purple-300 dark:border-purple-700',
    border: 'border-purple-200 dark:border-purple-800/40',
    bg: 'bg-purple-50/50 dark:bg-purple-950/20',
  },
  income: {
    badge: 'bg-blue-100 text-blue-800 dark:bg-blue-950/60 dark:text-blue-300 border-blue-300 dark:border-blue-700',
    border: 'border-blue-200 dark:border-blue-800/40',
    bg: 'bg-blue-50/50 dark:bg-blue-950/20',
  },
  expense: {
    badge: 'bg-rose-100 text-rose-800 dark:bg-rose-950/60 dark:text-rose-300 border-rose-300 dark:border-rose-700',
    border: 'border-rose-200 dark:border-rose-800/40',
    bg: 'bg-rose-50/50 dark:bg-rose-950/20',
  },
  neutral: DEFAULT_TONE,
};

/**
 * Standard Hierarchical Accounting Report Component.
 * Formats accounts hierarchically:
 * Account Head (L1) → Group (L2) → Ledger / Individual Account (L3) → Transactions / Details (L4).
 * Supports expand/collapse at all levels with subtotals and grand total.
 */
export function HierarchicalAccountsTable({
  heads,
  columns,
  currency = 'AED',
  fromDate,
  toDate,
  emptyMessage,
  isBalanced,
  grandTotals: customGrandTotals,
  onVoucherClick,
  initialExpandLevel = 2,
  extraHeader,
  bottomNote,
}: HierarchicalAccountsTableProps): React.JSX.Element {
  const { t, i18n } = useTranslation();
  const isArabic = i18n.language.startsWith('ar');

  const [search, setSearch] = useState('');

  // Expand state sets
  const [expandedHeads, setExpandedHeads] = useState<Set<string>>(() => {
    return new Set(heads.map((h) => h.id));
  });

  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(() => {
    if (initialExpandLevel >= 2) {
      return new Set(heads.flatMap((h) => h.groups.map((g) => g.id)));
    }
    return new Set();
  });

  const [expandedLedgers, setExpandedLedgers] = useState<Set<string>>(() => {
    if (initialExpandLevel >= 3) {
      return new Set(heads.flatMap((h) => h.groups.flatMap((g) => g.ledgers.map((l) => l.id))));
    }
    return new Set();
  });

  // Cached on-demand transactions for ledgers: ledgerId -> lines
  const [dynamicTransactions, setDynamicTransactions] = useState<
    Record<string, readonly ReportTransaction[]>
  >({});
  const [loadingLedgers, setLoadingLedgers] = useState<Set<string>>(new Set());

  // Quick Level Switchers
  const setLevel = (level: 1 | 2 | 3 | 4): void => {
    if (level === 1) {
      setExpandedHeads(new Set(heads.map((h) => h.id)));
      setExpandedGroups(new Set());
      setExpandedLedgers(new Set());
    } else if (level === 2) {
      setExpandedHeads(new Set(heads.map((h) => h.id)));
      setExpandedGroups(new Set(heads.flatMap((h) => h.groups.map((g) => g.id))));
      setExpandedLedgers(new Set());
    } else if (level === 3) {
      setExpandedHeads(new Set(heads.map((h) => h.id)));
      setExpandedGroups(new Set(heads.flatMap((h) => h.groups.map((g) => g.id))));
      setExpandedLedgers(
        new Set(heads.flatMap((h) => h.groups.flatMap((g) => g.ledgers.map((l) => l.id)))),
      );
    } else if (level === 4) {
      // Expand all + expand ledgers
      setExpandedHeads(new Set(heads.map((h) => h.id)));
      setExpandedGroups(new Set(heads.flatMap((h) => h.groups.map((g) => g.id))));
      const allLedgers = heads.flatMap((h) => h.groups.flatMap((g) => g.ledgers));
      setExpandedLedgers(new Set(allLedgers.map((l) => l.id)));
      // Trigger load for any missing transactions if dates are present
      if (fromDate && toDate) {
        allLedgers.forEach((l) => {
          if (!l.transactions && !dynamicTransactions[l.id]) {
            void loadLedgerTransactions(l.id);
          }
        });
      }
    }
  };

  const collapseAll = (): void => {
    setExpandedHeads(new Set());
    setExpandedGroups(new Set());
    setExpandedLedgers(new Set());
  };

  const expandAll = (): void => {
    setLevel(3);
  };

  const toggleHead = (id: string): void => {
    setExpandedHeads((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const toggleGroup = (id: string): void => {
    setExpandedGroups((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const toggleLedger = (ledger: ReportLedger): void => {
    const isExpanding = !expandedLedgers.has(ledger.id);

    setExpandedLedgers((prev) => {
      const next = new Set(prev);
      if (next.has(ledger.id)) next.delete(ledger.id);
      else next.add(ledger.id);
      return next;
    });

    if (isExpanding && !ledger.transactions && !dynamicTransactions[ledger.id] && fromDate && toDate) {
      void loadLedgerTransactions(ledger.id);
    }
  };

  const loadLedgerTransactions = async (ledgerId: string): Promise<void> => {
    if (loadingLedgers.has(ledgerId) || !fromDate || !toDate) return;

    setLoadingLedgers((prev) => new Set(prev).add(ledgerId));

    try {
      const res = await fetchLedgerStatement(ledgerId, fromDate, toDate);
      const mappedLines: readonly ReportTransaction[] = (res.lines ?? []).map((line, idx) => ({
        id: line.voucherId || `line-${idx}`,
        date: line.date,
        voucherNumber: line.voucherNumber,
        voucherType: line.voucherType,
        referenceNumber: line.referenceNumber,
        particulars:
          line.contraLedgerNames && line.contraLedgerNames.length > 0
            ? line.contraLedgerNames
            : line.narration || undefined,
        narration: line.narration,
        debit: line.debit,
        credit: line.credit,
        balance: line.runningBalance,
      }));

      setDynamicTransactions((prev) => ({ ...prev, [ledgerId]: mappedLines }));
    } catch {
      // Fallback empty list on error
      setDynamicTransactions((prev) => ({ ...prev, [ledgerId]: [] }));
    } finally {
      setLoadingLedgers((prev) => {
        const next = new Set(prev);
        next.delete(ledgerId);
        return next;
      });
    }
  };

  // Filtered Heads based on search
  const filteredHeads = useMemo(() => {
    const term = search.trim().toLowerCase();
    if (!term) return heads;

    return heads
      .map((head) => {
        const headMatch =
          head.name.toLowerCase().includes(term) ||
          (head.nameArabic && head.nameArabic.toLowerCase().includes(term));

        const matchedGroups = head.groups
          .map((group) => {
            const groupMatch =
              group.name.toLowerCase().includes(term) ||
              group.code.toLowerCase().includes(term);

            const matchedLedgers = group.ledgers.filter((ledger) => {
              return (
                ledger.name.toLowerCase().includes(term) ||
                ledger.code.toLowerCase().includes(term)
              );
            });

            if (groupMatch || headMatch) {
              return group;
            }

            if (matchedLedgers.length > 0) {
              return { ...group, ledgers: matchedLedgers };
            }

            return null;
          })
          .filter((g): g is ReportGroup => g !== null);

        if (headMatch) return head;

        if (matchedGroups.length > 0) {
          return { ...head, groups: matchedGroups };
        }

        return null;
      })
      .filter((h): h is ReportHead => h !== null);
  }, [heads, search]);

  // Recomputed Subtotals per Group
  const groupSubtotals = useMemo(() => {
    const map = new Map<string, Record<string, number>>();

    heads.forEach((h) => {
      h.groups.forEach((g) => {
        if (g.subtotals) {
          map.set(g.id, { ...g.subtotals } as Record<string, number>);
          return;
        }

        const totals: Record<string, number> = {};
        columns.forEach((col) => {
          totals[col.key] = g.ledgers.reduce((sum, l) => sum + (l.figures[col.key] ?? 0), 0);
        });
        map.set(g.id, totals);
      });
    });

    return map;
  }, [heads, columns]);

  // Recomputed Subtotals per Head
  const headSubtotals = useMemo(() => {
    const map = new Map<string, Record<string, number>>();

    heads.forEach((h) => {
      if (h.subtotals) {
        map.set(h.id, { ...h.subtotals } as Record<string, number>);
        return;
      }

      const totals: Record<string, number> = {};
      columns.forEach((col) => {
        totals[col.key] = h.groups.reduce((sum, g) => {
          const gTotals = groupSubtotals.get(g.id);
          return sum + (gTotals?.[col.key] ?? 0);
        }, 0);
      });
      map.set(h.id, totals);
    });

    return map;
  }, [heads, columns, groupSubtotals]);

  // Recomputed Overall Grand Totals
  const computedGrandTotals = useMemo(() => {
    if (customGrandTotals) return customGrandTotals;

    const totals: Record<string, number> = {};
    columns.forEach((col) => {
      totals[col.key] = heads.reduce((sum, h) => {
        const hTotals = headSubtotals.get(h.id);
        return sum + (hTotals?.[col.key] ?? 0);
      }, 0);
    });
    return totals;
  }, [customGrandTotals, columns, heads, headSubtotals]);

  // Total counts for stats
  const totalLedgerCount = useMemo(() => {
    return heads.reduce((acc, h) => acc + h.groups.reduce((gAcc, g) => gAcc + g.ledgers.length, 0), 0);
  }, [heads]);

  const totalGroupCount = useMemo(() => {
    return heads.reduce((acc, h) => acc + h.groups.length, 0);
  }, [heads]);

  const formatVal = (
    val: number | undefined,
    col: ReportColumn,
    row: unknown,
    level: 'head' | 'group' | 'ledger' | 'total',
  ): React.JSX.Element => {
    if (col.render) {
      return <>{col.render(val, row, level)}</>;
    }

    if (val === undefined || Number.isNaN(val)) {
      return <span className="text-ink-subtle">—</span>;
    }

    if (col.blankZero && Math.abs(val) < 0.0001) {
      return <span className="text-ink-subtle">—</span>;
    }

    return (
      <span className={clsx('font-mono tabular-nums', val < 0 && 'text-red-600 dark:text-red-400')}>
        {col.blankZero ? money(val) : moneyAlways(val)}
      </span>
    );
  };

  const isEmpty = heads.length === 0 || totalLedgerCount === 0;

  if (isEmpty) {
    return (
      <div className="card p-8 text-center text-ink-muted">
        <p className="text-sm font-medium">{emptyMessage ?? t('reports.none') ?? 'No account records found in this range.'}</p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {extraHeader}

      {/* Top Interactive Toolbar */}
      <div className="no-print flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-line bg-surface p-3 shadow-sm">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-xs font-semibold text-ink-muted uppercase tracking-wider me-1">
            {t('reports.hierarchyLevels') || 'Hierarchy'}:
          </span>

          <div className="inline-flex rounded-lg bg-surface-2 p-1 border border-line">
            <button
              type="button"
              onClick={() => setLevel(1)}
              className="rounded-md px-2.5 py-1 text-xs font-semibold text-ink-muted hover:text-ink hover:bg-surface transition"
              title="Collapse to Account Heads"
            >
              1. {t('reports.heads') || 'Heads'}
            </button>
            <button
              type="button"
              onClick={() => setLevel(2)}
              className="rounded-md px-2.5 py-1 text-xs font-semibold text-ink-muted hover:text-ink hover:bg-surface transition"
              title="Expand to Account Groups"
            >
              2. {t('reports.groups') || 'Groups'}
            </button>
            <button
              type="button"
              onClick={() => setLevel(3)}
              className="rounded-md px-2.5 py-1 text-xs font-semibold text-ink-muted hover:text-ink hover:bg-surface transition"
              title="Expand to Individual Accounts"
            >
              3. {t('reports.ledgers') || 'Ledgers'}
            </button>
            <button
              type="button"
              onClick={() => setLevel(4)}
              className="rounded-md px-2.5 py-1 text-xs font-semibold text-ink-muted hover:text-ink hover:bg-surface transition"
              title="Expand to Transactions / Details"
            >
              4. {t('reports.transactions') || 'Details'}
            </button>
          </div>

          <div className="hidden sm:flex items-center gap-1.5 ms-2">
            <button
              type="button"
              onClick={expandAll}
              className="rounded-lg border border-line px-2.5 py-1 text-xs font-medium text-ink-muted hover:text-ink hover:bg-surface-2 transition"
            >
              {t('common.expandAll') || 'Expand All'}
            </button>
            <button
              type="button"
              onClick={collapseAll}
              className="rounded-lg border border-line px-2.5 py-1 text-xs font-medium text-ink-muted hover:text-ink hover:bg-surface-2 transition"
            >
              {t('common.collapseAll') || 'Collapse All'}
            </button>
          </div>
        </div>

        <div className="flex items-center gap-3">
          <div className="relative min-w-44">
            <input
              type="search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('reports.searchAccounts') || 'Filter accounts / groups...'}
              className="field-input-sm w-full pe-7 text-xs"
            />
            {search && (
              <button
                type="button"
                onClick={() => setSearch('')}
                className="absolute inset-y-0 end-2 my-auto text-ink-muted hover:text-ink text-xs font-bold"
              >
                ×
              </button>
            )}
          </div>

          <span className="hidden md:inline-block text-xs font-medium text-ink-subtle">
            {heads.length} {t('reports.heads') || 'Heads'} · {totalGroupCount} {t('reports.groups') || 'Groups'} · {totalLedgerCount} {t('reports.accounts') || 'Accounts'}
          </span>
        </div>
      </div>

      {/* Main Hierarchical Tree Grid */}
      <div className="card overflow-hidden border border-line shadow-sm">
        <div className="overflow-x-auto">
          <table className="table min-w-full text-xs">
            <thead>
              <tr className="border-b-2 border-line bg-surface-3 text-ink-muted font-bold uppercase tracking-wider text-[11px]">
                <th className="py-3 px-4 text-start min-w-[20rem]">
                  <span>{t('reports.accountHierarchy') || 'Account Head → Group → Ledger → Details'}</span>
                </th>
                {columns.map((col) => (
                  <th
                    key={col.key}
                    style={col.width ? { width: col.width } : undefined}
                    className={clsx(
                      'py-3 px-3 whitespace-nowrap',
                      col.align === 'start' && 'text-start',
                      col.align === 'center' && 'text-center',
                      (!col.align || col.align === 'end') && 'text-end',
                    )}
                  >
                    {col.header}
                  </th>
                ))}
              </tr>
            </thead>

            <tbody className="divide-y divide-line/60">
              {filteredHeads.map((head) => {
                const isHeadExpanded = expandedHeads.has(head.id);
                const hTotals = headSubtotals.get(head.id) ?? {};
                const tone = (head.badgeTone ? TONE_CLASSES[head.badgeTone] : undefined) ?? DEFAULT_TONE;

                const headLedgersCount = head.groups.reduce((s, g) => s + g.ledgers.length, 0);

                return (
                  <Fragment key={head.id}>
                    {/* ======================================================== */}
                    {/* LEVEL 1: ACCOUNT HEAD (Asset / Liability / Equity etc.)   */}
                    {/* ======================================================== */}
                    <tr
                      onClick={() => toggleHead(head.id)}
                      className={clsx(
                        'cursor-pointer transition-colors font-bold select-none',
                        tone.bg,
                        'hover:bg-primary-500/10 border-t-2 border-line-strong',
                      )}
                    >
                      <td className="py-2.5 px-4">
                        <div className="flex items-center gap-2">
                          <span
                            className={clsx(
                              'inline-flex size-5 items-center justify-center rounded transition-transform text-ink-muted',
                              isHeadExpanded ? 'rotate-90' : 'rotate-0',
                            )}
                          >
                            <IconChevron className="size-3.5" />
                          </span>

                          <span
                            className={clsx(
                              'inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-xs uppercase tracking-wider border shadow-2xs font-extrabold',
                              tone.badge,
                            )}
                          >
                            {head.headId}. {isArabic && head.nameArabic ? head.nameArabic : head.name}
                          </span>

                          <span className="text-[11px] font-medium text-ink-subtle">
                            ({head.groups.length} {t('reports.groups') || 'groups'} · {headLedgersCount} {t('reports.accounts') || 'accounts'})
                          </span>
                        </div>
                      </td>

                      {columns.map((col) => (
                        <td
                          key={col.key}
                          className={clsx(
                            'py-2.5 px-3 font-mono font-bold whitespace-nowrap',
                            col.align === 'start' && 'text-start',
                            col.align === 'center' && 'text-center',
                            (!col.align || col.align === 'end') && 'text-end',
                          )}
                        >
                          {formatVal(hTotals[col.key], col, head, 'head')}
                        </td>
                      ))}
                    </tr>

                    {/* Groups under Head */}
                    {isHeadExpanded &&
                      head.groups.map((group) => {
                        const isGroupExpanded = expandedGroups.has(group.id);
                        const gTotals = groupSubtotals.get(group.id) ?? {};

                        return (
                          <Fragment key={group.id}>
                            {/* ================================================== */}
                            {/* LEVEL 2: GROUP (Current Assets, Fixed Assets etc.) */}
                            {/* ================================================== */}
                            <tr
                              onClick={() => toggleGroup(group.id)}
                              className="cursor-pointer bg-surface-2/60 hover:bg-surface-3 transition-colors font-semibold select-none border-t border-line/40"
                            >
                              <td className="py-2 ps-8 pe-4">
                                <div className="flex items-center gap-2">
                                  <span
                                    className={clsx(
                                      'inline-flex size-4 items-center justify-center rounded transition-transform text-ink-muted',
                                      isGroupExpanded ? 'rotate-90' : 'rotate-0',
                                    )}
                                  >
                                    <IconChevron className="size-3" />
                                  </span>

                                  <span className="font-mono text-[11px] text-ink-muted bg-surface-3 border border-line px-1.5 py-0.2 rounded font-medium">
                                    {group.code}
                                  </span>

                                  <span className="text-ink font-semibold">
                                    {group.name}
                                  </span>

                                  <span className="text-[10px] text-ink-subtle font-normal">
                                    ({group.ledgers.length} {t('reports.accounts') || 'ledgers'})
                                  </span>
                                </div>
                              </td>

                              {columns.map((col) => (
                                <td
                                  key={col.key}
                                  className={clsx(
                                    'py-2 px-3 font-mono font-semibold whitespace-nowrap text-ink',
                                    col.align === 'start' && 'text-start',
                                    col.align === 'center' && 'text-center',
                                    (!col.align || col.align === 'end') && 'text-end',
                                  )}
                                >
                                  {formatVal(gTotals[col.key], col, group, 'group')}
                                </td>
                              ))}
                            </tr>

                            {/* Ledgers under Group */}
                            {isGroupExpanded &&
                              group.ledgers.map((ledger) => {
                                const isLedgerExpanded = expandedLedgers.has(ledger.id);
                                const isLedgerLoading = loadingLedgers.has(ledger.id);
                                const txList = ledger.transactions ?? dynamicTransactions[ledger.id] ?? [];

                                return (
                                  <Fragment key={ledger.id}>
                                    {/* ============================================== */}
                                    {/* LEVEL 3: LEDGER / INDIVIDUAL ACCOUNT           */}
                                    {/* ============================================== */}
                                    <tr
                                      onClick={() => toggleLedger(ledger)}
                                      className={clsx(
                                        'cursor-pointer transition-colors select-none border-t border-line/30',
                                        isLedgerExpanded
                                          ? 'bg-primary-50/40 dark:bg-primary-950/20'
                                          : 'hover:bg-surface-2/40',
                                      )}
                                    >
                                      <td className="py-1.5 ps-14 pe-4">
                                        <div className="flex items-center gap-2">
                                          <span
                                            className={clsx(
                                              'inline-flex size-3.5 items-center justify-center rounded transition-transform text-ink-subtle hover:text-ink',
                                              isLedgerExpanded ? 'rotate-90' : 'rotate-0',
                                            )}
                                          >
                                            <IconChevron className="size-2.5" />
                                          </span>

                                          <span className="font-mono text-xs font-bold text-primary-700 dark:text-primary-400">
                                            {ledger.code}
                                          </span>

                                          <span className="text-ink font-medium">
                                            {ledger.name}
                                          </span>

                                          {isLedgerLoading && (
                                            <span className="skeleton size-3 rounded-full ms-1 animate-spin" />
                                          )}
                                        </div>
                                      </td>

                                      {columns.map((col) => (
                                        <td
                                          key={col.key}
                                          className={clsx(
                                            'py-1.5 px-3 whitespace-nowrap',
                                            col.align === 'start' && 'text-start',
                                            col.align === 'center' && 'text-center',
                                            (!col.align || col.align === 'end') && 'text-end',
                                          )}
                                        >
                                          {formatVal(ledger.figures[col.key], col, ledger, 'ledger')}
                                        </td>
                                      ))}
                                    </tr>

                                    {/* ============================================== */}
                                    {/* LEVEL 4: TRANSACTIONS / DETAILS (POSTINGS)     */}
                                    {/* ============================================== */}
                                    {isLedgerExpanded && (
                                      <tr className="bg-surface-2/30 border-y border-line">
                                        <td colSpan={columns.length + 1} className="p-3 ps-16 pe-6">
                                          <div className="rounded-xl border border-line bg-surface p-3 shadow-inner">
                                            <div className="flex items-center justify-between border-b border-line pb-2 mb-2">
                                              <span className="text-[11px] font-bold text-ink uppercase tracking-wider flex items-center gap-1.5">
                                                <span>📋 {t('reports.transactionDetails') || 'Transactions / Postings'}:</span>
                                                <span className="text-primary-600 dark:text-primary-400 font-mono">
                                                  {ledger.code} — {ledger.name}
                                                </span>
                                              </span>

                                              <span className="text-[10px] text-ink-muted">
                                                {txList.length} {t('reports.entries') || 'entries'}
                                              </span>
                                            </div>

                                            {isLedgerLoading ? (
                                              <div className="py-4 text-center text-xs text-ink-muted space-y-2">
                                                <div className="skeleton h-4 w-48 mx-auto rounded" />
                                                <div className="skeleton h-4 w-72 mx-auto rounded" />
                                              </div>
                                            ) : txList.length === 0 ? (
                                              <p className="py-3 text-center text-xs text-ink-subtle italic">
                                                {t('reports.noTransactionsPeriod') || 'No transaction postings for this account in the selected date range.'}
                                              </p>
                                            ) : (
                                              <div className="overflow-x-auto">
                                                <table className="w-full text-xs border-collapse">
                                                  <thead>
                                                    <tr className="border-b border-line text-[10px] font-bold text-ink-muted uppercase">
                                                      <th className="py-1 px-2 text-start w-24">{t('reports.date') || 'Date'}</th>
                                                      <th className="py-1 px-2 text-start w-28">{t('reports.voucherNo') || 'Voucher #'}</th>
                                                      <th className="py-1 px-2 text-start w-24">{t('reports.type') || 'Type'}</th>
                                                      <th className="py-1 px-3 text-start">{t('reports.particulars') || 'Particulars / Narration'}</th>
                                                      <th className="py-1 px-2 text-end w-28">{t('reports.debit') || 'Debit'}</th>
                                                      <th className="py-1 px-2 text-end w-28">{t('reports.credit') || 'Credit'}</th>
                                                      <th className="py-1 px-2 text-end w-28">{t('reports.balance') || 'Balance'}</th>
                                                    </tr>
                                                  </thead>
                                                  <tbody className="divide-y divide-line/40">
                                                    {txList.map((tx, txIdx) => (
                                                      <tr
                                                        key={tx.id || txIdx}
                                                        className="hover:bg-surface-2 transition-colors"
                                                      >
                                                        <td className="py-1.5 px-2 font-mono text-ink-muted whitespace-nowrap">
                                                          {tx.date}
                                                        </td>
                                                        <td className="py-1.5 px-2 font-mono font-medium text-primary-600 dark:text-primary-400 whitespace-nowrap">
                                                          {onVoucherClick ? (
                                                            <button
                                                              type="button"
                                                              onClick={() => onVoucherClick(tx.voucherNumber, tx.id)}
                                                              className="hover:underline font-semibold"
                                                            >
                                                              {tx.voucherNumber}
                                                            </button>
                                                          ) : (
                                                            tx.voucherNumber
                                                          )}
                                                        </td>
                                                        <td className="py-1.5 px-2 text-ink-muted whitespace-nowrap">
                                                          {tx.voucherType ? String(tx.voucherType) : '—'}
                                                        </td>
                                                        <td className="py-1.5 px-3 text-ink">
                                                          <div>
                                                            {Array.isArray(tx.particulars)
                                                              ? tx.particulars.join(', ')
                                                              : tx.particulars || tx.narration || '—'}
                                                          </div>
                                                          {tx.narration && tx.particulars && tx.particulars !== tx.narration && (
                                                            <span className="text-[10px] text-ink-subtle italic block">
                                                              {tx.narration}
                                                            </span>
                                                          )}
                                                        </td>
                                                        <td className="py-1.5 px-2 text-end font-mono tabular-nums">
                                                          {tx.debit !== undefined && tx.debit > 0 ? money(tx.debit) : '—'}
                                                        </td>
                                                        <td className="py-1.5 px-2 text-end font-mono tabular-nums">
                                                          {tx.credit !== undefined && tx.credit > 0 ? money(tx.credit) : '—'}
                                                        </td>
                                                        <td className="py-1.5 px-2 text-end font-mono tabular-nums font-semibold text-ink">
                                                          {tx.balance !== undefined ? moneyAlways(tx.balance) : '—'}
                                                        </td>
                                                      </tr>
                                                    ))}
                                                  </tbody>
                                                </table>
                                              </div>
                                            )}
                                          </div>
                                        </td>
                                      </tr>
                                    )}
                                  </Fragment>
                                );
                              })}

                            {/* Group Subtotal Row */}
                            {isGroupExpanded && (
                              <tr className="bg-surface-2/40 text-ink-muted text-[11px] font-semibold border-b border-line">
                                <td className="py-1.5 ps-10 pe-4 text-start italic">
                                  {t('reports.subtotal') || 'Subtotal'} {group.name}
                                </td>
                                {columns.map((col) => (
                                  <td
                                    key={col.key}
                                    className={clsx(
                                      'py-1.5 px-3 font-mono whitespace-nowrap text-end',
                                      col.align === 'start' && 'text-start',
                                      col.align === 'center' && 'text-center',
                                    )}
                                  >
                                    {formatVal(gTotals[col.key], col, group, 'group')}
                                  </td>
                                ))}
                              </tr>
                            )}
                          </Fragment>
                        );
                      })}

                    {/* Head Subtotal Row */}
                    {isHeadExpanded && (
                      <tr className="bg-surface-3 text-ink text-xs font-bold border-b-2 border-line">
                        <td className="py-2 ps-6 pe-4 text-start uppercase">
                          {t('reports.totalHead') || 'Total'} {head.name}
                        </td>
                        {columns.map((col) => (
                          <td
                            key={col.key}
                            className={clsx(
                              'py-2 px-3 font-mono whitespace-nowrap text-end font-extrabold',
                              col.align === 'start' && 'text-start',
                              col.align === 'center' && 'text-center',
                            )}
                          >
                            {formatVal(hTotals[col.key], col, head, 'head')}
                          </td>
                        ))}
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>

            {/* Grand Total Footer */}
            <tfoot className="border-t-3 border-line-strong bg-surface-3 font-bold text-xs">
              <tr className="border-b border-line text-ink">
                <td className="py-3 px-4 text-start text-sm uppercase tracking-wider font-black">
                  {t('reports.grandTotal') || 'GRAND TOTAL'} ({currency})
                </td>
                {columns.map((col) => (
                  <td
                    key={col.key}
                    className={clsx(
                      'py-3 px-3 font-mono font-black text-sm whitespace-nowrap',
                      col.align === 'start' && 'text-start',
                      col.align === 'center' && 'text-center',
                      (!col.align || col.align === 'end') && 'text-end',
                    )}
                  >
                    {formatVal(computedGrandTotals[col.key], col, computedGrandTotals, 'total')}
                  </td>
                ))}
              </tr>
            </tfoot>
          </table>
        </div>
      </div>

      {/* Balance Verification Banner (e.g. for Trial Balance & Balance Sheet) */}
      {isBalanced !== undefined && (
        <div
          className={clsx(
            'flex flex-wrap items-center justify-between gap-2 rounded-xl border px-4 py-3 text-sm font-semibold transition',
            isBalanced
              ? 'border-emerald-200 bg-emerald-50 text-emerald-900 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-100'
              : 'border-red-200 bg-red-50 text-red-900 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-100',
          )}
        >
          <div className="flex items-center gap-2">
            <span
              className={clsx(
                'size-2.5 rounded-full',
                isBalanced ? 'bg-emerald-500' : 'bg-red-500 animate-pulse',
              )}
            />
            <span>
              {isBalanced
                ? t('reports.balancedBooks') || 'Books are balanced.'
                : t('reports.unbalancedBooks') || 'Warning: Trial Balance / Balance Sheet is not balanced.'}
            </span>
          </div>

          <span className="text-xs font-mono font-medium">
            {currency}
          </span>
        </div>
      )}

      {bottomNote}
    </div>
  );
}
