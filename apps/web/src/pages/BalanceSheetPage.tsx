import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { request, type ApiError } from '@/lib/api';
import {
  AsAtControls,
  BalanceBadge,
  ReportFrame,
  moneyAlways,
} from '@/components/ReportFrame';

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

/** The balance sheet, presented as two facing columns. */
export function BalanceSheetPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [asAt, setAsAt] = useState(today());
  const [applied, setApplied] = useState(today());

  const query = useQuery<BalanceSheet, ApiError>({
    queryKey: ['balance-sheet', applied],
    queryFn: () =>
      request<BalanceSheet>(`/accounting/reports/balance-sheet?asAt=${applied}`),
  });

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
        <AsAtControls
          asAt={asAt}
          onChange={setAsAt}
          onApply={() => setApplied(asAt)}
          busy={query.isFetching}
        />
      }
    >
      {(data) => (
        <div className="space-y-4">
          <BalanceBadge isBalanced={data.isBalanced} currency={data.currency} />

          {/*
            Two facing columns, the traditional presentation: assets on one side,
            what funds them on the other. Stacking them would obscure the single
            fact the statement exists to show, which is that the two sides agree.
          */}
          <div className="grid gap-4 lg:grid-cols-2">
            <Panel
              heading={t('reports.assets')}
              lines={data.assets}
              total={data.totalAssets}
              totalLabel={t('reports.totalAssets')}
              currency={data.currency}
            />

            <div className="space-y-4">
              <Panel
                heading={t('reports.liabilities')}
                lines={data.liabilities}
                total={data.totalLiabilities}
                totalLabel={t('reports.totalLiabilities')}
                currency={data.currency}
              />

              <Panel
                heading={t('reports.equity')}
                lines={data.equity}
                total={data.totalEquity}
                totalLabel={t('reports.totalEquity')}
                currency={data.currency}
                extraRow={{
                  // Called out on its own line rather than folded into equity,
                  // because it is the figure that makes the statement balance and
                  // the one a reader most often wants to reconcile against the
                  // profit and loss.
                  label: t('reports.retainedEarnings'),
                  amount: data.retainedEarnings,
                }}
              />

              <div className="flex items-center justify-between rounded-xl bg-slate-100 px-4 py-3 text-base font-semibold dark:bg-slate-800">
                <span>{t('reports.totalLiabilitiesAndEquity')}</span>
                <span className="font-mono">
                  {moneyAlways(data.totalLiabilitiesAndEquity)} {data.currency}
                </span>
              </div>
            </div>
          </div>
        </div>
      )}
    </ReportFrame>
  );
}

function Panel({
  heading,
  lines,
  total,
  totalLabel,
  currency,
  extraRow,
}: {
  readonly heading: string;
  readonly lines: readonly StatementLine[];
  readonly total: number;
  readonly totalLabel: string;
  readonly currency: string;
  readonly extraRow?: { readonly label: string; readonly amount: number };
}): React.JSX.Element {
  return (
    <div className="overflow-hidden rounded-xl border border-slate-200 dark:border-slate-800">
      <h2 className="bg-slate-100 px-4 py-2 text-sm font-semibold dark:bg-slate-800">
        {heading}
      </h2>

      <table className="w-full border-collapse text-sm">
        <tbody>
          {lines.length === 0 && extraRow === undefined ? (
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
                </td>
                <td className="cell-numeric">{moneyAlways(line.amount)}</td>
              </tr>
            ))
          )}

          {extraRow && (
            <tr className="border-t border-slate-200 dark:border-slate-800">
              <td className="px-4 py-2 italic text-slate-600 dark:text-slate-400">
                {extraRow.label}
              </td>
              <td className="cell-numeric">{moneyAlways(extraRow.amount)}</td>
            </tr>
          )}
        </tbody>

        <tfoot className="border-t-2 border-slate-300 bg-slate-50 font-semibold dark:border-slate-700 dark:bg-slate-800/50">
          <tr>
            <td className="px-4 py-2">{totalLabel}</td>
            <td className="cell-numeric">
              {moneyAlways(extraRow ? total + extraRow.amount : total)} {currency}
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}
