import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { ReportFrame } from '@/components/ReportFrame';
import { trim, typeKey } from '@/pages/StockOperationsPage';
import type { ApiError } from '@/lib/api';
import {
  listMaster,
  type CategorySummary,
  type WarehouseSummary,
} from '@/lib/inventory';
import { listProducts, type ProductSummary } from '@/lib/products';
import {
  fetchItemMovement,
  fetchStockLedger,
  fetchStockValuation,
  type ItemMovementRow,
  type StockLedgerReport,
  type StockValuationReport,
  type StockValuationRow,
} from '@/lib/stock';

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
      render: (row) => row.value.toFixed(2),
    },
  ];

  const controls = (
    <div className="flex flex-wrap items-end gap-3">
      <WarehousePicker value={warehouseId} onChange={setWarehouseId} />
      <CategoryPicker value={categoryId} onChange={setCategoryId} />

      <label className="flex items-center gap-2 pb-1 text-sm">
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
          <p className="inline-block rounded-lg bg-slate-100 px-3 py-1.5 text-sm font-medium dark:bg-slate-800">
            {t('stock.totalValue', {
              value: report.totalValue.toLocaleString(undefined, {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2,
              }),
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
 * The stock ledger of one product.
 *
 * One product at a time, deliberately: the running balance column only means anything
 * within one product, and a ledger that mixed several would show a column of figures
 * that appear to jump about.
 */
export function StockLedgerPage(): React.JSX.Element {
  const { t } = useTranslation();

  const today = new Date().toISOString().slice(0, 10);

  const [productId, setProductId] = useState('');
  const [from, setFrom] = useState(`${today.slice(0, 4)}-01-01`);
  const [to, setTo] = useState(today);
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
    <div className="flex flex-wrap items-end gap-3">
      <label className="flex flex-col gap-1 text-sm">
        <span className="text-slate-600 dark:text-slate-400">{t('stock.product')}</span>
        <select
          value={productId}
          onChange={(event) => setProductId(event.target.value)}
          className="w-72 rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
        >
          <option value="">{t('stock.choose')}</option>
          {(products.data ?? []).map((product) => (
            <option key={product.id} value={product.id}>
              {product.code} — {product.description}
            </option>
          ))}
        </select>
      </label>

      <DateBox label={t('reports.from')} value={from} onChange={setFrom} />
      <DateBox label={t('reports.to')} value={to} onChange={setTo} />
      <WarehousePicker value={warehouseId} onChange={setWarehouseId} />
    </div>
  );

  if (!productId) {
    return (
      <section className="space-y-4">
        <header className="flex flex-wrap items-end justify-between gap-4">
          <h1 className="text-xl font-semibold">{t('nav.stockLedger')}</h1>
          {controls}
        </header>
        <p className="text-sm text-slate-500">{t('stock.chooseProduct')}</p>
      </section>
    );
  }

  return (
    <ReportFrame title={t('nav.stockLedger')} controls={controls} query={query}>
      {(report) => (
        <div className="space-y-4">
          <div className="flex flex-wrap gap-3 text-sm">
            <Figure label={t('stock.opening')} value={`${trim(report.openingQuantity)} ${report.stockUnitCode}`} />
            <Figure label={t('stock.totalIn')} value={trim(report.totalIn)} />
            <Figure label={t('stock.totalOut')} value={trim(report.totalOut)} />
            <Figure
              label={t('stock.closing')}
              value={`${trim(report.closingQuantity)} ${report.stockUnitCode}`}
            />
          </div>

          <div className="overflow-auto rounded-xl border border-slate-200 dark:border-slate-800">
            <table className="w-full border-collapse text-sm">
              <thead className="bg-slate-100 dark:bg-slate-800">
                <tr>
                  <th className="px-3 py-2 text-start font-semibold">{t('stock.date')}</th>
                  <th className="px-3 py-2 text-start font-semibold">{t('stock.number')}</th>
                  <th className="px-3 py-2 text-start font-semibold">{t('stock.type')}</th>
                  <th className="px-3 py-2 text-start font-semibold">
                    {t('stock.warehouse')}
                  </th>
                  <th className="px-3 py-2 text-end font-semibold">{t('stock.in')}</th>
                  <th className="px-3 py-2 text-end font-semibold">{t('stock.out')}</th>
                  <th className="px-3 py-2 text-end font-semibold">{t('stock.unitCost')}</th>
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
                    <td colSpan={10} className="px-3 py-6 text-center text-sm text-slate-500">
                      {t('stock.noMovementsInRange')}
                    </td>
                  </tr>
                ) : (
                  report.rows.map((row, index) => (
                    <tr
                      key={`${row.documentId}-${index}`}
                      className="border-t border-slate-100 dark:border-slate-900"
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
                      <td className="cell-numeric">{row.value.toFixed(2)}</td>
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
      render: (row) => row.valueIn.toFixed(2),
    },
    {
      key: 'valueOut',
      header: t('stock.valueOut'),
      value: (row) => row.valueOut,
      numeric: true,
      render: (row) => row.valueOut.toFixed(2),
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
    <div className="flex flex-wrap items-end gap-3">
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
    <label className="flex flex-col gap-1 text-sm">
      <span className="text-slate-600 dark:text-slate-400">{t('stock.warehouse')}</span>
      <select
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
      >
        <option value="">{t('stock.allWarehouses')}</option>
        {(warehouses.data ?? []).map((warehouse) => (
          <option key={warehouse.id} value={warehouse.id}>
            {warehouse.name}
          </option>
        ))}
      </select>
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
    <label className="flex flex-col gap-1 text-sm">
      <span className="text-slate-600 dark:text-slate-400">{t('products.category')}</span>
      <select
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
      >
        <option value="">{t('products.allCategories')}</option>
        {(categories.data ?? []).map((category) => (
          <option key={category.id} value={category.id}>
            {category.name}
          </option>
        ))}
      </select>
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
    <label className="flex flex-col gap-1 text-sm">
      <span className="text-slate-600 dark:text-slate-400">{label}</span>
      <input
        type="date"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
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
    <span className="rounded-lg bg-slate-100 px-3 py-1.5 dark:bg-slate-800">
      <span className="text-slate-500">{label}:</span>{' '}
      <span className="font-medium">{value}</span>
    </span>
  );
}
