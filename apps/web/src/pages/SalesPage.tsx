import { useEffect, useMemo, useState } from 'react';
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
import { StatusBadge, type StatusTone } from '@/components/StatusBadge';
import { IconBarcode, IconClose, IconPrinter } from '@/components/icons';
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
import {
  AccountNature,
  LedgerKind,
  listLedgers,
  type LedgerSummary,
} from '@/lib/ledgers';
import { fetchStockValuation, type StockValuationReport } from '@/lib/stock';
import { collect, numeric, required, useValidation } from '@/lib/validation';
import { useSettings } from '@/stores/settings';
import { DocumentType, useNextNumber } from '@/lib/numbering';
import {
  chargeSetupKey,
  enterableCharges,
  mapCharge,
  useChargeSetup,
  type ChargeDocument,
  type ChargeSetup,
} from '@/lib/charges';
import { useSession } from '@/stores/session';
import {
  SerialLineSummary,
  SerialPickerDialog,
  useSerialNumbersEnabled,
  type SerialDirection,
} from '@/components/SerialNumbers';
import type { ApiError } from '@/lib/api';
import { listCustomers, type CustomerSummary } from '@/lib/customers';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
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
  isReturn,
  listSalesInvoices,
  postSalesInvoice,
  SalesDocumentKind,
  SalesInvoiceStatus,
  SalesPaymentMode,
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
import { moneyAlways } from '@/lib/money';

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

  const columns: readonly GridColumn<SalesInvoiceSummary>[] = [
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
              : row.paymentMode === SalesPaymentMode.online
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
  ];

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
          onError={setError}
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

function AdditionalLedgersSection({
  rows,
  ledgers,
  document,
  setup,
  nonTax,
  onChange,
}: {
  readonly rows: readonly AdditionalLedgerItem[];
  readonly ledgers: readonly LedgerSummary[];
  /** The document the charges are set up for, when a new one is added from here. */
  readonly document: ChargeDocument;
  /** The firm's charges for a sale; offered and signed from it once it arrives. */
  readonly setup: readonly ChargeSetup[] | undefined;
  readonly nonTax: boolean;
  readonly onChange: (rows: readonly AdditionalLedgerItem[]) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const can = useSession((state) => state.can);
  const [settingUp, setSettingUp] = useState(false);

  // Only the charges the firm set up for a sale in this mode can be saved on one; the
  // whole chart used to be offered and the server refused all the rest.
  const options = setup
    ? enterableCharges(setup, nonTax).map((charge) => ({
        value: charge.ledgerId,
        label: `${charge.code} — ${charge.name}`,
      }))
    : ledgers.map((l) => ({ value: l.ledgerId, label: `${l.code} — ${l.name}` }));

  const adds = (row: AdditionalLedgerItem): boolean =>
    setup?.find((charge) => charge.ledgerId === row.ledgerId)?.isAddition ??
    row.isAddition;

  const removeRow = (id: string): void => {
    onChange(rows.filter((r) => r.id !== id));
  };

  const updateRow = (id: string, patch: Partial<AdditionalLedgerItem>): void => {
    onChange(rows.map((r) => (r.id === id ? { ...r, ...patch } : r)));
  };

  return (
    <div className="flex flex-col gap-2 rounded-xl border border-line bg-surface p-3">
      <div className="flex items-center justify-between border-b border-line pb-1.5">
        <span className="text-xs font-semibold uppercase tracking-wider text-ink-muted">
          {t('sales.additionalLedgers')}
        </span>
        <div className="flex items-center gap-1">
          {/* Any expense or income ledger can be carried on a sale; the first time it
              is, it is set up here - with which way it moves the total - rather than
              the invoice being abandoned for a settings screen. */}
          {can('accounting:additional-ledger:create') && (
            <button
              type="button"
              onClick={() => setSettingUp(true)}
              className="btn-ghost btn-xs"
            >
              {t('sales.setUpCharge')}
            </button>
          )}
          <button
            type="button"
            onClick={() =>
              onChange([
                ...rows,
                {
                  id: Math.random().toString(36).slice(2, 9),
                  ledgerId: '',
                  isAddition: true,
                  amount: '',
                },
              ])
            }
            className="btn-ghost btn-xs"
          >
            {t('sales.addCharge')}
          </button>
        </div>
      </div>

      {settingUp && (
        <ChargeSetupDialog
          document={document}
          ledgers={ledgers}
          setup={setup ?? []}
          onClose={() => setSettingUp(false)}
          onDone={(charge) => {
            setSettingUp(false);
            // Onto the first empty row, or a new one, ready for its amount.
            const empty = rows.find((row) => row.ledgerId === '');
            const chosen = {
              ledgerId: charge.ledgerId,
              isAddition: charge.isAddition,
            };
            onChange(
              empty
                ? rows.map((row) => (row.id === empty.id ? { ...row, ...chosen } : row))
                : [
                    ...rows,
                    { id: Math.random().toString(36).slice(2, 9), amount: '', ...chosen },
                  ],
            );
          }}
        />
      )}

      {rows.length === 0 ? (
        <p className="py-2 text-center text-xs text-ink-muted">
          {t('sales.noAdditionalLedgers')}
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-xs">
            <thead>
              <tr className="text-ink-muted">
                <th className="pb-1 text-start">{t('sales.ledger')}</th>
                <th className="w-24 pb-1 text-center">{t('sales.taxTreatment')}</th>
                <th className="w-24 pb-1 text-end">{t('sales.amount')}</th>
                <th className="w-7" />
              </tr>
            </thead>
            <tbody className="divide-y divide-line/60">
              {rows.map((row) => (
                <tr key={row.id}>
                  <td className="pe-2 py-1">
                    <SearchSelect
                      value={row.ledgerId}
                      onChange={(ledgerId) =>
                        updateRow(row.id, {
                          ledgerId,
                          isAddition:
                            setup?.find((charge) => charge.ledgerId === ledgerId)
                              ?.isAddition ?? row.isAddition,
                        })
                      }
                      options={options}
                      size="sm"
                      label={t('sales.chooseLedger')}
                      placeholder={t('sales.chooseLedger')}
                    />
                  </td>
                  {/* Which way it moves the total is the charge's own, set up once for
                      the firm - the server posts it that way whatever a row says, so
                      the screen shows it rather than asking. */}
                  <td className="px-1 py-1 text-center text-xs font-medium whitespace-nowrap">
                    {row.ledgerId === '' ? (
                      ''
                    ) : adds(row) ? (
                      <span className="text-emerald-700 dark:text-emerald-400">
                        {t('sales.addition')}
                      </span>
                    ) : (
                      <span className="text-red-600">{t('sales.deduction')}</span>
                    )}
                  </td>
                  <td className="px-1 py-1 text-end">
                    <input
                      type="number"
                      inputMode="decimal"
                      value={row.amount}
                      onChange={(e) => updateRow(row.id, { amount: e.target.value })}
                      className="field-input-sm w-20 text-end font-mono tabular-nums"
                    />
                  </td>
                  <td className="ps-1 py-1 text-end">
                    <button
                      type="button"
                      onClick={() => removeRow(row.id)}
                      className="rounded p-1 text-ink-muted transition hover:text-red-600"
                      aria-label={t('sales.removeLine')}
                    >
                      <IconClose className="size-3.5" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/**
 * Sets a ledger up as an additional ledger on this kind of document.
 *
 * Offers the firm's charge ledgers and its income and expense ledgers that are not set
 * up yet - a party, cash, bank or tax account already has its own place on a document -
 * and asks only which way it moves the total, which is the one thing the server cannot
 * guess.
 */
function ChargeSetupDialog({
  document,
  ledgers,
  setup,
  onClose,
  onDone,
}: {
  readonly document: ChargeDocument;
  readonly ledgers: readonly LedgerSummary[];
  readonly setup: readonly ChargeSetup[];
  readonly onClose: () => void;
  readonly onDone: (charge: ChargeSetup) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [ledgerId, setLedgerId] = useState('');
  const [isAddition, setIsAddition] = useState(true);

  const offered = useMemo(() => {
    const already = new Set(setup.map((charge) => charge.ledgerId));

    return ledgers
      .filter(
        (ledger) =>
          (ledger.kind === LedgerKind.additionalCharge ||
            (ledger.kind === LedgerKind.general &&
              (ledger.nature === AccountNature.income ||
                ledger.nature === AccountNature.expense))) &&
          !already.has(ledger.ledgerId),
      )
      .map((ledger) => ({
        value: ledger.ledgerId,
        label: `${ledger.code} — ${ledger.name}`,
        detail: ledger.groupName,
      }));
  }, [ledgers, setup]);

  const save = useMutation<ChargeSetup, ApiError>({
    mutationFn: () => mapCharge(document, ledgerId, isAddition),
    onSuccess: async (charge) => {
      await queryClient.invalidateQueries({ queryKey: chargeSetupKey(document) });
      onDone(charge);
    },
  });

  return (
    <Modal title={t('sales.setUpCharge')} size="form" onClose={onClose}>
      <div className="space-y-4">
        {save.isError && (
          <p className="alert-error">{save.error.detail || save.error.code}</p>
        )}

        <p className="text-xs text-ink-muted">{t('sales.setUpChargeHint')}</p>

        <FormField label={t('sales.ledger')} required>
          <SearchSelect
            value={ledgerId}
            onChange={setLedgerId}
            options={offered}
            label={t('sales.ledger')}
            placeholder={t('sales.chooseLedger')}
          />
        </FormField>

        <SelectField<string>
          label={t('sales.taxTreatment')}
          value={isAddition ? 'add' : 'deduct'}
          onChange={(value) => setIsAddition(value === 'add')}
          options={[
            { value: 'add', label: t('sales.addition') },
            { value: 'deduct', label: t('sales.deduction') },
          ]}
        />

        <div className="flex justify-end gap-2 border-t border-line pt-3">
          <ModalButton onClick={onClose}>{t('common.cancel')}</ModalButton>
          <ModalButton
            primary
            disabled={ledgerId === '' || save.isPending}
            onClick={() => save.mutate()}
          >
            {save.isPending ? t('common.saving') : t('sales.setUp')}
          </ModalButton>
        </div>
      </div>
    </Modal>
  );
}

/** Enters a draft: the header, then the lines. */
function EntryDialog({
  initialKind = SalesDocumentKind.invoice,
  editing,
  onClose,
  onSaved,
  onError,
}: {
  readonly initialKind?: number;
  /** A saved document being corrected. Absent, a new one is being entered. */
  readonly editing?: SalesInvoiceDetail;
  readonly onClose: () => void;
  /** With the document's id when it should open for printing. */
  readonly onSaved: (message: string, printId?: string) => void;
  readonly onError: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();
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

    if (!editing) return fallback;

    return editing.mode === TaxMode.nonTax ? 'NT' : fallback === 'NT' ? 'TAX' : fallback;
  });
  const [rateType, setRateType] = useState<'retail' | 'wholesale' | 'mrp'>(
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
  const [receivedInto, setReceivedInto] = useState(editing?.receivedIntoLedgerId ?? '');
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

    const targetType = isReturn(initialKind) ? 'SalesReturn' : 'Sales';
    return (settings.defaultAdditionalLedgers ?? [])
      .filter((l) => l.transactionType === targetType && l.isDefaultActive)
      .map((l) => ({
        id: Math.random().toString(36).slice(2, 9),
        ledgerId: l.ledgerId,
        isAddition: l.isAddition,
        amount: l.defaultAmount || '0.00',
      }));
  });
  const [activeBottomTab, setActiveBottomTab] = useState<'general' | 'shipping'>(
    'general',
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

  // Where a payment goes: the cash in the drawer, or the bank an online payment lands in.
  const cashAccounts = useMemo(
    () => (ledgers.data ?? []).filter((ledger) => ledger.kind === LedgerKind.cash),
    [ledgers.data],
  );
  const bankAccounts = useMemo(
    () => (ledgers.data ?? []).filter((ledger) => ledger.kind === LedgerKind.bank),
    [ledgers.data],
  );
  const receivingAccounts =
    paymentMode === SalesPaymentMode.online
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
          (paymentMode === SalesPaymentMode.online
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
  const employeeName = (id: string): string =>
    employees.find((employee) => employee.ledgerId === id)?.name ?? '—';

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
    { key: 'taxPercent', label: t('sales.taxPercent'), defaultOn: true },
    { key: 'taxAmount', label: t('sales.taxAmount'), defaultOn: false },
    { key: 'net', label: t('sales.net'), defaultOn: true, fixed: true },
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

  const productOptions = useMemo(
    () =>
      (products.data ?? []).map((product) =>
        productOption(product, onHand.get(product.id) ?? 0),
      ),
    [products.data, onHand],
  );

  useEffect(() => {
    if (warehouseId !== '' || !warehouses.data) {
      return;
    }

    const chosen =
      warehouses.data.find((warehouse) => warehouse.id === preferredWarehouseId) ??
      warehouses.data.find((warehouse) => warehouse.isDefault) ??
      (warehouses.data.length === 1 ? warehouses.data[0] : undefined);

    if (chosen) {
      setWarehouseId(chosen.id);
    }
  }, [warehouses.data, preferredWarehouseId, warehouseId]);

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

  const change = (index: number, patch: Partial<DraftLine>): void =>
    setLines((previous) =>
      previous.map((line, at) => (at === index ? { ...line, ...patch } : line)),
    );

  const serialsOn = settings.enableSerialNumbers;

  /** Adds one unit of a product to the invoice, with its serial number if it has one. */
  const addScanned = (found: ProductSummary, serial: string | null): void => {
    const defaultTax = lineTaxRate(taxMode, found, settings.defaultTaxRate);
    const baseRate =
      rateType === 'wholesale'
        ? found.wholesaleRate || found.retailRate
        : rateType === 'mrp'
          ? found.maximumRetailPrice || found.retailRate
          : found.retailRate;
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

  const handleRateTypeChange = (newRateType: 'retail' | 'wholesale' | 'mrp'): void => {
    setRateType(newRateType);
    setLines((prev) =>
      prev.map((l) => {
        if (!l.productId) return l;
        const prod = products.data?.find((p) => p.id === l.productId);
        if (!prod) return l;
        const baseRate =
          newRateType === 'wholesale'
            ? prod.wholesaleRate || prod.retailRate
            : newRateType === 'mrp'
              ? prod.maximumRetailPrice || prod.retailRate
              : prod.retailRate;
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
      onError(t('sales.noReceivingAccount'));
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
      onError(api.detail || api.code);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title={
        editing
          ? t('sales.editDocument', { number: editing.header.number })
          : isReturn(kind)
            ? t('sales.newReturn')
            : t('sales.newInvoice')
      }
      onClose={onClose}
      size="fullscreen"
    >
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
      {/* Four groups of fields: two to a row until there is room for all four,
          which leaves the paired fields inside each group a usable width - at
          three or four to a row on a tablet the dates, the warehouse and the
          salesman were cut to a few characters each. */}
      <div className="grid grid-cols-1 gap-3 rounded-2xl border border-line bg-surface-2/40 p-3 sm:grid-cols-2 sm:p-4 xl:grid-cols-4">
        {/* Document Numbers */}
        <div className="space-y-2">
          <TextField
            label={
              isReturn(kind) ? t('sales.returnNo') || 'Return No' : t('sales.invoiceNo')
            }
            value={nextInvoiceNo}
            disabled
            onChange={() => {}}
            className="bg-surface-3/50 font-mono font-semibold"
            size="sm"
          />
          <TextField
            label={t('sales.referenceNo')}
            value={reference}
            onChange={setReference}
            placeholder={nextNumber ?? ''}
            size="sm"
          />
        </div>

        {/* Column 2: Customer & Payment Mode */}
        <div className="space-y-2">
          <FormField label={t('sales.customer')} required error={errors['customerId']}>
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
              placeholder={t('sales.chooseCustomer')}
              size="sm"
            />
          </FormField>

          <SelectField<number>
            label={t('sales.paymentMode')}
            value={paymentMode}
            onChange={(val) => setPaymentMode(Number(val))}
            options={[
              { value: SalesPaymentMode.cash, label: t('paymentModes.cash') },
              { value: SalesPaymentMode.online, label: t('paymentModes.online') },
              { value: SalesPaymentMode.credit, label: t('paymentModes.credit') },
            ]}
            size="sm"
          />
        </div>

        {/* Column 3: Sales Staff & Barcode Scanner */}
        <div className="space-y-2">
          <div className="grid grid-cols-2 gap-2">
            <FormField label={t('sales.salesman')}>
              <SearchSelect
                value={employeeId(salesman) ?? ''}
                onChange={setSalesman}
                options={employeeOptions}
                clearable
                size="sm"
                label={t('sales.salesman')}
                placeholder={
                  employees.length === 0 ? t('sales.noEmployees') : t('common.choose')
                }
              />
            </FormField>
            <FormField label={t('sales.billingman')}>
              <SearchSelect
                value={employeeId(billingman) ?? ''}
                onChange={setBillingman}
                options={employeeOptions}
                clearable
                size="sm"
                label={t('sales.billingman')}
                placeholder={
                  employees.length === 0 ? t('sales.noEmployees') : t('common.choose')
                }
              />
            </FormField>
          </div>

          <div>
            <label className="block text-xs text-ink-muted mb-1 font-medium">
              {t('sales.barcode')}
            </label>
            <div className="relative flex items-center">
              <input
                type="text"
                value={barcodeInput}
                onChange={(e) => setBarcodeInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    void handleBarcodeScan(barcodeInput);
                  }
                }}
                placeholder={t('sales.barcodePlaceholder')}
                className="field-input-sm w-full pe-8 font-mono text-xs"
              />
              <button
                type="button"
                onClick={() => void handleBarcodeScan(barcodeInput)}
                className="absolute end-1.5 p-1 text-ink-muted hover:text-brand-600 transition"
                title="Scan / Read Barcode"
              >
                <IconBarcode className="size-4" />
              </button>
            </div>
            {barcodeNotice && (
              <span className="block mt-1 text-[11px] font-medium text-brand-600 dark:text-brand-300 truncate">
                {barcodeNotice}
              </span>
            )}
          </div>
        </div>

        {/* Column 4: Date, Mode, Rate & Warehouse */}
        <div className="space-y-2">
          <div className="grid grid-cols-2 gap-2">
            <DateField
              label={t('sales.date')}
              required
              value={date}
              onChange={setDate}
              error={errors['date']}
              size="sm"
            />
            <SelectField<'NT' | 'TAX' | 'GST'>
              label={t('sales.taxMode')}
              value={taxMode}
              onChange={(val) => handleTaxModeChange(val as 'NT' | 'TAX' | 'GST')}
              options={[
                { value: 'NT', label: 'NT (0%)' },
                { value: 'TAX', label: 'TAX (5%)' },
                { value: 'GST', label: 'GST (18%)' },
              ]}
              size="sm"
            />
          </div>

          <div className="grid grid-cols-2 gap-2">
            <SelectField<'retail' | 'wholesale' | 'mrp'>
              label={t('sales.salesRateType')}
              value={rateType}
              onChange={(val) =>
                handleRateTypeChange(val as 'retail' | 'wholesale' | 'mrp')
              }
              options={[
                { value: 'retail', label: 'Retail' },
                { value: 'wholesale', label: 'Wholesale' },
                { value: 'mrp', label: 'MRP' },
              ]}
              size="sm"
            />
            <FormField
              label={t('sales.warehouse')}
              required
              error={errors['warehouseId']}
            >
              <SearchSelect
                value={warehouseId}
                onChange={setWarehouseId}
                options={(warehouses.data ?? []).map((warehouse) => ({
                  value: warehouse.id,
                  label: `${warehouse.code} — ${warehouse.name}`,
                  ...(warehouse.isDefault ? { meta: t('settings.masterDefault') } : {}),
                }))}
                invalid={errors['warehouseId'] !== undefined}
                label={t('sales.warehouse')}
                placeholder={t('sales.chooseWarehouse')}
                size="sm"
              />
            </FormField>
          </div>
        </div>
      </div>

      {/* Sales Return against invoice selector */}
      {isReturn(kind) && (
        <div className="flex flex-wrap items-center justify-between gap-3 bg-surface-2/60 p-2.5 rounded-xl border border-line">
          <div className="flex items-center gap-2">
            <span className="text-xs font-semibold text-ink-muted">
              {t('sales.againstInvoice')}:
            </span>
            <div className="w-80">
              <SearchSelect
                value={returnsInvoiceId}
                onChange={setReturnsInvoiceId}
                clearable
                size="sm"
                label={t('sales.againstInvoice')}
                placeholder={t('sales.noInvoice')}
                options={(returnable.data?.items ?? []).map((inv) => ({
                  value: inv.salesInvoiceId,
                  label: inv.number,
                  detail: inv.date,
                  meta: moneyAlways(inv.total),
                }))}
              />
            </div>
          </div>
          <p className="text-xs text-amber-700 dark:text-amber-400 font-medium">
            {t('sales.returnHint')}
          </p>
        </div>
      )}

      {/* Line Items Table. The columns are the user's to choose, remembered in this
          browser, for the same reason the purchase lines' are. Below `lg` the product
          and the figures are a little narrower, so a portrait tablet has every
          column on screen rather than the tax and the net behind a sideways
          scroll; the purchase and order lines do the same. */}
      <div className="flex justify-end">{lineColumns.picker}</div>
      <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
        <table className="line-table sm:min-w-[40rem] lg:min-w-[46rem]">
          <thead className="text-start text-xs text-ink-muted">
            <tr>
              <th className="px-2 py-1 text-start">{t('sales.product')}</th>
              <ProductDetailHeaders shows={lineColumns.shows} labels={t} />
              <th className="px-2 py-1 text-end">{t('sales.quantity')}</th>
              {enableFreeQty && lineColumns.shows('freeQuantity') && (
                <th className="px-2 py-1 text-end">{t('sales.freeQuantity')}</th>
              )}
              <th className="px-2 py-1 text-end">
                {t('sales.rate')}
                {reverseCalc ? ` (${t('sales.inclusiveRate')})` : ''}
              </th>
              {enableDiscount && lineColumns.shows('discount') && (
                <th className="px-2 py-1 text-end">{t('sales.itemDiscount')}</th>
              )}
              {lineColumns.shows('taxPercent') && (
                <th className="px-2 py-1 text-end">{t('sales.taxPercent')}</th>
              )}
              {lineColumns.shows('taxAmount') && (
                <th className="px-2 py-1 text-end">{t('sales.taxAmount')}</th>
              )}
              <th className="px-2 py-1 text-end">{t('sales.net')}</th>
              <th className="line-action-column" />
            </tr>
          </thead>

          <tbody>
            {lines.map((line, index) => (
              <LineRow
                key={index}
                line={line}
                products={products.data ?? []}
                productOptions={productOptions}
                warehouseId={warehouseId}
                serialDirection={isReturn(kind) ? 'back' : 'out'}
                rateType={rateType}
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
      </div>

      <div className="flex flex-wrap items-center gap-4">
        <button
          type="button"
          onClick={() =>
            setLines((previous) => [
              ...previous,
              {
                ...emptyLine,
                taxPercentage: String(modeTaxRate(taxMode, settings.defaultTaxRate)),
              },
            ])
          }
          className="btn-secondary btn-sm"
        >
          {t('sales.addLine')}
        </button>
      </div>

      {/* Bottom Master Section: Customer Details + Additional Ledgers + Partial Payment & Totals */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 pt-2">
        {/* Panel 1: Customer Details & Narration */}
        <div
          className={clsx(
            'flex flex-col gap-2 rounded-xl border border-line bg-surface p-3',
            'lg:col-span-4',
          )}
        >
          <div className="flex items-center gap-2 border-b border-line pb-1.5">
            <button
              type="button"
              onClick={() => setActiveBottomTab('general')}
              className={clsx(
                'text-xs font-semibold uppercase tracking-wider transition px-1 py-0.5 border-b-2',
                activeBottomTab === 'general'
                  ? 'border-brand-600 text-brand-700 dark:text-brand-300'
                  : 'border-transparent text-ink-muted hover:text-ink',
              )}
            >
              {t('sales.generalTab')}
            </button>
            <button
              type="button"
              onClick={() => setActiveBottomTab('shipping')}
              className={clsx(
                'text-xs font-semibold uppercase tracking-wider transition px-1 py-0.5 border-b-2',
                activeBottomTab === 'shipping'
                  ? 'border-brand-600 text-brand-700 dark:text-brand-300'
                  : 'border-transparent text-ink-muted hover:text-ink',
              )}
            >
              {t('sales.shippingTab')}
            </button>
          </div>

          {activeBottomTab === 'general' ? (
            <div className="space-y-2 text-xs">
              <div className="grid grid-cols-2 gap-2 text-ink-muted">
                <div>
                  <span className="block text-[10px] uppercase font-bold text-ink-muted/80">
                    {t('customers.address')}
                  </span>
                  <p className="truncate text-ink font-medium">
                    {selectedCustomer?.contact.addressLine1 ||
                      selectedCustomer?.contact.addressLine2 ||
                      '—'}
                  </p>
                </div>
                <div>
                  <span className="block text-[10px] uppercase font-bold text-ink-muted/80">
                    {t('customers.mobile')}
                  </span>
                  <p className="truncate font-mono text-ink">
                    {selectedCustomer?.contact.mobileNumber ||
                      selectedCustomer?.contact.phone ||
                      '—'}
                  </p>
                </div>
              </div>

              <div>
                <label className="block text-[10px] uppercase font-bold text-ink-muted/80 mb-1">
                  {t('sales.narration')}
                </label>
                <textarea
                  rows={2}
                  value={narration}
                  onChange={(e) => setNarration(e.target.value)}
                  placeholder={t('sales.narration')}
                  className="w-full rounded-lg border border-line bg-surface p-2 text-xs text-ink focus:border-brand-500 focus:outline-none resize-none"
                />
              </div>
            </div>
          ) : (
            <div className="space-y-2 text-xs">
              <div className="grid grid-cols-2 gap-2">
                <div>
                  <span className="block text-[10px] uppercase font-bold text-ink-muted/80">
                    {t('sales.salesman')}
                  </span>
                  <p className="text-ink font-medium">{employeeName(salesman)}</p>
                </div>
                <div>
                  <span className="block text-[10px] uppercase font-bold text-ink-muted/80">
                    {t('sales.billingman')}
                  </span>
                  <p className="text-ink font-medium">{employeeName(billingman)}</p>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* Panel 2: Charges - freight, delivery, a discount off the whole bill. Always
            here: they used to appear only when Settings had defaults, so there was no
            way to put a charge on an invoice that did not start with one. */}
        <div className="lg:col-span-4">
          <AdditionalLedgersSection
            rows={additionalLedgers}
            ledgers={ledgers.data ?? []}
            document={isReturn(kind) ? 'salesReturn' : 'sales'}
            setup={chargeSetup}
            nonTax={taxMode === 'NT'}
            onChange={setAdditionalLedgers}
          />
        </div>

        {/*
          Panel 3: the bill, what is paid against it, and where that leaves the customer.

          Three blocks because they answer three different questions, and one list of
          figures had been answering all three at once: a "Balance" that was the bill
          minus the cash, beside a "Prev. Balance" that was the customer's opening
          balance from the day the firm started, beside a grand total nobody could tell
          the paid amount was taken from.
        */}
        <div
          className={clsx(
            'rounded-xl border border-line bg-surface-2/60 p-3 flex flex-col gap-3 text-xs',
            'lg:col-span-4',
          )}
        >
          <section aria-label={t('sales.billSection')} className="space-y-1.5">
            <h3 className="text-[10px] font-bold tracking-wide text-ink-muted uppercase">
              {t('sales.billSection')}
            </h3>
            <div className="flex justify-between text-ink-muted">
              <span>{t('sales.taxable')}</span>
              <span className="font-mono tabular-nums">
                {moneyAlways(totals.taxable)}
              </span>
            </div>
            {totals.tax > 0 && (
              <div className="flex justify-between text-ink-muted">
                <span>
                  {t('sales.tax')} ({taxMode})
                </span>
                <span className="font-mono tabular-nums">{moneyAlways(totals.tax)}</span>
              </div>
            )}
            {additionalLedgersTotal !== 0 && (
              <div className="flex justify-between text-ink-muted">
                <span>{t('sales.additionalLedgersTotal')}</span>
                <span className="font-mono tabular-nums">
                  {additionalLedgersTotal >= 0 ? '+' : ''}
                  {moneyAlways(additionalLedgersTotal)}
                </span>
              </div>
            )}
            <div className="flex justify-between border-t border-line pt-1 text-sm font-bold text-ink">
              <span>
                {isReturn(kind) ? t('sales.returnTotal') : t('sales.billTotal')}
              </span>
              <span className="font-mono text-base text-brand-700 tabular-nums dark:text-brand-300">
                {moneyAlways(grandTotal)}
              </span>
            </div>
          </section>

          {!isReturn(kind) && (
            <section
              aria-label={t('sales.paymentSection')}
              className="space-y-2 border-t border-line/70 pt-2"
            >
              <h3 className="text-[10px] font-bold tracking-wide text-ink-muted uppercase">
                {t('sales.paymentSection')}
              </h3>
              <div className="grid grid-cols-2 gap-2">
                <label className="block">
                  <span className="mb-1 block text-[10px] font-bold text-ink-muted uppercase">
                    {t('sales.receivedNow')}
                  </span>
                  <input
                    type="number"
                    inputMode="decimal"
                    min="0"
                    value={paidAmount}
                    placeholder={
                      paymentMode === SalesPaymentMode.credit
                        ? '0.00'
                        : moneyAlways(grandTotal)
                    }
                    onChange={(e) => setPaidAmount(e.target.value)}
                    className="field-input-sm w-full text-end font-mono font-semibold tabular-nums"
                  />
                </label>
                <label className="block min-w-0">
                  <span className="mb-1 block text-[10px] font-bold text-ink-muted uppercase">
                    {t('sales.receivedInto')}
                  </span>
                  <select
                    value={receivingAccountId}
                    onChange={(e) => setReceivedInto(e.target.value)}
                    disabled={effectivePaid <= 0}
                    className="field-input-sm w-full"
                  >
                    {receivingAccounts.length === 0 && (
                      <option value="">{t('sales.noReceivingAccount')}</option>
                    )}
                    {receivingAccounts.map((ledger) => (
                      <option key={ledger.ledgerId} value={ledger.ledgerId}>
                        {ledger.name}
                      </option>
                    ))}
                  </select>
                </label>

                {paymentMode === SalesPaymentMode.online && (
                  <label className="col-span-2 block">
                    <span className="mb-1 block text-[10px] font-bold text-ink-muted uppercase">
                      {t('sales.transactionId')}
                    </span>
                    <input
                      value={paymentReference}
                      maxLength={60}
                      onChange={(e) => setPaymentReference(e.target.value)}
                      placeholder={t('sales.transactionIdPlaceholder')}
                      className="field-input-sm w-full font-mono"
                    />
                  </label>
                )}

                {paymentMode === SalesPaymentMode.cash && (
                  <>
                    <label className="block">
                      <span className="mb-1 block text-[10px] font-bold text-ink-muted uppercase">
                        {t('sales.cashGiven')}
                      </span>
                      <input
                        type="number"
                        inputMode="decimal"
                        min="0"
                        value={cashGiven}
                        placeholder="0.00"
                        onChange={(e) => setCashGiven(e.target.value)}
                        className="field-input-sm w-full text-end font-mono tabular-nums"
                      />
                    </label>
                    <div>
                      <span className="mb-1 block text-[10px] font-bold text-ink-muted uppercase">
                        {t('sales.changeBack')}
                      </span>
                      <div className="flex h-8 items-center justify-end font-mono text-sm font-semibold text-emerald-600 tabular-nums dark:text-emerald-400">
                        {moneyAlways(changeBack)}
                      </div>
                    </div>
                  </>
                )}
              </div>
            </section>
          )}

          <section
            aria-label={t('sales.accountSection')}
            className="space-y-1.5 border-t border-line/70 pt-2"
          >
            <h3 className="text-[10px] font-bold tracking-wide text-ink-muted uppercase">
              {t('sales.accountSection')}
            </h3>
            <div className="flex justify-between text-ink-muted">
              <span>{t('sales.prevBalance')}</span>
              <span className="font-mono tabular-nums">
                {customerId === '' || account.isPending ? '—' : moneyAlways(prevBalance)}
              </span>
            </div>
            <div className="flex justify-between text-ink-muted">
              <span>{isReturn(kind) ? t('sales.thisReturn') : t('sales.thisBill')}</span>
              <span className="font-mono tabular-nums">
                {isReturn(kind) ? '−' : '+'}
                {moneyAlways(grandTotal)}
              </span>
            </div>
            {effectivePaid > 0 && (
              <div className="flex justify-between text-ink-muted">
                <span>{t('sales.receivedNow')}</span>
                <span className="font-mono tabular-nums">
                  −{moneyAlways(effectivePaid)}
                </span>
              </div>
            )}
            <div className="flex justify-between border-t border-line pt-1 text-sm font-bold text-ink">
              <span>{t('sales.balanceAfter')}</span>
              <span
                className={clsx(
                  'font-mono tabular-nums',
                  balanceAfter > 0.004 && 'text-red-600 dark:text-red-400',
                )}
              >
                {moneyAlways(balanceAfter)}
              </span>
            </div>
            {overLimitBy > 0 && (
              <p
                role="alert"
                className="text-[11px] font-medium text-amber-700 dark:text-amber-400"
              >
                {t('sales.overCreditLimit', {
                  limit: moneyAlways(account.data?.creditLimit ?? 0),
                  by: moneyAlways(overLimitBy),
                })}
                {creditRule === CreditLimitPolicy.block &&
                  ` ${t('sales.creditLimitBlocks')}`}
              </p>
            )}
          </section>
        </div>
      </div>

      {/* Save posts: the counter's sale is made in one press. A draft is still there
          for a document that is not finished, and printing straight after is the
          other thing a counter does with nearly every sale. */}
      <div className="flex flex-wrap justify-end gap-2 pt-2 border-t border-line/60">
        <ModalButton onClick={onClose}>{t('sales.close')}</ModalButton>
        {editing?.header.status !== SalesInvoiceStatus.posted && (
          <ModalButton disabled={busy} onClick={() => void save(false)}>
            {t('sales.saveDraft')}
          </ModalButton>
        )}
        <ModalButton disabled={busy} onClick={() => void save(true, true)}>
          {t('sales.saveAndPrint')}
        </ModalButton>
        <ModalButton primary disabled={busy} onClick={() => void save(true)}>
          {busy ? t('common.saving') : t('sales.save')}
        </ModalButton>
      </div>
    </Modal>
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
  readonly rateType: 'retail' | 'wholesale' | 'mrp';
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
        });
      }
    }
  }, [autoBatch, needsBatch, line.batchNumber, batches.data]);

  const handleProductSelect = (value: string): void => {
    const chosen = products.find((candidate) => candidate.id === value);
    if (chosen) {
      const baseRate =
        rateType === 'wholesale'
          ? chosen.wholesaleRate || chosen.retailRate
          : rateType === 'mrp'
            ? chosen.maximumRetailPrice || chosen.retailRate
            : chosen.retailRate;
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
      <tr className="border-t border-line">
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
            className="px-2 py-1 text-end font-mono tabular-nums text-ink-muted"
          >
            {Number.isFinite(net)
              ? moneyAlways(roundMoney((net * Number(line.taxPercentage || 0)) / 100))
              : '—'}
          </td>
        )}
        <td data-label={t('sales.net')} className="px-2 py-1 text-end font-mono">
          {Number.isFinite(net) ? moneyAlways(net) : '—'}
        </td>
        <td className="px-2 py-1 text-end">
          {removable && (
            <button
              type="button"
              onClick={onRemove}
              className="rounded px-1.5 py-0.5 text-xs font-medium text-red-600 transition hover:bg-red-50 dark:text-red-400 dark:hover:bg-red-500/10"
            >
              {t('sales.removeLine')}
            </button>
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
                          'inline-flex items-center gap-1 rounded px-2 py-0.5 text-xs font-mono font-medium',
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
                                <span className="text-[10px] text-purple-700 bg-purple-50 dark:bg-purple-950/40 dark:text-purple-300 px-1 rounded font-mono">
                                  IMEI/Serial: {line.serialNumberIds.join(', ')}
                                </span>
                              </div>
                            )}
                          </td>
                          <td
                            data-label={t('sales.quantity')}
                            className="px-2 py-1 text-end font-mono"
                          >
                            {line.quantity}
                          </td>
                          <td
                            data-label={t('sales.rate')}
                            className="px-2 py-1 text-end font-mono"
                          >
                            {moneyAlways(line.rate)}
                          </td>
                          <td
                            data-label={t('sales.taxable')}
                            className="px-2 py-1 text-end font-mono"
                          >
                            {moneyAlways(line.taxable)}
                          </td>
                          <td
                            data-label={t('sales.tax')}
                            className="px-2 py-1 text-end font-mono"
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

              {/* What posting produced. A sale leaves two documents on purpose, so the issue
                  is named rather than left for somebody to find in the stock ledger. */}
              {document.stockDocumentId && (
                <p className="text-xs text-ink-muted">{t('sales.postedProduced')}</p>
              )}

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
  return mode === SalesPaymentMode.cash
    ? t('paymentModes.cash')
    : mode === SalesPaymentMode.online
      ? t('paymentModes.online')
      : t('paymentModes.credit');
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
      <div className="font-mono tabular-nums text-ink">{value}</div>
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
          'field-input-sm ms-auto w-20 text-end font-mono tabular-nums lg:w-24',
          error && 'field-invalid',
        )}
      />
      {subtitle && (
        <span className="mt-0.5 block font-mono text-[10px] tabular-nums text-ink-muted text-end">
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
