import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { Modal } from '@/components/Modal';
import { ReportFrame } from '@/components/ReportFrame';
import { SearchSelect } from '@/components/SearchSelect';
import { StatusBadge, type StatusTone } from '@/components/StatusBadge';
import type { ApiError } from '@/lib/api';
import {
  auditChanges,
  fetchAuditLogFilters,
  listAuditLog,
  type AuditLogEntry,
  type AuditLogFilters,
  type AuditLogPage,
} from '@/lib/auditLog';
import { Alert, Labelled } from '@/pages/StockOperationsPage';

const PAGE_SIZE = 50;

const ACTIONS = ['Create', 'Update', 'Delete'] as const;

const ACTION_TONES: Record<string, StatusTone> = {
  Create: 'success',
  Update: 'info',
  Delete: 'danger',
};

/**
 * The audit trail: every master and transaction created, changed or deleted, by whom and
 * when, with what it said before and after. Read-only - nothing here, or anywhere, changes
 * an entry once it is written.
 */
export function AuditLogPage(): React.JSX.Element {
  const { t } = useTranslation();

  const [module, setModule] = useState('');
  const [userId, setUserId] = useState('');
  const [action, setAction] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<AuditLogEntry | null>(null);

  const filters = useQuery<AuditLogFilters, ApiError>({
    queryKey: ['audit-log-filters'],
    queryFn: fetchAuditLogFilters,
    staleTime: 60 * 1000,
  });

  const query = useQuery<AuditLogPage, ApiError>({
    queryKey: ['audit-log', module, userId, action, from, to, search, page],
    queryFn: () =>
      listAuditLog({
        module,
        userId,
        action,
        from,
        to,
        search,
        page,
        pageSize: PAGE_SIZE,
      }),
    placeholderData: keepPreviousData,
  });

  // A new filter starts the list again from its first page.
  const filtered =
    <TValue,>(set: (value: TValue) => void) =>
    (value: TValue): void => {
      set(value);
      setPage(1);
    };

  const actionLabel = (value: string): string => t(`auditLog.actions.${value}`, value);

  const columns: readonly GridColumn<AuditLogEntry>[] = [
    {
      key: 'when',
      header: t('auditLog.when'),
      value: (row) => row.occurredAtUtc,
      render: (row) => (
        <button
          type="button"
          onClick={() => setOpen(row)}
          className="cell-link font-mono text-xs"
        >
          {new Date(row.occurredAtUtc).toLocaleString()}
        </button>
      ),
    },
    { key: 'user', header: t('auditLog.user'), value: (row) => row.userName },
    { key: 'module', header: t('auditLog.module'), value: (row) => row.module },
    {
      key: 'action',
      header: t('auditLog.action'),
      value: (row) => actionLabel(row.action),
      render: (row) => (
        <StatusBadge
          tone={ACTION_TONES[row.action] ?? 'neutral'}
          label={actionLabel(row.action)}
        />
      ),
    },
    {
      key: 'record',
      header: t('auditLog.record'),
      value: (row) => row.recordLabel ?? row.recordId,
      wide: true,
    },
    {
      key: 'fields',
      header: t('auditLog.fields'),
      value: (row) => auditChanges(row).length,
      numeric: true,
    },
  ];

  const controls = (
    <div className="toolbar">
      <Labelled label={t('auditLog.search')}>
        <input
          type="search"
          value={search}
          onChange={(event) => filtered(setSearch)(event.target.value)}
          placeholder={t('auditLog.searchHint')}
          className="field-input-sm min-w-56"
        />
      </Labelled>
      <Labelled label={t('auditLog.module')}>
        <SearchSelect
          value={module}
          onChange={filtered(setModule)}
          clearable
          size="sm"
          label={t('auditLog.module')}
          placeholder={t('auditLog.allModules')}
          options={(filters.data?.modules ?? []).map((name) => ({
            value: name,
            label: name,
          }))}
        />
      </Labelled>
      <Labelled label={t('auditLog.user')}>
        <SearchSelect
          value={userId}
          onChange={filtered(setUserId)}
          clearable
          size="sm"
          label={t('auditLog.user')}
          placeholder={t('auditLog.allUsers')}
          options={(filters.data?.users ?? []).map((user) => ({
            value: user.userId,
            label: user.userName,
          }))}
        />
      </Labelled>
      <Labelled label={t('auditLog.action')}>
        <SearchSelect
          value={action}
          onChange={filtered(setAction)}
          clearable
          size="sm"
          label={t('auditLog.action')}
          placeholder={t('auditLog.allActions')}
          options={ACTIONS.map((value) => ({ value, label: actionLabel(value) }))}
        />
      </Labelled>
      <Labelled label={t('reports.from')}>
        <input
          type="date"
          value={from}
          onChange={(event) => filtered(setFrom)(event.target.value)}
          className="field-input-sm"
        />
      </Labelled>
      <Labelled label={t('reports.to')}>
        <input
          type="date"
          value={to}
          onChange={(event) => filtered(setTo)(event.target.value)}
          className="field-input-sm"
        />
      </Labelled>
    </div>
  );

  return (
    <>
      <ReportFrame
        title={t('nav.auditLog')}
        controls={controls}
        query={query}
        banner={
          filters.data && !filters.data.isSeparateDatabase ? (
            <Alert tone="error">{t('auditLog.sharedDatabase')}</Alert>
          ) : undefined
        }
      >
        {(data) => (
          <DataGrid
            gridKey="audit-log"
            rows={data.items}
            columns={columns}
            rowKey={(row) => row.id}
            emptyMessage={t('auditLog.none')}
            paging={{
              page: data.page,
              pageSize: data.pageSize,
              totalCount: data.totalCount,
              totalPages: Math.max(1, Math.ceil(data.totalCount / data.pageSize)),
              onPageChange: setPage,
            }}
          />
        )}
      </ReportFrame>

      {open && (
        <Modal
          title={`${open.module} · ${open.recordLabel ?? open.recordId}`}
          size="wide"
          onClose={() => setOpen(null)}
        >
          <EntryDetail entry={open} actionLabel={actionLabel} />
        </Modal>
      )}
    </>
  );
}

function EntryDetail({
  entry,
  actionLabel,
}: {
  readonly entry: AuditLogEntry;
  readonly actionLabel: (value: string) => string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const changes = auditChanges(entry);

  return (
    <div className="space-y-4">
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-ink-muted">{t('auditLog.when')}</dt>
        <dd>{new Date(entry.occurredAtUtc).toLocaleString()}</dd>
        <dt className="text-ink-muted">{t('auditLog.user')}</dt>
        <dd>{entry.userName}</dd>
        <dt className="text-ink-muted">{t('auditLog.action')}</dt>
        <dd>
          <StatusBadge
            tone={ACTION_TONES[entry.action] ?? 'neutral'}
            label={actionLabel(entry.action)}
          />
        </dd>
        <dt className="text-ink-muted">{t('auditLog.recordId')}</dt>
        <dd className="font-mono text-xs break-all">{entry.recordId}</dd>
      </dl>

      <div className="overflow-x-auto sm:rounded-xl sm:border sm:border-line">
        <table className="line-table">
          <thead className="bg-surface-2 text-[13px] text-ink-muted">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">
                {t('auditLog.field')}
              </th>
              <th className="px-3 py-2 text-start font-semibold">
                {t('auditLog.before')}
              </th>
              <th className="px-3 py-2 text-start font-semibold">
                {t('auditLog.after')}
              </th>
            </tr>
          </thead>
          <tbody>
            {changes.map((change) => (
              <tr key={change.field} className="border-t border-line align-top">
                <td data-label={t('auditLog.field')} className="px-3 py-2 font-medium">
                  {change.field}
                </td>
                <td data-label={t('auditLog.before')} className="px-3 py-2">
                  <Value value={change.before} />
                </td>
                <td data-label={t('auditLog.after')} className="px-3 py-2">
                  <Value value={change.after} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/** A value as it was recorded: plain where it is one, laid out where it is a list of lines. */
function Value({ value }: { readonly value: unknown }): React.JSX.Element {
  if (value === undefined) {
    return <span className="text-ink-muted">—</span>;
  }

  if (value === null) {
    return <span className="text-ink-muted italic">null</span>;
  }

  if (typeof value === 'object') {
    return (
      <pre className="max-h-64 overflow-auto rounded-md bg-surface-3 p-2 text-xs whitespace-pre-wrap">
        {JSON.stringify(value, null, 2)}
      </pre>
    );
  }

  return <span className="break-words">{String(value)}</span>;
}
