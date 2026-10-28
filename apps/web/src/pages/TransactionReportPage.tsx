import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import clsx from 'clsx';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { ReportFrame } from '@/components/ReportFrame';
import { SearchSelect, type SelectOption } from '@/components/SearchSelect';
import type { ApiError } from '@/lib/api';
import { listCustomers, type CustomerSummary } from '@/lib/customers';
import {
  listMaster,
  type BrandSummary,
  type CategorySummary,
  type WarehouseSummary,
} from '@/lib/inventory';
import { LedgerKind, listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { moneyAlways } from '@/lib/money';
import { TerritoryKind, useTerritories } from '@/lib/places';
import { listProducts, type ProductSummary } from '@/lib/products';
import { listSuppliers, type SupplierSummary } from '@/lib/suppliers';
import {
  fetchTransactionReport,
  NO_FILTERS,
  type TransactionDocument,
  type TransactionFilters,
  type TransactionGrouping,
  type TransactionReport,
  type TransactionReportRow,
} from '@/lib/transactionReports';
import { useGodownsOn, useTaxOn } from '@/stores/settings';

/** Sales over a period, by month, day, bill, item and more, with every filter the counter uses. */
export function SalesReportPage(): React.JSX.Element {
  return <TransactionReportScreen document="Sales" profit={false} />;
}

/** Purchases over a period, the same way. */
export function PurchaseReportPage(): React.JSX.Element {
  return <TransactionReportScreen document="Purchase" profit={false} />;
}

/** What sales made over their cost, by bill, item, day, month, brand, category or salesman. */
export function ProfitReportPage(): React.JSX.Element {
  return <TransactionReportScreen document="Sales" profit />;
}

const SALES_GROUPS: readonly TransactionGrouping[] = [
  'Month',
  'Day',
  'Bill',
  'Item',
  'Brand',
  'Category',
  'Salesman',
  'Party',
];

const PURCHASE_GROUPS: readonly TransactionGrouping[] = [
  'Month',
  'Day',
  'Bill',
  'Item',
  'Brand',
  'Category',
  'Party',
];

const PROFIT_GROUPS: readonly TransactionGrouping[] = [
  'Bill',
  'Item',
  'Day',
  'Month',
  'Brand',
  'Category',
  'Salesman',
  'Party',
];

function monthStart(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`;
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function TransactionReportScreen({
  document,
  profit,
}: {
  readonly document: TransactionDocument;
  readonly profit: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const taxOn = useTaxOn();
  const sales = document === 'Sales';

  const groups = profit ? PROFIT_GROUPS : sales ? SALES_GROUPS : PURCHASE_GROUPS;
  const [groupBy, setGroupBy] = useState<TransactionGrouping>(groups[0] ?? 'Month');
  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(today());
  const [filters, setFilters] = useState<TransactionFilters>(NO_FILTERS);
  const [showFilters, setShowFilters] = useState(false);

  const query = useQuery<TransactionReport, ApiError>({
    queryKey: ['transaction-report', document, from, to, groupBy, filters],
    queryFn: () => fetchTransactionReport(document, from, to, groupBy, filters),
  });

  const set = (patch: Partial<TransactionFilters>): void =>
    setFilters((current) => ({ ...current, ...patch }));
  const filtered = Object.values(filters).some((value) => value.trim() !== '');

  const columns = useMemo((): readonly GridColumn<TransactionReportRow>[] => {
    const money = (
      key: string,
      header: string,
      pick: (row: TransactionReportRow) => number | null,
      strong = false,
    ): GridColumn<TransactionReportRow> => ({
      key,
      header,
      numeric: true,
      value: (row) => pick(row) ?? 0,
      render: (row) => {
        const figure = pick(row);
        return figure === null ? (
          ''
        ) : (
          <span
            className={clsx(
              strong && 'font-semibold text-ink',
              key === 'profit' && figure < 0 && 'text-red-600 dark:text-red-400',
            )}
          >
            {moneyAlways(figure)}
          </span>
        );
      },
    });

    const first: GridColumn<TransactionReportRow> = {
      key: 'label',
      header: t(`txReports.group.${groupBy}`),
      value: (row) => row.label,
      wide: groupBy === 'Item',
      render: (row) =>
        groupBy === 'Item' ? (
          <Link
            to={`/inventory/product-history?${new URLSearchParams({ productId: row.key, from, to }).toString()}`}
            className="font-medium text-brand-700 hover:underline dark:text-brand-300"
          >
            {row.label}
          </Link>
        ) : (
          <span className="font-medium">{row.label}</span>
        ),
    };

    const detail: readonly GridColumn<TransactionReportRow>[] =
      groupBy === 'Bill' || groupBy === 'Item'
        ? [
            {
              key: 'detail',
              header: groupBy === 'Bill' ? t('txReports.dateParty') : t('masters.code'),
              value: (row) => row.detail ?? '',
            },
          ]
        : [];

    const count: readonly GridColumn<TransactionReportRow>[] =
      groupBy === 'Bill'
        ? []
        : [
            {
              key: 'bills',
              header: t('txReports.bills'),
              value: (row) => row.bills,
              numeric: true,
            },
          ];

    const quantity: GridColumn<TransactionReportRow> = {
      key: 'quantity',
      header: t('txReports.quantity'),
      value: (row) => row.quantity,
      numeric: true,
    };

    if (profit) {
      return [
        first,
        ...detail,
        ...count,
        quantity,
        money('amount', t('txReports.sale'), (row) => row.amount),
        money('cost', t('txReports.cost'), (row) => row.cost),
        money('profit', t('txReports.profit'), (row) => row.profit, true),
        {
          key: 'margin',
          header: t('txReports.margin'),
          numeric: true,
          value: (row) => row.marginPercent ?? 0,
          render: (row) =>
            row.marginPercent === null ? '' : `${row.marginPercent.toFixed(2)}%`,
        },
      ];
    }

    return [
      first,
      ...detail,
      ...count,
      quantity,
      money(
        'amount',
        sales ? t('txReports.sale') : t('txReports.purchase'),
        (row) => row.amount,
      ),
      ...(taxOn ? [money('tax', t('txReports.tax'), (row) => row.tax)] : []),
      money('others', t('txReports.others'), (row) => row.others),
      money('total', t('txReports.total'), (row) => row.total, true),
    ];
  }, [t, groupBy, profit, sales, taxOn, from, to]);

  const controls = (
    <div className="space-y-2">
      <div className="toolbar">
        <DateBox label={t('reports.from')} value={from} onChange={setFrom} />
        <DateBox label={t('reports.to')} value={to} onChange={setTo} />
        <div
          role="radiogroup"
          aria-label={t('txReports.view')}
          className="flex flex-wrap gap-1 rounded-lg border border-line bg-surface-2 p-1"
        >
          {groups.map((group) => (
            <button
              key={group}
              type="button"
              role="radio"
              aria-checked={groupBy === group}
              onClick={() => setGroupBy(group)}
              className={clsx(
                'rounded-md px-2.5 py-1 text-xs font-medium transition',
                groupBy === group
                  ? 'bg-surface text-ink shadow-sm ring-1 ring-line'
                  : 'text-ink-muted hover:bg-surface/50 hover:text-ink',
              )}
            >
              {group === 'Party'
                ? sales
                  ? t('txReports.group.Customer')
                  : t('txReports.group.Supplier')
                : t(`txReports.group.${group}`)}
            </button>
          ))}
        </div>
        <button
          type="button"
          aria-expanded={showFilters}
          onClick={() => setShowFilters((open) => !open)}
          className={clsx('btn-secondary btn-sm', filtered && 'ring-2 ring-brand-500/40')}
        >
          {t('txReports.filters')}
        </button>
        {filtered && (
          <button
            type="button"
            onClick={() => setFilters(NO_FILTERS)}
            className="btn-secondary btn-sm"
          >
            {t('txReports.clearFilters')}
          </button>
        )}
      </div>
      {showFilters && <Filters sales={sales} filters={filters} set={set} />}
    </div>
  );

  const title = profit
    ? t('nav.profitReport')
    : sales
      ? t('nav.salesReport')
      : t('nav.purchaseReport');

  return (
    <ReportFrame title={title} controls={controls} query={query}>
      {(report) => (
        <div className="space-y-3">
          <div className="flex flex-wrap gap-3 text-sm">
            <Figure label={t('txReports.bills')} value={String(report.totals.bills)} />
            <Figure
              label={profit || sales ? t('txReports.sale') : t('txReports.purchase')}
              value={moneyAlways(report.totals.amount)}
            />
            {profit ? (
              <>
                <Figure
                  label={t('txReports.cost')}
                  value={moneyAlways(report.totals.cost ?? 0)}
                />
                <Figure
                  label={t('txReports.profit')}
                  value={`${moneyAlways(report.totals.profit ?? 0)}${
                    report.totals.marginPercent === null
                      ? ''
                      : ` (${report.totals.marginPercent.toFixed(2)}%)`
                  }`}
                  strong
                />
              </>
            ) : (
              <>
                {taxOn && (
                  <Figure
                    label={t('txReports.tax')}
                    value={moneyAlways(report.totals.tax)}
                  />
                )}
                <Figure
                  label={t('txReports.others')}
                  value={moneyAlways(report.totals.others)}
                />
                <Figure
                  label={t('txReports.total')}
                  value={`${moneyAlways(report.totals.total)} ${report.currency}`}
                  strong
                />
              </>
            )}
          </div>
          <DataGrid
            gridKey={`tx-report-${profit ? 'profit' : document}-${groupBy}`}
            rows={report.rows}
            columns={columns}
            rowKey={(row) => row.key}
            emptyMessage={t('txReports.none')}
          />
        </div>
      )}
    </ReportFrame>
  );
}

/** The filters under the toolbar, as the counter's own report lays them out. */
function Filters({
  sales,
  filters,
  set,
}: {
  readonly sales: boolean;
  readonly filters: TransactionFilters;
  readonly set: (patch: Partial<TransactionFilters>) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const taxOn = useTaxOn();
  const godownsOn = useGodownsOn();

  const customers = useQuery<readonly CustomerSummary[], ApiError>({
    queryKey: ['customers', 'picker'],
    queryFn: () => listCustomers('', true),
    enabled: sales,
  });
  const suppliers = useQuery<readonly SupplierSummary[], ApiError>({
    queryKey: ['suppliers', 'picker'],
    queryFn: () => listSuppliers('', true),
    enabled: !sales,
  });
  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => listLedgers(true),
    staleTime: 5 * 60 * 1000,
    enabled: sales,
  });
  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', 'picker'],
    queryFn: () => listProducts('', '', false),
  });
  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });
  const brands = useQuery<readonly BrandSummary[], ApiError>({
    queryKey: ['brands', false],
    queryFn: () => listMaster<BrandSummary>('brands', false),
  });
  const categories = useQuery<readonly CategorySummary[], ApiError>({
    queryKey: ['categories', false],
    queryFn: () => listMaster<CategorySummary>('categories', false),
  });
  const areas = useTerritories(TerritoryKind.area);
  const routes = useTerritories(TerritoryKind.route);

  const parties: readonly SelectOption[] = sales
    ? (customers.data ?? []).map((row) => ({
        value: row.customerId,
        label: row.name,
        detail: row.code,
      }))
    : (suppliers.data ?? []).map((row) => ({
        value: row.supplierId,
        label: row.name,
        detail: row.code,
      }));

  const pick = (
    label: string,
    key: keyof TransactionFilters,
    options: readonly SelectOption[],
  ): React.JSX.Element => (
    <label className="field min-w-0">
      <span className="field-label">{label}</span>
      <SearchSelect
        value={filters[key]}
        onChange={(value) => set({ [key]: value })}
        options={options}
        clearable
        size="sm"
        label={label}
        placeholder={t('txReports.all')}
      />
    </label>
  );

  return (
    <div className="grid grid-cols-1 gap-2 rounded-xl border border-line bg-surface-2/40 p-3 sm:grid-cols-2 lg:grid-cols-5">
      {pick(
        sales ? t('txReports.customer') : t('txReports.supplier'),
        'partyLedgerId',
        parties,
      )}
      {sales &&
        pick(
          t('txReports.salesman'),
          'salesmanLedgerId',
          (ledgers.data ?? [])
            .filter((ledger) => ledger.kind === LedgerKind.employee)
            .map((ledger) => ({ value: ledger.ledgerId, label: ledger.name })),
        )}
      {pick(
        t('txReports.product'),
        'productId',
        (products.data ?? []).map((product) => ({
          value: product.id,
          label: product.description,
          detail: product.code,
        })),
      )}
      {taxOn && (
        <label className="field min-w-0">
          <span className="field-label">{t('txReports.mode')}</span>
          <select
            value={filters.mode}
            onChange={(event) => set({ mode: event.target.value })}
            className="field-input-sm"
          >
            <option value="">{t('txReports.all')}</option>
            <option value="Tax">{t('serviceSales.tax')}</option>
            <option value="NonTax">{t('serviceSales.nonTax')}</option>
          </select>
        </label>
      )}
      {godownsOn &&
        pick(
          t('service.godown'),
          'warehouseId',
          (warehouses.data ?? []).map((row) => ({ value: row.id, label: row.name })),
        )}
      {pick(
        t('txReports.brand'),
        'brandId',
        (brands.data ?? []).map((row) => ({ value: row.id, label: row.name })),
      )}
      {pick(
        t('txReports.category'),
        'categoryId',
        (categories.data ?? []).map((row) => ({ value: row.id, label: row.name })),
      )}
      {pick(
        t('txReports.area'),
        'areaId',
        areas.map((row) => ({ value: row.id, label: row.name })),
      )}
      {pick(
        t('txReports.route'),
        'routeId',
        routes.map((row) => ({ value: row.id, label: row.name })),
      )}
      {taxOn && (
        <label className="field min-w-0">
          <span className="field-label">{t('txReports.taxPercent')}</span>
          <input
            type="number"
            min="0"
            step="any"
            inputMode="decimal"
            value={filters.taxPercentage}
            onChange={(event) => set({ taxPercentage: event.target.value })}
            className="field-input-sm text-end font-mono"
          />
        </label>
      )}
      <label className="field min-w-0">
        <span className="field-label">{t('txReports.serialNo')}</span>
        <input
          value={filters.serialNumber}
          onChange={(event) => set({ serialNumber: event.target.value })}
          className="field-input-sm font-mono"
        />
      </label>
    </div>
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
  strong = false,
}: {
  readonly label: string;
  readonly value: string;
  readonly strong?: boolean;
}): React.JSX.Element {
  return (
    <div className="rounded-lg border border-line bg-surface-2 px-3 py-1.5">
      <span className="block text-[10px] font-bold tracking-wide text-ink-muted uppercase">
        {label}
      </span>
      <span
        className={clsx(
          'font-mono tabular-nums',
          strong ? 'text-base font-bold text-brand-700 dark:text-brand-300' : 'text-ink',
        )}
      >
        {value}
      </span>
    </div>
  );
}
