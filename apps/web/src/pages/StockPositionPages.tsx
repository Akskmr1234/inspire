import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { ReportFrame } from '@/components/ReportFrame';
import {
  BranchFilter,
  BrandFilter,
  CategoryFilter,
  DateFilter,
  GodownFilter,
} from '@/components/StockFilters';
import { trim } from '@/pages/StockOperationsPage';
import type { ApiError } from '@/lib/api';
import { moneyAlways } from '@/lib/money';
import {
  AGE_BANDS,
  ageBand,
  consolidate,
  fetchStockAging,
  fetchStockRegister,
  type PositionDimension,
  type PositionLine,
  type StockPositionFilters,
  type StockPositionReport,
} from '@/lib/stockPosition';

const today = (): string => new Date().toISOString().slice(0, 10);

const isDimension = (key: string): key is PositionDimension =>
  [
    'code',
    'name',
    'batch',
    'expiry',
    'category',
    'brand',
    'godown',
    'branch',
    'received',
  ].includes(key);

/** The filters both reports share, and the columns that tell their rows apart. */
function usePositionFilters(): {
  readonly filters: StockPositionFilters;
  readonly controls: React.JSX.Element;
  readonly set: (patch: Partial<StockPositionFilters>) => void;
} {
  const { t } = useTranslation();
  const [filters, setFilters] = useState<StockPositionFilters>({
    asOn: today(),
    branchId: '',
    warehouseId: '',
    categoryId: '',
    brandId: '',
  });

  const set = (patch: Partial<StockPositionFilters>): void =>
    setFilters((current) => ({ ...current, ...patch }));

  const controls = (
    <>
      <DateFilter
        label={t('stockPosition.asOn')}
        value={filters.asOn}
        onChange={(asOn) => set({ asOn })}
      />
      <BranchFilter
        value={filters.branchId}
        // A godown of another branch would leave nothing to show.
        onChange={(branchId) => set({ branchId, warehouseId: '' })}
      />
      <GodownFilter
        branchId={filters.branchId}
        value={filters.warehouseId}
        onChange={(warehouseId) => set({ warehouseId })}
      />
      <CategoryFilter
        value={filters.categoryId}
        onChange={(categoryId) => set({ categoryId })}
      />
      <BrandFilter value={filters.brandId} onChange={(brandId) => set({ brandId })} />
    </>
  );

  return { filters, controls, set };
}

/** The columns that describe a line: shown, they keep lines apart; hidden, they add them up. */
function describingColumns(
  t: (key: string) => string,
  withReceived: boolean,
): readonly GridColumn<PositionLine>[] {
  const productLink = (line: PositionLine, text: string): React.ReactNode =>
    line.productId ? (
      <Link
        to={`/inventory/product-history?productId=${line.productId}`}
        className="font-medium text-brand-700 hover:underline dark:text-brand-300"
      >
        {text}
      </Link>
    ) : (
      text
    );

  return [
    {
      key: 'code',
      header: t('stockPosition.code'),
      value: (line) => line.first.productCode,
      render: (line) => productLink(line, line.first.productCode),
    },
    {
      key: 'name',
      header: t('stockPosition.name'),
      value: (line) => line.first.productName,
      render: (line) => productLink(line, line.first.productName),
      wide: true,
    },
    {
      key: 'batch',
      header: t('stockPosition.batch'),
      value: (line) => line.first.batchNumber,
    },
    ...(withReceived
      ? [
          {
            key: 'received',
            header: t('stockPosition.received'),
            value: (line: PositionLine) => line.first.receivedOn ?? '',
          },
        ]
      : []),
    {
      key: 'expiry',
      header: t('stockPosition.expiry'),
      value: (line) => line.first.expiresOn ?? '',
    },
    {
      key: 'category',
      header: t('stockPosition.category'),
      value: (line) => line.first.categoryName,
    },
    {
      key: 'brand',
      header: t('stockPosition.brand'),
      value: (line) => line.first.brandName,
    },
    {
      key: 'godown',
      header: t('stockPosition.godown'),
      value: (line) => line.first.warehouseName,
    },
    {
      key: 'branch',
      header: t('stockPosition.branch'),
      value: (line) => line.first.branchName,
    },
  ];
}

function figureColumns(t: (key: string) => string): readonly GridColumn<PositionLine>[] {
  return [
    {
      key: 'quantity',
      header: t('stockPosition.quantity'),
      value: (line) => line.quantity,
      numeric: true,
      render: (line) => `${trim(line.quantity)}${line.unit ? ` ${line.unit}` : ''}`,
    },
    {
      key: 'rate',
      header: t('stockPosition.rate'),
      value: (line) => line.rate,
      numeric: true,
      render: (line) => (line.quantity === 0 ? '—' : trim(line.rate)),
    },
    {
      key: 'value',
      header: t('stockPosition.value'),
      value: (line) => line.value,
      numeric: true,
      render: (line) => moneyAlways(line.value),
    },
  ];
}

/** The rows, added up to the columns on screen. */
function useLines(
  rows: StockPositionReport['rows'] | undefined,
  defaults: readonly PositionDimension[],
): {
  readonly lines: readonly PositionLine[];
  readonly onColumns: (keys: readonly string[]) => void;
} {
  const [showing, setShowing] = useState<readonly PositionDimension[]>(defaults);
  const lines = useMemo(() => consolidate(rows ?? [], showing), [rows, showing]);

  return { lines, onColumns: (keys) => setShowing(keys.filter(isDimension)) };
}

function Totals({
  quantity,
  value,
  currency,
  children,
}: {
  readonly quantity: number;
  readonly value: number;
  readonly currency: string;
  readonly children?: React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <div className="flex flex-wrap gap-2 text-sm">
      <Figure label={t('stockPosition.totalQuantity')} value={trim(quantity)} />
      <Figure
        label={t('stockPosition.totalValue')}
        value={`${moneyAlways(value)} ${currency}`}
      />
      {children}
    </div>
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
      <span className="font-medium tabular-nums">{value}</span>
    </span>
  );
}

/**
 * The stock register: what is on hand as at a date, by product, batch, godown and
 * branch, with its rate and value.
 *
 * The rows add themselves up to the columns showing. With Branch on, a product held in
 * two branches is two rows; with Branch off, it is one, carrying both quantities and
 * both values. Batch works the same way. The totals above the grid are the same
 * however the columns are arranged, because hiding a column only adds rows together.
 */
export function StockRegisterPage(): React.JSX.Element {
  const { t } = useTranslation();
  const { filters, controls } = usePositionFilters();

  const query = useQuery<StockPositionReport, ApiError>({
    queryKey: ['stock-register', filters],
    queryFn: () => fetchStockRegister(filters),
  });

  const { lines, onColumns } = useLines(query.data?.rows, [
    'code',
    'name',
    'batch',
    'expiry',
    'category',
    'brand',
    'godown',
    'branch',
  ]);

  const columns = [...describingColumns(t, false), ...figureColumns(t)];

  return (
    <ReportFrame
      title={t('nav.stockRegister')}
      controls={<div className="toolbar">{controls}</div>}
      query={query}
    >
      {(report) => (
        <div className="space-y-3">
          <Totals
            quantity={report.totalQuantity}
            value={report.totalValue}
            currency={report.currency}
          />
          <DataGrid
            gridKey="stock-register-position"
            rows={lines}
            columns={columns}
            rowKey={(line) => line.key}
            emptyMessage={t('stockPosition.nothing')}
            onVisibleColumnsChange={onColumns}
          />
        </div>
      )}
    </ReportFrame>
  );
}

/**
 * The stock aging report: how long what is on hand has been held.
 *
 * Each part of the stock is dated by the receipt it came in on - newest receipts first,
 * since what is still on the shelf is what came in last - and its age counted to the
 * as-on date. The bands spread each row across 0-30, 31-60, 61-90, 91-180 and over 180
 * days; with Purchase date hidden, a product's receipts add up into one row spread
 * across the bands, and its age is their average, weighted by quantity.
 */
export function StockAgingPage(): React.JSX.Element {
  const { t } = useTranslation();
  const { filters, controls, set } = usePositionFilters();
  const [band, setBand] = useState('');
  const [bandsIn, setBandsIn] = useState<'quantity' | 'value'>('quantity');

  const query = useQuery<StockPositionReport, ApiError>({
    queryKey: ['stock-aging', filters],
    queryFn: () => fetchStockAging(filters),
  });

  const rows = useMemo(
    () =>
      band === ''
        ? query.data?.rows
        : query.data?.rows.filter(
            (row) => row.ageDays !== null && String(ageBand(row.ageDays)) === band,
          ),
    [query.data, band],
  );

  const { lines, onColumns } = useLines(rows, [
    'code',
    'name',
    'batch',
    'received',
    'expiry',
    'category',
    'brand',
    'godown',
    'branch',
  ]);

  const columns: readonly GridColumn<PositionLine>[] = [
    ...describingColumns(t, true),
    ...figureColumns(t),
    {
      key: 'age',
      header: t('stockPosition.age'),
      value: (line) => line.ageDays,
      numeric: true,
    },
    ...AGE_BANDS.map((definition, index): GridColumn<PositionLine> => ({
      key: definition.key,
      header: definition.label,
      numeric: true,
      value: (line) =>
        (bandsIn === 'quantity' ? line.bandQuantity : line.bandValue)[index] ?? 0,
      render: (line) => {
        const figure =
          (bandsIn === 'quantity' ? line.bandQuantity : line.bandValue)[index] ?? 0;
        if (figure === 0) return '';
        return bandsIn === 'quantity' ? trim(figure) : moneyAlways(figure);
      },
    })),
  ];

  const shownQuantity = lines.reduce((sum, line) => sum + line.quantity, 0);
  const shownValue = lines.reduce((sum, line) => sum + line.value, 0);
  const bandTotals = AGE_BANDS.map((_, index) =>
    lines.reduce(
      (sum, line) =>
        sum + ((bandsIn === 'quantity' ? line.bandQuantity : line.bandValue)[index] ?? 0),
      0,
    ),
  );

  const agingControls = (
    <div className="toolbar">
      {controls}
      <DateFilter
        label={t('stockPosition.expiryFrom')}
        value={filters.expiresFrom ?? ''}
        onChange={(expiresFrom) => set({ expiresFrom })}
      />
      <DateFilter
        label={t('stockPosition.expiryTo')}
        value={filters.expiresTo ?? ''}
        onChange={(expiresTo) => set({ expiresTo })}
      />
      <label className="field">
        <span className="field-label">{t('stockPosition.ageBand')}</span>
        <select
          value={band}
          onChange={(event) => setBand(event.target.value)}
          className="field-input-sm"
        >
          <option value="">{t('stockPosition.anyAge')}</option>
          {AGE_BANDS.map((definition, index) => (
            <option key={definition.key} value={String(index)}>
              {definition.label}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        <span className="field-label">{t('stockPosition.bandsIn')}</span>
        <select
          value={bandsIn}
          onChange={(event) => setBandsIn(event.target.value as 'quantity' | 'value')}
          className="field-input-sm"
        >
          <option value="quantity">{t('stockPosition.quantity')}</option>
          <option value="value">{t('stockPosition.value')}</option>
        </select>
      </label>
    </div>
  );

  return (
    <ReportFrame title={t('nav.stockAging')} controls={agingControls} query={query}>
      {(report) => (
        <div className="space-y-3">
          <Totals quantity={shownQuantity} value={shownValue} currency={report.currency}>
            {AGE_BANDS.map((definition, index) => (
              <Figure
                key={definition.key}
                label={definition.label}
                value={
                  bandsIn === 'quantity'
                    ? trim(bandTotals[index] ?? 0)
                    : moneyAlways(bandTotals[index] ?? 0)
                }
              />
            ))}
          </Totals>
          <DataGrid
            gridKey="stock-aging"
            rows={lines}
            columns={columns}
            rowKey={(line) => line.key}
            emptyMessage={t('stockPosition.nothing')}
            onVisibleColumnsChange={onColumns}
          />
        </div>
      )}
    </ReportFrame>
  );
}
