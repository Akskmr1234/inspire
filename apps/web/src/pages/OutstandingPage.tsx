import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import clsx from 'clsx';
import { AsAtControls, EmptyState, ReportFrame } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';
import { moneyAlways } from '@/lib/money';

interface OutstandingBill {
  readonly billId: string;
  readonly billNumber: string;
  readonly billDate: string;
  readonly dueDate: string;
  readonly originalAmount: number;
  readonly settledAmount: number;
  readonly outstandingAmount: number;
  readonly daysOverdue: number;
  readonly currency: string;
}

interface OutstandingParty {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly totalOutstanding: number;
  readonly totalOverdue: number;
  readonly bills: readonly OutstandingBill[];
}

interface OutstandingBills {
  readonly asAt: string;
  readonly currency: string;
  readonly parties: readonly OutstandingParty[];
  readonly totalOutstanding: number;
  readonly totalOverdue: number;
  readonly currencies: readonly string[];
}

interface AgingRow {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly notDue: number;
  readonly buckets: readonly number[];
  readonly total: number;
}

interface AgingAnalysis {
  readonly asAt: string;
  readonly currency: string;
  readonly bucketLabels: readonly string[];
  readonly rows: readonly AgingRow[];
  readonly totalNotDue: number;
  readonly bucketTotals: readonly number[];
  readonly total: number;
  readonly currencies: readonly string[];
}

type View = 'bills' | 'aging';

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * What customers owe the firm, or what the firm owes suppliers, bill by bill or aged.
 *
 * One screen for both sides, because the two reports are the same shape read from
 * opposite ends. Read from the open bills, so a party appears here when their account
 * is kept bill-wise; the balance of any other party is on their ledger statement.
 */
export function OutstandingPage({
  side,
  initialView = 'bills',
  headingKey,
}: {
  readonly side: 'debtors' | 'creditors';
  /**
   * Which reading opens first: the parties and what each owes in total (debtors and
   * creditors), or every bill still open (receivables and payables).
   */
  readonly initialView?: View;
  /** The translation key of the menu's name for the screen, where it is not the side's own. */
  readonly headingKey?: string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [asAt, setAsAt] = useState(today());
  const [applied, setApplied] = useState(today());
  const [view, setView] = useState<View>(initialView);
  const [overdueOnly, setOverdueOnly] = useState(false);

  const bills = useQuery<OutstandingBills, ApiError>({
    queryKey: ['outstanding', side, applied, overdueOnly],
    queryFn: () =>
      request<OutstandingBills>(
        `/accounting/reports/${side}?asAt=${applied}&overdueOnly=${String(overdueOnly)}`,
      ),
    enabled: view === 'bills',
  });

  const aging = useQuery<AgingAnalysis, ApiError>({
    queryKey: ['aging', side, applied],
    queryFn: () =>
      request<AgingAnalysis>(`/accounting/reports/${side}-aging?asAt=${applied}`),
    enabled: view === 'aging',
  });

  const controls = (
    <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-end sm:justify-between">
      <AsAtControls
        asAt={asAt}
        onChange={setAsAt}
        onApply={() => setApplied(asAt)}
        busy={view === 'bills' ? bills.isFetching : aging.isFetching}
      />
      <div className="flex flex-wrap items-center gap-3">
        {view === 'bills' && (
          <label className="flex items-center gap-2 text-sm text-ink">
            <input
              type="checkbox"
              className="field-check"
              checked={overdueOnly}
              onChange={(e) => setOverdueOnly(e.target.checked)}
            />
            {t('outstanding.overdueOnly')}
          </label>
        )}
        <div
          role="group"
          aria-label={t('outstanding.view')}
          className="flex items-center rounded-lg border border-line bg-surface-2 p-0.5 text-xs"
        >
          {(['bills', 'aging'] as const).map((mode) => (
            <button
              key={mode}
              type="button"
              aria-pressed={view === mode}
              onClick={() => setView(mode)}
              className={clsx(
                'rounded-md px-2.5 py-1 font-medium transition',
                view === mode
                  ? 'bg-surface font-semibold text-ink shadow-2xs'
                  : 'text-ink-muted hover:text-ink',
              )}
            >
              {t(`outstanding.views.${mode}`)}
            </button>
          ))}
        </div>
      </div>
    </div>
  );

  const title = headingKey ? t(headingKey) : t(`outstanding.${side}Title`);

  return view === 'bills' ? (
    <ReportFrame
      title={title}
      subtitle={t('reports.asAtDate', { date: applied })}
      controls={controls}
      query={bills}
    >
      {(data) => <BillWise data={data} side={side} />}
    </ReportFrame>
  ) : (
    <ReportFrame
      title={title}
      subtitle={t('reports.asAtDate', { date: applied })}
      controls={controls}
      query={aging}
    >
      {(data) => <AgeWise data={data} />}
    </ReportFrame>
  );
}

function BillWise({
  data,
  side,
}: {
  readonly data: OutstandingBills;
  readonly side: 'debtors' | 'creditors';
}): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set());

  if (data.parties.length === 0) {
    return (
      <EmptyState
        message={t(`outstanding.nothing.${side}`)}
        hint={t('outstanding.billWiseHint')}
      />
    );
  }

  const toggle = (id: string): void =>
    setOpen((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  return (
    <div className="space-y-3">
      <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-3">
        <Total label={t('outstanding.parties')} value={String(data.parties.length)} />
        <Total
          label={t('outstanding.totalOutstanding')}
          value={`${moneyAlways(data.totalOutstanding)} ${data.currency}`}
          strong
        />
        <Total
          label={t('outstanding.totalOverdue')}
          value={`${moneyAlways(data.totalOverdue)} ${data.currency}`}
          warn={data.totalOverdue > 0}
        />
      </dl>
      {data.currencies.length > 1 && (
        <p className="alert-warn text-sm">{t('outstanding.mixedCurrencies')}</p>
      )}

      <ul className="space-y-2">
        {data.parties.map((party) => {
          const expanded = open.has(party.ledgerId);
          return (
            <li key={party.ledgerId} className="panel overflow-hidden p-0">
              <button
                type="button"
                aria-expanded={expanded}
                onClick={() => toggle(party.ledgerId)}
                className="flex w-full flex-wrap items-center gap-x-4 gap-y-1 px-3 py-2.5 text-start hover:bg-surface-2/60"
              >
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium text-ink">
                    {party.ledgerName}
                  </span>
                  <span className="block text-xs text-ink-muted">
                    {party.ledgerCode} ·{' '}
                    {t('outstanding.billCount', { count: party.bills.length })}
                  </span>
                </span>
                <span className="text-end">
                  <span className="block font-mono font-semibold tabular-nums text-ink">
                    {moneyAlways(party.totalOutstanding)}
                  </span>
                  {party.totalOverdue > 0 && (
                    <span className="block font-mono text-xs tabular-nums text-amber-700 dark:text-amber-300">
                      {t('outstanding.overdueAmount', {
                        amount: moneyAlways(party.totalOverdue),
                      })}
                    </span>
                  )}
                </span>
              </button>
              {expanded && (
                <div className="border-t border-line">
                  <div className="overflow-x-auto">
                    <table className="table text-sm">
                      <thead>
                        <tr>
                          <th className="text-start">{t('outstanding.bill')}</th>
                          <th className="text-start">{t('reports.date')}</th>
                          <th className="text-start">{t('outstanding.due')}</th>
                          <th className="hidden text-end sm:table-cell">
                            {t('outstanding.original')}
                          </th>
                          <th className="hidden text-end sm:table-cell">
                            {t('outstanding.settled')}
                          </th>
                          <th className="text-end">{t('outstanding.outstanding')}</th>
                          <th className="text-end">{t('outstanding.daysOverdue')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {party.bills.map((bill) => (
                          <tr key={bill.billId}>
                            <td className="font-mono">{bill.billNumber}</td>
                            <td>{bill.billDate}</td>
                            <td>{bill.dueDate}</td>
                            <td className="cell-numeric hidden sm:table-cell">
                              {moneyAlways(bill.originalAmount)}
                            </td>
                            <td className="cell-numeric hidden sm:table-cell">
                              {moneyAlways(bill.settledAmount)}
                            </td>
                            <td className="cell-numeric font-semibold">
                              {moneyAlways(bill.outstandingAmount)}
                            </td>
                            <td
                              className={clsx(
                                'cell-numeric',
                                bill.daysOverdue > 0 &&
                                  'text-amber-700 dark:text-amber-300',
                              )}
                            >
                              {bill.daysOverdue > 0 ? bill.daysOverdue : '—'}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  <div className="flex justify-end border-t border-line px-3 py-2">
                    <button
                      type="button"
                      className="btn-ghost btn-xs"
                      onClick={() =>
                        navigate(`/accounting/ledger-statement?ledger=${party.ledgerId}`)
                      }
                    >
                      {t('ledgerMaster.statement')}
                    </button>
                  </div>
                </div>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}

function AgeWise({ data }: { readonly data: AgingAnalysis }): React.JSX.Element {
  const { t } = useTranslation();
  // The server names its buckets in English ("1-30 days", "Over 90 days"); read the
  // numbers back out so the headings follow the screen's language.
  const labels = useMemo(
    () =>
      data.bucketLabels.map((label) => {
        const range = /^(\d+)\s*-\s*(\d+)/.exec(label);
        if (range) return t('outstanding.range', { from: range[1], to: range[2] });
        const over = /(\d+)/.exec(label);
        return over ? t('outstanding.over', { days: over[1] }) : label;
      }),
    [data.bucketLabels, t],
  );

  if (data.rows.length === 0) {
    return (
      <EmptyState
        message={t('outstanding.nothingAged')}
        hint={t('outstanding.billWiseHint')}
      />
    );
  }

  return (
    <div className="space-y-3">
      <div className="table-wrap">
        <table className="table min-w-[40rem] text-sm">
          <thead>
            <tr>
              <th className="sticky start-0 z-10 bg-surface-3 text-start">
                {t('outstanding.party')}
              </th>
              <th className="text-end">{t('outstanding.notDue')}</th>
              {labels.map((label) => (
                <th key={label} className="text-end">
                  {label}
                </th>
              ))}
              <th className="text-end">{t('outstanding.total')}</th>
            </tr>
          </thead>
          <tbody>
            {data.rows.map((row) => (
              <tr key={row.ledgerId}>
                <td className="sticky start-0 z-10 bg-surface">
                  <span className="block font-medium text-ink">{row.ledgerName}</span>
                  <span className="block text-xs text-ink-muted">{row.ledgerCode}</span>
                </td>
                <td className="cell-numeric">{amount(row.notDue)}</td>
                {row.buckets.map((value, index) => (
                  <td
                    key={index}
                    className={clsx(
                      'cell-numeric',
                      value > 0 && index >= 2 && 'text-red-700 dark:text-red-300',
                      value > 0 && index < 2 && 'text-amber-700 dark:text-amber-300',
                    )}
                  >
                    {amount(value)}
                  </td>
                ))}
                <td className="cell-numeric font-semibold">{moneyAlways(row.total)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <td className="sticky start-0 z-10 bg-surface-3">
                {t('outstanding.total')}
              </td>
              <td className="cell-numeric">{moneyAlways(data.totalNotDue)}</td>
              {data.bucketTotals.map((value, index) => (
                <td key={index} className="cell-numeric">
                  {moneyAlways(value)}
                </td>
              ))}
              <td className="cell-numeric">
                {moneyAlways(data.total)} {data.currency}
              </td>
            </tr>
          </tfoot>
        </table>
      </div>
    </div>
  );
}

function amount(value: number): string {
  return Math.abs(value) < 0.005 ? '—' : moneyAlways(value);
}

function Total({
  label,
  value,
  strong = false,
  warn = false,
}: {
  readonly label: string;
  readonly value: string;
  readonly strong?: boolean;
  readonly warn?: boolean;
}): React.JSX.Element {
  return (
    <div className="panel px-3 py-2">
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd
        className={clsx(
          'font-mono tabular-nums',
          strong && 'font-semibold text-ink',
          warn ? 'text-amber-700 dark:text-amber-300' : 'text-ink',
        )}
      >
        {value}
      </dd>
    </div>
  );
}
