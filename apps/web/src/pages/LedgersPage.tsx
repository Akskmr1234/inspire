import { useCallback, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import clsx from 'clsx';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { DateRangeControls, ReportFrame } from '@/components/ReportFrame';
import { RowAction } from '@/components/MasterFrame';
import { GroupEditor, LedgerEditor, useAccountGroups } from '@/components/LedgerEditors';
import { request, type ApiError } from '@/lib/api';
import { groupPaths, isPlainKind, type AccountGroupSummary } from '@/lib/ledgers';
import { useSession } from '@/stores/session';
import {
  HierarchicalAccountsTable,
  type ReportColumn,
} from '@/components/HierarchicalAccountsTable';
import { buildChartOfAccountsHierarchy } from '@/lib/hierarchicalReports';
import { shortcutProps } from '@/lib/shortcuts';

/** The ledger kinds, keyed by the wire value the API serialises them as. */
const KIND_NAME: Record<number, string> = {
  1: 'General',
  2: 'Cash',
  3: 'Bank',
  4: 'Customer',
  5: 'Supplier',
  6: 'Employee',
  7: 'Tax',
  8: 'AdditionalCharge',
};

/** The account natures, keyed by the wire value. */
const NATURE_NAME: Record<number, string> = {
  1: 'Asset',
  2: 'Liability',
  3: 'Equity',
  4: 'Income',
  5: 'Expense',
};

interface LedgerSummary {
  readonly ledgerId: string;
  readonly code: string;
  readonly name: string;
  readonly kind: number;
  readonly groupCode: string;
  readonly groupName: string;
  readonly nature: number;
  readonly currency: string;
  readonly isBillWise: boolean;
}

interface TrialBalanceRow {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly openingDebit: number;
  readonly openingCredit: number;
  readonly periodDebit: number;
  readonly periodCredit: number;
  readonly closingDebit: number;
  readonly closingCredit: number;
}

interface TrialBalance {
  readonly rows: readonly TrialBalanceRow[];
  readonly totalOpeningDebit: number;
  readonly totalOpeningCredit: number;
  readonly totalPeriodDebit: number;
  readonly totalPeriodCredit: number;
  readonly totalClosingDebit: number;
  readonly totalClosingCredit: number;
}

type ViewMode = 'hierarchy' | 'grid' | 'groups';

function startOfYear(): string {
  return `${new Date().getFullYear()}-01-01`;
}

function endOfYear(): string {
  return `${new Date().getFullYear()}-12-31`;
}

/**
 * The chart of accounts: every ledger under its group and head, with its balances, and
 * the place a firm opens and corrects its own ledgers and groups.
 *
 * Three views of one list: the tree with balances, a flat grid to search and sort, and
 * the groups themselves. Customers, suppliers, tax and charge ledgers appear here like
 * any other, but are opened and corrected on their own screens, which also keep their
 * terms, registration or setup.
 */
export function LedgersPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const can = useSession((s) => s.can);
  const [from, setFrom] = useState(startOfYear());
  const [to, setTo] = useState(endOfYear());
  const [range, setRange] = useState({ from: startOfYear(), to: endOfYear() });
  const [viewMode, setViewMode] = useState<ViewMode>('hierarchy');
  // A ledger being edited: its id, 'new', or nothing.
  const [editing, setEditing] = useState<string | null>(null);
  const [groupEditing, setGroupEditing] = useState<AccountGroupSummary | 'new' | null>(
    null,
  );

  const canCreate = can('accounting:ledger:create');
  const canEdit = can('accounting:ledger:edit');

  const query = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => request<readonly LedgerSummary[]>('/accounting/ledgers'),
  });

  const groups = useAccountGroups();

  const tbQuery = useQuery<TrialBalance, ApiError>({
    queryKey: ['trial-balance-for-ledgers', range.from, range.to],
    queryFn: () =>
      request<TrialBalance>(
        `/accounting/reports/trial-balance?from=${range.from}&to=${range.to}`,
      ),
    staleTime: 60 * 1000,
  });

  const balancesMap = useMemo(() => {
    const map = new Map<
      string,
      { opening: number; debit: number; credit: number; closing: number }
    >();
    (tbQuery.data?.rows ?? []).forEach((row) => {
      const opening = row.openingDebit - row.openingCredit;
      const closing = row.closingDebit - row.closingCredit;
      const entry = {
        opening,
        debit: row.periodDebit,
        credit: row.periodCredit,
        closing,
      };
      map.set(row.ledgerId, entry);
      map.set(row.ledgerCode, entry);
    });
    return map;
  }, [tbQuery.data]);

  const kindById = useMemo(
    () => new Map((query.data ?? []).map((ledger) => [ledger.ledgerId, ledger.kind])),
    [query.data],
  );

  const ledgerActions = useCallback(
    (ledgerId: string): React.ReactNode => (
      <>
        <RowAction
          disabled={false}
          label={t('ledgerMaster.statement')}
          onClick={() =>
            navigate(
              `/accounting/ledger-statement?ledger=${ledgerId}&from=${range.from}&to=${range.to}`,
            )
          }
        />
        {canEdit && isPlainKind(kindById.get(ledgerId) ?? 0) && (
          <RowAction
            disabled={false}
            label={t('common.edit')}
            onClick={() => setEditing(ledgerId)}
          />
        )}
      </>
    ),
    [t, navigate, range, canEdit, kindById],
  );

  const hierarchyColumns = useMemo<readonly ReportColumn[]>(
    () => [
      { key: 'opening', header: t('reports.openingBalance') || 'Opening', align: 'end' },
      {
        key: 'debit',
        header: t('reports.debit') || 'Debit',
        align: 'end',
        blankZero: true,
      },
      {
        key: 'credit',
        header: t('reports.credit') || 'Credit',
        align: 'end',
        blankZero: true,
      },
      { key: 'closing', header: t('reports.closingBalance') || 'Closing', align: 'end' },
    ],
    [t],
  );

  const gridColumns = useMemo<readonly GridColumn<LedgerSummary>[]>(
    () => [
      {
        key: 'code',
        header: t('reports.ledger'),
        value: (row) => row.code,
      },
      {
        key: 'name',
        header: t('ledgers.name'),
        value: (row) => row.name,
        wide: true,
      },
      {
        key: 'kind',
        header: t('ledgers.kind'),
        value: (row) => t(`ledgerKinds.${KIND_NAME[row.kind]}`),
      },
      {
        key: 'groupCode',
        header: t('ledgers.groupCode'),
        value: (row) => row.groupCode,
        hiddenByDefault: true,
      },
      {
        key: 'groupName',
        header: t('reports.group'),
        value: (row) => row.groupName,
      },
      {
        key: 'nature',
        header: t('ledgers.nature'),
        value: (row) => t(`accountNatures.${NATURE_NAME[row.nature]}`),
      },
      {
        key: 'currency',
        header: t('ledgers.currency'),
        value: (row) => row.currency,
        hiddenByDefault: true,
      },
      {
        key: 'billWise',
        header: t('ledgers.billWise'),
        value: (row) => (row.isBillWise ? t('common.yes') : t('common.no')),
        hiddenByDefault: true,
      },
      {
        key: 'actions',
        header: '',
        value: () => null,
        render: (row) => (
          <span className="flex justify-end gap-1">{ledgerActions(row.ledgerId)}</span>
        ),
      },
    ],
    [t, ledgerActions],
  );

  const views: readonly [ViewMode, string][] = [
    ['hierarchy', t('reports.hierarchicalTree')],
    ['grid', t('reports.flatList')],
    ['groups', t('ledgerMaster.groups')],
  ];

  const controls = (
    <div className="flex flex-wrap items-center justify-between gap-3">
      {viewMode !== 'groups' ? (
        <DateRangeControls
          from={from}
          to={to}
          onFromChange={setFrom}
          onToChange={setTo}
          onApply={() => setRange({ from, to })}
          busy={query.isFetching || tbQuery.isFetching}
        />
      ) : (
        <span />
      )}

      <div
        role="group"
        aria-label={t('ledgerMaster.view')}
        className="flex items-center rounded-lg border border-line bg-surface-2 p-0.5 text-xs"
      >
        {views.map(([mode, label]) => (
          <button
            key={mode}
            type="button"
            aria-pressed={viewMode === mode}
            onClick={() => setViewMode(mode)}
            className={clsx(
              'rounded-md px-2.5 py-1 font-medium transition',
              viewMode === mode
                ? 'bg-surface font-semibold text-ink shadow-2xs'
                : 'text-ink-muted hover:text-ink',
            )}
          >
            {label}
          </button>
        ))}
      </div>
    </div>
  );

  const actions = canCreate && (
    <>
      <button
        type="button"
        className="btn-secondary btn-sm hidden sm:inline-flex"
        onClick={() => setGroupEditing('new')}
      >
        {t('ledgerMaster.newGroup')}
      </button>
      <button
        {...shortcutProps('new')}
        type="button"
        className="btn-primary btn-sm"
        onClick={() => setEditing('new')}
      >
        {t('ledgerMaster.newLedger')}
      </button>
    </>
  );

  return (
    <>
      <ReportFrame
        title={t('nav.ledgers')}
        controls={controls}
        actions={actions}
        query={query}
      >
        {(data) => {
          if (viewMode === 'groups') {
            return (
              <GroupsTable
                groups={groups.data ?? []}
                ledgers={data}
                canEdit={canEdit}
                canCreate={canCreate}
                onRename={setGroupEditing}
                onNew={() => setGroupEditing('new')}
              />
            );
          }

          if (viewMode === 'grid') {
            return (
              <DataGrid
                gridKey="ledgers"
                rows={data}
                columns={gridColumns}
                rowKey={(row) => row.ledgerId}
              />
            );
          }

          const heads = buildChartOfAccountsHierarchy(data, balancesMap);
          const grandTotals = tbQuery.data
            ? {
                opening: tbQuery.data.totalOpeningDebit - tbQuery.data.totalOpeningCredit,
                debit: tbQuery.data.totalPeriodDebit,
                credit: tbQuery.data.totalPeriodCredit,
                closing: tbQuery.data.totalClosingDebit - tbQuery.data.totalClosingCredit,
              }
            : undefined;

          return (
            <HierarchicalAccountsTable
              heads={heads}
              columns={hierarchyColumns}
              currency={data[0]?.currency}
              fromDate={range.from}
              toDate={range.to}
              initialExpandLevel={2}
              grandTotals={grandTotals}
              ledgerActions={ledgerActions}
              onVoucherClick={(vNum) => {
                navigate(`/accounting/voucher-report?posted=${encodeURIComponent(vNum)}`);
              }}
            />
          );
        }}
      </ReportFrame>

      {editing !== null && (
        <LedgerEditor
          ledgerId={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
        />
      )}
      {groupEditing !== null && (
        <GroupEditor
          group={groupEditing === 'new' ? null : groupEditing}
          onClose={() => setGroupEditing(null)}
        />
      )}
    </>
  );
}

/** The account groups, each with its place in the tree and how many ledgers it holds. */
function GroupsTable({
  groups,
  ledgers,
  canEdit,
  canCreate,
  onRename,
  onNew,
}: {
  readonly groups: readonly AccountGroupSummary[];
  readonly ledgers: readonly LedgerSummary[];
  readonly canEdit: boolean;
  readonly canCreate: boolean;
  readonly onRename: (group: AccountGroupSummary) => void;
  readonly onNew: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const paths = useMemo(() => groupPaths(groups), [groups]);
  const counts = useMemo(() => {
    const map = new Map<string, number>();
    for (const ledger of ledgers)
      map.set(ledger.groupCode, (map.get(ledger.groupCode) ?? 0) + 1);
    return map;
  }, [ledgers]);
  const sorted = useMemo(
    () =>
      [...groups].sort((a, b) =>
        (paths.get(a.accountGroupId) ?? '').localeCompare(
          paths.get(b.accountGroupId) ?? '',
        ),
      ),
    [groups, paths],
  );

  const columns: readonly GridColumn<AccountGroupSummary>[] = [
    { key: 'code', header: t('ledgerMaster.code'), value: (g) => g.code },
    {
      key: 'path',
      header: t('ledgerMaster.group'),
      value: (g) => paths.get(g.accountGroupId) ?? g.name,
      wide: true,
    },
    {
      key: 'nature',
      header: t('ledgers.nature'),
      value: (g) => t(`accountNatures.${NATURE_NAME[g.nature]}`),
    },
    {
      key: 'ledgers',
      header: t('ledgerMaster.ledgerCount'),
      value: (g) => counts.get(g.code) ?? 0,
      numeric: true,
    },
    {
      key: 'actions',
      header: '',
      value: () => null,
      render: (g) =>
        canEdit && !g.isSystemGroup ? (
          <span className="flex justify-end">
            <RowAction
              disabled={false}
              label={t('ledgerMaster.rename')}
              onClick={() => onRename(g)}
            />
          </span>
        ) : null,
    },
  ];

  return (
    <div className="space-y-3">
      {canCreate && (
        <div className="flex justify-end sm:hidden">
          <button type="button" className="btn-secondary btn-sm" onClick={onNew}>
            {t('ledgerMaster.newGroup')}
          </button>
        </div>
      )}
      <DataGrid
        gridKey="account-groups"
        rows={sorted}
        columns={columns}
        rowKey={(g) => g.accountGroupId}
      />
    </div>
  );
}
