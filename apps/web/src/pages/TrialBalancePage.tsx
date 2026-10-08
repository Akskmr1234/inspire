import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { request, type ApiError } from '@/lib/api';
import { DateRangeControls, ReportFrame } from '@/components/ReportFrame';
import {
  HierarchicalAccountsTable,
  type ReportColumn,
} from '@/components/HierarchicalAccountsTable';
import { buildTrialBalanceHierarchy } from '@/lib/hierarchicalReports';
import { listLedgers, type LedgerSummary } from '@/lib/ledgers';

interface TrialBalanceRow {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly groupCode: string;
  readonly groupName: string;
  readonly nature?: number | undefined;
  readonly openingDebit: number;
  readonly openingCredit: number;
  readonly periodDebit: number;
  readonly periodCredit: number;
  readonly closingDebit: number;
  readonly closingCredit: number;
}

interface TrialBalance {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly rows: readonly TrialBalanceRow[];
  readonly totalOpeningDebit: number;
  readonly totalOpeningCredit: number;
  readonly totalPeriodDebit: number;
  readonly totalPeriodCredit: number;
  readonly totalClosingDebit: number;
  readonly totalClosingCredit: number;
  readonly isBalanced: boolean;
}

function startOfYear(): string {
  return `${new Date().getFullYear()}-01-01`;
}

function endOfYear(): string {
  return `${new Date().getFullYear()}-12-31`;
}

/**
 * The trial balance screen formatted as a unified hierarchical report:
 * Account Head → Group → Ledger/Individual Account → Transactions/Details.
 * Supports expand/collapse with subtotals at all levels and grand total.
 */
export function TrialBalancePage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [from, setFrom] = useState(startOfYear());
  const [to, setTo] = useState(endOfYear());
  const [range, setRange] = useState({ from: startOfYear(), to: endOfYear() });

  const query = useQuery<TrialBalance, ApiError>({
    queryKey: ['trial-balance', range.from, range.to],
    queryFn: () =>
      request<TrialBalance>(
        `/accounting/reports/trial-balance?from=${range.from}&to=${range.to}`,
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
      map.set(l.ledgerId, l);
      map.set(l.code, l);
    });
    return map;
  }, [ledgersQuery.data]);

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

  return (
    <ReportFrame
      title={t('nav.trialBalance')}
      query={query}
      isEmpty={(data) => data.rows.length === 0}
      controls={
        <DateRangeControls
          from={from}
          to={to}
          onFromChange={setFrom}
          onToChange={setTo}
          onApply={() => setRange({ from, to })}
          busy={query.isFetching}
        />
      }
    >
      {(data) => {
        const heads = buildTrialBalanceHierarchy(data.rows, ledgersMap);
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
            fromDate={range.from}
            toDate={range.to}
            isBalanced={data.isBalanced}
            grandTotals={grandTotals}
            onVoucherClick={(vNum) => {
              navigate(`/accounting/voucher-report?posted=${encodeURIComponent(vNum)}`);
            }}
          />
        );
      }}
    </ReportFrame>
  );
}
