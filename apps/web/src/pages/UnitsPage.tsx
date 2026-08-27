import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { MasterField, MasterFrame, RowAction } from '@/components/MasterFrame';
import type { GridColumn } from '@/components/DataGrid';
import {
  createMaster,
  listMaster,
  setMasterActive,
  type UnitSummary,
} from '@/lib/inventory';

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
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <RowAction
          label={row.isActive ? t('masters.withdraw') : t('masters.restore')}
          disabled={busy}
          onClick={() =>
            run(async () => {
              await setMasterActive('UnitOfMeasure', row.id, !row.isActive);
            })
          }
        />
      ),
    },
  ];

  return (
    <MasterFrame<UnitSummary>
      title={t('nav.units')}
      addTitle={t('masters.newUnit')}
      queryKey="units"
      fetchRows={(includeInactive) => listMaster<UnitSummary>('units', includeInactive)}
      columns={columns}
      rowKey={(row) => row.id}
      addForm={(run, busy, rows) => <AddUnit run={run} busy={busy} rows={rows} />}
    />
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

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        if (!code.trim() || !name.trim()) {
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
        <MasterField
          label={t('masters.code')}
          value={code}
          onChange={setCode}
          placeholder="BOX"
        />
        <MasterField label={t('masters.name')} value={name} onChange={setName} />
        <MasterField label={t('units.symbol')} value={symbol} onChange={setSymbol} />

        <label className="field">
          <span className="field-label">{t('units.base')}</span>
          <select
            value={baseUnitId}
            onChange={(event) => setBaseUnitId(event.target.value)}
            className="field-input-sm"
          >
            <option value="">{t('units.newGroup')}</option>
            {bases.map((base) => (
              <option key={base.id} value={base.id}>
                {base.code} — {base.name}
              </option>
            ))}
          </select>
        </label>

        {/* A factor only means anything on a derived unit; a base is one by definition. */}
        {baseUnitId && (
          <MasterField label={t('units.factor')} value={factor} onChange={setFactor} />
        )}

        <MasterField
          label={t('units.decimals')}
          value={decimals}
          onChange={setDecimals}
        />
      </div>

      <div className="form-actions">
        <button type="submit" disabled={busy} className="btn-primary">
          {t('masters.add')}
        </button>
      </div>
    </form>
  );
}
