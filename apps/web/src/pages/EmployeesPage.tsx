import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { PageHeading } from '@/components/PageHeading';
import { RowAction } from '@/components/MasterFrame';
import { LedgerEditor } from '@/components/LedgerEditors';
import type { ApiError } from '@/lib/api';
import { LedgerKind, listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { useSession } from '@/stores/session';
import { shortcutProps } from '@/lib/shortcuts';

/**
 * The firm's employees: the ledgers it pays salaries, advances and expenses through.
 *
 * An employee is a ledger of its own kind rather than a separate record, so this is
 * the chart of accounts narrowed to that kind, with the editor opening straight onto
 * it. Customers and suppliers have screens of their own for the same reason, beside
 * this one under Masters › Ledger.
 */
export function EmployeesPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const can = useSession((s) => s.can);
  // An employee being edited: its ledger id, 'new', or nothing.
  const [editing, setEditing] = useState<string | null>(null);

  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers', 'all'],
    queryFn: () => listLedgers(false),
  });

  const rows = useMemo(
    () => (ledgers.data ?? []).filter((ledger) => ledger.kind === LedgerKind.employee),
    [ledgers.data],
  );

  const columns: readonly GridColumn<LedgerSummary>[] = [
    { key: 'code', header: t('masters.code'), value: (row) => row.code },
    { key: 'name', header: t('masters.name'), value: (row) => row.name },
    { key: 'group', header: t('ledgerMaster.group'), value: (row) => row.groupName },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <div className="flex flex-wrap justify-end gap-1">
          {can('accounting:ledger:edit') && (
            <RowAction
              label={t('common.edit')}
              disabled={false}
              onClick={() => setEditing(row.ledgerId)}
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
      ),
    },
  ];

  return (
    <section className="page">
      <PageHeading title={t('employees.title')} />

      {ledgers.isError && (
        <p className="alert-error">{ledgers.error.detail || ledgers.error.code}</p>
      )}

      <DataGrid
        gridKey="employees"
        rows={rows}
        columns={columns}
        rowKey={(row) => row.ledgerId}
        emptyMessage={ledgers.isPending ? t('common.loading') : t('employees.none')}
        actions={
          can('accounting:ledger:create') && (
            <button
              {...shortcutProps('new')}
              type="button"
              className="btn-primary btn-sm"
              onClick={() => setEditing('new')}
            >
              {t('employees.new')}
            </button>
          )
        }
      />

      {editing !== null && (
        <LedgerEditor
          ledgerId={editing === 'new' ? null : editing}
          kind={LedgerKind.employee}
          onClose={() => setEditing(null)}
        />
      )}
    </section>
  );
}
