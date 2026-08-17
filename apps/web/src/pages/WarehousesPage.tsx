import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { MasterField, MasterFrame, RowAction } from '@/components/MasterFrame';
import type { GridColumn } from '@/components/DataGrid';
import {
  createMaster,
  listMaster,
  setDefaultWarehouse,
  setMasterActive,
  type WarehouseSummary,
} from '@/lib/inventory';

/**
 * Warehouses, called godowns in the reference application and stock locations in the
 * interface.
 *
 * Exactly one is the default, and it is what every new document fills its location in
 * with. Promoting another demotes the current one in the same breath — the database
 * permits only one, so the two halves cannot be separated.
 */
export function WarehousesPage(): React.JSX.Element {
  const { t } = useTranslation();

  const columns = (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
  ): readonly GridColumn<WarehouseSummary>[] => [
    { key: 'code', header: t('masters.code'), value: (row) => row.code },
    { key: 'name', header: t('masters.name'), value: (row) => row.name },
    {
      key: 'nameArabic',
      header: t('masters.nameArabic'),
      value: (row) => row.nameArabic ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'branch',
      header: t('warehouses.branch'),
      // A warehouse serving every branch is an ordinary arrangement, so the absence
      // is labelled rather than left blank.
      value: (row) => row.branchName ?? t('warehouses.central'),
    },
    {
      key: 'address',
      header: t('warehouses.address'),
      value: (row) => row.address ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'default',
      header: t('warehouses.default'),
      value: (row) => (row.isDefault ? t('common.yes') : ''),
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
        <div className="flex flex-wrap justify-end gap-1">
          {/* Offering "make default" on the one that already is, or on a withdrawn
              one the server would refuse, would be offering an action that fails. */}
          {!row.isDefault && row.isActive && (
            <RowAction
              label={t('warehouses.makeDefault')}
              disabled={busy}
              onClick={() =>
                run(async () => {
                  await setDefaultWarehouse(row.id);
                })
              }
            />
          )}
          <RowAction
            label={row.isActive ? t('masters.withdraw') : t('masters.restore')}
            disabled={busy}
            onClick={() =>
              run(async () => {
                await setMasterActive('Warehouse', row.id, !row.isActive);
              })
            }
          />
        </div>
      ),
    },
  ];

  return (
    <MasterFrame<WarehouseSummary>
      title={t('nav.warehouses')}
      queryKey="warehouses"
      fetchRows={(includeInactive) =>
        listMaster<WarehouseSummary>('warehouses', includeInactive)
      }
      columns={columns}
      rowKey={(row) => row.id}
      addForm={(run, busy) => <AddWarehouse run={run} busy={busy} />}
    />
  );
}

function AddWarehouse({
  run,
  busy,
}: {
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [nameArabic, setNameArabic] = useState('');
  const [address, setAddress] = useState('');

  return (
    <form
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();

        if (!code.trim() || !name.trim()) {
          return;
        }

        run(async () => {
          await createMaster('warehouses', {
            code: code.trim(),
            name: name.trim(),
            nameArabic: nameArabic.trim() || null,
            address: address.trim() || null,
          });
        });

        setCode('');
        setName('');
        setNameArabic('');
        setAddress('');
      }}
    >
      <MasterField label={t('masters.code')} value={code} onChange={setCode} />
      <MasterField
        label={t('masters.name')}
        value={name}
        onChange={setName}
        width="w-44"
      />
      <MasterField
        label={t('masters.nameArabic')}
        value={nameArabic}
        onChange={setNameArabic}
        width="w-44"
      />
      <MasterField
        label={t('warehouses.address')}
        value={address}
        onChange={setAddress}
        width="w-56"
      />

      <button type="submit" disabled={busy} className="btn-primary">
        {t('masters.add')}
      </button>
    </form>
  );
}
