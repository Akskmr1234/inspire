import { useState } from 'react';
export { PageTabs } from '@/components/PageTabs';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { ReportFrame } from '@/components/ReportFrame';
import { SearchSelect } from '@/components/SearchSelect';
import type { ApiError } from '@/lib/api';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import { moneyAlways } from '@/lib/money';
import {
  fetchStockHistory,
  StockDocumentStatus,
  StockDocumentType,
  type StockHistoryRow,
} from '@/lib/stock';

/**
 * The history of stock corrections, line by line: what the system held before, what
 * it held after, and the difference, with the reason given.
 *
 * Read from the stock ledger rather than recomputed, so the figures are the ones the
 * moment the line posted - not today's balance, which has moved on since.
 */
export function StockHistoryTable({
  kind,
  title,
}: {
  readonly kind: number;
  readonly title: string;
}): React.JSX.Element {
  const { t } = useTranslation();

  const today = new Date().toISOString().slice(0, 10);
  const [from, setFrom] = useState(`${today.slice(0, 7)}-01`);
  const [to, setTo] = useState(today);
  const [warehouseId, setWarehouseId] = useState('');

  const query = useQuery<readonly StockHistoryRow[], ApiError>({
    queryKey: ['stock-history', kind, from, to, warehouseId],
    queryFn: () => fetchStockHistory(kind, from, to, warehouseId),
  });

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });

  const quantity = (value: number): string =>
    value.toLocaleString(undefined, { maximumFractionDigits: 6 });

  const columns: readonly GridColumn<StockHistoryRow>[] = [
    { key: 'date', header: t('stock.date'), value: (row) => row.date },
    {
      key: 'number',
      header: t('stock.number'),
      value: (row) => row.number,
      render: (row) => (
        <span
          className={clsx(
            row.status === StockDocumentStatus.cancelled &&
              'text-ink-subtle line-through',
          )}
        >
          {row.number}
        </span>
      ),
    },
    {
      key: 'type',
      header: t('stock.type'),
      value: (row) =>
        row.type === StockDocumentType.physicalVerification
          ? t('stock.typeVerification')
          : row.type === StockDocumentType.damagedStock
            ? t('stock.typeDamaged')
            : t('stock.typeAdjustment'),
      hiddenByDefault: true,
    },
    { key: 'warehouse', header: t('stock.warehouse'), value: (row) => row.warehouseName },
    { key: 'code', header: t('history.code'), value: (row) => row.productCode },
    { key: 'product', header: t('stock.product'), value: (row) => row.productName },
    {
      key: 'batch',
      header: t('stock.batch'),
      value: (row) => row.batchNumber ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'previous',
      header: t('history.previous'),
      value: (row) => row.previousQuantity,
      numeric: true,
      render: (row) => quantity(row.previousQuantity),
    },
    {
      key: 'actual',
      header: t('history.actual'),
      value: (row) => row.actualQuantity,
      numeric: true,
      render: (row) => quantity(row.actualQuantity),
    },
    {
      key: 'adjusted',
      header: t('history.adjusted'),
      value: (row) => row.adjustedQuantity,
      numeric: true,
      render: (row) => (
        <span
          className={clsx(
            row.adjustedQuantity > 0 && 'text-emerald-700 dark:text-emerald-300',
            row.adjustedQuantity < 0 && 'text-red-700 dark:text-red-300',
          )}
        >
          {row.adjustedQuantity > 0 ? '+' : ''}
          {quantity(row.adjustedQuantity)} {row.stockUnitCode}
        </span>
      ),
    },
    {
      key: 'value',
      header: t('stock.value'),
      value: (row) => row.value,
      numeric: true,
      render: (row) => moneyAlways(row.value),
      requiredPermission: 'inventory:report:view',
    },
    { key: 'reason', header: t('history.reason'), value: (row) => row.reason ?? '' },
    {
      key: 'status',
      header: t('stock.status'),
      value: (row) =>
        row.status === StockDocumentStatus.cancelled
          ? t('stock.statusCancelled')
          : t('stock.statusPosted'),
      hiddenByDefault: true,
    },
  ];

  const controls = (
    <div className="toolbar">
      <label className="field">
        <span className="field-label">{t('reports.from')}</span>
        <input
          type="date"
          value={from}
          onChange={(event) => setFrom(event.target.value)}
          className="field-input-sm"
        />
      </label>
      <label className="field">
        <span className="field-label">{t('reports.to')}</span>
        <input
          type="date"
          value={to}
          onChange={(event) => setTo(event.target.value)}
          className="field-input-sm"
        />
      </label>
      <label className="field">
        <span className="field-label">{t('stock.warehouse')}</span>
        <SearchSelect
          value={warehouseId}
          onChange={setWarehouseId}
          clearable
          size="sm"
          label={t('stock.warehouse')}
          placeholder={t('stock.allWarehouses')}
          options={(warehouses.data ?? [])
            .filter((warehouse) => !warehouse.isTransit)
            .map((warehouse) => ({ value: warehouse.id, label: warehouse.name }))}
        />
      </label>
    </div>
  );

  return (
    <ReportFrame title={title} controls={controls} query={query}>
      {(rows) => (
        <DataGrid
          gridKey={`stock-history-${kind}`}
          rows={rows}
          columns={columns}
          rowKey={(row) => `${row.documentId}-${row.productId}-${row.batchNumber ?? ''}`}
          emptyMessage={t('history.none')}
        />
      )}
    </ReportFrame>
  );
}
