import { useEffect, useMemo, useState } from 'react';
import { useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/lib/api';
import { listMaster, type UnitSummary, type WarehouseSummary } from '@/lib/inventory';
import { moneyAlways } from '@/lib/money';
import type { ProductDetail } from '@/lib/products';
import {
  cancelStockDocument,
  createStockDocument,
  fetchStockLedger,
  getStockDocument,
  StockDocumentStatus,
  StockDocumentType,
  type StockDocumentDetail,
  type StockLedgerReport,
  type StockLineInput,
} from '@/lib/stock';
import { useGodownsOn, useSettings } from '@/stores/settings';
import { usePendingSave } from '@/components/product/PendingSaves';
import { useSerialNumbersEnabled } from '@/components/SerialNumbers';

/** A row being typed: one dated quantity, in one warehouse, of one batch. */
interface Row {
  readonly key: string;
  date: string;
  warehouseId: string;
  quantity: string;
  rate: string;
  batchNumber: string;
  expiresOn: string;
  serialNumbers: string;
  remarks: string;
}

/** An opening stock entry already posted for this product. */
interface Entry {
  readonly document: StockDocumentDetail;
  readonly lineId: string;
  readonly quantity: number;
  readonly rate: number;
  readonly batchNumber: string | null;
  readonly expiresOn: string | null;
  readonly remarks: string | null;
}

const today = (): string => new Date().toISOString().slice(0, 10);
const LATEST = '2099-12-31';

const serialsOf = (typed: string): readonly string[] =>
  typed
    .split(/[\s,;]+/)
    .map((serial) => serial.trim())
    .filter(Boolean);

/**
 * A product's opening stock: every entry already made for it, each of which can be
 * corrected, and as many new rows as are needed - each with its own date, warehouse,
 * quantity and, where the product is batched, batch and expiry.
 *
 * A posted document is never edited in place. Correcting an entry cancels its document,
 * which reverses what it moved, and posts it again as corrected - with any other
 * products it carried unchanged - so the stock ledger shows both.
 */
export function OpeningStockTab({
  product,
  units,
}: {
  readonly product: ProductDetail;
  readonly units: readonly UnitSummary[];
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { preferredWarehouseId } = useSettings();
  const godownsOn = useGodownsOn();
  const serialsOn = useSerialNumbersEnabled() && product.tracksSerialNumbers;
  const batched = product.tracksBatches;
  const stockUnit = units.find((unit) => unit.id === product.stockUnitId)?.code ?? '';

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });
  const defaultWarehouse =
    preferredWarehouseId ||
    (warehouses.data?.find((warehouse) => warehouse.isDefault) ?? warehouses.data?.[0])
      ?.id ||
    '';

  const blankRow = (): Row => ({
    key: crypto.randomUUID(),
    date: today(),
    warehouseId: defaultWarehouse,
    quantity: '',
    rate: String(product.cost || product.retailRate || ''),
    batchNumber: '',
    expiresOn: '',
    serialNumbers: '',
    remarks: '',
  });

  const [rows, setRows] = useState<Row[]>(() => [blankRow()]);
  const [editing, setEditing] = useState<{ entry: Entry; row: Row } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // Rows typed before the warehouses arrived get the default once it is known.
  useEffect(() => {
    if (!defaultWarehouse) return;
    setRows((current) =>
      current.map((row) =>
        row.warehouseId ? row : { ...row, warehouseId: defaultWarehouse },
      ),
    );
  }, [defaultWarehouse]);

  // The documents behind the product's opening stock, read from its ledger.
  const ledger = useQuery<StockLedgerReport, ApiError>({
    queryKey: ['stock-ledger', product.id, 'opening'],
    queryFn: () => fetchStockLedger(product.id, '2000-01-01', LATEST, ''),
  });
  const documentIds = useMemo(
    () => [
      ...new Set(
        (ledger.data?.rows ?? [])
          .filter((row) => row.documentType === StockDocumentType.openingStock)
          .map((row) => row.documentId),
      ),
    ],
    [ledger.data],
  );
  const documents = useQueries({
    queries: documentIds.map((id) => ({
      queryKey: ['stock-document', id],
      queryFn: () => getStockDocument(id),
    })),
  });
  const entries: readonly Entry[] = documents
    .flatMap((query) => (query.data ? [query.data] : []))
    .filter((document) => document.status === StockDocumentStatus.posted)
    .flatMap((document) =>
      document.lines
        .filter((line) => line.productId === product.id)
        .map((line) => ({
          document,
          lineId: line.id,
          quantity: line.stockQuantity,
          rate: line.rate,
          batchNumber: line.batchNumber,
          expiresOn: line.expiresOn,
          remarks: line.remarks,
        })),
    )
    .sort((a, b) => a.document.date.localeCompare(b.document.date));

  const refresh = async (): Promise<void> => {
    await queryClient.invalidateQueries({ queryKey: ['stock-ledger', product.id] });
    await queryClient.invalidateQueries({ queryKey: ['stock-document'] });
    await queryClient.invalidateQueries({ queryKey: ['stock-valuation'] });
    await queryClient.invalidateQueries({ queryKey: ['product', product.id] });
  };

  const run = async (work: () => Promise<string>): Promise<void> => {
    setBusy(true);
    setError(null);
    try {
      setNotice(await work());
      await refresh();
    } catch (failure) {
      setError(
        failure instanceof ApiError
          ? failure.detail || failure.code
          : failure instanceof Error
            ? failure.message
            : String(failure),
      );
    } finally {
      setBusy(false);
    }
  };

  const lineOf = (row: Row): StockLineInput => ({
    productId: product.id,
    quantity: Number(row.quantity),
    rate: Number(row.rate) || 0,
    remarks: row.remarks.trim() || null,
    batchId: null,
    batchNumber: batched ? row.batchNumber.trim() || null : null,
    expiresOn: batched ? row.expiresOn || null : null,
    serialNumbers:
      serialsOn && serialsOf(row.serialNumbers).length
        ? serialsOf(row.serialNumbers)
        : null,
    warrantyUntil: null,
  });

  const problemIn = (row: Row): string | null => {
    const quantity = Number(row.quantity);
    if (!Number.isFinite(quantity) || quantity <= 0)
      return t('openingStock.quantityPositive');
    if (Number(row.rate) < 0) return t('openingStock.rateNotNegative');
    if (!row.warehouseId) return t('openingStock.warehouseRequired');
    if (!row.date) return t('openingStock.dateRequired');
    return null;
  };

  // New rows: one document per date and warehouse, since a document has one of each.
  const writeRows = async (filled: readonly Row[]): Promise<void> => {
    const groups = new Map<string, Row[]>();
    for (const row of filled) {
      const key = `${row.date}|${row.warehouseId}`;
      groups.set(key, [...(groups.get(key) ?? []), row]);
    }
    for (const group of groups.values()) {
      const first = group[0];
      if (!first) continue;
      await createStockDocument({
        type: StockDocumentType.openingStock,
        date: first.date,
        warehouseId: first.warehouseId,
        postImmediately: true,
        lines: group.map(lineOf),
      });
    }
    setRows([blankRow()]);
  };

  const saveRows = (): void => {
    const filled = rows.filter((row) => row.quantity.trim() !== '');
    const problem = filled.map(problemIn).find((found) => found !== null);
    if (filled.length === 0 || problem) {
      setError(problem ?? t('openingStock.nothingToSave'));
      return;
    }

    void run(async () => {
      await writeRows(filled);
      return t('openingStock.saved', { count: filled.length });
    });
  };

  // The product's one Save also posts any rows typed here and not yet saved.
  usePendingSave('openingStock', async () => {
    const filled = rows.filter((row) => row.quantity.trim() !== '');
    if (filled.length === 0) return;
    const problem = filled.map(problemIn).find((found) => found !== null);
    if (problem) throw new Error(`${t('products.tabOpeningStock')}: ${problem}`);
    await writeRows(filled);
    await refresh();
  });

  // A correction: cancel the document and post it again, this line as now typed.
  const saveEdit = (): void => {
    if (!editing) return;
    const problem = problemIn(editing.row);
    if (problem) {
      setError(problem);
      return;
    }
    const { entry, row } = editing;
    const document = entry.document;

    void run(async () => {
      const others: StockLineInput[] = document.lines
        .filter((line) => line.id !== entry.lineId)
        .map((line) => ({
          productId: line.productId,
          quantity: line.quantity,
          rate: line.rate,
          remarks: line.remarks,
          batchId: null,
          batchNumber: line.batchNumber,
          expiresOn: line.expiresOn,
          serialNumbers: null,
          warrantyUntil: null,
        }));
      const sameHeader =
        row.date === document.date && row.warehouseId === document.warehouseId;

      await cancelStockDocument(document.id, t('openingStock.correctedReason'));

      if (sameHeader) {
        await createStockDocument({
          type: StockDocumentType.openingStock,
          date: document.date,
          warehouseId: document.warehouseId,
          referenceNumber: document.referenceNumber,
          narration: document.narration,
          postImmediately: true,
          lines: [...others, lineOf(row)],
        });
      } else {
        if (others.length > 0) {
          await createStockDocument({
            type: StockDocumentType.openingStock,
            date: document.date,
            warehouseId: document.warehouseId,
            referenceNumber: document.referenceNumber,
            narration: document.narration,
            postImmediately: true,
            lines: others,
          });
        }
        await createStockDocument({
          type: StockDocumentType.openingStock,
          date: row.date,
          warehouseId: row.warehouseId,
          postImmediately: true,
          lines: [lineOf(row)],
        });
      }
      setEditing(null);
      return t('openingStock.updated');
    });
  };

  const removeEntry = (entry: Entry): void => {
    if (!window.confirm(t('openingStock.confirmRemove'))) return;
    const document = entry.document;
    void run(async () => {
      const others: StockLineInput[] = document.lines
        .filter((line) => line.id !== entry.lineId)
        .map((line) => ({
          productId: line.productId,
          quantity: line.quantity,
          rate: line.rate,
          remarks: line.remarks,
          batchId: null,
          batchNumber: line.batchNumber,
          expiresOn: line.expiresOn,
          serialNumbers: null,
          warrantyUntil: null,
        }));
      await cancelStockDocument(document.id, t('openingStock.correctedReason'));
      if (others.length > 0) {
        await createStockDocument({
          type: StockDocumentType.openingStock,
          date: document.date,
          warehouseId: document.warehouseId,
          referenceNumber: document.referenceNumber,
          narration: document.narration,
          postImmediately: true,
          lines: others,
        });
      }
      return t('openingStock.removed');
    });
  };

  const warehouseOptions = (warehouses.data ?? []).map((warehouse) => (
    <option key={warehouse.id} value={warehouse.id}>
      {warehouse.name}
    </option>
  ));

  /** The boxes of one row, for a new entry or a correction. */
  const rowFields = (
    row: Row,
    change: (patch: Partial<Row>) => void,
  ): React.JSX.Element => (
    <>
      <td className="px-2 py-1.5">
        <input
          type="date"
          aria-label={t('stock.date')}
          value={row.date}
          onChange={(event) => change({ date: event.target.value })}
          className="field-input-sm w-36"
        />
      </td>
      {godownsOn && (
        <td className="px-2 py-1.5">
          <select
            aria-label={t('stock.warehouse')}
            value={row.warehouseId}
            onChange={(event) => change({ warehouseId: event.target.value })}
            className="field-input-sm w-36"
          >
            {warehouseOptions}
          </select>
        </td>
      )}
      <td className="px-2 py-1.5 text-end">
        <input
          type="number"
          inputMode="decimal"
          step="any"
          min="0"
          aria-label={t('stock.quantity')}
          value={row.quantity}
          onChange={(event) => change({ quantity: event.target.value })}
          className="field-input-sm ms-auto w-24 text-end tabular-nums"
        />
      </td>
      <td className="px-2 py-1.5 text-end">
        <input
          type="number"
          inputMode="decimal"
          step="any"
          min="0"
          aria-label={t('stock.rate')}
          value={row.rate}
          onChange={(event) => change({ rate: event.target.value })}
          className="field-input-sm ms-auto w-24 text-end tabular-nums"
        />
      </td>
      {batched && (
        <>
          <td className="px-2 py-1.5">
            <input
              aria-label={t('stock.batch')}
              value={row.batchNumber}
              onChange={(event) => change({ batchNumber: event.target.value })}
              className="field-input-sm w-28"
            />
          </td>
          <td className="px-2 py-1.5">
            <input
              type="date"
              aria-label={t('stock.expiresOn')}
              value={row.expiresOn}
              onChange={(event) => change({ expiresOn: event.target.value })}
              className="field-input-sm w-36"
            />
          </td>
        </>
      )}
      {serialsOn && (
        <td className="px-2 py-1.5">
          <input
            aria-label={t('stock.units')}
            value={row.serialNumbers}
            onChange={(event) => change({ serialNumbers: event.target.value })}
            className="field-input-sm w-40"
          />
        </td>
      )}
      <td className="px-2 py-1.5">
        <input
          aria-label={t('stock.narration')}
          value={row.remarks}
          onChange={(event) => change({ remarks: event.target.value })}
          className="field-input-sm w-40"
        />
      </td>
    </>
  );

  const headings = (
    <>
      <th className="px-2 py-2 text-start">{t('stock.date')}</th>
      {godownsOn && <th className="px-2 py-2 text-start">{t('stock.warehouse')}</th>}
      <th className="px-2 py-2 text-end">
        {t('stock.quantity')}
        {stockUnit ? ` (${stockUnit})` : ''}
      </th>
      <th className="px-2 py-2 text-end">{t('stock.rate')}</th>
      {batched && (
        <>
          <th className="px-2 py-2 text-start">{t('stock.batch')}</th>
          <th className="px-2 py-2 text-start">{t('stock.expiresOn')}</th>
        </>
      )}
      {serialsOn && <th className="px-2 py-2 text-start">{t('stock.units')}</th>}
      <th className="px-2 py-2 text-start">{t('stock.narration')}</th>
    </>
  );

  const loading = ledger.isPending || documents.some((query) => query.isPending);
  const totalQuantity = entries.reduce((sum, entry) => sum + entry.quantity, 0);
  const totalValue = entries.reduce((sum, entry) => sum + entry.quantity * entry.rate, 0);

  return (
    <div className="space-y-5">
      {notice && (
        <p role="status" className="alert-success">
          {notice}
        </p>
      )}
      {error && <p className="alert-error">{error}</p>}

      <section className="card space-y-3 p-4">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h3 className="text-sm font-semibold text-ink">{t('openingStock.entered')}</h3>
          <span className="text-xs text-ink-muted tabular-nums">
            {totalQuantity} {stockUnit} · {moneyAlways(totalValue)} {product.currency}
          </span>
        </div>

        {loading ? (
          <p className="text-xs text-ink-muted">{t('common.loading')}</p>
        ) : entries.length === 0 ? (
          <p className="rounded-lg border border-dashed border-line p-4 text-center text-xs text-ink-muted">
            {t('openingStock.none')}
          </p>
        ) : (
          <div className="table-wrap">
            <table className="table text-sm">
              <thead>
                <tr>
                  {headings}
                  <th className="px-2 py-2 text-end">{t('openingStock.value')}</th>
                  <th className="px-2 py-2 text-start">{t('openingStock.document')}</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {entries.map((entry) =>
                  editing?.entry.lineId === entry.lineId ? (
                    <tr key={entry.lineId} className="bg-brand-500/5">
                      {rowFields(editing.row, (patch) =>
                        setEditing((current) =>
                          current
                            ? { ...current, row: { ...current.row, ...patch } }
                            : current,
                        ),
                      )}
                      <td className="px-2 py-1.5 text-end tabular-nums">
                        {moneyAlways(
                          (Number(editing.row.quantity) || 0) *
                            (Number(editing.row.rate) || 0),
                        )}
                      </td>
                      <td className="px-2 py-1.5 font-mono text-xs">
                        {entry.document.number}
                      </td>
                      <td className="px-2 py-1.5 text-end whitespace-nowrap">
                        <button
                          type="button"
                          disabled={busy}
                          onClick={saveEdit}
                          className="btn-primary btn-xs"
                        >
                          {t('openingStock.update')}
                        </button>{' '}
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => setEditing(null)}
                          className="btn-ghost btn-xs"
                        >
                          {t('common.cancel')}
                        </button>
                      </td>
                    </tr>
                  ) : (
                    <tr key={entry.lineId}>
                      <td className="px-2 py-1.5 whitespace-nowrap">
                        {entry.document.date}
                      </td>
                      {godownsOn && (
                        <td className="px-2 py-1.5">{entry.document.warehouseName}</td>
                      )}
                      <td className="px-2 py-1.5 text-end tabular-nums">
                        {entry.quantity}
                      </td>
                      <td className="px-2 py-1.5 text-end tabular-nums">
                        {moneyAlways(entry.rate)}
                      </td>
                      {batched && (
                        <>
                          <td className="px-2 py-1.5">{entry.batchNumber ?? '—'}</td>
                          <td className="px-2 py-1.5">{entry.expiresOn ?? '—'}</td>
                        </>
                      )}
                      {serialsOn && <td className="px-2 py-1.5 text-ink-muted">—</td>}
                      <td className="px-2 py-1.5 text-ink-muted">
                        {entry.remarks ?? ''}
                      </td>
                      <td className="px-2 py-1.5 text-end tabular-nums">
                        {moneyAlways(entry.quantity * entry.rate)}
                      </td>
                      <td className="px-2 py-1.5 font-mono text-xs">
                        {entry.document.number}
                      </td>
                      <td className="px-2 py-1.5 text-end whitespace-nowrap">
                        {!product.tracksSerialNumbers && (
                          <button
                            type="button"
                            disabled={busy || editing !== null}
                            onClick={() =>
                              setEditing({
                                entry,
                                row: {
                                  key: entry.lineId,
                                  date: entry.document.date,
                                  warehouseId: entry.document.warehouseId,
                                  quantity: String(entry.quantity),
                                  rate: String(entry.rate),
                                  batchNumber: entry.batchNumber ?? '',
                                  expiresOn: entry.expiresOn ?? '',
                                  serialNumbers: '',
                                  remarks: entry.remarks ?? '',
                                },
                              })
                            }
                            className="row-action row-action-neutral"
                          >
                            {t('common.edit')}
                          </button>
                        )}{' '}
                        <button
                          type="button"
                          disabled={busy || editing !== null}
                          onClick={() => removeEntry(entry)}
                          className="row-action row-action-neutral"
                        >
                          {t('common.remove')}
                        </button>
                      </td>
                    </tr>
                  ),
                )}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card space-y-3 p-4">
        <h3 className="text-sm font-semibold text-ink">{t('openingStock.add')}</h3>
        <div className="table-wrap">
          <table className="table text-sm">
            <thead>
              <tr>
                {headings}
                <th />
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.key}>
                  {rowFields(row, (patch) =>
                    setRows((current) =>
                      current.map((other) =>
                        other.key === row.key ? { ...other, ...patch } : other,
                      ),
                    ),
                  )}
                  <td className="px-2 py-1.5 text-end">
                    {rows.length > 1 && (
                      <button
                        type="button"
                        onClick={() =>
                          setRows((current) =>
                            current.filter((other) => other.key !== row.key),
                          )
                        }
                        aria-label={t('common.remove')}
                        className="row-action row-action-neutral"
                      >
                        ×
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={() =>
              setRows((current) => {
                const last = current[current.length - 1];
                // A new row starts like the last one, so a run of dated batches is quick.
                return [
                  ...current,
                  last
                    ? {
                        ...blankRow(),
                        date: last.date,
                        warehouseId: last.warehouseId,
                        rate: last.rate,
                      }
                    : blankRow(),
                ];
              })
            }
            className="btn-secondary btn-sm"
          >
            {t('openingStock.addRow')}
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={saveRows}
            className="btn-primary btn-sm ms-auto"
          >
            {busy ? t('common.saving') : t('openingStock.save')}
          </button>
        </div>
      </section>
    </div>
  );
}
