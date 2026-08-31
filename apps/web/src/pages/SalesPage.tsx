import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { Modal, ModalButton } from '@/components/Modal';
import { ReportFrame } from '@/components/ReportFrame';
import type { ApiError } from '@/lib/api';
import { listCustomers, type CustomerSummary } from '@/lib/customers';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import { listProducts, type ProductSummary } from '@/lib/products';
import {
  fetchProductBatches,
  fetchProductSerials,
  type BatchStockRow,
  type SerialNumberView,
} from '@/lib/stock';
import {
  cancelSalesInvoice,
  createSalesInvoice,
  getSalesInvoice,
  isReturn,
  listSalesInvoices,
  postSalesInvoice,
  SalesDocumentKind,
  SalesInvoiceStatus,
  type PagedResult,
  type SalesInvoiceDetail,
  type SalesInvoiceSummary,
  type SalesLineInput,
} from '@/lib/sales';

const PAGE_SIZE = 25;

/** A line as the screen holds it, before it is worth sending. */
interface DraftLine {
  productId: string;
  quantity: string;
  rate: string;
  taxPercentage: string;
  discount: string;
  /** The batch sold, where the product is tracked in batches. */
  batchNumber: string;
  /** The units sold, where the product is tracked by serial number. */
  serialNumbers: readonly string[];
}

const emptyLine: DraftLine = {
  productId: '',
  quantity: '1',
  rate: '',
  taxPercentage: '0',
  discount: '0',
  batchNumber: '',
  serialNumbers: [],
};

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
export function SalesPage(): React.JSX.Element {
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
      key: 'kind',
      header: t('sales.kind'),
      value: (row) => (isReturn(row.kind) ? t('sales.return') : t('sales.invoice')),
      render: (row) => (
        <span
          className={clsx(
            'rounded px-2 py-0.5 text-xs',
            isReturn(row.kind)
              ? 'bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300'
              : 'bg-surface-3 text-ink-muted',
          )}
        >
          {isReturn(row.kind) ? t('sales.return') : t('sales.invoice')}
        </span>
      ),
    },
    { key: 'date', header: t('sales.date'), value: (row) => row.date },
    { key: 'customer', header: t('sales.customer'), value: (row) => row.customerName },
    {
      key: 'reference',
      header: t('sales.reference'),
      value: (row) => row.referenceNumber ?? '',
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
    { key: 'tax', header: t('sales.tax'), value: (row) => row.tax, numeric: true },
    { key: 'total', header: t('sales.total'), value: (row) => row.total, numeric: true },
    {
      key: 'status',
      header: t('sales.status'),
      value: (row) => statusLabel(row.status, t),
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <button
          type="button"
          onClick={() => setViewing(row.salesInvoiceId)}
          className="row-action row-action-neutral"
        >
          {t('sales.open')}
        </button>
      ),
    },
  ];

  const controls = (
    <div className="toolbar">
      <Field label={t('sales.from')}>
        <DateInput value={from} onChange={narrow(setFrom)} />
      </Field>

      <Field label={t('sales.to')}>
        <DateInput value={to} onChange={narrow(setTo)} />
      </Field>

      <Field label={t('sales.kind')}>
        <Select
          value={kindFilter}
          onChange={narrow<number | ''>(setKindFilter)}
          options={[
            { value: '', label: t('sales.allKinds') },
            { value: SalesDocumentKind.invoice, label: t('sales.invoice') },
            { value: SalesDocumentKind.return, label: t('sales.return') },
          ]}
        />
      </Field>

      <Field label={t('sales.status')}>
        <Select
          value={statusFilter}
          onChange={narrow<number | ''>(setStatusFilter)}
          options={[
            { value: '', label: t('sales.allStatuses') },
            { value: SalesInvoiceStatus.draft, label: t('sales.draft') },
            { value: SalesInvoiceStatus.posted, label: t('sales.posted') },
            { value: SalesInvoiceStatus.cancelled, label: t('sales.cancelled') },
          ]}
        />
      </Field>

      <Field label={t('sales.search')}>
        <input
          type="search"
          value={search}
          onChange={(event) => narrow(setSearch)(event.target.value)}
          placeholder={t('sales.searchHint')}
          className="field-input-sm"
        />
      </Field>
    </div>
  );

  return (
    <>
      <ReportFrame title={t('nav.sales')} controls={controls} query={query}>
        {(result) => (
          <div className="space-y-3">
            {error && <p className="alert-error">{error}</p>}

            {notice && <p className="alert-success">{notice}</p>}

            <DataGrid
              gridKey="sales-invoices"
              rows={result.items}
              columns={columns}
              rowKey={(row) => row.salesInvoiceId}
              emptyMessage={t('sales.none')}
              actions={
                <GridAction label={t('sales.new')} onClick={() => setEntering(true)} />
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
          onClose={() => setEntering(false)}
          onSaved={(message) => {
            setEntering(false);
            setNotice(message);
            void queryClient.invalidateQueries({ queryKey: ['sales-invoices'] });
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
              const posted = await postSalesInvoice(id);

              return t('sales.postedNotice', {
                number: posted.number,
                stock: posted.stockDocumentNumber,
                total: posted.total.toFixed(2),
              });
            })
          }
          onCancel={(id, reason) =>
            run(async () => {
              await cancelSalesInvoice(id, reason);
              setViewing(null);

              return t('sales.cancelledNotice');
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

  const [kind, setKind] = useState<number>(SalesDocumentKind.invoice);
  const [date, setDate] = useState(today);
  const [customerId, setCustomerId] = useState('');
  const [warehouseId, setWarehouseId] = useState('');
  const [reference, setReference] = useState('');
  const [returnsInvoiceId, setReturnsInvoiceId] = useState('');
  const [lines, setLines] = useState<readonly DraftLine[]>([{ ...emptyLine }]);
  const [busy, setBusy] = useState(false);

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

  // Only posted invoices can be returned against, and only the ones this customer
  // actually has: offering somebody else's would be offering a mistake.
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
      const payload: readonly SalesLineInput[] = lines
        .filter((line) => line.productId && Number(line.quantity) > 0)
        .map((line) => ({
          productId: line.productId,
          quantity: Number(line.quantity),
          rate: Number(line.rate || 0),
          taxPercentage: Number(line.taxPercentage || 0),
          discount: Number(line.discount || 0),
          batchNumber: line.batchNumber || null,
          // Always sent, empty included: the server reads an empty list as "no units
          // named", and omitting the field would say the same thing less plainly.
          serialNumbers: line.serialNumbers,
        }));

      const header = await createSalesInvoice({
        date,
        customerLedgerId: customerId,
        warehouseId,
        lines: payload,
        kind,
        returnsInvoiceId: isReturn(kind) && returnsInvoiceId ? returnsInvoiceId : null,
        referenceNumber: reference || null,
      });

      onSaved(t('sales.savedNotice', { number: header.number }));
    } catch (failure) {
      const api = failure as ApiError;
      onError(api.detail || api.code);
    } finally {
      setBusy(false);
    }
  };

  const ready =
    customerId !== '' &&
    warehouseId !== '' &&
    lines.some((line) => line.productId && Number(line.quantity) > 0);

  return (
    <Modal
      title={isReturn(kind) ? t('sales.newReturn') : t('sales.newInvoice')}
      onClose={onClose}
    >
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <Field label={t('sales.kind')}>
          <Select
            value={kind}
            onChange={(value) => setKind(Number(value))}
            options={[
              { value: SalesDocumentKind.invoice, label: t('sales.invoice') },
              { value: SalesDocumentKind.return, label: t('sales.return') },
            ]}
          />
        </Field>

        <Field label={t('sales.date')}>
          <DateInput value={date} onChange={setDate} />
        </Field>

        <Field label={t('sales.customer')}>
          <Select
            value={customerId}
            onChange={(value) => setCustomerId(String(value))}
            options={[
              { value: '', label: t('sales.chooseCustomer') },
              ...(customers.data ?? []).map((customer) => ({
                value: customer.customerId,
                label: `${customer.code} — ${customer.name}`,
              })),
            ]}
          />
        </Field>

        <Field label={t('sales.warehouse')}>
          <Select
            value={warehouseId}
            onChange={(value) => setWarehouseId(String(value))}
            options={[
              { value: '', label: t('sales.chooseWarehouse') },
              ...(warehouses.data ?? []).map((warehouse) => ({
                value: warehouse.id,
                label: warehouse.name,
              })),
            ]}
          />
        </Field>

        <Field label={t('sales.reference')}>
          <input
            value={reference}
            onChange={(event) => setReference(event.target.value)}
            className="field-input-sm"
          />
        </Field>

        {isReturn(kind) && (
          <Field label={t('sales.againstInvoice')}>
            <Select
              value={returnsInvoiceId}
              onChange={(value) => setReturnsInvoiceId(String(value))}
              options={[
                { value: '', label: t('sales.noInvoice') },
                ...(returnable.data?.items ?? []).map((invoice) => ({
                  value: invoice.salesInvoiceId,
                  label: `${invoice.number} — ${invoice.total.toFixed(2)}`,
                })),
              ]}
            />
          </Field>
        )}
      </div>

      {isReturn(kind) && (
        // Said plainly rather than left to be discovered: the cost the goods come back
        // at, and whether the credit finds a bill, both hang on this.
        <p className="alert-warn text-xs">{t('sales.returnHint')}</p>
      )}

      <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
        <table className="w-full min-w-[46rem] text-sm">
          <thead className="text-start text-xs text-ink-muted">
            <tr>
              <th className="px-2 py-1 text-start">{t('sales.product')}</th>
              <th className="px-2 py-1 text-end">{t('sales.quantity')}</th>
              <th className="px-2 py-1 text-end">{t('sales.rate')}</th>
              <th className="px-2 py-1 text-end">{t('sales.discount')}</th>
              <th className="px-2 py-1 text-end">{t('sales.taxPercent')}</th>
              <th className="px-2 py-1 text-end">{t('sales.net')}</th>
              <th />
            </tr>
          </thead>

          <tbody>
            {lines.map((line, index) => (
              <LineRow
                key={index}
                line={line}
                products={products.data ?? []}
                warehouseId={warehouseId}
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
          {t('sales.addLine')}
        </button>

        <span className="ms-auto text-sm">
          {t('sales.taxable')}:{' '}
          <strong className="font-mono">{totals.taxable.toFixed(2)}</strong>
        </span>
        <span className="text-sm">
          {t('sales.tax')}: <strong className="font-mono">{totals.tax.toFixed(2)}</strong>
        </span>
        <span className="text-base">
          {t('sales.total')}:{' '}
          <strong className="font-mono">{totals.total.toFixed(2)}</strong>
        </span>
      </div>

      {/* What the screen adds up is what the lines come to; the server rounds the total
          to the currency and may differ in the last place, so this is called an
          estimate rather than presented as the figure that will be billed. */}
      <p className="text-xs text-ink-muted">{t('sales.totalsHint')}</p>

      <div className="flex justify-end gap-2">
        <ModalButton onClick={onClose}>{t('sales.close')}</ModalButton>
        <ModalButton primary disabled={!ready || busy} onClick={() => void save()}>
          {t('sales.saveDraft')}
        </ModalButton>
      </div>
    </Modal>
  );
}

/**
 * One line, and whatever the product it names needs beyond a quantity and a rate.
 *
 * A row of its own rather than markup in a loop, because a batched product has to ask the
 * warehouse which batches it holds and a serialised one has to ask which units are on the
 * shelf — and a hook cannot be called inside a loop. Without these a firm could not sell a
 * batched or serialised product from this screen at all: the server refuses the line, and
 * rightly, because a batch nobody named is stock nobody can trace.
 */
function LineRow({
  line,
  products,
  warehouseId,
  removable,
  onChange,
  onRemove,
}: {
  readonly line: DraftLine;
  readonly products: readonly ProductSummary[];
  readonly warehouseId: string;
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

  const batches = useQuery<readonly BatchStockRow[], ApiError>({
    queryKey: ['sales-line-batches', line.productId, warehouseId],
    queryFn: () => fetchProductBatches(line.productId, warehouseId),
    enabled: needsBatch && line.productId !== '' && warehouseId !== '',
  });

  // Only what is on the shelf in this warehouse. A unit already sold is not one that can
  // be sold again, and offering it would produce a refusal nobody could act on.
  const serials = useQuery<readonly SerialNumberView[], ApiError>({
    queryKey: ['sales-line-serials', line.productId, warehouseId],
    queryFn: () => fetchProductSerials(line.productId, warehouseId),
    enabled: needsSerials && line.productId !== '' && warehouseId !== '',
  });

  const toggleSerial = (number: string): void => {
    const chosen = new Set(line.serialNumbers);

    if (chosen.has(number)) {
      chosen.delete(number);
    } else {
      chosen.add(number);
    }

    onChange({ serialNumbers: [...chosen] });
  };

  return (
    <>
      <tr className="border-t border-line">
        <td className="px-2 py-1">
          <Select
            value={line.productId}
            onChange={(value) =>
              // The batch and the units belong to the product that was chosen before,
              // so they are dropped rather than carried onto a different one.
              onChange({ productId: String(value), batchNumber: '', serialNumbers: [] })
            }
            options={[
              { value: '', label: t('sales.chooseProduct') },
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
              {t('sales.removeLine')}
            </button>
          )}
        </td>
      </tr>

      {(needsBatch || needsSerials) && (
        <tr className="border-t border-dashed border-line">
          <td colSpan={7} className="px-2 pb-2">
            <div className="flex flex-wrap items-start gap-4">
              {needsBatch && (
                <Field label={t('sales.batch')}>
                  <Select
                    value={line.batchNumber}
                    onChange={(value) => onChange({ batchNumber: String(value) })}
                    options={[
                      { value: '', label: t('sales.chooseBatch') },
                      ...(batches.data ?? []).map((batch) => ({
                        value: batch.batchNumber,
                        label: `${batch.batchNumber} (${batch.quantity})`,
                      })),
                    ]}
                  />
                </Field>
              )}

              {needsSerials && (
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-ink-muted">
                    {t('sales.serialsChosen', {
                      chosen: line.serialNumbers.length,
                      wanted: Number.isFinite(wanted) ? wanted : 0,
                    })}
                  </span>

                  <div className="flex max-h-24 flex-wrap gap-2 overflow-auto">
                    {(serials.data ?? []).map((unit) => (
                      <label
                        key={unit.serialNumberId}
                        className="flex cursor-pointer items-center gap-1.5 rounded-md border border-line bg-surface px-2 py-0.5 text-xs transition hover:border-line-strong hover:bg-surface-3"
                      >
                        <input
                          type="checkbox"
                          checked={line.serialNumbers.includes(unit.number)}
                          onChange={() => toggleSerial(unit.number)}
                        />
                        {unit.number}
                      </label>
                    ))}

                    {(serials.data ?? []).length === 0 && (
                      <span className="text-xs text-ink-muted">
                        {t('sales.noSerials')}
                      </span>
                    )}
                  </div>
                </div>
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

  const query = useQuery<SalesInvoiceDetail, ApiError>({
    queryKey: ['sales-invoice', id],
    queryFn: () => getSalesInvoice(id),
  });

  const document = query.data;

  return (
    <Modal title={document?.header.number ?? t('sales.document')} onClose={onClose}>
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
            <Detail label={t('sales.date')} value={document.date} />
            <Detail
              label={t('sales.status')}
              value={statusLabel(document.header.status, t)}
            />
            <Detail label={t('sales.currency')} value={document.currency} />
            <Detail
              label={t('sales.taxable')}
              value={document.header.taxable.toFixed(2)}
            />
            <Detail label={t('sales.tax')} value={document.header.tax.toFixed(2)} />
            <Detail label={t('sales.total')} value={document.header.total.toFixed(2)} />
          </div>

          <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
            <table className="w-full min-w-[32rem] text-sm">
              <thead className="text-xs text-ink-muted">
                <tr>
                  <th className="px-2 py-1 text-start">#</th>
                  <th className="px-2 py-1 text-end">{t('sales.quantity')}</th>
                  <th className="px-2 py-1 text-end">{t('sales.rate')}</th>
                  <th className="px-2 py-1 text-end">{t('sales.taxable')}</th>
                  <th className="px-2 py-1 text-end">{t('sales.tax')}</th>
                </tr>
              </thead>
              <tbody>
                {document.lines.map((line) => (
                  <tr key={line.lineNumber} className="border-t border-line">
                    <td className="px-2 py-1">{line.lineNumber}</td>
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

          {/* What posting produced. A sale leaves two documents on purpose, so the issue
              is named rather than left for somebody to find in the stock ledger. */}
          {document.stockDocumentId && (
            <p className="text-xs text-ink-muted">{t('sales.postedProduced')}</p>
          )}

          {document.header.status === SalesInvoiceStatus.draft && (
            <div className="flex justify-end">
              <ModalButton primary disabled={busy} onClick={() => onPost(id)}>
                {t('sales.post')}
              </ModalButton>
            </div>
          )}

          {document.header.status === SalesInvoiceStatus.posted && (
            <div className="flex flex-wrap items-end justify-end gap-2">
              <Field label={t('sales.cancelReason')}>
                <input
                  value={reason}
                  onChange={(event) => setReason(event.target.value)}
                  className="field-input-sm"
                />
              </Field>
              <ModalButton
                disabled={busy || reason.trim() === ''}
                onClick={() => onCancel(id, reason.trim())}
              >
                {t('sales.cancel')}
              </ModalButton>
            </div>
          )}
        </>
      )}

      <div className="flex justify-end">
        <ModalButton onClick={onClose}>{t('sales.close')}</ModalButton>
      </div>
    </Modal>
  );
}

function statusLabel(status: number, t: (key: string) => string): string {
  if (status === SalesInvoiceStatus.posted) return t('sales.posted');
  if (status === SalesInvoiceStatus.cancelled) return t('sales.cancelled');

  return t('sales.draft');
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
