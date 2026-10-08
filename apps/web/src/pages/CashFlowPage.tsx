import { Fragment, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import clsx from 'clsx';
import { DateRangeControls, ReportFrame, moneyAlways } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';
import {
  HierarchicalAccountsTable,
  type ReportColumn,
} from '@/components/HierarchicalAccountsTable';
import { buildCashFlowHierarchy } from '@/lib/hierarchicalReports';

/** The cash flow headings, keyed by the wire value the API serialises them as. */
const CATEGORY_NAME: Record<number, string> = {
  1: 'Operating',
  2: 'Investing',
  3: 'Financing',
};

interface CashFlowLine {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly inflow: number;
  readonly outflow: number;
  readonly net: number;
}

interface CashFlowSection {
  readonly category: number;
  readonly lines: readonly CashFlowLine[];
  readonly inflow: number;
  readonly outflow: number;
  readonly net: number;
}

interface CashFlow {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly sections: readonly CashFlowSection[];
  readonly openingBalance: number;
  readonly closingBalance: number;
  readonly netChange: number;
  readonly isReconciled: boolean;
}

function startOfYear(): string {
  return `${new Date().getFullYear()}-01-01`;
}

function endOfYear(): string {
  return `${new Date().getFullYear()}-12-31`;
}

/** A signed figure, red when cash left and green when it arrived. */
function Signed({ value }: { readonly value: number }): React.JSX.Element {
  return (
    <span
      className={clsx(
        'tabular-nums font-mono',
        value < 0
          ? 'text-red-700 dark:text-red-400 font-medium'
          : 'text-emerald-700 dark:text-emerald-400 font-medium',
      )}
    >
      {moneyAlways(value)}
    </span>
  );
}

/**
 * The cash flow statement.
 *
 * Formatted as a unified hierarchical report:
 * Category Head (Operating, Investing, Financing Activities) → Group → Ledger/Individual Account → Transactions/Details.
 * Supports expand/collapse with subtotals at all levels and grand total, plus transaction drill-down.
 */
export function CashFlowPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [from, setFrom] = useState(startOfYear());
  const [to, setTo] = useState(endOfYear());
  const [range, setRange] = useState({ from: startOfYear(), to: endOfYear() });
  const [viewMode, setViewMode] = useState<'hierarchy' | 'classic'>('hierarchy');

  const query = useQuery<CashFlow, ApiError>({
    queryKey: ['cash-flow', range.from, range.to],
    queryFn: () =>
      request<CashFlow>(
        `/accounting/reports/cash-flow?from=${range.from}&to=${range.to}`,
      ),
  });

  const columns = useMemo<readonly ReportColumn[]>(
    () => [
      { key: 'inflow', header: t('reports.cashIn') || 'Cash In', align: 'end', blankZero: true },
      { key: 'outflow', header: t('reports.cashOut') || 'Cash Out', align: 'end', blankZero: true },
      {
        key: 'net',
        header: t('cheques.net') || 'Net Movement',
        align: 'end',
        render: (val) => <Signed value={val ?? 0} />,
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
        busy={query.isFetching}
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
          onClick={() => setViewMode('classic')}
          className={clsx(
            'flex items-center gap-1.5 rounded-md px-2.5 py-1 font-medium transition',
            viewMode === 'classic'
              ? 'bg-surface text-ink shadow-2xs font-semibold'
              : 'text-ink-muted hover:text-ink',
          )}
        >
          <span>📋</span>
          <span>{t('reports.classicView') || 'Classic Flow'}</span>
        </button>
      </div>
    </div>
  );

  return (
    <ReportFrame title={t('nav.cashFlow')} controls={controls} query={query}>
      {(data) => {
        const reconciliationBanner = (
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-line bg-surface-2/60 px-4 py-3">
            <p
              className={clsx(
                'inline-flex items-center gap-2 rounded-lg px-3 py-1.5 text-xs font-semibold',
                data.isReconciled
                  ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-500/12 dark:text-emerald-200'
                  : 'bg-red-50 text-red-800 dark:bg-red-500/12 dark:text-red-200',
              )}
            >
              <span
                aria-hidden="true"
                className={clsx(
                  'size-2 rounded-full',
                  data.isReconciled ? 'bg-emerald-500' : 'animate-breathe bg-red-500',
                )}
              />
              {data.isReconciled
                ? `${t('reports.cashReconciled')} · ${data.currency}`
                : t('reports.cashNotReconciled')}
            </p>

            <div className="flex items-center gap-4 text-xs">
              <span className="text-ink-muted">
                {t('reports.openingBalance')}:{' '}
                <strong className="font-mono text-ink">
                  {moneyAlways(data.openingBalance)} {data.currency}
                </strong>
              </span>
              <span className="text-ink-muted">
                {t('reports.netChange')}:{' '}
                <strong className="font-mono">
                  <Signed value={data.netChange} /> {data.currency}
                </strong>
              </span>
              <span className="text-ink-muted">
                {t('reports.closingBalance')}:{' '}
                <strong className="font-mono text-ink">
                  {moneyAlways(data.closingBalance)} {data.currency}
                </strong>
              </span>
            </div>
          </div>
        );

        if (viewMode === 'hierarchy') {
          const heads = buildCashFlowHierarchy(data.sections);
          const totalInflow = data.sections.reduce((s, sec) => s + sec.inflow, 0);
          const totalOutflow = data.sections.reduce((s, sec) => s + sec.outflow, 0);
          const grandTotals = {
            inflow: totalInflow,
            outflow: totalOutflow,
            net: data.netChange,
          };

          return (
            <div className="space-y-4">
              {reconciliationBanner}

              <HierarchicalAccountsTable
                heads={heads}
                columns={columns}
                currency={data.currency}
                fromDate={range.from}
                toDate={range.to}
                initialExpandLevel={3}
                grandTotals={grandTotals}
                onVoucherClick={(vNum) => {
                  navigate(`/accounting/voucher-report?posted=${encodeURIComponent(vNum)}`);
                }}
              />
            </div>
          );
        }

        return (
          <div className="space-y-4">
            {reconciliationBanner}

            <div className="table-wrap table-wrap-tall">
              <table className="table min-w-[44rem]">
                <thead>
                  <tr>
                    <th className="text-start">{t('reports.ledger')}</th>
                    <th className="text-end">{t('reports.cashIn')}</th>
                    <th className="text-end">{t('reports.cashOut')}</th>
                    <th className="text-end">{t('cheques.net')}</th>
                  </tr>
                </thead>

                <tbody>
                  <tr className="font-medium">
                    <td colSpan={3}>{t('reports.openingBalance')}</td>
                    <td className="cell-numeric">{moneyAlways(data.openingBalance)}</td>
                  </tr>

                  {data.sections.map((section) => (
                    <Fragment key={section.category}>
                      <tr className="bg-surface-2 font-semibold">
                        <td>{t(`cashFlow.${CATEGORY_NAME[section.category]}`)}</td>
                        <td className="cell-numeric">{moneyAlways(section.inflow)}</td>
                        <td className="cell-numeric">{moneyAlways(section.outflow)}</td>
                        <td className="cell-numeric">
                          <Signed value={section.net} />
                        </td>
                      </tr>

                      {section.lines.length === 0 ? (
                        <tr>
                          <td className="py-1 ps-8 text-ink-subtle italic" colSpan={4}>
                            {t('reports.noMovement')}
                          </td>
                        </tr>
                      ) : (
                        section.lines.map((line) => (
                          <tr key={line.ledgerId}>
                            <td className="py-1 ps-8">
                              <span className="text-ink-subtle">{line.ledgerCode}</span>{' '}
                              {line.ledgerName}
                            </td>
                            <td className="cell-numeric py-1">
                              {moneyAlways(line.inflow)}
                            </td>
                            <td className="cell-numeric py-1">
                              {moneyAlways(line.outflow)}
                            </td>
                            <td className="cell-numeric py-1">
                              <Signed value={line.net} />
                            </td>
                          </tr>
                        ))
                      )}
                    </Fragment>
                  ))}

                  <tr className="font-medium">
                    <td colSpan={3}>{t('reports.netChange')}</td>
                    <td className="cell-numeric">
                      <Signed value={data.netChange} />
                    </td>
                  </tr>
                </tbody>

                <tfoot>
                  <tr>
                    <td colSpan={3}>{t('reports.closingBalance')}</td>
                    <td className="cell-numeric">
                      {moneyAlways(data.closingBalance)} {data.currency}
                    </td>
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>
        );
      }}
    </ReportFrame>
  );
}
