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

              <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-line bg-surface-3 px-4 py-3.5 text-base font-semibold text-ink">
                <span>{t('reports.totalLiabilitiesAndEquity')}</span>
                <span className="font-mono tabular-nums">
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
    <div className="card overflow-hidden">
      <h2 className="border-b border-line bg-surface-3 px-4 py-2.5 text-xs font-semibold tracking-wide text-ink-muted uppercase">
        {heading}
      </h2>

      <div className="overflow-x-auto">
        <table className="table">
          <tbody>
            {lines.length === 0 && extraRow === undefined ? (
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
                    <span className="font-medium">{line.ledgerCode}</span>
                    <span className="ms-2 text-ink-muted">{line.ledgerName}</span>
                  </td>
                  <td className="cell-numeric">{moneyAlways(line.amount)}</td>
                </tr>
              ))
            )}

            {extraRow && (
              <tr className="border-t border-line transition-colors hover:bg-surface-2">
                <td className="px-4 py-2 text-ink-muted italic">{extraRow.label}</td>
                <td className="cell-numeric">{moneyAlways(extraRow.amount)}</td>
              </tr>
            )}
          </tbody>

          <tfoot className="border-t-2 border-line-strong bg-surface-2 font-semibold">
            <tr>
              <td className="px-4 py-2.5 text-ink">{totalLabel}</td>
              <td className="cell-numeric">
                {moneyAlways(extraRow ? total + extraRow.amount : total)} {currency}
              </td>
            </tr>
          </tfoot>
        </table>
      </div>
    </div>
  );
}
