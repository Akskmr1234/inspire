import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { useModalBehaviour } from '@/components/useModalBehaviour';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { ReportFrame } from '@/components/ReportFrame';
import type { ApiError } from '@/lib/api';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import { listProducts, type ProductSummary } from '@/lib/products';
import { listSuppliers, type SupplierSummary } from '@/lib/suppliers';
import type { PagedResult } from '@/lib/sales';
import {
  cancelPurchaseInvoice,
  createPurchaseInvoice,
  getPurchaseInvoice,
  isPurchaseReturn,
  listPurchaseInvoices,
  postPurchaseInvoice,
  PurchaseDocumentKind,
  PurchaseInvoiceStatus,
  type PurchaseInvoiceDetail,
  type PurchaseInvoiceSummary,
  type PurchaseLineInput,
} from '@/lib/purchase';

const PAGE_SIZE = 25;

/** A line as the screen holds it, before it is worth sending. */
interface DraftLine {
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

const emptyLine: DraftLine = {
  productId: '',
  quantity: '1',
  rate: '',
  taxPercentage: '0',
  discount: '0',
  batchNumber: '',
  expiresOn: '',
  serialNumbers: '',
};

/** Splits a keyed-in block of serial numbers into the numbers it names. */
function splitSerials(entered: string): readonly string[] {
  return entered
    .split(/[\n,;]/)
    .map((number) => number.trim())
    .filter((number) => number !== '');
}

/**
 * Purchases and debit notes.
 *
 * One screen for both, because they are one kind of document. Entering and posting are
 * separate here as they are on the server: a draft has moved nothing and can be corrected
 * while somebody keys it off the supplier's invoice; posting receives the stock, raises
 * the debt and writes the books in one transaction.
 *
 * Cancelling is for a purchase that should never have been entered. Goods the firm has
 * accepted and is sending back go on a purchase return instead — and a cancellation can
 * be refused because the goods are already gone, which is the server's answer to say
 * rather than this screen's to predict.
 */
export function PurchasePage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  const today = new Date().toISOString().slice(0, 10);
  const monthStart = `${today.slice(0, 7)}-01`;

  const [from, setFrom] = useState(monthStart);
  const [to, setTo] = useState(today);
  const [kindFilter, setKindFilter] = useState<number | ''>('');
  const [statusFilter, setStatusFilter] = useState<number | ''>('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const [entering, setEntering] = useState(false);
  const [viewing, setViewing] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const filter = { from, to, kind: kindFilter, status: statusFilter, search };

  const query = useQuery<PagedResult<PurchaseInvoiceSummary>, ApiError>({
    queryKey: ['purchase-invoices', from, to, kindFilter, statusFilter, search, page],
    queryFn: () => listPurchaseInvoices(filter, page, PAGE_SIZE),
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
    { key: 'number', header: t('purchase.number'), value: (row) => row.number },
    {
      key: 'kind',
      header: t('purchase.kind'),
      value: (row) =>
        isPurchaseReturn(row.kind) ? t('purchase.return') : t('purchase.invoice'),
      render: (row) => (
        <span
          className={clsx(
            'rounded px-2 py-0.5 text-xs',
            isPurchaseReturn(row.kind)
              ? 'bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300'
              : 'bg-surface-3 text-ink-muted',
          )}
        >
          {isPurchaseReturn(row.kind) ? t('purchase.return') : t('purchase.invoice')}
        </span>
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
    },
    { key: 'tax', header: t('purchase.tax'), value: (row) => row.tax, numeric: true },
    {
      key: 'total',
      header: t('purchase.total'),
      value: (row) => row.total,
      numeric: true,
    },
    {
      key: 'status',
      header: t('purchase.status'),
      value: (row) => statusLabel(row.status, t),
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <button
          type="button"
          onClick={() => setViewing(row.purchaseInvoiceId)}
          className="rounded-md border border-line px-2 py-0.5 text-xs font-medium text-ink-muted transition hover:border-line-strong hover:bg-surface-3 hover:text-ink active:scale-95"
        >
          {t('purchase.open')}
        </button>
      ),
    },
  ];

  const controls = (
    <div className="toolbar">
      <Field label={t('purchase.from')}>
        <DateInput value={from} onChange={narrow(setFrom)} />
      </Field>

      <Field label={t('purchase.to')}>
        <DateInput value={to} onChange={narrow(setTo)} />
      </Field>

      <Field label={t('purchase.kind')}>
        <Select
          value={kindFilter}
          onChange={narrow<number | ''>(setKindFilter)}
          options={[
            { value: '', label: t('purchase.allKinds') },
            { value: PurchaseDocumentKind.invoice, label: t('purchase.invoice') },
            { value: PurchaseDocumentKind.return, label: t('purchase.return') },
          ]}
        />
      </Field>

      <Field label={t('purchase.status')}>
        <Select
          value={statusFilter}
          onChange={narrow<number | ''>(setStatusFilter)}
          options={[
            { value: '', label: t('purchase.allStatuses') },
            { value: PurchaseInvoiceStatus.draft, label: t('purchase.draft') },
            { value: PurchaseInvoiceStatus.posted, label: t('purchase.posted') },
            { value: PurchaseInvoiceStatus.cancelled, label: t('purchase.cancelled') },
          ]}
        />
      </Field>

      <Field label={t('purchase.search')}>
        <input
          type="search"
          value={search}
          onChange={(event) => narrow(setSearch)(event.target.value)}
          placeholder={t('purchase.searchHint')}
          className="field-input-sm"
        />
      </Field>

      <button
        type="button"
        onClick={() => setEntering(true)}
        className="btn-primary btn-sm self-end py-1.5"
      >
        {t('purchase.new')}
      </button>
    </div>
  );

  return (
    <>
      <ReportFrame title={t('nav.purchase')} controls={controls} query={query}>
        {(result) => (
          <div className="space-y-3">
            {error && <p className="alert-error">{error}</p>}

            {notice && <p className="alert-success">{notice}</p>}

            <DataGrid
              gridKey="purchase-invoices"
              rows={result.items}
              columns={columns}
              rowKey={(row) => row.purchaseInvoiceId}
              emptyMessage={t('purchase.none')}
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

      {/* Outside the frame on purpose: a list that failed to load must still let
          somebody enter a document, and the frame renders its children only on
          success. */}
      {entering && (
        <EntryDialog
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
                total: posted.total.toFixed(2),
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

/** Enters a draft: the header, then the lines. */
function EntryDialog({
  onClose,
  onSaved,
  onError,
}: {
  readonly onClose: () => void;
  readonly onSaved: (message: string) => void;
  readonly onError: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const today = new Date().toISOString().slice(0, 10);

  const [kind, setKind] = useState<number>(PurchaseDocumentKind.invoice);
  const [date, setDate] = useState(today);
  const [supplierId, setSupplierId] = useState('');
  const [warehouseId, setWarehouseId] = useState('');
  const [supplierInvoiceNumber, setSupplierInvoiceNumber] = useState('');
  const [supplierInvoiceDate, setSupplierInvoiceDate] = useState('');
  const [returnsInvoiceId, setReturnsInvoiceId] = useState('');
  const [lines, setLines] = useState<readonly DraftLine[]>([{ ...emptyLine }]);
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

  // Only posted purchases can be returned against, and only this supplier's: offering
  // somebody else's would be offering a mistake.
  const returnable = useQuery<PagedResult<PurchaseInvoiceSummary>, ApiError>({
    queryKey: ['purchase-invoices', 'returnable', supplierId],
    queryFn: () =>
      listPurchaseInvoices(
        {
          kind: PurchaseDocumentKind.invoice,
          status: PurchaseInvoiceStatus.posted,
          supplierLedgerId: supplierId,
        },
        1,
        50,
      ),
    enabled: isPurchaseReturn(kind) && supplierId !== '',
  });

  const totals = useMemo(() => {
    let taxable = 0;
    let tax = 0;

    for (const line of lines) {
      const net = Number(line.quantity) * Number(line.rate) - Number(line.discount || 0);

      if (Number.isFinite(net) && net > 0) {
        taxable += net;
        tax += (net * Number(line.taxPercentage || 0)) / 100;
      }
    }

    return { taxable, tax, total: taxable + tax };
  }, [lines]);

  const change = (index: number, patch: Partial<DraftLine>): void =>
    setLines((previous) =>
      previous.map((line, at) => (at === index ? { ...line, ...patch } : line)),
    );

  const save = async (): Promise<void> => {
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
        date,
        supplierLedgerId: supplierId,
        warehouseId,
        lines: payload,
        kind,
        returnsInvoiceId:
          isPurchaseReturn(kind) && returnsInvoiceId ? returnsInvoiceId : null,
        supplierInvoiceNumber: supplierInvoiceNumber.trim() || null,
        // Only sent with the number it belongs to: a date on its own is a fact about a
        // document nobody can identify, and the server refuses it.
        supplierInvoiceDate: supplierInvoiceNumber.trim()
          ? supplierInvoiceDate || null
          : null,
      });

      onSaved(t('purchase.savedNotice', { number: header.number }));
    } catch (failure) {
      const api = failure as ApiError;
      onError(api.detail || api.code);
    } finally {
      setBusy(false);
    }
  };

  const ready =
    supplierId !== '' &&
    warehouseId !== '' &&
    lines.some((line) => line.productId && Number(line.quantity) > 0);

  return (
    <Dialog
      title={isPurchaseReturn(kind) ? t('purchase.newReturn') : t('purchase.newInvoice')}
      onClose={onClose}
    >
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <Field label={t('purchase.kind')}>
          <Select
            value={kind}
            onChange={(value) => setKind(Number(value))}
            options={[
              { value: PurchaseDocumentKind.invoice, label: t('purchase.invoice') },
              { value: PurchaseDocumentKind.return, label: t('purchase.return') },
            ]}
          />
        </Field>

        <Field label={t('purchase.date')}>
          <DateInput value={date} onChange={setDate} />
        </Field>

        <Field label={t('purchase.supplier')}>
          <Select
            value={supplierId}
            onChange={(value) => setSupplierId(String(value))}
            options={[
              { value: '', label: t('purchase.chooseSupplier') },
              ...(suppliers.data ?? []).map((supplier) => ({
                value: supplier.supplierId,
                label: `${supplier.code} — ${supplier.name}`,
              })),
            ]}
          />
        </Field>

        <Field label={t('purchase.warehouse')}>
          <Select
            value={warehouseId}
            onChange={(value) => setWarehouseId(String(value))}
            options={[
              { value: '', label: t('purchase.chooseWarehouse') },
              ...(warehouses.data ?? []).map((warehouse) => ({
                value: warehouse.id,
                label: warehouse.name,
              })),
            ]}
          />
        </Field>

        <Field label={t('purchase.supplierInvoice')}>
          <input
            value={supplierInvoiceNumber}
            onChange={(event) => setSupplierInvoiceNumber(event.target.value)}
            className="field-input-sm"
          />
        </Field>

        <Field label={t('purchase.supplierInvoiceDate')}>
          <DateInput value={supplierInvoiceDate} onChange={setSupplierInvoiceDate} />
        </Field>

        {isPurchaseReturn(kind) && (
          <Field label={t('purchase.againstInvoice')}>
            <Select
              value={returnsInvoiceId}
              onChange={(value) => setReturnsInvoiceId(String(value))}
              options={[
                { value: '', label: t('purchase.noInvoice') },
                ...(returnable.data?.items ?? []).map((invoice) => ({
                  value: invoice.purchaseInvoiceId,
                  label: `${invoice.number} — ${invoice.total.toFixed(2)}`,
                })),
              ]}
            />
          </Field>
        )}
      </div>

      {/* Said plainly rather than left to be discovered: reclaiming input tax needs the
          supplier's own tax invoice, and the same number twice is refused. */}
      <p className="rounded-lg border border-line bg-surface-2 px-3 py-2 text-xs text-ink-muted">
        {t('purchase.supplierInvoiceHint')}
      </p>

      {isPurchaseReturn(kind) && (
        <p className="alert-warn text-xs">{t('purchase.returnHint')}</p>
      )}

      <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
        <table className="w-full min-w-[46rem] text-sm">
          <thead className="text-start text-xs text-ink-muted">
            <tr>
              <th className="px-2 py-1 text-start">{t('purchase.product')}</th>
              <th className="px-2 py-1 text-end">{t('purchase.quantity')}</th>
              <th className="px-2 py-1 text-end">{t('purchase.rate')}</th>
              <th className="px-2 py-1 text-end">{t('purchase.discount')}</th>
              <th className="px-2 py-1 text-end">{t('purchase.taxPercent')}</th>
              <th className="px-2 py-1 text-end">{t('purchase.net')}</th>
              <th />
            </tr>
          </thead>

          <tbody>
            {lines.map((line, index) => (
              <LineRow
                key={index}
                line={line}
                isReturn={isPurchaseReturn(kind)}
                products={products.data ?? []}
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
          onClick={() => setLines((previous) => [...previous, { ...emptyLine }])}
          className="btn-secondary btn-sm"
        >
          {t('purchase.addLine')}
        </button>

        <span className="ms-auto text-sm">
          {t('purchase.taxable')}:{' '}
          <strong className="font-mono">{totals.taxable.toFixed(2)}</strong>
        </span>
        <span className="text-sm">
          {t('purchase.tax')}:{' '}
          <strong className="font-mono">{totals.tax.toFixed(2)}</strong>
        </span>
        <span className="text-base">
          {t('purchase.total')}:{' '}
          <strong className="font-mono">{totals.total.toFixed(2)}</strong>
        </span>
      </div>

      {/* What the screen adds up is what the lines come to; the server rounds the total
          to the currency and may differ in the last place. */}
      <p className="text-xs text-ink-muted">{t('purchase.totalsHint')}</p>

      <div className="flex justify-end gap-2">
        <DialogButton onClick={onClose}>{t('purchase.close')}</DialogButton>
        <DialogButton primary disabled={!ready || busy} onClick={() => void save()}>
          {t('purchase.saveDraft')}
        </DialogButton>
      </div>
    </Dialog>
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
  removable,
  onChange,
  onRemove,
}: {
  readonly line: DraftLine;
  readonly isReturn: boolean;
  readonly products: readonly ProductSummary[];
  readonly removable: boolean;
  readonly onChange: (patch: Partial<DraftLine>) => void;
  readonly onRemove: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const product = products.find((candidate) => candidate.id === line.productId);
  const net = Number(line.quantity) * Number(line.rate) - Number(line.discount || 0);

  const wanted = Number(line.quantity);
  const needsBatch = product?.tracksBatches === true;
  const needsSerials = product?.tracksSerialNumbers === true;
  const named = splitSerials(line.serialNumbers).length;

  return (
    <>
      <tr className="border-t border-line">
        <td className="px-2 py-1">
          <Select
            value={line.productId}
            onChange={(value) =>
              // The batch and the units belong to the product that was chosen before,
              // so they are dropped rather than carried onto a different one.
              onChange({
                productId: String(value),
                batchNumber: '',
                expiresOn: '',
                serialNumbers: '',
              })
            }
            options={[
              { value: '', label: t('purchase.chooseProduct') },
              ...products.map((candidate) => ({
                value: candidate.id,
                label: `${candidate.code} — ${candidate.description}`,
              })),
            ]}
          />
        </td>
        <NumberCell
          value={line.quantity}
          onChange={(value) => onChange({ quantity: value })}
        />
        <NumberCell value={line.rate} onChange={(value) => onChange({ rate: value })} />
        <NumberCell
          value={line.discount}
          onChange={(value) => onChange({ discount: value })}
        />
        <NumberCell
          value={line.taxPercentage}
          onChange={(value) => onChange({ taxPercentage: value })}
        />
        <td className="px-2 py-1 text-end font-mono">
          {Number.isFinite(net) ? net.toFixed(2) : '—'}
        </td>
        <td className="px-2 py-1 text-end">
          {removable && (
            <button
              type="button"
              onClick={onRemove}
              className="rounded px-1.5 py-0.5 text-xs font-medium text-red-600 transition hover:bg-red-50 dark:text-red-400 dark:hover:bg-red-500/10"
            >
              {t('purchase.removeLine')}
            </button>
          )}
        </td>
      </tr>

      {(needsBatch || needsSerials) && (
        <tr className="border-t border-dashed border-line">
          <td colSpan={7} className="px-2 pb-2">
            <div className="flex flex-wrap items-start gap-4">
              {needsBatch && (
                <>
                  <Field label={t('purchase.batch')}>
                    <input
                      value={line.batchNumber}
                      onChange={(event) => onChange({ batchNumber: event.target.value })}
                      placeholder={
                        isReturn ? t('purchase.batchExisting') : t('purchase.batchHint')
                      }
                      className="field-input-sm w-full sm:w-40"
                    />
                  </Field>

                  {/* An expiry belongs to a batch, and only a purchase can state one:
                      a return is sending back goods whose batch is already on file with
                      its dates, and restating them is refused. */}
                  {!isReturn && (
                    <Field label={t('purchase.expiresOn')}>
                      <DateInput
                        value={line.expiresOn}
                        onChange={(value) => onChange({ expiresOn: value })}
                      />
                    </Field>
                  )}
                </>
              )}

              {needsSerials && (
                <Field
                  label={t('purchase.serialsNamed', {
                    named,
                    wanted: Number.isFinite(wanted) ? wanted : 0,
                  })}
                >
                  <textarea
                    value={line.serialNumbers}
                    onChange={(event) => onChange({ serialNumbers: event.target.value })}
                    rows={2}
                    placeholder={t('purchase.serialsHint')}
                    className="field-input-sm w-full font-mono text-xs sm:w-72"
                  />
                </Field>
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
    <Dialog title={document?.header.number ?? t('purchase.document')} onClose={onClose}>
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
            <Detail label={t('purchase.date')} value={document.date} />
            <Detail
              label={t('purchase.status')}
              value={statusLabel(document.header.status, t)}
            />
            <Detail label={t('purchase.currency')} value={document.currency} />
            <Detail
              label={t('purchase.supplierInvoice')}
              value={document.supplierInvoiceNumber ?? '—'}
            />
            <Detail
              label={t('purchase.supplierInvoiceDate')}
              value={document.supplierInvoiceDate ?? '—'}
            />
            <Detail
              label={t('purchase.taxable')}
              value={document.header.taxable.toFixed(2)}
            />
            <Detail label={t('purchase.tax')} value={document.header.tax.toFixed(2)} />
            <Detail
              label={t('purchase.total')}
              value={document.header.total.toFixed(2)}
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
                      {line.rate.toFixed(2)}
                    </td>
                    <td className="px-2 py-1 text-end font-mono">
                      {line.taxable.toFixed(2)}
                    </td>
                    <td className="px-2 py-1 text-end font-mono">
                      {line.tax.toFixed(2)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* What posting produced. A purchase leaves two documents on purpose, so the
              receipt is named rather than left for somebody to find in the stock ledger. */}
          {document.stockDocumentId && (
            <p className="text-xs text-ink-muted">{t('purchase.postedProduced')}</p>
          )}

          {document.header.status === PurchaseInvoiceStatus.draft && (
            <div className="flex justify-end">
              <DialogButton primary disabled={busy} onClick={() => onPost(id)}>
                {t('purchase.post')}
              </DialogButton>
            </div>
          )}

          {document.header.status === PurchaseInvoiceStatus.posted && (
            <div className="flex flex-wrap items-end justify-end gap-2">
              <Field label={t('purchase.cancelReason')}>
                <input
                  value={reason}
                  onChange={(event) => setReason(event.target.value)}
                  className="field-input-sm"
                />
              </Field>
              <DialogButton
                disabled={busy || reason.trim() === ''}
                onClick={() => onCancel(id, reason.trim())}
              >
                {t('purchase.cancelDocument')}
              </DialogButton>
            </div>
          )}
        </>
      )}

      <div className="flex justify-end">
        <DialogButton onClick={onClose}>{t('purchase.close')}</DialogButton>
      </div>
    </Dialog>
  );
}

function statusLabel(status: number, t: (key: string) => string): string {
  if (status === PurchaseInvoiceStatus.posted) return t('purchase.posted');
  if (status === PurchaseInvoiceStatus.cancelled) return t('purchase.cancelled');

  return t('purchase.draft');
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

function DateInput({
  value,
  onChange,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  return (
    <input
      type="date"
      value={value}
      onChange={(event) => onChange(event.target.value)}
      className="field-input-sm"
    />
  );
}

function NumberCell({
  value,
  onChange,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  return (
    <td className="px-2 py-1">
      <input
        type="number"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="field-input-sm w-24 text-end font-mono tabular-nums"
      />
    </td>
  );
}

function Select<TValue extends string | number>({
  value,
  onChange,
  options,
}: {
  readonly value: TValue;
  readonly onChange: (value: TValue) => void;
  readonly options: readonly { readonly value: TValue; readonly label: string }[];
}): React.JSX.Element {
  return (
    <select
      value={value}
      onChange={(event) =>
        onChange(
          (typeof value === 'number'
            ? Number(event.target.value)
            : event.target.value) as TValue,
        )
      }
      className="field-input-sm"
    >
      {options.map((option) => (
        <option key={String(option.value)} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  );
}

function Dialog({
  title,
  onClose,
  children,
}: {
  readonly title: string;
  readonly onClose: () => void;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  const panel = useModalBehaviour(onClose);

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto overscroll-contain bg-slate-950/50 backdrop-blur-[2px] sm:p-6"
      role="dialog"
      aria-modal="true"
      aria-label={title}
    >
      {/*
        `tabIndex={-1}` so the panel itself can take focus while a document is still
        loading and has no control to give it to yet.
      */}
      <div
        ref={panel as React.RefObject<HTMLDivElement>}
        tabIndex={-1}
        className="animate-rise flex min-h-full w-full max-w-5xl flex-col gap-4 border-line bg-surface p-4 shadow-float outline-none sm:min-h-0 sm:rounded-2xl sm:border sm:p-5"
      >
        <div className="flex items-center justify-between gap-3">
          <h2 className="truncate text-lg font-semibold tracking-tight text-ink">
            {title}
          </h2>
          <button type="button" onClick={onClose} className="btn-icon" aria-label="Close">
            ✕
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

function DialogButton({
  onClick,
  children,
  primary,
  disabled,
}: {
  readonly onClick: () => void;
  readonly children: React.ReactNode;
  readonly primary?: boolean;
  readonly disabled?: boolean;
}): React.JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={clsx(
        'btn px-3 py-1.5 text-sm',
        primary
          ? 'bg-brand-600 text-white shadow-xs hover:bg-brand-700'
          : 'border border-line-strong bg-surface text-ink hover:bg-surface-3',
      )}
    >
      {children}
    </button>
  );
}
