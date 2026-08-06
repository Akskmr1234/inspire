import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { ReportFrame, moneyAlways } from '@/components/ReportFrame';
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
  1: 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300',
  2: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300',
  3: 'bg-red-50 text-red-700 line-through dark:bg-red-950 dark:text-red-300',
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
    <span
      className={clsx(
        'inline-block rounded px-2 py-0.5 text-xs font-medium',
        STATUS_STYLES[status],
      )}
    >
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
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ from, to, type, status });
      }}
    >
      <label className="flex flex-col gap-1 text-sm">
        <span className="text-slate-600 dark:text-slate-400">{t('reports.from')}</span>
        <input
          type="date"
          value={from}
          onChange={(event) => setFrom(event.target.value)}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
        />
      </label>

      <label className="flex flex-col gap-1 text-sm">
        <span className="text-slate-600 dark:text-slate-400">{t('reports.to')}</span>
        <input
          type="date"
          value={to}
          onChange={(event) => setTo(event.target.value)}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
        />
      </label>

      <label className="flex flex-col gap-1 text-sm">
        <span className="text-slate-600 dark:text-slate-400">
          {t('reports.voucherType')}
        </span>
        <select
          value={type}
          onChange={(event) => setType(event.target.value)}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
        >
          <option value="">{t('reports.allTypes')}</option>
          {Object.entries(TYPE_NAME).map(([value, name]) => (
            <option key={value} value={value}>
              {t(`voucherTypes.${name}`)}
            </option>
          ))}
        </select>
      </label>

      <label className="flex flex-col gap-1 text-sm">
        <span className="text-slate-600 dark:text-slate-400">{t('reports.status')}</span>
        <select
          value={status}
          onChange={(event) => setStatus(event.target.value)}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
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
        className="rounded-md bg-sky-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-sky-700 disabled:opacity-60"
      >
        {query.isFetching ? t('reports.running') : t('reports.run')}
      </button>
    </form>
  );

  return (
    <ReportFrame title={t('nav.voucherReport')} controls={controls} query={query}>
      {(data) =>
        data.vouchers.length === 0 ? (
          <p className="text-sm text-slate-500">{t('reports.noData')}</p>
        ) : (
          <div className="space-y-4">
            {data.currencies.length > 1 && (
              <p className="text-sm text-amber-700 dark:text-amber-400">
                {t('cheques.multiCurrency')}
              </p>
            )}

            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="text-slate-600 dark:text-slate-400">
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

            <div className="overflow-x-auto">
              <table className="w-full min-w-[60rem] border-collapse text-sm">
                <thead>
                  <tr className="border-b border-slate-300 text-left dark:border-slate-700">
                    <th className="py-2 pe-3 font-medium">{t('reports.date')}</th>
                    <th className="py-2 pe-3 font-medium">{t('reports.voucherNo')}</th>
                    <th className="py-2 pe-3 font-medium">{t('reports.voucherType')}</th>
                    <th className="py-2 pe-3 font-medium">{t('reports.status')}</th>
                    <th className="py-2 pe-3 font-medium">{t('reports.reference')}</th>
                    <th className="py-2 pe-3 font-medium">{t('reports.particulars')}</th>
                    <th className="py-2 text-end font-medium">{t('reports.amount')}</th>
                  </tr>
                </thead>

                <tbody>
                  {data.vouchers.map((voucher) => (
                    <tr
                      key={voucher.voucherId}
                      className="border-b border-slate-100 dark:border-slate-900"
                    >
                      <td className="py-1 pe-3 text-slate-600 dark:text-slate-400">
                        {voucher.date}
                      </td>
                      <td className="py-1 pe-3 font-medium">{voucher.voucherNumber}</td>
                      <td className="py-1 pe-3">
                        {t(`voucherTypes.${TYPE_NAME[voucher.type]}`)}
                      </td>
                      <td className="py-1 pe-3">
                        <StatusBadge status={voucher.status} />
                      </td>
                      <td className="py-1 pe-3 text-slate-600 dark:text-slate-400">
                        {voucher.referenceNumber ?? ''}
                      </td>
                      <td className="py-1 pe-3 text-slate-600 dark:text-slate-400">
                        {voucher.narration ?? ''}
                      </td>
                      <td className="py-1 text-end tabular-nums">
                        {moneyAlways(voucher.documentAmount)}{' '}
                        <span className="text-slate-400">{voucher.currency}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <dl className="grid max-w-xs grid-cols-2 gap-x-6 gap-y-1 text-sm">
              <dt className="text-slate-600 dark:text-slate-400">{t('reports.totals')}</dt>
              <dd className="text-end font-medium tabular-nums">
                {moneyAlways(data.totalBaseAmount)} {data.currency}
              </dd>
            </dl>
          </div>
        )
      }
    </ReportFrame>
  );
}
