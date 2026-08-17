import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { EmptyState, ReportFrame, Spinner, moneyAlways } from '@/components/ReportFrame';
import { DirectionBadge } from '@/components/ChequeBadges';
import { request, type ApiError } from '@/lib/api';
import { CHEQUE_DIRECTIONS, type ChequeReportLine } from '@/lib/cheques';

interface ChequeCalendarDay {
  readonly date: string;
  readonly receivable: number;
  readonly payable: number;
  readonly net: number;
  readonly cheques: readonly ChequeReportLine[];
}

interface ChequeCalendar {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly days: readonly ChequeCalendarDay[];
  readonly totalReceivable: number;
  readonly totalPayable: number;
  readonly currencies: readonly string[];
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function startOfMonth(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`;
}

/** A signed figure, red when it takes money out of the position, green when it brings it in. */
function net(value: number): React.JSX.Element {
  return (
    <span
      className={clsx(
        'tabular-nums',
        value < 0
          ? 'text-red-700 dark:text-red-400'
          : 'text-emerald-700 dark:text-emerald-400',
      )}
    >
      {moneyAlways(value)}
    </span>
  );
}

/**
 * The PDC calendar: the same pending cheques as the post-dated report, arranged by
 * the day they fall due rather than by party.
 *
 * The question is different - not who owes what, but what lands this week and
 * whether the account can carry it - so the figure that leads each day is the net:
 * receivable in less payable out. Only days with cheques on them appear.
 */
export function ChequeCalendarPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [from, setFrom] = useState(startOfMonth());
  const [to, setTo] = useState(today());
  const [direction, setDirection] = useState('');
  const [criteria, setCriteria] = useState({
    from: startOfMonth(),
    to: today(),
    direction: '',
  });

  const query = useQuery<ChequeCalendar, ApiError>({
    queryKey: ['cheque-calendar', criteria.from, criteria.to, criteria.direction],
    queryFn: () => {
      const params = new URLSearchParams({ from: criteria.from, to: criteria.to });

      if (criteria.direction) {
        params.set('direction', criteria.direction);
      }

      return request<ChequeCalendar>(
        `/accounting/reports/cheque-calendar?${params.toString()}`,
      );
    },
  });

  const controls = (
    <form
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ from, to, direction });
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
    <ReportFrame title={t('nav.chequeCalendar')} controls={controls} query={query}>
      {(data) =>
        data.days.length === 0 ? (
          <EmptyState message={t('cheques.noDue')} />
        ) : (
          <div className="space-y-4">
            {data.currencies.length > 1 && (
              <p className="alert-warn">{t('cheques.multiCurrency')}</p>
            )}

            {data.days.map((day) => (
              <section key={day.date} className="card overflow-hidden">
                <header className="flex flex-wrap items-baseline justify-between gap-2 border-b border-line bg-surface-3 px-4 py-3">
                  <h2 className="font-semibold text-ink">{day.date}</h2>
                  <p className="text-sm text-ink-muted">
                    {t('cheques.receivable')}: {moneyAlways(day.receivable)} ·{' '}
                    {t('cheques.payable')}: {moneyAlways(day.payable)} ·{' '}
                    {t('cheques.net')}: {net(day.net)} {data.currency}
                  </p>
                </header>

                <div className="overflow-x-auto">
                  <table className="table min-w-[44rem]">
                    <thead>
                      <tr>
                        <th className="text-start">{t('cheques.chequeNo')}</th>
                        <th className="text-start">{t('cheques.party')}</th>
                        <th className="text-start">{t('cheques.direction.label')}</th>
                        <th className="text-start">{t('cheques.bank')}</th>
                        <th className="text-end">{t('cheques.amount')}</th>
                      </tr>
                    </thead>

                    <tbody>
                      {day.cheques.map((cheque) => (
                        <tr key={cheque.chequeId}>
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
                          <td className="py-1.5 text-ink-muted">
                            {cheque.bankName ?? ''}
                          </td>
                          <td className="cell-numeric py-1.5">
                            {moneyAlways(cheque.amount)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </section>
            ))}

            <div className="panel text-sm">
              <p className="font-semibold text-ink">
                {data.from} — {data.to}
              </p>
              <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-1.5 sm:grid-cols-3">
                <dt className="text-ink-muted">{t('cheques.totalReceivable')}</dt>
                <dd className="text-end font-mono tabular-nums">
                  {moneyAlways(data.totalReceivable)}
                </dd>
                <dt className="text-ink-muted">{t('cheques.totalPayable')}</dt>
                <dd className="text-end font-mono tabular-nums">
                  {moneyAlways(data.totalPayable)}
                </dd>
                <dt className="text-ink-muted">{t('cheques.net')}</dt>
                <dd className="text-end font-mono font-semibold tabular-nums">
                  {net(data.totalReceivable - data.totalPayable)} {data.currency}
                </dd>
              </dl>
            </div>
          </div>
        )
      }
    </ReportFrame>
  );
}
