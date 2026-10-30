import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { Modal } from '@/components/Modal';
import { ReportFrame } from '@/components/ReportFrame';
import { SearchSelect } from '@/components/SearchSelect';
import { StatusBadge, type StatusTone } from '@/components/StatusBadge';
import type { ApiError } from '@/lib/api';
import {
  approveEditRequest,
  EditAction,
  EditDocumentKind,
  EditRequestStatus,
  getEditRequest,
  listEditRequests,
  rejectEditRequest,
  withdrawEditRequest,
  type EditRequestDetail,
  type EditRequestSummary,
} from '@/lib/approvals';
import { listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { moneyAlways } from '@/lib/money';
import { listProducts, type ProductSummary } from '@/lib/products';
import { Alert, Labelled, trim } from '@/pages/StockOperationsPage';

const STATUS_KEYS: Record<number, string> = {
  [EditRequestStatus.pending]: 'pending',
  [EditRequestStatus.approved]: 'approved',
  [EditRequestStatus.rejected]: 'rejected',
  [EditRequestStatus.withdrawn]: 'withdrawn',
};

const STATUS_TONES: Record<number, StatusTone> = {
  [EditRequestStatus.pending]: 'warn',
  [EditRequestStatus.approved]: 'success',
  [EditRequestStatus.rejected]: 'danger',
  [EditRequestStatus.withdrawn]: 'neutral',
};

const KIND_KEYS: Record<number, string> = {
  [EditDocumentKind.salesInvoice]: 'sales',
  [EditDocumentKind.purchaseInvoice]: 'purchase',
  [EditDocumentKind.voucher]: 'voucher',
};

/** Where a document of each kind opens. */
function documentPath(request: EditRequestSummary): string | null {
  switch (request.kind) {
    case EditDocumentKind.salesInvoice:
      return `/sales/invoices?open=${request.documentId}`;
    case EditDocumentKind.purchaseInvoice:
      return `/purchase/invoices?open=${request.documentId}`;
    default:
      return null;
  }
}

/**
 * Changes to posted documents waiting for approval.
 *
 * An approver sees the changes they may decide; anybody sees the ones they asked for, so
 * a clerk can tell whether their correction has been made yet.
 */
export function ApprovalsPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [status, setStatus] = useState<number | ''>(EditRequestStatus.pending);
  const [open, setOpen] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const query = useQuery<readonly EditRequestSummary[], ApiError>({
    queryKey: ['edit-requests', status],
    queryFn: () => listEditRequests(status),
  });

  const statusLabel = (value: number): string =>
    t(`approvals.status.${STATUS_KEYS[value] ?? 'pending'}`);

  const columns: readonly GridColumn<EditRequestSummary>[] = [
    {
      key: 'when',
      header: t('approvals.requestedAt'),
      value: (row) => row.requestedAtUtc,
      render: (row) => (
        <button
          type="button"
          onClick={() => setOpen(row.id)}
          className="cell-link font-mono text-xs"
        >
          {new Date(row.requestedAtUtc).toLocaleString()}
        </button>
      ),
    },
    {
      key: 'document',
      header: t('approvals.document'),
      value: (row) =>
        `${t(`approvals.kinds.${KIND_KEYS[row.kind] ?? 'voucher'}`)} ${row.documentNumber}`,
    },
    {
      key: 'action',
      header: t('approvals.action'),
      value: (row) =>
        row.action === EditAction.cancel
          ? t('approvals.cancelAction')
          : t('approvals.editAction'),
    },
    {
      key: 'by',
      header: t('approvals.requestedBy'),
      value: (row) => row.requestedByName,
    },
    {
      key: 'reason',
      header: t('approvals.reason'),
      value: (row) => row.reason ?? '',
      wide: true,
    },
    {
      key: 'status',
      header: t('approvals.statusLabel'),
      value: (row) => statusLabel(row.status),
      render: (row) => (
        <StatusBadge
          tone={STATUS_TONES[row.status] ?? 'neutral'}
          label={statusLabel(row.status)}
        />
      ),
    },
    {
      key: 'decidedBy',
      header: t('approvals.decidedBy'),
      value: (row) => row.decidedByName ?? '',
    },
  ];

  const controls = (
    <div className="toolbar">
      <Labelled label={t('approvals.statusLabel')}>
        <SearchSelect
          value={status === '' ? '' : String(status)}
          onChange={(next) => setStatus(next === '' ? '' : Number(next))}
          clearable
          size="sm"
          label={t('approvals.statusLabel')}
          placeholder={t('approvals.allStatuses')}
          options={Object.entries(STATUS_KEYS).map(([value, key]) => ({
            value,
            label: t(`approvals.status.${key}`),
          }))}
        />
      </Labelled>
    </div>
  );

  return (
    <>
      <ReportFrame title={t('nav.approvals')} controls={controls} query={query}>
        {(rows) => (
          <div className="space-y-3">
            {notice && <Alert tone="ok">{notice}</Alert>}
            <DataGrid
              gridKey="edit-requests"
              rows={rows}
              columns={columns}
              rowKey={(row) => row.id}
              emptyMessage={t('approvals.none')}
            />
          </div>
        )}
      </ReportFrame>

      {open && (
        <RequestDialog
          id={open}
          onClose={(message) => {
            setOpen(null);
            if (message) {
              setNotice(message);
              window.setTimeout(() => setNotice(null), 4000);
            }
          }}
        />
      )}
    </>
  );
}

function RequestDialog({
  id,
  onClose,
}: {
  readonly id: string;
  readonly onClose: (message?: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [note, setNote] = useState('');
  const [error, setError] = useState<string | null>(null);

  const detail = useQuery<EditRequestDetail, ApiError>({
    queryKey: ['edit-request', id],
    queryFn: () => getEditRequest(id),
  });

  const decide = useMutation<string, ApiError, () => Promise<string>>({
    mutationFn: (action) => action(),
    onSuccess: async (message) => {
      await queryClient.invalidateQueries({ queryKey: ['edit-requests'] });
      onClose(message);
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const request = detail.data?.summary;
  const path = request ? documentPath(request) : null;

  return (
    <Modal
      title={
        request
          ? `${t(`approvals.kinds.${KIND_KEYS[request.kind] ?? 'voucher'}`)} ${request.documentNumber}`
          : t('approvals.request')
      }
      size="wide"
      onClose={() => onClose()}
    >
      {detail.isPending ? (
        <p className="text-sm text-ink-muted">{t('common.loading')}</p>
      ) : detail.error ? (
        <Alert tone="error">{detail.error.detail || detail.error.code}</Alert>
      ) : request ? (
        <div className="space-y-4">
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
            <dt className="text-ink-muted">{t('approvals.action')}</dt>
            <dd>
              {request.action === EditAction.cancel
                ? t('approvals.cancelAction')
                : t('approvals.editAction')}
            </dd>
            <dt className="text-ink-muted">{t('approvals.requestedBy')}</dt>
            <dd>
              {request.requestedByName} ·{' '}
              {new Date(request.requestedAtUtc).toLocaleString()}
            </dd>
            <dt className="text-ink-muted">{t('approvals.reason')}</dt>
            <dd>{request.reason ?? '—'}</dd>
            {request.decidedByName && (
              <>
                <dt className="text-ink-muted">{t('approvals.decidedBy')}</dt>
                <dd>
                  {request.decidedByName}
                  {request.decidedAtUtc
                    ? ` · ${new Date(request.decidedAtUtc).toLocaleString()}`
                    : ''}
                  {request.decisionNote ? ` — ${request.decisionNote}` : ''}
                </dd>
              </>
            )}
          </dl>

          {request.action === EditAction.edit && detail.data && (
            <ProposedChange kind={request.kind} payload={detail.data.payload} />
          )}

          {path && (
            <button
              type="button"
              className="cell-link text-sm"
              onClick={() => navigate(path)}
            >
              {t('approvals.openDocument')}
            </button>
          )}

          {error && <Alert tone="error">{error}</Alert>}

          {(request.canDecide ||
            (request.isOwn && request.status === EditRequestStatus.pending)) && (
            <div className="space-y-3 border-t border-line/60 pt-3">
              {request.canDecide && (
                <label className="field">
                  <span className="field-label">{t('approvals.note')}</span>
                  <input
                    value={note}
                    onChange={(event) => setNote(event.target.value)}
                    placeholder={t('approvals.noteHint')}
                    className="field-input"
                  />
                </label>
              )}
              <div className="flex flex-wrap gap-3">
                {request.canDecide && (
                  <>
                    <button
                      type="button"
                      disabled={decide.isPending}
                      onClick={() =>
                        decide.mutate(async () => {
                          await approveEditRequest(request.id, note.trim() || null);
                          return t('approvals.approvedNotice', {
                            number: request.documentNumber,
                          });
                        })
                      }
                      className="btn-primary"
                    >
                      {t('approvals.approve')}
                    </button>
                    <button
                      type="button"
                      disabled={decide.isPending}
                      onClick={() => {
                        if (!note.trim()) {
                          setError(t('approvals.reasonNeeded'));
                          return;
                        }
                        decide.mutate(async () => {
                          await rejectEditRequest(request.id, note.trim());
                          return t('approvals.rejectedNotice', {
                            number: request.documentNumber,
                          });
                        });
                      }}
                      className="btn-secondary"
                    >
                      {t('approvals.reject')}
                    </button>
                  </>
                )}
                {request.isOwn && request.status === EditRequestStatus.pending && (
                  <button
                    type="button"
                    disabled={decide.isPending}
                    onClick={() =>
                      decide.mutate(async () => {
                        await withdrawEditRequest(request.id);
                        return t('approvals.withdrawnNotice');
                      })
                    }
                    className="btn-secondary"
                  >
                    {t('approvals.withdraw')}
                  </button>
                )}
              </div>
            </div>
          )}
        </div>
      ) : null}
    </Modal>
  );
}

interface ProposedLine {
  readonly productId?: string;
  readonly quantity?: number;
  readonly rate?: number;
  readonly ledgerId?: string;
  readonly side?: number;
  readonly amount?: number;
  readonly narration?: string | null;
}

interface Proposal {
  readonly date?: string;
  readonly narration?: string | null;
  readonly lines?: readonly ProposedLine[];
}

/** The document as the change would leave it: its date and lines, by name. */
function ProposedChange({
  kind,
  payload,
}: {
  readonly kind: number;
  readonly payload: string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const proposal = useMemo<Proposal>(() => {
    try {
      return JSON.parse(payload) as Proposal;
    } catch {
      return {};
    }
  }, [payload]);
  const isVoucher = kind === EditDocumentKind.voucher;

  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', 'picker'],
    queryFn: () => listProducts('', '', false),
    enabled: !isVoucher,
  });
  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => listLedgers(true),
    enabled: isVoucher,
    staleTime: 5 * 60 * 1000,
  });

  const productName = (id: string | undefined): string => {
    const product = products.data?.find((candidate) => candidate.id === id);
    return product ? `${product.code} — ${product.description}` : (id ?? '');
  };
  const ledgerName = (id: string | undefined): string =>
    ledgers.data?.find((candidate) => candidate.ledgerId === id)?.name ?? id ?? '';

  return (
    <div className="space-y-2">
      <h3 className="text-sm font-semibold">{t('approvals.proposed')}</h3>
      {proposal.date && (
        <p className="text-sm">
          {t('approvals.date')}: {proposal.date}
        </p>
      )}
      <div className="overflow-x-auto sm:rounded-xl sm:border sm:border-line">
        <table className="line-table">
          <thead className="bg-surface-2 text-[13px] text-ink-muted">
            {isVoucher ? (
              <tr>
                <th className="px-3 py-2 text-start font-semibold">
                  {t('approvals.ledger')}
                </th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('approvals.debit')}
                </th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('approvals.credit')}
                </th>
              </tr>
            ) : (
              <tr>
                <th className="px-3 py-2 text-start font-semibold">
                  {t('approvals.product')}
                </th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('stock.quantity')}
                </th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('approvals.rate')}
                </th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('approvals.amount')}
                </th>
              </tr>
            )}
          </thead>
          <tbody>
            {(proposal.lines ?? []).map((line, index) =>
              isVoucher ? (
                <tr key={index} className="border-t border-line">
                  <td data-label={t('approvals.ledger')} className="px-3 py-2">
                    {ledgerName(line.ledgerId)}
                  </td>
                  <td data-label={t('approvals.debit')} className="cell-numeric">
                    {line.side === 1 ? moneyAlways(line.amount ?? 0) : ''}
                  </td>
                  <td data-label={t('approvals.credit')} className="cell-numeric">
                    {line.side === 2 ? moneyAlways(line.amount ?? 0) : ''}
                  </td>
                </tr>
              ) : (
                <tr key={index} className="border-t border-line">
                  <td data-label={t('approvals.product')} className="px-3 py-2">
                    {productName(line.productId)}
                  </td>
                  <td data-label={t('stock.quantity')} className="cell-numeric">
                    {trim(line.quantity ?? 0)}
                  </td>
                  <td data-label={t('approvals.rate')} className="cell-numeric">
                    {moneyAlways(line.rate ?? 0)}
                  </td>
                  <td data-label={t('approvals.amount')} className="cell-numeric">
                    {moneyAlways((line.quantity ?? 0) * (line.rate ?? 0))}
                  </td>
                </tr>
              ),
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
