import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { Modal } from '@/components/Modal';
import { HeadingAction } from '@/components/PageHeading';
import { ReportFrame, ReportSkeleton } from '@/components/ReportFrame';
import type { ApiError } from '@/lib/api';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import { listProducts, type ProductSummary } from '@/lib/products';
import {
  allowsNegative,
  cancelStockDocument,
  carriesRate,
  createStockDocument,
  fetchProductBatches,
  fetchProductSerials,
  getStockDocument,
  isCount,
  isTransfer,
  listStockDocuments,
  opensBatches,
  postStockDocument,
  STOCK_TYPES,
  StockDocumentStatus,
  StockDocumentType,
  type BatchStockRow,
  type SerialNumberView,
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
    {
      key: 'lines',
      header: t('stock.lines'),
      value: (row) => row.lineCount,
      numeric: true,
    },
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
              'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
            row.status === StockDocumentStatus.draft &&
              'bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
            row.status === StockDocumentStatus.cancelled &&
              'bg-surface-3 text-ink-subtle line-through',
          )}
        >
          {t(statusKey(row.status))}
        </span>
      ),
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

      <Labelled label={t('stock.type')}>
        <select
          value={typeFilter}
          onChange={(event) =>
            setTypeFilter(event.target.value === '' ? '' : Number(event.target.value))
          }
          className="field-input-sm"
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
          className="field-input-sm"
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
    <>
      <ReportFrame
        title={t('nav.stockOperations')}
        controls={controls}
        actions={
          <HeadingAction label={t('stock.new')} onClick={() => setEntering(true)} />
        }
        query={query}
      >
        {(rows) => (
          <div className="space-y-3">
            {error && !entering && <Alert tone="error">{error}</Alert>}
            {notice && <Alert tone="ok">{notice}</Alert>}

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

      {/*
        A receipt with a dozen lines never belonged above the register it is entered
        into: it pushed the list off the screen while it was open, and the list is
        what the screen is for. The same dialog the sales and purchase screens use,
        and outside the frame for the same reason — a register that failed to load
        must still let somebody enter a document.
      */}
      {entering && (
        <Modal title={t('stock.new')} onClose={() => setEntering(false)}>
          {error && <Alert tone="error">{error}</Alert>}

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
        </Modal>
      )}
    </>
  );
}

/** A draft line being typed. */
interface DraftLine {
  readonly key: string;
  productId: string;
  quantity: string;
  rate: string;
  remarks: string;
  batchId: string;
  batchNumber: string;
  expiresOn: string;
  serialNumbers: string;
  warrantyUntil: string;
}

function emptyLine(): DraftLine {
  return {
    key: crypto.randomUUID(),
    productId: '',
    quantity: '',
    rate: '',
    remarks: '',
    batchId: '',
    batchNumber: '',
    expiresOn: '',
    serialNumbers: '',
    warrantyUntil: '',
  };
}

/**
 * The unit numbers on a line, however they were typed.
 *
 * Split on anything that is not part of a number, so a storekeeper can paste a column
 * out of a spreadsheet, type them separated by commas, or scan them one after another —
 * and the blanks that leaves between them are dropped rather than sent as empty units.
 */
function parseSerials(entered: string): readonly string[] {
  return entered
    .split(/[\s,;]+/)
    .map((number) => number.trim())
    .filter((number) => number.length > 0);
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

  const batched = (productId: string): boolean =>
    (products.data ?? []).some(
      (product) => product.id === productId && product.tracksBatches,
    );

  const serialised = (productId: string): boolean =>
    (products.data ?? []).some(
      (product) => product.id === productId && product.tracksSerialNumbers,
    );

  // The columns appear once a product that needs them is on the grid rather than
  // always. Most firms track batches and serials on a handful of products, and a
  // permanently empty column on every other document would be in the way of the ones
  // they do enter.
  const anyBatched = lines.some((line) => batched(line.productId));
  const anySerialised = lines.some((line) => serialised(line.productId));

  const update = (key: string, patch: Partial<DraftLine>): void =>
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        const entered: StockLineInput[] = lines
          .filter((line) => line.productId && line.quantity.trim())
          .map((line) => ({
            productId: line.productId,
            quantity: Number(line.quantity),
            rate: showRate && line.rate.trim() ? Number(line.rate) : 0,
            remarks: line.remarks.trim() || null,

            // One or the other, never both: a batch chosen from stock is identified,
            // and a batch typed onto a receipt is named. Sending a blank number where
            // the server expects to generate one would be naming it "".
            batchId: line.batchId || null,
            batchNumber: line.batchId ? null : line.batchNumber.trim() || null,
            expiresOn: line.batchId ? null : line.expiresOn || null,

            // Sent only where there are any. An empty array on an unserialised line
            // would be a claim that the line moves no units, which is different from
            // a line whose units are not tracked.
            serialNumbers: parseSerials(line.serialNumbers).length
              ? parseSerials(line.serialNumbers)
              : null,
            warrantyUntil: line.warrantyUntil || null,
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

      {counting && <p className="text-xs text-ink-muted">{t('stock.countHint')}</p>}

      {allowsNegative(type) && (
        <p className="text-xs text-ink-muted">{t('stock.adjustmentHint')}</p>
      )}

      {anyBatched && <p className="text-xs text-ink-muted">{t('stock.batchHint')}</p>}

      {anySerialised && <p className="text-xs text-ink-muted">{t('stock.serialHint')}</p>}

      <div className="table-wrap max-h-[70vh] overflow-y-auto">
        <table className="table">
          <thead className="bg-surface-3">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.product')}</th>
              <th className="px-3 py-2 text-end font-semibold">
                {counting ? t('stock.counted') : t('stock.quantity')}
              </th>
              {showRate && (
                <th className="px-3 py-2 text-end font-semibold">{t('stock.rate')}</th>
              )}
              {anyBatched && (
                <th className="px-3 py-2 text-start font-semibold">{t('stock.batch')}</th>
              )}
              {anySerialised && (
                <th className="px-3 py-2 text-start font-semibold">{t('stock.units')}</th>
              )}
              <th className="px-3 py-2 text-start font-semibold">{t('stock.remarks')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.key} className="border-t border-line">
                <td className="px-2 py-1">
                  <select
                    value={line.productId}
                    onChange={(event) =>
                      // The batch goes with the product it belonged to. Keeping it
                      // would offer a lot of the old product against the new one.
                      update(line.key, {
                        productId: event.target.value,
                        batchId: '',
                        batchNumber: '',
                        expiresOn: '',
                        serialNumbers: '',
                        warrantyUntil: '',
                      })
                    }
                    className="field-input-sm"
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
                    onChange={(event) =>
                      update(line.key, { quantity: event.target.value })
                    }
                    className="field-input-sm w-28 text-end font-mono tabular-nums"
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
                      className="field-input-sm w-28 text-end font-mono tabular-nums"
                    />
                  </td>
                )}
                {anyBatched && (
                  <td className="px-2 py-1">
                    {batched(line.productId) ? (
                      <BatchCell
                        line={line}
                        type={type}
                        warehouseId={warehouseId}
                        onChange={(patch) => update(line.key, patch)}
                      />
                    ) : (
                      <span className="text-xs text-ink-subtle">
                        {t('stock.batchNone')}
                      </span>
                    )}
                  </td>
                )}
                {anySerialised && (
                  <td className="px-2 py-1">
                    {serialised(line.productId) ? (
                      <SerialCell
                        line={line}
                        type={type}
                        warehouseId={warehouseId}
                        onChange={(patch) => update(line.key, patch)}
                      />
                    ) : (
                      <span className="text-xs text-ink-subtle">
                        {t('stock.batchNone')}
                      </span>
                    )}
                  </td>
                )}
                <td className="px-2 py-1">
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
                    className="rounded-md border border-line px-2 py-0.5 text-xs font-medium text-ink-muted transition hover:border-line-strong hover:bg-surface-3 hover:text-ink disabled:pointer-events-none disabled:opacity-40"
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
 * The batch a line moves: chosen from stock, or named on the way in.
 *
 * Two different questions, so two different controls. Taking goods out means picking
 * one of the lots that are actually there, with what is left of each and what it cost
 * shown against it — the choice section 10 asks for, made for the user when only one
 * lot comes back. Bringing goods in means writing down the number on the carton, or
 * leaving it blank for the server to generate the next one.
 */
function BatchCell({
  line,
  type,
  warehouseId,
  onChange,
}: {
  readonly line: DraftLine;
  readonly type: number;
  readonly warehouseId: string;
  readonly onChange: (patch: Partial<DraftLine>) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const naming = opensBatches(type);

  // Only where a choice has to be made. A receipt names its own lot, so asking the
  // server what is in stock would be a round trip nobody reads.
  const batches = useQuery<readonly BatchStockRow[], ApiError>({
    queryKey: ['product-batches', line.productId, warehouseId],
    queryFn: () => fetchProductBatches(line.productId, warehouseId),
    enabled: !naming && Boolean(line.productId && warehouseId),
  });

  const available = batches.data ?? [];

  // Section 10: one batch in stock is chosen for the user, several are offered. A
  // picker with one lot on the shelf should not have to tell the system which lot.
  // Guarded rather than given a dependency list, because the guard is the same
  // condition either way and a list would have to name a closure that changes on
  // every render.
  useEffect(() => {
    const only = available.length === 1 ? available[0] : undefined;

    if (!naming && !line.batchId && only) {
      onChange({ batchId: only.batchId });
    }
  });

  if (naming) {
    return (
      <div className="flex gap-1">
        <input
          value={line.batchNumber}
          onChange={(event) => onChange({ batchNumber: event.target.value })}
          placeholder={t('stock.batchAuto')}
          className="field-input-sm w-28"
        />
        <input
          type="date"
          value={line.expiresOn}
          onChange={(event) => onChange({ expiresOn: event.target.value })}
          title={t('stock.expiresOn')}
          className="field-input-sm w-36"
        />
      </div>
    );
  }

  if (batches.isSuccess && available.length === 0) {
    return <span className="text-xs text-amber-600">{t('stock.noBatchesInStock')}</span>;
  }

  return (
    <select
      value={line.batchId}
      onChange={(event) => onChange({ batchId: event.target.value })}
      className="field-input-sm w-full sm:w-56"
    >
      <option value="">{t('stock.chooseBatch')}</option>
      {available.map((batch) => (
        <option key={batch.batchId} value={batch.batchId}>
          {batch.batchNumber} —{' '}
          {t('stock.batchAvailable', {
            quantity: trim(batch.quantity),
            rate: trim(batch.unitCost),
          })}
          {batch.expiresOn ? ` — ${batch.expiresOn}` : ''}
        </option>
      ))}
    </select>
  );
}

/**
 * The units a line moves: written down on the way in, picked on the way out.
 *
 * Both controls are lists rather than one box per unit, because the quantity is not
 * known when the row is drawn and a storekeeper receiving forty handsets is pasting a
 * column out of a spreadsheet or scanning them one after another. The count is shown
 * against what the line needs, so a short list is visible before the server refuses it.
 */
function SerialCell({
  line,
  type,
  warehouseId,
  onChange,
}: {
  readonly line: DraftLine;
  readonly type: number;
  readonly warehouseId: string;
  readonly onChange: (patch: Partial<DraftLine>) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const naming = opensBatches(type);

  // Only where a choice has to be made. A receipt names the numbers on the cases in
  // front of it, so asking the server what is in stock would be a round trip nobody
  // reads.
  const units = useQuery<readonly SerialNumberView[], ApiError>({
    queryKey: ['product-serials', line.productId, warehouseId],
    queryFn: () => fetchProductSerials(line.productId, warehouseId),
    enabled: !naming && Boolean(line.productId && warehouseId),
  });

  const entered = parseSerials(line.serialNumbers).length;
  const needed = Math.abs(Number(line.quantity) || 0);

  const counter = (
    <span
      className={clsx(
        'text-xs',
        entered === needed && needed > 0
          ? 'text-ink-muted'
          : 'text-amber-700 dark:text-amber-300',
      )}
    >
      {t('stock.unitsCounted', { count: entered, needed: trim(needed) })}
    </span>
  );

  if (naming) {
    return (
      <div className="flex flex-col gap-1">
        <textarea
          value={line.serialNumbers}
          onChange={(event) => onChange({ serialNumbers: event.target.value })}
          placeholder={t('stock.unitsPlaceholder')}
          rows={2}
          className="field-input-sm w-full font-mono text-xs sm:w-56"
        />
        <div className="flex items-center gap-2">
          <input
            type="date"
            value={line.warrantyUntil}
            onChange={(event) => onChange({ warrantyUntil: event.target.value })}
            title={t('stock.warrantyUntil')}
            className="field-input-sm w-full text-xs sm:w-36"
          />
          {counter}
        </div>
      </div>
    );
  }

  const available = units.data ?? [];

  if (units.isSuccess && available.length === 0) {
    return <span className="text-xs text-amber-600">{t('stock.unitsNone')}</span>;
  }

  return (
    <div className="flex flex-col gap-1">
      <select
        multiple
        size={Math.min(4, Math.max(2, available.length))}
        value={parseSerials(line.serialNumbers)}
        onChange={(event) =>
          onChange({
            serialNumbers: [...event.target.selectedOptions]
              .map((option) => option.value)
              .join('\n'),
          })
        }
        aria-label={t('stock.unitsChoose')}
        className="field-input-sm w-full font-mono text-xs sm:w-56"
      >
        {available.map((unit) => (
          <option key={unit.serialNumberId} value={unit.number}>
            {unit.number}
            {unit.isUnderWarranty ? ' ✓' : ''}
          </option>
        ))}
      </select>
      {counter}
    </div>
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
    return (
      <section className="page" aria-busy="true">
        <div className="skeleton h-7 w-64 rounded" />
        <ReportSkeleton rows={4} />
      </section>
    );
  }

  if (query.isError || !query.data) {
    return <Alert tone="error">{query.error?.detail ?? t('stock.notFound')}</Alert>;
  }

  const document = query.data;

  // Shown only where the document actually moved a batch. A column of empty cells on
  // every unbatched receipt would say nothing, on every document, forever.
  const batchedDocument = document.lines.some((line) => line.batchNumber);

  return (
    <section className="page">
      <header className="page-header sm:items-center">
        <div className="min-w-0">
          <h2 className="page-title">
            {document.number} — {t(typeKey(document.type))}
          </h2>
          <p className="text-xs text-ink-muted">
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
          className="panel toolbar"
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
        <table className="table">
          <thead className="bg-surface-3">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">#</th>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.product')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('stock.quantity')}</th>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.unit')}</th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('stock.inStockUnits')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">{t('stock.rate')}</th>
              {batchedDocument && (
                <th className="px-3 py-2 text-start font-semibold">{t('stock.batch')}</th>
              )}
              <th className="px-3 py-2 text-start font-semibold">{t('stock.remarks')}</th>
            </tr>
          </thead>
          <tbody>
            {document.lines.map((line) => (
              <tr key={line.id} className="border-t border-line">
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
                {batchedDocument && (
                  <td className="px-3 py-1.5">
                    {line.batchNumber ?? ''}
                    {line.expiresOn && (
                      <span className="ms-2 text-xs text-ink-muted">
                        {t('stock.expiresOn')} {line.expiresOn}
                      </span>
                    )}
                  </td>
                )}
                <td className="px-3 py-1.5">{line.remarks ?? ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>

      <Panel title={t('stock.movements')}>
        {document.movements.length === 0 ? (
          <p className="px-3 py-4 text-sm text-ink-muted">{t('stock.noMovements')}</p>
        ) : (
          <table className="table">
            <thead className="bg-surface-3">
              <tr>
                <th className="px-3 py-2 text-start font-semibold">
                  {t('stock.product')}
                </th>
                <th className="px-3 py-2 text-start font-semibold">
                  {t('stock.warehouse')}
                </th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('stock.quantity')}
                </th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('stock.unitCost')}
                </th>
                <th className="px-3 py-2 text-end font-semibold">{t('stock.value')}</th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('stock.balanceAfter')}
                </th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('stock.averageAfter')}
                </th>
                {batchedDocument && (
                  <th className="px-3 py-2 text-start font-semibold">
                    {t('stock.batch')}
                  </th>
                )}
              </tr>
            </thead>
            <tbody>
              {document.movements.map((movement, index) => (
                <tr
                  key={`${movement.productCode}-${movement.warehouseName}-${index}`}
                  className="border-t border-line"
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
                  {batchedDocument && (
                    <td className="px-3 py-1.5">{movement.batchNumber ?? ''}</td>
                  )}
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
    <label className="field">
      <span className="field-label">{label}</span>
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
      <h3 className="text-sm font-semibold text-ink-muted uppercase">{title}</h3>
      <div className="table-wrap max-h-[70vh] overflow-y-auto">{children}</div>
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
        'rounded-xl border px-4 py-3 text-sm',
        tone === 'error' &&
          'border-red-200 bg-red-50 text-red-800 dark:border-red-500/30 dark:bg-red-500/10 dark:text-red-200',
        tone === 'ok' &&
          'border-emerald-200 bg-emerald-50 text-emerald-800 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-200',
        tone === 'muted' && 'border-line bg-surface-2 text-ink-muted',
      )}
    >
      {children}
    </div>
  );
}
