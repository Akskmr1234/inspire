import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { Modal, ModalButton } from '@/components/Modal';
import { ReportFrame } from '@/components/ReportFrame';
import { CheckField, DateField, Field, SelectField, TextField } from '@/components/Form';
import { SearchSelect, type SelectOption } from '@/components/SearchSelect';
import { StatusBadge, type StatusTone } from '@/components/StatusBadge';
import {
  ChargesPanel,
  DocumentTotals,
  chargeTotal,
  productOption,
  stockByProduct,
  useDefaultCharges,
  productDetailColumns,
  ProductDetailCells,
  ProductDetailHeaders,
  useLineColumns,
  type DraftCharge,
  type LineColumn,
} from '@/components/DocumentLines';
import type { ApiError } from '@/lib/api';
import { listCustomers, type CustomerSummary } from '@/lib/customers';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import { listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { listProducts, type ProductSummary } from '@/lib/products';
import { listSuppliers, type SupplierSummary } from '@/lib/suppliers';
import { fetchStockValuation, type StockValuationReport } from '@/lib/stock';
import { PurchaseTaxMode } from '@/lib/purchase';
import type { PagedResult } from '@/lib/sales';
import {
  closePurchaseOrder,
  closeSalesOrder,
  confirmPurchaseOrder,
  confirmSalesOrder,
  convertPurchaseOrder,
  convertSalesOrder,
  createPurchaseOrder,
  createSalesOrder,
  getPurchaseOrder,
  getSalesOrder,
  listPurchaseOrders,
  listSalesOrders,
  OrderStatus,
  type OrderChargeDetail,
  type OrderLineDetail,
  type OrderLineInput,
} from '@/lib/orders';
import { collect, numeric, required, useValidation } from '@/lib/validation';
import { useSettings } from '@/stores/settings';
import { moneyAlways } from '@/lib/money';

/**
 * Purchase orders and sales orders.
 *
 * Neither screen existed. The seeded menu has carried both entries since the orders
 * module was written and the router had neither route, so choosing "Purchase orders"
 * fell through the catch-all and landed the reader on the trial balance — which is
 * what the review calls the route mistake. The API behind them is complete: draft,
 * confirm, convert to a purchase or an invoice, and close short with a reason.
 *
 * One component for both, told which module it belongs to. An order placed with a
 * supplier and an order taken from a customer are the same document pointed two
 * ways, and the parts that genuinely differ — who the party is, what a conversion
 * produces — are the four values in `SHAPES` below.
 */
type OrderKind = 'purchase' | 'sales';

/** A row of either list, read through the fields the screen actually shows. */
interface OrderRow {
  readonly id: string;
  readonly number: string;
  readonly date: string;
  readonly expectedOn: string | null;
  readonly partyCode: string;
  readonly partyName: string;
  readonly status: number;
  readonly referenceNumber: string | null;
  readonly lineCount: number;
  readonly outstandingLines: number;
  readonly taxable: number;
  readonly tax: number;
  readonly total: number;
}

/** An order in full, read the same way. */
interface OrderDocument {
  readonly number: string;
  readonly status: number;
  readonly date: string;
  readonly expectedOn: string | null;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly closureReason: string | null;
  readonly currency: string;
  readonly taxable: number;
  readonly tax: number;
  readonly chargeTotal: number;
  readonly total: number;
  readonly lines: readonly OrderLineDetail[];
  readonly charges: readonly OrderChargeDetail[];
}

/** A line being entered on an order. */
interface DraftLine {
  readonly key: string;
  productId: string;
  quantity: string;
  rate: string;
  taxPercentage: string;
  discount: string;
}

function emptyLine(taxRate: number): DraftLine {
  return {
    key: crypto.randomUUID(),
    productId: '',
    quantity: '1',
    rate: '',
    taxPercentage: String(taxRate),
    discount: '0',
  };
}

function lineNet(line: DraftLine): number {
  const net = Number(line.quantity) * Number(line.rate) - Number(line.discount || 0);

  return Number.isFinite(net) ? net : 0;
}

/** Orders placed with suppliers. */
export function PurchaseOrdersPage(): React.JSX.Element {
  return <OrdersPage kind="purchase" />;
}

/** Orders taken from customers. */
export function SalesOrdersPage(): React.JSX.Element {
  return <OrdersPage kind="sales" />;
}

function OrdersPage({ kind }: { readonly kind: OrderKind }): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const pageSize = useSettings((state) => state.pageSize);

  const today = new Date().toISOString().slice(0, 10);
  const monthStart = `${today.slice(0, 7)}-01`;

  const [from, setFrom] = useState(monthStart);
  const [to, setTo] = useState(today);
  const [statusFilter, setStatusFilter] = useState<number | ''>('');
  const [outstandingOnly, setOutstandingOnly] = useState(false);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const [entering, setEntering] = useState(false);
  const [viewing, setViewing] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const listKey = kind === 'purchase' ? 'purchase-orders' : 'sales-orders';

  const query = useQuery<PagedResult<OrderRow>, ApiError>({
    queryKey: [listKey, from, to, statusFilter, outstandingOnly, search, page, pageSize],
    queryFn: async () => {
      const filter = {
        from,
        to,
        status: statusFilter,
        search,
        outstandingOnly,
      };

      if (kind === 'purchase') {
        const result = await listPurchaseOrders(filter, page, pageSize);

        return {
          ...result,
          items: result.items.map((row) => ({
            id: row.purchaseOrderId,
            number: row.number,
            date: row.date,
            expectedOn: row.expectedOn,
            partyCode: row.supplierCode,
            partyName: row.supplierName,
            status: row.status,
            referenceNumber: row.referenceNumber,
            lineCount: row.lineCount,
            outstandingLines: row.outstandingLines,
            taxable: row.taxable,
            tax: row.tax,
            total: row.total,
          })),
        };
      }

      const result = await listSalesOrders(filter, page, pageSize);

      return {
        ...result,
        items: result.items.map((row) => ({
          id: row.salesOrderId,
          number: row.number,
          date: row.date,
          expectedOn: row.expectedOn,
          partyCode: row.customerCode,
          partyName: row.customerName,
          status: row.status,
          referenceNumber: row.referenceNumber,
          lineCount: row.lineCount,
          outstandingLines: row.outstandingLines,
          taxable: row.taxable,
          tax: row.tax,
          total: row.total,
        })),
      };
    },
  });

  const mutation = useMutation<string | null, ApiError, () => Promise<string | null>>({
    mutationFn: (action) => action(),
    onSuccess: async (message) => {
      setError(null);
      setNotice(message);
      window.setTimeout(() => setNotice(null), 6000);

      await queryClient.invalidateQueries({ queryKey: [listKey] });
      await queryClient.invalidateQueries({ queryKey: [`${listKey}-detail`] });
      // A conversion produces a draft document in the other module, so its list is
      // stale the moment this succeeds.
      await queryClient.invalidateQueries({
        queryKey: [kind === 'purchase' ? 'purchase-invoices' : 'sales-invoices'],
      });
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const run = (action: () => Promise<string | null>): void => mutation.mutate(action);

  const narrow =
    <T,>(set: (value: T) => void) =>
    (value: T): void => {
      set(value);
      setPage(1);
    };

  const columns: readonly GridColumn<OrderRow>[] = [
    {
      key: 'number',
      header: t('orders.number'),
      value: (row) => row.number,
      render: (row) => (
        <button type="button" onClick={() => setViewing(row.id)} className="cell-link">
          {row.number}
        </button>
      ),
    },
    { key: 'date', header: t('orders.date'), value: (row) => row.date },
    {
      key: 'expected',
      header: t('orders.expectedOn'),
      value: (row) => row.expectedOn ?? '',
    },
    {
      key: 'party',
      header: kind === 'purchase' ? t('orders.supplier') : t('orders.customer'),
      value: (row) => row.partyName,
    },
    {
      key: 'reference',
      header: t('orders.reference'),
      value: (row) => row.referenceNumber ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'lines',
      header: t('orders.lines'),
      value: (row) => row.lineCount,
      numeric: true,
    },
    {
      key: 'outstanding',
      header: t('orders.outstandingLines'),
      value: (row) => row.outstandingLines,
      numeric: true,
    },
    {
      key: 'taxable',
      header: t('orders.taxable'),
      value: (row) => row.taxable,
      numeric: true,
      render: (row) => moneyAlways(row.taxable),
    },
    {
      key: 'tax',
      header: t('orders.tax'),
      value: (row) => row.tax,
      numeric: true,
      render: (row) => moneyAlways(row.tax),
    },
    {
      key: 'total',
      header: t('orders.total'),
      value: (row) => row.total,
      numeric: true,
      render: (row) => moneyAlways(row.total),
    },
    {
      key: 'status',
      header: t('orders.status'),
      value: (row) => statusLabel(row.status, t),
      render: (row) => (
        <StatusBadge
          tone={statusTone(row.status)}
          label={statusLabel(row.status, t)}
          struck={row.status === OrderStatus.cancelled}
        />
      ),
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      // As on the purchase list: the card leads with the order number, which is
      // already the way in.
      hideOnNarrow: true,
      render: (row) => (
        <button
          type="button"
          onClick={() => setViewing(row.id)}
          className="row-action row-action-neutral"
        >
          {t('orders.open')}
        </button>
      ),
    },
  ];

  const controls = (
    <div className="filter-grid">
      <DateField
        label={t('orders.from')}
        value={from}
        onChange={narrow(setFrom)}
        size="sm"
      />
      <DateField label={t('orders.to')} value={to} onChange={narrow(setTo)} size="sm" />

      <SelectField<number | ''>
        label={t('orders.status')}
        value={statusFilter}
        onChange={narrow<number | ''>(setStatusFilter)}
        size="sm"
        options={[
          { value: '', label: t('orders.allStatuses') },
          { value: OrderStatus.draft, label: t('orders.draft') },
          { value: OrderStatus.confirmed, label: t('orders.confirmed') },
          { value: OrderStatus.completed, label: t('orders.completed') },
          { value: OrderStatus.cancelled, label: t('orders.cancelled') },
        ]}
      />

      <TextField
        label={t('orders.search')}
        type="search"
        value={search}
        onChange={(value) => narrow(setSearch)(value)}
        placeholder={t('orders.searchHint')}
        size="sm"
      />

      <CheckField
        label={t('orders.outstandingOnly')}
        checked={outstandingOnly}
        onChange={narrow(setOutstandingOnly)}
      />
    </div>
  );

  return (
    <>
      <ReportFrame
        title={kind === 'purchase' ? t('nav.purchaseOrders') : t('nav.salesOrders')}
        controls={controls}
        query={query}
      >
        {(result) => (
          <div className="space-y-3">
            {error && <p className="alert-error">{error}</p>}
            {notice && <p className="alert-success">{notice}</p>}

            <DataGrid
              gridKey={listKey}
              rows={result.items}
              columns={columns}
              rowKey={(row) => row.id}
              emptyMessage={t('orders.none')}
              actions={
                <GridAction label={t('orders.new')} onClick={() => setEntering(true)} />
              }
              paging={{
                page: result.page,
                pageSize: result.pageSize,
                totalCount: result.totalCount,
                totalPages: result.totalPages,
                onPageChange: setPage,
              }}
            />
          </div>
        )}
      </ReportFrame>

      {entering && (
        <OrderEntryDialog
          kind={kind}
          onClose={() => setEntering(false)}
          onSaved={(message) => {
            setEntering(false);
            setNotice(message);
            void queryClient.invalidateQueries({ queryKey: [listKey] });
          }}
          onError={setError}
        />
      )}

      {viewing && (
        <OrderDialog
          kind={kind}
          id={viewing}
          busy={mutation.isPending}
          onClose={() => setViewing(null)}
          onConfirm={(id) =>
            run(async () => {
              const confirmed =
                kind === 'purchase'
                  ? await confirmPurchaseOrder(id)
                  : await confirmSalesOrder(id);

              return t('orders.confirmedNotice', { number: confirmed.number });
            })
          }
          onConvert={(id) =>
            run(async () => {
              if (kind === 'purchase') {
                const created = await convertPurchaseOrder(id);

                return t('orders.convertedPurchase', { number: created.number });
              }

              const created = await convertSalesOrder(id);

              return t('orders.convertedSales', { number: created.number });
            })
          }
          onClose_={(id, reason) =>
            run(async () => {
              if (kind === 'purchase') {
                await closePurchaseOrder(id, reason);
              } else {
                await closeSalesOrder(id, reason);
              }

              setViewing(null);

              return t('orders.closedNotice');
            })
          }
        />
      )}
    </>
  );
}

/** What the entry dialog holds while an order is being keyed in. */
interface OrderHeaderDraft {
  date: string;
  expectedOn: string;
  partyId: string;
  warehouseId: string;
  referenceNumber: string;
  narration: string;
}

/** Enters an order as a draft. */
function OrderEntryDialog({
  kind,
  onClose,
  onSaved,
  onError,
}: {
  readonly kind: OrderKind;
  readonly onClose: () => void;
  readonly onSaved: (message: string) => void;
  readonly onError: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const { taxRates, defaultTaxRate, preferredWarehouseId } = useSettings();

  const [mode, setMode] = useState<number>(PurchaseTaxMode.tax);
  const [draft, setDraft] = useState<OrderHeaderDraft>({
    date: new Date().toISOString().slice(0, 10),
    expectedOn: '',
    partyId: '',
    warehouseId: '',
    referenceNumber: '',
    narration: '',
  });
  const [lines, setLines] = useState<readonly DraftLine[]>([emptyLine(defaultTaxRate)]);
  const [charges, setCharges] = useState<readonly DraftCharge[]>([]);
  const [busy, setBusy] = useState(false);

  const suppliers = useQuery<readonly SupplierSummary[], ApiError>({
    queryKey: ['suppliers', 'picker'],
    queryFn: () => listSuppliers('', true),
    enabled: kind === 'purchase',
  });

  const customers = useQuery<readonly CustomerSummary[], ApiError>({
    queryKey: ['customers', 'picker'],
    queryFn: () => listCustomers('', true),
    enabled: kind === 'sales',
  });

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });

  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', 'picker'],
    queryFn: () => listProducts('', '', false),
  });

  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => listLedgers(true),
    staleTime: 5 * 60 * 1000,
  });

  useDefaultCharges(
    kind === 'purchase' ? 'purchaseOrder' : 'salesOrder',
    ledgers.data ?? [],
    setCharges,
  );

  const valuation = useQuery<StockValuationReport, ApiError>({
    queryKey: ['stock-valuation', 'picker', draft.warehouseId],
    queryFn: () => fetchStockValuation(draft.warehouseId, '', true),
    staleTime: 60 * 1000,
  });

  const onHand = useMemo(() => stockByProduct(valuation.data?.rows), [valuation.data]);

  const productOptions = useMemo(
    () =>
      (products.data ?? []).map((product) =>
        productOption(product, onHand.get(product.id) ?? 0),
      ),
    [products.data, onHand],
  );

  const partyOptions: readonly SelectOption[] = useMemo(() => {
    if (kind === 'purchase') {
      return (suppliers.data ?? []).map((supplier) => ({
        value: supplier.supplierId,
        label: `${supplier.code} — ${supplier.name}`,
        ...(supplier.contact.mobileNumber
          ? { detail: supplier.contact.mobileNumber }
          : {}),
      }));
    }

    return (customers.data ?? []).map((customer) => ({
      value: customer.customerId,
      label: `${customer.code} — ${customer.name}`,
      ...(customer.contact.mobileNumber ? { detail: customer.contact.mobileNumber } : {}),
    }));
  }, [kind, suppliers.data, customers.data]);

  // The same default the purchase screen applies, and for the same reason: an order
  // in a firm with one store goes to that store, and asking every time is asking a
  // question with one answer.
  useEffect(() => {
    if (draft.warehouseId !== '' || !warehouses.data) {
      return;
    }

    const chosen =
      warehouses.data.find((warehouse) => warehouse.id === preferredWarehouseId) ??
      warehouses.data.find((warehouse) => warehouse.isDefault) ??
      (warehouses.data.length === 1 ? warehouses.data[0] : undefined);

    if (chosen) {
      setDraft((current) => ({ ...current, warehouseId: chosen.id }));
    }
  }, [warehouses.data, preferredWarehouseId, draft.warehouseId]);

  const lineColumns: readonly LineColumn[] = [
    { key: 'product', label: t('orders.product'), defaultOn: true, fixed: true },
    ...productDetailColumns(t),
    { key: 'quantity', label: t('orders.quantity'), defaultOn: true, fixed: true },
    { key: 'rate', label: t('orders.rate'), defaultOn: true, fixed: true },
    { key: 'discount', label: t('orders.discount'), defaultOn: true },
    { key: 'taxPercent', label: t('orders.taxPercent'), defaultOn: true },
    { key: 'net', label: t('orders.net'), defaultOn: true, fixed: true },
  ];

  const columns = useLineColumns(`orders-${kind}`, lineColumns);

  const totals = useMemo(() => {
    let gross = 0;
    let discount = 0;
    let tax = 0;

    for (const line of lines) {
      const net = lineNet(line);

      if (net > 0) {
        // Gross and discount are carried separately rather than recovered from the
        // net, because a bill has to show what the goods were priced at as well as
        // what is being charged for them — and on a line whose discount exceeds its
        // value, which is skipped below, subtracting back would invent a figure.
        gross += Number(line.quantity) * Number(line.rate);
        discount += Number(line.discount || 0);
        tax += (net * Number(line.taxPercentage || 0)) / 100;
      }
    }

    return {
      gross: Number.isFinite(gross) ? gross : 0,
      discount: Number.isFinite(discount) ? discount : 0,
      tax,
      // The taxable value, which is what the net comes to. Kept under its tax name
      // as well, because that is what the API and the tax return call it.
      taxable:
        (Number.isFinite(gross) ? gross : 0) - (Number.isFinite(discount) ? discount : 0),
    };
  }, [lines]);

  const { errors, submit } = useValidation<OrderHeaderDraft>((values) =>
    collect({
      date: required(values.date, t('orders.dateRequired')),
      partyId: required(
        values.partyId,
        kind === 'purchase' ? t('orders.supplierRequired') : t('orders.customerRequired'),
      ),
      warehouseId: required(values.warehouseId, t('orders.warehouseRequired')),
      expectedOn:
        values.expectedOn !== '' && values.expectedOn < values.date
          ? t('orders.expectedBeforeDate')
          : null,
      lines: lines.some((line) => line.productId && Number(line.quantity) > 0)
        ? null
        : t('orders.linesRequired'),
    }),
  );

  const lineErrors = useMemo(
    () =>
      lines.map((line) =>
        collect({
          quantity: line.productId
            ? numeric(line.quantity, t('orders.quantityInvalid'), { min: 0.000001 })
            : null,
          rate: line.productId
            ? numeric(line.rate, t('orders.rateInvalid'), { min: 0 })
            : null,
          taxPercentage: numeric(line.taxPercentage, t('orders.taxInvalid'), {
            min: 0,
            max: 100,
          }),
        }),
      ),
    [lines, t],
  );

  const save = async (): Promise<void> => {
    if (!submit(draft) || lineErrors.some((line) => Object.keys(line).length > 0)) {
      return;
    }

    setBusy(true);

    try {
      const payload: readonly OrderLineInput[] = lines
        .filter((line) => line.productId && Number(line.quantity) > 0)
        .map((line) => ({
          productId: line.productId,
          quantity: Number(line.quantity),
          rate: Number(line.rate || 0),
          taxPercentage: Number(line.taxPercentage || 0),
          discount: Number(line.discount || 0),
        }));

      const chargeInput = charges
        .filter((charge) => charge.ledgerId && Number(charge.amount) > 0)
        .map((charge) => ({
          ledgerId: charge.ledgerId,
          amount: Math.abs(Number(charge.amount)),
        }));

      const common = {
        date: draft.date,
        warehouseId: draft.warehouseId,
        lines: payload,
        charges: chargeInput,
        mode,
        expectedOn: draft.expectedOn || null,
        referenceNumber: draft.referenceNumber.trim() || null,
        narration: draft.narration.trim() || null,
      };

      const header =
        kind === 'purchase'
          ? await createPurchaseOrder({ ...common, supplierLedgerId: draft.partyId })
          : await createSalesOrder({ ...common, customerLedgerId: draft.partyId });

      onSaved(t('orders.savedNotice', { number: header.number }));
    } catch (failure) {
      const api = failure as ApiError;
      onError(api.detail || api.code);
    } finally {
      setBusy(false);
    }
  };

  const set = <TField extends keyof OrderHeaderDraft>(
    field: TField,
    value: OrderHeaderDraft[TField],
  ): void => setDraft((current) => ({ ...current, [field]: value }));

  const change = (index: number, patch: Partial<DraftLine>): void =>
    setLines((previous) =>
      previous.map((line, at) => (at === index ? { ...line, ...patch } : line)),
    );

  return (
    <Modal
      title={kind === 'purchase' ? t('orders.newPurchase') : t('orders.newSales')}
      onClose={onClose}
    >
      <div className="form-grid-3">
        <Field label={t('orders.number')}>
          <input value={t('orders.numberOnSave')} disabled className="field-input" />
        </Field>

        <DateField
          label={t('orders.date')}
          required
          value={draft.date}
          onChange={(value) => set('date', value)}
          error={errors['date']}
        />

        <DateField
          label={t('orders.expectedOn')}
          value={draft.expectedOn}
          onChange={(value) => set('expectedOn', value)}
          error={errors['expectedOn']}
        />

        <Field
          label={kind === 'purchase' ? t('orders.supplier') : t('orders.customer')}
          required
          error={errors['partyId']}
        >
          <SearchSelect
            value={draft.partyId}
            onChange={(value) => set('partyId', value)}
            options={partyOptions}
            invalid={errors['partyId'] !== undefined}
            label={kind === 'purchase' ? t('orders.supplier') : t('orders.customer')}
            placeholder={t('common.choose')}
          />
        </Field>

        <Field label={t('orders.warehouse')} required error={errors['warehouseId']}>
          <SearchSelect
            value={draft.warehouseId}
            onChange={(value) => set('warehouseId', value)}
            options={(warehouses.data ?? []).map((warehouse) => ({
              value: warehouse.id,
              label: `${warehouse.code} — ${warehouse.name}`,
              ...(warehouse.isDefault ? { meta: t('settings.masterDefault') } : {}),
            }))}
            invalid={errors['warehouseId'] !== undefined}
            label={t('orders.warehouse')}
            placeholder={t('common.choose')}
          />
        </Field>

        <SelectField
          label={t('purchase.taxMode')}
          value={mode}
          onChange={setMode}
          options={[
            { value: PurchaseTaxMode.tax, label: t('purchase.modeTax') },
            { value: PurchaseTaxMode.nonTax, label: t('purchase.modeNonTax') },
            { value: PurchaseTaxMode.cst, label: t('purchase.modeCst') },
          ]}
        />

        <TextField
          label={t('orders.reference')}
          value={draft.referenceNumber}
          onChange={(value) => set('referenceNumber', value)}
        />

        <TextField
          label={t('orders.narration')}
          value={draft.narration}
          onChange={(value) => set('narration', value)}
          className="sm:col-span-2"
        />
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-semibold text-ink">{t('orders.linesTitle')}</h3>
        {columns.picker}
      </div>

      {errors['lines'] && <p className="field-message-error">{errors['lines']}</p>}

      <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
        <table className="line-table sm:min-w-[42rem]">
          <thead className="text-xs text-ink-muted">
            <tr>
              <th className="px-2 py-1 text-start">{t('orders.product')}</th>
              <ProductDetailHeaders shows={columns.shows} labels={t} />
              <th className="px-2 py-1 text-end">{t('orders.quantity')}</th>
              <th className="px-2 py-1 text-end">{t('orders.rate')}</th>
              {columns.shows('discount') && (
                <th className="px-2 py-1 text-end">{t('orders.discount')}</th>
              )}
              {columns.shows('taxPercent') && (
                <th className="px-2 py-1 text-end">{t('orders.taxPercent')}</th>
              )}
              <th className="px-2 py-1 text-end">{t('orders.net')}</th>
              <th className="line-action-column" />
            </tr>
          </thead>

          <tbody>
            {lines.map((line, index) => {
              const errorsForLine = lineErrors[index] ?? {};

              return (
                <tr key={line.key} className="border-t border-line">
                  <td data-label={t('orders.product')} className="min-w-64 px-2 py-1">
                    <SearchSelect
                      value={line.productId}
                      onChange={(value) => {
                        const product = (products.data ?? []).find(
                          (candidate) => candidate.id === value,
                        );

                        change(index, {
                          productId: value,
                          // An order starts at the rate the master holds: cost for a
                          // purchase, retail for a sale. Typed over freely — it is a
                          // starting point, not a price list.
                          rate: String(
                            (kind === 'purchase' ? product?.cost : product?.retailRate) ??
                              '',
                          ),
                        });
                      }}
                      options={productOptions}
                      size="sm"
                      label={t('orders.product')}
                      placeholder={t('purchase.chooseProduct')}
                    />
                  </td>

                  <ProductDetailCells
                    product={(products.data ?? []).find(
                      (candidate) => candidate.id === line.productId,
                    )}
                    shows={columns.shows}
                    labels={t}
                  />

                  <NumberCell
                    value={line.quantity}
                    label={t('orders.quantity')}
                    error={errorsForLine['quantity']}
                    onChange={(value) => change(index, { quantity: value })}
                  />
                  <NumberCell
                    value={line.rate}
                    label={t('orders.rate')}
                    error={errorsForLine['rate']}
                    onChange={(value) => change(index, { rate: value })}
                  />

                  {columns.shows('discount') && (
                    <NumberCell
                      value={line.discount}
                      label={t('orders.discount')}
                      onChange={(value) => change(index, { discount: value })}
                    />
                  )}

                  {columns.shows('taxPercent') && (
                    <td data-label={t('orders.taxPercent')} className="px-2 py-1">
                      <input
                        type="number"
                        list="erp-order-tax-rates"
                        step="0.01"
                        min="0"
                        max="100"
                        inputMode="decimal"
                        aria-label={t('orders.taxPercent')}
                        value={line.taxPercentage}
                        onChange={(event) =>
                          change(index, { taxPercentage: event.target.value })
                        }
                        className={clsx(
                          'field-input-sm ms-auto w-24 text-end font-mono tabular-nums',
                          errorsForLine['taxPercentage'] && 'field-invalid',
                        )}
                      />
                      <datalist id="erp-order-tax-rates">
                        {taxRates.map((rate) => (
                          <option key={rate} value={rate} />
                        ))}
                      </datalist>
                    </td>
                  )}

                  <td
                    data-label={t('orders.net')}
                    className="px-2 py-1 text-end font-mono tabular-nums"
                  >
                    {moneyAlways(lineNet(line))}
                  </td>

                  <td className="px-2 py-1 text-end">
                    {lines.length > 1 && (
                      <button
                        type="button"
                        onClick={() =>
                          setLines((previous) => previous.filter((_, at) => at !== index))
                        }
                        className="row-action row-action-danger"
                      >
                        {t('purchase.removeLine')}
                      </button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <button
        type="button"
        onClick={() => setLines((previous) => [...previous, emptyLine(defaultTaxRate)])}
        className="btn-secondary btn-sm self-start"
      >
        {t('purchase.addLine')}
      </button>

      <ChargesPanel
        charges={charges}
        ledgers={ledgers.data ?? []}
        onChange={setCharges}
      />

      <div className="line-totals flex flex-wrap items-end justify-end gap-6">
        <DocumentTotals
          gross={totals.gross}
          discount={totals.discount}
          tax={totals.tax}
          charges={chargeTotal(charges, ledgers.data ?? [])}
        />
      </div>

      <div className="flex justify-end gap-2">
        <ModalButton onClick={onClose}>{t('common.cancel')}</ModalButton>
        <ModalButton primary disabled={busy} onClick={() => void save()}>
          {busy ? t('common.saving') : t('orders.saveDraft')}
        </ModalButton>
      </div>
    </Modal>
  );
}

/** Reads an order back, and offers what may still be done to it. */
function OrderDialog({
  kind,
  id,
  busy,
  onClose,
  onConfirm,
  onConvert,
  onClose_,
}: {
  readonly kind: OrderKind;
  readonly id: string;
  readonly busy: boolean;
  readonly onClose: () => void;
  readonly onConfirm: (id: string) => void;
  readonly onConvert: (id: string) => void;
  /** Closes the order short, or cancels it. Named apart from the dialog's own close. */
  readonly onClose_: (id: string, reason: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [reason, setReason] = useState('');

  const query = useQuery<OrderDocument, ApiError>({
    queryKey: [`${kind}-orders-detail`, id],
    queryFn: async () => {
      if (kind === 'purchase') {
        const found = await getPurchaseOrder(id);

        return {
          number: found.header.number,
          status: found.header.status,
          date: found.date,
          expectedOn: found.expectedOn,
          referenceNumber: found.referenceNumber,
          narration: found.narration,
          closureReason: found.closureReason,
          currency: found.currency,
          taxable: found.header.taxable,
          tax: found.header.tax,
          chargeTotal: found.header.chargeTotal,
          total: found.header.total,
          lines: found.lines,
          charges: found.charges,
        };
      }

      const found = await getSalesOrder(id);

      return {
        number: found.header.number,
        status: found.header.status,
        date: found.date,
        expectedOn: found.expectedOn,
        referenceNumber: found.referenceNumber,
        narration: found.narration,
        closureReason: found.closureReason,
        currency: found.currency,
        taxable: found.header.taxable,
        tax: found.header.tax,
        chargeTotal: found.header.chargeTotal,
        total: found.header.total,
        lines: found.lines,
        charges: found.charges,
      };
    },
  });

  const order = query.data;

  return (
    <Modal title={order?.number ?? t('orders.document')} onClose={onClose}>
      {query.isLoading && (
        <div className="space-y-3" aria-busy="true">
          <span className="skeleton block h-4 w-40 rounded" />
          <span className="skeleton block h-24 w-full rounded-lg" />
        </div>
      )}

      {order && (
        <>
          <div className="grid gap-2 text-sm sm:grid-cols-3">
            <Detail label={t('orders.number')} value={order.number} />
            <Detail label={t('orders.date')} value={order.date} />
            <Detail label={t('orders.expectedOn')} value={order.expectedOn ?? '—'} />
            <div>
              <span className="text-xs text-ink-muted">{t('orders.status')}</span>
              <div className="mt-0.5">
                <StatusBadge
                  tone={statusTone(order.status)}
                  label={statusLabel(order.status, t)}
                  struck={order.status === OrderStatus.cancelled}
                />
              </div>
            </div>
            <Detail label={t('orders.reference')} value={order.referenceNumber ?? '—'} />
            <Detail label={t('orders.currency')} value={order.currency} />
          </div>

          {order.closureReason && (
            <p className="alert-warn text-xs">
              {t('orders.closedFor', { reason: order.closureReason })}
            </p>
          )}

          {/*
            Said on the screen rather than left to be worked out from a trial
            balance that does not mention it. An order is a commitment, not a
            transaction: it writes no voucher, so it moves no account and appears in
            no statement. What reaches the books is the purchase or the invoice a
            conversion produces, and only once that is posted.
          */}
          <p className="rounded-lg border border-line bg-surface-2 px-3 py-2 text-xs text-ink-muted">
            {t('orders.booksHint')}
          </p>

          <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
            <table className="w-full min-w-[34rem] text-sm">
              <thead className="text-xs text-ink-muted">
                <tr>
                  <th className="px-2 py-1 text-start">#</th>
                  <th className="px-2 py-1 text-end">{t('orders.ordered')}</th>
                  <th className="px-2 py-1 text-end">{t('orders.invoiced')}</th>
                  <th className="px-2 py-1 text-end">{t('orders.outstanding')}</th>
                  <th className="px-2 py-1 text-end">{t('orders.rate')}</th>
                  <th className="px-2 py-1 text-end">{t('orders.taxable')}</th>
                </tr>
              </thead>
              <tbody>
                {order.lines.map((line) => (
                  <tr key={line.lineNumber} className="border-t border-line">
                    <td className="px-2 py-1">{line.lineNumber}</td>
                    <td className="px-2 py-1 text-end font-mono">{line.quantity}</td>
                    <td className="px-2 py-1 text-end font-mono">
                      {line.invoicedQuantity}
                    </td>
                    <td
                      className={clsx(
                        'px-2 py-1 text-end font-mono',
                        line.outstandingQuantity > 0 && 'font-semibold text-ink',
                      )}
                    >
                      {line.outstandingQuantity}
                    </td>
                    <td className="px-2 py-1 text-end font-mono">
                      {moneyAlways(line.rate)}
                    </td>
                    <td className="px-2 py-1 text-end font-mono">
                      {moneyAlways(line.taxable)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/*
            The same ladder the order was entered against. Gross and discount are
            added up from the lines because the header carries neither — it keeps the
            taxable value, which is what is left after the discount.
          */}
          <div className="line-totals flex flex-wrap items-end justify-end gap-6">
            <DocumentTotals
              gross={order.lines.reduce(
                (running, line) => running + line.quantity * line.rate,
                0,
              )}
              discount={order.lines.reduce((running, line) => running + line.discount, 0)}
              tax={order.tax}
              charges={order.chargeTotal}
              currency={order.currency}
            />
          </div>

          {/*
            What an order can do next, and only what it can do next. A draft is
            confirmed; a confirmed order is converted into the document that moves
            the goods; either can be closed with a reason. A completed or cancelled
            one is finished, and offering a button that the server would refuse is
            how a screen teaches people to distrust it.
          */}
          {order.status === OrderStatus.draft && (
            <div className="flex justify-end">
              <ModalButton primary disabled={busy} onClick={() => onConfirm(id)}>
                {t('orders.confirm')}
              </ModalButton>
            </div>
          )}

          {order.status === OrderStatus.confirmed && (
            <div className="flex flex-wrap justify-end gap-2">
              <ModalButton primary disabled={busy} onClick={() => onConvert(id)}>
                {kind === 'purchase'
                  ? t('orders.convertPurchase')
                  : t('orders.convertSales')}
              </ModalButton>
            </div>
          )}

          {(order.status === OrderStatus.draft ||
            order.status === OrderStatus.confirmed) && (
            <div className="flex flex-wrap items-end justify-end gap-2">
              <TextField
                label={t('orders.closeReason')}
                size="sm"
                value={reason}
                onChange={setReason}
              />
              <ModalButton
                disabled={busy || reason.trim() === ''}
                onClick={() => onClose_(id, reason.trim())}
              >
                {t('orders.close')}
              </ModalButton>
            </div>
          )}
        </>
      )}

      <div className="flex justify-end">
        <ModalButton onClick={onClose}>{t('common.close')}</ModalButton>
      </div>
    </Modal>
  );
}

function statusLabel(status: number, t: (key: string) => string): string {
  if (status === OrderStatus.confirmed) return t('orders.confirmed');
  if (status === OrderStatus.completed) return t('orders.completed');
  if (status === OrderStatus.cancelled) return t('orders.cancelled');

  return t('orders.draft');
}

function statusTone(status: number): StatusTone {
  if (status === OrderStatus.confirmed) return 'info';
  if (status === OrderStatus.completed) return 'success';
  if (status === OrderStatus.cancelled) return 'danger';

  return 'warn';
}

function Detail({
  label,
  value,
}: {
  readonly label: string;
  readonly value: string;
}): React.JSX.Element {
  return (
    <div>
      <span className="text-xs text-ink-muted">{label}</span>
      <div className="font-mono tabular-nums text-ink">{value}</div>
    </div>
  );
}

function NumberCell({
  value,
  onChange,
  label,
  error,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly label: string;
  readonly error?: string | undefined;
}): React.JSX.Element {
  /*
    The cell end-aligns its box, because the header above it is end-aligned too.
    Left as it was, a `w-24` input sat at the start of a wider cell with its
    caption over the gap to its right — every numeric column on these entry grids
    was a heading pointing at the space beside the figures.
  */
  return (
    <td data-label={label} className="px-2 py-1 text-end">
      <input
        type="number"
        inputMode="decimal"
        aria-label={label}
        aria-invalid={error ? true : undefined}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={clsx(
          'field-input-sm ms-auto w-24 text-end font-mono tabular-nums',
          error && 'field-invalid',
        )}
      />
    </td>
  );
}
