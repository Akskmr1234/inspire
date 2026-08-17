import { Fragment, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import {
  BalanceBadge,
  EmptyState,
  ReportFrame,
  Spinner,
  money,
} from '@/components/ReportFrame';
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

/** A group's code, name and ledger count, drawn the same whether it expands or not. */
function GroupLabel({
  group,
}: {
  readonly group: AccountGroupSummaryRow;
}): React.JSX.Element {
  return (
    <span className="whitespace-nowrap">
      <span>{group.groupCode}</span>
      <span className="ms-2">{group.groupName}</span>
      <span className="ms-2 font-normal text-ink-subtle">({group.ledgerCount})</span>
    </span>
  );
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
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ from, to, includeZeroBalances, includeLedgers });
      }}
    >
      <div className="field">
        <label htmlFor="from" className="field-label">
          {t('reports.from')}
        </label>
        <input
          id="from"
          type="date"
          className="field-input-sm"
          value={from}
          onChange={(e) => setFrom(e.target.value)}
        />
      </div>
      <div className="field">
        <label htmlFor="to" className="field-label">
          {t('reports.to')}
        </label>
        <input
          id="to"
          type="date"
          className="field-input-sm"
          value={to}
          onChange={(e) => setTo(e.target.value)}
        />
      </div>

      <label className="field-check pb-1">
        <input
          type="checkbox"
          checked={includeLedgers}
          onChange={(e) => setIncludeLedgers(e.target.checked)}
        />
        {t('reports.includeLedgers')}
      </label>

      <label className="field-check pb-1">
        <input
          type="checkbox"
          checked={includeZeroBalances}
          onChange={(e) => setIncludeZeroBalances(e.target.checked)}
        />
        {t('reports.includeZeroBalances')}
      </label>

      <button
        type="submit"
        disabled={query.isFetching}
        className="btn-primary btn-sm py-1.5"
      >
        {query.isFetching && <Spinner />}
        {query.isFetching ? t('reports.running') : t('reports.run')}
      </button>
    </form>
  );

  return (
    <ReportFrame title={t('nav.accountGroupSummary')} controls={controls} query={query}>
      {(data) =>
        data.groups.length === 0 ? (
          <EmptyState message={t('reports.noData')} />
        ) : (
          <div className="space-y-4">
            <BalanceBadge isBalanced={data.isBalanced} currency={data.currency} />

            <div className="table-wrap table-wrap-tall">
              <table className="table min-w-[60rem]">
                <thead>
                  <tr>
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
                  {data.groups.map((group) => {
                    const canExpand = group.ledgers.length > 0;
                    const isOpen = expanded.has(group.groupCode);

                    return (
                      <Fragment key={group.groupCode}>
                        <tr className="bg-surface-2 font-semibold">
                          <td className="py-1.5">
                            {/*
                              A real button rather than a click handler on the row.
                              The row carries the group's figures, and a keyboard
                              user needs something focusable to press — a `<tr>`
                              with an onClick is reachable by mouse only.
                            */}
                            {canExpand ? (
                              <button
                                type="button"
                                onClick={() => toggle(group.groupCode)}
                                aria-expanded={isOpen}
                                className="-mx-1 flex items-center gap-1.5 rounded-md px-1 py-0.5 text-start transition hover:bg-surface-3"
                              >
                                <span
                                  aria-hidden="true"
                                  className={clsx(
                                    'inline-block text-ink-subtle transition-transform duration-200',
                                    isOpen ? 'rotate-90' : 'rotate-0 rtl:-rotate-180',
                                  )}
                                >
                                  ▸
                                </span>
                                <GroupLabel group={group} />
                              </button>
                            ) : (
                              <span className="flex items-center gap-1.5 ps-[1.125rem]">
                                <GroupLabel group={group} />
                              </span>
                            )}
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
                            <tr key={ledger.ledgerId} className="animate-fade-in">
                              <td className="ps-9">
                                <span className="font-medium">{ledger.ledgerCode}</span>
                                <span className="ms-2 text-ink-muted">
                                  {ledger.ledgerName}
                                </span>
                              </td>
                              <td className="cell-numeric">
                                {money(ledger.openingDebit)}
                              </td>
                              <td className="cell-numeric">
                                {money(ledger.openingCredit)}
                              </td>
                              <td className="cell-numeric">
                                {money(ledger.periodDebit)}
                              </td>
                              <td className="cell-numeric">
                                {money(ledger.periodCredit)}
                              </td>
                              <td className="cell-numeric">
                                {money(ledger.closingDebit)}
                              </td>
                              <td className="cell-numeric">
                                {money(ledger.closingCredit)}
                              </td>
                            </tr>
                          ))}
                      </Fragment>
                    );
                  })}
                </tbody>

                <tfoot>
                  <tr>
                    <td>{t('reports.totals')}</td>
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
