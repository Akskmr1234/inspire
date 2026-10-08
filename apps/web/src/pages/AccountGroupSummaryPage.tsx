import { Fragment, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import clsx from 'clsx';
import {
  BalanceBadge,
  EmptyState,
  ReportFrame,
  Spinner,
  money,
} from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';
import {
  HierarchicalAccountsTable,
  type ReportColumn,
} from '@/components/HierarchicalAccountsTable';
import { buildAccountGroupSummaryHierarchy } from '@/lib/hierarchicalReports';

interface AccountGroupSummaryLedger {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly openingDebit: number;
  readonly openingCredit: number;
  readonly periodDebit: number;
  readonly periodCredit: number;
  readonly closingDebit: number;
  readonly closingCredit: number;
}

interface AccountGroupSummaryRow {
  readonly groupCode: string;
  readonly groupName: string;
  readonly nature: number;
  readonly openingDebit: number;
  readonly openingCredit: number;
  readonly periodDebit: number;
  readonly periodCredit: number;
  readonly closingDebit: number;
  readonly closingCredit: number;
  readonly ledgerCount: number;
  readonly ledgers: readonly AccountGroupSummaryLedger[];
}

interface AccountGroupSummary {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly groups: readonly AccountGroupSummaryRow[];
  readonly totalOpeningDebit: number;
  readonly totalOpeningCredit: number;
  readonly totalPeriodDebit: number;
  readonly totalPeriodCredit: number;
  readonly totalClosingDebit: number;
  readonly totalClosingCredit: number;
  readonly isBalanced: boolean;
}

function GroupLabel({
  group,
}: {
  readonly group: AccountGroupSummaryRow;
}): React.JSX.Element {
  return (
    <span className="inline-flex items-baseline gap-2 whitespace-nowrap">
      <span>{group.groupCode}</span>
      <span>{group.groupName}</span>
      <span className="font-normal text-ink-subtle">({group.ledgerCount})</span>
    </span>
  );
}

function startOfYear(): string {
  return `${new Date().getFullYear()}-01-01`;
}

function endOfYear(): string {
  return `${new Date().getFullYear()}-12-31`;
}

/**
 * The account group summary report.
 *
 * Formatted as a unified hierarchical report:
 * Account Head (Asset, Liability, Equity, Income, Expense) → Group → Ledger/Individual Account → Transactions/Details.
 * Supports expand/collapse with subtotals at all levels and grand total, plus transaction drill-down.
 */
export function AccountGroupSummaryPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [from, setFrom] = useState(startOfYear());
  const [to, setTo] = useState(endOfYear());
  const [includeZeroBalances, setIncludeZeroBalances] = useState(false);
  const [includeLedgers, setIncludeLedgers] = useState(true);
  const [criteria, setCriteria] = useState({
    from: startOfYear(),
    to: endOfYear(),
    includeZeroBalances: false,
    includeLedgers: true,
  });
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());
  const [viewMode, setViewMode] = useState<'hierarchy' | 'classic'>('hierarchy');

  const query = useQuery<AccountGroupSummary, ApiError>({
    queryKey: [
      'account-group-summary',
      criteria.from,
      criteria.to,
      criteria.includeZeroBalances,
      criteria.includeLedgers,
    ],
    queryFn: () => {
      const params = new URLSearchParams({ from: criteria.from, to: criteria.to });

      if (criteria.includeZeroBalances) {
        params.set('includeZeroBalances', 'true');
      }

      params.set('includeLedgers', String(criteria.includeLedgers));

      return request<AccountGroupSummary>(
        `/accounting/reports/account-group-summary?${params.toString()}`,
      );
    },
  });

  const columns = useMemo<readonly ReportColumn[]>(
    () => [
      { key: 'openingDebit', header: t('reports.openingDebit'), align: 'end', blankZero: true },
      { key: 'openingCredit', header: t('reports.openingCredit'), align: 'end', blankZero: true },
      { key: 'periodDebit', header: t('reports.periodDebit'), align: 'end', blankZero: true },
      { key: 'periodCredit', header: t('reports.periodCredit'), align: 'end', blankZero: true },
      { key: 'closingDebit', header: t('reports.closingDebit'), align: 'end', blankZero: true },
      { key: 'closingCredit', header: t('reports.closingCredit'), align: 'end', blankZero: true },
    ],
    [t],
  );

  const toggle = (code: string): void =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(code)) {
        next.delete(code);
      } else {
        next.add(code);
      }
      return next;
    });

  const controls = (
    <form
      className="toolbar flex flex-wrap items-end justify-between gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ from, to, includeZeroBalances, includeLedgers });
      }}
    >
      <div className="flex flex-wrap items-end gap-3">
        <div className="field">
          <label htmlFor="from" className="field-label">
            {t('reports.from')}
          </label>
          <input
            id="from"
            type="date"
            className="field-input-sm"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="to" className="field-label">
            {t('reports.to')}
          </label>
          <input
            id="to"
            type="date"
            className="field-input-sm"
            value={to}
            onChange={(e) => setTo(e.target.value)}
          />
        </div>

        <label className="field-check pb-1">
          <input
            type="checkbox"
            checked={includeLedgers}
            onChange={(e) => setIncludeLedgers(e.target.checked)}
          />
          {t('reports.includeLedgers')}
        </label>

        <label className="field-check pb-1">
          <input
            type="checkbox"
            checked={includeZeroBalances}
            onChange={(e) => setIncludeZeroBalances(e.target.checked)}
          />
          {t('reports.includeZeroBalances')}
        </label>

        <button type="submit" disabled={query.isFetching} className="btn-primary btn-sm">
          {query.isFetching && <Spinner />}
          {query.isFetching ? t('reports.running') : t('reports.run')}
        </button>
      </div>

      <div className="flex items-center rounded-lg border border-line bg-surface-2 p-0.5 text-xs">
        <button
          type="button"
          onClick={() => setViewMode('hierarchy')}
          className={clsx(
            'flex items-center gap-1.5 rounded-md px-2.5 py-1 font-medium transition',
            viewMode === 'hierarchy'
              ? 'bg-surface text-ink shadow-2xs font-semibold'
              : 'text-ink-muted hover:text-ink',
          )}
        >
          <span>🌳</span>
          <span>{t('reports.hierarchicalTree') || 'Hierarchical Tree'}</span>
        </button>
        <button
          type="button"
          onClick={() => setViewMode('classic')}
          className={clsx(
            'flex items-center gap-1.5 rounded-md px-2.5 py-1 font-medium transition',
            viewMode === 'classic'
              ? 'bg-surface text-ink shadow-2xs font-semibold'
              : 'text-ink-muted hover:text-ink',
          )}
        >
          <span>📋</span>
          <span>{t('reports.classicView') || 'Classic Groups'}</span>
        </button>
      </div>
    </form>
  );

  return (
    <ReportFrame title={t('nav.accountGroupSummary')} controls={controls} query={query}>
      {(data) =>
        data.groups.length === 0 ? (
          <EmptyState message={t('reports.noData')} />
        ) : viewMode === 'hierarchy' ? (
          (() => {
            const heads = buildAccountGroupSummaryHierarchy(data.groups);
            const grandTotals = {
              openingDebit: data.totalOpeningDebit,
              openingCredit: data.totalOpeningCredit,
              periodDebit: data.totalPeriodDebit,
              periodCredit: data.totalPeriodCredit,
              closingDebit: data.totalClosingDebit,
              closingCredit: data.totalClosingCredit,
            };

            return (
              <HierarchicalAccountsTable
                heads={heads}
                columns={columns}
                currency={data.currency}
                fromDate={criteria.from}
                toDate={criteria.to}
                isBalanced={data.isBalanced}
                grandTotals={grandTotals}
                initialExpandLevel={2}
                onVoucherClick={(vNum) => {
                  navigate(`/accounting/voucher-report?posted=${encodeURIComponent(vNum)}`);
                }}
              />
            );
          })()
        ) : (
          <div className="space-y-4">
            <BalanceBadge isBalanced={data.isBalanced} currency={data.currency} />

            <div className="table-wrap table-wrap-tall">
              <table className="table min-w-[60rem]">
                <thead>
                  <tr>
                    <th className="text-start">{t('reports.group')}</th>
                    <th className="text-end">{t('reports.openingDebit')}</th>
                    <th className="text-end">{t('reports.openingCredit')}</th>
                    <th className="text-end">{t('reports.periodDebit')}</th>
                    <th className="text-end">{t('reports.periodCredit')}</th>
                    <th className="text-end">{t('reports.closingDebit')}</th>
                    <th className="text-end">{t('reports.closingCredit')}</th>
                  </tr>
                </thead>

                <tbody>
                  {data.groups.map((group) => {
                    const canExpand = group.ledgers.length > 0;
                    const isOpen = expanded.has(group.groupCode);

                    return (
                      <Fragment key={group.groupCode}>
                        <tr className="bg-surface-2 font-semibold">
                          <td className="py-1.5">
                            {canExpand ? (
                              <button
                                type="button"
                                onClick={() => toggle(group.groupCode)}
                                aria-expanded={isOpen}
                                className="-mx-1 flex items-center gap-1.5 rounded-md px-1 py-0.5 text-start transition hover:bg-surface-3"
                              >
                                <span
                                  aria-hidden="true"
                                  className={clsx(
                                    'inline-block text-ink-subtle transition-transform duration-200',
                                    isOpen ? 'rotate-90' : 'rotate-0 rtl:-rotate-180',
                                  )}
                                >
                                  ▸
                                </span>
                                <GroupLabel group={group} />
                              </button>
                            ) : (
                              <span className="flex items-center gap-1.5 ps-[1.125rem]">
                                <GroupLabel group={group} />
                              </span>
                            )}
                          </td>
                          <td className="cell-numeric">{money(group.openingDebit)}</td>
                          <td className="cell-numeric">{money(group.openingCredit)}</td>
                          <td className="cell-numeric">{money(group.periodDebit)}</td>
                          <td className="cell-numeric">{money(group.periodCredit)}</td>
                          <td className="cell-numeric">{money(group.closingDebit)}</td>
                          <td className="cell-numeric">{money(group.closingCredit)}</td>
                        </tr>

                        {isOpen &&
                          group.ledgers.map((ledger) => (
                            <tr key={ledger.ledgerId} className="animate-fade-in">
                              <td className="ps-9">
                                <span className="flex flex-wrap items-baseline gap-2">
                                  <span className="font-medium">{ledger.ledgerCode}</span>
                                  <span className="text-ink-muted">
                                    {ledger.ledgerName}
                                  </span>
                                </span>
                              </td>
                              <td className="cell-numeric">
                                {money(ledger.openingDebit)}
                              </td>
                              <td className="cell-numeric">
                                {money(ledger.openingCredit)}
                              </td>
                              <td className="cell-numeric">
                                {money(ledger.periodDebit)}
                              </td>
                              <td className="cell-numeric">
                                {money(ledger.periodCredit)}
                              </td>
                              <td className="cell-numeric">
                                {money(ledger.closingDebit)}
                              </td>
                              <td className="cell-numeric">
                                {money(ledger.closingCredit)}
                              </td>
                            </tr>
                          ))}
                      </Fragment>
                    );
                  })}
                </tbody>

                <tfoot>
                  <tr>
                    <td>{t('reports.totals')}</td>
                    <td className="cell-numeric">{money(data.totalOpeningDebit)}</td>
                    <td className="cell-numeric">{money(data.totalOpeningCredit)}</td>
                    <td className="cell-numeric">{money(data.totalPeriodDebit)}</td>
                    <td className="cell-numeric">{money(data.totalPeriodCredit)}</td>
                    <td className="cell-numeric">{money(data.totalClosingDebit)}</td>
                    <td className="cell-numeric">{money(data.totalClosingCredit)}</td>
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>
        )
      }
    </ReportFrame>
  );
}
