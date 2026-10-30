import { useEffect, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { CreditLimitPolicy, getPolicies, type FirmPolicies } from '@/lib/policies';
import { isHeldForApproval } from '@/lib/approvals';
import clsx from 'clsx';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { Modal, ModalButton } from '@/components/Modal';
import { ReportFrame } from '@/components/ReportFrame';
import { DateField, Field as FormField, SelectField, TextField } from '@/components/Form';
import { SearchSelect } from '@/components/SearchSelect';
import { QuickAdd } from '@/components/QuickAdd';
import { AdditionalLedgerList } from '@/components/AdditionalLedgerList';
import { StatusBadge, type StatusTone } from '@/components/StatusBadge';
import {
  IconBox,
  IconCreditCard,
  IconDocument,
  IconPercent,
  IconPlus,
  IconPrinter,
  IconUser,
} from '@/components/icons';
import {
  BillCard,
  CompactField,
  FooterSection,
  InvoiceBody,
  InvoiceCard,
  InvoiceFields,
  InvoiceFooter,
  InvoiceActions,
  InvoiceHeader,
  InvoiceLines,
  InvoiceWindow,
  NarrationInput,
  PartyCard,
  RemoveLineButton,
  ValueField,
} from '@/components/invoice/InvoiceLayout';
import { ScanField } from '@/components/BarcodeScan';
import { PrintCenter } from '@/components/print/PrintCenter';
import { printableSale } from '@/lib/printing';
import {
  DocumentTotals,
  ProductDetailCells,
  ProductDetailHeaders,
  productDetailColumns,
  productOption,
  stockByProduct,
  useLineColumns,
  type LineColumn,
} from '@/components/DocumentLines';
import { LedgerKind, listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { fetchStockValuation, type StockValuationReport } from '@/lib/stock';
import { collect, numeric, required, useValidation } from '@/lib/validation';
import {
  useDirectSave,
  useGodownsOn,
  useSettings,
  useTaxOn,
  type SalesRateType,
} from '@/stores/settings';
import { DocumentType, useNextNumber } from '@/lib/numbering';
import { chargeAmounts, useChargeSetup } from '@/lib/charges';
import {
  SerialLineSummary,
  SerialPickerDialog,
  useSerialNumbersEnabled,
  type SerialDirection,
} from '@/components/SerialNumbers';
import type { ApiError } from '@/lib/api';
import { listCustomers, type CustomerSummary } from '@/lib/customers';
import { listMaster, startingGodown, type WarehouseSummary } from '@/lib/inventory';
import { listProducts, type ProductSummary } from '@/lib/products';
import {
  fetchProductBatches,
  fetchProductSerials,
  findSerial,
  SerialStatus,
  type BatchStockRow,
  type SerialNumberView,
} from '@/lib/stock';
import {
  cancelSalesInvoice,
  createSalesInvoice,
  getCustomerAccount,
  getSalesInvoice,
  getSalesRateHints,
  isReturn,
  listSalesInvoices,
  postSalesInvoice,
  SalesDocumentKind,
  SalesInvoiceStatus,
  SalesPaymentMode,
  paidThroughBank,
  TaxMode,
  updateSalesInvoice,
  type CustomerAccount,
  type PagedResult,
  type SalesInvoiceDetail,
  type SalesInvoiceLineDetail,
  type SalesInvoiceSummary,
  type SalesLineInput,
  type SalesChargeInput,
} from '@/lib/sales';
import { getSalesOrder, listSalesOrders, SalesOrderKind } from '@/lib/orders';
import {
  getPurchaseInvoice,
  listPurchaseInvoices,
  PurchaseDocumentKind,
} from '@/lib/purchase';
import { ReferenceLoader, type ReferenceSource } from '@/components/ReferenceLoader';
import { moneyAlways } from '@/lib/money';
import { shortcutProps } from '@/lib/shortcuts';
import { ShortcutHints } from '@/components/ShortcutHints';

const PAGE_SIZE = 25;

/** A line as the screen holds it, before it is worth sending. */
interface DraftLine {
  productId: string;
  quantity: string;
  freeQuantity?: string;
  rate: string;
  inclusiveRate?: string;
  taxPercentage: string;
  discount: string;
  /** The batch sold, where the product is tracked in batches. */
  batchNumber: string;
  /** The batch as a saved line remembers it, until somebody picks another. */
  batchId?: string | undefined;
  expiresOn?: string;
  /** The units sold, where the product is tracked by serial number. */
  serialNumbers: readonly string[];
}

/** Rounds to the currency's two places, half away from zero, as the server's Money does. */
function roundMoney(value: number): number {
  return (Math.sign(value) * Math.round(Math.abs(value) * 100 + 1e-9)) / 100;
}

/**
 * The rate a line starts at under a tax mode.
 *
 * Non-tax is nothing; a taxed sale starts at the firm's own default rate from
 * Settings rather than a five written into the screen, so a firm whose VAT is not
 * five per cent is not correcting every line.
 */
function modeTaxRate(mode: 'NT' | 'TAX' | 'GST', defaultTaxRate: number): number {
  return mode === 'NT' ? 0 : mode === 'TAX' ? defaultTaxRate : 18;
}

/**
 * The rate a line starts at: on a tax invoice, the product's own rate from the tax rate
 * master where it has one, otherwise the mode's.
 */
function lineTaxRate(
  mode: 'NT' | 'TAX' | 'GST',
  product: ProductSummary | undefined,
  defaultTaxRate: number,
): number {
  return mode === 'TAX' && product?.taxPercentage != null
    ? product.taxPercentage
    : modeTaxRate(mode, defaultTaxRate);
}

/**
 * What a product sells at from its own master under a rate type. The rate types that
 * look elsewhere - the customer's quotation or last sale, the batch - start from retail,
 * which stands where there is nothing to take.
 */
function masterRate(product: ProductSummary, rateType: SalesRateType): number {
  if (rateType === 'wholesale') return product.wholesaleRate || product.retailRate;
  if (rateType === 'mrp') return product.maximumRetailPrice || product.retailRate;

  return product.retailRate;
}

const emptyLine: DraftLine = {
  productId: '',
  quantity: '1',
  freeQuantity: '0',
  rate: '',
  inclusiveRate: '',
  taxPercentage: '0',
  discount: '0',
  batchNumber: '',
  expiresOn: '',
  serialNumbers: [],
};

/** A saved line as the entry screen holds it, for a correction to start from. */
function linesFrom(line: SalesInvoiceLineDetail): DraftLine {
  // The rate the line was taxed at: its heads added up, CGST and SGST making the GST
  // rate between them; worked back from the amounts where it carried no heads.
  const headRate = line.components.reduce((sum, head) => sum + head.percentage, 0);
  const taxRate =
    headRate > 0
      ? headRate
      : line.taxable > 0
        ? Math.round((line.tax / line.taxable) * 10000) / 100
        : 0;

  return {
    ...emptyLine,
    productId: line.productId,
    quantity: String(line.quantity),
    rate: String(line.rate),
    inclusiveRate: (line.rate * (1 + taxRate / 100)).toFixed(2),
    taxPercentage: String(taxRate),
    discount: String(line.discount),
    batchId: line.batchId ?? undefined,
    serialNumbers: line.serialNumbers ?? [],
  };
}

/**
 * Sales invoices and credit notes.
 *
 * One screen for both, because they are one kind of document: an invoice and a return
 * differ in which way the goods and the money move and not at all in shape, so two
 * screens would be two copies of the same grid and the second would drift. The kind is
 * chosen when a document is entered and shown as a badge in the list.
 *
 * Entering and posting are separate here as they are on the server. A draft has moved
 * nothing and can be abandoned; posting issues the stock, raises or credits the debt and
 * writes the books in one transaction, and what it produced is reported back rather than
 * left to be looked up.
 */
export function SalesPage({
  kind = SalesDocumentKind.invoice,
}: {
  readonly kind?: number;
} = {}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  const isSalesReturn = isReturn(kind);
  const today = new Date().toISOString().slice(0, 10);
  const monthStart = `${today.slice(0, 7)}-01`;

  const [from, setFrom] = useState(monthStart);
  const [to, setTo] = useState(today);
  const kindFilter = isSalesReturn ? SalesDocumentKind.return : SalesDocumentKind.invoice;
  const [statusFilter, setStatusFilter] = useState<number | ''>('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const [entering, setEntering] = useState(false);
  const [viewing, setViewing] = useState<{ id: string; print?: boolean } | null>(null);

  // Opened on a particular invoice by another screen - a delivery note raising the
  // invoice that bills it - and the parameter dropped so a refresh lands on the list.
  const [params, setParams] = useSearchParams();
  useEffect(() => {
    const open = params.get('open');
    if (open) {
      setViewing({ id: open });
      setParams({}, { replace: true });
    }
  }, [params, setParams]);
  // The document being corrected, read back in full so the entry screen can start
  // from what it says.
  const [editing, setEditing] = useState<SalesInvoiceDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const filter = { from, to, kind: kindFilter, status: statusFilter, search };

  const query = useQuery<PagedResult<SalesInvoiceSummary>, ApiError>({
    queryKey: ['sales-invoices', from, to, kindFilter, statusFilter, search, page],
    queryFn: () => listSalesInvoices(filter, page, PAGE_SIZE),
  });

  const mutation = useMutation<string | null, ApiError, () => Promise<string | null>>({
    mutationFn: (action) => action(),
    onSuccess: async (message) => {
      setError(null);
      setNotice(message);
      window.setTimeout(() => setNotice(null), 5000);

      await queryClient.invalidateQueries({ queryKey: ['sales-invoices'] });
      await queryClient.invalidateQueries({ queryKey: ['sales-invoice'] });
    },
    // The server owns the rules — short stock, a missing tax account, a bill somebody
    // has paid against — so its own message is shown rather than one guessed at here.
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const run = (action: () => Promise<string | null>): void => mutation.mutate(action);

  // Any change of filter goes back to the first page: staying on page four of a list
  // that now has two would show an empty screen and look broken.
  const narrow =
    <T,>(set: (value: T) => void) =>
    (value: T): void => {
      set(value);
      setPage(1);
    };

  const taxOn = useTaxOn();
  const allColumns: readonly GridColumn<SalesInvoiceSummary>[] = [
    { key: 'number', header: t('sales.number'), value: (row) => row.number },
    {
      key: 'reference',
      header: t('sales.referenceNo'),
      value: (row) => row.referenceNumber ?? '—',
    },
    { key: 'date', header: t('sales.date'), value: (row) => row.date },
    {
      key: 'time',
      header: t('sales.time'),
      value: (row) => timeOfSale(row.createdAtUtc),
    },
    { key: 'customer', header: t('sales.customer'), value: (row) => row.customerName },
    {
      key: 'paymentMode',
      header: t('sales.paymentMode'),
      value: (row) => paymentModeLabel(row.paymentMode, t),
      render: (row) => (
        <StatusBadge
          tone={
            row.paymentMode === SalesPaymentMode.cash
              ? 'success'
              : paidThroughBank(row.paymentMode)
                ? 'info'
                : 'neutral'
          }
          label={paymentModeLabel(row.paymentMode, t)}
        />
      ),
    },
    {
      key: 'amountReceived',
      header: t('sales.amountReceived'),
      value: (row) => row.amountReceived,
      numeric: true,
      render: (row) => (row.amountReceived > 0 ? moneyAlways(row.amountReceived) : ''),
      hiddenByDefault: true,
    },
    {
      key: 'paymentReference',
      header: t('sales.paymentReference'),
      value: (row) => row.paymentReference ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'salesman',
      header: t('sales.salesman'),
      value: (row) => row.salesmanName ?? '',
    },
    {
      key: 'billingMan',
      header: t('sales.billingman'),
      value: (row) => row.billingManName ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'lines',
      header: t('sales.lines'),
      value: (row) => row.lineCount,
      numeric: true,
    },
    {
      key: 'taxable',
      header: t('sales.taxable'),
      value: (row) => row.taxable,
      numeric: true,
    },
    {
      key: 'discount',
      header: t('sales.itemDiscount'),
      value: (row) => row.discount,
      numeric: true,
      hiddenByDefault: true,
    },
    { key: 'tax', header: t('sales.tax'), value: (row) => row.tax, numeric: true },
    {
      key: 'charges',
      header: t('sales.additionalLedgersTotal'),
      value: (row) => row.chargeTotal,
      numeric: true,
      hiddenByDefault: true,
    },
    { key: 'total', header: t('sales.total'), value: (row) => row.total, numeric: true },
    {
      key: 'narration',
      header: t('sales.narration'),
      value: (row) => row.narration ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'status',
      header: t('sales.status'),
      value: (row) => statusLabel(row.status, t),
      render: (row) => (
        <StatusBadge
          tone={statusTone(row.status)}
          label={statusLabel(row.status, t)}
          struck={row.status === SalesInvoiceStatus.cancelled}
        />
      ),
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <div className="flex items-center gap-1.5">
          <button
            type="button"
            onClick={() => setViewing({ id: row.salesInvoiceId, print: false })}
            className="row-action row-action-neutral"
          >
            {t('sales.open')}
          </button>
          <button
            type="button"
            onClick={() => setViewing({ id: row.salesInvoiceId, print: true })}
            className="row-action row-action-neutral flex items-center gap-1 text-primary-600 dark:text-primary-400 hover:text-primary-700 font-medium"
            title={t('sales.print') || 'Print Invoice'}
          >
            <IconPrinter className="size-3.5" />
            <span>{t('sales.print') || 'Print'}</span>
          </button>
        </div>
      ),
    },
  ]; // Without tax, the taxable and tax columns would only ever read the same as the
  // total and nothing.
  const columns = taxOn
    ? allColumns
    : allColumns.filter((column) => column.key !== 'taxable' && column.key !== 'tax');

  const controls = (
    <div className="filter-grid flex flex-wrap items-end gap-3 w-full">
      <TextField
        label={t('sales.search')}
        type="search"
        value={search}
        onChange={(value) => narrow(setSearch)(value)}
        placeholder={t('sales.searchHint')}
        size="sm"
        className="min-w-48 flex-1"
      />

      <SelectField<number | ''>
        label={t('sales.status')}
        value={statusFilter}
        onChange={narrow<number | ''>(setStatusFilter)}
        size="sm"
        options={[
          { value: '', label: t('sales.allStatuses') },
          { value: SalesInvoiceStatus.draft, label: t('sales.draft') },
          { value: SalesInvoiceStatus.posted, label: t('sales.posted') },
          { value: SalesInvoiceStatus.cancelled, label: t('sales.cancelled') },
        ]}
      />

      <div className="ms-auto flex items-end gap-2">
        <DateField
          label={t('sales.from')}
          value={from}
          onChange={narrow(setFrom)}
          size="sm"
        />
        <DateField label={t('sales.to')} value={to} onChange={narrow(setTo)} size="sm" />
      </div>
    </div>
  );

  return (
    <>
      <ReportFrame
        title={
          isSalesReturn
            ? t('sales.salesReturns') || 'Sales Returns'
            : t('sales.salesInvoices') || 'Sales Invoices'
        }
        controls={controls}
        query={query}
      >
        {(result) => (
          <div className="space-y-3">
            {error && <p className="alert-error">{error}</p>}

            {notice && <p className="alert-success">{notice}</p>}

            <DataGrid
              gridKey={isSalesReturn ? 'sales-returns' : 'sales-invoices'}
              rows={result.items}
              columns={columns}
              rowKey={(row) => row.salesInvoiceId}
              emptyMessage={
                isSalesReturn
                  ? t('sales.noReturns') || 'No sales returns in this range.'
                  : t('sales.none')
              }
              actions={
                <GridAction
                  label={
                    isSalesReturn
                      ? t('sales.newSalesReturn') || 'New return'
                      : t('sales.newInvoice')
                  }
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
          initialKind={editing?.kind ?? kindFilter}
          {...(editing ? { editing } : {})}
          onClose={() => {
            setEntering(false);
            setEditing(null);
          }}
          onSaved={(message, printId) => {
            setEntering(false);
            setEditing(null);
            setNotice(message);
            // Save and print: the posted document opens straight onto its print.
            if (printId) setViewing({ id: printId, print: true });
            void queryClient.invalidateQueries({ queryKey: ['sales-invoices'] });
            void queryClient.invalidateQueries({ queryKey: ['sales-invoice'] });
          }}
        />
      )}

      {viewing && (
        <DocumentDialog
          id={viewing.id}
          initialPrint={viewing.print ?? false}
          busy={mutation.isPending}
          onClose={() => setViewing(null)}
          onPost={(id) =>
            run(async () => {
              const posted = await postSalesInvoice(id);

              return t('sales.postedNotice', {
                number: posted.number,
                stock: posted.stockDocumentNumber,
                total: moneyAlways(posted.total),
              });
            })
          }
          onCancel={(id, reason) =>
            run(async () => {
              const held = await cancelSalesInvoice(id, reason);
              setViewing(null);

              return held
                ? t('approvals.heldNotice', { number: held.documentNumber })
                : t('sales.cancelledNotice');
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

interface AdditionalLedgerItem {
  readonly id: string;
  readonly ledgerId: string;
  readonly isAddition: boolean;
  readonly amount: string;
}

/** Enters a draft: the header, then the lines. */
function EntryDialog({
  initialKind = SalesDocumentKind.invoice,
  editing,
  onClose,
  onSaved,
}: {
  readonly initialKind?: number;
  /** A saved document being corrected. Absent, a new one is being entered. */
  readonly editing?: SalesInvoiceDetail;
  readonly onClose: () => void;
  /** With the document's id when it should open for printing. */
  readonly onSaved: (message: string, printId?: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();
  const taxOn = useTaxOn();
  const godownsOn = useGodownsOn();
  const directSave = useDirectSave();
  const preferredWarehouseId = settings.preferredWarehouseId;

  const today = new Date().toISOString().slice(0, 10);

  const [kind] = useState<number>(initialKind);
  const [date, setDate] = useState(editing?.date ?? today);

  // The number the server will give this document, read from its own series. A
  // document being corrected already has its number and keeps it.
  const { next: nextNumber } = useNextNumber(
    editing
      ? null
      : isReturn(kind)
        ? DocumentType.salesReturn
        : DocumentType.salesInvoice,
    date,
  );
  const nextInvoiceNo =
    editing?.header.number ?? nextNumber ?? t('numbering.issuedOnSave');
  const [customerId, setCustomerId] = useState(
    editing?.customerLedgerId ?? (settings.defaultCustomerId || ''),
  );
  const [warehouseId, setWarehouseId] = useState(editing?.warehouseId ?? '');
  const [reference, setReference] = useState(editing?.referenceNumber ?? '');
  const [returnsInvoiceId, setReturnsInvoiceId] = useState(
    editing?.returnsInvoiceId ?? '',
  );
  const [editReason, setEditReason] = useState('');
  // Cash unless the counter's own default is credit: most of what a shop sells is
  // paid for there and then.
  const [paymentMode, setPaymentMode] = useState<number>(
    editing?.paymentMode ??
      (settings.defaultPaymentMode === 'Credit'
        ? SalesPaymentMode.credit
        : SalesPaymentMode.cash),
  );
  // Employee ledgers. A default saved before these were pickers holds a name rather
  // than an id, and is dropped below for want of an employee to match.
  const [salesman, setSalesman] = useState<string>(
    editing ? (editing.salesmanLedgerId ?? '') : settings.defaultSalesman,
  );
  const [billingman, setBillingman] = useState<string>(
    editing ? (editing.billingManLedgerId ?? '') : settings.defaultBillingman,
  );
  const [taxMode, setTaxMode] = useState<'NT' | 'TAX' | 'GST'>(() => {
    const fallback = (settings.defaultTaxMode as 'NT' | 'TAX' | 'GST') || 'TAX';

    // A firm that charges no tax enters every sale without it.
    if (!taxOn) return 'NT';
    if (!editing) return fallback;

    return editing.mode === TaxMode.nonTax ? 'NT' : fallback === 'NT' ? 'TAX' : fallback;
  });
  const [rateType, setRateType] = useState<SalesRateType>(
    settings.defaultSalesRateType || 'retail',
  );
  const reverseCalc = settings.enableReverseCalculation ?? false;
  const autoBatch = settings.enableAutoBatch ?? true;
  const enableFreeQty = settings.enableFreeQuantity ?? true;
  const enableDiscount = settings.enableItemDiscount ?? true;

  const [barcodeInput, setBarcodeInput] = useState('');
  const [barcodeNotice, setBarcodeNotice] = useState<string | null>(null);

  const [narration, setNarration] = useState(editing?.narration ?? '');
  // Blank means the whole bill for a cash or online sale and nothing for one on account;
  // a figure typed here is a part payment. A posted sale being corrected shows what it
  // took, unless that was the whole of it.
  const [paidAmount, setPaidAmount] = useState<string>(() =>
    editing && editing.amountReceived > 0 && editing.amountReceived < editing.header.total
      ? String(editing.amountReceived)
      : '',
  );
  const [cashGiven, setCashGiven] = useState<string>('');
  // Where the money paid with the sale goes: the settings' default cash or bank
  // account for the mode, or the one a corrected document already named.
  const [receivedInto] = useState(editing?.receivedIntoLedgerId ?? '');
  const [paymentReference, setPaymentReference] = useState(
    editing?.paymentReference ?? '',
  );
  const [additionalLedgers, setAdditionalLedgers] = useState<
    readonly AdditionalLedgerItem[]
  >(() => {
    if (editing) {
      return editing.charges.map((charge) => ({
        id: Math.random().toString(36).slice(2, 9),
        ledgerId: charge.ledgerId,
        isAddition: charge.isAddition,
        amount: String(charge.amount),
      }));
    }

    // A return starts with none; a sale with the amounts Settings gives its additional
    // ledgers. One no longer set up for a sale is dropped when the setup arrives.
    if (isReturn(initialKind)) return [];
    return (settings.defaultAdditionalLedgers ?? [])
      .filter(
        (l) =>
          l.transactionType === 'Sales' &&
          l.isDefaultActive &&
          Number(l.defaultAmount) > 0,
      )
      .map((l) => ({
        id: Math.random().toString(36).slice(2, 9),
        ledgerId: l.ledgerId,
        isAddition: l.isAddition,
        amount: l.defaultAmount,
      }));
  });

  /** Sets an additional ledger's amount, putting the ledger on the bill if it is not yet. */
  const setChargeAmount = (ledgerId: string, amount: string, adds: boolean): void =>
    setAdditionalLedgers((rows) =>
      rows.some((row) => row.ledgerId === ledgerId)
        ? rows.map((row) => (row.ledgerId === ledgerId ? { ...row, amount } : row))
        : [
            ...rows,
            {
              id: Math.random().toString(36).slice(2, 9),
              ledgerId,
              isAddition: adds,
              amount,
            },
          ],
    );

  // Started at the mode's rate. It used to start at '0', and choosing a product only
  // filled the rate in when the line had none — but '0' is a string, so it always
  // had one, and every line of a taxed invoice went out at zero per cent.
  const [lines, setLines] = useState<readonly DraftLine[]>(() =>
    editing
      ? editing.lines.map((line) => linesFrom(line))
      : [
          {
            ...emptyLine,
            taxPercentage: String(
              modeTaxRate(
                (settings.defaultTaxMode as 'NT' | 'TAX' | 'GST') || 'TAX',
                settings.defaultTaxRate,
              ),
            ),
          },
        ],
  );
  const [busy, setBusy] = useState(false);
  // Why the last save was refused, shown in the window the user is still in.
  const [failure, setFailure] = useState<string | null>(null);

  // Alt+A, or the Add line button, leaves the cursor in the new line's product.
  const linesBody = useRef<HTMLTableSectionElement>(null);
  const focusNewLine = useRef(false);
  useEffect(() => {
    if (!focusNewLine.current) return;
    focusNewLine.current = false;

    const rows = linesBody.current?.querySelectorAll('[data-line-row]');
    rows?.[rows.length - 1]
      ?.querySelector<HTMLInputElement>('[role="combobox"]')
      ?.focus();
  }, [lines.length]);

  const customers = useQuery<readonly CustomerSummary[], ApiError>({
    queryKey: ['customers', 'picker'],
    queryFn: () => listCustomers('', true),
  });

  const selectedCustomer = useMemo(
    () => customers.data?.find((c) => c.customerId === customerId),
    [customers.data, customerId],
  );

  // What the customer owes before this sale: their balance across everything posted to
  // them. It used to be their opening balance, which is what they owed the day the firm
  // started using the system and nothing since.
  const account = useQuery<CustomerAccount, ApiError>({
    queryKey: ['customer-account', customerId],
    queryFn: () => getCustomerAccount(customerId),
    enabled: customerId !== '',
    staleTime: 30 * 1000,
  });

  // A posted sale being corrected is already in that balance - its bill, and the receipt
  // for whatever was paid with it - so it is taken back out to show what came before it.
  const ownEffect =
    editing && editing.header.status === SalesInvoiceStatus.posted
      ? (isReturn(editing.kind) ? -1 : 1) * editing.header.total - editing.amountReceived
      : 0;
  const prevBalance = (account.data?.balance ?? 0) - ownEffect;

  useEffect(() => {
    if (!customerId && settings.defaultCustomerId) {
      setCustomerId(settings.defaultCustomerId);
    }
  }, [customerId, settings.defaultCustomerId]);

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

  // Where a payment goes: the cash in the drawer, or the bank a card or online payment
  // lands in.
  const cashAccounts = useMemo(
    () => (ledgers.data ?? []).filter((ledger) => ledger.kind === LedgerKind.cash),
    [ledgers.data],
  );
  const bankAccounts = useMemo(
    () => (ledgers.data ?? []).filter((ledger) => ledger.kind === LedgerKind.bank),
    [ledgers.data],
  );
  const receivingAccounts = paidThroughBank(paymentMode)
    ? bankAccounts
    : paymentMode === SalesPaymentMode.cash
      ? cashAccounts
      : [...cashAccounts, ...bankAccounts];
  // The account chosen, or the firm's default for the mode, or the first there is.
  const receivingAccountId = receivingAccounts.some(
    (ledger) => ledger.ledgerId === receivedInto,
  )
    ? receivedInto
    : (receivingAccounts.find(
        (ledger) =>
          ledger.ledgerId ===
          (paidThroughBank(paymentMode)
            ? settings.defaultBankLedgerId
            : settings.defaultCashLedgerId),
      )?.ledgerId ??
      receivingAccounts[0]?.ledgerId ??
      '');

  // The salesman and the billing man come from the employee master, so a sale is
  // credited to somebody a commission report can find.
  const employees = useMemo(
    () => (ledgers.data ?? []).filter((ledger) => ledger.kind === LedgerKind.employee),
    [ledgers.data],
  );
  const employeeOptions = useMemo(
    () =>
      // By name: the boxes are narrow, and the name is what the counter knows them by.
      employees.map((employee) => ({
        value: employee.ledgerId,
        label: employee.name,
        detail: employee.code,
      })),
    [employees],
  );
  const employeeId = (id: string): string | null =>
    employees.some((employee) => employee.ledgerId === id) ? id : null;

  const lineColumnList: readonly LineColumn[] = [
    { key: 'product', label: t('sales.product'), defaultOn: true, fixed: true },
    ...productDetailColumns(t),
    { key: 'quantity', label: t('sales.quantity'), defaultOn: true, fixed: true },
    ...(enableFreeQty
      ? [{ key: 'freeQuantity', label: t('sales.freeQuantity'), defaultOn: true }]
      : []),
    { key: 'rate', label: t('sales.rate'), defaultOn: true, fixed: true },
    ...(enableDiscount
      ? [{ key: 'discount', label: t('sales.itemDiscount'), defaultOn: true }]
      : []),
    ...(taxOn
      ? [
          { key: 'taxPercent', label: t('sales.taxPercent'), defaultOn: true },
          { key: 'taxAmount', label: t('sales.taxAmount'), defaultOn: false },
        ]
      : []),
    { key: 'net', label: t('sales.net'), defaultOn: true, fixed: true },
    { key: 'lineTotal', label: t('sales.lineTotal'), defaultOn: true },
  ];
  const lineColumns = useLineColumns(
    isReturn(kind) ? 'sales-return' : 'sales',
    lineColumnList,
  );

  const valuation = useQuery<StockValuationReport, ApiError>({
    queryKey: ['stock-valuation', 'picker', warehouseId],
    queryFn: () => fetchStockValuation(warehouseId, '', true),
    staleTime: 60 * 1000,
  });

  const onHand = useMemo(() => stockByProduct(valuation.data?.rows), [valuation.data]);

  // The counter's item search leaves out what the master hides from it. A line that
  // already holds such an item still shows it, and its code or barcode still finds it.
  const productOptions = useMemo(
    () =>
      (products.data ?? [])
        .filter(
          (product) =>
            !product.hideInPos ||
            isReturn(kind) ||
            lines.some((line) => line.productId === product.id),
        )
        .map((product) => productOption(product, onHand.get(product.id) ?? 0)),
    [products.data, onHand, lines, kind],
  );

  useEffect(() => {
    if (warehouseId !== '' || !warehouses.data) {
      return;
    }

    const chosen = startingGodown(warehouses.data, preferredWarehouseId, godownsOn);

    if (chosen) {
      setWarehouseId(chosen.id);
    }
  }, [warehouses.data, preferredWarehouseId, warehouseId, godownsOn]);

  const returnable = useQuery<PagedResult<SalesInvoiceSummary>, ApiError>({
    queryKey: ['sales-invoices', 'returnable', customerId],
    queryFn: () =>
      listSalesInvoices(
        {
          kind: SalesDocumentKind.invoice,
          status: SalesInvoiceStatus.posted,
          customerLedgerId: customerId,
        },
        1,
        50,
      ),
    enabled: isReturn(kind) && customerId !== '',
  });

  const chargeSetup = useChargeSetup(isReturn(kind) ? 'salesReturn' : 'sales');

  // A default for a ledger since taken off the sales setup would be refused on saving,
  // so a new invoice drops it once the setup arrives. One being corrected keeps what
  // it carries.
  useEffect(() => {
    if (editing || chargeSetup === undefined) return;
    const configured = new Set(chargeSetup.map((charge) => charge.ledgerId));
    setAdditionalLedgers((current) =>
      current.every((row) => configured.has(row.ledgerId))
        ? current
        : current.filter((row) => configured.has(row.ledgerId)),
    );
  }, [editing, chargeSetup]);

  const additionalLedgersTotal = useMemo(() => {
    return additionalLedgers.reduce((acc, row) => {
      const amt = Number(row.amount);
      // A row with no account is not sent, so it is not counted either.
      if (row.ledgerId === '' || !Number.isFinite(amt) || amt <= 0) return acc;
      // The direction the server will post it in, from the firm's setup.
      const adds =
        chargeSetup?.find((charge) => charge.ledgerId === row.ledgerId)?.isAddition ??
        row.isAddition;
      return adds ? acc + amt : acc - amt;
    }, 0);
  }, [additionalLedgers, chargeSetup]);

  const totals = useMemo(() => {
    let gross = 0;
    let discount = 0;
    let tax = 0;
    let taxed = 0;

    for (const line of lines) {
      const lineQty = Number(line.quantity || 0);
      const lineRate = Number(line.rate || 0);
      const lineDisc = enableDiscount ? Number(line.discount || 0) : 0;
      const lineTaxPct = Number(line.taxPercentage || 0);

      // Each line is taxed on its value rounded to the currency, and its tax rounded
      // the same way, as the server does; summing unrounded figures put the screen a
      // cent away from the saved invoice.
      const net = roundMoney(lineQty * lineRate - lineDisc);

      if (Number.isFinite(net) && net > 0) {
        gross += lineQty * lineRate;
        discount += lineDisc;
        taxed += net;
        tax += roundMoney((net * lineTaxPct) / 100);
      }
    }

    const safeGross = Number.isFinite(gross) ? gross : 0;
    const safeDiscount = Number.isFinite(discount) ? discount : 0;
    // The lines as taxed, each rounded, rather than gross less discount unrounded.
    const taxable = Number.isFinite(taxed) ? taxed : 0;

    return {
      gross: safeGross,
      discount: safeDiscount,
      tax,
      taxable,
      total: taxable + tax,
    };
  }, [lines, enableDiscount]);

  const grandTotal = useMemo(() => {
    const raw = totals.total + additionalLedgersTotal;
    return Number.isFinite(raw) ? Math.max(0, raw) : 0;
  }, [totals.total, additionalLedgersTotal]);

  // What the customer is paying now. Blank is the whole bill for cash or online and
  // nothing on account; more than the bill is cash tendered, and only the bill is
  // received - the rest is change.
  const typedPaid = paidAmount.trim() === '' ? null : Number(paidAmount);
  const paidInFull =
    !isReturn(kind) &&
    grandTotal > 0 &&
    (typedPaid === null
      ? paymentMode !== SalesPaymentMode.credit
      : Number.isFinite(typedPaid) && typedPaid >= grandTotal);
  const effectivePaid = useMemo(() => {
    if (isReturn(kind)) return 0;
    if (paidInFull) return grandTotal;
    return typedPaid !== null && Number.isFinite(typedPaid) ? Math.max(0, typedPaid) : 0;
  }, [kind, paidInFull, grandTotal, typedPaid]);

  // Where the customer stands once this document is posted: what they owed, plus this
  // bill (or less this return), less what they are paying now.
  const balanceAfter =
    prevBalance + (isReturn(kind) ? -grandTotal : grandTotal) - effectivePaid;

  // The firm's credit-limit rule: ignored, a warning, or a refusal when the sale posts.
  const policies = useQuery<FirmPolicies, ApiError>({
    queryKey: ['firm-policies'],
    queryFn: getPolicies,
    staleTime: 60 * 1000,
  });
  const creditRule = policies.data?.creditLimit ?? CreditLimitPolicy.warn;

  // How far past their credit limit the unpaid part of this bill takes them, if it does.
  const overLimitBy = useMemo(() => {
    const limit = account.data?.creditLimit;
    if (limit === null || limit === undefined || isReturn(kind)) return 0;
    if (creditRule === CreditLimitPolicy.ignore) return 0;
    const unpaid = Math.max(0, grandTotal - effectivePaid);
    const ownOutstanding =
      editing && editing.header.status === SalesInvoiceStatus.posted
        ? Math.max(0, editing.header.total - editing.amountReceived)
        : 0;
    const after = (account.data?.outstanding ?? 0) - ownOutstanding + unpaid;
    return after > limit + 0.004 ? after - limit : 0;
  }, [account.data, kind, grandTotal, effectivePaid, editing, creditRule]);

  // Cash handed over beyond what is being paid comes back as change - whether it was
  // typed as cash tendered or as more than the bill in the amount received.
  const changeBack = useMemo(() => {
    const tendered =
      cashGiven.trim() !== ''
        ? Number(cashGiven)
        : typedPaid !== null && typedPaid > grandTotal
          ? typedPaid
          : 0;
    if (!Number.isFinite(tendered) || tendered <= 0) return 0;
    return Math.max(0, tendered - effectivePaid);
  }, [cashGiven, typedPaid, grandTotal, effectivePaid]);

  // ---- Loading from another document, found by its number.
  const referenceSources: readonly ReferenceSource[] = isReturn(kind)
    ? [
        {
          key: 'salesInvoice',
          label: t('references.salesInvoice'),
          list: async () =>
            (
              await listSalesInvoices(
                { kind: SalesDocumentKind.invoice, status: SalesInvoiceStatus.posted },
                1,
                100,
              )
            ).items.map((row) => ({
              id: row.salesInvoiceId,
              number: row.number,
              date: row.date,
              party: row.customerName,
              total: row.total,
            })),
        },
      ]
    : [
        ...[
          {
            key: 'quotation',
            kind: SalesOrderKind.quotation,
            label: t('references.quotation'),
          },
          {
            key: 'salesOrder',
            kind: SalesOrderKind.order,
            label: t('references.salesOrder'),
          },
        ].map((source): ReferenceSource => ({
          key: source.key,
          label: source.label,
          list: async () =>
            (await listSalesOrders({}, 1, 100, source.kind)).items.map((row) => ({
              id: row.salesOrderId,
              number: row.number,
              date: row.date,
              party: row.customerName,
              total: row.total,
            })),
        })),
        {
          key: 'purchase',
          label: t('references.purchase'),
          list: async () =>
            (
              await listPurchaseInvoices({ kind: PurchaseDocumentKind.invoice }, 1, 100)
            ).items.map((row) => ({
              id: row.purchaseInvoiceId,
              number: row.number,
              date: row.date,
              party: row.supplierName,
              total: row.total,
            })),
        },
      ];

  /** The rate a loaded line was taxed at, or nothing where the firm charges no tax. */
  const loadedTax = (tax: number, taxable: number): number =>
    taxOn && taxMode !== 'NT' && taxable > 0
      ? Math.round((tax / taxable) * 10000) / 100
      : 0;

  const loadReference = async (sourceKey: string, id: string): Promise<void> => {
    if (sourceKey === 'salesInvoice') {
      const invoice = await getSalesInvoice(id);
      setCustomerId(invoice.customerLedgerId);
      setWarehouseId(invoice.warehouseId);
      setReturnsInvoiceId(id);
      setReference(invoice.header.number);
      setLines(invoice.lines.map(linesFrom));
      return;
    }

    if (sourceKey === 'purchase') {
      // What was bought, at what it sells for rather than at what it cost.
      const purchase = await getPurchaseInvoice(id);
      setReference(purchase.header.number);
      setLines(
        purchase.lines.map((line) => {
          const product = (products.data ?? []).find(
            (candidate) => candidate.id === line.productId,
          );
          const rate = product ? masterRate(product, rateType) : line.rate;
          const tax = taxOn ? lineTaxRate(taxMode, product, settings.defaultTaxRate) : 0;

          return {
            ...emptyLine,
            productId: line.productId,
            quantity: String(line.quantity),
            rate: String(rate),
            inclusiveRate: (rate * (1 + tax / 100)).toFixed(2),
            taxPercentage: String(tax),
          };
        }),
      );
      return;
    }

    const order = await getSalesOrder(id);
    setCustomerId(order.customerLedgerId);
    setWarehouseId(order.warehouseId);
    setReference(order.header.number);
    setLines(
      order.lines.map((line) => {
        const tax = loadedTax(line.tax, line.taxable);
        const quantity =
          line.outstandingQuantity > 0 ? line.outstandingQuantity : line.quantity;

        return {
          ...emptyLine,
          productId: line.productId,
          quantity: String(quantity),
          rate: String(line.rate),
          inclusiveRate: (line.rate * (1 + tax / 100)).toFixed(2),
          taxPercentage: String(tax),
          discount: String(line.discount),
        };
      }),
    );
  };

  const change = (index: number, patch: Partial<DraftLine>): void =>
    setLines((previous) =>
      previous.map((line, at) => (at === index ? { ...line, ...patch } : line)),
    );

  const serialsOn = settings.enableSerialNumbers;

  /** Adds one unit of a product to the invoice, with its serial number if it has one. */
  const addScanned = (found: ProductSummary, serial: string | null): void => {
    const defaultTax = lineTaxRate(taxMode, found, settings.defaultTaxRate);
    const baseRate = masterRate(found, rateType);
    const incRate = (baseRate * (1 + defaultTax / 100)).toFixed(2);

    const existingIndex = lines.findIndex((l) => l.productId === found.id);
    const existing = existingIndex >= 0 ? lines[existingIndex] : undefined;

    if (existing && serial && existing.serialNumbers.includes(serial)) {
      setBarcodeNotice(t('serials.alreadyChosen', { number: serial }));
    } else if (existingIndex >= 0 && existing) {
      // One more of what is already on the invoice. A scanned unit joins its line's
      // serials, and the quantity only grows when every unit counted already has one,
      // so scanning the boxes of a line typed as "3" names those three.
      const serialNumbers = serial
        ? [...existing.serialNumbers, serial]
        : existing.serialNumbers;
      const counted = Number(existing.quantity || '0');
      const nextQty = serial && serialNumbers.length <= counted ? counted : counted + 1;

      change(existingIndex, { quantity: String(nextQty), serialNumbers });
      setBarcodeNotice(`${found.code}${serial ? ` · ${serial}` : ''} (Qty: ${nextQty})`);
    } else {
      const emptyIndex = lines.findIndex((l) => !l.productId);
      const newLine: DraftLine = {
        productId: found.id,
        quantity: '1',
        freeQuantity: '0',
        rate: String(baseRate),
        inclusiveRate: incRate,
        taxPercentage: String(defaultTax),
        discount: '0',
        batchNumber: '',
        expiresOn: '',
        serialNumbers: serial ? [serial] : [],
      };
      if (emptyIndex >= 0) {
        setLines((prev) => prev.map((l, idx) => (idx === emptyIndex ? newLine : l)));
      } else {
        setLines((prev) => [...prev, newLine]);
      }
      setBarcodeNotice(
        `${t('sales.scanProductSuccess')}: ${found.code}${serial ? ` · ${serial}` : ''}`,
      );
    }

    setBarcodeInput('');
    window.setTimeout(() => setBarcodeNotice(null), 3000);
  };

  /*
    What a scan finds, most exact first.

    A product's own code; then, where the firm tracks serial numbers, a unit on the
    shelf whose number it is - which picks the product and the unit together, so
    scanning an IMEI is one action instead of choosing a phone and then choosing which
    one; then the server's own search, which also matches the product's barcodes. The
    old match took the first product whose name merely contained what was scanned, so
    a scan of "A4" could add whatever product happened to mention A4.
  */
  const handleBarcodeScan = async (scannedCode: string): Promise<void> => {
    const code = scannedCode.trim();
    if (!code) return;

    const all = products.data ?? [];
    const byCode = all.find((p) => p.code.toLowerCase() === code.toLowerCase());

    if (byCode) {
      addScanned(byCode, null);
      return;
    }

    if (serialsOn && !isReturn(kind)) {
      try {
        const units = await findSerial(code);
        const unit = units.find(
          (candidate) =>
            (candidate.status === SerialStatus.inStock ||
              candidate.status === SerialStatus.returnedFromCustomer) &&
            (warehouseId === '' || candidate.warehouseId === warehouseId),
        );
        const product = unit ? all.find((p) => p.id === unit.productId) : undefined;

        if (unit && product) {
          addScanned(product, unit.number);
          return;
        }

        if (units.length > 0) {
          setBarcodeNotice(t('serials.notOnShelf', { number: code }));
          window.setTimeout(() => setBarcodeNotice(null), 3000);
          return;
        }
      } catch {
        // Not a serial number the server knows; fall through to the product search.
      }
    }

    try {
      const matches = await listProducts(code, '', false);
      const only = matches.length === 1 ? matches[0] : undefined;
      const product = only ? (all.find((p) => p.id === only.id) ?? only) : undefined;

      if (product) {
        addScanned(product, null);
        return;
      }
    } catch {
      // Fall through to "not found".
    }

    setBarcodeNotice(t('sales.scanProductNotFound'));
    window.setTimeout(() => setBarcodeNotice(null), 3000);
  };

  const handleTaxModeChange = (newMode: 'NT' | 'TAX' | 'GST'): void => {
    setTaxMode(newMode);
    setLines((prev) =>
      prev.map((l) => {
        const newTaxPct = lineTaxRate(
          newMode,
          (products.data ?? []).find((product) => product.id === l.productId),
          settings.defaultTaxRate,
        );
        const updated = { ...l, taxPercentage: String(newTaxPct) };
        if (reverseCalc && l.inclusiveRate) {
          const inc = Number(l.inclusiveRate);
          if (Number.isFinite(inc) && inc > 0) {
            updated.rate = (inc / (1 + newTaxPct / 100)).toFixed(4);
          }
        } else if (!reverseCalc && l.rate) {
          const r = Number(l.rate);
          if (Number.isFinite(r) && r > 0) {
            updated.inclusiveRate = (r * (1 + newTaxPct / 100)).toFixed(2);
          }
        }
        return updated;
      }),
    );
  };

  const handleRateTypeChange = (newRateType: SalesRateType): void => {
    setRateType(newRateType);
    setLines((prev) =>
      prev.map((l) => {
        if (!l.productId) return l;
        const prod = products.data?.find((p) => p.id === l.productId);
        if (!prod) return l;
        const baseRate = masterRate(prod, newRateType);
        const taxPct = Number(l.taxPercentage || 0);
        return {
          ...l,
          rate: String(baseRate),
          inclusiveRate: (baseRate * (1 + taxPct / 100)).toFixed(2),
        };
      }),
    );
  };

  const { errors, submit } = useValidation<{
    date: string;
    customerId: string;
    warehouseId: string;
  }>((values) =>
    collect({
      date: required(values.date, t('sales.dateRequired')),
      customerId: required(values.customerId, t('sales.customerRequired')),
      warehouseId: required(values.warehouseId, t('sales.warehouseRequired')),
      lines: lines.some((line) => line.productId && Number(line.quantity) > 0)
        ? null
        : t('sales.linesRequired'),
    }),
  );

  const lineErrors = useMemo(
    () =>
      lines.map((line) =>
        collect({
          quantity: line.productId
            ? numeric(line.quantity, t('sales.quantityInvalid'), { min: 0.000001 })
            : null,
          rate: line.productId
            ? numeric(line.rate, t('sales.rateInvalid'), { min: 0 })
            : null,
          taxPercentage: numeric(line.taxPercentage, t('sales.taxInvalid'), {
            min: 0,
            max: 100,
          }),
        }),
      ),
    [lines, t],
  );

  /**
   * Saves the document. Posting is the default - a sale saved is a sale made - and a
   * draft is the exception somebody asks for; `print` opens the posted copy to print.
   */
  const save = async (post: boolean, print = false): Promise<void> => {
    setFailure(null);
    if (
      !submit({ date, customerId, warehouseId }) ||
      lineErrors.some((line) => Object.keys(line).length > 0)
    ) {
      return;
    }

    // What was paid with the sale. In full is sent as such, so the server takes the
    // figure from its own rounded total rather than this screen's arithmetic.
    const payment = isReturn(kind)
      ? { amountReceived: 0, paidInFull: false, receivedIntoLedgerId: null }
      : {
          amountReceived: paidInFull ? 0 : roundMoney(effectivePaid),
          paidInFull,
          receivedIntoLedgerId:
            paidInFull || effectivePaid > 0 ? receivingAccountId || null : null,
          paymentReference: paymentReference.trim() || null,
        };

    if (payment.receivedIntoLedgerId === null && (paidInFull || effectivePaid > 0)) {
      setFailure(t('sales.noReceivingAccount'));
      return;
    }

    setBusy(true);

    try {
      const payload: readonly SalesLineInput[] = lines
        .filter((line) => line.productId && Number(line.quantity) > 0)
        .map((line) => ({
          productId: line.productId,
          quantity: Number(line.quantity),
          rate: Number(line.rate || 0),
          taxPercentage: Number(line.taxPercentage || 0),
          discount: enableDiscount ? Number(line.discount || 0) : 0,
          // A number picked on the screen wins; a saved line that kept its batch sends
          // the batch it remembered.
          batchNumber: line.batchNumber || null,
          batchId: line.batchNumber ? null : (line.batchId ?? null),
          serialNumbers: line.serialNumbers,
        }));

      // A blank reference takes the invoice's own number, as the firm asked: the
      // number the server's series will issue, never one made up on this screen.
      const finalReference = reference.trim() || (editing ? '' : nextNumber || '');

      // Requirement 7: addition ledgers mapped into charges
      const chargesPayload: readonly SalesChargeInput[] = additionalLedgers
        .filter((row) => row.ledgerId !== '' && Number(row.amount) > 0)
        .map((row) => ({
          ledgerId: row.ledgerId,
          amount: Number(row.amount),
        }));

      const input = {
        date,
        customerLedgerId: customerId,
        warehouseId,
        lines: payload,
        kind,
        returnsInvoiceId: isReturn(kind) && returnsInvoiceId ? returnsInvoiceId : null,
        referenceNumber: finalReference || null,
        narration: narration.trim() || null,
        charges: chargesPayload,
        // Non-tax is the exception somebody chooses, so it is the one sent; otherwise
        // the server takes the mode from the firm's regime, or keeps the document's.
        mode:
          taxMode === 'NT'
            ? TaxMode.nonTax
            : editing && editing.mode !== TaxMode.nonTax
              ? editing.mode
              : editing
                ? TaxMode.tax
                : null,
        paymentMode,
        salesmanLedgerId: employeeId(salesman),
        billingManLedgerId: employeeId(billingman),
        ...payment,
      };

      if (editing) {
        const header = await updateSalesInvoice(editing.header.salesInvoiceId, {
          ...input,
          post,
          reason: editReason.trim() || null,
        });

        if (isHeldForApproval(header)) {
          onSaved(t('approvals.heldNotice', { number: header.documentNumber }));
          return;
        }

        onSaved(
          t('sales.updatedNotice', { number: header.number }),
          print ? header.salesInvoiceId : undefined,
        );
        return;
      }

      const header = await createSalesInvoice({ ...input, post });

      onSaved(
        post
          ? t('sales.savedPostedNotice', { number: header.number })
          : t('sales.savedNotice', { number: header.number }),
        print ? header.salesInvoiceId : undefined,
      );
    } catch (failure) {
      const api = failure as ApiError;
      setFailure(api.detail || api.code);
    } finally {
      setBusy(false);
    }
  };

  const documentTitle = editing
    ? t('sales.editDocument', { number: editing.header.number })
    : isReturn(kind)
      ? t('sales.newReturn')
      : t('sales.newInvoice');
  const totalLabel = isReturn(kind) ? t('invoice.returnTotal') : t('invoice.billTotal');
  const draftAllowed =
    editing?.header.status !== SalesInvoiceStatus.posted &&
    ((settings.allowSaveDraft !== false && !directSave) || editing !== undefined);
  const itemCount = lines.filter((line) => line.productId !== '').length;
  const totalQuantity = lines.reduce(
    (sum, line) =>
      line.productId !== '' && Number.isFinite(Number(line.quantity))
        ? sum + Number(line.quantity)
        : sum,
    0,
  );

  return (
    <InvoiceWindow
      title={documentTitle}
      onClose={onClose}
      dirty={!editing && (itemCount > 0 || narration.trim() !== '')}
    >
      <InvoiceHeader
        icon={<IconDocument className="size-5" />}
        title={documentTitle}
        subtitle={
          editing
            ? t('invoice.editing', { number: editing.header.number })
            : isReturn(kind)
              ? t('invoice.newSalesReturn')
              : t('invoice.newSales')
        }
      />

      <InvoiceBody>
        {failure && (
          <p role="alert" className="alert-error">
            {failure}
          </p>
        )}
        {editing && editing.header.status === SalesInvoiceStatus.posted && (
          <div className="alert-warn flex flex-wrap items-end gap-3 text-xs">
            <p className="min-w-0 flex-1">{t('sales.editPostedHint')}</p>
            <label className="flex flex-col gap-1">
              <span className="font-medium">{t('sales.editReason')}</span>
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
          {/* Two lines on a wide screen: who, when, how they pay and the numbers; then
              what a counter sets once and seldom touches, and the two ways to fill the
              lines without typing them - scan the goods, or bring them in from a
              quotation or an order (for a return, from the invoice). */}
          <InvoiceFields>
            <div className="min-w-0 sm:col-span-2" {...shortcutProps('customer')}>
              <FormField
                label={t('sales.customer')}
                required
                error={errors['customerId']}
              >
                <QuickAdd kind="customer" onCreated={setCustomerId}>
                  <SearchSelect
                    value={customerId}
                    onChange={setCustomerId}
                    options={(customers.data ?? []).map((customer) => ({
                      value: customer.customerId,
                      label: `${customer.code} — ${customer.name}`,
                      ...(customer.contact.mobileNumber
                        ? { detail: customer.contact.mobileNumber }
                        : {}),
                    }))}
                    invalid={errors['customerId'] !== undefined}
                    label={t('sales.customer')}
                    placeholder={`${t('sales.chooseCustomer')} (F2)`}
                  />
                </QuickAdd>
              </FormField>
            </div>

            <DateField
              label={t('sales.date')}
              required
              value={date}
              onChange={setDate}
              error={errors['date']}
            />

            <SelectField<number>
              label={t('sales.paymentMode')}
              value={paymentMode}
              onChange={(val) => setPaymentMode(Number(val))}
              options={[
                { value: SalesPaymentMode.cash, label: t('paymentModes.cash') },
                { value: SalesPaymentMode.card, label: t('paymentModes.card') },
                { value: SalesPaymentMode.online, label: t('paymentModes.online') },
                { value: SalesPaymentMode.credit, label: t('paymentModes.credit') },
              ]}
            />

            <TextField
              label={
                isReturn(kind) ? t('sales.returnNo') || 'Return No' : t('sales.invoiceNo')
              }
              value={nextInvoiceNo}
              disabled
              onChange={() => {}}
            />
            <TextField
              label={t('sales.referenceNo')}
              value={reference}
              onChange={setReference}
              placeholder={nextNumber ?? ''}
            />
            {godownsOn && (
              <FormField
                label={t('sales.warehouse')}
                required
                error={errors['warehouseId']}
              >
                <QuickAdd kind="warehouse" onCreated={setWarehouseId}>
                  <SearchSelect
                    value={warehouseId}
                    onChange={setWarehouseId}
                    options={(warehouses.data ?? []).map((warehouse) => ({
                      value: warehouse.id,
                      label: `${warehouse.code} — ${warehouse.name}`,
                      ...(warehouse.isDefault
                        ? { meta: t('settings.masterDefault') }
                        : {}),
                    }))}
                    invalid={errors['warehouseId'] !== undefined}
                    label={t('sales.warehouse')}
                    placeholder={t('sales.chooseWarehouse')}
                  />
                </QuickAdd>
              </FormField>
            )}

            <FormField label={t('sales.salesman')}>
              <QuickAdd kind="employee" onCreated={setSalesman}>
                <SearchSelect
                  value={employeeId(salesman) ?? ''}
                  onChange={setSalesman}
                  options={employeeOptions}
                  clearable
                  label={t('sales.salesman')}
                  placeholder={
                    employees.length === 0 ? t('sales.noEmployees') : t('common.choose')
                  }
                />
              </QuickAdd>
            </FormField>
            <FormField label={t('sales.billingman')}>
              <QuickAdd kind="employee" onCreated={setBillingman}>
                <SearchSelect
                  value={employeeId(billingman) ?? ''}
                  onChange={setBillingman}
                  options={employeeOptions}
                  clearable
                  label={t('sales.billingman')}
                  placeholder={
                    employees.length === 0 ? t('sales.noEmployees') : t('common.choose')
                  }
                />
              </QuickAdd>
            </FormField>

            {isReturn(kind) && (
              <FormField
                label={t('sales.againstInvoice')}
                className="sm:col-span-2 md:col-span-1"
              >
                <SearchSelect
                  value={returnsInvoiceId}
                  onChange={setReturnsInvoiceId}
                  clearable
                  label={t('sales.againstInvoice')}
                  placeholder={t('sales.noInvoice')}
                  options={(returnable.data?.items ?? []).map((inv) => ({
                    value: inv.salesInvoiceId,
                    label: inv.number,
                    detail: inv.date,
                    meta: moneyAlways(inv.total),
                  }))}
                />
              </FormField>
            )}

            <div className="min-w-0 sm:col-span-2" {...shortcutProps('scan')}>
              <ScanField
                value={barcodeInput}
                onChange={setBarcodeInput}
                onScan={() => void handleBarcodeScan(barcodeInput)}
                label={t('invoice.scanLabel')}
                placeholder={`${t('sales.barcodePlaceholder')} (F4)`}
                notice={
                  barcodeNotice && (
                    <span
                      role="status"
                      className="font-medium text-brand-700 dark:text-brand-300"
                    >
                      {barcodeNotice}
                    </span>
                  )
                }
              />
            </div>
            {!editing && (
              <ReferenceLoader
                asField
                sources={referenceSources}
                onLoad={loadReference}
                className="sm:col-span-2 lg:col-span-3 xl:col-span-2"
              />
            )}
            <NarrationInput
              value={narration}
              onChange={setNarration}
              label={t('invoice.narration')}
              className={
                isReturn(kind)
                  ? 'sm:col-span-2 lg:col-span-3 xl:col-span-2'
                  : 'sm:col-span-2 lg:col-span-3'
              }
            />
          </InvoiceFields>
        </InvoiceCard>

        {/* The lines take whatever height the party and the money leave. Their
            columns are the user's to choose, remembered in this browser; on a phone
            each line is a card of its own. */}
        <InvoiceCard
          fill
          icon={<IconBox className="size-5" />}
          title={t('invoice.items')}
          meta={t('invoice.itemCount', { count: itemCount })}
          actions={lineColumns.picker}
        >
          <InvoiceLines>
            <table className="line-table sm:min-w-[40rem] lg:min-w-[46rem]">
              <thead className="sticky top-0 z-10 bg-surface-2 text-start text-[13px] text-ink-muted">
                <tr>
                  <th className="rounded-s-lg px-3 py-2.5 text-start font-medium">
                    {t('sales.product')}
                  </th>
                  <ProductDetailHeaders shows={lineColumns.shows} labels={t} />
                  <th className="px-2 py-2.5 text-end font-medium">
                    {t('sales.quantity')}
                  </th>
                  {enableFreeQty && lineColumns.shows('freeQuantity') && (
                    <th className="px-2 py-2.5 text-end font-medium">
                      {t('sales.freeQuantity')}
                    </th>
                  )}
                  <th className="px-2 py-2.5 text-end font-medium">
                    {t('sales.rate')}
                    {reverseCalc ? ` (${t('sales.inclusiveRate')})` : ''}
                  </th>
                  {enableDiscount && lineColumns.shows('discount') && (
                    <th className="px-2 py-2.5 text-end font-medium">
                      {t('sales.itemDiscount')}
                    </th>
                  )}
                  {lineColumns.shows('taxPercent') && (
                    <th className="px-2 py-2.5 text-end font-medium">
                      {t('sales.taxPercent')}
                    </th>
                  )}
                  {lineColumns.shows('taxAmount') && (
                    <th className="px-2 py-2.5 text-end font-medium">
                      {t('sales.taxAmount')}
                    </th>
                  )}
                  <th className="px-2 py-2.5 text-end font-medium">{t('sales.net')}</th>
                  {lineColumns.shows('lineTotal') && (
                    <th className="px-2 py-2.5 text-end font-medium">
                      {t('sales.lineTotal')}
                    </th>
                  )}
                  <th className="line-action-column rounded-e-lg" />
                </tr>
              </thead>

              <tbody ref={linesBody}>
                {lines.map((line, index) => (
                  <LineRow
                    key={index}
                    line={line}
                    products={products.data ?? []}
                    productOptions={productOptions}
                    warehouseId={warehouseId}
                    serialDirection={isReturn(kind) ? 'back' : 'out'}
                    rateType={rateType}
                    customerId={customerId}
                    modeRate={modeTaxRate(taxMode, settings.defaultTaxRate)}
                    productTax={taxMode === 'TAX'}
                    reverseCalc={reverseCalc}
                    autoBatch={autoBatch}
                    enableFreeQty={enableFreeQty}
                    enableDiscount={enableDiscount}
                    shows={lineColumns.shows}
                    columnCount={
                      3 +
                      lineColumnList.filter(
                        (column) =>
                          column.key !== 'product' &&
                          column.key !== 'quantity' &&
                          lineColumns.shows(column.key),
                      ).length
                    }
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
              onClick={() => {
                // The cursor goes to the new line's product, so the next item is
                // typed straight away rather than reached with the mouse.
                focusNewLine.current = true;
                setLines((previous) => [
                  ...previous,
                  {
                    ...emptyLine,
                    taxPercentage: String(modeTaxRate(taxMode, settings.defaultTaxRate)),
                  },
                ]);
              }}
              className="btn-secondary mt-3 mb-1"
            >
              <IconPlus className="size-4" />
              {t('invoice.addItem')}
            </button>
          </InvoiceLines>
        </InvoiceCard>

        {/* Along the foot: who the bill is for; everything that changes what it
            comes to - the rates, the tax, the additional ledgers - in one card; the
            payment, with what it leaves owing; and the bill set out line by line. */}
        <InvoiceFooter className="xl:grid-cols-[minmax(0,0.85fr)_minmax(0,1.05fr)_minmax(0,1.2fr)_minmax(0,1fr)]">
          <PartyCard
            icon={<IconUser className="size-[18px]" />}
            title={t('invoice.customerDetails')}
            party={selectedCustomer}
            empty={t('invoice.noCustomer')}
            money={moneyAlways}
          />

          <FooterSection
            icon={<IconPercent className="size-[18px]" />}
            title={t('invoice.ratesAndCharges')}
          >
            <div className="grid grid-cols-2 gap-3">
              <SelectField<SalesRateType>
                size="sm"
                label={t('sales.salesRateType')}
                value={rateType}
                onChange={(val) => handleRateTypeChange(val as SalesRateType)}
                options={[
                  { value: 'retail', label: t('prefs.rateRetail') },
                  { value: 'wholesale', label: t('prefs.rateWholesale') },
                  { value: 'mrp', label: t('prefs.rateMrp') },
                  { value: 'lastQuotation', label: t('prefs.rateLastQuotation') },
                  { value: 'customerRate', label: t('prefs.rateCustomer') },
                  { value: 'batch', label: t('prefs.rateBatch') },
                ]}
              />
              {taxOn && (
                <SelectField<'NT' | 'TAX' | 'GST'>
                  size="sm"
                  label={t('sales.taxMode')}
                  value={taxMode}
                  onChange={(val) => handleTaxModeChange(val as 'NT' | 'TAX' | 'GST')}
                  options={[
                    { value: 'NT', label: 'NT (0%)' },
                    { value: 'TAX', label: 'TAX (5%)' },
                    { value: 'GST', label: 'GST (18%)' },
                  ]}
                />
              )}
            </div>
            <div className="mt-3 space-y-1.5" {...shortcutProps('charges')}>
              <span className="block text-[13px] font-medium text-ink/80">
                {t('sales.additionalLedgers')}
              </span>
              <AdditionalLedgerList
                carried={additionalLedgers}
                setup={chargeSetup}
                nonTax={taxMode === 'NT'}
                ledgers={ledgers.data ?? []}
                onAmount={setChargeAmount}
                onRemove={(ledgerId) =>
                  setAdditionalLedgers((rows) =>
                    rows.filter((row) => row.ledgerId !== ledgerId),
                  )
                }
              />
            </div>
          </FooterSection>

          <FooterSection
            icon={<IconCreditCard className="size-[18px]" />}
            title={
              isReturn(kind) ? t('invoice.customerAccount') : t('invoice.paymentDetails')
            }
          >
            <div className="grid grid-cols-2 gap-x-3 gap-y-3 xl:grid-cols-3">
              {!isReturn(kind) && (
                <CompactField label={t('sales.receivedNow')}>
                  <input
                    {...shortcutProps('payment')}
                    type="number"
                    inputMode="decimal"
                    min="0"
                    aria-label={t('sales.receivedNow')}
                    value={paidAmount}
                    placeholder={
                      paymentMode === SalesPaymentMode.credit
                        ? '0.00'
                        : moneyAlways(grandTotal)
                    }
                    onChange={(e) => setPaidAmount(e.target.value)}
                    className="field-input-sm text-end font-semibold tabular-nums"
                  />
                </CompactField>
              )}
              {!isReturn(kind) && paidThroughBank(paymentMode) && (
                <CompactField
                  label={
                    paymentMode === SalesPaymentMode.card
                      ? t('sales.cardApproval')
                      : t('sales.transactionId')
                  }
                >
                  <input
                    value={paymentReference}
                    maxLength={60}
                    aria-label={
                      paymentMode === SalesPaymentMode.card
                        ? t('sales.cardApproval')
                        : t('sales.transactionId')
                    }
                    onChange={(e) => setPaymentReference(e.target.value)}
                    placeholder={
                      paymentMode === SalesPaymentMode.card
                        ? t('sales.cardApprovalPlaceholder')
                        : t('sales.transactionIdPlaceholder')
                    }
                    className="field-input-sm tabular-nums"
                  />
                </CompactField>
              )}
              {!isReturn(kind) && paymentMode === SalesPaymentMode.cash && (
                <>
                  <CompactField label={t('sales.cashGiven')}>
                    <input
                      type="number"
                      inputMode="decimal"
                      min="0"
                      aria-label={t('sales.cashGiven')}
                      value={cashGiven}
                      placeholder="0.00"
                      onChange={(e) => setCashGiven(e.target.value)}
                      className="field-input-sm text-end tabular-nums"
                    />
                  </CompactField>
                  <ValueField
                    label={t('sales.changeBack')}
                    value={moneyAlways(changeBack)}
                    tone="good"
                  />
                </>
              )}
              {isReturn(kind) ? (
                <ValueField
                  label={t('sales.thisReturn')}
                  value={
                    grandTotal > 0.004 ? `−${moneyAlways(grandTotal)}` : moneyAlways(0)
                  }
                />
              ) : (
                <ValueField
                  label={t('sales.billDue')}
                  value={moneyAlways(Math.max(0, grandTotal - effectivePaid))}
                  tone={grandTotal - effectivePaid > 0.004 ? 'due' : 'strong'}
                />
              )}
              <ValueField
                label={t('sales.prevBalance')}
                value={
                  customerId === '' || account.isPending ? '—' : moneyAlways(prevBalance)
                }
              />
              <ValueField
                label={t('invoice.newBalance')}
                value={moneyAlways(balanceAfter)}
                tone={balanceAfter > 0.004 ? 'due' : 'strong'}
              />
            </div>
            {overLimitBy > 0 && (
              <p
                role="alert"
                className="mt-2 text-xs font-medium text-amber-700 dark:text-amber-400"
              >
                {t('sales.overCreditLimit', {
                  limit: moneyAlways(account.data?.creditLimit ?? 0),
                  by: moneyAlways(overLimitBy),
                })}
                {creditRule === CreditLimitPolicy.block &&
                  ` ${t('sales.creditLimitBlocks')}`}
              </p>
            )}
          </FooterSection>

          <BillCard
            className="md:col-span-2 xl:col-span-1"
            label={totalLabel}
            total={grandTotal}
            money={moneyAlways}
            breakdown={{
              items: itemCount,
              quantity: totalQuantity,
              gross: totals.gross,
              discount: totals.discount,
              taxable: totals.taxable,
              tax: totals.tax,
              showTax: taxOn,
              taxLabel: `${t('sales.tax')} (${taxMode})`,
              charges: chargeAmounts(
                chargeSetup,
                taxMode === 'NT',
                additionalLedgers,
                ledgers.data ?? [],
              ),
            }}
          />
        </InvoiceFooter>
      </InvoiceBody>

      <InvoiceActions
        hints={
          <ShortcutHints
            actions={['customer', 'scan', 'add-line', 'charges', 'payment']}
          />
        }
        total={{ label: totalLabel, amount: moneyAlways(grandTotal) }}
        secondary={[
          ...(draftAllowed
            ? [
                {
                  label: t('sales.saveDraft'),
                  onClick: () => void save(false),
                  shortcut: 'draft' as const,
                  disabled: busy,
                },
              ]
            : []),
          {
            label: t('sales.save'),
            onClick: () => void save(true),
            shortcut: 'save',
            disabled: busy,
          },
        ]}
        primary={{
          label: busy ? t('common.saving') : t('sales.saveAndPrint'),
          icon: <IconPrinter className="size-4" />,
          onClick: () => void save(true, true),
          shortcut: 'print',
          disabled: busy,
        }}
      />
    </InvoiceWindow>
  );
}

/**
 * One line, and whatever the product it names needs beyond a quantity and a rate.
 */
function LineRow({
  line,
  products,
  productOptions,
  warehouseId,
  serialDirection,
  rateType,
  customerId,
  modeRate,
  productTax,
  reverseCalc,
  autoBatch,
  enableFreeQty,
  enableDiscount,
  shows,
  columnCount,
  errors,
  removable,
  onChange,
  onRemove,
}: {
  readonly line: DraftLine;
  readonly products: readonly ProductSummary[];
  readonly productOptions: ReturnType<typeof productOption>[];
  readonly warehouseId: string;
  /** Out of stock for a sale, back from the customer for a return. */
  readonly serialDirection: SerialDirection;
  readonly rateType: SalesRateType;
  /** The customer, for the rates they were last quoted or charged. */
  readonly customerId: string;
  /** The rate the tax mode starts a line at. */
  readonly modeRate: number;
  /** Whether a chosen product brings its own rate from the tax rate master. */
  readonly productTax: boolean;
  readonly reverseCalc: boolean;
  readonly autoBatch: boolean;
  readonly enableFreeQty: boolean;
  readonly enableDiscount: boolean;
  /** Whether the user has the column on. */
  readonly shows: (key: string) => boolean;
  /** How many columns the row has, for the batch and serial row beneath it. */
  readonly columnCount: number;
  readonly errors: Readonly<Record<string, string>>;
  readonly removable: boolean;
  readonly onChange: (patch: Partial<DraftLine>) => void;
  readonly onRemove: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const product = products.find((candidate) => candidate.id === line.productId);
  const net =
    Number(line.quantity) * Number(line.rate) -
    (enableDiscount ? Number(line.discount || 0) : 0);

  const wanted = Number(line.quantity);
  const needsBatch = product?.tracksBatches === true;
  const serialsOn = useSerialNumbersEnabled();
  // Offered where the firm has switched serial numbers on and the product is tracked
  // by them - never as a box on every line, and never demanded.
  const needsSerials = serialsOn && product?.tracksSerialNumbers === true;
  const [pickerOpen, setPickerOpen] = useState(false);
  const [offerPicker, setOfferPicker] = useState(false);

  const batches = useQuery<readonly BatchStockRow[], ApiError>({
    queryKey: ['sales-line-batches', line.productId, warehouseId],
    queryFn: () => fetchProductBatches(line.productId, warehouseId),
    enabled: needsBatch && line.productId !== '' && warehouseId !== '',
  });

  // The line as it now stands, for answers that arrive after somebody has moved on.
  const latest = useRef(line);
  latest.current = line;

  /** The rate fields for a rate, at the line's own tax. */
  const priced = (rate: number): Partial<DraftLine> => ({
    rate: String(rate),
    inclusiveRate: (rate * (1 + Number(latest.current.taxPercentage || 0) / 100)).toFixed(
      2,
    ),
  });

  /** A batch's own selling rate, where the counter sells at batch rates and it has one. */
  const batchRate = (batchNumber: string): Partial<DraftLine> => {
    if (rateType !== 'batch') return {};

    const chosen = (batches.data ?? []).find(
      (batch) => batch.batchNumber === batchNumber,
    );

    return chosen?.sellingRate ? priced(chosen.sellingRate) : {};
  };

  // The customer's last quotation or last sale, asked for when the product, the
  // customer or the rate type changes - never when a saved line is first shown, whose
  // rate somebody already chose. Where there is none, the master's rate stands.
  const hintKey = `${rateType}|${customerId}|${line.productId}`;
  const lastHintKey = useRef(hintKey);

  useEffect(() => {
    if (hintKey === lastHintKey.current) return;
    lastHintKey.current = hintKey;

    if (rateType !== 'lastQuotation' && rateType !== 'customerRate') return;
    if (!customerId || !line.productId) return;

    const productId = line.productId;

    getSalesRateHints(customerId, productId)
      .then((hints) => {
        const hint = rateType === 'lastQuotation' ? hints.lastQuotation : hints.lastSale;

        if (hint && latest.current.productId === productId) {
          onChange(priced(hint.rate));
        }
      })
      .catch(() => undefined);
  }, [hintKey]);

  // Switched to batch rates: the batch already picked takes its own.
  const lastRateType = useRef(rateType);

  useEffect(() => {
    if (rateType === lastRateType.current) return;
    lastRateType.current = rateType;

    const patch = line.batchNumber ? batchRate(line.batchNumber) : {};

    if (patch.rate) onChange(patch);
  }, [rateType]);

  // What the shelf holds, read only to decide whether to offer the picker the moment
  // a serialised product is chosen for a sale. The firm asked for exactly that: a popup
  // of the available numbers, when there are any.
  const serials = useQuery<readonly SerialNumberView[], ApiError>({
    queryKey: ['sales-line-serials', line.productId, warehouseId],
    queryFn: () => fetchProductSerials(line.productId, warehouseId),
    enabled:
      offerPicker &&
      serialDirection === 'out' &&
      line.productId !== '' &&
      warehouseId !== '',
  });

  useEffect(() => {
    if (!offerPicker || serialDirection !== 'out' || !serials.data) {
      return;
    }

    setOfferPicker(false);

    if (
      serials.data.some(
        (unit) =>
          unit.status === SerialStatus.inStock ||
          unit.status === SerialStatus.returnedFromCustomer,
      )
    ) {
      setPickerOpen(true);
    }
  }, [offerPicker, serialDirection, serials.data]);

  // A saved line names its batch by identifier; show it by number where the shelf
  // still lists it, and otherwise leave it to be sent back as it was.
  useEffect(() => {
    if (!line.batchId || line.batchNumber || !batches.data) return;

    const kept = batches.data.find((b) => b.batchId === line.batchId);

    if (kept) {
      onChange({ batchNumber: kept.batchNumber, expiresOn: kept.expiresOn ?? '' });
    }
  }, [line.batchId, line.batchNumber, batches.data]);

  // Auto Batch FIFO
  useEffect(() => {
    if (
      autoBatch &&
      needsBatch &&
      !line.batchId &&
      (!line.batchNumber || line.batchNumber === '') &&
      batches.data &&
      batches.data.length > 0
    ) {
      const available = batches.data.find((b) => b.quantity > 0) ?? batches.data[0];
      if (available) {
        onChange({
          batchNumber: available.batchNumber,
          expiresOn: available.expiresOn ?? '',
          ...batchRate(available.batchNumber),
        });
      }
    }
  }, [autoBatch, needsBatch, line.batchNumber, batches.data]);

  const handleProductSelect = (value: string): void => {
    const chosen = products.find((candidate) => candidate.id === value);
    if (chosen) {
      const baseRate = masterRate(chosen, rateType);
      // A product chosen afresh starts at the mode's rate, like its price starts at
      // the master's: whatever the previous product on this line carried is not a
      // rate anybody chose for this one.
      const defaultTax =
        productTax && chosen.taxPercentage != null ? chosen.taxPercentage : modeRate;
      const incRate = (baseRate * (1 + defaultTax / 100)).toFixed(2);
      onChange({
        productId: value,
        rate: String(baseRate),
        inclusiveRate: incRate,
        taxPercentage: String(defaultTax),
        batchNumber: '',
        batchId: undefined,
        expiresOn: '',
        serialNumbers: [],
      });
      // A serialised product just chosen for goods going out: if the shelf holds
      // numbered units, the picker opens by itself (see the effect below).
      setOfferPicker(serialsOn && chosen.tracksSerialNumbers);
    } else {
      onChange({
        productId: value,
        batchNumber: '',
        batchId: undefined,
        expiresOn: '',
        serialNumbers: [],
      });
    }
  };

  const isExpired = line.expiresOn ? new Date(line.expiresOn) < new Date() : false;
  const isNearExpiry = line.expiresOn
    ? !isExpired &&
      (new Date(line.expiresOn).getTime() - Date.now()) / (1000 * 3600 * 24) <= 30
    : false;

  return (
    <>
      <tr data-line-row className="border-t border-line">
        <td data-label={t('sales.product')} className="min-w-40 px-2 py-1 lg:min-w-64">
          <SearchSelect
            value={line.productId}
            onChange={handleProductSelect}
            options={productOptions}
            size="sm"
            label={t('sales.product')}
            placeholder={t('sales.chooseProduct')}
          />
        </td>
        <ProductDetailCells product={product} shows={shows} labels={t} />
        <NumberCell
          label={t('sales.quantity')}
          value={line.quantity}
          error={errors['quantity']}
          onChange={(value) => onChange({ quantity: value })}
        />
        {enableFreeQty && shows('freeQuantity') && (
          <NumberCell
            label={t('sales.freeQuantity')}
            value={line.freeQuantity ?? '0'}
            onChange={(value) => onChange({ freeQuantity: value })}
          />
        )}
        {reverseCalc ? (
          <NumberCell
            label={`${t('sales.rate')} (${t('sales.inclusiveRate')})`}
            value={line.inclusiveRate ?? ''}
            error={errors['rate']}
            subtitle={line.rate ? `Excl: ${moneyAlways(Number(line.rate))}` : undefined}
            onChange={(val) => {
              const inc = Number(val);
              const taxPct = Number(line.taxPercentage || 0);
              const exc =
                Number.isFinite(inc) && inc > 0
                  ? (inc / (1 + taxPct / 100)).toFixed(4)
                  : '';
              onChange({ inclusiveRate: val, rate: exc });
            }}
          />
        ) : (
          <NumberCell
            label={t('sales.rate')}
            value={line.rate}
            error={errors['rate']}
            subtitle={
              line.inclusiveRate
                ? `Incl: ${moneyAlways(Number(line.inclusiveRate))}`
                : undefined
            }
            onChange={(val) => {
              const r = Number(val);
              const taxPct = Number(line.taxPercentage || 0);
              const inc =
                Number.isFinite(r) && r > 0 ? (r * (1 + taxPct / 100)).toFixed(2) : '';
              onChange({ rate: val, inclusiveRate: inc });
            }}
          />
        )}
        {enableDiscount && shows('discount') && (
          <NumberCell
            label={t('sales.itemDiscount')}
            value={line.discount}
            onChange={(value) => onChange({ discount: value })}
          />
        )}
        {shows('taxPercent') && (
          <NumberCell
            label={t('sales.taxPercent')}
            value={line.taxPercentage}
            error={errors['taxPercentage']}
            onChange={(value) => onChange({ taxPercentage: value })}
          />
        )}
        {shows('taxAmount') && (
          <td
            data-label={t('sales.taxAmount')}
            className="px-2 py-1 text-end tabular-nums text-ink-muted"
          >
            {Number.isFinite(net)
              ? moneyAlways(roundMoney((net * Number(line.taxPercentage || 0)) / 100))
              : '—'}
          </td>
        )}
        <td data-label={t('sales.net')} className="px-2 py-1 text-end tabular-nums">
          {Number.isFinite(net) ? moneyAlways(net) : '—'}
        </td>
        {shows('lineTotal') && (
          <td
            data-label={t('sales.lineTotal')}
            className="px-2 py-1 text-end font-semibold tabular-nums"
          >
            {Number.isFinite(net)
              ? moneyAlways(
                  net + roundMoney((net * Number(line.taxPercentage || 0)) / 100),
                )
              : '—'}
          </td>
        )}
        <td className="px-2 py-1 text-end">
          {removable && (
            <RemoveLineButton onClick={onRemove} label={t('sales.removeLine')} />
          )}
        </td>
      </tr>

      {(needsBatch || needsSerials) && (
        <tr className="border-t border-dashed border-line bg-surface-2/30">
          <td colSpan={columnCount} className="px-3 py-2">
            <div className="flex flex-wrap items-center gap-4">
              {needsBatch && (
                <div className="flex flex-wrap items-center gap-2">
                  <Field label={t('sales.batch')}>
                    <Select
                      label={t('sales.batch')}
                      value={line.batchNumber}
                      onChange={(value) => {
                        const chosen = (batches.data ?? []).find(
                          (b) => b.batchNumber === String(value),
                        );
                        onChange({
                          batchNumber: String(value),
                          expiresOn: chosen?.expiresOn ?? '',
                          ...batchRate(String(value)),
                        });
                      }}
                      options={[
                        { value: '', label: t('sales.chooseBatch') },
                        ...(batches.data ?? []).map((batch) => ({
                          value: batch.batchNumber,
                          label: `${batch.batchNumber} (Qty: ${batch.quantity}${batch.expiresOn ? ` · Exp: ${batch.expiresOn}` : ''})`,
                        })),
                      ]}
                    />
                  </Field>

                  {line.expiresOn && (
                    <div className="mt-4">
                      <span
                        className={clsx(
                          'inline-flex items-center gap-1 rounded px-2 py-0.5 text-xs tabular-nums font-medium',
                          isExpired
                            ? 'bg-red-50 text-red-700 dark:bg-red-500/20 dark:text-red-300 ring-1 ring-red-500/30'
                            : isNearExpiry
                              ? 'bg-amber-50 text-amber-700 dark:bg-amber-500/20 dark:text-amber-300 ring-1 ring-amber-500/30'
                              : 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-300',
                        )}
                      >
                        {isExpired ? t('sales.batchExpired') : t('sales.batchExpiresOn')}:{' '}
                        {line.expiresOn}
                      </span>
                    </div>
                  )}

                  {autoBatch && (
                    <div className="mt-4">
                      <span className="rounded bg-brand-50 px-2 py-0.5 text-[10px] font-semibold text-brand-700 dark:bg-brand-500/15 dark:text-brand-300">
                        {t('sales.autoBatch')} (FIFO)
                      </span>
                    </div>
                  )}
                </div>
              )}

              {needsSerials && (
                <SerialLineSummary
                  chosen={line.serialNumbers}
                  quantity={wanted}
                  onOpen={() => setPickerOpen(true)}
                />
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
          direction={serialDirection}
          chosen={line.serialNumbers}
          quantity={wanted}
          onClose={() => setPickerOpen(false)}
          onDone={(serialNumbers, quantity) => {
            onChange({ serialNumbers, quantity: String(quantity) });
            setPickerOpen(false);
          }}
        />
      )}
    </>
  );
}

/** Reads a document back, and offers what may still be done to it or printed. */
function DocumentDialog({
  id,
  busy,
  onClose,
  onPost,
  onCancel,
  onEdit,
  initialPrint = false,
}: {
  readonly id: string;
  readonly busy: boolean;
  readonly onClose: () => void;
  readonly onPost: (id: string) => void;
  readonly onCancel: (id: string, reason: string) => void;
  readonly onEdit: (document: SalesInvoiceDetail) => void;
  readonly initialPrint?: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [reason, setReason] = useState('');
  const [activeTab, setActiveTab] = useState<'details' | 'print'>(
    initialPrint ? 'print' : 'details',
  );

  const query = useQuery<SalesInvoiceDetail, ApiError>({
    queryKey: ['sales-invoice', id],
    queryFn: () => getSalesInvoice(id),
  });

  const customers = useQuery<readonly CustomerSummary[], ApiError>({
    queryKey: ['customers', 'picker'],
    queryFn: () => listCustomers('', true),
  });

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });

  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', 'picker'],
    queryFn: () => listProducts('', '', false),
  });

  const document = query.data;

  const matchedCustomer = useMemo(
    () =>
      customers.data?.find(
        (c) =>
          c.customerId === document?.customerLedgerId ||
          c.code === document?.customerLedgerId,
      ),
    [customers.data, document?.customerLedgerId],
  );

  return (
    <Modal
      title={
        document?.header.number
          ? `${document.header.number} — ${activeTab === 'print' ? t('sales.print') || 'Print Preview' : t('sales.document') || 'Document Details'}`
          : t('sales.document') || 'Document Details'
      }
      onClose={onClose}
      size="fullscreen"
    >
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
        <div className="space-y-4">
          {/* View Tab Switcher & Status */}
          <div className="no-print flex flex-wrap items-center justify-between gap-3 border-b border-line pb-3">
            <div className="inline-flex rounded-xl bg-surface-2 p-1 border border-line">
              <button
                type="button"
                onClick={() => setActiveTab('details')}
                className={clsx(
                  'rounded-lg px-3.5 py-1.5 text-xs font-semibold transition',
                  activeTab === 'details'
                    ? 'bg-surface text-ink shadow-sm'
                    : 'text-ink-muted hover:text-ink',
                )}
              >
                {t('sales.documentDetails') || 'Document Details'}
              </button>
              <button
                type="button"
                onClick={() => setActiveTab('print')}
                className={clsx(
                  'inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-xs font-semibold transition',
                  activeTab === 'print'
                    ? 'bg-primary-600 text-white shadow-sm'
                    : 'text-ink-muted hover:text-ink',
                )}
              >
                <IconPrinter className="size-3.5" />
                <span>
                  {t('sales.printInvoice') || 'Print / Formats (A4 / A5 / POS)'}
                </span>
              </button>
            </div>

            <div className="flex items-center gap-2">
              <StatusBadge
                tone={statusTone(document.header.status)}
                label={statusLabel(document.header.status, t)}
                struck={document.header.status === SalesInvoiceStatus.cancelled}
              />
            </div>
          </div>

          {activeTab === 'print' ? (
            <PrintCenter
              document={printableSale(
                document,
                {
                  customers: customers.data ?? [],
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
              onDone={onClose}
            />
          ) : (
            <>
              <div className="grid grid-cols-1 gap-2 text-sm sm:grid-cols-3 bg-surface-2/30 p-3 rounded-xl border border-line">
                <Detail label={t('sales.date')} value={document.date} />
                <div>
                  <span className="text-xs text-ink-muted">{t('sales.customer')}</span>
                  <div className="mt-0.5 font-medium text-ink">
                    {matchedCustomer?.name || document.customerLedgerId || '—'}
                  </div>
                </div>
                <Detail label={t('sales.currency')} value={document.currency} />
                <Detail
                  label={t('sales.paymentMode')}
                  value={paymentModeLabel(document.paymentMode, t)}
                />
                {document.amountReceived > 0 && (
                  <Detail
                    label={t('sales.amountReceived')}
                    value={moneyAlways(document.amountReceived)}
                  />
                )}
                {document.paymentReference && (
                  <Detail
                    label={t('sales.paymentReference')}
                    value={document.paymentReference}
                  />
                )}
              </div>

              <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
                <table className="line-table sm:min-w-[32rem]">
                  <thead className="text-xs text-ink-muted">
                    <tr>
                      <th className="px-2 py-1 text-start">#</th>
                      <th className="px-2 py-1 text-start">
                        {t('sales.product')} & Serial / IMEI
                      </th>
                      <th className="px-2 py-1 text-end">{t('sales.quantity')}</th>
                      <th className="px-2 py-1 text-end">{t('sales.rate')}</th>
                      <th className="px-2 py-1 text-end">{t('sales.taxable')}</th>
                      <th className="px-2 py-1 text-end">{t('sales.tax')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {document.lines.map((line) => {
                      const prod = products.data?.find((p) => p.id === line.productId);
                      return (
                        <tr key={line.lineNumber} className="border-t border-line">
                          <td data-label="#" className="px-2 py-1">
                            {line.lineNumber}
                          </td>
                          <td data-label={t('sales.product')} className="px-2 py-1">
                            <span className="font-medium text-ink">
                              {prod?.description || prod?.code || line.productId}
                            </span>
                            {line.serialNumberIds && line.serialNumberIds.length > 0 && (
                              <div className="mt-0.5 flex flex-wrap gap-1">
                                <span className="text-[10px] text-purple-700 bg-purple-50 dark:bg-purple-950/40 dark:text-purple-300 px-1 rounded tabular-nums">
                                  IMEI/Serial: {line.serialNumberIds.join(', ')}
                                </span>
                              </div>
                            )}
                          </td>
                          <td
                            data-label={t('sales.quantity')}
                            className="px-2 py-1 text-end tabular-nums"
                          >
                            {line.quantity}
                          </td>
                          <td
                            data-label={t('sales.rate')}
                            className="px-2 py-1 text-end tabular-nums"
                          >
                            {moneyAlways(line.rate)}
                          </td>
                          <td
                            data-label={t('sales.taxable')}
                            className="px-2 py-1 text-end tabular-nums"
                          >
                            {moneyAlways(line.taxable)}
                          </td>
                          <td
                            data-label={t('sales.tax')}
                            className="px-2 py-1 text-end tabular-nums"
                          >
                            {moneyAlways(line.tax)}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>

              {/*
                The same ladder the draft was entered against. Gross and discount are
                added up from the lines because the header carries neither — it keeps the
                taxable value, which is what is left after the discount.
              */}
              <div className="line-totals flex flex-wrap items-end justify-end gap-6 pt-2">
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

              <div className="flex flex-wrap items-end justify-between gap-3 pt-4 border-t border-line max-sm:justify-start max-sm:gap-2">
                <div className="flex items-center gap-2">
                  <ModalButton onClick={() => setActiveTab('print')}>
                    <span className="inline-flex items-center gap-1.5">
                      <IconPrinter className="size-4" />
                      {t('sales.print') || 'Print Invoice'}
                    </span>
                  </ModalButton>
                </div>

                {/*
                  On a phone the two groups flow as one row of buttons, and the
                  reason for a cancellation gets a line of its own under them: wrapped
                  inside the right-hand group it landed in a corner, half the width of
                  the screen, below a button it had nothing to do with.
                */}
                <div className="flex flex-wrap items-end gap-2 max-sm:contents">
                  {document.header.status !== SalesInvoiceStatus.cancelled && (
                    <ModalButton disabled={busy} onClick={() => onEdit(document)}>
                      {t('sales.edit')}
                    </ModalButton>
                  )}

                  {document.header.status === SalesInvoiceStatus.draft && (
                    <ModalButton primary disabled={busy} onClick={() => onPost(id)}>
                      {t('sales.post')}
                    </ModalButton>
                  )}

                  {document.header.status !== SalesInvoiceStatus.cancelled && (
                    <div className="flex flex-wrap items-end justify-end gap-2 max-sm:order-last max-sm:w-full max-sm:flex-nowrap">
                      <div className="max-sm:min-w-0 max-sm:flex-1">
                        <Field label={t('sales.cancelReason')}>
                          <input
                            value={reason}
                            onChange={(event) => setReason(event.target.value)}
                            className="field-input-sm"
                          />
                        </Field>
                      </div>
                      <ModalButton
                        disabled={busy || reason.trim() === ''}
                        onClick={() => onCancel(id, reason.trim())}
                      >
                        {t('sales.cancel')}
                      </ModalButton>
                    </div>
                  )}

                  <ModalButton onClick={onClose}>{t('sales.close')}</ModalButton>
                </div>
              </div>
            </>
          )}
        </div>
      )}
    </Modal>
  );
}

/** The time of day a sale was entered, in the reader's own clock. */
function timeOfSale(createdAtUtc: string): string {
  const at = new Date(createdAtUtc);

  return Number.isNaN(at.getTime())
    ? ''
    : at.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function paymentModeLabel(mode: number, t: (key: string) => string): string {
  if (mode === SalesPaymentMode.cash) return t('paymentModes.cash');
  if (mode === SalesPaymentMode.card) return t('paymentModes.card');
  if (mode === SalesPaymentMode.online) return t('paymentModes.online');

  return t('paymentModes.credit');
}

function statusLabel(status: number, t: (key: string) => string): string {
  if (status === SalesInvoiceStatus.posted) return t('sales.posted');
  if (status === SalesInvoiceStatus.cancelled) return t('sales.cancelled');

  return t('sales.draft');
}

/** Posted is done, cancelled did not happen, and a draft is still somebody's to finish. */
function statusTone(status: number): StatusTone {
  if (status === SalesInvoiceStatus.posted) return 'success';
  if (status === SalesInvoiceStatus.cancelled) return 'danger';

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

function Field({
  label,
  children,
}: {
  readonly label: string;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <label className="flex min-w-0 flex-col gap-1 text-xs text-ink-muted">
      {label}
      {children}
    </label>
  );
}

function NumberCell({
  value,
  onChange,
  label,
  error,
  subtitle,
  placeholder,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly label: string;
  readonly error?: string | undefined;
  readonly subtitle?: string | undefined;
  readonly placeholder?: string | undefined;
}): React.JSX.Element {
  return (
    <td data-label={label} className="px-2 py-1 text-end">
      <input
        type="number"
        inputMode="decimal"
        aria-label={label}
        value={value}
        placeholder={placeholder}
        aria-invalid={error ? true : undefined}
        onChange={(event) => onChange(event.target.value)}
        className={clsx(
          'field-input-sm ms-auto w-20 text-end tabular-nums lg:w-24',
          error && 'field-invalid',
        )}
      />
      {subtitle && (
        <span className="mt-0.5 block text-[10px] tabular-nums text-ink-muted text-end">
          {subtitle}
        </span>
      )}
    </td>
  );
}

function Select<TValue extends string | number>({
  value,
  onChange,
  options,
  label,
}: {
  readonly value: TValue;
  readonly onChange: (value: TValue) => void;
  readonly options: readonly { readonly value: TValue; readonly label: string }[];
  readonly label?: string | undefined;
}): React.JSX.Element {
  return (
    <SearchSelect
      value={String(value)}
      onChange={(next) =>
        onChange((typeof value === 'number' ? Number(next) : next) as TValue)
      }
      size="sm"
      label={label}
      options={options.map((option) => ({
        value: String(option.value),
        label: option.label,
      }))}
    />
  );
}

/** Sales returns screen rendering the returns list and entry. */
export function SalesReturnsPage(): React.JSX.Element {
  return <SalesPage kind={SalesDocumentKind.return} />;
}
