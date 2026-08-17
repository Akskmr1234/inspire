import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { request, type ApiError } from '@/lib/api';
import { BalanceBadge, DateRangeControls, ReportFrame } from '@/components/ReportFrame';

interface TrialBalanceRow {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly groupCode: string;
  readonly groupName: string;
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

/** Formats a figure for a financial column, blanking zero so the eye follows the numbers. */
function money(value: number): string {
  return value === 0
    ? ''
    : value.toLocaleString(undefined, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      });
}

function startOfYear(): string {
  return `${new Date().getFullYear()}-01-01`;
}

function endOfYear(): string {
  return `${new Date().getFullYear()}-12-31`;
}

/** The trial balance screen. */
export function TrialBalancePage(): React.JSX.Element {
  const { t } = useTranslation();
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
      {(data) => (
        <div className="space-y-4">
          {/*
            The balance state is shown prominently and coloured, because a trial
            balance that does not balance means the books are broken. Printing the
            figures without saying so would leave a reader to compare two totals
            and hope they notice.
          */}
          <BalanceBadge isBalanced={data.isBalanced} currency={data.currency} />

          <div className="table-wrap table-wrap-tall">
            {/*
              A floor width, so the eight money columns keep their figures on one
              line and the table scrolls inside its own container rather than
              crushing every column to three characters on a narrow screen.
            */}
            <table className="table min-w-[64rem]">
              <thead>
                <tr>
                  <th className="text-start">{t('reports.ledger')}</th>
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
                {data.rows.map((row) => (
                  <tr key={row.ledgerId}>
                    <td>
                      <span className="font-medium">{row.ledgerCode}</span>
                      <span className="ms-2 text-ink-muted">{row.ledgerName}</span>
                    </td>
                    <td className="text-ink-muted">
                      {row.groupCode} {row.groupName}
                    </td>
                    <td className="cell-numeric">{money(row.openingDebit)}</td>
                    <td className="cell-numeric">{money(row.openingCredit)}</td>
                    <td className="cell-numeric">{money(row.periodDebit)}</td>
                    <td className="cell-numeric">{money(row.periodCredit)}</td>
                    <td className="cell-numeric">{money(row.closingDebit)}</td>
                    <td className="cell-numeric">{money(row.closingCredit)}</td>
                  </tr>
                ))}
              </tbody>

              <tfoot>
                <tr>
                  <td colSpan={2}>{t('reports.totals')}</td>
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
      )}
    </ReportFrame>
  );
}
