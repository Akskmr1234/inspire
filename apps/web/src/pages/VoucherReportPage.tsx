import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { EmptyState, ReportFrame, Spinner, moneyAlways } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';

/** The voucher statuses, keyed by the wire value the API serialises them as. */
const STATUS_NAME: Record<number, string> = { 1: 'Draft', 2: 'Posted', 3: 'Cancelled' };

/** The voucher types, keyed by the wire value, for the `voucherTypes.<name>` lookup. */
const TYPE_NAME: Record<number, string> = {
  1: 'CashReceipt',
  2: 'BankReceipt',
  3: 'CashPayment',
  4: 'BankPayment',
  5: 'Journal',
  6: 'Contra',
};

const STATUS_STYLES: Record<number, string> = {
  1: 'bg-surface-3 text-ink-muted',
  2: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
  3: 'bg-red-50 text-red-700 line-through dark:bg-red-500/15 dark:text-red-300',
};

interface VoucherReportLine {
  readonly voucherId: string;
  readonly date: string;
  readonly voucherNumber: string;
  readonly type: number;
  readonly status: number;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly currency: string;
  readonly exchangeRate: number;
  readonly documentAmount: number;
  readonly baseAmount: number;
}

interface VoucherReport {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly vouchers: readonly VoucherReportLine[];
  readonly voucherCount: number;
  readonly totalBaseAmount: number;
  /** Keyed by the status's wire value as a string, e.g. `{ "2": 5, "3": 1 }`. */
  readonly countByStatus: Readonly<Record<string, number>>;
  readonly currencies: readonly string[];
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function startOfMonth(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`;
}

/**
 * Reads a status's count from the server's tally, under either key form.
 *
 * System.Text.Json has serialised enum dictionary keys as the numeric value in some
 * framework versions and the name in others; looking under both keeps the summary
 * right rather than silently reading every count as zero.
 */
function countFor(counts: Readonly<Record<string, number>>, value: number): number {
  const name = STATUS_NAME[value];

  return counts[String(value)] ?? (name === undefined ? undefined : counts[name]) ?? 0;
}

/** A small coloured pill for a voucher's status. */
function StatusBadge({ status }: { readonly status: number }): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <span className={clsx('badge', STATUS_STYLES[status])}>
      {t(`voucherStatus.${STATUS_NAME[status]}`)}
    </span>
  );
}

/**
 * The voucher report: a register of vouchers by document.
 *
 * Where the day book reads a period line by line and posted-only, this reads it
 * document by document and across every status - the drafts still to be posted, the
 * cancelled entry somebody is asking about. Amounts are shown in each voucher's own
 * currency and totalled in the base currency, the one figure that sums across them.
 */
export function VoucherReportPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [from, setFrom] = useState(startOfMonth());
  const [to, setTo] = useState(today());
  const [type, setType] = useState('');
  const [status, setStatus] = useState('');
  const [criteria, setCriteria] = useState({
    from: startOfMonth(),
    to: today(),
    type: '',
    status: '',
  });

  const query = useQuery<VoucherReport, ApiError>({
    queryKey: [
      'voucher-report',
      criteria.from,
      criteria.to,
      criteria.type,
      criteria.status,
    ],
    queryFn: () => {
      const params = new URLSearchParams({ from: criteria.from, to: criteria.to });

      if (criteria.type) {
        params.set('type', criteria.type);
      }

      if (criteria.status) {
        params.set('status', criteria.status);
      }

      return request<VoucherReport>(
        `/accounting/reports/voucher-report?${params.toString()}`,
      );
    },
  });

  const controls = (
    <form
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ from, to, type, status });
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
          value={type}
          onChange={(event) => setType(event.target.value)}
          className="field-input-sm"
        >
          <option value="">{t('reports.allTypes')}</option>
          {Object.entries(TYPE_NAME).map(([value, name]) => (
            <option key={value} value={value}>
              {t(`voucherTypes.${name}`)}
            </option>
          ))}
        </select>
      </label>

      <label className="field">
        <span className="field-label">{t('reports.status')}</span>
        <select
          value={status}
          onChange={(event) => setStatus(event.target.value)}
          className="field-input-sm"
        >
          <option value="">{t('reports.allStatuses')}</option>
          {Object.entries(STATUS_NAME).map(([value, name]) => (
            <option key={value} value={value}>
              {t(`voucherStatus.${name}`)}
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
    <ReportFrame title={t('nav.voucherReport')} controls={controls} query={query}>
      {(data) =>
        data.vouchers.length === 0 ? (
          <EmptyState message={t('reports.noData')} />
        ) : (
          <div className="space-y-4">
            {data.currencies.length > 1 && (
              <p className="alert-warn">{t('cheques.multiCurrency')}</p>
            )}

            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="text-ink-muted">
                {t('reports.voucherCount', { count: data.voucherCount })}:
              </span>
              {Object.keys(STATUS_NAME)
                .map(Number)
                .filter((value) => countFor(data.countByStatus, value) > 0)
                .map((value) => (
                  <span key={value} className="flex items-center gap-1">
                    <StatusBadge status={value} />
                    <span className="tabular-nums">
                      {countFor(data.countByStatus, value)}
                    </span>
                  </span>
                ))}
            </div>

            <div className="table-wrap table-wrap-tall">
              <table className="table min-w-[60rem]">
                <thead>
                  <tr>
                    <th className="text-start">{t('reports.date')}</th>
                    <th className="text-start">{t('reports.voucherNo')}</th>
                    <th className="text-start">{t('reports.voucherType')}</th>
                    <th className="text-start">{t('reports.status')}</th>
                    <th className="text-start">{t('reports.reference')}</th>
                    <th className="text-start">{t('reports.particulars')}</th>
                    <th className="text-end">{t('reports.amount')}</th>
                  </tr>
                </thead>

                <tbody>
                  {data.vouchers.map((voucher) => (
                    <tr key={voucher.voucherId}>
                      <td className="py-1.5 text-ink-muted whitespace-nowrap">
                        {voucher.date}
                      </td>
                      <td className="py-1.5 font-medium whitespace-nowrap">
                        {voucher.voucherNumber}
                      </td>
                      <td className="py-1.5 whitespace-nowrap">
                        {t(`voucherTypes.${TYPE_NAME[voucher.type]}`)}
                      </td>
                      <td className="py-1.5">
                        <StatusBadge status={voucher.status} />
                      </td>
                      <td className="py-1.5 text-ink-muted">
                        {voucher.referenceNumber ?? ''}
                      </td>
                      <td className="py-1.5 text-ink-muted">{voucher.narration ?? ''}</td>
                      <td className="cell-numeric py-1.5">
                        {moneyAlways(voucher.documentAmount)}{' '}
                        <span className="text-ink-subtle">{voucher.currency}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <dl className="panel grid max-w-sm grid-cols-2 gap-x-6 gap-y-1 text-sm">
              <dt className="text-ink-muted">{t('reports.totals')}</dt>
              <dd className="text-end font-mono font-semibold tabular-nums">
                {moneyAlways(data.totalBaseAmount)} {data.currency}
              </dd>
            </dl>
          </div>
        )
      }
    </ReportFrame>
  );
}
