import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import clsx from 'clsx';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { DateRangeControls, ReportFrame } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';
import {
  HierarchicalAccountsTable,
  type ReportColumn,
} from '@/components/HierarchicalAccountsTable';
import { buildChartOfAccountsHierarchy } from '@/lib/hierarchicalReports';

/** The ledger kinds, keyed by the wire value the API serialises them as. */
const KIND_NAME: Record<number, string> = {
  1: 'General',
  2: 'Cash',
  3: 'Bank',
  4: 'Customer',
  5: 'Supplier',
  6: 'Employee',
  7: 'Tax',
  8: 'AdditionalCharge',
};

/** The account natures, keyed by the wire value. */
const NATURE_NAME: Record<number, string> = {
  1: 'Asset',
  2: 'Liability',
  3: 'Equity',
  4: 'Income',
  5: 'Expense',
};

interface LedgerSummary {
  readonly ledgerId: string;
  readonly code: string;
  readonly name: string;
  readonly kind: number;
  readonly groupCode: string;
  readonly groupName: string;
  readonly nature: number;
  readonly currency: string;
  readonly isBillWise: boolean;
}

interface TrialBalanceRow {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly openingDebit: number;
  readonly openingCredit: number;
  readonly periodDebit: number;
  readonly periodCredit: number;
  readonly closingDebit: number;
  readonly closingCredit: number;
}

interface TrialBalance {
  readonly rows: readonly TrialBalanceRow[];
  readonly totalOpeningDebit: number;
  readonly totalOpeningCredit: number;
  readonly totalPeriodDebit: number;
  readonly totalPeriodCredit: number;
  readonly totalClosingDebit: number;
  readonly totalClosingCredit: number;
}

function startOfYear(): string {
  return `${new Date().getFullYear()}-01-01`;
}

function endOfYear(): string {
  return `${new Date().getFullYear()}-12-31`;
}

/**
 * The chart of accounts / General Ledger report.
 *
 * Formatted as a unified hierarchical report:
 * Account Head (Asset, Liability, Equity, Income, Expense) → Group → Ledger/Individual Account → Transactions/Details.
 * Supports expand/collapse with subtotals at all levels and grand total, plus transaction drill-down.
 */
export function LedgersPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [from, setFrom] = useState(startOfYear());
  const [to, setTo] = useState(endOfYear());
  const [range, setRange] = useState({ from: startOfYear(), to: endOfYear() });
  const [viewMode, setViewMode] = useState<'hierarchy' | 'grid'>('hierarchy');

  const query = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => request<readonly LedgerSummary[]>('/accounting/ledgers'),
  });

  const tbQuery = useQuery<TrialBalance, ApiError>({
    queryKey: ['trial-balance-for-ledgers', range.from, range.to],
    queryFn: () =>
      request<TrialBalance>(
        `/accounting/reports/trial-balance?from=${range.from}&to=${range.to}`,
      ),
    staleTime: 60 * 1000,
  });

  const balancesMap = useMemo(() => {
    const map = new Map<
      string,
      { opening: number; debit: number; credit: number; closing: number }
    >();
    (tbQuery.data?.rows ?? []).forEach((row) => {
      const opening = row.openingDebit - row.openingCredit;
      const closing = row.closingDebit - row.closingCredit;
      const entry = {
        opening,
        debit: row.periodDebit,
        credit: row.periodCredit,
        closing,
      };
      map.set(row.ledgerId, entry);
      map.set(row.ledgerCode, entry);
    });
    return map;
  }, [tbQuery.data]);

  const hierarchyColumns = useMemo<readonly ReportColumn[]>(
    () => [
      { key: 'opening', header: t('reports.openingBalance') || 'Opening', align: 'end' },
      { key: 'debit', header: t('reports.debit') || 'Debit', align: 'end', blankZero: true },
      { key: 'credit', header: t('reports.credit') || 'Credit', align: 'end', blankZero: true },
      { key: 'closing', header: t('reports.closingBalance') || 'Closing', align: 'end' },
    ],
    [t],
  );

  const gridColumns = useMemo<readonly GridColumn<LedgerSummary>[]>(
    () => [
      {
        key: 'code',
        header: t('reports.ledger'),
        value: (row) => row.code,
      },
      {
        key: 'name',
        header: t('ledgers.name'),
        value: (row) => row.name,
      },
      {
        key: 'kind',
        header: t('ledgers.kind'),
        value: (row) => t(`ledgerKinds.${KIND_NAME[row.kind]}`),
      },
      {
        key: 'groupCode',
        header: t('ledgers.groupCode'),
        value: (row) => row.groupCode,
        hiddenByDefault: true,
      },
      {
        key: 'groupName',
        header: t('reports.group'),
        value: (row) => row.groupName,
      },
      {
        key: 'nature',
        header: t('ledgers.nature'),
        value: (row) => t(`accountNatures.${NATURE_NAME[row.nature]}`),
      },
      {
        key: 'currency',
        header: t('ledgers.currency'),
        value: (row) => row.currency,
      },
      {
        key: 'billWise',
        header: t('ledgers.billWise'),
        value: (row) => (row.isBillWise ? t('common.yes') : t('common.no')),
      },
    ],
    [t],
  );

  const controls = (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <DateRangeControls
        from={from}
        to={to}
        onFromChange={setFrom}
        onToChange={setTo}
        onApply={() => setRange({ from, to })}
        busy={query.isFetching || tbQuery.isFetching}
      />

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
          onClick={() => setViewMode('grid')}
          className={clsx(
            'flex items-center gap-1.5 rounded-md px-2.5 py-1 font-medium transition',
            viewMode === 'grid'
              ? 'bg-surface text-ink shadow-2xs font-semibold'
              : 'text-ink-muted hover:text-ink',
          )}
        >
          <span>📋</span>
          <span>{t('reports.flatList') || 'Flat Grid'}</span>
        </button>
      </div>
    </div>
  );

  return (
    <ReportFrame title={t('nav.ledgers')} controls={controls} query={query}>
      {(data) => {
        if (viewMode === 'grid') {
          return (
            <DataGrid
              gridKey="ledgers"
              rows={data}
              columns={gridColumns}
              rowKey={(row) => row.ledgerId}
            />
          );
        }

        const heads = buildChartOfAccountsHierarchy(data, balancesMap);
        const grandTotals = tbQuery.data
          ? {
              opening: tbQuery.data.totalOpeningDebit - tbQuery.data.totalOpeningCredit,
              debit: tbQuery.data.totalPeriodDebit,
              credit: tbQuery.data.totalPeriodCredit,
              closing: tbQuery.data.totalClosingDebit - tbQuery.data.totalClosingCredit,
            }
          : undefined;

        return (
          <HierarchicalAccountsTable
            heads={heads}
            columns={hierarchyColumns}
            currency="AED"
            fromDate={range.from}
            toDate={range.to}
            initialExpandLevel={2}
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
