import { isHeldForApproval } from '@/lib/approvals';
import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import clsx from 'clsx';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { Modal, ModalButton } from '@/components/Modal';
import { ReportFrame } from '@/components/ReportFrame';
import { DateField, Field, SelectField, TextField } from '@/components/Form';
import { SearchSelect } from '@/components/SearchSelect';
import { QuickAdd } from '@/components/QuickAdd';
import { StatusBadge, type StatusTone } from '@/components/StatusBadge';
import { IconBox, IconDocument, IconPlus } from '@/components/icons';
import {
  BillCard,
  BillFigures,
  FooterSection,
  InvoiceBody,
  InvoiceCard,
  InvoiceFields,
  InvoiceFooter,
  InvoiceHeader,
  InvoiceLines,
  InvoiceWindow,
  NarrationField,
  RemoveLineButton,
} from '@/components/invoice/InvoiceLayout';
import { ShortcutHints } from '@/components/ShortcutHints';
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
import { listMaster, startingGodown, type WarehouseSummary } from '@/lib/inventory';
import { listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { listProducts, type ProductSummary } from '@/lib/products';
import { addScanned, BarcodeScan } from '@/components/BarcodeScan';
import { listSuppliers, type SupplierSummary } from '@/lib/suppliers';
import { fetchStockValuation, type StockValuationReport } from '@/lib/stock';
import type { PagedResult } from '@/lib/sales';
import { collect, numeric, required, useValidation } from '@/lib/validation';
import { useDirectSave, useGodownsOn, useSettings, useTaxOn } from '@/stores/settings';
import { DocumentType, useNextNumber } from '@/lib/numbering';
import { useChargeSetup } from '@/lib/charges';
import { PrintCenter } from '@/components/print/PrintCenter';
import { printablePurchase } from '@/lib/printing';
import {
  SerialLineSummary,
  SerialPickerDialog,
  useSerialNumbersEnabled,
} from '@/components/SerialNumbers';
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
  updatePurchaseInvoice,
  type PurchaseInvoiceDetail,
  type PurchaseInvoiceSummary,
  type PurchaseLineInput,
} from '@/lib/purchase';
import { getPurchaseOrder, listPurchaseOrders } from '@/lib/orders';
import { ReferenceLoader, type ReferenceSource } from '@/components/ReferenceLoader';
import { moneyAlways } from '@/lib/money';
import { shortcutProps } from '@/lib/shortcuts';

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
  /** What the batch is to sell at, where the firm prices by batch. Blank for the product's rates. */
  sellingRate: string;
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
    sellingRate: '',
    serialNumbers: '',
  };
}

/** A saved line as the entry screen holds it, for a correction to start from. */
function lineFrom(line: PurchaseInvoiceDetail['lines'][number]): DraftLine {
  const headRate = line.components.reduce((sum, head) => sum + head.percentage, 0);
  const taxRate =
    headRate > 0
      ? headRate
      : line.taxable > 0
        ? Math.round((line.tax / line.taxable) * 10000) / 100
        : 0;

  return {
    key: crypto.randomUUID(),
    productId: line.productId,
    quantity: String(line.quantity),
    rate: String(line.rate),
    taxPercentage: String(taxRate),
    discount: String(line.discount),
    batchNumber: line.batchNumber ?? '',
    expiresOn: line.expiresOn ?? '',
    sellingRate: line.sellingRate ? String(line.sellingRate) : '',
    serialNumbers: line.serialNumbers.join('\n'),
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

  // Opened on a particular purchase by another screen - a receipt note raising the
  // purchase that pays for it - and the parameter dropped so a refresh lands on the list.
  const [params, setParams] = useSearchParams();
  useEffect(() => {
    const open = params.get('open');
    if (open) {
      setViewing(open);
      setParams({}, { replace: true });
    }
  }, [params, setParams]);
  // The document being corrected, read back in full so the entry screen starts from it.
  const [editing, setEditing] = useState<PurchaseInvoiceDetail | null>(null);
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

  const taxOn = useTaxOn();
  const allColumns: readonly GridColumn<PurchaseInvoiceSummary>[] = [
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
  const columns = taxOn
    ? allColumns
    : allColumns.filter((column) => column.key !== 'taxable' && column.key !== 'tax');

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
      {(entering || editing) && (
        <EntryDialog
          kind={editing?.kind ?? kind}
          {...(editing ? { editing } : {})}
          onClose={() => {
            setEntering(false);
            setEditing(null);
          }}
          onSaved={(message) => {
            setEntering(false);
            setEditing(null);
            setNotice(message);
            void queryClient.invalidateQueries({ queryKey: ['purchase-invoices'] });
            void queryClient.invalidateQueries({ queryKey: ['purchase-invoice'] });
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
              const held = await cancelPurchaseInvoice(id, reason);
              setViewing(null);

              return held
                ? t('approvals.heldNotice', { number: held.documentNumber })
                : t('purchase.cancelledNotice');
            })
          }
          onEdit={(document) => {
            setViewing(null);
            setEditing(document);
          }}
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
  editing,
  onClose,
  onSaved,
  onError,
}: {
  readonly kind: number;
  /** A saved document being corrected. Absent, a new one is being entered. */
  readonly editing?: PurchaseInvoiceDetail;
  readonly onClose: () => void;
  readonly onSaved: (message: string) => void;
  readonly onError: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const { taxRates, defaultTaxRate, preferredWarehouseId, allowSaveDraft } =
    useSettings();
  const taxOn = useTaxOn();
  const godownsOn = useGodownsOn();
  const directSave = useDirectSave();
  // Direct save makes every purchase final as it is saved, so there is no draft to offer.
  const allowDrafts = allowSaveDraft !== false && !directSave;

  const isReturn = isPurchaseReturn(kind);
  const today = new Date().toISOString().slice(0, 10);

  // A firm that charges no tax enters every purchase without it.
  const [mode, setMode] = useState<number>(
    editing?.mode ?? (taxOn ? PurchaseTaxMode.tax : PurchaseTaxMode.nonTax),
  );
  const [draft, setDraft] = useState<HeaderDraft>({
    date: editing?.date ?? today,
    supplierId: editing?.supplierLedgerId ?? '',
    warehouseId: editing?.warehouseId ?? '',
    supplierInvoiceNumber: editing?.supplierInvoiceNumber ?? '',
    supplierInvoiceDate: editing?.supplierInvoiceDate ?? '',
    returnsInvoiceId: editing?.returnsInvoiceId ?? '',
    narration: editing?.narration ?? '',
  });
  const [editReason, setEditReason] = useState('');
  // The number the series will issue this document, read without reserving it. The
  // field said "issued when the draft is saved" and nothing more; now it says which.
  const { next: nextNumber } = useNextNumber(
    editing
      ? null
      : isReturn
        ? DocumentType.purchaseReturn
        : DocumentType.purchaseInvoice,
    draft.date,
  );
  const [lines, setLines] = useState<readonly DraftLine[]>(() =>
    editing ? editing.lines.map(lineFrom) : [emptyLine(defaultTaxRate)],
  );
  const [charges, setCharges] = useState<readonly DraftCharge[]>(() =>
    (editing?.charges ?? []).map((charge) => ({
      key: crypto.randomUUID(),
      ledgerId: charge.ledgerId,
      amount: String(charge.amount),
    })),
  );
  const [busy, setBusy] = useState(false);
  // Why the last save was refused, shown in the window the user is still in.
  const [failure, setFailure] = useState<string | null>(null);

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

  const chargeSetup = useChargeSetup(isReturn ? 'purchaseReturn' : 'purchase');
  useDefaultCharges(
    isReturn ? 'purchaseReturn' : 'purchase',
    editing ? [] : (ledgers.data ?? []),
    setCharges,
    chargeSetup,
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

    const chosen = startingGodown(warehouses.data, preferredWarehouseId, godownsOn);

    if (chosen) {
      setDraft((current) => ({ ...current, warehouseId: chosen.id }));
    }
  }, [warehouses.data, preferredWarehouseId, draft.warehouseId, godownsOn]);

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
    ...(taxOn
      ? [
          { key: 'taxPercent', label: t('purchase.taxPercent'), defaultOn: true },
          { key: 'taxAmount', label: t('purchase.taxAmount'), defaultOn: false },
        ]
      : []),
    { key: 'net', label: t('purchase.net'), defaultOn: true, fixed: true },
    { key: 'lineTotal', label: t('sales.lineTotal'), defaultOn: true },
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
        tax += taxOn ? (net * Number(line.taxPercentage || 0)) / 100 : 0;
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
  }, [lines, taxOn]);

  const chargesTotal = chargeTotal(charges, ledgers.data ?? [], chargeSetup);

  const change = (index: number, patch: Partial<DraftLine>): void =>
    setLines((previous) =>
      previous.map((line, at) => (at === index ? { ...line, ...patch } : line)),
    );

  const { errors, submit } = useValidation<HeaderDraft>((values) =>
    collect({
      date: required(values.date, t('purchase.dateRequired')),
      supplierId: required(values.supplierId, t('purchase.supplierRequired')),
      warehouseId: required(values.warehouseId, t('purchase.warehouseRequired')),
      // Every purchase names the supplier's own invoice; a return is against one.
      supplierInvoiceNumber: isReturn
        ? null
        : required(values.supplierInvoiceNumber, t('purchase.supplierInvoiceMissing')),
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

  /** Saves the document; with `post`, posts it straight after. */
  const save = async (post = false): Promise<void> => {
    setFailure(null);
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
          taxPercentage: taxOn ? Number(line.taxPercentage || 0) : 0,
          discount: Number(line.discount || 0),
          batchNumber: line.batchNumber.trim() || null,
          expiresOn: line.expiresOn || null,
          sellingRate: Number(line.sellingRate) > 0 ? Number(line.sellingRate) : null,
          serialNumbers: splitSerials(line.serialNumbers),
        }));

      const input = {
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
      };

      if (editing) {
        const header = await updatePurchaseInvoice(editing.header.purchaseInvoiceId, {
          ...input,
          reason: editReason.trim() || null,
        });

        onSaved(
          isHeldForApproval(header)
            ? t('approvals.heldNotice', { number: header.documentNumber })
            : t('purchase.updatedNotice', { number: header.number }),
        );
        return;
      }

      const header = await createPurchaseInvoice(input);

      if (post) {
        try {
          const posted = await postPurchaseInvoice(header.purchaseInvoiceId);
          onSaved(
            t('purchase.postedNotice', {
              number: posted.number,
              stock: posted.stockDocumentNumber,
              total: moneyAlways(posted.total),
            }),
          );
        } catch (failure) {
          // The draft stands; the reason posting refused it is what puts it right.
          // The window closes on it, so saving again cannot enter it twice.
          const api = failure as ApiError;
          onSaved(t('purchase.savedNotice', { number: header.number }));
          onError(
            t('purchase.savedNotPosted', {
              number: header.number,
              reason: api.detail || api.code,
            }),
          );
        }
        return;
      }

      onSaved(t('purchase.savedNotice', { number: header.number }));
    } catch (failure) {
      const api = failure as ApiError;
      setFailure(api.detail || api.code);
    } finally {
      setBusy(false);
    }
  };

  // ---- Loading from another document, found by its number.
  const referenceSources: readonly ReferenceSource[] = isReturn
    ? [
        {
          key: 'purchaseInvoice',
          label: t('references.purchaseInvoice'),
          list: async () =>
            (
              await listPurchaseInvoices(
                {
                  kind: PurchaseDocumentKind.invoice,
                  status: PurchaseInvoiceStatus.posted,
                },
                1,
                100,
              )
            ).items.map((row) => ({
              id: row.purchaseInvoiceId,
              number: row.number,
              date: row.date,
              party: row.supplierName,
              total: row.total,
            })),
        },
      ]
    : [
        {
          key: 'purchaseOrder',
          label: t('references.purchaseOrder'),
          list: async () =>
            (await listPurchaseOrders({}, 1, 100)).items.map((row) => ({
              id: row.purchaseOrderId,
              number: row.number,
              date: row.date,
              party: row.supplierName,
              total: row.total,
            })),
        },
      ];

  const loadReference = async (sourceKey: string, id: string): Promise<void> => {
    if (sourceKey === 'purchaseInvoice') {
      const invoice = await getPurchaseInvoice(id);
      setDraft((current) => ({
        ...current,
        supplierId: invoice.supplierLedgerId,
        warehouseId: invoice.warehouseId,
        returnsInvoiceId: id,
      }));
      setLines(invoice.lines.map(lineFrom));
      return;
    }

    const order = await getPurchaseOrder(id);
    setDraft((current) => ({
      ...current,
      supplierId: order.supplierLedgerId,
      warehouseId: order.warehouseId,
      narration: current.narration || order.header.number,
    }));
    setLines(
      order.lines.map((line) => ({
        ...emptyLine(0),
        productId: line.productId,
        quantity: String(
          line.outstandingQuantity > 0 ? line.outstandingQuantity : line.quantity,
        ),
        rate: String(line.rate),
        discount: String(line.discount),
        taxPercentage: String(
          taxOn && line.taxable > 0
            ? Math.round((line.tax / line.taxable) * 10000) / 100
            : 0,
        ),
      })),
    );
  };

  const set = <TField extends keyof HeaderDraft>(
    field: TField,
    value: HeaderDraft[TField],
  ): void => setDraft((current) => ({ ...current, [field]: value }));

  const documentTitle = editing
    ? t('purchase.editDocument', { number: editing.header.number })
    : isReturn
      ? t('purchase.newReturn')
      : t('purchase.newInvoice');
  const billTotal = totals.taxable + totals.tax + chargesTotal;
  const totalLabel = isReturn ? t('invoice.returnTotal') : t('invoice.billTotal');
  const itemCount = lines.filter((line) => line.productId !== '').length;

  return (
    <InvoiceWindow title={documentTitle} onClose={onClose}>
      <InvoiceHeader
        icon={<IconDocument className="size-6" />}
        title={documentTitle}
        subtitle={
          editing
            ? t('invoice.editing', { number: editing.header.number })
            : isReturn
              ? t('invoice.newPurchaseReturn')
              : t('invoice.newPurchase')
        }
        onCancel={onClose}
        primary={
          editing
            ? {
                label: busy ? t('common.saving') : t('common.save'),
                onClick: () => void save(),
                shortcut: 'save',
                disabled: busy,
              }
            : {
                label: busy ? t('common.saving') : t('purchase.saveAndPost'),
                onClick: () => void save(true),
                shortcut: 'save',
                disabled: busy,
              }
        }
        more={
          !editing && allowDrafts
            ? [
                {
                  label: t('purchase.saveDraft'),
                  onClick: () => void save(),
                  shortcut: 'draft',
                  disabled: busy,
                },
              ]
            : []
        }
        hints={<ShortcutHints actions={['scan', 'add-line', 'charges', 'save']} />}
      />

      <InvoiceBody>
        {failure && (
          <p role="alert" className="alert-error">
            {failure}
          </p>
        )}
        {editing && editing.header.status === PurchaseInvoiceStatus.posted && (
          <div className="alert-warn flex flex-wrap items-end gap-3 text-xs">
            <p className="min-w-0 flex-1">{t('purchase.editPostedHint')}</p>
            <label className="flex flex-col gap-1">
              <span className="font-medium">{t('purchase.editReason')}</span>
              <input
                value={editReason}
                onChange={(event) => setEditReason(event.target.value)}
                className="field-input-sm w-64"
                maxLength={400}
              />
            </label>
          </div>
        )}

        <InvoiceCard>
          {/* Two lines on a wide screen: who, when and the numbers; then what fills
              the lines without typing them - a new purchase from its order, a new
              return from the purchase it sends goods back against, or a scan. */}
          <InvoiceFields>
            <div className="min-w-0 sm:col-span-2">
              <Field label={t('purchase.supplier')} required error={errors['supplierId']}>
                <QuickAdd kind="supplier" onCreated={(id) => set('supplierId', id)}>
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
                </QuickAdd>
              </Field>
            </div>

            <DateField
              label={t('purchase.date')}
              required
              value={draft.date}
              onChange={(value) => set('date', value)}
              error={errors['date']}
            />

            {/*
              The tax mode: whether this document carries tax at all, and under
              which scheme. Defaulted by the server from the firm's regime when
              it is not stated — but a purchase from an unregistered supplier is
              a real and frequent case.
            */}
            {taxOn ? (
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
            ) : null}

            {/*
              The document's own number is not asked for: it is issued by the
              branch's numbering series when the draft is saved. The supplier's
              number, which the firm does not issue, is beside it and mandatory
              on a tax invoice.
            */}
            <Field label={t('purchase.documentNumber')}>
              <input
                value={editing?.header.number ?? nextNumber ?? t('purchase.numberOnSave')}
                disabled
                className="field-input"
              />
            </Field>

            <TextField
              label={t('purchase.supplierInvoice')}
              required={!isReturn}
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

            {godownsOn && (
              <Field
                label={t('purchase.warehouse')}
                required
                error={errors['warehouseId']}
              >
                <QuickAdd kind="warehouse" onCreated={(id) => set('warehouseId', id)}>
                  <SearchSelect
                    value={draft.warehouseId}
                    onChange={(value) => set('warehouseId', value)}
                    options={(warehouses.data ?? []).map((warehouse) => ({
                      value: warehouse.id,
                      label: `${warehouse.code} — ${warehouse.name}`,
                      ...(warehouse.isDefault
                        ? { meta: t('settings.masterDefault') }
                        : {}),
                    }))}
                    invalid={errors['warehouseId'] !== undefined}
                    label={t('purchase.warehouse')}
                    placeholder={t('purchase.chooseWarehouse')}
                  />
                </QuickAdd>
              </Field>
            )}

            {isReturn && (
              <Field label={t('purchase.againstInvoice')} className="sm:col-span-2">
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

            <div className="min-w-0 sm:col-span-2" {...shortcutProps('scan')}>
              <BarcodeScan
                asField
                products={products.data ?? []}
                onScan={(product) => {
                  // At the product's cost and its own tax rate, as choosing it in
                  // the picker does.
                  const next = addScanned(lines, product, () => ({
                    ...emptyLine(defaultTaxRate),
                    productId: product.id,
                    rate: product.cost ? String(product.cost) : '',
                    ...(product.taxPercentage != null
                      ? { taxPercentage: String(product.taxPercentage) }
                      : {}),
                  }));
                  setLines(next.lines);
                  return next.quantity;
                }}
              />
            </div>
            {!editing && (
              <ReferenceLoader
                asField
                sources={referenceSources}
                onLoad={loadReference}
                className="sm:col-span-2 lg:col-span-3"
              />
            )}
          </InvoiceFields>
        </InvoiceCard>

        <InvoiceCard
          fill
          icon={<IconBox className="size-5" />}
          title={t('invoice.items')}
          meta={t('invoice.itemCount', { count: itemCount })}
          actions={columns.picker}
        >
          {errors['lines'] && (
            <p className="field-message-error mb-2">{errors['lines']}</p>
          )}

          <InvoiceLines>
            <table className="line-table sm:min-w-[40rem] lg:min-w-[44rem]">
              <thead className="sticky top-0 z-10 bg-surface-2 text-[13px] text-ink-muted">
                <tr>
                  <th className="rounded-s-lg px-3 py-2.5 text-start font-medium">
                    {t('purchase.product')}
                  </th>
                  <ProductDetailHeaders shows={columns.shows} labels={t} />
                  <th className="px-2 py-2.5 text-end font-medium">
                    {t('purchase.quantity')}
                  </th>
                  <th className="px-2 py-2.5 text-end font-medium">
                    {t('purchase.rate')}
                  </th>
                  {columns.shows('discount') && (
                    <th className="px-2 py-2.5 text-end font-medium">
                      {t('purchase.discount')}
                    </th>
                  )}
                  {columns.shows('taxPercent') && (
                    <th className="px-2 py-2.5 text-end font-medium">
                      {t('purchase.taxPercent')}
                    </th>
                  )}
                  {columns.shows('taxAmount') && (
                    <th className="px-2 py-2.5 text-end font-medium">
                      {t('purchase.taxAmount')}
                    </th>
                  )}
                  <th className="px-2 py-2.5 text-end font-medium">
                    {t('purchase.net')}
                  </th>
                  {columns.shows('lineTotal') && (
                    <th className="px-2 py-2.5 text-end font-medium">
                      {t('sales.lineTotal')}
                    </th>
                  )}
                  <th className="line-action-column rounded-e-lg" />
                </tr>
              </thead>

              <tbody>
                {lines.map((line, index) => (
                  <LineRow
                    key={line.key}
                    line={line}
                    isReturn={isReturn}
                    warehouseId={draft.warehouseId}
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

            <button
              {...shortcutProps('add-line')}
              type="button"
              onClick={() =>
                setLines((previous) => [...previous, emptyLine(defaultTaxRate)])
              }
              className="btn-secondary mt-3 mb-1"
            >
              <IconPlus className="size-4" />
              {t('invoice.addItem')}
            </button>
          </InvoiceLines>
        </InvoiceCard>

        {/* Along the foot: the narration, and the bill picked out - its total, the
            figures it is built from, and the additional ledgers typed in beside
            them. */}
        <InvoiceFooter className="lg:grid-cols-[minmax(0,1fr)_minmax(0,1.6fr)]">
          <FooterSection
            icon={<IconDocument className="size-[18px]" />}
            title={t('invoice.narration')}
          >
            <NarrationField
              value={draft.narration}
              onChange={(value) => set('narration', value)}
              label={t('invoice.narration')}
              placeholder={t('invoice.narration')}
            />
          </FooterSection>

          <BillCard
            label={totalLabel}
            total={moneyAlways(billTotal)}
            figures={
              <BillFigures
                gross={totals.gross}
                discount={totals.discount}
                tax={totals.tax}
                showTax={taxOn}
                money={moneyAlways}
              />
            }
            charges={
              <ChargesPanel
                inline
                charges={charges}
                ledgers={ledgers.data ?? []}
                onChange={setCharges}
                setup={chargeSetup}
                nonTax={mode === PurchaseTaxMode.nonTax}
              />
            }
          />
        </InvoiceFooter>
      </InvoiceBody>
    </InvoiceWindow>
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
  warehouseId,
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
  /** Where the goods go to or come from, for the units a return can send back. */
  readonly warehouseId: string;
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

  const taxOn = useTaxOn();
  const product = products.find((candidate) => candidate.id === line.productId);
  const net = lineNet(line);
  const tax = (net * Number(line.taxPercentage || 0)) / 100;

  const wanted = Number(line.quantity);
  const needsBatch = product?.tracksBatches === true;
  // Offered only where the firm has switched serial numbers on, and always optional:
  // a purchase of five may name five, four, two or none of them.
  const serialsOn = useSerialNumbersEnabled();
  const needsSerials = serialsOn && product?.tracksSerialNumbers === true;
  const [pickerOpen, setPickerOpen] = useState(false);

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
      'lineTotal',
    ].filter((key) => columns.shows(key)).length;

  return (
    <>
      <tr className="border-t border-line">
        <td data-label={t('purchase.product')} className="min-w-40 px-2 py-1 lg:min-w-64">
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
                // The product's own rate from the tax rate master, where it has one.
                ...(products.find((candidate) => candidate.id === value)?.taxPercentage !=
                null
                  ? {
                      taxPercentage: String(
                        products.find((candidate) => candidate.id === value)
                          ?.taxPercentage,
                      ),
                    }
                  : {}),
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
                'field-input-sm ms-auto w-20 text-end tabular-nums lg:w-24',
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
            className="px-2 py-1 text-end tabular-nums text-ink-muted"
          >
            {moneyAlways(tax)}
          </td>
        )}

        <td data-label={t('purchase.net')} className="px-2 py-1 text-end tabular-nums">
          {Number.isFinite(net) ? moneyAlways(net) : '—'}
        </td>

        {columns.shows('lineTotal') && (
          <td
            data-label={t('sales.lineTotal')}
            className="px-2 py-1 text-end font-semibold tabular-nums"
          >
            {Number.isFinite(net) ? moneyAlways(net + (taxOn ? tax : 0)) : '—'}
          </td>
        )}

        <td className="px-2 py-1 text-end">
          {removable && (
            <RemoveLineButton onClick={onRemove} label={t('purchase.removeLine')} />
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

                  {/* What the batch sells at, offered by the sales screen when the
                      counter sells at batch-wise rates. */}
                  {!isReturn && (
                    <TextField
                      label={t('purchase.batchSellingRate')}
                      size="sm"
                      value={line.sellingRate}
                      onChange={(value) =>
                        onChange({ sellingRate: value.replace(/[^0-9.]/g, '') })
                      }
                      placeholder={t('purchase.batchSellingRateHint')}
                      className="w-full sm:w-36"
                    />
                  )}
                </>
              )}

              {needsSerials && (
                <div className="self-end">
                  <SerialLineSummary
                    chosen={splitSerials(line.serialNumbers)}
                    quantity={wanted}
                    onOpen={() => setPickerOpen(true)}
                  />
                </div>
              )}
            </div>
          </td>
        </tr>
      )}

      {pickerOpen && product && (
        <SerialPickerDialog
          productId={product.id}
          productLabel={`${product.code} — ${product.description}`}
          warehouseId={warehouseId}
          // A purchase brings new numbers in; a return to the supplier sends back
          // units that are on the shelf.
          direction={isReturn ? 'out' : 'in'}
          chosen={splitSerials(line.serialNumbers)}
          quantity={wanted}
          onClose={() => setPickerOpen(false)}
          onDone={(serials, quantity) => {
            onChange({ serialNumbers: serials.join('\n'), quantity: String(quantity) });
            setPickerOpen(false);
          }}
        />
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
  onEdit,
}: {
  readonly id: string;
  readonly busy: boolean;
  readonly onClose: () => void;
  readonly onPost: (id: string) => void;
  readonly onCancel: (id: string, reason: string) => void;
  readonly onEdit: (document: PurchaseInvoiceDetail) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [reason, setReason] = useState('');
  const [printing, setPrinting] = useState(false);

  const query = useQuery<PurchaseInvoiceDetail, ApiError>({
    queryKey: ['purchase-invoice', id],
    queryFn: () => getPurchaseInvoice(id),
  });

  const document = query.data;

  if (printing && document) {
    return (
      <Modal
        title={document.header.number}
        size="fullscreen"
        onClose={() => setPrinting(false)}
      >
        <PurchasePrint document={document} onDone={() => setPrinting(false)} />
      </Modal>
    );
  }

  return (
    <Modal title={document?.header.number ?? t('purchase.document')} onClose={onClose}>
      {query.isLoading && (
        <div className="space-y-3" aria-busy="true">
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
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
          <div className="grid grid-cols-1 gap-2 text-sm sm:grid-cols-3">
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
            <table className="line-table sm:min-w-[32rem]">
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
                    <td data-label="#" className="px-2 py-1">
                      {line.lineNumber}
                    </td>
                    <td
                      data-label={t('purchase.batch')}
                      className="px-2 py-1 tabular-nums text-xs"
                    >
                      {line.batchNumber ?? '—'}
                    </td>
                    <td
                      data-label={t('purchase.quantity')}
                      className="px-2 py-1 text-end tabular-nums"
                    >
                      {line.quantity}
                    </td>
                    <td
                      data-label={t('purchase.rate')}
                      className="px-2 py-1 text-end tabular-nums"
                    >
                      {moneyAlways(line.rate)}
                    </td>
                    <td
                      data-label={t('purchase.taxable')}
                      className="px-2 py-1 text-end tabular-nums"
                    >
                      {moneyAlways(line.taxable)}
                    </td>
                    <td
                      data-label={t('purchase.tax')}
                      className="px-2 py-1 text-end tabular-nums"
                    >
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

          <div className="flex justify-end gap-2">
            <ModalButton onClick={() => setPrinting(true)}>
              {t('printing.print')}
            </ModalButton>
            {document.header.status !== PurchaseInvoiceStatus.cancelled && (
              <ModalButton disabled={busy} onClick={() => onEdit(document)}>
                {t('purchase.edit')}
              </ModalButton>
            )}
          </div>

          {document.header.status !== PurchaseInvoiceStatus.cancelled && (
            <div className="flex flex-wrap items-end justify-end gap-2">
              <TextField
                label={t('purchase.cancelReason')}
                className="max-sm:min-w-0 max-sm:flex-1"
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

      <div className="flex justify-end pt-3 border-t border-line/60">
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
      <div className="tabular-nums text-ink">{value}</div>
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
          'field-input-sm ms-auto w-20 text-end tabular-nums lg:w-24',
          error && 'field-invalid',
        )}
      />
    </td>
  );
}

/** A purchase on paper, with what it names looked up. */
function PurchasePrint({
  document,
  onDone,
}: {
  readonly document: PurchaseInvoiceDetail;
  readonly onDone: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();

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

  if (suppliers.isPending || warehouses.isPending || products.isPending) {
    return <p className="text-sm text-ink-muted">{t('common.loading')}</p>;
  }

  return (
    <PrintCenter
      document={printablePurchase(
        document,
        {
          suppliers: suppliers.data ?? [],
          products: products.data ?? [],
          warehouses: warehouses.data ?? [],
        },
        {
          taxable: t('printing.taxable'),
          tax: t('printing.taxTotal'),
          charges: t('printing.charges'),
          roundOff: t('printing.roundOff'),
        },
      )}
      onDone={onDone}
    />
  );
}
