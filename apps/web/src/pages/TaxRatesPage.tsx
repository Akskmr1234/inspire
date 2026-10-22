import { useTranslation } from 'react-i18next';
import { MasterFrame, RowAction } from '@/components/MasterFrame';
import { TaxRateForm } from '@/components/TaxRateForm';
import type { GridColumn } from '@/components/DataGrid';
import { ActiveBadge } from '@/components/StatusBadge';
import {
  listTaxRates,
  setTaxRateActive,
  updateTaxRate,
  type TaxRate,
} from '@/lib/taxRates';

/**
 * The tax rates a product can carry.
 *
 * A short list the firm chooses - Exempt, VAT 5% - rather than a number typed on every
 * product, so a change of rate is made once. One is the default: what a new product
 * starts with, and what a document line falls back to for a product with none.
 */
export function TaxRatesPage(): React.JSX.Element {
  const { t } = useTranslation();

  const columns = (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
    edit: (row: TaxRate) => void,
  ): readonly GridColumn<TaxRate>[] => [
    { key: 'code', header: t('masters.code'), value: (row) => row.code },
    { key: 'name', header: t('masters.name'), value: (row) => row.name },
    {
      key: 'nameArabic',
      header: t('masters.nameArabic'),
      value: (row) => row.nameArabic ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'percentage',
      header: t('taxRates.percentage'),
      value: (row) => row.percentage,
      numeric: true,
      render: (row) => `${row.percentage.toFixed(2)}%`,
    },
    {
      key: 'default',
      header: t('taxRates.default'),
      value: (row) => (row.isDefault ? t('common.yes') : ''),
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
        <div className="flex flex-wrap justify-end gap-1">
          <RowAction label={t('common.edit')} disabled={busy} onClick={() => edit(row)} />
          {!row.isDefault && row.isActive && (
            <RowAction
              label={t('taxRates.makeDefault')}
              disabled={busy}
              onClick={() =>
                run(async () => {
                  await updateTaxRate(row.id, { ...row, isDefault: true });
                })
              }
            />
          )}
          <RowAction
            label={row.isActive ? t('masters.withdraw') : t('masters.restore')}
            tone={row.isActive ? 'danger' : 'neutral'}
            disabled={busy}
            onClick={() =>
              run(async () => {
                await setTaxRateActive(row.id, !row.isActive);
              })
            }
          />
        </div>
      ),
    },
  ];

  return (
    <MasterFrame<TaxRate>
      title={t('taxRates.title')}
      addTitle={t('taxRates.new')}
      editTitle={t('taxRates.edit')}
      queryKey="tax-rates"
      fetchRows={(includeInactive) => listTaxRates(includeInactive)}
      columns={columns}
      rowKey={(row) => row.id}
      addForm={(run, busy, rows) => <TaxRateForm rates={rows} run={run} busy={busy} />}
      editForm={(row, run, busy, rows) => (
        <TaxRateForm record={row} rates={rows} run={run} busy={busy} />
      )}
    />
  );
}
