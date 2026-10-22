import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { PageHeading } from '@/components/PageHeading';
import { RowAction } from '@/components/MasterFrame';
import { LedgerEditor } from '@/components/LedgerEditors';
import type { ApiError } from '@/lib/api';
import { isPlainKind, LedgerKind, listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { useSession } from '@/stores/session';

/** The translation key of each kind, under `ledgerKinds`. */
const KIND_KEY: Record<number, string> = {
  [LedgerKind.general]: 'General',
  [LedgerKind.cash]: 'Cash',
  [LedgerKind.bank]: 'Bank',
  [LedgerKind.customer]: 'Customer',
  [LedgerKind.supplier]: 'Supplier',
  [LedgerKind.employee]: 'Employee',
  [LedgerKind.tax]: 'Tax',
  [LedgerKind.additionalCharge]: 'AdditionalCharge',
};

/** Where a party's own screen opens it, for the kinds edited there. */
const PARTY_SCREEN: Record<number, string> = {
  [LedgerKind.customer]: '/sales/customers',
  [LedgerKind.supplier]: '/purchase/suppliers',
};

/**
 * Every ledger in one list, to find, open and create.
 *
 * The chart of accounts shows the same ledgers in their groups, with balances; this is
 * the plain list somebody reaches for to add a ledger or correct one. Customers and
 * suppliers open on their own screens, which keep their terms and addresses.
 */
export function LedgerListPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const can = useSession((s) => s.can);
  // A ledger being edited: its id, 'new', or nothing.
  const [editing, setEditing] = useState<string | null>(null);
  const [kind, setKind] = useState<number>(0);
  const [includeInactive, setIncludeInactive] = useState(false);

  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers', includeInactive ? 'all' : 'active'],
    queryFn: () => listLedgers(!includeInactive),
  });

  const rows = useMemo(
    () => (ledgers.data ?? []).filter((ledger) => kind === 0 || ledger.kind === kind),
    [ledgers.data, kind],
  );

  const columns: readonly GridColumn<LedgerSummary>[] = [
    { key: 'code', header: t('masters.code'), value: (row) => row.code },
    { key: 'name', header: t('masters.name'), value: (row) => row.name, wide: true },
    {
      key: 'nameArabic',
      header: t('ledgerMaster.nameArabic'),
      value: (row) => row.nameArabic ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'kind',
      header: t('ledgerMaster.kind'),
      value: (row) => t(`ledgerKinds.${KIND_KEY[row.kind] ?? 'General'}`),
    },
    { key: 'group', header: t('ledgerMaster.group'), value: (row) => row.groupName },
    {
      key: 'currency',
      header: t('ledgers.currency'),
      value: (row) => row.currency,
      hiddenByDefault: true,
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => {
        const party = PARTY_SCREEN[row.kind];

        return (
          <div className="flex flex-nowrap justify-end gap-1">
            {can('accounting:ledger:edit') && isPlainKind(row.kind) && (
              <RowAction
                label={t('common.edit')}
                disabled={false}
                onClick={() => setEditing(row.ledgerId)}
              />
            )}
            {party && (
              <RowAction
                label={t('common.edit')}
                disabled={false}
                onClick={() => void navigate(`${party}?edit=${row.ledgerId}`)}
              />
            )}
            <RowAction
              label={t('ledgerMaster.statement')}
              disabled={false}
              onClick={() => {
                void navigate(`/accounting/ledger-statement?ledger=${row.ledgerId}`);
              }}
            />
          </div>
        );
      },
    },
  ];

  return (
    <section className="page">
      <PageHeading title={t('ledgerList.title')} />

      {ledgers.isError && (
        <p className="alert-error">{ledgers.error.detail || ledgers.error.code}</p>
      )}

      <DataGrid
        gridKey="ledger-list"
        rows={rows}
        columns={columns}
        rowKey={(row) => row.ledgerId}
        emptyMessage={ledgers.isPending ? t('common.loading') : t('ledgerList.none')}
        filters={
          <div className="flex flex-wrap items-center gap-2">
            <select
              className="field-input-sm w-auto"
              value={kind}
              aria-label={t('ledgerMaster.kind')}
              onChange={(event) => setKind(Number(event.target.value))}
            >
              <option value={0}>{t('ledgerList.allKinds')}</option>
              {Object.entries(KIND_KEY).map(([value, key]) => (
                <option key={value} value={value}>
                  {t(`ledgerKinds.${key}`)}
                </option>
              ))}
            </select>
            <label className="field-check shrink-0 text-xs whitespace-nowrap">
              <input
                type="checkbox"
                checked={includeInactive}
                onChange={(event) => setIncludeInactive(event.target.checked)}
              />
              {t('masters.includeWithdrawn')}
            </label>
          </div>
        }
        actions={
          can('accounting:ledger:create') && (
            <button
              type="button"
              className="btn-primary btn-sm"
              onClick={() => setEditing('new')}
            >
              {t('ledgerMaster.newLedger')}
            </button>
          )
        }
      />

      {editing !== null && (
        <LedgerEditor
          ledgerId={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
        />
      )}
    </section>
  );
}
