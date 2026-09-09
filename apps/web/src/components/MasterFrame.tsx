import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { Modal } from '@/components/Modal';
import { ReportFrame } from '@/components/ReportFrame';
import { TextField } from '@/components/Form';
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
 *
 * The button that opens them sits at the head of the grid's own controls, beside the
 * column picker and the export: adding to a list is done while looking at the list.
 */
export function MasterFrame<TRow>({
  title,
  addTitle,
  editTitle,
  queryKey,
  fetchRows,
  columns,
  rowKey,
  addForm,
  editForm,
}: {
  readonly title: string;
  /** Names the record being created — "New supplier", not "Add". */
  readonly addTitle: string;
  /** Names the record being changed — "Edit unit". Required where `editForm` is. */
  readonly editTitle?: string;
  readonly queryKey: string;
  readonly fetchRows: (includeInactive: boolean) => Promise<readonly TRow[]>;
  /**
   * Built with a runner, so a row's own actions can invoke a mutation, and with the
   * opener for the edit dialog where this master has one.
   */
  readonly columns: (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
    edit: (row: TRow) => void,
  ) => readonly GridColumn<TRow>[];
  readonly rowKey: (row: TRow) => string;
  /** The fields this master asks for when adding a record. */
  readonly addForm: (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
    rows: readonly TRow[],
  ) => React.ReactNode;
  /**
   * The fields this master offers when changing one.
   *
   * Optional, because not every master can be changed: the API exposes a rename for
   * a unit and a full update for a customer, and nothing at all for a brand. A
   * master without this simply has no Edit action, which is honest — an Edit button
   * that opens a form the server will refuse is worse than no button.
   */
  readonly editForm?: (
    row: TRow,
    run: (action: () => Promise<void>) => void,
    busy: boolean,
    rows: readonly TRow[],
  ) => React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [includeInactive, setIncludeInactive] = useState(false);
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<TRow | null>(null);
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
      setEditing(null);
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
        checked={includeInactive}
        onChange={(event) => setIncludeInactive(event.target.checked)}
      />
      {t('masters.includeWithdrawn')}
    </label>
  );

  return (
    <>
      <ReportFrame title={title} controls={null} query={query}>
        {(rows) => (
          <div className="space-y-3">
            {/*
              Only while the dialog is shut. A refusal from an add belongs in front
              of the fields that caused it, not behind the dialog still covering
              them — the same message is rendered there instead.
            */}
            {error && !adding && !editing && (
              <div role="alert" className="alert-error">
                {error}
              </div>
            )}

            <DataGrid
              gridKey={queryKey}
              rows={rows}
              columns={columns(run, mutation.isPending, (row) => {
                setError(null);
                setEditing(row);
              })}
              rowKey={rowKey}
              filters={includeWithdrawn}
              actions={<GridAction label={addTitle} onClick={() => setAdding(true)} />}
            />
          </div>
        )}
      </ReportFrame>

      {/*
        Outside the frame rather than beside the grid that opens it. The frame
        swaps its children for a skeleton whenever the list goes back to pending — a
        refetch after an add, the withdrawn toggle — and a form mounted inside would
        be unmounted mid-entry, taking whatever had been typed with it.

        The rows the form is given are whatever the query holds: a unit's base list,
        a category's parent list. An empty list is the right answer while there is
        nothing to choose from.
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

      {/*
        Outside the frame for the reason the add dialog is: the frame swaps its
        children for a skeleton whenever the list refetches, and a form mounted
        inside would lose whatever had been typed into it.

        Keyed on the row, so opening a second record after a first starts from that
        record's own values rather than the previous one's — a form component that
        stays mounted keeps its state, and the state here is the record.
      */}
      {editing && editForm && (
        <Modal
          title={editTitle ?? t('common.edit')}
          size="form"
          onClose={() => setEditing(null)}
        >
          {error && (
            <div role="alert" className="alert-error">
              {error}
            </div>
          )}

          <div key={rowKey(editing)}>
            {editForm(editing, run, mutation.isPending, query.data ?? [])}
          </div>
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
        'row-action',
        tone === 'danger' ? 'row-action-danger' : 'row-action-neutral',
      )}
    >
      {label}
    </button>
  );
}

/**
 * A labelled input for a master's add form.
 *
 * Now a thin wrapper over the shared `TextField`, which is where the caption
 * placement, the mandatory marker and the error message live. It stays because the
 * simpler masters read better with it, and because a screen that only needs a label
 * and a value should not have to know about the rest.
 */
export function MasterField({
  label,
  value,
  onChange,
  placeholder,
  required,
  error,
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly placeholder?: string;
  readonly required?: boolean;
  readonly error?: string | undefined;
}): React.JSX.Element {
  return (
    <TextField
      label={label}
      value={value}
      onChange={onChange}
      error={error}
      {...(required === undefined ? {} : { required })}
      {...(placeholder === undefined ? {} : { placeholder })}
    />
  );
}
