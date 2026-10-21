import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import type { ApiError } from '@/lib/api';
import {
  createFinancialYear,
  FinancialYearStatus,
  listFinancialYears,
  setFinancialYearOpen,
  updateFinancialYear,
  type FinancialYearInput,
  type FinancialYearSummary,
} from '@/lib/financialYears';
import { useSession } from '@/stores/session';

/**
 * The firm's financial years: add one, move its dates, close it to postings.
 *
 * A year is the period every document is numbered and posted into, so the server
 * refuses dates that would strand a document outside its year, and a closed year takes
 * no postings until it is opened again.
 */
export function FinancialYearsSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const editable = useSession((state) => state.can)('platform:setting:edit');

  const [editing, setEditing] = useState<FinancialYearSummary | 'new' | null>(null);
  const [error, setError] = useState<string | null>(null);

  const years = useQuery<readonly FinancialYearSummary[], ApiError>({
    queryKey: ['financial-years'],
    queryFn: listFinancialYears,
  });

  const mutation = useMutation<unknown, ApiError, () => Promise<unknown>>({
    mutationFn: (action) => action(),
    onSuccess: async () => {
      setError(null);
      setEditing(null);
      await queryClient.invalidateQueries({ queryKey: ['financial-years'] });
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const statusLabel = (status: number): string =>
    status === FinancialYearStatus.open
      ? t('years.open')
      : status === FinancialYearStatus.closed
        ? t('years.closed')
        : t('years.locked');

  return (
    <div className="space-y-3">
      {error && <p className="alert-warn text-sm">{error}</p>}

      <div className="overflow-x-auto rounded-lg border border-line">
        <table className="w-full text-sm">
          <thead className="bg-surface-2 text-xs text-ink-muted uppercase">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">{t('years.code')}</th>
              <th className="px-3 py-2 text-start font-semibold">{t('years.start')}</th>
              <th className="px-3 py-2 text-start font-semibold">{t('years.end')}</th>
              <th className="px-3 py-2 text-start font-semibold">{t('years.status')}</th>
              <th className="px-3 py-2" />
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {(years.data ?? []).map((year) => (
              <tr key={year.financialYearId}>
                <td className="px-3 py-2 font-medium">{year.code}</td>
                <td className="px-3 py-2 font-mono text-xs">{year.startDate}</td>
                <td className="px-3 py-2 font-mono text-xs">{year.endDate}</td>
                <td className="px-3 py-2">
                  <span
                    className={clsx(
                      'rounded px-2 py-0.5 text-xs',
                      year.status === FinancialYearStatus.open
                        ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'
                        : 'bg-surface-3 text-ink-muted',
                    )}
                  >
                    {statusLabel(year.status)}
                  </span>
                </td>
                <td className="px-3 py-2 text-end whitespace-nowrap">
                  {editable && year.status !== FinancialYearStatus.locked && (
                    <span className="inline-flex gap-1">
                      <button
                        type="button"
                        onClick={() => setEditing(year)}
                        className="btn-secondary btn-xs"
                      >
                        {t('years.edit')}
                      </button>
                      <button
                        type="button"
                        disabled={mutation.isPending}
                        onClick={() =>
                          mutation.mutate(() =>
                            setFinancialYearOpen(
                              year.financialYearId,
                              year.status !== FinancialYearStatus.open,
                            ),
                          )
                        }
                        className="btn-secondary btn-xs"
                      >
                        {year.status === FinancialYearStatus.open
                          ? t('years.close')
                          : t('years.reopen')}
                      </button>
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {editing !== null && (
        <YearForm
          initial={editing === 'new' ? null : editing}
          busy={mutation.isPending}
          onCancel={() => {
            setEditing(null);
            setError(null);
          }}
          onSubmit={(input) =>
            mutation.mutate(() =>
              editing === 'new'
                ? createFinancialYear(input)
                : updateFinancialYear(editing.financialYearId, input),
            )
          }
        />
      )}

      {editable && editing === null && (
        <button
          type="button"
          onClick={() => setEditing('new')}
          className="btn-secondary btn-sm"
        >
          {t('years.add')}
        </button>
      )}
    </div>
  );
}

function YearForm({
  initial,
  busy,
  onCancel,
  onSubmit,
}: {
  readonly initial: FinancialYearSummary | null;
  readonly busy: boolean;
  readonly onCancel: () => void;
  readonly onSubmit: (input: FinancialYearInput) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState(initial?.code ?? '');
  const [startDate, setStartDate] = useState(initial?.startDate ?? '');
  const [endDate, setEndDate] = useState(initial?.endDate ?? '');

  return (
    <form
      className="grid grid-cols-1 gap-3 rounded-lg border border-line bg-surface-2 p-3 sm:grid-cols-4"
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit({ code: code.trim(), startDate, endDate });
      }}
    >
      <label className="field">
        <span className="field-label">{t('years.code')}</span>
        <input
          value={code}
          onChange={(event) => setCode(event.target.value)}
          placeholder="2027"
          className="field-input-sm"
          required
        />
      </label>
      <label className="field">
        <span className="field-label">{t('years.start')}</span>
        <input
          type="date"
          value={startDate}
          onChange={(event) => setStartDate(event.target.value)}
          className="field-input-sm"
          required
        />
      </label>
      <label className="field">
        <span className="field-label">{t('years.end')}</span>
        <input
          type="date"
          value={endDate}
          onChange={(event) => setEndDate(event.target.value)}
          className="field-input-sm"
          required
        />
      </label>
      <div className="flex items-end gap-2">
        <button type="submit" disabled={busy} className="btn-primary btn-sm">
          {t('years.save')}
        </button>
        <button type="button" onClick={onCancel} className="btn-secondary btn-sm">
          {t('products.cancel')}
        </button>
      </div>
    </form>
  );
}
