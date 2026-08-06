import { Fragment, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ReportFrame, BalanceBadge, money } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';

interface AccountGroupSummaryLedger {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly openingDebit: number;
  readonly openingCredit: number;
  readonly periodDebit: number;
  readonly periodCredit: number;
  readonly closingDebit: number;
  readonly closingCredit: number;
}

interface AccountGroupSummaryRow {
  readonly groupCode: string;
  readonly groupName: string;
  readonly nature: number;
  readonly openingDebit: number;
  readonly openingCredit: number;
  readonly periodDebit: number;
  readonly periodCredit: number;
  readonly closingDebit: number;
  readonly closingCredit: number;
  readonly ledgerCount: number;
  readonly ledgers: readonly AccountGroupSummaryLedger[];
}

interface AccountGroupSummary {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly groups: readonly AccountGroupSummaryRow[];
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
 * The account group report: the trial balance rolled up to the group each ledger
 * reports under, and reconciling with it to the penny.
 *
 * Groups are collapsed to their subtotal by default - the summary a reader opens it
 * for - and expand to the ledgers behind them for drill-down. The same opening,
 * period, and closing columns as the trial balance, and the same balance check, which
 * carries the same weight: if it is false the books are broken.
 */
export function AccountGroupSummaryPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [from, setFrom] = useState(startOfYear());
  const [to, setTo] = useState(endOfYear());
  const [includeZeroBalances, setIncludeZeroBalances] = useState(false);
  const [includeLedgers, setIncludeLedgers] = useState(true);
  const [criteria, setCriteria] = useState({
    from: startOfYear(),
    to: endOfYear(),
    includeZeroBalances: false,
    includeLedgers: true,
  });
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());

  const query = useQuery<AccountGroupSummary, ApiError>({
    queryKey: [
      'account-group-summary',
      criteria.from,
      criteria.to,
      criteria.includeZeroBalances,
      criteria.includeLedgers,
    ],
    queryFn: () => {
      const params = new URLSearchParams({ from: criteria.from, to: criteria.to });

      if (criteria.includeZeroBalances) {
        params.set('includeZeroBalances', 'true');
      }

      params.set('includeLedgers', String(criteria.includeLedgers));

      return request<AccountGroupSummary>(
        `/accounting/reports/account-group-summary?${params.toString()}`,
      );
    },
  });

  const toggle = (code: string): void =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(code)) {
        next.delete(code);
      } else {
        next.add(code);
      }
      return next;
    });

  const controls = (
    <form
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ from, to, includeZeroBalances, includeLedgers });
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

      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={includeLedgers}
          onChange={(e) => setIncludeLedgers(e.target.checked)}
        />
        {t('reports.includeLedgers')}
      </label>

      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={includeZeroBalances}
          onChange={(e) => setIncludeZeroBalances(e.target.checked)}
        />
        {t('reports.includeZeroBalances')}
      </label>

      <button type="submit" disabled={query.isFetching} className="btn-primary">
        {query.isFetching ? t('reports.running') : t('reports.run')}
      </button>
    </form>
  );

  return (
    <ReportFrame title={t('nav.accountGroupSummary')} controls={controls} query={query}>
      {(data) =>
        data.groups.length === 0 ? (
          <p className="text-sm text-slate-500">{t('reports.noData')}</p>
        ) : (
          <div className="space-y-4">
            <BalanceBadge isBalanced={data.isBalanced} currency={data.currency} />

            <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800">
              <table className="w-full border-collapse text-sm">
                <thead className="bg-slate-100 dark:bg-slate-800">
                  <tr>
                    <th className="px-3 py-2 text-start font-semibold">
                      {t('reports.group')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('reports.openingDebit')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('reports.openingCredit')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('reports.periodDebit')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('reports.periodCredit')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('reports.closingDebit')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('reports.closingCredit')}
                    </th>
                  </tr>
                </thead>

                <tbody>
                  {data.groups.map((group) => {
                    const canExpand = group.ledgers.length > 0;
                    const isOpen = expanded.has(group.groupCode);

                    return (
                      <Fragment key={group.groupCode}>
                        <tr
                          onClick={canExpand ? () => toggle(group.groupCode) : undefined}
                          className={
                            'border-t border-slate-200 bg-slate-50 font-semibold dark:border-slate-800 dark:bg-slate-800/40' +
                            (canExpand ? ' cursor-pointer hover:bg-slate-100 dark:hover:bg-slate-800' : '')
                          }
                        >
                          <td className="px-3 py-2">
                            {canExpand && (
                              <span className="me-1 inline-block w-3 text-slate-400">
                                {isOpen ? '▾' : '▸'}
                              </span>
                            )}
                            <span>{group.groupCode}</span>
                            <span className="ms-2">{group.groupName}</span>
                            <span className="ms-2 font-normal text-slate-400">
                              ({group.ledgerCount})
                            </span>
                          </td>
                          <td className="cell-numeric">{money(group.openingDebit)}</td>
                          <td className="cell-numeric">{money(group.openingCredit)}</td>
                          <td className="cell-numeric">{money(group.periodDebit)}</td>
                          <td className="cell-numeric">{money(group.periodCredit)}</td>
                          <td className="cell-numeric">{money(group.closingDebit)}</td>
                          <td className="cell-numeric">{money(group.closingCredit)}</td>
                        </tr>

                        {isOpen &&
                          group.ledgers.map((ledger) => (
                            <tr
                              key={ledger.ledgerId}
                              className="border-t border-slate-100 dark:border-slate-900"
                            >
                              <td className="px-3 py-2 ps-8">
                                <span className="font-medium">{ledger.ledgerCode}</span>
                                <span className="ms-2 text-slate-500">
                                  {ledger.ledgerName}
                                </span>
                              </td>
                              <td className="cell-numeric">{money(ledger.openingDebit)}</td>
                              <td className="cell-numeric">
                                {money(ledger.openingCredit)}
                              </td>
                              <td className="cell-numeric">{money(ledger.periodDebit)}</td>
                              <td className="cell-numeric">{money(ledger.periodCredit)}</td>
                              <td className="cell-numeric">{money(ledger.closingDebit)}</td>
                              <td className="cell-numeric">
                                {money(ledger.closingCredit)}
                              </td>
                            </tr>
                          ))}
                      </Fragment>
                    );
                  })}
                </tbody>

                <tfoot className="border-t-2 border-slate-300 bg-slate-100 font-semibold dark:border-slate-700 dark:bg-slate-800">
                  <tr>
                    <td className="px-3 py-2">{t('reports.totals')}</td>
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
        )
      }
    </ReportFrame>
  );
}
