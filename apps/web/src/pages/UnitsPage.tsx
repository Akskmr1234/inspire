import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { MasterFrame, RowAction } from '@/components/MasterFrame';
import { Field, NumberField, SelectField, TextField } from '@/components/Form';
import type { GridColumn } from '@/components/DataGrid';
import { ActiveBadge } from '@/components/StatusBadge';
import {
  createMaster,
  listMaster,
  renameUnit,
  setMasterActive,
  type UnitSummary,
} from '@/lib/inventory';
import { collect, maxLength, numeric, required, useValidation } from '@/lib/validation';

/**
 * Units of measurement.
 *
 * A unit either is a base — the thing its group is counted in — or converts directly
 * to one. There is no third case: a unit may not convert to another derived unit,
 * because chaining makes every conversion compound and, with a fractional factor,
 * compounding is where the rounding error comes from.
 */
export function UnitsPage(): React.JSX.Element {
  const { t } = useTranslation();

  const columns = (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
    edit: (row: UnitSummary) => void,
  ): readonly GridColumn<UnitSummary>[] => [
    { key: 'code', header: t('masters.code'), value: (row) => row.code },
    { key: 'name', header: t('masters.name'), value: (row) => row.name },
    { key: 'symbol', header: t('units.symbol'), value: (row) => row.symbol ?? '' },
    {
      key: 'base',
      header: t('units.base'),
      // A base unit is shown as such rather than left blank, so the two cases are
      // told apart at a glance instead of by an absence.
      value: (row) => row.baseUnitCode ?? t('units.isBase'),
    },
    {
      key: 'factor',
      header: t('units.factor'),
      value: (row) => row.conversionFactor,
      numeric: true,
    },
    {
      key: 'decimals',
      header: t('units.decimals'),
      value: (row) => row.decimalPlaces,
      numeric: true,
    },
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
                await setMasterActive('UnitOfMeasure', row.id, !row.isActive);
              })
            }
          />
        </span>
      ),
    },
  ];

  return (
    <MasterFrame<UnitSummary>
      title={t('nav.units')}
      addTitle={t('masters.newUnit')}
      editTitle={t('units.edit')}
      queryKey="units"
      fetchRows={(includeInactive) => listMaster<UnitSummary>('units', includeInactive)}
      columns={columns}
      rowKey={(row) => row.id}
      addForm={(run, busy, rows) => <AddUnit run={run} busy={busy} rows={rows} />}
      editForm={(row, run, busy) => <EditUnit unit={row} run={run} busy={busy} />}
    />
  );
}

/**
 * The edit form: one field, and a plain statement of why the others are not here.
 *
 * The API offers a rename and nothing else, because a code and a conversion factor
 * are what documents already entered are stated in. Rather than show four boxes and
 * refuse three of them on save, the form shows the values that cannot move as text
 * and says so.
 */
function EditUnit({
  unit,
  run,
  busy,
}: {
  readonly unit: UnitSummary;
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [name, setName] = useState(unit.name);

  const { errors, submit } = useValidation<{ name: string }>((values) =>
    collect({
      name:
        required(values.name, t('units.nameRequired')) ??
        maxLength(values.name, 100, t('units.nameTooLong')),
    }),
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        if (!submit({ name })) {
          return;
        }

        run(async () => {
          await renameUnit(unit.id, name.trim());
        });
      }}
    >
      <div className="form-grid">
        <Field label={t('masters.code')} hint={t('units.codeFixed')}>
          <input value={unit.code} disabled className="field-input" />
        </Field>

        <TextField
          label={t('masters.name')}
          required
          autoFocus
          value={name}
          onChange={setName}
          error={errors['name']}
        />

        <Field label={t('units.base')} hint={t('units.factorFixed')}>
          <input
            value={unit.baseUnitCode ?? t('units.isBase')}
            disabled
            className="field-input"
          />
        </Field>

        <Field label={t('units.factor')}>
          <input
            value={String(unit.conversionFactor)}
            disabled
            className="field-input text-end font-mono tabular-nums"
          />
        </Field>
      </div>

      <div className="form-actions">
        <button type="submit" disabled={busy} className="btn-primary">
          {busy ? t('common.saving') : t('common.save')}
        </button>
      </div>
    </form>
  );
}

function AddUnit({
  run,
  busy,
  rows,
}: {
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
  readonly rows: readonly UnitSummary[];
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [symbol, setSymbol] = useState('');
  const [baseUnitId, setBaseUnitId] = useState('');
  const [factor, setFactor] = useState('1');
  const [decimals, setDecimals] = useState('0');

  // Only base units may be chosen as a base, which is the rule made visible: the
  // option that would be refused is never offered.
  const bases = rows.filter((row) => row.baseUnitId === null && row.isActive);

  const draft = { code, name, symbol, baseUnitId, factor, decimals };

  const { errors, submit } = useValidation<typeof draft>((values) =>
    collect({
      code:
        required(values.code, t('units.codeRequired')) ??
        maxLength(values.code, 20, t('units.codeTooLong')),
      name:
        required(values.name, t('units.nameRequired')) ??
        maxLength(values.name, 100, t('units.nameTooLong')),
      // A factor is meaningless on a base unit, so it is only checked where the
      // form is actually asking for one.
      factor:
        values.baseUnitId === ''
          ? null
          : (required(values.factor, t('units.factorRequired')) ??
            numeric(values.factor, t('units.factorInvalid'), { min: 0.000001 })),
      decimals: numeric(values.decimals, t('units.decimalsInvalid'), {
        min: 0,
        max: 6,
        integer: true,
      }),
    }),
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        if (!submit(draft)) {
          return;
        }

        run(async () => {
          await createMaster('units', {
            code: code.trim(),
            name: name.trim(),
            symbol: symbol.trim() || null,
            baseUnitId: baseUnitId || null,
            conversionFactor: baseUnitId ? Number(factor) || 1 : 1,
            decimalPlaces: Number(decimals) || 0,
          });
        });
      }}
    >
      <div className="form-grid">
        <TextField
          label={t('masters.code')}
          required
          autoFocus
          value={code}
          onChange={setCode}
          error={errors['code']}
          placeholder="BOX"
        />
        <TextField
          label={t('masters.name')}
          required
          value={name}
          onChange={setName}
          error={errors['name']}
        />
        <TextField label={t('units.symbol')} value={symbol} onChange={setSymbol} />

        <SelectField
          label={t('units.base')}
          value={baseUnitId}
          onChange={setBaseUnitId}
          options={[
            { value: '', label: t('units.newGroup') },
            ...bases.map((base) => ({
              value: base.id,
              label: `${base.code} — ${base.name}`,
            })),
          ]}
        />

        {/* A factor only means anything on a derived unit; a base is one by definition. */}
        {baseUnitId && (
          <NumberField
            label={t('units.factor')}
            required
            min={0}
            value={factor}
            onChange={setFactor}
            error={errors['factor']}
          />
        )}

        <NumberField
          label={t('units.decimals')}
          min={0}
          step="1"
          value={decimals}
          onChange={setDecimals}
          error={errors['decimals']}
        />
      </div>

      <div className="form-actions">
        <button type="submit" disabled={busy} className="btn-primary">
          {busy ? t('common.saving') : t('masters.add')}
        </button>
      </div>
    </form>
  );
}
