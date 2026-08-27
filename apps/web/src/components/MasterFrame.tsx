import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { Modal } from '@/components/Modal';
import { HeadingAction } from '@/components/PageHeading';
import { ReportFrame } from '@/components/ReportFrame';
import type { ApiError } from '@/lib/api';

/**
 * The chrome the inventory master screens share: the list, the include-withdrawn
 * toggle, the add form, and the plumbing that refreshes one after the other.
 *
 * Extracted after the second of the four rather than the first. They differ in their
 * columns and in what an add form asks for, and nothing else — so the part that varies
 * is passed in and the part that does not is written once. Repeating the mutation and
 * invalidation per screen is how three of them end up refreshing and the fourth does
 * not.
 *
 * The add form is a dialog rather than a panel above the list. A master is a list
 * that is read constantly and added to occasionally — a customer is created once and
 * looked up for years — and the fields held a fifth of the screen open for the
 * occasional case on every visit. Opened on demand they cost nothing until they are
 * wanted, and they get a dialog's room rather than a strip above the grid.
 */
export function MasterFrame<TRow>({
  title,
  addTitle,
  queryKey,
  fetchRows,
  columns,
  rowKey,
  addForm,
}: {
  readonly title: string;
  /** Names the record being created — "New supplier", not "Add". */
  readonly addTitle: string;
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
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const query = useQuery<readonly TRow[], ApiError>({
    queryKey: [queryKey, includeInactive],
    queryFn: () => fetchRows(includeInactive),
  });

  const mutation = useMutation<void, ApiError, () => Promise<void>>({
    mutationFn: (action) => action(),
    onSuccess: async () => {
      setError(null);
      // The record is in the list behind the dialog, so the dialog has said what it
      // had to say. Closing also discards the fields, which is why no form here
      // resets itself — a second record starts from a fresh one.
      setAdding(false);
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

  /*
    In the grid's toolbar rather than in a filter bar of its own. One checkbox does
    not fill a strip across the screen, and a strip is what the list would pay for
    it — the toggle is the only filter a master has.
  */
  const includeWithdrawn = (
    <label className="field-check shrink-0 text-xs whitespace-nowrap">
      <input
        type="checkbox"
        className="size-3.5"
        checked={includeInactive}
        onChange={(event) => setIncludeInactive(event.target.checked)}
      />
      {t('masters.includeWithdrawn')}
    </label>
  );

  return (
    <>
      <ReportFrame
        title={title}
        controls={null}
        actions={<HeadingAction label={addTitle} onClick={() => setAdding(true)} />}
        query={query}
      >
        {(rows) => (
          <div className="space-y-3">
            {/*
              Only while the dialog is shut. A refusal from an add belongs in front
              of the fields that caused it, not behind the dialog still covering
              them — the same message is rendered there instead.
            */}
            {error && !adding && (
              <div role="alert" className="alert-error">
                {error}
              </div>
            )}

            <DataGrid
              gridKey={queryKey}
              rows={rows}
              columns={columns(run, mutation.isPending)}
              rowKey={rowKey}
              filters={includeWithdrawn}
            />
          </div>
        )}
      </ReportFrame>

      {/*
        Outside the frame, which renders its children only once the list has
        arrived: a master whose list failed to load must still let somebody add to
        it. The rows the form is given are whatever the query holds — a unit's base
        list, a category's parent list — and an empty list is the right answer while
        there is nothing to choose from.
      */}
      {adding && (
        <Modal title={addTitle} size="form" onClose={() => setAdding(false)}>
          {error && (
            <div role="alert" className="alert-error">
              {error}
            </div>
          )}

          {addForm(run, mutation.isPending, query.data ?? [])}
        </Modal>
      )}
    </>
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
 * It used to name the width it wanted — `w-20` for a symbol, `w-44` for a name —
 * which is how a filter strip is built. In the dialog the form now lives in, the
 * cell decides and every field fills it: a panel of boxes each stopping at a
 * different point is harder to read down than one column of equal ones, and a
 * two-character field is no easier to type into for being two characters wide.
 */
export function MasterField({
  label,
  value,
  onChange,
  placeholder,
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly placeholder?: string;
}): React.JSX.Element {
  return (
    <label className="field w-full">
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
