import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { ReportFrame } from '@/components/ReportFrame';
import type { ApiError } from '@/lib/api';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import { listProducts, type ProductSummary } from '@/lib/products';
import {
  allowsNegative,
  cancelStockDocument,
  carriesRate,
  createStockDocument,
  getStockDocument,
  isCount,
  isTransfer,
  listStockDocuments,
  postStockDocument,
  STOCK_TYPES,
  StockDocumentStatus,
  StockDocumentType,
  type StockDocumentDetail,
  type StockDocumentSummary,
  type StockLineInput,
} from '@/lib/stock';

/**
 * Stock operations.
 *
 * One screen for every kind of document rather than one each, because a receipt, an
 * issue, a transfer and a count differ in what they mean and in which fields apply —
 * which the type decides — and not at all in shape. Seven screens would be seven
 * copies of the same grid, and the seventh would drift.
 *
 * The fields that do not apply are hidden rather than disabled: a rate on an issue is
 * refused by the server, because what an issue costs was decided by the position it
 * comes out of, so offering the box would be offering something that cannot be saved.
 */
export function StockOperationsPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  const today = new Date().toISOString().slice(0, 10);
  const monthStart = `${today.slice(0, 7)}-01`;

  const [from, setFrom] = useState(monthStart);
  const [to, setTo] = useState(today);
  const [typeFilter, setTypeFilter] = useState<number | ''>('');
  const [warehouseFilter, setWarehouseFilter] = useState('');
  const [entering, setEntering] = useState(false);
  const [viewing, setViewing] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const query = useQuery<readonly StockDocumentSummary[], ApiError>({
    queryKey: ['stock-documents', from, to, typeFilter, warehouseFilter],
    queryFn: () => listStockDocuments(from, to, typeFilter, warehouseFilter),
  });

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });

  const mutation = useMutation<string | null, ApiError, () => Promise<string | null>>({
    mutationFn: (action) => action(),
    onSuccess: async (message) => {
      setError(null);
      setNotice(message);
      window.setTimeout(() => setNotice(null), 4000);

      await queryClient.invalidateQueries({ queryKey: ['stock-documents'] });
      await queryClient.invalidateQueries({ queryKey: ['stock-valuation'] });
    },
    // The server owns the rules — not enough stock, a unit that does not convert, a
    // receipt whose goods have gone — and its message names the product. Guessing at
    // one here would lose that.
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const run = (action: () => Promise<string | null>): void => {
    setError(null);
    mutation.mutate(action);
  };

  const columns: readonly GridColumn<StockDocumentSummary>[] = [
    {
      key: 'number',
      header: t('stock.number'),
      value: (row) => row.number,
      render: (row) => (
        <button
          type="button"
          onClick={() => setViewing(row.id)}
          className="font-mono text-brand-700 underline-offset-2 hover:underline dark:text-brand-100"
        >
          {row.number}
        </button>
      ),
    },
    { key: 'date', header: t('stock.date'), value: (row) => row.date },
    {
      key: 'type',
      header: t('stock.type'),
      value: (row) => t(typeKey(row.type)),
    },
    {
      key: 'warehouse',
      header: t('stock.warehouse'),
      value: (row) =>
        row.destinationWarehouseName
          ? `${row.warehouseName} → ${row.destinationWarehouseName}`
          : row.warehouseName,
    },
    {
      key: 'reference',
      header: t('stock.reference'),
      value: (row) => row.referenceNumber ?? '',
      hiddenByDefault: true,
    },
    { key: 'lines', header: t('stock.lines'), value: (row) => row.lineCount, numeric: true },
    {
      key: 'quantity',
      header: t('stock.quantity'),
      value: (row) => row.totalQuantity,
      numeric: true,
      render: (row) => trim(row.totalQuantity),
    },
    {
      key: 'value',
      header: t('stock.value'),
      value: (row) => row.totalValue,
      numeric: true,
      render: (row) => row.totalValue.toFixed(2),
      // What the firm paid for the goods, which is not something everybody who may
      // look at a movement should see.
      requiredPermission: 'inventory:report:view',
    },
    {
      key: 'status',
      header: t('stock.status'),
      value: (row) => t(statusKey(row.status)),
      render: (row) => (
        <span
          className={clsx(
            'rounded px-2 py-0.5 text-xs',
            row.status === StockDocumentStatus.posted &&
              'bg-emerald-50 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200',
            row.status === StockDocumentStatus.draft &&
              'bg-amber-50 text-amber-800 dark:bg-amber-950 dark:text-amber-200',
            row.status === StockDocumentStatus.cancelled &&
              'bg-slate-100 text-slate-600 line-through dark:bg-slate-800 dark:text-slate-300',
          )}
        >
          {t(statusKey(row.status))}
        </span>
      ),
    },
  ];

  const controls = (
    <div className="flex flex-wrap items-end gap-3">
      <Labelled label={t('reports.from')}>
        <input
          type="date"
          value={from}
          onChange={(event) => setFrom(event.target.value)}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-900"
        />
      </Labelled>

      <Labelled label={t('reports.to')}>
        <input
          type="date"
          value={to}
          onChange={(event) => setTo(event.target.value)}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-900"
        />
      </Labelled>

      <Labelled label={t('stock.type')}>
        <select
          value={typeFilter}
          onChange={(event) =>
            setTypeFilter(event.target.value === '' ? '' : Number(event.target.value))
          }
          className="rounded-md border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-900"
        >
          <option value="">{t('stock.allTypes')}</option>
          {STOCK_TYPES.map((type) => (
            <option key={type} value={type}>
              {t(typeKey(type))}
            </option>
          ))}
        </select>
      </Labelled>

      <Labelled label={t('stock.warehouse')}>
        <select
          value={warehouseFilter}
          onChange={(event) => setWarehouseFilter(event.target.value)}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-900"
        >
          <option value="">{t('stock.allWarehouses')}</option>
          {(warehouses.data ?? []).map((warehouse) => (
            <option key={warehouse.id} value={warehouse.id}>
              {warehouse.name}
            </option>
          ))}
        </select>
      </Labelled>
    </div>
  );

  if (viewing) {
    return (
      <StockDocumentView
        documentId={viewing}
        busy={mutation.isPending}
        error={error}
        onClose={() => setViewing(null)}
        onPost={(id) =>
          run(async () => {
            const posted = await postStockDocument(id);
            return t('stock.postedNotice', {
              number: posted.number,
              count: posted.movements,
            });
          })
        }
        onCancel={(id, reason) =>
          run(async () => {
            await cancelStockDocument(id, reason);
            return t('stock.cancelledNotice');
          })
        }
      />
    );
  }

  return (
    <ReportFrame title={t('nav.stockOperations')} controls={controls} query={query}>
      {(rows) => (
        <div className="space-y-4">
          {error && <Alert tone="error">{error}</Alert>}
          {notice && <Alert tone="ok">{notice}</Alert>}

          {entering ? (
            <StockEntry
              warehouses={warehouses.data ?? []}
              busy={mutation.isPending}
              onCancel={() => setEntering(false)}
              onSubmit={(body) =>
                run(async () => {
                  const created = await createStockDocument(body);
                  setEntering(false);

                  return created.status === StockDocumentStatus.draft
                    ? t('stock.draftNotice', { number: created.number })
                    : t('stock.postedNotice', {
                        number: created.number,
                        count: created.movements,
                      });
                })
              }
            />
          ) : (
            <button type="button" onClick={() => setEntering(true)} className="btn-primary">
              {t('stock.new')}
            </button>
          )}

          <DataGrid
            gridKey="stock-documents"
            rows={rows}
            columns={columns}
            rowKey={(row) => row.id}
            emptyMessage={t('stock.noneFound')}
          />
        </div>
      )}
    </ReportFrame>
  );
}

/** A draft line being typed. */
interface DraftLine {
  readonly key: string;
  productId: string;
  quantity: string;
  rate: string;
  remarks: string;
}

function emptyLine(): DraftLine {
  return { key: crypto.randomUUID(), productId: '', quantity: '', rate: '', remarks: '' };
}

/** The entry form: a header, a grid of products, and one Save. */
function StockEntry({
  warehouses,
  busy,
  onCancel,
  onSubmit,
}: {
  readonly warehouses: readonly WarehouseSummary[];
  readonly busy: boolean;
  readonly onCancel: () => void;
  readonly onSubmit: (body: {
    readonly type: number;
    readonly date: string;
    readonly warehouseId: string;
    readonly lines: readonly StockLineInput[];
    readonly destinationWarehouseId?: string | null;
    readonly referenceNumber?: string | null;
    readonly narration?: string | null;
    readonly postImmediately: boolean;
  }) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const [type, setType] = useState<number>(StockDocumentType.materialReceipt);
  const [date, setDate] = useState(new Date().toISOString().slice(0, 10));
  const [warehouseId, setWarehouseId] = useState(
    warehouses.find((warehouse) => warehouse.isDefault)?.id ?? warehouses[0]?.id ?? '',
  );
  const [destinationId, setDestinationId] = useState('');
  const [reference, setReference] = useState('');
  const [narration, setNarration] = useState('');
  const [post, setPost] = useState(true);
  const [lines, setLines] = useState<readonly DraftLine[]>([emptyLine()]);

  // Every stocked product of the firm, fetched once. A transaction screen that
  // searched per keystroke would be a round trip per character on a master this size;
  // the grid's own filtering handles the picking.
  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', '', '', false],
    queryFn: () => listProducts('', '', false),
  });

  const showRate = carriesRate(type);
  const transfer = isTransfer(type);
  const counting = isCount(type);

  const update = (key: string, patch: Partial<DraftLine>): void =>
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );

  return (
    <form
      className="space-y-4 rounded-xl border border-slate-200 p-4 dark:border-slate-800"
      onSubmit={(event) => {
        event.preventDefault();

        const entered: StockLineInput[] = lines
          .filter((line) => line.productId && line.quantity.trim())
          .map((line) => ({
            productId: line.productId,
            quantity: Number(line.quantity),
            rate: showRate && line.rate.trim() ? Number(line.rate) : 0,
            remarks: line.remarks.trim() || null,
          }));

        if (entered.length === 0 || !warehouseId || (transfer && !destinationId)) {
          return;
        }

        onSubmit({
          type,
          date,
          warehouseId,
          destinationWarehouseId: transfer ? destinationId : null,
          lines: entered,
          referenceNumber: reference.trim() || null,
          narration: narration.trim() || null,
          postImmediately: post,
        });
      }}
    >
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <label className="block">
          <span className="field-label">{t('stock.type')}</span>
          <select
            value={type}
            onChange={(event) => {
              const next = Number(event.target.value);
              setType(next);

              // A destination only means something on a transfer, and the server
              // refuses one anywhere else.
              if (!isTransfer(next)) {
                setDestinationId('');
              }
            }}
            className="field-input"
          >
            {STOCK_TYPES.map((candidate) => (
              <option key={candidate} value={candidate}>
                {t(typeKey(candidate))}
              </option>
            ))}
          </select>
        </label>

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
          <span className="field-label">
            {transfer ? t('stock.fromWarehouse') : t('stock.warehouse')}
          </span>
          <select
            value={warehouseId}
            onChange={(event) => setWarehouseId(event.target.value)}
            className="field-input"
            required
          >
            <option value="">{t('stock.choose')}</option>
            {warehouses.map((warehouse) => (
              <option key={warehouse.id} value={warehouse.id}>
                {warehouse.name}
              </option>
            ))}
          </select>
        </label>

        {transfer && (
          <label className="block">
            <span className="field-label">{t('stock.toWarehouse')}</span>
            <select
              value={destinationId}
              onChange={(event) => setDestinationId(event.target.value)}
              className="field-input"
              required
            >
              <option value="">{t('stock.choose')}</option>
              {warehouses
                .filter((warehouse) => warehouse.id !== warehouseId)
                .map((warehouse) => (
                  <option key={warehouse.id} value={warehouse.id}>
                    {warehouse.name}
                  </option>
                ))}
            </select>
          </label>
        )}

        <label className="block">
          <span className="field-label">{t('stock.reference')}</span>
          <input
            value={reference}
            onChange={(event) => setReference(event.target.value)}
            className="field-input"
          />
        </label>

        <label className="block lg:col-span-2">
          <span className="field-label">{t('stock.narration')}</span>
          <input
            value={narration}
            onChange={(event) => setNarration(event.target.value)}
            className="field-input"
          />
        </label>
      </div>

      {counting && (
        <p className="text-xs text-slate-500">{t('stock.countHint')}</p>
      )}

      {allowsNegative(type) && (
        <p className="text-xs text-slate-500">{t('stock.adjustmentHint')}</p>
      )}

      <div className="overflow-auto rounded-lg border border-slate-200 dark:border-slate-800">
        <table className="w-full border-collapse text-sm">
          <thead className="bg-slate-100 dark:bg-slate-800">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.product')}</th>
              <th className="px-3 py-2 text-end font-semibold">
                {counting ? t('stock.counted') : t('stock.quantity')}
              </th>
              {showRate && (
                <th className="px-3 py-2 text-end font-semibold">{t('stock.rate')}</th>
              )}
              <th className="px-3 py-2 text-start font-semibold">{t('stock.remarks')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.key} className="border-t border-slate-100 dark:border-slate-900">
                <td className="px-2 py-1">
                  <select
                    value={line.productId}
                    onChange={(event) => update(line.key, { productId: event.target.value })}
                    className="w-full rounded border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
                  >
                    <option value="">{t('stock.choose')}</option>
                    {(products.data ?? []).map((product) => (
                      <option key={product.id} value={product.id}>
                        {product.code} — {product.description}
                      </option>
                    ))}
                  </select>
                </td>
                <td className="px-2 py-1">
                  <input
                    type="number"
                    step="any"
                    value={line.quantity}
                    onChange={(event) => update(line.key, { quantity: event.target.value })}
                    className="w-28 rounded border border-slate-300 bg-white px-2 py-1 text-end dark:border-slate-700 dark:bg-slate-900"
                  />
                </td>
                {showRate && (
                  <td className="px-2 py-1">
                    <input
                      type="number"
                      step="any"
                      min="0"
                      value={line.rate}
                      onChange={(event) => update(line.key, { rate: event.target.value })}
                      placeholder={t('stock.ratePlaceholder')}
                      className="w-28 rounded border border-slate-300 bg-white px-2 py-1 text-end dark:border-slate-700 dark:bg-slate-900"
                    />
                  </td>
                )}
                <td className="px-2 py-1">
                  <input
                    value={line.remarks}
                    onChange={(event) => update(line.key, { remarks: event.target.value })}
                    className="w-full rounded border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
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
                    className="rounded border border-slate-300 px-2 py-0.5 text-xs text-slate-600 disabled:opacity-40 dark:border-slate-700 dark:text-slate-300"
                  >
                    {t('stock.removeLine')}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          onClick={() => setLines((current) => [...current, emptyLine()])}
          className="btn-secondary"
        >
          {t('stock.addLine')}
        </button>

        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={post}
            onChange={(event) => setPost(event.target.checked)}
          />
          {t('stock.postNow')}
        </label>

        <button type="submit" disabled={busy} className="btn-primary ms-auto">
          {t('stock.save')}
        </button>
        <button type="button" onClick={onCancel} className="btn-secondary">
          {t('products.cancel')}
        </button>
      </div>
    </form>
  );
}

/**
 * One document: what it says, and what it did.
 *
 * The movements are shown beside the lines rather than instead of them, because they
 * are different facts. A line says four cases of something; a movement says
 * ninety-six pieces left the main store at 5.25 each and what remained afterwards.
 * On a cancelled document the reversals sit here too, which is the point of reversing
 * rather than deleting.
 */
function StockDocumentView({
  documentId,
  busy,
  error,
  onClose,
  onPost,
  onCancel,
}: {
  readonly documentId: string;
  readonly busy: boolean;
  readonly error: string | null;
  readonly onClose: () => void;
  readonly onPost: (id: string) => void;
  readonly onCancel: (id: string, reason: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [reason, setReason] = useState('');
  const [cancelling, setCancelling] = useState(false);

  const query = useQuery<StockDocumentDetail, ApiError>({
    queryKey: ['stock-document', documentId],
    queryFn: () => getStockDocument(documentId),
  });

  if (query.isPending) {
    return <p className="text-sm text-slate-500">{t('common.loading')}</p>;
  }

  if (query.isError || !query.data) {
    return <Alert tone="error">{query.error?.detail ?? t('stock.notFound')}</Alert>;
  }

  const document = query.data;

  return (
    <section className="space-y-4">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">
            {document.number} — {t(typeKey(document.type))}
          </h2>
          <p className="text-xs text-slate-500">
            {document.date} · {document.warehouseName}
            {document.destinationWarehouseName
              ? ` → ${document.destinationWarehouseName}`
              : ''}{' '}
            · {t(statusKey(document.status))}
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          {document.status === StockDocumentStatus.draft && (
            <button
              type="button"
              disabled={busy}
              onClick={() => onPost(document.id)}
              className="btn-primary"
            >
              {t('stock.post')}
            </button>
          )}

          {document.status === StockDocumentStatus.posted && (
            <button
              type="button"
              disabled={busy}
              onClick={() => setCancelling((value) => !value)}
              className="btn-secondary"
            >
              {t('stock.cancel')}
            </button>
          )}

          <button type="button" onClick={onClose} className="btn-secondary">
            {t('products.backToList')}
          </button>
        </div>
      </header>

      {error && <Alert tone="error">{error}</Alert>}

      {document.cancellationReason && (
        <Alert tone="muted">
          {t('stock.cancelledFor', { reason: document.cancellationReason })}
        </Alert>
      )}

      {cancelling && (
        <form
          className="flex flex-wrap items-end gap-3 rounded-lg border border-slate-200 p-3 dark:border-slate-800"
          onSubmit={(event) => {
            event.preventDefault();

            if (reason.trim()) {
              onCancel(document.id, reason.trim());
              setCancelling(false);
              setReason('');
            }
          }}
        >
          <label className="flex-1">
            <span className="field-label">{t('stock.cancelReason')}</span>
            <input
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              className="field-input"
              required
            />
          </label>
          <button type="submit" disabled={busy} className="btn-primary">
            {t('stock.confirmCancel')}
          </button>
        </form>
      )}

      <Panel title={t('stock.lines')}>
        <table className="w-full border-collapse text-sm">
          <thead className="bg-slate-100 dark:bg-slate-800">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">#</th>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.product')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('stock.quantity')}</th>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.unit')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('stock.inStockUnits')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('stock.rate')}</th>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.remarks')}</th>
            </tr>
          </thead>
          <tbody>
            {document.lines.map((line) => (
              <tr key={line.id} className="border-t border-slate-100 dark:border-slate-900">
                <td className="px-3 py-1.5">{line.lineNumber}</td>
                <td className="px-3 py-1.5">
                  <span className="font-mono">{line.productCode}</span>{' '}
                  {line.productDescription}
                </td>
                <td className="cell-numeric">{trim(line.quantity)}</td>
                <td className="px-3 py-1.5">{line.unitCode}</td>
                <td className="cell-numeric">
                  {trim(line.stockQuantity)} {line.stockUnitCode}
                </td>
                <td className="cell-numeric">{line.rate === 0 ? '' : trim(line.rate)}</td>
                <td className="px-3 py-1.5">{line.remarks ?? ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>

      <Panel title={t('stock.movements')}>
        {document.movements.length === 0 ? (
          <p className="px-3 py-4 text-sm text-slate-500">{t('stock.noMovements')}</p>
        ) : (
          <table className="w-full border-collapse text-sm">
            <thead className="bg-slate-100 dark:bg-slate-800">
              <tr>
                <th className="px-3 py-2 text-start font-semibold">{t('stock.product')}</th>
                <th className="px-3 py-2 text-start font-semibold">{t('stock.warehouse')}</th>
                <th className="px-3 py-2 text-end font-semibold">{t('stock.quantity')}</th>
                <th className="px-3 py-2 text-end font-semibold">{t('stock.unitCost')}</th>
                <th className="px-3 py-2 text-end font-semibold">{t('stock.value')}</th>
                <th className="px-3 py-2 text-end font-semibold">{t('stock.balanceAfter')}</th>
                <th className="px-3 py-2 text-end font-semibold">{t('stock.averageAfter')}</th>
              </tr>
            </thead>
            <tbody>
              {document.movements.map((movement, index) => (
                <tr
                  key={`${movement.productCode}-${movement.warehouseName}-${index}`}
                  className="border-t border-slate-100 dark:border-slate-900"
                >
                  <td className="px-3 py-1.5 font-mono">{movement.productCode}</td>
                  <td className="px-3 py-1.5">{movement.warehouseName}</td>
                  <td
                    className={clsx(
                      'cell-numeric',
                      movement.quantity < 0 ? 'text-red-700 dark:text-red-300' : '',
                    )}
                  >
                    {trim(movement.quantity)}
                  </td>
                  <td className="cell-numeric">{trim(movement.unitCost)}</td>
                  <td className="cell-numeric">{movement.value.toFixed(2)}</td>
                  <td className="cell-numeric">{trim(movement.balanceQuantity)}</td>
                  <td className="cell-numeric">{trim(movement.balanceAverageCost)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Panel>
    </section>
  );
}

/** The translation key for a document type. */
export function typeKey(type: number): string {
  switch (type) {
    case StockDocumentType.openingStock:
      return 'stock.typeOpening';
    case StockDocumentType.materialReceipt:
      return 'stock.typeReceipt';
    case StockDocumentType.materialIssue:
      return 'stock.typeIssue';
    case StockDocumentType.stockTransfer:
      return 'stock.typeTransfer';
    case StockDocumentType.stockAdjustment:
      return 'stock.typeAdjustment';
    case StockDocumentType.damagedStock:
      return 'stock.typeDamaged';
    default:
      return 'stock.typeVerification';
  }
}

function statusKey(status: number): string {
  switch (status) {
    case StockDocumentStatus.draft:
      return 'stock.statusDraft';
    case StockDocumentStatus.posted:
      return 'stock.statusPosted';
    default:
      return 'stock.statusCancelled';
  }
}

/**
 * Formats a quantity without the trailing zeros a fixed scale would add.
 *
 * Quantities are kept to six places so any unit can express itself, but a shop
 * counting in whole pieces should see 12 rather than 12.000000.
 */
export function trim(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: 6 });
}

function Labelled({
  label,
  children,
}: {
  readonly label: string;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <label className="flex flex-col gap-1 text-sm">
      <span className="text-slate-600 dark:text-slate-400">{label}</span>
      {children}
    </label>
  );
}

function Panel({
  title,
  children,
}: {
  readonly title: string;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <div className="space-y-2">
      <h3 className="text-sm font-semibold text-slate-500 uppercase">{title}</h3>
      <div className="overflow-auto rounded-xl border border-slate-200 dark:border-slate-800">
        {children}
      </div>
    </div>
  );
}

function Alert({
  tone,
  children,
}: {
  readonly tone: 'error' | 'ok' | 'muted';
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <div
      role="alert"
      className={clsx(
        'rounded-lg border px-4 py-3 text-sm',
        tone === 'error' &&
          'border-red-300 bg-red-50 text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200',
        tone === 'ok' &&
          'border-emerald-300 bg-emerald-50 text-emerald-800 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-200',
        tone === 'muted' &&
          'border-slate-300 bg-slate-50 text-slate-700 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-300',
      )}
    >
      {children}
    </div>
  );
}
