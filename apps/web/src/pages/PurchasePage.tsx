import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { Modal, ModalButton } from '@/components/Modal';
import { ReportFrame } from '@/components/ReportFrame';
import {
  DateField,
  Field,
  SelectField,
  TextAreaField,
  TextField,
} from '@/components/Form';
import { SearchSelect } from '@/components/SearchSelect';
import { StatusBadge, type StatusTone } from '@/components/StatusBadge';
import {
  ChargesPanel,
  DocumentTotals,
  chargeTotal,
  productDetailColumns,
  ProductDetailCells,
  ProductDetailHeaders,
  productOption,
  stockByProduct,
  useDefaultCharges,
  useLineColumns,
  type DraftCharge,
  type LineColumn,
} from '@/components/DocumentLines';
import type { ApiError } from '@/lib/api';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import { listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { listProducts, type ProductSummary } from '@/lib/products';
import { listSuppliers, type SupplierSummary } from '@/lib/suppliers';
import { fetchStockValuation, type StockValuationReport } from '@/lib/stock';
import type { PagedResult } from '@/lib/sales';
import { collect, numeric, required, useValidation } from '@/lib/validation';
import { useSettings } from '@/stores/settings';
import {
  cancelPurchaseInvoice,
  createPurchaseInvoice,
  getPurchaseInvoice,
  isPurchaseReturn,
  listPurchaseInvoices,
  postPurchaseInvoice,
  PurchaseDocumentKind,
  PurchaseInvoiceStatus,
  PurchaseTaxMode,
  type PurchaseInvoiceDetail,
  type PurchaseInvoiceSummary,
  type PurchaseLineInput,
} from '@/lib/purchase';
import { moneyAlways } from '@/lib/money';

/** A line as the screen holds it, before it is worth sending. */
interface DraftLine {
  readonly key: string;
  productId: string;
  quantity: string;
  rate: string;
  taxPercentage: string;
  discount: string;
  /**
   * The batch the goods arrived in, typed rather than chosen.
   *
   * The difference from the sales screen that matters. A sale picks a batch off a shelf;
   * a purchase is usually the moment one comes into existence, so this is read off the
   * carton and the receipt opens it.
   */
  batchNumber: string;
  expiresOn: string;
  /** The numbers on the units arriving, one per line as they are keyed in. */
  serialNumbers: string;
}

function emptyLine(taxRate: number): DraftLine {
  return {
    key: crypto.randomUUID(),
    productId: '',
    quantity: '1',
    rate: '',
    taxPercentage: String(taxRate),
    discount: '0',
    batchNumber: '',
    expiresOn: '',
    serialNumbers: '',
  };
}

/** Splits a keyed-in block of serial numbers into the numbers it names. */
function splitSerials(entered: string): readonly string[] {
  return entered
    .split(/[\n,;]/)
    .map((number) => number.trim())
    .filter((number) => number !== '');
}

/** What a line comes to before tax. */
function lineNet(line: DraftLine): number {
  const net = Number(line.quantity) * Number(line.rate) - Number(line.discount || 0);

  return Number.isFinite(net) ? net : 0;
}

/**
 * Purchases and purchase returns.
 *
 * Two menu entries and two screens, from one component told which it is. They were
 * one screen with a Kind dropdown, which is how a storekeeper sending goods back
 * arrived at a form that opens on "Purchase" and has to be switched — and how the
 * list of purchases came up carrying returns among them. What they are is one kind
 * of document running in opposite directions, so the code stays one; what somebody
 * is doing when they open the screen is one or the other, so the screens are two.
 *
 * Entering and posting are separate here as they are on the server: a draft has moved
 * nothing and can be corrected while somebody keys it off the supplier's invoice;
 * posting receives the stock, raises the debt and writes the books in one transaction.
 *
 * Cancelling is for a document that should never have been entered. Goods the firm has
 * accepted and is sending back go on a purchase return instead — and a cancellation can
 * be refused because the goods are already gone, which is the server's answer to say
 * rather than this screen's to predict.
 */
export function PurchasePage({
  kind = PurchaseDocumentKind.invoice,
}: {
  /** Which of the two documents this screen is for. */
  readonly kind?: number;
} = {}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const pageSize = useSettings((state) => state.pageSize);

  const isReturn = isPurchaseReturn(kind);

  const today = new Date().toISOString().slice(0, 10);
  const monthStart = `${today.slice(0, 7)}-01`;

  const [from, setFrom] = useState(monthStart);
  const [to, setTo] = useState(today);
  const [statusFilter, setStatusFilter] = useState<number | ''>('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const [entering, setEntering] = useState(false);
  const [viewing, setViewing] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // The kind is the screen's, not a filter: this list is purchases or it is returns.
  const filter = { from, to, kind, status: statusFilter, search };

  const query = useQuery<PagedResult<PurchaseInvoiceSummary>, ApiError>({
    queryKey: ['purchase-invoices', kind, from, to, statusFilter, search, page, pageSize],
    queryFn: () => listPurchaseInvoices(filter, page, pageSize),
  });

  const mutation = useMutation<string | null, ApiError, () => Promise<string | null>>({
    mutationFn: (action) => action(),
    onSuccess: async (message) => {
      setError(null);
      setNotice(message);
      window.setTimeout(() => setNotice(null), 5000);

      await queryClient.invalidateQueries({ queryKey: ['purchase-invoices'] });
      await queryClient.invalidateQueries({ queryKey: ['purchase-invoice'] });
    },
    // The server owns the rules — a missing account, a batch that does not exist, a
    // supplier invoice already entered — so its own message is shown.
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const run = (action: () => Promise<string | null>): void => mutation.mutate(action);

  const narrow =
    <T,>(set: (value: T) => void) =>
    (value: T): void => {
      set(value);
      setPage(1);
    };

  const columns: readonly GridColumn<PurchaseInvoiceSummary>[] = [
    {
      key: 'number',
      header: t('purchase.number'),
      value: (row) => row.number,
      // The document number opens the document, which is what somebody clicks on
      // every other list in the application. It was a button captioned "Open" at
      // the far end of eleven columns.
      render: (row) => (
        <button
          type="button"
          onClick={() => setViewing(row.purchaseInvoiceId)}
          className="cell-link"
        >
          {row.number}
        </button>
      ),
    },
    { key: 'date', header: t('purchase.date'), value: (row) => row.date },
    { key: 'supplier', header: t('purchase.supplier'), value: (row) => row.supplierName },
    {
      key: 'supplierInvoice',
      header: t('purchase.supplierInvoice'),
      // Shown by default rather than hidden: it is what somebody holding the supplier's
      // document is looking the entry up by.
      value: (row) => row.supplierInvoiceNumber ?? '',
    },
    {
      key: 'supplierInvoiceDate',
      header: t('purchase.supplierInvoiceDate'),
      value: (row) => row.supplierInvoiceDate ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'lines',
      header: t('purchase.lines'),
      value: (row) => row.lineCount,
      numeric: true,
    },
    {
      key: 'taxable',
      header: t('purchase.taxable'),
      value: (row) => row.taxable,
      numeric: true,
      render: (row) => moneyAlways(row.taxable),
    },
    {
      key: 'tax',
      header: t('purchase.tax'),
      value: (row) => row.tax,
      numeric: true,
      render: (row) => moneyAlways(row.tax),
    },
    {
      key: 'total',
      header: t('purchase.total'),
      value: (row) => row.total,
      numeric: true,
      render: (row) => moneyAlways(row.total),
    },
    {
      key: 'status',
      header: t('purchase.status'),
      value: (row) => statusLabel(row.status, t),
      render: (row) => (
        <StatusBadge
          tone={statusTone(row.status)}
          label={statusLabel(row.status, t)}
          struck={row.status === PurchaseInvoiceStatus.cancelled}
        />
      ),
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      // Withdrawn below `sm`. The grid draws a card there and leads it with the
      // document number, which is already the link into the document — a button
      // captioned "Open" underneath is the same action offered twice, and on the
      // narrowest screen it is the one that costs a row of height.
      hideOnNarrow: true,
      render: (row) => (
        <button
          type="button"
          onClick={() => setViewing(row.purchaseInvoiceId)}
          className="row-action row-action-neutral"
        >
          {row.status === PurchaseInvoiceStatus.draft
            ? t('purchase.openDraft')
            : t('purchase.open')}
        </button>
      ),
    },
  ];

  const controls = (
    <div className="filter-grid">
      <DateField
        label={t('purchase.from')}
        value={from}
        onChange={narrow(setFrom)}
        size="sm"
      />
      <DateField label={t('purchase.to')} value={to} onChange={narrow(setTo)} size="sm" />

      <SelectField<number | ''>
        label={t('purchase.status')}
        value={statusFilter}
        onChange={narrow<number | ''>(setStatusFilter)}
        size="sm"
        options={[
          { value: '', label: t('purchase.allStatuses') },
          { value: PurchaseInvoiceStatus.draft, label: t('purchase.draft') },
          { value: PurchaseInvoiceStatus.posted, label: t('purchase.posted') },
          { value: PurchaseInvoiceStatus.cancelled, label: t('purchase.cancelled') },
        ]}
      />

      <TextField
        label={t('purchase.search')}
        type="search"
        value={search}
        onChange={(value) => narrow(setSearch)(value)}
        placeholder={t('purchase.searchHint')}
        size="sm"
      />
    </div>
  );

  return (
    <>
      <ReportFrame
        title={isReturn ? t('nav.purchaseReturns') : t('nav.purchase')}
        controls={controls}
        query={query}
      >
        {(result) => (
          <div className="space-y-3">
            {error && <p className="alert-error">{error}</p>}

            {notice && <p className="alert-success">{notice}</p>}

            <DataGrid
              gridKey={isReturn ? 'purchase-returns' : 'purchase-invoices'}
              rows={result.items}
              columns={columns}
              rowKey={(row) => row.purchaseInvoiceId}
              emptyMessage={isReturn ? t('purchase.noReturns') : t('purchase.none')}
              actions={
                <GridAction
                  label={isReturn ? t('purchase.newReturn') : t('purchase.newInvoice')}
                  onClick={() => setEntering(true)}
                />
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

      {/* Outside the frame on purpose: it replaces its children with a skeleton
          whenever the list goes back to pending — a page change or a filter — and an
          invoice half entered should not go with them. */}
      {entering && (
        <EntryDialog
          kind={kind}
          onClose={() => setEntering(false)}
          onSaved={(message) => {
            setEntering(false);
            setNotice(message);
            void queryClient.invalidateQueries({ queryKey: ['purchase-invoices'] });
          }}
          onError={setError}
        />
      )}

      {viewing && (
        <DocumentDialog
          id={viewing}
          busy={mutation.isPending}
          onClose={() => setViewing(null)}
          onPost={(id) =>
            run(async () => {
              const posted = await postPurchaseInvoice(id);

              return t('purchase.postedNotice', {
                number: posted.number,
                stock: posted.stockDocumentNumber,
                total: moneyAlways(posted.total),
              });
            })
          }
          onCancel={(id, reason) =>
            run(async () => {
              await cancelPurchaseInvoice(id, reason);
              setViewing(null);

              return t('purchase.cancelledNotice');
            })
          }
        />
      )}
    </>
  );
}

/** The purchase-returns screen: the same component, told which document it is for. */
export function PurchaseReturnsPage(): React.JSX.Element {
  return <PurchasePage kind={PurchaseDocumentKind.return} />;
}

/** What the entry dialog holds while a document is being keyed in. */
interface HeaderDraft {
  date: string;
  supplierId: string;
  warehouseId: string;
  supplierInvoiceNumber: string;
  supplierInvoiceDate: string;
  returnsInvoiceId: string;
  narration: string;
}

/** Enters a draft: the header, the lines, then whatever is charged beside them. */
function EntryDialog({
  kind,
  onClose,
  onSaved,
  onError,
}: {
  readonly kind: number;
  readonly onClose: () => void;
  readonly onSaved: (message: string) => void;
  readonly onError: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const { taxRates, defaultTaxRate, preferredWarehouseId } = useSettings();

  const isReturn = isPurchaseReturn(kind);
  const today = new Date().toISOString().slice(0, 10);

  const [mode, setMode] = useState<number>(PurchaseTaxMode.tax);
  const [draft, setDraft] = useState<HeaderDraft>({
    date: today,
    supplierId: '',
    warehouseId: '',
    supplierInvoiceNumber: '',
    supplierInvoiceDate: '',
    returnsInvoiceId: '',
    narration: '',
  });
  const [lines, setLines] = useState<readonly DraftLine[]>([emptyLine(defaultTaxRate)]);
  const [charges, setCharges] = useState<readonly DraftCharge[]>([]);
  const [busy, setBusy] = useState(false);

  const suppliers = useQuery<readonly SupplierSummary[], ApiError>({
    queryKey: ['suppliers', 'picker'],
    queryFn: () => listSuppliers('', true),
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
    isReturn ? 'purchaseReturn' : 'purchase',
    ledgers.data ?? [],
    setCharges,
  );

  // What is on the shelf, for the product picker. One call for the whole master
  // rather than one per line, and stale for a minute: a purchase being keyed in is
  // not a stock take, and the figure is context rather than a control.
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

  /*
    The warehouse a purchase starts at.

    Every purchase in a firm with one store went to the same warehouse, and the
    screen made somebody choose it from a dropdown every time — then refused the
    document if they forgot. The master already records which warehouse is the
    default; Settings can override it for a workstation that receives somewhere
    else. Applied once, and only while the field is still empty, so it never
    overwrites a choice somebody has made.
  */
  useEffect(() => {
    if (draft.warehouseId !== '' || !warehouses.data) {
      return;
    }

    const preferred = warehouses.data.find(
      (warehouse) => warehouse.id === preferredWarehouseId,
    );
    const fallback =
      warehouses.data.find((warehouse) => warehouse.isDefault) ??
      (warehouses.data.length === 1 ? warehouses.data[0] : undefined);

    const chosen = preferred ?? fallback;

    if (chosen) {
      setDraft((current) => ({ ...current, warehouseId: chosen.id }));
    }
  }, [warehouses.data, preferredWarehouseId, draft.warehouseId]);

  // Only posted purchases can be returned against, and only this supplier's: offering
  // somebody else's would be offering a mistake.
  const returnable = useQuery<PagedResult<PurchaseInvoiceSummary>, ApiError>({
    queryKey: ['purchase-invoices', 'returnable', draft.supplierId],
    queryFn: () =>
      listPurchaseInvoices(
        {
          kind: PurchaseDocumentKind.invoice,
          status: PurchaseInvoiceStatus.posted,
          supplierLedgerId: draft.supplierId,
        },
        1,
        50,
      ),
    enabled: isReturn && draft.supplierId !== '',
  });

  const lineColumns: readonly LineColumn[] = [
    { key: 'product', label: t('purchase.product'), defaultOn: true, fixed: true },
    ...productDetailColumns(t),
    { key: 'quantity', label: t('purchase.quantity'), defaultOn: true, fixed: true },
    { key: 'rate', label: t('purchase.rate'), defaultOn: true, fixed: true },
    { key: 'discount', label: t('purchase.discount'), defaultOn: true },
    { key: 'taxPercent', label: t('purchase.taxPercent'), defaultOn: true },
    { key: 'taxAmount', label: t('purchase.taxAmount'), defaultOn: false },
    { key: 'net', label: t('purchase.net'), defaultOn: true, fixed: true },
  ];

  const columns = useLineColumns('purchase', lineColumns);

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

  const chargesTotal = chargeTotal(charges, ledgers.data ?? []);

  const change = (index: number, patch: Partial<DraftLine>): void =>
    setLines((previous) =>
      previous.map((line, at) => (at === index ? { ...line, ...patch } : line)),
    );

  const { errors, submit } = useValidation<HeaderDraft>((values) =>
    collect({
      date: required(values.date, t('purchase.dateRequired')),
      supplierId: required(values.supplierId, t('purchase.supplierRequired')),
      warehouseId: required(values.warehouseId, t('purchase.warehouseRequired')),
      /*
        Not required to save a draft, on purpose.

        Input tax is only reclaimable against the supplier's own tax invoice, so
        this number does have to be here eventually — but a draft is precisely the
        state for a delivery that has arrived before its paperwork, and refusing to
        record it until the invoice turns up means it does not get recorded at all.
        The screen says what is still needed, and posting is where it is enforced.
      */
      supplierInvoiceDate:
        values.supplierInvoiceDate !== '' && values.supplierInvoiceNumber.trim() === ''
          ? t('purchase.invoiceDateNeedsNumber')
          : null,
      lines: lines.some((line) => line.productId && Number(line.quantity) > 0)
        ? null
        : t('purchase.linesRequired'),
    }),
  );

  const lineErrors = useMemo(
    () =>
      lines.map((line) =>
        collect({
          quantity: line.productId
            ? numeric(line.quantity, t('purchase.quantityInvalid'), { min: 0.000001 })
            : null,
          rate: line.productId
            ? numeric(line.rate, t('purchase.rateInvalid'), { min: 0 })
            : null,
          discount: numeric(line.discount, t('purchase.discountInvalid'), { min: 0 }),
          taxPercentage: numeric(line.taxPercentage, t('purchase.taxInvalid'), {
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
      const payload: readonly PurchaseLineInput[] = lines
        .filter((line) => line.productId && Number(line.quantity) > 0)
        .map((line) => ({
          productId: line.productId,
          quantity: Number(line.quantity),
          rate: Number(line.rate || 0),
          taxPercentage: Number(line.taxPercentage || 0),
          discount: Number(line.discount || 0),
          batchNumber: line.batchNumber.trim() || null,
          expiresOn: line.expiresOn || null,
          serialNumbers: splitSerials(line.serialNumbers),
        }));

      const header = await createPurchaseInvoice({
        date: draft.date,
        supplierLedgerId: draft.supplierId,
        warehouseId: draft.warehouseId,
        lines: payload,
        kind,
        mode,
        charges: charges
          .filter((charge) => charge.ledgerId && Number(charge.amount) > 0)
          .map((charge) => ({
            ledgerId: charge.ledgerId,
            // Always positive on the wire: the firm's charge matrix decides which
            // way each one moves the total, and a negative freight would be a
            // second, contradictory answer.
            amount: Math.abs(Number(charge.amount)),
          })),
        returnsInvoiceId:
          isReturn && draft.returnsInvoiceId ? draft.returnsInvoiceId : null,
        supplierInvoiceNumber: draft.supplierInvoiceNumber.trim() || null,
        // Only sent with the number it belongs to: a date on its own is a fact about a
        // document nobody can identify, and the server refuses it.
        supplierInvoiceDate: draft.supplierInvoiceNumber.trim()
          ? draft.supplierInvoiceDate || null
          : null,
        narration: draft.narration.trim() || null,
      });

      onSaved(t('purchase.savedNotice', { number: header.number }));
    } catch (failure) {
      const api = failure as ApiError;
      onError(api.detail || api.code);
    } finally {
      setBusy(false);
    }
  };

  const set = <TField extends keyof HeaderDraft>(
    field: TField,
    value: HeaderDraft[TField],
  ): void => setDraft((current) => ({ ...current, [field]: value }));

  return (
    <Modal
      title={isReturn ? t('purchase.newReturn') : t('purchase.newInvoice')}
      onClose={onClose}
    >
      <div className="form-grid-3">
        {/*
          The document's own number is not asked for: it is issued by the branch's
          numbering series when the draft is saved, so the field says what will
          happen rather than inviting somebody to type a number that will be
          discarded. The supplier's number, which the firm does not issue, is a
          field further down and mandatory on a tax invoice.
        */}
        <Field label={t('purchase.documentNumber')}>
          <input value={t('purchase.numberOnSave')} disabled className="field-input" />
        </Field>

        <DateField
          label={t('purchase.date')}
          required
          value={draft.date}
          onChange={(value) => set('date', value)}
          error={errors['date']}
        />

        <Field label={t('purchase.supplier')} required error={errors['supplierId']}>
          <SearchSelect
            value={draft.supplierId}
            onChange={(value) => set('supplierId', value)}
            options={(suppliers.data ?? []).map((supplier) => ({
              value: supplier.supplierId,
              label: `${supplier.code} — ${supplier.name}`,
              ...(supplier.contact.mobileNumber
                ? { detail: supplier.contact.mobileNumber }
                : {}),
            }))}
            invalid={errors['supplierId'] !== undefined}
            label={t('purchase.supplier')}
            placeholder={t('purchase.chooseSupplier')}
          />
        </Field>

        <Field label={t('purchase.warehouse')} required error={errors['warehouseId']}>
          <SearchSelect
            value={draft.warehouseId}
            onChange={(value) => set('warehouseId', value)}
            options={(warehouses.data ?? []).map((warehouse) => ({
              value: warehouse.id,
              label: `${warehouse.code} — ${warehouse.name}`,
              ...(warehouse.isDefault ? { meta: t('settings.masterDefault') } : {}),
            }))}
            invalid={errors['warehouseId'] !== undefined}
            label={t('purchase.warehouse')}
            placeholder={t('purchase.chooseWarehouse')}
          />
        </Field>

        {/*
          The tax mode: whether this document carries tax at all, and under which
          scheme. Defaulted by the server from the firm's regime when it is not
          stated — but a purchase from an unregistered supplier is a real and
          frequent case, and the screen had no way to say so.
        */}
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

        {/*
          Not marked mandatory, because the draft saves without it. A red marker on
          a field the form accepts as empty is how a marker stops meaning anything;
          the notice below says what it is for and posting is where it is needed.
        */}
        <TextField
          label={t('purchase.supplierInvoice')}
          value={draft.supplierInvoiceNumber}
          onChange={(value) => set('supplierInvoiceNumber', value)}
          error={errors['supplierInvoiceNumber']}
        />

        <DateField
          label={t('purchase.supplierInvoiceDate')}
          value={draft.supplierInvoiceDate}
          onChange={(value) => set('supplierInvoiceDate', value)}
          error={errors['supplierInvoiceDate']}
        />

        {isReturn && (
          <Field label={t('purchase.againstInvoice')}>
            <SearchSelect
              value={draft.returnsInvoiceId}
              onChange={(value) => set('returnsInvoiceId', value)}
              clearable
              label={t('purchase.againstInvoice')}
              placeholder={t('purchase.noInvoice')}
              options={(returnable.data?.items ?? []).map((invoice) => ({
                value: invoice.purchaseInvoiceId,
                label: invoice.number,
                detail: invoice.date,
                meta: moneyAlways(invoice.total),
              }))}
            />
          </Field>
        )}

        <TextField
          label={t('purchase.narration')}
          value={draft.narration}
          onChange={(value) => set('narration', value)}
          className="sm:col-span-2"
        />
      </div>

      {/*
        A notice rather than a refusal, and beside the field it is about rather than
        at the foot of the dialog. The draft saves without the number; posting is
        where it is needed, and saying so at the point of entry is what stops it
        being a surprise then.
      */}
      {!isReturn &&
      mode === PurchaseTaxMode.tax &&
      draft.supplierInvoiceNumber.trim() === '' ? (
        <p className="alert-warn text-xs">{t('purchase.supplierInvoiceMissing')}</p>
      ) : (
        /* Said plainly rather than left to be discovered: reclaiming input tax needs
           the supplier's own tax invoice, and the same number twice is refused. */
        <p className="rounded-lg border border-line bg-surface-2 px-3 py-2 text-xs text-ink-muted">
          {t('purchase.supplierInvoiceHint')}
        </p>
      )}

      {isReturn && <p className="alert-warn text-xs">{t('purchase.returnHint')}</p>}

      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-semibold text-ink">{t('purchase.linesTitle')}</h3>
        {/* The line grid gets a column picker of its own, for the same reason the
            list grids have one: a firm that buys at a flat rate with no discounts
            should not look past two empty boxes on every line of every purchase. */}
        {columns.picker}
      </div>

      {errors['lines'] && <p className="field-message-error">{errors['lines']}</p>}

      <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
        <table className="line-table sm:min-w-[44rem]">
          <thead className="text-xs text-ink-muted">
            <tr>
              <th className="px-2 py-1 text-start">{t('purchase.product')}</th>
              <ProductDetailHeaders shows={columns.shows} labels={t} />
              <th className="px-2 py-1 text-end">{t('purchase.quantity')}</th>
              <th className="px-2 py-1 text-end">{t('purchase.rate')}</th>
              {columns.shows('discount') && (
                <th className="px-2 py-1 text-end">{t('purchase.discount')}</th>
              )}
              {columns.shows('taxPercent') && (
                <th className="px-2 py-1 text-end">{t('purchase.taxPercent')}</th>
              )}
              {columns.shows('taxAmount') && (
                <th className="px-2 py-1 text-end">{t('purchase.taxAmount')}</th>
              )}
              <th className="px-2 py-1 text-end">{t('purchase.net')}</th>
              <th className="line-action-column" />
            </tr>
          </thead>

          <tbody>
            {lines.map((line, index) => (
              <LineRow
                key={line.key}
                line={line}
                isReturn={isReturn}
                products={products.data ?? []}
                productOptions={productOptions}
                taxRates={taxRates}
                columns={columns}
                errors={lineErrors[index] ?? {}}
                removable={lines.length > 1}
                onChange={(patch) => change(index, patch)}
                onRemove={() =>
                  setLines((previous) => previous.filter((_, at) => at !== index))
                }
              />
            ))}
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
          charges={chargesTotal}
        />
      </div>

      {/* What the screen adds up is what the lines come to; the server rounds the total
          to the currency and may differ in the last place. */}
      <p className="text-xs text-ink-muted">{t('purchase.totalsHint')}</p>

      <div className="flex justify-end gap-2">
        <ModalButton onClick={onClose}>{t('common.cancel')}</ModalButton>
        <ModalButton primary disabled={busy} onClick={() => void save()}>
          {busy ? t('common.saving') : t('purchase.saveDraft')}
        </ModalButton>
      </div>
    </Modal>
  );
}

/**
 * One line, and whatever the product it names needs beyond a quantity and a rate.
 *
 * The batch and the serial numbers are typed rather than chosen, which is the whole
 * difference from the sales line: a purchase brings goods into existence, so there is
 * nothing on a shelf yet to offer. A return is the exception - the goods are leaving, so
 * the batch it names has to be one that already exists.
 */
function LineRow({
  line,
  isReturn,
  products,
  productOptions,
  taxRates,
  columns,
  errors,
  removable,
  onChange,
  onRemove,
}: {
  readonly line: DraftLine;
  readonly isReturn: boolean;
  readonly products: readonly ProductSummary[];
  readonly productOptions: ReturnType<typeof productOption>[];
  readonly taxRates: readonly number[];
  readonly columns: ReturnType<typeof useLineColumns>;
  readonly errors: Readonly<Record<string, string>>;
  readonly removable: boolean;
  readonly onChange: (patch: Partial<DraftLine>) => void;
  readonly onRemove: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const product = products.find((candidate) => candidate.id === line.productId);
  const net = lineNet(line);
  const tax = (net * Number(line.taxPercentage || 0)) / 100;

  const wanted = Number(line.quantity);
  const needsBatch = product?.tracksBatches === true;
  const needsSerials = product?.tracksSerialNumbers === true;
  const named = splitSerials(line.serialNumbers).length;

  /*
    How far the batch and serial sub-row reaches: product, quantity, rate, net and
    the action column, plus whichever optional columns are on. Counted rather than
    fixed because the row above it is counted too, and a sub-row that stops short
    leaves a gap in the middle of a line somebody is filling in.
  */
  const span =
    4 +
    [
      'productCode',
      'productUnit',
      'productRetail',
      'productMrp',
      'discount',
      'taxPercent',
      'taxAmount',
    ].filter((key) => columns.shows(key)).length;

  return (
    <>
      <tr className="border-t border-line">
        <td data-label={t('purchase.product')} className="min-w-64 px-2 py-1">
          {/*
            The picker shows what each product is and what is on the shelf, and it
            can be typed into. It was a native select listing `code — description`
            and nothing else, so choosing what to reorder meant opening the stock
            report in another tab, and finding a product meant scrolling.
          */}
          <SearchSelect
            value={line.productId}
            onChange={(value) =>
              // The batch and the units belong to the product that was chosen before,
              // so they are dropped rather than carried onto a different one.
              onChange({
                productId: value,
                batchNumber: '',
                expiresOn: '',
                serialNumbers: '',
                ...(products.find((candidate) => candidate.id === value)?.cost
                  ? {
                      rate: String(
                        products.find((candidate) => candidate.id === value)?.cost ?? '',
                      ),
                    }
                  : {}),
              })
            }
            options={productOptions}
            size="sm"
            label={t('purchase.product')}
            placeholder={t('purchase.chooseProduct')}
          />
        </td>

        <ProductDetailCells product={product} shows={columns.shows} labels={t} />

        <NumberCell
          value={line.quantity}
          error={errors['quantity']}
          label={t('purchase.quantity')}
          onChange={(value) => onChange({ quantity: value })}
        />
        <NumberCell
          value={line.rate}
          error={errors['rate']}
          label={t('purchase.rate')}
          onChange={(value) => onChange({ rate: value })}
        />

        {columns.shows('discount') && (
          <NumberCell
            value={line.discount}
            error={errors['discount']}
            label={t('purchase.discount')}
            onChange={(value) => onChange({ discount: value })}
          />
        )}

        {columns.shows('taxPercent') && (
          <td data-label={t('purchase.taxPercent')} className="px-2 py-1 text-end">
            {/*
              A list of the rates the firm actually charges, plus whatever is typed.
              Under GST there are seven of them and mistyping 18 as 1.8 is a return
              that has to be amended; a picker of the firm's own rates is what the
              Settings screen exists to fill.
            */}
            <input
              type="number"
              list="erp-tax-rates"
              step="0.01"
              min="0"
              max="100"
              inputMode="decimal"
              aria-label={t('purchase.taxPercent')}
              value={line.taxPercentage}
              onChange={(event) => onChange({ taxPercentage: event.target.value })}
              className={clsx(
                'field-input-sm ms-auto w-24 text-end font-mono tabular-nums',
                errors['taxPercentage'] && 'field-invalid',
              )}
            />
            <datalist id="erp-tax-rates">
              {taxRates.map((rate) => (
                <option key={rate} value={rate} />
              ))}
            </datalist>
          </td>
        )}

        {columns.shows('taxAmount') && (
          <td
            data-label={t('purchase.taxAmount')}
            className="px-2 py-1 text-end font-mono tabular-nums text-ink-muted"
          >
            {moneyAlways(tax)}
          </td>
        )}

        <td
          data-label={t('purchase.net')}
          className="px-2 py-1 text-end font-mono tabular-nums"
        >
          {Number.isFinite(net) ? moneyAlways(net) : '—'}
        </td>

        <td className="px-2 py-1 text-end">
          {removable && (
            <button
              type="button"
              onClick={onRemove}
              className="row-action row-action-danger"
            >
              {t('purchase.removeLine')}
            </button>
          )}
        </td>
      </tr>

      {Object.values(errors).some(Boolean) && (
        <tr>
          <td colSpan={span} className="px-2 pb-1">
            <p className="field-message-error">
              {Object.values(errors).filter(Boolean).join(' ')}
            </p>
          </td>
        </tr>
      )}

      {(needsBatch || needsSerials) && (
        <tr className="border-t border-dashed border-line">
          <td colSpan={span} className="px-2 pb-2">
            <div className="flex flex-wrap items-start gap-4">
              {needsBatch && (
                <>
                  <TextField
                    label={t('purchase.batch')}
                    size="sm"
                    value={line.batchNumber}
                    onChange={(value) => onChange({ batchNumber: value })}
                    placeholder={
                      isReturn ? t('purchase.batchExisting') : t('purchase.batchHint')
                    }
                    className="w-full sm:w-40"
                  />

                  {/* An expiry belongs to a batch, and only a purchase can state one:
                      a return is sending back goods whose batch is already on file with
                      its dates, and restating them is refused. */}
                  {!isReturn && (
                    <DateField
                      label={t('purchase.expiresOn')}
                      size="sm"
                      value={line.expiresOn}
                      onChange={(value) => onChange({ expiresOn: value })}
                    />
                  )}
                </>
              )}

              {needsSerials && (
                <TextAreaField
                  label={t('purchase.serialsNamed', {
                    named,
                    wanted: Number.isFinite(wanted) ? wanted : 0,
                  })}
                  size="sm"
                  mono
                  rows={2}
                  value={line.serialNumbers}
                  onChange={(value) => onChange({ serialNumbers: value })}
                  placeholder={t('purchase.serialsHint')}
                  className="w-full sm:w-72"
                />
              )}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

/** Reads a document back, and offers what may still be done to it. */
function DocumentDialog({
  id,
  busy,
  onClose,
  onPost,
  onCancel,
}: {
  readonly id: string;
  readonly busy: boolean;
  readonly onClose: () => void;
  readonly onPost: (id: string) => void;
  readonly onCancel: (id: string, reason: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [reason, setReason] = useState('');

  const query = useQuery<PurchaseInvoiceDetail, ApiError>({
    queryKey: ['purchase-invoice', id],
    queryFn: () => getPurchaseInvoice(id),
  });

  const document = query.data;

  return (
    <Modal title={document?.header.number ?? t('purchase.document')} onClose={onClose}>
      {query.isLoading && (
        <div className="space-y-3" aria-busy="true">
          <div className="grid gap-2 sm:grid-cols-3">
            {Array.from({ length: 6 }, (_, index) => (
              <div key={index}>
                <span className="skeleton block h-2 w-16 rounded" />
                <span className="skeleton mt-1.5 block h-4 w-24 rounded" />
              </div>
            ))}
          </div>
          <span className="skeleton block h-24 w-full rounded-lg" />
        </div>
      )}

      {document && (
        <>
          <div className="grid gap-2 text-sm sm:grid-cols-3">
            <Detail label={t('purchase.documentNumber')} value={document.header.number} />
            <Detail label={t('purchase.date')} value={document.date} />
            {/* The one field on this panel that is a state rather than a fact, so it
                gets the badge the list uses rather than the mono type the rest do. */}
            <div>
              <span className="text-xs text-ink-muted">{t('purchase.status')}</span>
              <div className="mt-0.5">
                <StatusBadge
                  tone={statusTone(document.header.status)}
                  label={statusLabel(document.header.status, t)}
                  struck={document.header.status === PurchaseInvoiceStatus.cancelled}
                />
              </div>
            </div>
            <Detail label={t('purchase.currency')} value={document.currency} />
            <Detail
              label={t('purchase.supplierInvoice')}
              value={document.supplierInvoiceNumber ?? '—'}
            />
            <Detail
              label={t('purchase.supplierInvoiceDate')}
              value={document.supplierInvoiceDate ?? '—'}
            />
          </div>

          <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
            <table className="w-full min-w-[32rem] text-sm">
              <thead className="text-xs text-ink-muted">
                <tr>
                  <th className="px-2 py-1 text-start">#</th>
                  <th className="px-2 py-1 text-start">{t('purchase.batch')}</th>
                  <th className="px-2 py-1 text-end">{t('purchase.quantity')}</th>
                  <th className="px-2 py-1 text-end">{t('purchase.rate')}</th>
                  <th className="px-2 py-1 text-end">{t('purchase.taxable')}</th>
                  <th className="px-2 py-1 text-end">{t('purchase.tax')}</th>
                </tr>
              </thead>
              <tbody>
                {document.lines.map((line) => (
                  <tr key={line.lineNumber} className="border-t border-line">
                    <td className="px-2 py-1">{line.lineNumber}</td>
                    <td className="px-2 py-1 font-mono text-xs">
                      {line.batchNumber ?? '—'}
                    </td>
                    <td className="px-2 py-1 text-end font-mono">{line.quantity}</td>
                    <td className="px-2 py-1 text-end font-mono">
                      {moneyAlways(line.rate)}
                    </td>
                    <td className="px-2 py-1 text-end font-mono">
                      {moneyAlways(line.taxable)}
                    </td>
                    <td className="px-2 py-1 text-end font-mono">
                      {moneyAlways(line.tax)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/*
            The same ladder the draft was entered against, rather than four money
            figures scattered among the dates and the supplier's reference. A bill
            reads down: what the goods came to, what came off, what is taxable, the
            tax, the charges, and what is owed. Gross and discount are added up from
            the lines because the header carries neither — it keeps the taxable value,
            which is what is left after the discount.
          */}
          <div className="line-totals flex flex-wrap items-end justify-end gap-6">
            <DocumentTotals
              gross={document.lines.reduce(
                (running, line) => running + line.quantity * line.rate,
                0,
              )}
              discount={document.lines.reduce(
                (running, line) => running + line.discount,
                0,
              )}
              tax={document.header.tax}
              charges={document.header.chargeTotal}
              rounding={document.header.roundingDifference}
              currency={document.currency}
            />
          </div>

          {/* What posting produced. A purchase leaves two documents on purpose, so the
              receipt is named rather than left for somebody to find in the stock ledger. */}
          {document.stockDocumentId && (
            <p className="text-xs text-ink-muted">{t('purchase.postedProduced')}</p>
          )}

          {document.header.status === PurchaseInvoiceStatus.draft && (
            <>
              {/*
                Where the supplier's invoice number is actually needed. The draft
                was allowed to exist without it; posting is what makes the input
                tax reclaimable, and a purchase posted without the number is one
                somebody has to find again at a return.
              */}
              {document.kind !== PurchaseDocumentKind.return &&
                document.mode === PurchaseTaxMode.tax &&
                !document.supplierInvoiceNumber && (
                  <p className="alert-warn text-xs">
                    {t('purchase.supplierInvoiceMissing')}
                  </p>
                )}

              <div className="flex justify-end">
                <ModalButton primary disabled={busy} onClick={() => onPost(id)}>
                  {t('purchase.post')}
                </ModalButton>
              </div>
            </>
          )}

          {document.header.status === PurchaseInvoiceStatus.posted && (
            <div className="flex flex-wrap items-end justify-end gap-2">
              <TextField
                label={t('purchase.cancelReason')}
                size="sm"
                value={reason}
                onChange={setReason}
              />
              <ModalButton
                disabled={busy || reason.trim() === ''}
                onClick={() => onCancel(id, reason.trim())}
              >
                {t('purchase.cancelDocument')}
              </ModalButton>
            </div>
          )}
        </>
      )}

      <div className="flex justify-end">
        <ModalButton onClick={onClose}>{t('purchase.close')}</ModalButton>
      </div>
    </Modal>
  );
}

function statusLabel(status: number, t: (key: string) => string): string {
  if (status === PurchaseInvoiceStatus.posted) return t('purchase.posted');
  if (status === PurchaseInvoiceStatus.cancelled) return t('purchase.cancelled');

  return t('purchase.draft');
}

/** Posted is done, cancelled did not happen, and a draft is still somebody's to finish. */
function statusTone(status: number): StatusTone {
  if (status === PurchaseInvoiceStatus.posted) return 'success';
  if (status === PurchaseInvoiceStatus.cancelled) return 'danger';

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
