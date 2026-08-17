import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { BalanceBadge, ReportFrame, Spinner } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';

interface DayBookLine {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly narration: string | null;
  readonly debit: number;
  readonly credit: number;
}

interface DayBookEntry {
  readonly voucherId: string;
  readonly date: string;
  readonly voucherNumber: string;
  readonly voucherType: string;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly amount: number;
  readonly lines: readonly DayBookLine[];
}

interface DayBook {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly totalDebit: number;
  readonly totalCredit: number;
  readonly voucherCount: number;
  readonly entries: readonly DayBookEntry[];
}

/**
 * The voucher types the filter offers.
 *
 * Values must match the server enum names exactly - the API binds the query
 * string straight onto `VoucherType`, and a mismatch fails validation rather
 * than silently returning everything.
 */
const VOUCHER_TYPES = [
  'CashReceipt',
  'BankReceipt',
  'CashPayment',
  'BankPayment',
  'Journal',
  'Contra',
] as const;

/** Formats a figure for a financial column, blanking zero so the eye follows the numbers. */
function money(value: number): string {
  return value === 0
    ? ''
    : value.toLocaleString(undefined, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      });
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function startOfMonth(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`;
}

/**
 * The day book: every voucher posted in a period, in order.
 *
 * Defaults to the current month rather than the year the other reports use. The
 * day book is a register read a day or a week at a time, and it returns every
 * line of every voucher - a year of it is neither useful on screen nor kind to
 * the server.
 */
export function DayBookPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [from, setFrom] = useState(startOfMonth());
  const [to, setTo] = useState(today());
  const [voucherType, setVoucherType] = useState('');
  const [criteria, setCriteria] = useState({
    from: startOfMonth(),
    to: today(),
    voucherType: '',
  });

  const query = useQuery<DayBook, ApiError>({
    queryKey: ['day-book', criteria.from, criteria.to, criteria.voucherType],
    queryFn: () => {
      const params = new URLSearchParams({ from: criteria.from, to: criteria.to });

      if (criteria.voucherType) {
        params.set('voucherType', criteria.voucherType);
      }

      return request<DayBook>(`/accounting/reports/day-book?${params.toString()}`);
    },
  });

  const controls = (
    <form
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ from, to, voucherType });
      }}
    >
      <label className="field">
        <span className="field-label">{t('reports.from')}</span>
        <input
          type="date"
          value={from}
          onChange={(event) => setFrom(event.target.value)}
          className="field-input-sm"
        />
      </label>

      <label className="field">
        <span className="field-label">{t('reports.to')}</span>
        <input
          type="date"
          value={to}
          onChange={(event) => setTo(event.target.value)}
          className="field-input-sm"
        />
      </label>

      <label className="field">
        <span className="field-label">{t('reports.voucherType')}</span>
        <select
          value={voucherType}
          onChange={(event) => setVoucherType(event.target.value)}
          className="field-input-sm"
        >
          <option value="">{t('reports.allTypes')}</option>
          {VOUCHER_TYPES.map((type) => (
            <option key={type} value={type}>
              {t(`voucherTypes.${type}`)}
            </option>
          ))}
        </select>
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
    <ReportFrame
      title={t('nav.dayBook')}
      controls={controls}
      query={query}
      isEmpty={(data) => data.entries.length === 0}
    >
      {(data) => (
        <div className="space-y-4">
          <p className="text-sm text-ink-muted">
            {t('reports.voucherCount', { count: data.voucherCount })} · {data.currency}
          </p>

          <div className="table-wrap table-wrap-tall">
            <table className="table min-w-[56rem]">
              <thead>
                <tr>
                  <th className="text-start">{t('reports.date')}</th>
                  <th className="text-start">{t('reports.voucherNo')}</th>
                  <th className="text-start">{t('reports.ledger')}</th>
                  <th className="text-start">{t('reports.particulars')}</th>
                  <th className="text-end">{t('reports.debit')}</th>
                  <th className="text-end">{t('reports.credit')}</th>
                </tr>
              </thead>

              {data.entries.map((entry) => (
                // One tbody per voucher, so the browser keeps a voucher's lines
                // together and the group can be styled as the single document it
                // actually is. The border goes on the group rather than each row,
                // which is what makes a five-line voucher read as one block.
                <tbody
                  key={entry.voucherId}
                  className="border-t border-line align-top transition-colors hover:bg-surface-2"
                >
                  {entry.lines.map((line, index) => (
                    <tr
                      key={`${entry.voucherId}-${line.ledgerId}-${index}`}
                      className="border-t-0"
                    >
                      <td className="py-1 text-ink-muted whitespace-nowrap">
                        {index === 0 ? entry.date : ''}
                      </td>
                      <td className="py-1">
                        {index === 0 ? (
                          <span className="font-medium whitespace-nowrap">
                            {entry.voucherNumber}
                          </span>
                        ) : (
                          ''
                        )}
                      </td>
                      <td className="py-1">
                        <span className="text-ink-subtle">{line.ledgerCode}</span>{' '}
                        {line.ledgerName}
                      </td>
                      <td className="py-1 text-ink-muted">
                        {index === 0
                          ? (line.narration ?? entry.narration ?? '')
                          : (line.narration ?? '')}
                      </td>
                      <td className="cell-numeric py-1">{money(line.debit)}</td>
                      <td className="cell-numeric py-1">{money(line.credit)}</td>
                    </tr>
                  ))}
                </tbody>
              ))}

              <tfoot>
                <tr>
                  <td colSpan={4}>{t('reports.totals')}</td>
                  <td className="cell-numeric">{money(data.totalDebit)}</td>
                  <td className="cell-numeric">{money(data.totalCredit)}</td>
                </tr>
              </tfoot>
            </table>
          </div>

          {/*
            Both totals are shown and explicitly compared. Debits equal credits by
            the voucher aggregate's own invariant, so a mismatch here means
            something has posted incorrectly - and saying so plainly is far better
            than presenting figures that quietly do not add up.
          */}
          <BalanceBadge
            isBalanced={data.totalDebit === data.totalCredit}
            currency={data.currency}
          />
        </div>
      )}
    </ReportFrame>
  );
}
