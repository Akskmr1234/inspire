import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { request, type ApiError } from '@/lib/api';
import { DateRangeControls, ReportFrame, moneyAlways } from '@/components/ReportFrame';

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

/** The profit and loss statement. */
export function ProfitAndLossPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [from, setFrom] = useState(startOfYear());
  const [to, setTo] = useState(endOfYear());
  const [range, setRange] = useState({ from: startOfYear(), to: endOfYear() });

  const query = useQuery<ProfitAndLoss, ApiError>({
    queryKey: ['profit-and-loss', range.from, range.to],
    queryFn: () =>
      request<ProfitAndLoss>(
        `/accounting/reports/profit-and-loss?from=${range.from}&to=${range.to}`,
      ),
  });

  return (
    <ReportFrame
      title={t('nav.profitAndLoss')}
      query={query}
      isEmpty={(data) => data.income.length === 0 && data.expenses.length === 0}
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
        <div className="max-w-2xl space-y-4">
          <Section
            heading={t('reports.income')}
            lines={data.income}
            total={data.totalIncome}
            totalLabel={t('reports.totalIncome')}
            currency={data.currency}
          />

          <Section
            heading={t('reports.expenses')}
            lines={data.expenses}
            total={data.totalExpenses}
            totalLabel={t('reports.totalExpenses')}
            currency={data.currency}
          />

          {/*
            A loss is stated as a loss, in red, not as a negative profit. An
            accountant reading "-1,733.33" beside the word "profit" has to do a
            double-take; naming it removes the ambiguity.
          */}
          <div
            className={clsx(
              'flex items-center justify-between rounded-xl px-4 py-3 text-base font-semibold',
              data.netProfit >= 0
                ? 'bg-emerald-50 text-emerald-900 dark:bg-emerald-950 dark:text-emerald-100'
                : 'bg-red-50 text-red-900 dark:bg-red-950 dark:text-red-100',
            )}
          >
            <span>
              {data.netProfit >= 0 ? t('reports.netProfit') : t('reports.netLoss')}
            </span>
            <span className="font-mono">
              {moneyAlways(Math.abs(data.netProfit))} {data.currency}
            </span>
          </div>
        </div>
      )}
    </ReportFrame>
  );
}

function Section({
  heading,
  lines,
  total,
  totalLabel,
  currency,
}: {
  readonly heading: string;
  readonly lines: readonly StatementLine[];
  readonly total: number;
  readonly totalLabel: string;
  readonly currency: string;
}): React.JSX.Element {
  return (
    <div className="overflow-hidden rounded-xl border border-slate-200 dark:border-slate-800">
      <h2 className="bg-slate-100 px-4 py-2 text-sm font-semibold dark:bg-slate-800">
        {heading}
      </h2>

      <table className="w-full border-collapse text-sm">
        <tbody>
          {lines.length === 0 ? (
            <tr>
              <td className="px-4 py-3 text-slate-400" colSpan={2}>
                —
              </td>
            </tr>
          ) : (
            lines.map((line) => (
              <tr
                key={line.ledgerCode}
                className="border-t border-slate-200 dark:border-slate-800"
              >
                <td className="px-4 py-2">
                  <span className="font-medium">{line.ledgerCode}</span>
                  <span className="ms-2 text-slate-500">{line.ledgerName}</span>
                  <span className="ms-2 text-xs text-slate-400">{line.groupName}</span>
                </td>
                <td className="cell-numeric">{moneyAlways(line.amount)}</td>
              </tr>
            ))
          )}
        </tbody>

        <tfoot className="border-t-2 border-slate-300 bg-slate-50 font-semibold dark:border-slate-700 dark:bg-slate-800/50">
          <tr>
            <td className="px-4 py-2">{totalLabel}</td>
            <td className="cell-numeric">
              {moneyAlways(total)} {currency}
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
