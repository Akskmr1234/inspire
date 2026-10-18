import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { MasterFrame, RowAction } from '@/components/MasterFrame';
import { TextField } from '@/components/Form';
import type { GridColumn } from '@/components/DataGrid';
import { ActiveBadge } from '@/components/StatusBadge';
import {
  createTerritory,
  listTerritories,
  renameTerritory,
  setTerritoryActive,
  TerritoryKind,
  type TerritorySummary,
} from '@/lib/places';
import { collect, maxLength, required, useValidation } from '@/lib/validation';

/**
 * Areas and routes: the two ways a firm divides up where its customers are - the part of
 * town they are in, and the round a van calls on them on. One screen, a tab for each,
 * because they are the same short list of names kept for different questions.
 */
export function TerritoriesPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [kind, setKind] = useState<number>(TerritoryKind.area);

  return (
    <div className="space-y-3">
      <div role="tablist" className="flex gap-1 overflow-x-auto border-b border-line">
        {[TerritoryKind.area, TerritoryKind.route].map((value) => (
          <button
            key={value}
            type="button"
            role="tab"
            aria-selected={kind === value}
            onClick={() => setKind(value)}
            className={clsx(
              '-mb-px shrink-0 border-b-2 px-3 py-2.5 text-sm font-medium whitespace-nowrap transition',
              kind === value
                ? 'border-brand-600 text-brand-700 dark:border-brand-400 dark:text-brand-300'
                : 'border-transparent text-ink-muted hover:border-line-strong hover:text-ink',
            )}
          >
            {value === TerritoryKind.area ? t('places.areas') : t('places.routes')}
          </button>
        ))}
      </div>

      {/* Keyed so each tab keeps a list, and a layout, of its own. */}
      <Territories key={kind} kind={kind} />
    </div>
  );
}

function Territories({ kind }: { readonly kind: number }): React.JSX.Element {
  const { t } = useTranslation();
  const isArea = kind === TerritoryKind.area;

  const columns = (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
    edit: (row: TerritorySummary) => void,
  ): readonly GridColumn<TerritorySummary>[] => [
    { key: 'name', header: t('masters.name'), value: (row) => row.name },
    {
      key: 'status',
      header: t('masters.status'),
      value: (row) => (row.isActive ? t('masters.active') : t('masters.withdrawn')),
      render: (row) => <ActiveBadge isActive={row.isActive} />,
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <span className="inline-flex gap-1.5">
          <RowAction label={t('common.edit')} disabled={busy} onClick={() => edit(row)} />
          <RowAction
            label={row.isActive ? t('masters.withdraw') : t('masters.restore')}
            tone={row.isActive ? 'danger' : 'neutral'}
            disabled={busy}
            onClick={() =>
              run(async () => {
                await setTerritoryActive(row.id, !row.isActive);
              })
            }
          />
        </span>
      ),
    },
  ];

  return (
    <MasterFrame<TerritorySummary>
      title={isArea ? t('places.areas') : t('places.routes')}
      addTitle={isArea ? t('places.newArea') : t('places.newRoute')}
      editTitle={isArea ? t('places.editArea') : t('places.editRoute')}
      queryKey="territories"
      queryArgs={[kind]}
      fetchRows={(includeInactive) => listTerritories(kind, includeInactive)}
      columns={columns}
      rowKey={(row) => row.id}
      addForm={(run, busy) => <TerritoryForm kind={kind} run={run} busy={busy} />}
      editForm={(row, run, busy) => (
        <TerritoryForm kind={kind} run={run} busy={busy} existing={row} />
      )}
    />
  );
}

function TerritoryForm({
  kind,
  run,
  busy,
  existing,
}: {
  readonly kind: number;
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
  readonly existing?: TerritorySummary;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [name, setName] = useState(existing?.name ?? '');

  const { errors, submit } = useValidation<{ name: string }>((values) =>
    collect({
      name:
        required(values.name, t('places.nameRequired')) ??
        maxLength(values.name, 100, t('places.nameTooLong')),
    }),
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        if (!submit({ name })) return;

        run(async () => {
          if (existing) await renameTerritory(existing.id, name.trim());
          else await createTerritory(kind, name.trim());
        });
      }}
    >
      <TextField
        label={t('masters.name')}
        required
        autoFocus
        value={name}
        onChange={setName}
        error={errors['name']}
      />

      <div className="form-actions">
        <button type="submit" disabled={busy} className="btn-primary">
          {busy ? t('common.saving') : existing ? t('common.save') : t('masters.add')}
        </button>
      </div>
    </form>
  );
}
