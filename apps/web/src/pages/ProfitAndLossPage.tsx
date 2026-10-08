import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import clsx from 'clsx';
import { request, type ApiError } from '@/lib/api';
import { DateRangeControls, ReportFrame, moneyAlways } from '@/components/ReportFrame';
import {
  HierarchicalAccountsTable,
  type ReportColumn,
} from '@/components/HierarchicalAccountsTable';
import { buildProfitAndLossHierarchy } from '@/lib/hierarchicalReports';
import { listLedgers, type LedgerSummary } from '@/lib/ledgers';

interface StatementLine {
  readonly groupCode: string;
  readonly groupName: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly amount: number;
}

interface ProfitAndLoss {
  readonly currency: string;
  readonly income: readonly StatementLine[];
  readonly expenses: readonly StatementLine[];
  readonly totalIncome: number;
  readonly totalExpenses: number;
  readonly netProfit: number;
}

function startOfYear(): string {
  return `${new Date().getFullYear()}-01-01`;
}

function endOfYear(): string {
  return `${new Date().getFullYear()}-12-31`;
}

/**
 * The profit and loss statement formatted as a unified hierarchical report:
 * Account Head (Income & Expense) → Group → Ledger/Individual Account → Transactions/Details.
 * Supports expand/collapse with subtotals at all levels and grand total.
 */
export function ProfitAndLossPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [from, setFrom] = useState(startOfYear());
  const [to, setTo] = useState(endOfYear());
  const [range, setRange] = useState({ from: startOfYear(), to: endOfYear() });
  const [viewMode, setViewMode] = useState<'tree' | 'facing'>('tree');

  const query = useQuery<ProfitAndLoss, ApiError>({
    queryKey: ['profit-and-loss', range.from, range.to],
    queryFn: () =>
      request<ProfitAndLoss>(
        `/accounting/reports/profit-and-loss?from=${range.from}&to=${range.to}`,
      ),
  });

  const ledgersQuery = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => listLedgers(false),
    staleTime: 5 * 60 * 1000,
  });

  const ledgersMap = useMemo(() => {
    const map = new Map<string, LedgerSummary>();
    (ledgersQuery.data ?? []).forEach((l) => {
      map.set(l.code, l);
      map.set(l.ledgerId, l);
    });
    return map;
  }, [ledgersQuery.data]);

  const columns = useMemo<readonly ReportColumn[]>(
    () => [
      { key: 'amount', header: t('reports.amount') || 'Amount', align: 'end' },
    ],
    [t],
  );

  return (
    <ReportFrame
      title={t('nav.profitAndLoss')}
      query={query}
      isEmpty={(data) => data.income.length === 0 && data.expenses.length === 0}
      controls={
        <div className="flex flex-wrap items-center gap-3">
          <DateRangeControls
            from={from}
            to={to}
            onFromChange={setFrom}
            onToChange={setTo}
            onApply={() => setRange({ from, to })}
            busy={query.isFetching}
          />

          <div className="inline-flex rounded-lg bg-surface-2 p-1 border border-line ms-auto">
            <button
              type="button"
              onClick={() => setViewMode('tree')}
              className={clsx(
                'rounded-md px-2.5 py-1 text-xs font-semibold transition',
                viewMode === 'tree'
                  ? 'bg-surface text-ink shadow-2xs'
                  : 'text-ink-muted hover:text-ink',
              )}
            >
              {t('reports.hierarchicalView') || 'Hierarchical Tree'}
            </button>
            <button
              type="button"
              onClick={() => setViewMode('facing')}
              className={clsx(
                'rounded-md px-2.5 py-1 text-xs font-semibold transition',
                viewMode === 'facing'
                  ? 'bg-surface text-ink shadow-2xs'
                  : 'text-ink-muted hover:text-ink',
              )}
            >
              {t('reports.facingColumns') || 'Facing Columns'}
            </button>
          </div>
        </div>
      }
    >
      {(data) => {
        const heads = buildProfitAndLossHierarchy(data.income, data.expenses, ledgersMap);

        const netProfitBanner = (
          <div
            className={clsx(
              'flex animate-rise-sm flex-wrap items-center justify-between gap-2 rounded-xl border px-4 py-3.5 text-base font-semibold shadow-2xs',
              data.netProfit >= 0
                ? 'border-emerald-200 bg-emerald-50 text-emerald-900 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-100'
                : 'border-red-200 bg-red-50 text-red-900 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-100',
            )}
          >
            <div className="flex items-center gap-2">
              <span
                className={clsx(
                  'size-2.5 rounded-full',
                  data.netProfit >= 0 ? 'bg-emerald-500' : 'bg-red-500',
                )}
              />
              <span>
                {data.netProfit >= 0 ? t('reports.netProfit') : t('reports.netLoss')}
              </span>
            </div>

            <span className="font-mono tabular-nums text-lg font-black">
              {moneyAlways(Math.abs(data.netProfit))} {data.currency}
            </span>
          </div>
        );

        if (viewMode === 'facing') {
          return (
            <div className="space-y-4">
              <div className="grid items-start gap-4 lg:grid-cols-2">
                <HierarchicalAccountsTable
                  heads={heads.filter((h) => h.headId === 4)}
                  columns={columns}
                  currency={data.currency}
                  fromDate={range.from}
                  toDate={range.to}
                  emptyMessage={t('reports.noIncome') || 'No income records'}
                  grandTotals={{ amount: data.totalIncome }}
                  onVoucherClick={(vNum) => {
                    navigate(`/accounting/voucher-report?posted=${encodeURIComponent(vNum)}`);
                  }}
                />

                <HierarchicalAccountsTable
                  heads={heads.filter((h) => h.headId === 5)}
                  columns={columns}
                  currency={data.currency}
                  fromDate={range.from}
                  toDate={range.to}
                  emptyMessage={t('reports.noExpenses') || 'No expense records'}
                  grandTotals={{ amount: data.totalExpenses }}
                  onVoucherClick={(vNum) => {
                    navigate(`/accounting/voucher-report?posted=${encodeURIComponent(vNum)}`);
                  }}
                />
              </div>

              {netProfitBanner}
            </div>
          );
        }

        return (
          <HierarchicalAccountsTable
            heads={heads}
            columns={columns}
            currency={data.currency}
            fromDate={range.from}
            toDate={range.to}
            grandTotals={{ amount: data.totalIncome - data.totalExpenses }}
            bottomNote={netProfitBanner}
            onVoucherClick={(vNum) => {
              navigate(`/accounting/voucher-report?posted=${encodeURIComponent(vNum)}`);
            }}
          />
        );
      }}
    </ReportFrame>
  );
}
