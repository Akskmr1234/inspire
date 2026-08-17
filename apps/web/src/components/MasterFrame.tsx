import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { ReportFrame } from '@/components/ReportFrame';
import type { ApiError } from '@/lib/api';

/**
 * The chrome the four inventory master screens share: the list, the include-withdrawn
 * toggle, the add form, and the plumbing that refreshes one after the other.
 *
 * Extracted after the second of the four rather than the first. They differ in their
 * columns and in what an add form asks for, and nothing else — so the part that varies
 * is passed in and the part that does not is written once. Repeating the mutation and
 * invalidation per screen is how three of them end up refreshing and the fourth does
 * not.
 */
export function MasterFrame<TRow>({
  title,
  queryKey,
  fetchRows,
  columns,
  rowKey,
  addForm,
}: {
  readonly title: string;
  readonly queryKey: string;
  readonly fetchRows: (includeInactive: boolean) => Promise<readonly TRow[]>;
  /** Built with a runner, so a row's own actions can invoke a mutation. */
  readonly columns: (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
  ) => readonly GridColumn<TRow>[];
  readonly rowKey: (row: TRow) => string;
  /** The fields this master asks for when adding a record. */
  readonly addForm: (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
    rows: readonly TRow[],
  ) => React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [includeInactive, setIncludeInactive] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const query = useQuery<readonly TRow[], ApiError>({
    queryKey: [queryKey, includeInactive],
    queryFn: () => fetchRows(includeInactive),
  });

  const mutation = useMutation<void, ApiError, () => Promise<void>>({
    mutationFn: (action) => action(),
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: [queryKey] });
    },
    // The server owns the rules — a default warehouse refusing withdrawal, a code
    // already taken — so its message is shown rather than one guessed at here.
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const run = (action: () => Promise<void>): void => {
    setError(null);
    mutation.mutate(action);
  };

  const controls = (
    <label className="field-check pb-1">
      <input
        type="checkbox"
        checked={includeInactive}
        onChange={(event) => setIncludeInactive(event.target.checked)}
      />
      {t('masters.includeWithdrawn')}
    </label>
  );

  return (
    <ReportFrame title={title} controls={controls} query={query}>
      {(rows) => (
        <div className="space-y-4">
          {error && (
            <div role="alert" className="alert-error">
              {error}
            </div>
          )}

          <div className="panel">{addForm(run, mutation.isPending, rows)}</div>

          <DataGrid
            gridKey={queryKey}
            rows={rows}
            columns={columns(run, mutation.isPending)}
            rowKey={rowKey}
          />
        </div>
      )}
    </ReportFrame>
  );
}

/** A small action button for a grid row. */
export function RowAction({
  label,
  disabled,
  onClick,
  tone = 'neutral',
}: {
  readonly label: string;
  readonly disabled: boolean;
  readonly onClick: () => void;
  /** `danger` for the ones that withdraw or delete, so they read differently. */
  readonly tone?: 'neutral' | 'danger';
}): React.JSX.Element {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className={clsx(
        'rounded-md border px-2 py-0.5 text-xs font-medium whitespace-nowrap transition duration-150',
        'active:scale-95 disabled:pointer-events-none disabled:opacity-40',
        tone === 'danger'
          ? 'border-red-200 text-red-700 hover:bg-red-50 dark:border-red-500/30 dark:text-red-300 dark:hover:bg-red-500/10'
          : 'border-line text-ink-muted hover:border-line-strong hover:bg-surface-3 hover:text-ink',
      )}
    >
      {label}
    </button>
  );
}

/**
 * A labelled input for a master's add form.
 *
 * `width` names the width the field prefers on a roomy screen. It is paired with a
 * full width below `sm`, because a row of `w-32` boxes on a phone leaves four
 * two-inch fields stranded beside each other rather than one usable one.
 */
export function MasterField({
  label,
  value,
  onChange,
  placeholder,
  width = 'sm:w-32',
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly placeholder?: string;
  readonly width?: string;
}): React.JSX.Element {
  return (
    <label className={clsx('field w-full', width)}>
      <span className="field-label">{label}</span>
      <input
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder ?? ''}
        className="field-input-sm"
      />
    </label>
  );
}
