import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import clsx from 'clsx';
import { request, type ApiError } from '@/lib/api';
import {
  AsAtControls,
  ReportFrame,
  moneyAlways,
} from '@/components/ReportFrame';
import {
  HierarchicalAccountsTable,
  type ReportColumn,
} from '@/components/HierarchicalAccountsTable';
import { buildBalanceSheetHierarchy } from '@/lib/hierarchicalReports';
import { listLedgers, type LedgerSummary } from '@/lib/ledgers';

interface StatementLine {
  readonly groupCode: string;
  readonly groupName: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly amount: number;
}

interface BalanceSheet {
  readonly currency: string;
  readonly assets: readonly StatementLine[];
  readonly liabilities: readonly StatementLine[];
  readonly equity: readonly StatementLine[];
  readonly totalAssets: number;
  readonly totalLiabilities: number;
  readonly totalEquity: number;
  readonly retainedEarnings: number;
  readonly totalLiabilitiesAndEquity: number;
  readonly isBalanced: boolean;
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * The balance sheet formatted as a unified hierarchical report:
 * Account Head (Assets, Liabilities, Equity) → Group → Ledger/Individual Account → Transactions/Details.
 * Supports expand/collapse with subtotals at all levels and grand total.
 */
export function BalanceSheetPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [asAt, setAsAt] = useState(today());
  const [applied, setApplied] = useState(today());
  const [viewMode, setViewMode] = useState<'tree' | 'facing'>('tree');

  const query = useQuery<BalanceSheet, ApiError>({
    queryKey: ['balance-sheet', applied],
    queryFn: () =>
      request<BalanceSheet>(`/accounting/reports/balance-sheet?asAt=${applied}`),
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
      title={t('nav.balanceSheet')}
      query={query}
      isEmpty={(data) =>
        data.assets.length === 0 &&
        data.liabilities.length === 0 &&
        data.equity.length === 0
      }
      controls={
        <div className="flex flex-wrap items-center gap-3">
          <AsAtControls
            asAt={asAt}
            onChange={setAsAt}
            onApply={() => setApplied(asAt)}
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
        const heads = buildBalanceSheetHierarchy(
          data.assets,
          data.liabilities,
          data.equity,
          data.retainedEarnings,
          ledgersMap,
        );

        const summaryCard = (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
            <div className="rounded-xl border border-emerald-200 dark:border-emerald-800/40 bg-emerald-50/50 dark:bg-emerald-950/20 p-3.5">
              <span className="text-xs font-semibold text-emerald-800 dark:text-emerald-300 uppercase tracking-wider block">
                {t('reports.totalAssets')}
              </span>
              <div className="font-mono text-xl font-black text-emerald-900 dark:text-emerald-100 mt-1">
                {moneyAlways(data.totalAssets)} {data.currency}
              </div>
            </div>

            <div className="rounded-xl border border-amber-200 dark:border-amber-800/40 bg-amber-50/50 dark:bg-amber-950/20 p-3.5">
              <span className="text-xs font-semibold text-amber-800 dark:text-amber-300 uppercase tracking-wider block">
                {t('reports.totalLiabilities')}
              </span>
              <div className="font-mono text-xl font-black text-amber-900 dark:text-amber-100 mt-1">
                {moneyAlways(data.totalLiabilities)} {data.currency}
              </div>
            </div>

            <div className="rounded-xl border border-purple-200 dark:border-purple-800/40 bg-purple-50/50 dark:bg-purple-950/20 p-3.5 sm:col-span-2 lg:col-span-1">
              <span className="text-xs font-semibold text-purple-800 dark:text-purple-300 uppercase tracking-wider block">
                {t('reports.totalLiabilitiesAndEquity')}
              </span>
              <div className="font-mono text-xl font-black text-purple-900 dark:text-purple-100 mt-1">
                {moneyAlways(data.totalLiabilitiesAndEquity)} {data.currency}
              </div>
            </div>
          </div>
        );

        if (viewMode === 'facing') {
          return (
            <div className="space-y-4">
              <div className="grid items-start gap-4 lg:grid-cols-2">
                <HierarchicalAccountsTable
                  heads={heads.filter((h) => h.headId === 1)}
                  columns={columns}
                  currency={data.currency}
                  emptyMessage={t('reports.noAssets') || 'No assets recorded'}
                  grandTotals={{ amount: data.totalAssets }}
                  onVoucherClick={(vNum) => {
                    navigate(`/accounting/voucher-report?posted=${encodeURIComponent(vNum)}`);
                  }}
                />

                <div className="space-y-4">
                  <HierarchicalAccountsTable
                    heads={heads.filter((h) => h.headId === 2 || h.headId === 3)}
                    columns={columns}
                    currency={data.currency}
                    emptyMessage={t('reports.noLiabilities') || 'No liabilities or equity recorded'}
                    grandTotals={{ amount: data.totalLiabilitiesAndEquity }}
                    isBalanced={data.isBalanced}
                    onVoucherClick={(vNum) => {
                      navigate(`/accounting/voucher-report?posted=${encodeURIComponent(vNum)}`);
                    }}
                  />
                </div>
              </div>

              {summaryCard}
            </div>
          );
        }

        return (
          <HierarchicalAccountsTable
            heads={heads}
            columns={columns}
            currency={data.currency}
            isBalanced={data.isBalanced}
            grandTotals={{ amount: data.totalAssets }}
            bottomNote={summaryCard}
            onVoucherClick={(vNum) => {
              navigate(`/accounting/voucher-report?posted=${encodeURIComponent(vNum)}`);
            }}
          />
        );
      }}
    </ReportFrame>
  );
}
