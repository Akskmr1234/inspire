import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { EmptyState, ReportFrame, Spinner, moneyAlways } from '@/components/ReportFrame';
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
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ asAt, direction });
      }}
    >
      <label className="field">
        <span className="field-label">{t('reports.asAt')}</span>
        <input
          type="date"
          value={asAt}
          onChange={(event) => setAsAt(event.target.value)}
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
    <ReportFrame title={t('nav.postDatedCheques')} controls={controls} query={query}>
      {(data) =>
        data.cheques.length === 0 ? (
          <EmptyState message={t('cheques.noPostDated')} />
        ) : (
          <div className="space-y-4">
            {data.currencies.length > 1 && (
              <p className="alert-warn">{t('cheques.multiCurrency')}</p>
            )}

            <div className="table-wrap table-wrap-tall">
              <table className="table min-w-[52rem]">
                <thead>
                  <tr>
                    <th className="text-start">{t('cheques.chequeNo')}</th>
                    <th className="text-start">{t('cheques.party')}</th>
                    <th className="text-start">{t('cheques.direction.label')}</th>
                    <th className="text-start">{t('cheques.dueDate')}</th>
                    <th className="text-end">{t('cheques.daysToRun')}</th>
                    <th className="text-start">{t('cheques.bank')}</th>
                    <th className="text-end">{t('cheques.amount')}</th>
                  </tr>
                </thead>

                <tbody>
                  {data.cheques.map((cheque) => (
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
                      <td className="py-1.5 text-ink-muted whitespace-nowrap">
                        {cheque.instrumentDate}
                      </td>
                      <td className="cell-numeric py-1.5">
                        {cheque.daysUntilDue === 0
                          ? t('cheques.dueNow')
                          : cheque.daysUntilDue}
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
              <dt className="text-ink-muted">{t('cheques.totalReceivable')}</dt>
              <dd className="text-end font-mono font-semibold tabular-nums text-emerald-700 dark:text-emerald-400">
                {moneyAlways(data.totalReceivable)} {data.currency}
              </dd>
              <dt className="text-ink-muted">{t('cheques.totalPayable')}</dt>
              <dd className="text-end font-mono font-semibold tabular-nums text-amber-700 dark:text-amber-400">
                {moneyAlways(data.totalPayable)} {data.currency}
              </dd>
            </dl>
          </div>
        )
      }
    </ReportFrame>
  );
}
