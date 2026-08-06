import { Fragment, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { ReportFrame, moneyAlways } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';

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
        'tabular-nums',
        value < 0
          ? 'text-red-700 dark:text-red-400'
          : 'text-emerald-700 dark:text-emerald-400',
      )}
    >
      {moneyAlways(value)}
    </span>
  );
}

/**
 * The cash flow statement.
 *
 * Built by the direct method from the postings themselves, so every line names the
 * account the money came from or went to. A transfer between the firm's own accounts
 * appears nowhere, which is correct: it does not change what the firm holds.
 *
 * The reconciliation banner is the point of the screen. A cash flow statement is a
 * claim about where a known change in the cash position came from, and if the sections
 * do not add back to that change the right thing to do is say so rather than present
 * three plausible sections that quietly do not sum.
 */
export function CashFlowPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [from, setFrom] = useState(startOfYear());
  const [to, setTo] = useState(endOfYear());
  const [range, setRange] = useState({ from: startOfYear(), to: endOfYear() });

  const query = useQuery<CashFlow, ApiError>({
    queryKey: ['cash-flow', range.from, range.to],
    queryFn: () =>
      request<CashFlow>(
        `/accounting/reports/cash-flow?from=${range.from}&to=${range.to}`,
      ),
  });

  const controls = (
    <form
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setRange({ from, to });
      }}
    >
      <div>
        <label htmlFor="from" className="field-label">
          {t('reports.from')}
        </label>
        <input
          id="from"
          type="date"
          className="field-input"
          value={from}
          onChange={(e) => setFrom(e.target.value)}
        />
      </div>
      <div>
        <label htmlFor="to" className="field-label">
          {t('reports.to')}
        </label>
        <input
          id="to"
          type="date"
          className="field-input"
          value={to}
          onChange={(e) => setTo(e.target.value)}
        />
      </div>
      <button type="submit" disabled={query.isFetching} className="btn-primary">
        {query.isFetching ? t('reports.running') : t('reports.run')}
      </button>
    </form>
  );

  return (
    <ReportFrame title={t('nav.cashFlow')} controls={controls} query={query}>
      {(data) => (
        <div className="space-y-4">
          <p
            className={clsx(
              'inline-block rounded-lg px-3 py-1.5 text-sm font-medium',
              data.isReconciled
                ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200'
                : 'bg-red-100 text-red-900 dark:bg-red-950 dark:text-red-100',
            )}
          >
            {data.isReconciled
              ? `${t('reports.cashReconciled')} · ${data.currency}`
              : t('reports.cashNotReconciled')}
          </p>

          <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800">
            <table className="w-full border-collapse text-sm">
              <thead className="bg-slate-100 dark:bg-slate-800">
                <tr>
                  <th className="px-3 py-2 text-start font-semibold">
                    {t('reports.ledger')}
                  </th>
                  <th className="px-3 py-2 text-end font-semibold">
                    {t('reports.cashIn')}
                  </th>
                  <th className="px-3 py-2 text-end font-semibold">
                    {t('reports.cashOut')}
                  </th>
                  <th className="px-3 py-2 text-end font-semibold">{t('cheques.net')}</th>
                </tr>
              </thead>

              <tbody>
                <tr className="border-t border-slate-200 font-medium dark:border-slate-800">
                  <td className="px-3 py-2" colSpan={3}>
                    {t('reports.openingBalance')}
                  </td>
                  <td className="cell-numeric">{moneyAlways(data.openingBalance)}</td>
                </tr>

                {data.sections.map((section) => (
                  <Fragment key={section.category}>
                    <tr className="border-t border-slate-200 bg-slate-50 font-semibold dark:border-slate-800 dark:bg-slate-800/40">
                      <td className="px-3 py-2">
                        {t(`cashFlow.${CATEGORY_NAME[section.category]}`)}
                      </td>
                      <td className="cell-numeric">{moneyAlways(section.inflow)}</td>
                      <td className="cell-numeric">{moneyAlways(section.outflow)}</td>
                      <td className="cell-numeric">
                        <Signed value={section.net} />
                      </td>
                    </tr>

                    {section.lines.length === 0 ? (
                      <tr className="border-t border-slate-100 dark:border-slate-900">
                        <td
                          className="px-3 py-1 ps-8 text-slate-400 italic"
                          colSpan={4}
                        >
                          {t('reports.noMovement')}
                        </td>
                      </tr>
                    ) : (
                      section.lines.map((line) => (
                        <tr
                          key={line.ledgerId}
                          className="border-t border-slate-100 dark:border-slate-900"
                        >
                          <td className="px-3 py-1 ps-8">
                            <span className="text-slate-500">{line.ledgerCode}</span>{' '}
                            {line.ledgerName}
                          </td>
                          <td className="cell-numeric">{moneyAlways(line.inflow)}</td>
                          <td className="cell-numeric">{moneyAlways(line.outflow)}</td>
                          <td className="cell-numeric">
                            <Signed value={line.net} />
                          </td>
                        </tr>
                      ))
                    )}
                  </Fragment>
                ))}

                <tr className="border-t border-slate-200 font-medium dark:border-slate-800">
                  <td className="px-3 py-2" colSpan={3}>
                    {t('reports.netChange')}
                  </td>
                  <td className="cell-numeric">
                    <Signed value={data.netChange} />
                  </td>
                </tr>
              </tbody>

              <tfoot className="border-t-2 border-slate-300 bg-slate-100 font-semibold dark:border-slate-700 dark:bg-slate-800">
                <tr>
                  <td className="px-3 py-2" colSpan={3}>
                    {t('reports.closingBalance')}
                  </td>
                  <td className="cell-numeric">
                    {moneyAlways(data.closingBalance)} {data.currency}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>
        </div>
      )}
    </ReportFrame>
  );
}
