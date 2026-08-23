import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { EmptyState, ReportFrame, Spinner, moneyAlways } from '@/components/ReportFrame';
import { DirectionBadge, StatusBadge } from '@/components/ChequeBadges';
import { request, type ApiError } from '@/lib/api';
import { CHEQUE_DIRECTIONS, CHEQUE_STATUSES, type ChequeReportLine } from '@/lib/cheques';

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
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ from, to, direction, status });
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
        <span className="field-label">{t('cheques.direction.label')}</span>
        <select
          value={direction}
          onChange={(event) => setDirection(event.target.value)}
          className="field-input-sm"
        >
          <option value="">{t('cheques.allDirections')}</option>
          {CHEQUE_DIRECTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {t(`cheques.direction.${option.name}`)}
            </option>
          ))}
        </select>
      </label>

      <label className="field">
        <span className="field-label">{t('cheques.status.label')}</span>
        <select
          value={status}
          onChange={(event) => setStatus(event.target.value)}
          className="field-input-sm"
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
        className="btn-primary btn-sm py-1.5"
      >
        {query.isFetching && <Spinner />}
        {query.isFetching ? t('reports.running') : t('reports.run')}
      </button>
    </form>
  );

  return (
    <ReportFrame title={t('nav.chequeRegister')} controls={controls} query={query}>
      {(data) =>
        data.cheques.length === 0 ? (
          <EmptyState message={t('cheques.noCheques')} />
        ) : (
          <div className="space-y-4">
            {data.currencies.length > 1 && (
              <p className="alert-warn">{t('cheques.multiCurrency')}</p>
            )}

            {/* Where the period's cheques stand, at a glance, before the detail. */}
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="text-ink-muted">{t('cheques.countByStatus')}:</span>
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

            <div className="table-wrap table-wrap-tall">
              <table className="table min-w-[60rem]">
                <thead>
                  <tr>
                    <th className="text-start">{t('cheques.recordedOn')}</th>
                    <th className="text-start">{t('cheques.chequeNo')}</th>
                    <th className="text-start">{t('cheques.party')}</th>
                    <th className="text-start">{t('cheques.direction.label')}</th>
                    <th className="text-start">{t('cheques.status.label')}</th>
                    <th className="text-start">{t('cheques.instrumentDate')}</th>
                    <th className="text-start">{t('cheques.bank')}</th>
                    <th className="text-end">{t('cheques.amount')}</th>
                  </tr>
                </thead>

                <tbody>
                  {data.cheques.map((cheque) => (
                    <tr key={cheque.chequeId}>
                      <td className="py-1.5 text-ink-muted whitespace-nowrap">
                        {cheque.recordedOn}
                      </td>
                      <td className="py-1.5 font-medium whitespace-nowrap">
                        {cheque.chequeNumber}
                      </td>
                      <td className="py-1.5">
                        <span className="text-ink-subtle">{cheque.partyCode}</span>{' '}
                        {cheque.partyName}
                      </td>
                      <td className="py-1.5">
                        <DirectionBadge direction={cheque.direction} />
                      </td>
                      <td className="py-1.5">
                        <StatusBadge status={cheque.status} />
                      </td>
                      <td className="py-1.5 text-ink-muted whitespace-nowrap">
                        {cheque.instrumentDate}
                      </td>
                      <td className="py-1.5 text-ink-muted">{cheque.bankName ?? ''}</td>
                      <td className="cell-numeric py-1.5">
                        {moneyAlways(cheque.amount)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <dl className="panel grid max-w-sm grid-cols-2 gap-x-6 gap-y-1.5 text-sm">
              <dt className="text-ink-muted">{t('cheques.totalReceived')}</dt>
              <dd className="text-end font-mono font-semibold tabular-nums text-emerald-700 dark:text-emerald-400">
                {moneyAlways(data.totalReceived)} {data.currency}
              </dd>
              <dt className="text-ink-muted">{t('cheques.totalIssued')}</dt>
              <dd className="text-end font-mono font-semibold tabular-nums text-amber-700 dark:text-amber-400">
                {moneyAlways(data.totalIssued)} {data.currency}
              </dd>
            </dl>
          </div>
        )
      }
    </ReportFrame>
  );
}
