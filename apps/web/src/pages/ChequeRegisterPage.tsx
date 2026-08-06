import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ReportFrame, moneyAlways } from '@/components/ReportFrame';
import { DirectionBadge, StatusBadge } from '@/components/ChequeBadges';
import { request, type ApiError } from '@/lib/api';
import {
  CHEQUE_DIRECTIONS,
  CHEQUE_STATUSES,
  type ChequeReportLine,
} from '@/lib/cheques';

interface ChequeRegister {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly cheques: readonly ChequeReportLine[];
  readonly totalReceived: number;
  readonly totalIssued: number;
  /** Keyed by the status's wire value as a string, e.g. `{ "1": 4, "3": 2 }`. */
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
 * Reads a status's count out of the server's tally.
 *
 * The tally is a dictionary keyed by the status enum, and System.Text.Json's
 * choice of key form for an enum-keyed dictionary has shifted between framework
 * versions - the numeric value in some, the name in others. Looking under both
 * keeps the summary correct either way rather than silently reading every count
 * as zero the day the server's serialiser changes its mind.
 */
function countFor(
  counts: Readonly<Record<string, number>>,
  status: { readonly value: number; readonly name: string },
): number {
  return counts[String(status.value)] ?? counts[status.name] ?? 0;
}

/**
 * The cheque register: every cheque taken in or written out over a period.
 *
 * Read by when a cheque changed hands, not by when it falls due, and it shows
 * closed cheques as well as live ones - a register that dropped a cheque the moment
 * it cleared could not answer the question it is usually opened for. It pages from
 * the newest end, because that is where a reader looking for something recent
 * starts.
 */
export function ChequeRegisterPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [from, setFrom] = useState(startOfMonth());
  const [to, setTo] = useState(today());
  const [direction, setDirection] = useState('');
  const [status, setStatus] = useState('');
  const [criteria, setCriteria] = useState({
    from: startOfMonth(),
    to: today(),
    direction: '',
    status: '',
  });

  const query = useQuery<ChequeRegister, ApiError>({
    queryKey: [
      'cheque-register',
      criteria.from,
      criteria.to,
      criteria.direction,
      criteria.status,
    ],
    queryFn: () => {
      const params = new URLSearchParams({ from: criteria.from, to: criteria.to });

      if (criteria.direction) {
        params.set('direction', criteria.direction);
      }

      if (criteria.status) {
        params.set('status', criteria.status);
      }

      return request<ChequeRegister>(
        `/accounting/reports/cheque-register?${params.toString()}`,
      );
    },
  });

  const controls = (
    <form
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ from, to, direction, status });
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
          {t('cheques.direction.label')}
        </span>
        <select
          value={direction}
          onChange={(event) => setDirection(event.target.value)}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
        >
          <option value="">{t('cheques.allDirections')}</option>
          {CHEQUE_DIRECTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {t(`cheques.direction.${option.name}`)}
            </option>
          ))}
        </select>
      </label>

      <label className="flex flex-col gap-1 text-sm">
        <span className="text-slate-600 dark:text-slate-400">
          {t('cheques.status.label')}
        </span>
        <select
          value={status}
          onChange={(event) => setStatus(event.target.value)}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
        >
          <option value="">{t('cheques.allStatuses')}</option>
          {CHEQUE_STATUSES.map((option) => (
            <option key={option.value} value={option.value}>
              {t(`cheques.status.${option.name}`)}
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
    <ReportFrame title={t('nav.chequeRegister')} controls={controls} query={query}>
      {(data) =>
        data.cheques.length === 0 ? (
          <p className="text-sm text-slate-500">{t('cheques.noCheques')}</p>
        ) : (
          <div className="space-y-4">
            {data.currencies.length > 1 && (
              <p className="text-sm text-amber-700 dark:text-amber-400">
                {t('cheques.multiCurrency')}
              </p>
            )}

            {/* Where the period's cheques stand, at a glance, before the detail. */}
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="text-slate-600 dark:text-slate-400">
                {t('cheques.countByStatus')}:
              </span>
              {CHEQUE_STATUSES.filter(
                (option) => countFor(data.countByStatus, option) > 0,
              ).map((option) => (
                <span key={option.value} className="flex items-center gap-1">
                  <StatusBadge status={option.value} />
                  <span className="tabular-nums">
                    {countFor(data.countByStatus, option)}
                  </span>
                </span>
              ))}
            </div>

            <div className="overflow-x-auto">
              <table className="w-full min-w-[60rem] border-collapse text-sm">
                <thead>
                  <tr className="border-b border-slate-300 text-left dark:border-slate-700">
                    <th className="py-2 pe-3 font-medium">{t('cheques.recordedOn')}</th>
                    <th className="py-2 pe-3 font-medium">{t('cheques.chequeNo')}</th>
                    <th className="py-2 pe-3 font-medium">{t('cheques.party')}</th>
                    <th className="py-2 pe-3 font-medium">
                      {t('cheques.direction.label')}
                    </th>
                    <th className="py-2 pe-3 font-medium">{t('cheques.status.label')}</th>
                    <th className="py-2 pe-3 font-medium">{t('cheques.instrumentDate')}</th>
                    <th className="py-2 pe-3 font-medium">{t('cheques.bank')}</th>
                    <th className="py-2 text-end font-medium">{t('cheques.amount')}</th>
                  </tr>
                </thead>

                <tbody>
                  {data.cheques.map((cheque) => (
                    <tr
                      key={cheque.chequeId}
                      className="border-b border-slate-100 dark:border-slate-900"
                    >
                      <td className="py-1 pe-3 text-slate-600 dark:text-slate-400">
                        {cheque.recordedOn}
                      </td>
                      <td className="py-1 pe-3 font-medium">{cheque.chequeNumber}</td>
                      <td className="py-1 pe-3">
                        <span className="text-slate-500 dark:text-slate-500">
                          {cheque.partyCode}
                        </span>{' '}
                        {cheque.partyName}
                      </td>
                      <td className="py-1 pe-3">
                        <DirectionBadge direction={cheque.direction} />
                      </td>
                      <td className="py-1 pe-3">
                        <StatusBadge status={cheque.status} />
                      </td>
                      <td className="py-1 pe-3 text-slate-600 dark:text-slate-400">
                        {cheque.instrumentDate}
                      </td>
                      <td className="py-1 pe-3 text-slate-600 dark:text-slate-400">
                        {cheque.bankName ?? ''}
                      </td>
                      <td className="py-1 text-end tabular-nums">
                        {moneyAlways(cheque.amount)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <dl className="grid max-w-sm grid-cols-2 gap-x-6 gap-y-1 text-sm">
              <dt className="text-slate-600 dark:text-slate-400">
                {t('cheques.totalReceived')}
              </dt>
              <dd className="text-end font-medium tabular-nums text-emerald-700 dark:text-emerald-400">
                {moneyAlways(data.totalReceived)} {data.currency}
              </dd>
              <dt className="text-slate-600 dark:text-slate-400">
                {t('cheques.totalIssued')}
              </dt>
              <dd className="text-end font-medium tabular-nums text-amber-700 dark:text-amber-400">
                {moneyAlways(data.totalIssued)} {data.currency}
              </dd>
            </dl>
          </div>
        )
      }
    </ReportFrame>
  );
}
