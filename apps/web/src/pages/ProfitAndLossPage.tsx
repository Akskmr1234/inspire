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
        <div className="space-y-4">
          {/*
            Facing columns, as the balance sheet has. The statement used to be held
            at `max-w-2xl` under a filter strip that spanned the screen, so the two
            disagreed about where the page ended by most of half a metre — and the
            reader's eye, running down a column of figures, fell off it. Side by
            side the two halves fit the same width the filters do, and what the
            period earned sits beside what it spent.

            `items-start`, because income and expenses rarely run to the same
            number of accounts and the shorter one should stop where it stops.
          */}
          <div className="grid items-start gap-4 lg:grid-cols-2">
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
          </div>

          {/*
            A loss is stated as a loss, in red, not as a negative profit. An
            accountant reading "-1,733.33" beside the word "profit" has to do a
            double-take; naming it removes the ambiguity.
          */}
          <div
            className={clsx(
              'flex animate-rise-sm flex-wrap items-center justify-between gap-2 rounded-xl border px-4 py-3.5 text-base font-semibold',
              data.netProfit >= 0
                ? 'border-emerald-200 bg-emerald-50 text-emerald-900 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-100'
                : 'border-red-200 bg-red-50 text-red-900 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-100',
            )}
          >
            <span>
              {data.netProfit >= 0 ? t('reports.netProfit') : t('reports.netLoss')}
            </span>
            <span className="font-mono tabular-nums">
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
    <div className="card overflow-hidden">
      <h2 className="border-b border-line bg-surface-3 px-4 py-2.5 text-xs font-semibold tracking-wide text-ink-muted uppercase">
        {heading}
      </h2>

      <div className="overflow-x-auto">
        <table className="table">
          <tbody>
            {lines.length === 0 ? (
              <tr>
                <td className="px-4 py-3 text-ink-subtle" colSpan={2}>
                  —
                </td>
              </tr>
            ) : (
              lines.map((line) => (
                <tr
                  key={line.ledgerCode}
                  className="border-t border-line transition-colors hover:bg-surface-2"
                >
                  <td className="px-4 py-2 text-ink">
                    {/* Laid out as a row with a gap, not inline spans with a logical
                        margin. A margin on an inline box is placed at the start of its
                        own fragment, and the bidirectional algorithm reorders fragments
                        of Latin text inside an Arabic line — so in Arabic the gap landed
                        on the far side of the name and the account code ran straight
                        into it. Flex items are laid out in the container's direction and
                        are not reordered, so the gap holds both ways. */}
                    <span className="flex flex-wrap items-baseline gap-2">
                      <span className="font-medium">{line.ledgerCode}</span>
                      <span className="text-ink-muted">{line.ledgerName}</span>
                      <span className="text-xs text-ink-subtle">{line.groupName}</span>
                    </span>
                  </td>
                  <td className="cell-numeric">{moneyAlways(line.amount)}</td>
                </tr>
              ))
            )}
          </tbody>

          <tfoot className="border-t-2 border-line-strong bg-surface-2 font-semibold">
            <tr>
              <td className="px-4 py-2.5 text-ink">{totalLabel}</td>
              <td className="cell-numeric">
                {moneyAlways(total)} {currency}
              </td>
            </tr>
          </tfoot>
        </table>
      </div>
    </div>
  );
}
