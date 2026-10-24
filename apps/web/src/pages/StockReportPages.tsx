import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { EmptyState, ReportFrame } from '@/components/ReportFrame';
import { PageHeading } from '@/components/PageHeading';
import { StatusBadge } from '@/components/StatusBadge';
import { trim, typeKey } from '@/pages/StockOperationsPage';
import type { ApiError } from '@/lib/api';
import { listMaster, type CategorySummary, type WarehouseSummary } from '@/lib/inventory';
import { listProducts, type ProductSummary } from '@/lib/products';
import {
  fetchBatchStock,
  fetchExpiring,
  fetchSalesProfit,
  setBatchSellingRate,
  fetchItemMovement,
  fetchProductSerials,
  fetchStockLedger,
  fetchStockRegister,
  type StockRegisterReport,
  type StockRegisterRow,
  fetchStockValuation,
  SerialStatus,
  type BatchStockReport,
  type BatchStockRow,
  type ItemMovementRow,
  type SalesProfitReport,
  type SalesProfitRow,
  type SerialNumberView,
  type StockLedgerReport,
  type StockValuationReport,
  type StockValuationRow,
} from '@/lib/stock';
import { moneyAlways } from '@/lib/money';
import { SearchSelect } from '@/components/SearchSelect';

/**
 * The stock valuation.
 *
 * Read from the positions rather than by summing the ledger. The position is the
 * running answer, maintained on every movement; summing several years of movements to
 * reproduce it would be slower and could only ever agree.
 */
export function StockValuationPage(): React.JSX.Element {
  const { t } = useTranslation();

  const [warehouseId, setWarehouseId] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [includeZero, setIncludeZero] = useState(false);

  const query = useQuery<StockValuationReport, ApiError>({
    queryKey: ['stock-valuation', warehouseId, categoryId, includeZero],
    queryFn: () => fetchStockValuation(warehouseId, categoryId, includeZero),
  });

  const columns: readonly GridColumn<StockValuationRow>[] = [
    { key: 'code', header: t('masters.code'), value: (row) => row.productCode },
    {
      key: 'description',
      header: t('products.description'),
      value: (row) => row.productDescription,
      // Opens the item's movements, which is what explains the figure beside it.
      render: (row) => (
        <Link
          to={`/inventory/product-history?productId=${row.productId}`}
          className="font-medium text-brand-700 hover:underline dark:text-brand-300"
        >
          {row.productDescription}
        </Link>
      ),
    },
    { key: 'category', header: t('products.category'), value: (row) => row.categoryName },
    { key: 'warehouse', header: t('stock.warehouse'), value: (row) => row.warehouseName },
    {
      key: 'quantity',
      header: t('stock.onHand'),
      value: (row) => row.quantity,
      numeric: true,
      render: (row) => (
        <span
          className={clsx(
            row.isBelowReorderLevel && 'font-semibold text-amber-700 dark:text-amber-300',
          )}
        >
          {trim(row.quantity)} {row.stockUnitCode}
        </span>
      ),
    },
    {
      key: 'reorder',
      header: t('products.reorderLevel'),
      value: (row) => row.reorderLevel,
      numeric: true,
      render: (row) => (row.reorderLevel === 0 ? '' : trim(row.reorderLevel)),
      hiddenByDefault: true,
    },
    {
      key: 'average',
      header: t('stock.averageCost'),
      value: (row) => row.averageCost,
      numeric: true,
      render: (row) => trim(row.averageCost),
    },
    {
      key: 'value',
      header: t('stock.value'),
      value: (row) => row.value,
      numeric: true,
      render: (row) => moneyAlways(row.value),
    },
  ];

  const controls = (
    <div className="toolbar">
      <WarehousePicker value={warehouseId} onChange={setWarehouseId} />
      <CategoryPicker value={categoryId} onChange={setCategoryId} />

      <label className="field-check">
        <input
          type="checkbox"
          checked={includeZero}
          onChange={(event) => setIncludeZero(event.target.checked)}
        />
        {t('stock.includeZero')}
      </label>
    </div>
  );

  return (
    <ReportFrame title={t('nav.stockValuation')} controls={controls} query={query}>
      {(report) => (
        <div className="space-y-4">
          <p className="inline-block rounded-lg border border-line bg-surface-2 px-3 py-1.5 text-sm font-medium text-ink">
            {t('stock.totalValue', {
              value: moneyAlways(report.totalValue),
              currency: report.currency,
            })}
          </p>

          <DataGrid
            gridKey="stock-valuation"
            rows={report.rows}
            columns={columns}
            rowKey={(row) => `${row.productId}-${row.warehouseId}`}
            emptyMessage={t('stock.nothingOnHand')}
          />
        </div>
      )}
    </ReportFrame>
  );
}

/**
 * The batch-wise stock of section 8.3.
 *
 * Its total is the stock valuation's total for the same products, and not by
 * coincidence: every batch movement moves the product's position by the same quantity
 * at the same cost, so the two reports are two views of one number.
 */
export function BatchStockPage(): React.JSX.Element {
  const { t } = useTranslation();

  const [warehouseId, setWarehouseId] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [includeZero, setIncludeZero] = useState(false);

  const query = useQuery<BatchStockReport, ApiError>({
    queryKey: ['batch-stock', warehouseId, categoryId, includeZero],
    queryFn: () => fetchBatchStock(warehouseId, categoryId, includeZero),
  });

  const controls = (
    <div className="toolbar">
      <WarehousePicker value={warehouseId} onChange={setWarehouseId} />
      <CategoryPicker value={categoryId} onChange={setCategoryId} />

      <label className="field-check">
        <input
          type="checkbox"
          checked={includeZero}
          onChange={(event) => setIncludeZero(event.target.checked)}
        />
        {t('stock.includeZero')}
      </label>
    </div>
  );

  return (
    <ReportFrame title={t('nav.batchStock')} controls={controls} query={query}>
      {(report) => (
        <div className="space-y-4">
          <p className="inline-block rounded-lg border border-line bg-surface-2 px-3 py-1.5 text-sm font-medium text-ink">
            {t('stock.totalValue', {
              value: moneyAlways(report.totalValue),
              currency: report.currency,
            })}
          </p>

          <DataGrid
            gridKey="batch-stock"
            rows={report.rows}
            columns={batchColumns(t)}
            rowKey={(row) => `${row.batchId}-${row.warehouseId}`}
            emptyMessage={t('stock.noBatchStock')}
          />
        </div>
      )}
    </ReportFrame>
  );
}

/**
 * The expiry report of section 8.3.
 *
 * Read from what is on a shelf rather than from the batch master. A lot that expired
 * last year and sold out in full is not something anybody can act on, and listing it
 * would bury the ones that are still there.
 */
export function ExpiryReportPage(): React.JSX.Element {
  const { t } = useTranslation();

  const today = new Date().toISOString().slice(0, 10);

  const [asOn, setAsOn] = useState(today);
  const [withinDays, setWithinDays] = useState<number | ''>('');
  const [warehouseId, setWarehouseId] = useState('');
  const [categoryId, setCategoryId] = useState('');

  const query = useQuery<readonly BatchStockRow[], ApiError>({
    queryKey: ['expiry', asOn, withinDays, warehouseId, categoryId],
    queryFn: () => fetchExpiring(asOn, withinDays, warehouseId, categoryId),
  });

  const controls = (
    <div className="toolbar">
      <DateBox label={t('stock.asOn')} value={asOn} onChange={setAsOn} />

      <label className="field">
        <span className="field-label">{t('stock.expiringWithin')}</span>
        <SearchSelect
          value={withinDays === '' ? '' : String(withinDays)}
          onChange={(next) => setWithinDays(next === '' ? '' : Number(next))}
          clearable
          size="sm"
          label={t('stock.expiringWithin')}
          placeholder={t('stock.onlyExpired')}
          options={[30, 60, 90, 180, 365].map((days) => ({
            value: String(days),
            label: `${days} ${t('stock.days')}`,
          }))}
        />
      </label>

      <WarehousePicker value={warehouseId} onChange={setWarehouseId} />
      <CategoryPicker value={categoryId} onChange={setCategoryId} />
    </div>
  );

  return (
    <ReportFrame title={t('nav.expiryReport')} controls={controls} query={query}>
      {(rows) => (
        <DataGrid
          gridKey="expiry-report"
          rows={rows}
          columns={batchColumns(t, true)}
          rowKey={(row) => `${row.batchId}-${row.warehouseId}`}
          emptyMessage={t('stock.nothingExpiring')}
        />
      )}
    </ReportFrame>
  );
}

/**
 * The columns both batch reports show.
 *
 * One definition rather than two. The two reports differ in which batches they select
 * and in whether the days-left column earns its place, not in what a batch is.
 */
function batchColumns(
  t: ReturnType<typeof useTranslation>['t'],
  expiring = false,
): readonly GridColumn<BatchStockRow>[] {
  return [
    { key: 'batch', header: t('stock.batch'), value: (row) => row.batchNumber },
    { key: 'code', header: t('masters.code'), value: (row) => row.productCode },
    {
      key: 'description',
      header: t('products.description'),
      value: (row) => row.productDescription,
    },
    { key: 'warehouse', header: t('stock.warehouse'), value: (row) => row.warehouseName },
    {
      key: 'quantity',
      header: t('stock.onHand'),
      value: (row) => row.quantity,
      numeric: true,
      render: (row) => `${trim(row.quantity)} ${row.stockUnitCode}`,
    },
    {
      key: 'unitCost',
      header: t('stock.unitCost'),
      value: (row) => row.unitCost,
      numeric: true,
      render: (row) => trim(row.unitCost),
    },
    {
      key: 'purchaseRate',
      header: t('stock.purchaseRate'),
      value: (row) => row.purchaseRate,
      numeric: true,
      render: (row) => trim(row.purchaseRate),
      hiddenByDefault: !expiring,
    },
    {
      key: 'sellingRate',
      header: t('stock.sellingRate'),
      value: (row) => row.sellingRate ?? 0,
      numeric: true,
      // Set here as well as on the purchase that brought the batch in, for a batch
      // whose price changes while it is on the shelf.
      render: (row) => <SellingRateCell row={row} />,
      hiddenByDefault: expiring,
    },
    {
      key: 'value',
      header: t('stock.value'),
      value: (row) => row.value,
      numeric: true,
      render: (row) => moneyAlways(row.value),
    },
    {
      key: 'manufactured',
      header: t('stock.manufacturedOn'),
      value: (row) => row.manufacturedOn ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'expires',
      header: t('stock.expiresOn'),
      value: (row) => row.expiresOn ?? '',
    },
    {
      key: 'daysLeft',
      header: t('stock.daysToExpiry'),
      value: (row) => row.daysToExpiry ?? Number.MAX_SAFE_INTEGER,
      numeric: true,
      // Past its date is the one thing on this report that has to be visible at a
      // glance, so it is coloured rather than merely negative.
      render: (row) =>
        row.daysToExpiry === null ? (
          ''
        ) : (
          <span
            className={clsx(
              row.daysToExpiry < 0 && 'font-semibold text-red-700 dark:text-red-300',
              row.daysToExpiry >= 0 &&
                row.daysToExpiry <= 30 &&
                'font-semibold text-amber-700 dark:text-amber-300',
            )}
          >
            {row.daysToExpiry < 0 ? t('stock.expired') : row.daysToExpiry}
          </span>
        ),
      hiddenByDefault: !expiring,
    },
  ];
}

/** A batch's selling rate, changed where it is shown and saved when the field is left. */
function SellingRateCell({ row }: { readonly row: BatchStockRow }): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const shown = row.sellingRate ? String(row.sellingRate) : '';
  const [value, setValue] = useState(shown);

  const save = useMutation<void, ApiError, number | null>({
    mutationFn: (rate) => setBatchSellingRate(row.batchId, rate),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['batch-stock'] }),
  });

  const commit = (): void => {
    const rate = Number(value) > 0 ? Number(value) : null;

    if ((rate ?? 0) !== (row.sellingRate ?? 0)) {
      save.mutate(rate);
    }
  };

  return (
    <input
      value={value}
      inputMode="decimal"
      aria-label={t('stock.sellingRate')}
      placeholder="—"
      onChange={(event) => setValue(event.target.value.replace(/[^0-9.]/g, ''))}
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === 'Enter') event.currentTarget.blur();
      }}
      className={clsx(
        'field-input-sm w-24 text-end tabular-nums',
        save.isError && 'border-red-500',
      )}
    />
  );
}

/**
 * Profit on what was sold, each batch at its own cost.
 *
 * The requirement's own example is the test of it: a product bought in one batch at 10
 * and another at 15, both sold at 20, shows 10 on the first and 5 on the second - the
 * cost the stock ledger took each batch out at, not the product's average.
 */
export function BatchProfitPage(): React.JSX.Element {
  const { t } = useTranslation();

  const today = new Date().toISOString().slice(0, 10);

  const [productId, setProductId] = useState('');
  const [from, setFrom] = useState(`${today.slice(0, 7)}-01`);
  const [to, setTo] = useState(today);

  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', '', '', false],
    queryFn: () => listProducts('', '', false),
  });

  const query = useQuery<SalesProfitReport, ApiError>({
    queryKey: ['batch-profit', from, to, productId],
    queryFn: () => fetchSalesProfit(from, to, productId),
  });

  const columns: readonly GridColumn<SalesProfitRow>[] = [
    { key: 'code', header: t('masters.code'), value: (row) => row.productCode },
    { key: 'name', header: t('products.description'), value: (row) => row.productName },
    { key: 'batch', header: t('stock.batch'), value: (row) => row.batchNumber ?? '—' },
    {
      key: 'quantity',
      header: t('stock.soldQuantity'),
      value: (row) => row.quantity,
      numeric: true,
      render: (row) => trim(row.quantity),
    },
    {
      key: 'purchaseRate',
      header: t('stock.purchaseRate'),
      value: (row) => row.purchaseRate ?? 0,
      numeric: true,
      render: (row) => (row.purchaseRate === null ? '—' : trim(row.purchaseRate)),
      hiddenByDefault: true,
    },
    {
      key: 'sellingRate',
      header: t('stock.sellingRate'),
      value: (row) => row.sellingRate ?? 0,
      numeric: true,
      render: (row) => (row.sellingRate === null ? '—' : trim(row.sellingRate)),
      hiddenByDefault: true,
    },
    {
      key: 'sales',
      header: t('stock.salesValue'),
      value: (row) => row.sales,
      numeric: true,
      render: (row) => moneyAlways(row.sales),
    },
    {
      key: 'cost',
      header: t('stock.costOfSales'),
      value: (row) => row.cost,
      numeric: true,
      render: (row) => moneyAlways(row.cost),
    },
    {
      key: 'profit',
      header: t('stock.profit'),
      value: (row) => row.profit,
      numeric: true,
      render: (row) => (
        <span
          className={clsx(
            row.profit < 0 && 'font-semibold text-red-700 dark:text-red-300',
          )}
        >
          {moneyAlways(row.profit)}
        </span>
      ),
    },
    {
      key: 'margin',
      header: t('stock.margin'),
      value: (row) => row.marginPercent ?? 0,
      numeric: true,
      render: (row) => (row.marginPercent === null ? '—' : `${row.marginPercent}%`),
    },
  ];

  const controls = (
    <div className="toolbar">
      <DateBox label={t('reports.from')} value={from} onChange={setFrom} />
      <DateBox label={t('reports.to')} value={to} onChange={setTo} />
      <label className="field">
        <span className="field-label">{t('stock.product')}</span>
        <SearchSelect
          value={productId}
          onChange={setProductId}
          clearable
          size="sm"
          className="w-full sm:w-72"
          label={t('stock.product')}
          placeholder={t('stock.allProducts')}
          options={(products.data ?? []).map((product) => ({
            value: product.id,
            label: `${product.code} — ${product.description}`,
          }))}
        />
      </label>
    </div>
  );

  return (
    <ReportFrame title={t('nav.batchProfit')} controls={controls} query={query}>
      {(report) => (
        <div className="space-y-4">
          <div className="flex flex-wrap gap-3 text-sm">
            <Figure
              label={t('stock.salesValue')}
              value={moneyAlways(report.totalSales)}
            />
            <Figure
              label={t('stock.costOfSales')}
              value={moneyAlways(report.totalCost)}
            />
            <Figure label={t('stock.profit')} value={moneyAlways(report.totalProfit)} />
          </div>

          <DataGrid
            gridKey="batch-profit"
            rows={report.rows}
            columns={columns}
            rowKey={(row) => `${row.productId}-${row.batchId ?? ''}`}
            emptyMessage={t('stock.noSales')}
          />
        </div>
      )}
    </ReportFrame>
  );
}

/**
 * The stock ledger of one product.
 *
 * One product at a time, deliberately: the running balance column only means anything
 * within one product, and a ledger that mixed several would show a column of figures
 * that appear to jump about.
 */
export function StockLedgerPage(): React.JSX.Element {
  const { t } = useTranslation();

  return <ProductMovements title={t('nav.stockLedger')} />;
}

/**
 * Everything that happened to one product: purchases, sales, returns, transfers and
 * adjustments, with the stock each left. The same screen as the stock ledger, under
 * the name the counter looks for it by, and the one the stock report opens.
 */
export function ProductHistoryPage(): React.JSX.Element {
  const { t } = useTranslation();

  return <ProductMovements title={t('nav.productHistory')} />;
}

function ProductMovements({ title }: { readonly title: string }): React.JSX.Element {
  const { t } = useTranslation();
  const [params] = useSearchParams();

  const today = new Date().toISOString().slice(0, 10);

  // Opened from another report, the product and the period come with it.
  const [productId, setProductId] = useState(params.get('productId') ?? '');
  const [from, setFrom] = useState(params.get('from') ?? `${today.slice(0, 4)}-01-01`);
  const [to, setTo] = useState(params.get('to') ?? today);
  const [warehouseId, setWarehouseId] = useState('');

  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', '', '', false],
    queryFn: () => listProducts('', '', false),
  });

  const query = useQuery<StockLedgerReport, ApiError>({
    queryKey: ['stock-ledger', productId, from, to, warehouseId],
    queryFn: () => fetchStockLedger(productId, from, to, warehouseId),
    // Nothing to read until a product is chosen, and asking for one that has not been
    // would be a round trip guaranteed to 404.
    enabled: productId !== '',
  });

  const controls = (
    <div className="toolbar">
      <label className="field">
        <span className="field-label">{t('stock.product')}</span>
        <SearchSelect
          value={productId}
          onChange={setProductId}
          clearable
          size="sm"
          className="w-full sm:w-72"
          label={t('stock.product')}
          placeholder={t('stock.choose')}
          options={(products.data ?? []).map((product) => ({
            value: product.id,
            label: `${product.code} — ${product.description}`,
          }))}
        />
      </label>

      <DateBox label={t('reports.from')} value={from} onChange={setFrom} />
      <DateBox label={t('reports.to')} value={to} onChange={setTo} />
      <WarehousePicker value={warehouseId} onChange={setWarehouseId} />
    </div>
  );

  if (!productId) {
    return (
      // The same arrangement as the report once a product is chosen: the title in
      // the bar, the filters in their strip. It used to put the title and the
      // filters side by side, which on a phone left the four fields a third of
      // the screen beside a two-line heading.
      <section className="page">
        <PageHeading title={title} />
        <div className="filter-bar">{controls}</div>
        <EmptyState message={t('stock.chooseProduct')} />
      </section>
    );
  }

  return (
    <ReportFrame title={title} controls={controls} query={query}>
      {(report) => (
        <div className="space-y-4">
          <div className="flex flex-wrap gap-3 text-sm">
            <Figure
              label={t('stock.opening')}
              value={`${trim(report.openingQuantity)} ${report.stockUnitCode}`}
            />
            <Figure label={t('stock.totalIn')} value={trim(report.totalIn)} />
            <Figure label={t('stock.totalOut')} value={trim(report.totalOut)} />
            <Figure
              label={t('stock.closing')}
              value={`${trim(report.closingQuantity)} ${report.stockUnitCode}`}
            />
          </div>

          <div className="table-wrap max-h-[70vh] overflow-y-auto">
            <table className="table">
              <thead className="bg-surface-3">
                <tr>
                  <th className="px-3 py-2 text-start font-semibold">
                    {t('stock.date')}
                  </th>
                  <th className="px-3 py-2 text-start font-semibold">
                    {t('stock.number')}
                  </th>
                  <th className="px-3 py-2 text-start font-semibold">
                    {t('stock.type')}
                  </th>
                  <th className="px-3 py-2 text-start font-semibold">
                    {t('stock.warehouse')}
                  </th>
                  <th className="px-3 py-2 text-end font-semibold">{t('stock.in')}</th>
                  <th className="px-3 py-2 text-end font-semibold">{t('stock.out')}</th>
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
                </tr>
              </thead>
              <tbody>
                {report.rows.length === 0 ? (
                  <tr>
                    <td
                      colSpan={10}
                      className="px-3 py-6 text-center text-sm text-ink-muted"
                    >
                      {t('stock.noMovementsInRange')}
                    </td>
                  </tr>
                ) : (
                  report.rows.map((row, index) => (
                    <tr
                      key={`${row.documentId}-${index}`}
                      className="border-t border-line"
                    >
                      <td className="px-3 py-1.5">{row.date}</td>
                      <td className="px-3 py-1.5 font-mono">{row.documentNumber}</td>
                      <td className="px-3 py-1.5">{t(typeKey(row.documentType))}</td>
                      <td className="px-3 py-1.5">{row.warehouseName}</td>
                      <td className="cell-numeric">
                        {row.quantityIn === 0 ? '' : trim(row.quantityIn)}
                      </td>
                      <td className="cell-numeric">
                        {row.quantityOut === 0 ? '' : trim(row.quantityOut)}
                      </td>
                      <td className="cell-numeric">{trim(row.unitCost)}</td>
                      <td className="cell-numeric">{moneyAlways(row.value)}</td>
                      <td className="cell-numeric">{trim(row.balanceQuantity)}</td>
                      <td className="cell-numeric">{trim(row.balanceAverageCost)}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </ReportFrame>
  );
}

/**
 * Item movement.
 *
 * Answers which products turn over and which sit still — the question behind the
 * movement classification on the product master, and the reason that field is worth
 * setting from a report rather than from memory.
 */
export function ItemMovementPage(): React.JSX.Element {
  const { t } = useTranslation();

  const today = new Date().toISOString().slice(0, 10);

  const [from, setFrom] = useState(`${today.slice(0, 7)}-01`);
  const [to, setTo] = useState(today);
  const [warehouseId, setWarehouseId] = useState('');
  const [categoryId, setCategoryId] = useState('');

  const query = useQuery<readonly ItemMovementRow[], ApiError>({
    queryKey: ['item-movement', from, to, warehouseId, categoryId],
    queryFn: () => fetchItemMovement(from, to, warehouseId, categoryId),
  });

  const columns: readonly GridColumn<ItemMovementRow>[] = [
    { key: 'code', header: t('masters.code'), value: (row) => row.productCode },
    {
      key: 'description',
      header: t('products.description'),
      value: (row) => row.productDescription,
    },
    { key: 'category', header: t('products.category'), value: (row) => row.categoryName },
    {
      key: 'in',
      header: t('stock.in'),
      value: (row) => row.quantityIn,
      numeric: true,
      render: (row) => `${trim(row.quantityIn)} ${row.stockUnitCode}`,
    },
    {
      key: 'out',
      header: t('stock.out'),
      value: (row) => row.quantityOut,
      numeric: true,
      render: (row) => `${trim(row.quantityOut)} ${row.stockUnitCode}`,
    },
    {
      key: 'valueIn',
      header: t('stock.valueIn'),
      value: (row) => row.valueIn,
      numeric: true,
      render: (row) => moneyAlways(row.valueIn),
    },
    {
      key: 'valueOut',
      header: t('stock.valueOut'),
      value: (row) => row.valueOut,
      numeric: true,
      render: (row) => moneyAlways(row.valueOut),
    },
    {
      key: 'movements',
      header: t('stock.movements'),
      value: (row) => row.movements,
      numeric: true,
    },
    {
      key: 'last',
      header: t('stock.lastMoved'),
      value: (row) => row.lastMovedOn ?? '',
    },
  ];

  const controls = (
    <div className="toolbar">
      <DateBox label={t('reports.from')} value={from} onChange={setFrom} />
      <DateBox label={t('reports.to')} value={to} onChange={setTo} />
      <WarehousePicker value={warehouseId} onChange={setWarehouseId} />
      <CategoryPicker value={categoryId} onChange={setCategoryId} />
    </div>
  );

  return (
    <ReportFrame title={t('nav.itemMovement')} controls={controls} query={query}>
      {(rows) => (
        <DataGrid
          gridKey="item-movement"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.productId}
          emptyMessage={t('stock.nothingMoved')}
        />
      )}
    </ReportFrame>
  );
}

/**
 * Serial numbers tracking and register (Requirements 13 & 14).
 *
 * Shows all serialised units across warehouses and products,
 * including warranty status, cost, batch number, and current status.
 */
export function SerialStockPage(): React.JSX.Element {
  const { t } = useTranslation();

  const [warehouseId, setWarehouseId] = useState('');
  const [productId, setProductId] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<number | ''>('');

  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', 'picker'],
    queryFn: () => listProducts('', '', false),
  });

  const serials = useQuery<readonly SerialNumberView[], ApiError>({
    queryKey: ['serials-report', productId, warehouseId],
    queryFn: () => fetchProductSerials(productId, warehouseId, true),
  });

  const rows = useMemo(() => {
    let list = serials.data ?? [];
    if (statusFilter !== '') {
      list = list.filter((r) => r.status === statusFilter);
    }
    if (search.trim()) {
      const q = search.trim().toLowerCase();
      list = list.filter(
        (r) =>
          r.number.toLowerCase().includes(q) ||
          r.productCode.toLowerCase().includes(q) ||
          r.productDescription.toLowerCase().includes(q) ||
          (r.batchNumber && r.batchNumber.toLowerCase().includes(q)),
      );
    }
    return list;
  }, [serials.data, statusFilter, search]);

  const columns: readonly GridColumn<SerialNumberView>[] = [
    {
      key: 'number',
      header: t('stock.serialNumber'),
      value: (row) => row.number,
      render: (row) => (
        <span className="font-mono font-semibold text-ink">{row.number}</span>
      ),
    },
    {
      key: 'product',
      header: t('stock.product'),
      value: (row) => `${row.productCode} — ${row.productDescription}`,
      render: (row) => (
        <div>
          <span className="font-mono text-xs text-ink-muted">{row.productCode}</span>
          <p className="font-medium text-ink">{row.productDescription}</p>
        </div>
      ),
    },
    {
      key: 'batch',
      header: t('stock.batch'),
      value: (row) => row.batchNumber ?? '—',
      render: (row) => (
        <span className="font-mono text-xs">{row.batchNumber ?? '—'}</span>
      ),
    },
    {
      key: 'warehouse',
      header: t('stock.warehouse'),
      value: (row) => row.warehouseName ?? '—',
    },
    {
      key: 'status',
      header: t('sales.status'),
      value: (row) => String(row.status),
      render: (row) => {
        const tone =
          row.status === SerialStatus.inStock
            ? 'success'
            : row.status === SerialStatus.issued
              ? 'neutral'
              : 'warn';
        const label =
          row.status === SerialStatus.inStock
            ? t('stock.statusInStock')
            : row.status === SerialStatus.issued
              ? t('stock.statusIssued')
              : t('stock.statusReturned');
        return <StatusBadge tone={tone} label={label} />;
      },
    },
    {
      key: 'cost',
      header: t('stock.averageCost'),
      value: (row) => row.unitCost,
      numeric: true,
      render: (row) => moneyAlways(row.unitCost),
    },
    {
      key: 'warranty',
      header: t('stock.warrantyUntil'),
      value: (row) => row.warrantyUntil ?? '—',
      render: (row) => (
        <div className="flex items-center gap-1.5">
          <span>{row.warrantyUntil ?? '—'}</span>
          {row.warrantyUntil && (
            <span
              className={`rounded px-1.5 py-0.5 text-[10px] font-semibold ${
                row.isUnderWarranty
                  ? 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300'
                  : 'bg-rose-500/15 text-rose-700 dark:text-rose-300'
              }`}
            >
              {row.isUnderWarranty
                ? t('stock.underWarranty')
                : t('stock.warrantyExpired')}
            </span>
          )}
        </div>
      ),
    },
    {
      key: 'receivedOn',
      header: t('reports.from'),
      value: (row) => row.receivedOn ?? '—',
    },
  ];

  const controls = (
    <div className="toolbar flex flex-wrap items-end gap-3">
      <div className="min-w-44">
        <span className="field-label">{t('sales.search')}</span>
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Serial number or product…"
          className="field-input-sm"
        />
      </div>

      <WarehousePicker value={warehouseId} onChange={setWarehouseId} />

      <label className="field">
        <span className="field-label">{t('stock.product')}</span>
        <SearchSelect
          value={productId}
          onChange={setProductId}
          clearable
          size="sm"
          label={t('stock.product')}
          placeholder="All products"
          options={(products.data ?? [])
            .filter((p) => p.tracksSerialNumbers)
            .map((p) => ({ value: p.id, label: `${p.code} — ${p.description}` }))}
        />
      </label>

      <label className="field">
        <span className="field-label">{t('sales.status')}</span>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value ? Number(e.target.value) : '')}
          className="field-input-sm"
        >
          <option value="">{t('sales.allStatuses')}</option>
          <option value={SerialStatus.inStock}>{t('stock.statusInStock')}</option>
          <option value={SerialStatus.issued}>{t('stock.statusIssued')}</option>
          <option value={SerialStatus.returnedFromCustomer}>
            {t('stock.statusReturned')}
          </option>
        </select>
      </label>
    </div>
  );

  return (
    <ReportFrame title={t('stock.serialsRegister')} controls={controls} query={serials}>
      {() => (
        <DataGrid
          gridKey="serials-stock"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.serialNumberId}
          emptyMessage="No serial numbers found matching filters."
        />
      )}
    </ReportFrame>
  );
}

/**
 * The detailed stock register: for each product, what it opened at, what was bought and
 * sold, everything else that came in or went out, and what it closed at. A row opens the
 * product's movements over the same period.
 */
export function StockRegisterPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();

  const today = new Date().toISOString().slice(0, 10);

  const [from, setFrom] = useState(`${today.slice(0, 7)}-01`);
  const [to, setTo] = useState(today);
  const [warehouseId, setWarehouseId] = useState('');
  const [categoryId, setCategoryId] = useState('');

  const query = useQuery<StockRegisterReport, ApiError>({
    queryKey: ['stock-register', from, to, warehouseId, categoryId],
    queryFn: () => fetchStockRegister(from, to, warehouseId, categoryId),
  });

  const quantity = (
    key: string,
    header: string,
    pick: (row: StockRegisterRow) => number,
    strong = false,
  ): GridColumn<StockRegisterRow> => ({
    key,
    header,
    value: pick,
    numeric: true,
    render: (row) => (
      <span className={clsx(strong && 'font-semibold text-ink')}>{trim(pick(row))}</span>
    ),
  });

  const columns: readonly GridColumn<StockRegisterRow>[] = [
    { key: 'code', header: t('masters.code'), value: (row) => row.productCode },
    {
      key: 'description',
      header: t('products.description'),
      value: (row) => row.productDescription,
      render: (row) => (
        <button
          type="button"
          onClick={() =>
            void navigate(
              `/inventory/product-history?${new URLSearchParams({ productId: row.productId, from, to }).toString()}`,
            )
          }
          className="text-start font-medium text-brand-700 hover:underline dark:text-brand-300"
        >
          {row.productDescription}
        </button>
      ),
    },
    {
      key: 'category',
      header: t('products.category'),
      value: (row) => row.categoryName,
      hiddenByDefault: true,
    },
    { key: 'unit', header: t('stock.unit'), value: (row) => row.stockUnitCode },
    quantity('opening', t('stock.opening'), (row) => row.opening),
    quantity('purchase', t('stockRegister.purchase'), (row) => row.purchase),
    quantity('sales', t('stockRegister.sales'), (row) => row.sales),
    quantity('otherIn', t('stockRegister.otherIn'), (row) => row.otherIn),
    quantity('otherOut', t('stockRegister.otherOut'), (row) => row.otherOut),
    quantity('closing', t('stock.closing'), (row) => row.closing, true),
  ];

  const controls = (
    <div className="toolbar">
      <DateBox label={t('reports.from')} value={from} onChange={setFrom} />
      <DateBox label={t('reports.to')} value={to} onChange={setTo} />
      <WarehousePicker value={warehouseId} onChange={setWarehouseId} />
      <CategoryPicker value={categoryId} onChange={setCategoryId} />
    </div>
  );

  return (
    <ReportFrame title={t('nav.stockRegister')} controls={controls} query={query}>
      {(report) => (
        <DataGrid
          gridKey="stock-register"
          rows={report.rows}
          columns={columns}
          rowKey={(row) => row.productId}
          emptyMessage={t('stock.nothingMoved')}
        />
      )}
    </ReportFrame>
  );
}

function WarehousePicker({
  value,
  onChange,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });

  return (
    <label className="field">
      <span className="field-label">{t('stock.warehouse')}</span>
      <SearchSelect
        value={value}
        onChange={onChange}
        clearable
        size="sm"
        label={t('stock.warehouse')}
        placeholder={t('stock.allWarehouses')}
        options={(warehouses.data ?? []).map((warehouse) => ({
          value: warehouse.id,
          label: warehouse.name,
        }))}
      />
    </label>
  );
}

function CategoryPicker({
  value,
  onChange,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const categories = useQuery<readonly CategorySummary[], ApiError>({
    queryKey: ['categories', false],
    queryFn: () => listMaster<CategorySummary>('categories', false),
  });

  return (
    <label className="field">
      <span className="field-label">{t('products.category')}</span>
      <SearchSelect
        value={value}
        onChange={onChange}
        clearable
        size="sm"
        label={t('products.category')}
        placeholder={t('products.allCategories')}
        options={(categories.data ?? []).map((category) => ({
          value: category.id,
          label: category.name,
        }))}
      />
    </label>
  );
}

function DateBox({
  label,
  value,
  onChange,
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      <input
        type="date"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="field-input-sm"
      />
    </label>
  );
}

function Figure({
  label,
  value,
}: {
  readonly label: string;
  readonly value: string;
}): React.JSX.Element {
  return (
    <span className="rounded-lg border border-line bg-surface-2 px-3 py-1.5">
      <span className="text-ink-muted">{label}:</span>{' '}
      <span className="font-medium">{value}</span>
    </span>
  );
}
