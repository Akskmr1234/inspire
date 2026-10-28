import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { Modal } from '@/components/Modal';
import { ReportFrame, ReportSkeleton } from '@/components/ReportFrame';
import { SearchSelect } from '@/components/SearchSelect';
import { productOption, stockByProduct } from '@/components/DocumentLines';
import type { ApiError } from '@/lib/api';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import { getPolicies, type FirmPolicies } from '@/lib/policies';
import { listProducts, type ProductSummary } from '@/lib/products';
import { addScanned, BarcodeScan } from '@/components/BarcodeScan';
import {
  createStockDocument,
  fetchStockValuation,
  StockDocumentType,
  type StockValuationReport,
} from '@/lib/stock';
import {
  approveStockTransfer,
  cancelStockTransfer,
  createStockTransfer,
  dispatchStockTransfer,
  getStockTransfer,
  listStockTransfers,
  receiveStockTransfer,
  rejectStockTransfer,
  StockTransferStatus,
  TRANSFER_STATUSES,
  updateStockTransfer,
  type StockTransferDetail,
  type StockTransferInput,
  type StockTransferSummary,
} from '@/lib/stockTransfers';
import { useSession } from '@/stores/session';
import { Alert, Labelled, StockEntry, trim } from '@/pages/StockOperationsPage';
import { shortcutProps } from '@/lib/shortcuts';

/**
 * Transfers between branches.
 *
 * A request is a conversation between two warehouses: one asks, the other agrees to
 * what it can spare, sends it, and the first receives it. Each step is a button on the
 * request for whoever holds the permission for it, so the people at each end find
 * what is waiting on them in one list rather than in a stock register of every kind
 * of movement.
 *
 * Direct transfers stay available from here where the firm allows them, because
 * somebody moving stock between two shelves of the same shop does not need anybody's
 * agreement to do it.
 */
export function StockTransfersPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const can = useSession((state) => state.can);

  const today = new Date().toISOString().slice(0, 10);
  const monthStart = `${today.slice(0, 7)}-01`;

  const [from, setFrom] = useState(monthStart);
  const [to, setTo] = useState(today);
  const [statusFilter, setStatusFilter] = useState<number | ''>('');
  const [warehouseFilter, setWarehouseFilter] = useState('');
  const [entering, setEntering] = useState<'request' | 'direct' | null>(null);
  const [viewing, setViewing] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const query = useQuery<readonly StockTransferSummary[], ApiError>({
    queryKey: ['stock-transfers', from, to, statusFilter, warehouseFilter],
    queryFn: () => listStockTransfers(from, to, statusFilter, warehouseFilter),
  });

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });

  const policies = useQuery<FirmPolicies, ApiError>({
    queryKey: ['firm-policies'],
    queryFn: getPolicies,
    staleTime: 60 * 1000,
  });

  // Goods in transit is the road, not a place anybody asks for goods from.
  const places = useMemo(
    () => (warehouses.data ?? []).filter((warehouse) => !warehouse.isTransit),
    [warehouses.data],
  );

  const mutation = useMutation<string | null, ApiError, () => Promise<string | null>>({
    mutationFn: (action) => action(),
    onSuccess: async (message) => {
      setError(null);
      setNotice(message);
      window.setTimeout(() => setNotice(null), 4000);

      await queryClient.invalidateQueries({ queryKey: ['stock-transfers'] });
      await queryClient.invalidateQueries({ queryKey: ['stock-transfer'] });
      await queryClient.invalidateQueries({ queryKey: ['stock-documents'] });
      await queryClient.invalidateQueries({ queryKey: ['stock-valuation'] });
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const run = (action: () => Promise<string | null>): void => {
    setError(null);
    mutation.mutate(action);
  };

  const directAllowed =
    policies.data?.transfersRequireApproval !== true &&
    can('inventory:stock-adjustment:create');

  const columns: readonly GridColumn<StockTransferSummary>[] = [
    {
      key: 'number',
      header: t('transfers.number'),
      value: (row) => row.number,
      render: (row) => (
        <button type="button" onClick={() => setViewing(row.id)} className="cell-link">
          {row.number}
        </button>
      ),
    },
    { key: 'date', header: t('stock.date'), value: (row) => row.date },
    { key: 'from', header: t('transfers.from'), value: (row) => row.fromWarehouseName },
    { key: 'to', header: t('transfers.to'), value: (row) => row.toWarehouseName },
    {
      key: 'status',
      header: t('stock.status'),
      value: (row) => t(transferStatusKey(row.status)),
      render: (row) => <StatusBadge status={row.status} />,
    },
    {
      key: 'lines',
      header: t('stock.lines'),
      value: (row) => row.lineCount,
      numeric: true,
    },
    {
      key: 'requested',
      header: t('transfers.requested'),
      value: (row) => row.requestedQuantity,
      numeric: true,
      render: (row) => trim(row.requestedQuantity),
    },
    {
      key: 'dispatched',
      header: t('transfers.dispatched'),
      value: (row) => row.dispatchedQuantity,
      numeric: true,
      render: (row) => trim(row.dispatchedQuantity),
    },
    {
      key: 'dispatchNumber',
      header: t('transfers.dispatchNumber'),
      value: (row) => row.dispatchNumber ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'receiptNumber',
      header: t('transfers.receiptNumber'),
      value: (row) => row.receiptNumber ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'narration',
      header: t('stock.narration'),
      value: (row) => row.narration ?? '',
      hiddenByDefault: true,
    },
  ];

  const controls = (
    <div className="toolbar">
      <Labelled label={t('reports.from')}>
        <input
          type="date"
          value={from}
          onChange={(event) => setFrom(event.target.value)}
          className="field-input-sm"
        />
      </Labelled>
      <Labelled label={t('reports.to')}>
        <input
          type="date"
          value={to}
          onChange={(event) => setTo(event.target.value)}
          className="field-input-sm"
        />
      </Labelled>
      <Labelled label={t('stock.status')}>
        <SearchSelect
          value={statusFilter === '' ? '' : String(statusFilter)}
          onChange={(next) => setStatusFilter(next === '' ? '' : Number(next))}
          clearable
          size="sm"
          label={t('stock.status')}
          placeholder={t('transfers.allStatuses')}
          options={TRANSFER_STATUSES.map((status) => ({
            value: String(status),
            label: t(transferStatusKey(status)),
          }))}
        />
      </Labelled>
      <Labelled label={t('stock.warehouse')}>
        <SearchSelect
          value={warehouseFilter}
          onChange={setWarehouseFilter}
          clearable
          size="sm"
          label={t('stock.warehouse')}
          placeholder={t('stock.allWarehouses')}
          options={places.map((warehouse) => ({
            value: warehouse.id,
            label: warehouse.name,
          }))}
        />
      </Labelled>
    </div>
  );

  if (viewing) {
    return (
      <TransferView
        requestId={viewing}
        warehouses={places}
        busy={mutation.isPending}
        error={error}
        notice={notice}
        run={run}
        onClose={() => {
          setViewing(null);
          setError(null);
        }}
      />
    );
  }

  return (
    <>
      <ReportFrame title={t('nav.stockTransfers')} controls={controls} query={query}>
        {(rows) => (
          <div className="space-y-3">
            {policies.data?.transfersRequireApproval && (
              <Alert tone="muted">{t('transfers.requestsRequired')}</Alert>
            )}
            {error && !entering && <Alert tone="error">{error}</Alert>}
            {notice && <Alert tone="ok">{notice}</Alert>}

            <DataGrid
              gridKey="stock-transfers"
              rows={rows}
              columns={columns}
              rowKey={(row) => row.id}
              emptyMessage={t('transfers.noneFound')}
              actions={
                <div className="flex flex-wrap gap-2">
                  {directAllowed && (
                    <button
                      type="button"
                      onClick={() => setEntering('direct')}
                      className="btn-secondary btn-sm"
                    >
                      {t('transfers.direct')}
                    </button>
                  )}
                  {can('inventory:stock-transfer:create') && (
                    <GridAction
                      label={t('transfers.new')}
                      onClick={() => setEntering('request')}
                    />
                  )}
                </div>
              }
            />
          </div>
        )}
      </ReportFrame>

      {entering === 'request' && (
        <Modal title={t('transfers.new')} size="full" onClose={() => setEntering(null)}>
          {error && <Alert tone="error">{error}</Alert>}
          <RequestForm
            warehouses={places}
            busy={mutation.isPending}
            onCancel={() => setEntering(null)}
            onSubmit={(body) =>
              run(async () => {
                const raised = await createStockTransfer(body);
                setEntering(null);
                setViewing(raised.stockTransferRequestId);
                return t('transfers.raisedNotice', { number: raised.number });
              })
            }
          />
        </Modal>
      )}

      {entering === 'direct' && (
        <Modal
          title={t('transfers.direct')}
          size="full"
          onClose={() => setEntering(null)}
        >
          {error && <Alert tone="error">{error}</Alert>}
          <StockEntry
            warehouses={places}
            lockedType={StockDocumentType.stockTransfer}
            busy={mutation.isPending}
            onCancel={() => setEntering(null)}
            onSubmit={(body) =>
              run(async () => {
                const created = await createStockDocument(body);
                setEntering(null);
                return t('stock.postedNotice', {
                  number: created.number,
                  count: created.movements,
                });
              })
            }
          />
        </Modal>
      )}
    </>
  );
}

/** One request: what was asked, what was agreed, what went, and the next step. */
function TransferView({
  requestId,
  warehouses,
  busy,
  error,
  notice,
  run,
  onClose,
}: {
  readonly requestId: string;
  readonly warehouses: readonly WarehouseSummary[];
  readonly busy: boolean;
  readonly error: string | null;
  readonly notice: string | null;
  readonly run: (action: () => Promise<string | null>) => void;
  readonly onClose: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const can = useSession((state) => state.can);
  const today = new Date().toISOString().slice(0, 10);

  const [dialog, setDialog] = useState<
    'edit' | 'approve' | 'reject' | 'cancel' | 'dispatch' | 'receive' | null
  >(null);

  const query = useQuery<StockTransferDetail, ApiError>({
    queryKey: ['stock-transfer', requestId],
    queryFn: () => getStockTransfer(requestId),
  });

  if (query.isPending) {
    return (
      <section className="page" aria-busy="true">
        <div className="skeleton h-7 w-64 rounded" />
        <ReportSkeleton rows={4} />
      </section>
    );
  }

  if (query.isError || !query.data) {
    return <Alert tone="error">{query.error?.detail ?? t('transfers.notFound')}</Alert>;
  }

  const detail = query.data;
  const { summary } = detail;
  const status = summary.status;

  const open =
    status === StockTransferStatus.requested || status === StockTransferStatus.approved;

  // Each step closes the dialog it came from once the server has it. A failure leaves
  // the dialog open, so what was entered is not lost to a refusal about one line.
  const act = (action: () => Promise<string>): void =>
    run(async () => {
      const message = await action();
      setDialog(null);
      return message;
    });

  return (
    <section className="page">
      <header className="page-header sm:items-center">
        <div className="min-w-0">
          <h2 className="page-title flex flex-wrap items-center gap-2">
            {summary.number} <StatusBadge status={status} />
          </h2>
          <p className="text-xs text-ink-muted">
            {summary.date} · {summary.fromWarehouseName} → {summary.toWarehouseName}
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          {status === StockTransferStatus.requested &&
            can('inventory:stock-transfer:create') && (
              <button
                type="button"
                onClick={() => setDialog('edit')}
                className="btn-secondary"
              >
                {t('transfers.edit')}
              </button>
            )}
          {status === StockTransferStatus.requested &&
            can('inventory:stock-transfer:approve') && (
              <button
                type="button"
                onClick={() => setDialog('approve')}
                className="btn-primary"
              >
                {t('transfers.approve')}
              </button>
            )}
          {status === StockTransferStatus.approved &&
            can('inventory:stock-transfer:edit') && (
              <button
                type="button"
                onClick={() => setDialog('dispatch')}
                className="btn-primary"
              >
                {t('transfers.dispatch')}
              </button>
            )}
          {status === StockTransferStatus.dispatched &&
            can('inventory:stock-transfer:edit') && (
              <button
                type="button"
                onClick={() => setDialog('receive')}
                className="btn-primary"
              >
                {t('transfers.receive')}
              </button>
            )}
          {open && can('inventory:stock-transfer:approve') && (
            <button
              type="button"
              onClick={() => setDialog('reject')}
              className="btn-secondary"
            >
              {t('transfers.reject')}
            </button>
          )}
          {open && can('inventory:stock-transfer:delete') && (
            <button
              type="button"
              onClick={() => setDialog('cancel')}
              className="btn-secondary"
            >
              {t('transfers.cancelRequest')}
            </button>
          )}
          <button type="button" onClick={onClose} className="btn-secondary">
            {t('products.backToList')}
          </button>
        </div>
      </header>

      {error && !dialog && <Alert tone="error">{error}</Alert>}
      {notice && <Alert tone="ok">{notice}</Alert>}

      <Steps detail={detail} />

      {summary.narration && (
        <p className="text-sm">
          <span className="text-ink-muted">{t('stock.narration')}: </span>
          {summary.narration}
        </p>
      )}
      {detail.reason && (
        <Alert tone="muted">
          {t('transfers.reason')}: {detail.reason}
        </Alert>
      )}

      <div className="overflow-x-auto sm:rounded-xl sm:border sm:border-line sm:bg-surface sm:shadow-card">
        <table className="line-table">
          <thead className="bg-surface-3 text-xs tracking-wide text-ink-muted uppercase">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.product')}</th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('transfers.requested')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('transfers.approved')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('transfers.dispatched')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('transfers.availableAtSource')}
              </th>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.remarks')}</th>
            </tr>
          </thead>
          <tbody>
            {detail.lines.map((line) => (
              <tr key={line.productId} className="border-t border-line">
                <td data-label={t('stock.product')} className="px-3 py-1.5">
                  <span className="font-mono text-xs text-ink-muted">
                    {line.productCode}
                  </span>{' '}
                  {line.productName}
                </td>
                <td data-label={t('transfers.requested')} className="cell-numeric">
                  {trim(line.quantity)} {line.unitCode}
                </td>
                <td data-label={t('transfers.approved')} className="cell-numeric">
                  {status === StockTransferStatus.requested
                    ? '—'
                    : `${trim(line.approvedQuantity)} ${line.stockUnitCode}`}
                </td>
                <td data-label={t('transfers.dispatched')} className="cell-numeric">
                  {line.dispatchedQuantity > 0
                    ? `${trim(line.dispatchedQuantity)} ${line.stockUnitCode}`
                    : '—'}
                </td>
                <td
                  data-label={t('transfers.availableAtSource')}
                  className={clsx(
                    'cell-numeric',
                    open &&
                      line.availableAtSource < line.stockQuantity &&
                      'text-amber-600',
                  )}
                >
                  {trim(line.availableAtSource)} {line.stockUnitCode}
                </td>
                <td data-label={t('stock.remarks')} className="px-3 py-1.5">
                  {line.remarks ?? ''}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {dialog === 'edit' && (
        <Modal title={t('transfers.edit')} size="full" onClose={() => setDialog(null)}>
          {error && <Alert tone="error">{error}</Alert>}
          <RequestForm
            warehouses={warehouses}
            initial={detail}
            busy={busy}
            onCancel={() => setDialog(null)}
            onSubmit={(body) =>
              act(async () => {
                await updateStockTransfer(requestId, body);
                return t('transfers.savedNotice');
              })
            }
          />
        </Modal>
      )}

      {dialog === 'approve' && (
        <Modal title={t('transfers.approve')} size="wide" onClose={() => setDialog(null)}>
          {error && <Alert tone="error">{error}</Alert>}
          <ApproveForm
            detail={detail}
            busy={busy}
            onCancel={() => setDialog(null)}
            onSubmit={(lines) =>
              act(async () => {
                await approveStockTransfer(requestId, lines);
                return t('transfers.approvedNotice', { number: summary.number });
              })
            }
          />
        </Modal>
      )}

      {(dialog === 'reject' || dialog === 'cancel') && (
        <Modal
          title={
            dialog === 'reject' ? t('transfers.reject') : t('transfers.cancelRequest')
          }
          size="form"
          onClose={() => setDialog(null)}
        >
          {error && <Alert tone="error">{error}</Alert>}
          <ReasonForm
            busy={busy}
            onCancel={() => setDialog(null)}
            onSubmit={(reason) =>
              act(async () => {
                if (dialog === 'reject') {
                  await rejectStockTransfer(requestId, reason);
                  return t('transfers.rejectedNotice', { number: summary.number });
                }

                await cancelStockTransfer(requestId, reason);
                return t('transfers.cancelledNotice', { number: summary.number });
              })
            }
          />
        </Modal>
      )}

      {dialog === 'dispatch' && (
        <Modal
          title={t('transfers.dispatch')}
          size="full"
          onClose={() => setDialog(null)}
        >
          {error && <Alert tone="error">{error}</Alert>}
          <StockEntry
            warehouses={warehouses}
            lockedType={StockDocumentType.stockTransfer}
            fixedWarehouseId={summary.fromWarehouseId}
            dispatching
            initialLines={detail.lines
              .filter((line) => line.approvedQuantity > 0)
              .map((line) => ({
                productId: line.productId,
                quantity: line.approvedQuantity,
                remarks: line.remarks,
              }))}
            busy={busy}
            onCancel={() => setDialog(null)}
            onSubmit={(body) =>
              act(async () => {
                const sent = await dispatchStockTransfer(requestId, {
                  date: body.date,
                  lines: body.lines,
                  narration: body.narration ?? null,
                });
                return t('transfers.dispatchedNotice', {
                  number: summary.number,
                  document: sent.documentNumber ?? '',
                });
              })
            }
          />
        </Modal>
      )}

      {dialog === 'receive' && (
        <Modal title={t('transfers.receive')} size="form" onClose={() => setDialog(null)}>
          {error && <Alert tone="error">{error}</Alert>}
          <ReceiveForm
            defaultDate={today}
            busy={busy}
            onCancel={() => setDialog(null)}
            onSubmit={(date) =>
              act(async () => {
                const received = await receiveStockTransfer(requestId, date);
                return t('transfers.receivedNotice', {
                  number: summary.number,
                  document: received.documentNumber ?? '',
                });
              })
            }
          />
        </Modal>
      )}
    </section>
  );
}

/** The four steps of a request, with the ones done marked and the document each raised. */
function Steps({ detail }: { readonly detail: StockTransferDetail }): React.JSX.Element {
  const { t } = useTranslation();
  const status = detail.summary.status;

  const stopped =
    status === StockTransferStatus.rejected || status === StockTransferStatus.cancelled;

  const steps = [
    {
      label: t('transfers.statusRequested'),
      done: true,
      when: detail.createdAtUtc,
      note: null,
    },
    {
      label: t('transfers.statusApproved'),
      done: status >= StockTransferStatus.approved && !stopped,
      when: stopped ? null : detail.decidedAtUtc,
      note: null,
    },
    {
      label: t('transfers.statusDispatched'),
      done: status >= StockTransferStatus.dispatched && !stopped,
      when: detail.dispatchedAtUtc,
      note: detail.summary.dispatchNumber,
    },
    {
      label: t('transfers.statusReceived'),
      done: status === StockTransferStatus.received,
      when: detail.receivedAtUtc,
      note: detail.summary.receiptNumber,
    },
  ];

  return (
    <ol className="grid grid-cols-2 gap-2 sm:grid-cols-4">
      {steps.map((step, index) => (
        <li
          key={step.label}
          className={clsx(
            'rounded-xl border px-3 py-2 text-sm',
            step.done
              ? 'border-emerald-200 bg-emerald-50 text-emerald-800 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-200'
              : 'border-line bg-surface-2 text-ink-muted',
          )}
        >
          <div className="font-medium">
            {index + 1}. {step.label}
          </div>
          {step.done && step.when && (
            <div className="text-xs opacity-80">
              {new Date(step.when).toLocaleString()}
            </div>
          )}
          {step.note && <div className="font-mono text-xs">{step.note}</div>}
        </li>
      ))}
    </ol>
  );
}

/** A line of a request being typed. */
interface RequestLine {
  readonly key: string;
  productId: string;
  quantity: string;
  remarks: string;
}

function blankLine(): RequestLine {
  return { key: crypto.randomUUID(), productId: '', quantity: '', remarks: '' };
}

/** Raising or changing a request: two warehouses, a date and the products wanted. */
function RequestForm({
  warehouses,
  initial,
  busy,
  onCancel,
  onSubmit,
}: {
  readonly warehouses: readonly WarehouseSummary[];
  readonly initial?: StockTransferDetail;
  readonly busy: boolean;
  readonly onCancel: () => void;
  readonly onSubmit: (body: StockTransferInput) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const [date, setDate] = useState(
    initial?.summary.date ?? new Date().toISOString().slice(0, 10),
  );
  const [fromId, setFromId] = useState(initial?.summary.fromWarehouseId ?? '');
  const [toId, setToId] = useState(
    initial?.summary.toWarehouseId ??
      warehouses.find((warehouse) => warehouse.isDefault)?.id ??
      '',
  );
  const [narration, setNarration] = useState(initial?.summary.narration ?? '');
  const [lines, setLines] = useState<readonly RequestLine[]>(() =>
    initial
      ? initial.lines.map((line) => ({
          key: crypto.randomUUID(),
          productId: line.productId,
          quantity: String(line.stockQuantity),
          remarks: line.remarks ?? '',
        }))
      : [blankLine()],
  );

  const [problem, setProblem] = useState<string | null>(null);

  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', '', '', false],
    queryFn: () => listProducts('', '', false),
  });

  // What the sending warehouse holds, beside each product: the requester is asking for
  // what is on someone else's shelf, and should see whether it is there.
  const valuation = useQuery<StockValuationReport, ApiError>({
    queryKey: ['stock-valuation', 'picker', fromId],
    queryFn: () => fetchStockValuation(fromId, '', true),
    enabled: fromId !== '',
    staleTime: 60 * 1000,
  });

  const onHand = useMemo(() => stockByProduct(valuation.data?.rows), [valuation.data]);

  const productOptions = useMemo(
    () =>
      (products.data ?? [])
        .filter((product) => product.itemType === 1)
        .map((product) => productOption(product, onHand.get(product.id) ?? 0)),
    [products.data, onHand],
  );

  const update = (key: string, patch: Partial<RequestLine>): void =>
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        const entered = lines
          .filter((line) => line.productId && Number(line.quantity) > 0)
          .map((line) => ({
            productId: line.productId,
            quantity: Number(line.quantity),
            remarks: line.remarks.trim() || null,
          }));

        // Said rather than silently ignored: a Send that does nothing reads as broken.
        if (!fromId || !toId) {
          setProblem(t('transfers.chooseWarehouses'));
          return;
        }

        if (entered.length === 0) {
          setProblem(t('transfers.addAProduct'));
          return;
        }

        setProblem(null);

        onSubmit({
          date,
          fromWarehouseId: fromId,
          toWarehouseId: toId,
          narration: narration.trim() || null,
          lines: entered,
        });
      }}
    >
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <label className="block">
          <span className="field-label">{t('stock.date')}</span>
          <input
            type="date"
            value={date}
            onChange={(event) => setDate(event.target.value)}
            className="field-input"
            required
          />
        </label>
        <label className="block">
          <span className="field-label">{t('transfers.askFrom')}</span>
          <SearchSelect
            value={fromId}
            onChange={setFromId}
            label={t('transfers.askFrom')}
            placeholder={t('stock.choose')}
            options={warehouses
              .filter((warehouse) => warehouse.id !== toId)
              .map((warehouse) => ({ value: warehouse.id, label: warehouse.name }))}
          />
        </label>
        <label className="block">
          <span className="field-label">{t('transfers.deliverTo')}</span>
          <SearchSelect
            value={toId}
            onChange={setToId}
            label={t('transfers.deliverTo')}
            placeholder={t('stock.choose')}
            options={warehouses
              .filter((warehouse) => warehouse.id !== fromId)
              .map((warehouse) => ({ value: warehouse.id, label: warehouse.name }))}
          />
        </label>
        <label className="block">
          <span className="field-label">{t('stock.narration')}</span>
          <input
            value={narration}
            onChange={(event) => setNarration(event.target.value)}
            className="field-input"
          />
        </label>
      </div>

      <BarcodeScan
        products={products.data ?? []}
        onScan={(product) => {
          const next = addScanned(lines, product, () => ({
            ...blankLine(),
            productId: product.id,
          }));
          setLines(next.lines);
          return next.quantity;
        }}
      />

      <div className="overflow-x-auto sm:rounded-xl sm:border sm:border-line sm:bg-surface sm:shadow-card">
        <table className="line-table">
          <thead className="bg-surface-3 text-xs tracking-wide text-ink-muted uppercase">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.product')}</th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('transfers.quantityWanted')}
              </th>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.remarks')}</th>
              <th className="text-end" />
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.key} className="border-t border-line">
                <td data-label={t('stock.product')} className="min-w-64 px-2 py-1">
                  <SearchSelect
                    value={line.productId}
                    onChange={(productId) => update(line.key, { productId })}
                    options={productOptions}
                    size="sm"
                    label={t('stock.product')}
                    placeholder={t('stock.choose')}
                  />
                </td>
                <td data-label={t('transfers.quantityWanted')} className="px-2 py-1">
                  <input
                    type="number"
                    step="any"
                    min="0"
                    value={line.quantity}
                    onChange={(event) =>
                      update(line.key, { quantity: event.target.value })
                    }
                    className="field-input-sm text-end font-mono tabular-nums"
                  />
                </td>
                <td data-label={t('stock.remarks')} className="px-2 py-1">
                  <input
                    value={line.remarks}
                    onChange={(event) =>
                      update(line.key, { remarks: event.target.value })
                    }
                    className="field-input-sm"
                  />
                </td>
                <td className="px-2 py-1 text-end">
                  <button
                    type="button"
                    disabled={lines.length === 1}
                    onClick={() =>
                      setLines((current) =>
                        current.filter((candidate) => candidate.key !== line.key),
                      )
                    }
                    className="rounded-md border border-line px-2 py-0.5 text-xs font-medium text-ink-muted transition hover:bg-surface-3 disabled:pointer-events-none disabled:opacity-40"
                  >
                    {t('stock.removeLine')}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {problem && <Alert tone="error">{problem}</Alert>}

      <div className="flex flex-wrap items-center gap-3 border-t border-line/60 pt-3">
        <button
          {...shortcutProps('add-line')}
          type="button"
          onClick={() => setLines((current) => [...current, blankLine()])}
          className="btn-secondary"
        >
          {t('stock.addLine')}
        </button>
        <button type="submit" disabled={busy} className="btn-primary ms-auto">
          {initial ? t('transfers.saveChanges') : t('transfers.send')}
        </button>
        <button type="button" onClick={onCancel} className="btn-secondary">
          {t('products.cancel')}
        </button>
      </div>
    </form>
  );
}

/**
 * Agreeing to a request, line by line.
 *
 * Each line starts at what was asked for, or at what the shelf holds where that is
 * less: an approver agreeing to send goods that are not there would only move the
 * refusal to the storekeeper.
 */
function ApproveForm({
  detail,
  busy,
  onCancel,
  onSubmit,
}: {
  readonly detail: StockTransferDetail;
  readonly busy: boolean;
  readonly onCancel: () => void;
  readonly onSubmit: (lines: readonly { productId: string; quantity: number }[]) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const [agreed, setAgreed] = useState<Record<string, string>>(() =>
    Object.fromEntries(
      detail.lines.map((line) => [
        line.productId,
        String(Math.max(0, Math.min(line.stockQuantity, line.availableAtSource))),
      ]),
    ),
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit(
          detail.lines.map((line) => ({
            productId: line.productId,
            quantity: Number(agreed[line.productId] ?? 0),
          })),
        );
      }}
    >
      <div className="overflow-x-auto sm:rounded-xl sm:border sm:border-line sm:bg-surface sm:shadow-card">
        <table className="line-table">
          <thead className="bg-surface-3 text-xs tracking-wide text-ink-muted uppercase">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.product')}</th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('transfers.requested')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('transfers.availableAtSource')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('transfers.approved')}
              </th>
            </tr>
          </thead>
          <tbody>
            {detail.lines.map((line) => (
              <tr key={line.productId} className="border-t border-line">
                <td data-label={t('stock.product')} className="px-3 py-1.5">
                  <span className="font-mono text-xs text-ink-muted">
                    {line.productCode}
                  </span>{' '}
                  {line.productName}
                </td>
                <td data-label={t('transfers.requested')} className="cell-numeric">
                  {trim(line.stockQuantity)} {line.stockUnitCode}
                </td>
                <td
                  data-label={t('transfers.availableAtSource')}
                  className="cell-numeric"
                >
                  {trim(line.availableAtSource)} {line.stockUnitCode}
                </td>
                <td data-label={t('transfers.approved')} className="px-3 py-1.5 text-end">
                  <input
                    type="number"
                    step="any"
                    min="0"
                    max={line.stockQuantity}
                    value={agreed[line.productId] ?? ''}
                    onChange={(event) =>
                      setAgreed((current) => ({
                        ...current,
                        [line.productId]: event.target.value,
                      }))
                    }
                    aria-label={t('transfers.approved')}
                    className="field-input-sm w-28 text-end font-mono tabular-nums"
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="flex justify-end gap-2">
        <button type="button" onClick={onCancel} className="btn-secondary">
          {t('products.cancel')}
        </button>
        <button type="submit" disabled={busy} className="btn-primary">
          {t('transfers.approve')}
        </button>
      </div>
    </form>
  );
}

function ReasonForm({
  busy,
  onCancel,
  onSubmit,
}: {
  readonly busy: boolean;
  readonly onCancel: () => void;
  readonly onSubmit: (reason: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [reason, setReason] = useState('');

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (reason.trim()) {
          onSubmit(reason.trim());
        }
      }}
    >
      <label className="block">
        <span className="field-label">{t('transfers.reason')}</span>
        <textarea
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          className="field-input min-h-24"
          required
        />
      </label>
      <div className="flex justify-end gap-2">
        <button type="button" onClick={onCancel} className="btn-secondary">
          {t('products.cancel')}
        </button>
        <button type="submit" disabled={busy || !reason.trim()} className="btn-primary">
          {t('transfers.confirm')}
        </button>
      </div>
    </form>
  );
}

function ReceiveForm({
  defaultDate,
  busy,
  onCancel,
  onSubmit,
}: {
  readonly defaultDate: string;
  readonly busy: boolean;
  readonly onCancel: () => void;
  readonly onSubmit: (date: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [date, setDate] = useState(defaultDate);

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit(date);
      }}
    >
      <label className="block">
        <span className="field-label">{t('transfers.receivedOn')}</span>
        <input
          type="date"
          value={date}
          onChange={(event) => setDate(event.target.value)}
          className="field-input"
          required
        />
      </label>
      <div className="flex justify-end gap-2">
        <button type="button" onClick={onCancel} className="btn-secondary">
          {t('products.cancel')}
        </button>
        <button type="submit" disabled={busy} className="btn-primary">
          {t('transfers.receive')}
        </button>
      </div>
    </form>
  );
}

function StatusBadge({ status }: { readonly status: number }): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <span
      className={clsx(
        'rounded px-2 py-0.5 text-xs font-medium',
        status === StockTransferStatus.requested &&
          'bg-sky-50 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300',
        status === StockTransferStatus.approved &&
          'bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
        status === StockTransferStatus.dispatched &&
          'bg-violet-50 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300',
        status === StockTransferStatus.received &&
          'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
        (status === StockTransferStatus.rejected ||
          status === StockTransferStatus.cancelled) &&
          'bg-surface-3 text-ink-subtle',
      )}
    >
      {t(transferStatusKey(status))}
    </span>
  );
}

/** The translation key for a request's state. */
function transferStatusKey(status: number): string {
  switch (status) {
    case StockTransferStatus.requested:
      return 'transfers.statusRequested';
    case StockTransferStatus.approved:
      return 'transfers.statusApproved';
    case StockTransferStatus.dispatched:
      return 'transfers.statusDispatched';
    case StockTransferStatus.received:
      return 'transfers.statusReceived';
    case StockTransferStatus.rejected:
      return 'transfers.statusRejected';
    default:
      return 'transfers.statusCancelled';
  }
}
