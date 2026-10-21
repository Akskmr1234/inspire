import { useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { Modal } from '@/components/Modal';
import { PageHeading } from '@/components/PageHeading';
import { SearchSelect } from '@/components/SearchSelect';
import { PageTabs, StockHistoryTable } from '@/components/StockHistory';
import { productOption } from '@/components/DocumentLines';
import type { ApiError } from '@/lib/api';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import { moneyAlways } from '@/lib/money';
import { listProducts, type ProductSummary } from '@/lib/products';
import { downloadCsv, readSpreadsheet, type SheetRows } from '@/lib/spreadsheet';
import {
  createStockDocument,
  fetchStockValuation,
  StockDocumentType,
  StockHistoryKind,
  type StockValuationReport,
} from '@/lib/stock';
import { useSession } from '@/stores/session';
import { Alert, StockOperationsPage } from '@/pages/StockOperationsPage';

/** How many rows the count grid draws at once. A shop's whole master would be thousands. */
const PAGE = 200;

/** One product on the count, with the figures it is judged by. */
interface CountRow {
  readonly product: ProductSummary;
  readonly system: number;
  readonly averageCost: number;
}

/** What the uploaded sheet said that could not be matched to a product. */
interface UploadResult {
  readonly matched: number;
  readonly unmatched: readonly string[];
}

/**
 * Stock adjustment: correcting the books to what is on the shelf.
 *
 * The count is entered here, by hand or from a spreadsheet the storekeeper filled in,
 * and compared with what the system holds before anything moves. Only what was counted
 * is adjusted - a product nobody counted is not a product found to be missing - and the
 * differences are shown for review, with what they are worth, before they post. The
 * posting is one physical verification document, so the history, the stock ledger and
 * the accounts all carry the same correction.
 *
 * Batch-tracked products are counted batch by batch on the stock operations screen: a
 * product's total says nothing about which lot is short.
 */
export function StockAdjustmentPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [tab, setTab] = useState<'count' | 'history'>('count');

  return (
    <section className="page">
      {/* The history's report frame carries its own heading, so this one is the
          count's alone: two in one bar would fight over it. */}
      {tab === 'count' && <PageHeading title={t('nav.stockAdjustment')} />}
      <PageTabs
        tabs={[
          { value: 'count', label: t('adjustment.countTab') },
          { value: 'history', label: t('adjustment.historyTab') },
        ]}
        value={tab}
        onChange={setTab}
      />
      {tab === 'count' ? (
        <CountSheet onPosted={() => setTab('history')} />
      ) : (
        <StockHistoryTable
          kind={StockHistoryKind.adjustments}
          title={t('adjustment.historyTitle')}
        />
      )}
    </section>
  );
}

function CountSheet({ onPosted }: { readonly onPosted: () => void }): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const can = useSession((state) => state.can);
  const fileInput = useRef<HTMLInputElement>(null);

  const today = new Date().toISOString().slice(0, 10);
  const [date, setDate] = useState(today);
  const [warehouseId, setWarehouseId] = useState('');
  const [reason, setReason] = useState('');
  const [search, setSearch] = useState('');
  const [show, setShow] = useState<'all' | 'counted' | 'differences'>('all');
  const [counted, setCounted] = useState<ReadonlyMap<string, string>>(new Map());
  const [extra, setExtra] = useState<readonly string[]>([]);
  const [upload, setUpload] = useState<UploadResult | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [reviewing, setReviewing] = useState(false);

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });

  const places = (warehouses.data ?? []).filter((warehouse) => !warehouse.isTransit);
  const place = warehouseId || places.find((warehouse) => warehouse.isDefault)?.id || '';

  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', '', '', false],
    queryFn: () => listProducts('', '', false),
  });

  const valuation = useQuery<StockValuationReport, ApiError>({
    queryKey: ['stock-valuation', 'count', place],
    queryFn: () => fetchStockValuation(place, '', true),
    enabled: place !== '',
  });

  const countable = useMemo(
    () =>
      (products.data ?? []).filter(
        (product) => product.itemType === 1 && !product.tracksBatches && product.isActive,
      ),
    [products.data],
  );

  const positions = useMemo(() => {
    const map = new Map<string, { quantity: number; averageCost: number }>();

    for (const row of valuation.data?.rows ?? []) {
      map.set(row.productId, { quantity: row.quantity, averageCost: row.averageCost });
    }

    return map;
  }, [valuation.data]);

  // The products the warehouse has ever held, and any the count added. A product that
  // has never been here is offered through the picker rather than listed.
  const rows = useMemo<readonly CountRow[]>(
    () =>
      countable
        .filter(
          (product) =>
            positions.has(product.id) ||
            counted.has(product.id) ||
            extra.includes(product.id),
        )
        .map((product) => ({
          product,
          system: positions.get(product.id)?.quantity ?? 0,
          averageCost: positions.get(product.id)?.averageCost ?? product.cost,
        })),
    [countable, positions, counted, extra],
  );

  const difference = (row: CountRow): number | null => {
    const entered = counted.get(row.product.id);
    return entered === undefined || entered.trim() === ''
      ? null
      : Number(entered) - row.system;
  };

  const filtered = rows.filter((row) => {
    const term = search.trim().toLowerCase();
    const matches =
      !term ||
      row.product.code.toLowerCase().includes(term) ||
      row.product.description.toLowerCase().includes(term);
    const diff = difference(row);

    return (
      matches &&
      (show === 'all' ||
        (show === 'counted' && diff !== null) ||
        (show === 'differences' && diff !== null && diff !== 0))
    );
  });

  const countedRows = rows.filter((row) => difference(row) !== null);
  const changed = countedRows.filter((row) => difference(row) !== 0);
  const gain = changed.reduce(
    (sum, row) => sum + Math.max(difference(row) ?? 0, 0) * row.averageCost,
    0,
  );
  const loss = changed.reduce(
    (sum, row) => sum + Math.min(difference(row) ?? 0, 0) * row.averageCost,
    0,
  );

  const setCount = (productId: string, value: string): void =>
    setCounted((current) => {
      const next = new Map(current);
      if (value === '') {
        next.delete(productId);
      } else {
        next.set(productId, value);
      }
      return next;
    });

  const mutation = useMutation<string, ApiError>({
    mutationFn: async () => {
      const posted = await createStockDocument({
        type: StockDocumentType.physicalVerification,
        date,
        warehouseId: place,
        narration: reason.trim() || null,
        postImmediately: true,
        lines: countedRows.map((row) => ({
          productId: row.product.id,
          quantity: Number(counted.get(row.product.id)),
        })),
      });

      return posted.number;
    },
    onSuccess: async () => {
      setReviewing(false);
      setCounted(new Map());
      setExtra([]);
      setUpload(null);
      setReason('');
      await queryClient.invalidateQueries({ queryKey: ['stock-valuation'] });
      await queryClient.invalidateQueries({ queryKey: ['stock-history'] });
      await queryClient.invalidateQueries({ queryKey: ['stock-documents'] });
      onPosted();
    },
  });

  const applySheet = (sheet: SheetRows): void => {
    const header = sheet.findIndex(
      (row) =>
        row.some((cell) => /code|barcode|item/i.test(cell)) &&
        row.some((cell) => /count|actual|physical|qty|quantity/i.test(cell)),
    );

    const headings = header >= 0 ? (sheet[header] ?? []) : [];
    const codeColumn = Math.max(
      0,
      headings.findIndex((cell) => /code|barcode|item/i.test(cell)),
    );

    // The counted column by name, preferring "counted" or "actual" over a bare
    // "quantity" - the template carries both the system's figure and the count.
    const byName = (pattern: RegExp): number =>
      headings.findIndex((cell) => pattern.test(cell));
    let countColumn = byName(/count|actual|physical/i);
    if (countColumn < 0) countColumn = byName(/qty|quantity/i);

    const byCode = new Map(
      countable.map((product) => [product.code.trim().toLowerCase(), product]),
    );
    const next = new Map(counted);
    const added: string[] = [];
    const unmatched: string[] = [];
    let matched = 0;

    for (const row of sheet.slice(header + 1)) {
      const code = (row[codeColumn] ?? '').trim();
      const cells = countColumn >= 0 ? [row[countColumn] ?? ''] : [...row].reverse();
      const quantity = cells
        .map((cell) => cell.trim().replace(',', '.'))
        .find((cell) => cell !== '' && Number.isFinite(Number(cell)));

      if (!code || quantity === undefined) {
        continue;
      }

      const product = byCode.get(code.toLowerCase());

      if (!product) {
        unmatched.push(code);
        continue;
      }

      next.set(product.id, quantity);
      added.push(product.id);
      matched++;
    }

    setCounted(next);
    setExtra((current) => [...new Set([...current, ...added])]);
    setUpload({ matched, unmatched });
    setShow('counted');
  };

  const onFile = async (file: File | undefined): Promise<void> => {
    if (!file) return;
    setProblem(null);

    try {
      applySheet(await readSpreadsheet(file));
    } catch {
      setProblem(t('adjustment.unreadable'));
    } finally {
      if (fileInput.current) fileInput.current.value = '';
    }
  };

  const template = (): void =>
    downloadCsv(`stock-count-${today}.csv`, [
      [
        t('adjustment.colCode'),
        t('adjustment.colDescription'),
        t('adjustment.colUnit'),
        t('adjustment.colSystem'),
        t('adjustment.colCounted'),
      ],
      ...rows.map((row) => [
        row.product.code,
        row.product.description,
        row.product.stockUnitCode,
        String(row.system),
        counted.get(row.product.id) ?? '',
      ]),
    ]);

  const quantity = (value: number): string =>
    value.toLocaleString(undefined, { maximumFractionDigits: 6 });

  return (
    <div className="space-y-4">
      <div className="card card-body grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <label className="block">
          <span className="field-label">{t('stock.date')}</span>
          <input
            type="date"
            value={date}
            onChange={(event) => setDate(event.target.value)}
            className="field-input"
          />
        </label>
        <label className="block">
          <span className="field-label">{t('stock.warehouse')}</span>
          <SearchSelect
            value={place}
            onChange={(next) => {
              setWarehouseId(next);
              setCounted(new Map());
              setExtra([]);
              setUpload(null);
            }}
            label={t('stock.warehouse')}
            placeholder={t('stock.choose')}
            options={places.map((warehouse) => ({
              value: warehouse.id,
              label: warehouse.name,
            }))}
          />
        </label>
        <label className="block lg:col-span-2">
          <span className="field-label">{t('history.reason')}</span>
          <input
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder={t('adjustment.reasonPlaceholder')}
            className="field-input"
          />
        </label>
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <label className="field min-w-48 flex-1">
          <span className="field-label">{t('adjustment.search')}</span>
          <input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            className="field-input-sm"
          />
        </label>
        <label className="field">
          <span className="field-label">{t('adjustment.show')}</span>
          <select
            value={show}
            onChange={(event) => setShow(event.target.value as typeof show)}
            className="field-input-sm"
          >
            <option value="all">{t('adjustment.showAll')}</option>
            <option value="counted">{t('adjustment.showCounted')}</option>
            <option value="differences">{t('adjustment.showDifferences')}</option>
          </select>
        </label>
        <div className="field min-w-64">
          <span className="field-label">{t('adjustment.addProduct')}</span>
          <SearchSelect
            value=""
            onChange={(productId) => {
              if (productId) {
                setExtra((current) => [...new Set([...current, productId])]);
                setSearch('');
              }
            }}
            size="sm"
            label={t('adjustment.addProduct')}
            placeholder={t('stock.choose')}
            options={countable
              .filter(
                (product) => !positions.has(product.id) && !extra.includes(product.id),
              )
              .map((product) => productOption(product, 0))}
          />
        </div>
        <button type="button" onClick={template} className="btn-secondary btn-sm">
          {t('adjustment.downloadSheet')}
        </button>
        <button
          type="button"
          onClick={() => fileInput.current?.click()}
          className="btn-secondary btn-sm"
        >
          {t('adjustment.uploadSheet')}
        </button>
        <input
          ref={fileInput}
          type="file"
          accept=".xlsx,.csv,text/csv"
          className="hidden"
          onChange={(event) => void onFile(event.target.files?.[0])}
        />
      </div>

      <p className="text-xs text-ink-muted">
        {t('adjustment.subtitle')} {t('adjustment.hint')}
      </p>

      {problem && <Alert tone="error">{problem}</Alert>}
      {upload && (
        <Alert tone={upload.unmatched.length > 0 ? 'muted' : 'ok'}>
          {t('adjustment.uploaded', { count: upload.matched })}
          {upload.unmatched.length > 0 && (
            <span className="block text-xs">
              {t('adjustment.unmatched', { count: upload.unmatched.length })}{' '}
              {upload.unmatched.slice(0, 20).join(', ')}
              {upload.unmatched.length > 20 ? '…' : ''}
            </span>
          )}
        </Alert>
      )}

      <div className="overflow-x-auto sm:max-h-[60vh] sm:overflow-y-auto sm:rounded-xl sm:border sm:border-line sm:bg-surface sm:shadow-card">
        <table className="line-table">
          <thead className="bg-surface-3 text-xs tracking-wide text-ink-muted uppercase">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.product')}</th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('adjustment.colSystem')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('adjustment.colCounted')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('history.adjusted')}
              </th>
              {can('inventory:report:view') && (
                <th className="px-3 py-2 text-end font-semibold">{t('stock.value')}</th>
              )}
            </tr>
          </thead>
          <tbody>
            {filtered.slice(0, PAGE).map((row) => {
              const diff = difference(row);

              return (
                <tr key={row.product.id} className="border-t border-line">
                  <td data-label={t('stock.product')} className="px-3 py-1.5">
                    <span className="font-mono text-xs text-ink-muted">
                      {row.product.code}
                    </span>{' '}
                    {row.product.description}
                  </td>
                  <td data-label={t('adjustment.colSystem')} className="cell-numeric">
                    {quantity(row.system)} {row.product.stockUnitCode}
                  </td>
                  <td
                    data-label={t('adjustment.colCounted')}
                    className="px-2 py-1 text-end"
                  >
                    <input
                      type="number"
                      step="any"
                      min="0"
                      value={counted.get(row.product.id) ?? ''}
                      onChange={(event) => setCount(row.product.id, event.target.value)}
                      aria-label={t('adjustment.colCounted')}
                      className="field-input-sm w-28 text-end font-mono tabular-nums"
                    />
                  </td>
                  <td
                    data-label={t('history.adjusted')}
                    className={clsx(
                      'cell-numeric',
                      diff !== null &&
                        diff > 0 &&
                        'text-emerald-700 dark:text-emerald-300',
                      diff !== null && diff < 0 && 'text-red-700 dark:text-red-300',
                    )}
                  >
                    {diff === null ? '' : `${diff > 0 ? '+' : ''}${quantity(diff)}`}
                  </td>
                  {can('inventory:report:view') && (
                    <td data-label={t('stock.value')} className="cell-numeric">
                      {diff === null || diff === 0
                        ? ''
                        : moneyAlways(diff * row.averageCost)}
                    </td>
                  )}
                </tr>
              );
            })}
            {filtered.length === 0 && (
              <tr>
                <td colSpan={5} className="px-3 py-6 text-center text-sm text-ink-muted">
                  {place
                    ? t('adjustment.nothingToCount')
                    : t('adjustment.chooseWarehouse')}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {filtered.length > PAGE && (
        <p className="text-xs text-ink-muted">
          {t('adjustment.showingFirst', { shown: PAGE, total: filtered.length })}
        </p>
      )}

      <div className="flex flex-wrap items-center gap-3 border-t border-line/60 pt-3 text-sm">
        <span>
          {t('adjustment.summary', {
            counted: countedRows.length,
            changed: changed.length,
          })}
        </span>
        {can('inventory:report:view') && changed.length > 0 && (
          <span className="text-xs text-ink-muted">
            {t('adjustment.gain')} {moneyAlways(gain)} · {t('adjustment.loss')}{' '}
            {moneyAlways(loss)}
          </span>
        )}
        <button
          type="button"
          disabled={
            countedRows.length === 0 ||
            !place ||
            !can('inventory:stock-adjustment:create')
          }
          onClick={() => setReviewing(true)}
          className="btn-primary ms-auto"
        >
          {t('adjustment.review')}
        </button>
      </div>

      {reviewing && (
        <Modal
          title={t('adjustment.reviewTitle')}
          size="wide"
          onClose={() => setReviewing(false)}
        >
          <div className="space-y-4">
            {mutation.isError && (
              <Alert tone="error">{mutation.error.detail || mutation.error.code}</Alert>
            )}
            <p className="text-sm">
              {t('adjustment.reviewIntro', {
                counted: countedRows.length,
                changed: changed.length,
                warehouse: places.find((warehouse) => warehouse.id === place)?.name ?? '',
              })}
            </p>
            <div className="overflow-x-auto sm:max-h-[50vh] sm:overflow-y-auto sm:rounded-xl sm:border sm:border-line">
              <table className="line-table">
                <thead className="bg-surface-3 text-xs tracking-wide text-ink-muted uppercase">
                  <tr>
                    <th className="px-3 py-2 text-start font-semibold">
                      {t('stock.product')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('history.previous')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('history.actual')}
                    </th>
                    <th className="px-3 py-2 text-end font-semibold">
                      {t('history.adjusted')}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {changed.map((row) => {
                    const diff = difference(row) ?? 0;
                    return (
                      <tr key={row.product.id} className="border-t border-line">
                        <td data-label={t('stock.product')} className="px-3 py-1.5">
                          <span className="font-mono text-xs text-ink-muted">
                            {row.product.code}
                          </span>{' '}
                          {row.product.description}
                        </td>
                        <td data-label={t('history.previous')} className="cell-numeric">
                          {quantity(row.system)}
                        </td>
                        <td data-label={t('history.actual')} className="cell-numeric">
                          {counted.get(row.product.id)}
                        </td>
                        <td
                          data-label={t('history.adjusted')}
                          className={clsx(
                            'cell-numeric',
                            diff > 0
                              ? 'text-emerald-700 dark:text-emerald-300'
                              : 'text-red-700 dark:text-red-300',
                          )}
                        >
                          {diff > 0 ? '+' : ''}
                          {quantity(diff)} {row.product.stockUnitCode}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            {changed.length === 0 && (
              <p className="text-sm text-ink-muted">{t('adjustment.noDifferences')}</p>
            )}
            <div className="flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setReviewing(false)}
                className="btn-secondary"
              >
                {t('adjustment.back')}
              </button>
              <button
                type="button"
                disabled={mutation.isPending}
                onClick={() => mutation.mutate()}
                className="btn-primary"
              >
                {t('adjustment.confirm')}
              </button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}

/**
 * Damaged stock: goods written off, with why.
 *
 * The register of write-offs, entered as any stock document is but with the reason
 * asked for up front, and the history of what was written off beside it.
 */
export function DamagedStockPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [tab, setTab] = useState<'entries' | 'history'>('entries');

  return (
    <div className="space-y-3">
      <PageTabs
        tabs={[
          { value: 'entries', label: t('damaged.entriesTab') },
          { value: 'history', label: t('damaged.historyTab') },
        ]}
        value={tab}
        onChange={setTab}
      />
      {tab === 'entries' ? (
        <DamagedEntries />
      ) : (
        <StockHistoryTable
          kind={StockHistoryKind.damaged}
          title={t('damaged.historyTitle')}
        />
      )}
    </div>
  );
}

function DamagedEntries(): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <StockOperationsPage
      lockedType={StockDocumentType.damagedStock}
      title={t('nav.damagedStock')}
      reasonRequired
    />
  );
}
