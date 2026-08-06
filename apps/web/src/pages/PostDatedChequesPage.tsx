import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ReportFrame, moneyAlways } from '@/components/ReportFrame';
import { DirectionBadge } from '@/components/ChequeBadges';
import { request, type ApiError } from '@/lib/api';
import { CHEQUE_DIRECTIONS, type ChequeReportLine } from '@/lib/cheques';

interface PostDatedCheques {
  readonly asAt: string;
  readonly currency: string;
  readonly cheques: readonly ChequeReportLine[];
  readonly totalReceivable: number;
  readonly totalPayable: number;
  readonly currencies: readonly string[];
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * The post-dated cheque report: what is still in hand and has not yet fallen due.
 *
 * What a treasurer reads to know what is coming and a credit controller reads to
 * know what has not arrived. Only pending cheques dated after the reporting date
 * appear - one already with the bank is no longer a promise but an outcome, and
 * belongs on the register.
 */
export function PostDatedChequesPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [asAt, setAsAt] = useState(today());
  const [direction, setDirection] = useState('');
  const [criteria, setCriteria] = useState({ asAt: today(), direction: '' });

  const query = useQuery<PostDatedCheques, ApiError>({
    queryKey: ['post-dated-cheques', criteria.asAt, criteria.direction],
    queryFn: () => {
      const params = new URLSearchParams({ asAt: criteria.asAt });

      if (criteria.direction) {
        params.set('direction', criteria.direction);
      }

      return request<PostDatedCheques>(
        `/accounting/reports/post-dated-cheques?${params.toString()}`,
      );
    },
  });

  const controls = (
    <form
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ asAt, direction });
      }}
    >
      <label className="flex flex-col gap-1 text-sm">
        <span className="text-slate-600 dark:text-slate-400">{t('reports.asAt')}</span>
        <input
          type="date"
          value={asAt}
          onChange={(event) => setAsAt(event.target.value)}
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
    <ReportFrame title={t('nav.postDatedCheques')} controls={controls} query={query}>
      {(data) =>
        data.cheques.length === 0 ? (
          <p className="text-sm text-slate-500">{t('cheques.noPostDated')}</p>
        ) : (
          <div className="space-y-4">
            {data.currencies.length > 1 && (
              <p className="text-sm text-amber-700 dark:text-amber-400">
                {t('cheques.multiCurrency')}
              </p>
            )}

            <div className="overflow-x-auto">
              <table className="w-full min-w-[52rem] border-collapse text-sm">
                <thead>
                  <tr className="border-b border-slate-300 text-left dark:border-slate-700">
                    <th className="py-2 pe-3 font-medium">{t('cheques.chequeNo')}</th>
                    <th className="py-2 pe-3 font-medium">{t('cheques.party')}</th>
                    <th className="py-2 pe-3 font-medium">
                      {t('cheques.direction.label')}
                    </th>
                    <th className="py-2 pe-3 font-medium">{t('cheques.dueDate')}</th>
                    <th className="py-2 pe-3 text-end font-medium">
                      {t('cheques.daysToRun')}
                    </th>
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
                        {cheque.instrumentDate}
                      </td>
                      <td className="py-1 pe-3 text-end tabular-nums">
                        {cheque.daysUntilDue === 0
                          ? t('cheques.dueNow')
                          : cheque.daysUntilDue}
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
                {t('cheques.totalReceivable')}
              </dt>
              <dd className="text-end font-medium tabular-nums text-emerald-700 dark:text-emerald-400">
                {moneyAlways(data.totalReceivable)} {data.currency}
              </dd>
              <dt className="text-slate-600 dark:text-slate-400">
                {t('cheques.totalPayable')}
              </dt>
              <dd className="text-end font-medium tabular-nums text-amber-700 dark:text-amber-400">
                {moneyAlways(data.totalPayable)} {data.currency}
              </dd>
            </dl>
          </div>
        )
      }
    </ReportFrame>
  );
}
