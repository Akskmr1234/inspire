import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { ReportFrame, moneyAlways } from '@/components/ReportFrame';
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
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ from, to, direction });
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
    <ReportFrame title={t('nav.chequeCalendar')} controls={controls} query={query}>
      {(data) =>
        data.days.length === 0 ? (
          <p className="text-sm text-slate-500">{t('cheques.noDue')}</p>
        ) : (
          <div className="space-y-6">
            {data.currencies.length > 1 && (
              <p className="text-sm text-amber-700 dark:text-amber-400">
                {t('cheques.multiCurrency')}
              </p>
            )}

            {data.days.map((day) => (
              <section key={day.date} className="space-y-2">
                <header className="flex flex-wrap items-baseline justify-between gap-2">
                  <h2 className="font-semibold">{day.date}</h2>
                  <p className="text-sm text-slate-600 dark:text-slate-400">
                    {t('cheques.receivable')}: {moneyAlways(day.receivable)} ·{' '}
                    {t('cheques.payable')}: {moneyAlways(day.payable)} ·{' '}
                    {t('cheques.net')}: {net(day.net)} {data.currency}
                  </p>
                </header>

                <div className="overflow-x-auto">
                  <table className="w-full min-w-[44rem] border-collapse text-sm">
                    <thead>
                      <tr className="border-b border-slate-300 text-left dark:border-slate-700">
                        <th className="py-2 pe-3 font-medium">{t('cheques.chequeNo')}</th>
                        <th className="py-2 pe-3 font-medium">{t('cheques.party')}</th>
                        <th className="py-2 pe-3 font-medium">
                          {t('cheques.direction.label')}
                        </th>
                        <th className="py-2 pe-3 font-medium">{t('cheques.bank')}</th>
                        <th className="py-2 text-end font-medium">{t('cheques.amount')}</th>
                      </tr>
                    </thead>

                    <tbody>
                      {day.cheques.map((cheque) => (
                        <tr
                          key={cheque.chequeId}
                          className="border-b border-slate-100 dark:border-slate-900"
                        >
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
              </section>
            ))}

            <div className="rounded-lg border border-slate-300 px-4 py-3 text-sm dark:border-slate-700">
              <p className="font-semibold">
                {data.from} — {data.to}
              </p>
              <dl className="mt-2 grid grid-cols-2 gap-x-6 gap-y-1 sm:grid-cols-3">
                <dt className="text-slate-600 dark:text-slate-400">
                  {t('cheques.totalReceivable')}
                </dt>
                <dd className="text-end tabular-nums">
                  {moneyAlways(data.totalReceivable)}
                </dd>
                <dt className="text-slate-600 dark:text-slate-400">
                  {t('cheques.totalPayable')}
                </dt>
                <dd className="text-end tabular-nums">
                  {moneyAlways(data.totalPayable)}
                </dd>
                <dt className="text-slate-600 dark:text-slate-400">{t('cheques.net')}</dt>
                <dd className="text-end font-semibold">
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
