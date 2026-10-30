import { useMemo, useState } from 'react';
import { RemoveLineButton } from '@/components/invoice/InvoiceLayout';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { BatchPick } from '@/components/BatchPick';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { DateField, SelectField, TextAreaField, TextField } from '@/components/Form';
import { Modal } from '@/components/Modal';
import { Panel } from '@/components/Panel';
import { ReportFrame } from '@/components/ReportFrame';
import { SearchSelect } from '@/components/SearchSelect';
import { QuickAdd } from '@/components/QuickAdd';
import { StatusBadge, type StatusTone } from '@/components/StatusBadge';
import { productOption } from '@/components/DocumentLines';
import type { ApiError } from '@/lib/api';
import { listCustomers, type CustomerSummary } from '@/lib/customers';
import { listMaster, startingGodown, type WarehouseSummary } from '@/lib/inventory';
import { useGodownsOn } from '@/stores/settings';
import { listProducts, type ProductSummary } from '@/lib/products';
import { addScanned, BarcodeScan } from '@/components/BarcodeScan';
import {
  listPurchaseInvoices,
  PurchaseDocumentKind,
  PurchaseInvoiceStatus,
  type PurchaseInvoiceSummary,
} from '@/lib/purchase';
import {
  addRmaPhoto,
  createRma,
  getRma,
  getRmaPhoto,
  inspectRma,
  listRmas,
  refundRma,
  removeRmaPhoto,
  replaceRma,
  RmaCondition,
  RmaDirection,
  RmaStatus,
  shrinkEvidence,
  stepRma,
  updateRma,
  type RmaDetail,
  type RmaInput,
  type RmaStep,
  type RmaSummary,
} from '@/lib/rmas';
import {
  listSalesInvoices,
  SalesDocumentKind,
  SalesInvoiceStatus,
  type PagedResult,
  type SalesInvoiceSummary,
} from '@/lib/sales';
import { listSuppliers, type SupplierSummary } from '@/lib/suppliers';
import { useSession } from '@/stores/session';
import { Alert, Labelled, trim } from '@/pages/StockOperationsPage';
import { shortcutProps } from '@/lib/shortcuts';

const STATUS_KEYS: Record<number, string> = {
  [RmaStatus.requested]: 'requested',
  [RmaStatus.approved]: 'approved',
  [RmaStatus.received]: 'received',
  [RmaStatus.underInspection]: 'underInspection',
  [RmaStatus.repaired]: 'repaired',
  [RmaStatus.replaced]: 'replaced',
  [RmaStatus.refunded]: 'refunded',
  [RmaStatus.closed]: 'closed',
  [RmaStatus.rejected]: 'rejected',
  [RmaStatus.cancelled]: 'cancelled',
};

const STATUS_TONES: Record<number, StatusTone> = {
  [RmaStatus.requested]: 'warn',
  [RmaStatus.approved]: 'info',
  [RmaStatus.received]: 'info',
  [RmaStatus.underInspection]: 'brand',
  [RmaStatus.repaired]: 'success',
  [RmaStatus.replaced]: 'success',
  [RmaStatus.refunded]: 'success',
  [RmaStatus.closed]: 'neutral',
  [RmaStatus.rejected]: 'danger',
  [RmaStatus.cancelled]: 'danger',
};

const CONDITION_KEYS: Record<number, string> = {
  [RmaCondition.good]: 'good',
  [RmaCondition.repairable]: 'repairable',
  [RmaCondition.defective]: 'defective',
  [RmaCondition.damaged]: 'damaged',
};

/** What an action on a return came to, and whether it ends the dialog. */
interface Done {
  readonly message: string | null;
  readonly close: boolean;
}

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

function blank(value: string): string | null {
  return value.trim() || null;
}

function statusLabel(t: (key: string) => string, status: number): string {
  return t(`rma.status.${STATUS_KEYS[status] ?? 'requested'}`);
}

// ------------------------------------------------------------------------------- the list

/**
 * Return merchandise authorisations: goods coming back from customers and going back to
 * suppliers, each followed from the request to what came of it.
 */
export function RmasPage(): React.JSX.Element {
  const { t } = useTranslation();
  const can = useSession((state) => state.can);
  const [params, setParams] = useSearchParams();

  const [direction, setDirection] = useState<number | ''>('');
  const [status, setStatus] = useState<number | ''>('');
  const [search, setSearch] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [open, setOpen] = useState<string | 'new' | null>(params.get('open'));
  const [notice, setNotice] = useState<string | null>(null);

  const query = useQuery<readonly RmaSummary[], ApiError>({
    queryKey: ['rmas', direction, status, search, from, to],
    queryFn: () => listRmas(direction, status, search, from, to),
  });

  const close = (message?: string): void => {
    setOpen(null);
    if (params.has('open')) {
      params.delete('open');
      setParams(params, { replace: true });
    }
    if (message) {
      setNotice(message);
      window.setTimeout(() => setNotice(null), 4000);
    }
  };

  const columns: readonly GridColumn<RmaSummary>[] = [
    {
      key: 'number',
      header: t('rma.number'),
      value: (row) => row.number,
      render: (row) => (
        <button
          type="button"
          onClick={() => setOpen(row.id)}
          className="cell-link font-mono"
        >
          {row.number}
        </button>
      ),
    },
    { key: 'date', header: t('rma.date'), value: (row) => row.date },
    {
      key: 'direction',
      header: t('rma.direction'),
      value: (row) =>
        row.direction === RmaDirection.fromCustomer
          ? t('rma.fromCustomer')
          : t('rma.toSupplier'),
    },
    { key: 'party', header: t('rma.party'), value: (row) => row.partyName },
    {
      key: 'invoice',
      header: t('rma.invoice'),
      value: (row) => row.invoiceReference ?? '',
    },
    { key: 'reason', header: t('rma.reason'), value: (row) => row.reason },
    {
      key: 'quantity',
      header: t('rma.quantity'),
      value: (row) => row.quantity,
      numeric: true,
      render: (row) => trim(row.quantity),
    },
    {
      key: 'status',
      header: t('rma.statusLabel'),
      value: (row) => statusLabel(t, row.status),
      render: (row) => (
        <StatusBadge
          tone={STATUS_TONES[row.status] ?? 'neutral'}
          label={statusLabel(t, row.status)}
          struck={row.status === RmaStatus.cancelled}
        />
      ),
    },
    {
      key: 'condition',
      header: t('rma.condition'),
      value: (row) =>
        row.condition
          ? t(`rma.conditions.${CONDITION_KEYS[row.condition] ?? 'good'}`)
          : '',
    },
    { key: 'outcome', header: t('rma.outcome'), value: (row) => row.outcomeNumber ?? '' },
  ];

  const controls = (
    <div className="toolbar">
      <Labelled label={t('rma.search')}>
        <input
          type="search"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder={t('rma.searchHint')}
          className="field-input-sm min-w-56"
        />
      </Labelled>
      <Labelled label={t('rma.direction')}>
        <SearchSelect
          value={direction === '' ? '' : String(direction)}
          onChange={(next) => setDirection(next === '' ? '' : Number(next))}
          clearable
          size="sm"
          label={t('rma.direction')}
          placeholder={t('rma.bothDirections')}
          options={[
            { value: String(RmaDirection.fromCustomer), label: t('rma.fromCustomer') },
            { value: String(RmaDirection.toSupplier), label: t('rma.toSupplier') },
          ]}
        />
      </Labelled>
      <Labelled label={t('rma.statusLabel')}>
        <SearchSelect
          value={status === '' ? '' : String(status)}
          onChange={(next) => setStatus(next === '' ? '' : Number(next))}
          clearable
          size="sm"
          label={t('rma.statusLabel')}
          placeholder={t('rma.allStatuses')}
          options={Object.entries(STATUS_KEYS).map(([value, key]) => ({
            value,
            label: t(`rma.status.${key}`),
          }))}
        />
      </Labelled>
      <Labelled label={t('reports.from')}>
        <input
          type="date"
          value={from}
          onChange={(event) => setFrom(event.target.value)}
          className="field-input-sm"
        />
      </Labelled>
      <Labelled label={t('reports.to')}>
        <input
          type="date"
          value={to}
          onChange={(event) => setTo(event.target.value)}
          className="field-input-sm"
        />
      </Labelled>
    </div>
  );

  return (
    <>
      <ReportFrame title={t('nav.rmas')} controls={controls} query={query}>
        {(rows) => (
          <div className="space-y-3">
            {notice && <Alert tone="ok">{notice}</Alert>}
            <DataGrid
              gridKey="rmas"
              rows={rows}
              columns={columns}
              rowKey={(row) => row.id}
              emptyMessage={t('rma.none')}
              actions={
                can('service:rma:create') ? (
                  <GridAction label={t('rma.new')} onClick={() => setOpen('new')} />
                ) : undefined
              }
            />
          </div>
        )}
      </ReportFrame>

      {open !== null && <RmaEditor id={open === 'new' ? null : open} onClose={close} />}
    </>
  );
}

// ------------------------------------------------------------------------------ the return

function RmaEditor({
  id,
  onClose,
}: {
  readonly id: string | null;
  readonly onClose: (message?: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const rma = useQuery<RmaDetail, ApiError>({
    queryKey: ['rma', id],
    queryFn: () => getRma(id ?? ''),
    enabled: id !== null,
  });

  const title = id === null ? t('rma.new') : (rma.data?.summary.number ?? t('rma.card'));

  return (
    <Modal title={title} size="fullscreen" onClose={() => onClose()}>
      {id !== null && rma.isPending ? (
        <p className="text-sm text-ink-muted">{t('common.loading')}</p>
      ) : rma.error ? (
        <Alert tone="error">{rma.error.detail || rma.error.code}</Alert>
      ) : rma.data && rma.data.summary.status !== RmaStatus.requested ? (
        <RmaView rma={rma.data} onClose={onClose} />
      ) : (
        <RmaForm
          key={rma.data?.summary.id ?? 'new'}
          rma={rma.data ?? null}
          onClose={onClose}
        />
      )}
    </Modal>
  );
}

interface LineDraft {
  readonly key: string;
  productId: string;
  quantity: string;
  serialNumber: string;
  reason: string;
  batchId: string;
}

function blankLine(): LineDraft {
  return {
    key: crypto.randomUUID(),
    productId: '',
    quantity: '1',
    serialNumber: '',
    reason: '',
    batchId: '',
  };
}

/** Enters a return, or changes one still awaiting approval and takes it to approval. */
function RmaForm({
  rma,
  onClose,
}: {
  readonly rma: RmaDetail | null;
  readonly onClose: (message?: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const can = useSession((state) => state.can);
  const godownsOn = useGodownsOn();

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });
  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', 'picker'],
    queryFn: () => listProducts('', '', false),
  });
  const customers = useQuery<readonly CustomerSummary[], ApiError>({
    queryKey: ['customers', 'picker'],
    queryFn: () => listCustomers('', true),
  });
  const suppliers = useQuery<readonly SupplierSummary[], ApiError>({
    queryKey: ['suppliers', 'picker'],
    queryFn: () => listSuppliers('', true),
  });

  const godowns = useMemo(
    () => (warehouses.data ?? []).filter((warehouse) => !warehouse.isTransit),
    [warehouses.data],
  );

  const [date, setDate] = useState(rma?.summary.date ?? todayIso());
  const [direction, setDirection] = useState<number>(
    rma?.summary.direction ?? RmaDirection.fromCustomer,
  );
  const [partyId, setPartyId] = useState(rma?.summary.partyLedgerId ?? '');
  const [warehouseId, setWarehouseId] = useState(rma?.warehouseId ?? '');
  const [reason, setReason] = useState(rma?.summary.reason ?? '');
  const [narration, setNarration] = useState(rma?.narration ?? '');
  const [invoiceReference, setInvoiceReference] = useState(
    rma && rma.linkedInvoiceNumber !== rma.summary.invoiceReference
      ? (rma.summary.invoiceReference ?? '')
      : '',
  );
  const [invoiceId, setInvoiceId] = useState(
    rma?.salesInvoiceId ?? rma?.purchaseInvoiceId ?? '',
  );
  const [lines, setLines] = useState<readonly LineDraft[]>(() =>
    rma && rma.lines.length > 0
      ? rma.lines.map((line) => ({
          key: crypto.randomUUID(),
          productId: line.productId,
          quantity: String(line.quantity),
          serialNumber: line.serialNumber ?? '',
          reason: line.reason ?? '',
          batchId: line.batchId ?? '',
        }))
      : [blankLine()],
  );
  const [note, setNote] = useState('');
  const [error, setError] = useState<string | null>(null);

  const godown = warehouseId || startingGodown(godowns, '', godownsOn)?.id || '';
  const fromCustomer = direction === RmaDirection.fromCustomer;

  // The party's posted invoices, for the return to name the one the goods came on.
  const invoices = useQuery<
    readonly { value: string; label: string; detail: string }[],
    ApiError
  >({
    queryKey: ['rma-invoices', direction, partyId],
    queryFn: async () => {
      if (fromCustomer) {
        const page: PagedResult<SalesInvoiceSummary> = await listSalesInvoices(
          {
            customerLedgerId: partyId,
            kind: SalesDocumentKind.invoice,
            status: SalesInvoiceStatus.posted,
          },
          1,
          100,
        );
        return page.items.map((invoice) => ({
          value: invoice.salesInvoiceId,
          label: invoice.number,
          detail: `${invoice.date} · ${invoice.total.toFixed(2)}`,
        }));
      }
      const page: PagedResult<PurchaseInvoiceSummary> = await listPurchaseInvoices(
        {
          supplierLedgerId: partyId,
          kind: PurchaseDocumentKind.invoice,
          status: PurchaseInvoiceStatus.posted,
        },
        1,
        100,
      );
      return page.items.map((invoice) => ({
        value: invoice.purchaseInvoiceId,
        label: invoice.number,
        detail: `${invoice.date}${invoice.supplierInvoiceNumber ? ` · ${invoice.supplierInvoiceNumber}` : ''}`,
      }));
    },
    enabled: partyId !== '',
  });

  const parties = fromCustomer
    ? (customers.data ?? []).map((customer) => ({
        value: customer.customerId,
        label: customer.name,
        detail: [customer.code, customer.contact.mobileNumber]
          .filter(Boolean)
          .join(' · '),
      }))
    : (suppliers.data ?? []).map((supplier) => ({
        value: supplier.supplierId,
        label: supplier.name,
        detail: supplier.code,
      }));

  const stocked = useMemo(
    () => (products.data ?? []).filter((product) => product.itemType === 1),
    [products.data],
  );
  const productOptions = useMemo(
    () => stocked.map((product) => productOption(product, undefined)),
    [stocked],
  );
  const productById = useMemo(
    () => new Map(stocked.map((product) => [product.id, product])),
    [stocked],
  );

  const updateLine = (key: string, patch: Partial<LineDraft>): void =>
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );

  const input = (): RmaInput => ({
    date,
    direction,
    partyLedgerId: partyId,
    warehouseId: godown,
    reason: reason.trim(),
    narration: blank(narration),
    invoiceReference: blank(invoiceReference),
    salesInvoiceId: fromCustomer && invoiceId ? invoiceId : null,
    purchaseInvoiceId: !fromCustomer && invoiceId ? invoiceId : null,
    lines: lines
      .filter((line) => line.productId && Number(line.quantity) > 0)
      .map((line) => ({
        productId: line.productId,
        quantity: Number(line.quantity),
        reason: blank(line.reason),
        serialNumber: blank(line.serialNumber),
        batchId: line.batchId || null,
      })),
  });

  const refresh = async (): Promise<void> => {
    await queryClient.invalidateQueries({ queryKey: ['rmas'] });
    await queryClient.invalidateQueries({ queryKey: ['rma'] });
  };

  const save = useMutation<string, ApiError, RmaStep | null>({
    mutationFn: async (then) => {
      let id = rma?.summary.id ?? '';
      if (rma) {
        await updateRma(id, input());
      } else {
        id = await createRma(input());
      }
      if (then) {
        await stepRma(id, then, blank(note));
      }
      return then ?? 'saved';
    },
    onSuccess: async (outcome) => {
      await refresh();
      onClose(t(outcome === 'saved' ? 'rma.savedNotice' : `rma.notices.${outcome}`));
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const submit = (then: RmaStep | null): void => {
    const entered = input();
    if (!partyId || !godown || !entered.reason || entered.lines.length === 0) {
      setError(t('rma.incomplete'));
      return;
    }
    if ((then === 'reject' || then === 'cancel') && !note.trim()) {
      setError(t('rma.reasonNeeded'));
      return;
    }
    setError(null);
    save.mutate(then);
  };

  const editable = can(rma ? 'service:rma:edit' : 'service:rma:create');

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        submit(null);
      }}
    >
      {rma && (
        <StatusBadge
          tone={STATUS_TONES[rma.summary.status] ?? 'neutral'}
          label={statusLabel(t, rma.summary.status)}
        />
      )}

      <fieldset disabled={!editable} className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <SelectField<number>
            label={t('rma.direction')}
            value={direction}
            disabled={rma !== null}
            onChange={(value) => {
              setDirection(value);
              setPartyId('');
              setInvoiceId('');
            }}
            options={[
              { value: RmaDirection.fromCustomer, label: t('rma.fromCustomer') },
              { value: RmaDirection.toSupplier, label: t('rma.toSupplier') },
            ]}
          />
          <DateField
            label={t('rma.date')}
            value={date}
            onChange={setDate}
            disabled={rma !== null}
          />
          <label className="block">
            <span className="field-label">
              {fromCustomer ? t('rma.customer') : t('rma.supplier')}
            </span>
            <QuickAdd
              kind={fromCustomer ? 'customer' : 'supplier'}
              disabled={rma !== null}
              onCreated={(id) => {
                setPartyId(id);
                setInvoiceId('');
              }}
            >
              <SearchSelect
                value={partyId}
                disabled={rma !== null}
                onChange={(value) => {
                  setPartyId(value);
                  setInvoiceId('');
                }}
                label={fromCustomer ? t('rma.customer') : t('rma.supplier')}
                placeholder={t('stock.choose')}
                options={parties}
              />
            </QuickAdd>
          </label>
          {godownsOn && (
            <SelectField<string>
              label={t('rma.godown')}
              value={godown}
              disabled={rma !== null}
              onChange={setWarehouseId}
              hint={t('rma.godownHint')}
              options={godowns.map((warehouse) => ({
                value: warehouse.id,
                label: warehouse.name,
              }))}
            />
          )}
        </div>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <label className="block">
            <span className="field-label">
              {fromCustomer ? t('rma.sale') : t('rma.purchase')}
            </span>
            <SearchSelect
              value={invoiceId}
              onChange={setInvoiceId}
              clearable
              label={fromCustomer ? t('rma.sale') : t('rma.purchase')}
              placeholder={partyId ? t('rma.pickInvoice') : t('rma.pickPartyFirst')}
              options={invoices.data ?? []}
            />
          </label>
          <TextField
            label={t('rma.invoiceReference')}
            value={invoiceReference}
            onChange={setInvoiceReference}
          />
          <TextField
            label={t('rma.reason')}
            required
            value={reason}
            onChange={setReason}
            className="lg:col-span-2"
          />
        </div>
        <TextAreaField
          label={t('rma.narration')}
          rows={2}
          value={narration}
          onChange={setNarration}
        />

        <BarcodeScan
          products={products.data ?? []}
          onScan={(product) => {
            const next = addScanned(lines, product, () => ({
              ...blankLine(),
              productId: product.id,
            }));
            setLines(next.lines);
            return next.quantity;
          }}
        />

        <div className="overflow-x-auto sm:rounded-xl sm:border sm:border-line sm:bg-surface sm:shadow-card">
          <table className="line-table">
            <thead className="bg-surface-2 text-[13px] text-ink-muted">
              <tr>
                <th className="px-3 py-2 text-start font-semibold">{t('rma.product')}</th>
                <th className="px-3 py-2 text-start font-semibold">{t('stock.batch')}</th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('stock.quantity')}
                </th>
                <th className="px-3 py-2 text-start font-semibold">{t('rma.serial')}</th>
                <th className="px-3 py-2 text-start font-semibold">
                  {t('rma.lineReason')}
                </th>
                <th />
              </tr>
            </thead>
            <tbody>
              {lines.map((line) => {
                const product = productById.get(line.productId);

                return (
                  <tr key={line.key} className="border-t border-line">
                    <td data-label={t('rma.product')} className="min-w-64 px-2 py-1">
                      <SearchSelect
                        value={line.productId}
                        onChange={(productId) =>
                          updateLine(line.key, { productId, batchId: '' })
                        }
                        size="sm"
                        label={t('rma.product')}
                        placeholder={t('stock.choose')}
                        options={productOptions}
                      />
                    </td>
                    <td data-label={t('stock.batch')} className="min-w-40 px-2 py-1">
                      {product?.tracksBatches ? (
                        <BatchPick
                          productId={line.productId}
                          warehouseId={godown}
                          value={line.batchId}
                          onChange={(batchId) => updateLine(line.key, { batchId })}
                        />
                      ) : null}
                    </td>
                    <td data-label={t('stock.quantity')} className="px-2 py-1">
                      <input
                        type="number"
                        step="any"
                        min="0"
                        value={line.quantity}
                        onChange={(event) =>
                          updateLine(line.key, { quantity: event.target.value })
                        }
                        className="field-input-sm text-end font-mono tabular-nums"
                      />
                    </td>
                    <td data-label={t('rma.serial')} className="px-2 py-1">
                      <input
                        value={line.serialNumber}
                        onChange={(event) =>
                          updateLine(line.key, { serialNumber: event.target.value })
                        }
                        className="field-input-sm font-mono"
                      />
                    </td>
                    <td data-label={t('rma.lineReason')} className="px-2 py-1">
                      <input
                        value={line.reason}
                        onChange={(event) =>
                          updateLine(line.key, { reason: event.target.value })
                        }
                        className="field-input-sm"
                      />
                    </td>
                    <td className="px-2 py-1 text-end">
                      <RemoveLineButton
                        disabled={lines.length === 1}
                        onClick={() =>
                          setLines((current) =>
                            current.filter((candidate) => candidate.key !== line.key),
                          )
                        }
                        label={t('stock.removeLine')}
                      />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <button
          {...shortcutProps('add-line')}
          type="button"
          onClick={() => setLines((current) => [...current, blankLine()])}
          className="btn-secondary"
        >
          {t('stock.addLine')}
        </button>
      </fieldset>

      {rma && (
        <TextField
          label={t('rma.stepNote')}
          value={note}
          onChange={setNote}
          hint={t('rma.stepNoteHint')}
        />
      )}

      {error && <Alert tone="error">{error}</Alert>}

      <div className="form-actions">
        {rma && can('service:rma:approve') && (
          <>
            <button
              type="button"
              disabled={save.isPending}
              onClick={() => submit('approve')}
              className="btn-secondary"
            >
              {t('rma.approve')}
            </button>
            <button
              type="button"
              disabled={save.isPending}
              onClick={() => submit('reject')}
              className="btn-secondary"
            >
              {t('rma.reject')}
            </button>
          </>
        )}
        {rma && can('service:rma:delete') && (
          <button
            type="button"
            disabled={save.isPending}
            onClick={() => submit('cancel')}
            className="btn-secondary"
          >
            {t('rma.cancel')}
          </button>
        )}
        <button type="button" onClick={() => onClose()} className="btn-secondary ms-auto">
          {t('common.close')}
        </button>
        {editable && (
          <button type="submit" disabled={save.isPending} className="btn-primary">
            {rma ? t('rma.save') : t('rma.request')}
          </button>
        )}
      </div>
    </form>
  );
}

/** A return past approval: its goods, inspection, photographs, outcome and history. */
function RmaView({
  rma,
  onClose,
}: {
  readonly rma: RmaDetail;
  readonly onClose: (message?: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const can = useSession((state) => state.can);
  const godownsOn = useGodownsOn();

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });
  const godowns = (warehouses.data ?? []).filter((warehouse) => !warehouse.isTransit);

  const status = rma.summary.status;
  const fromCustomer = rma.summary.direction === RmaDirection.fromCustomer;
  const awaitsOutcome =
    status === RmaStatus.received || status === RmaStatus.underInspection;
  const finished =
    status === RmaStatus.closed ||
    status === RmaStatus.rejected ||
    status === RmaStatus.cancelled;

  const [condition, setCondition] = useState<number>(
    rma.summary.condition ?? RmaCondition.defective,
  );
  const [inspection, setInspection] = useState(rma.inspectionNotes ?? '');
  const [note, setNote] = useState('');
  const [shelf, setShelf] = useState(
    godowns.find((warehouse) => warehouse.isDefault)?.id ?? '',
  );
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const refresh = async (): Promise<void> => {
    await queryClient.invalidateQueries({ queryKey: ['rmas'] });
    await queryClient.invalidateQueries({ queryKey: ['rma', rma.summary.id] });
  };

  // Steps that lead on to more work keep the return open; closing or withdrawing it ends here.
  const act = useMutation<Done, ApiError, () => Promise<Done>>({
    mutationFn: (action) => action(),
    onSuccess: async ({ message, close }) => {
      setError(null);
      setNote('');
      await refresh();
      if (close && message) {
        onClose(message);
      } else {
        setNotice(message);
      }
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const step = (which: RmaStep): void => {
    if (which === 'cancel' && !note.trim()) {
      setError(t('rma.reasonNeeded'));
      return;
    }
    act.mutate(async () => {
      await stepRma(rma.summary.id, which, blank(note));
      return {
        message: t(`rma.notices.${which}`),
        close: which === 'close' || which === 'cancel',
      };
    });
  };

  const outcomeLink = (): { label: string; to: string } | null => {
    if (rma.creditNoteId) {
      return {
        label: rma.summary.outcomeNumber ?? '',
        to: `/sales/returns?open=${rma.creditNoteId}`,
      };
    }
    if (rma.debitNoteId) {
      return {
        label: rma.summary.outcomeNumber ?? '',
        to: `/purchase/returns?open=${rma.debitNoteId}`,
      };
    }
    if (rma.replacementDocumentId) {
      return { label: rma.summary.outcomeNumber ?? '', to: `/inventory/stock-transfers` };
    }
    return null;
  };
  const outcome = outcomeLink();

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3 text-sm">
        <StatusBadge
          tone={STATUS_TONES[status] ?? 'neutral'}
          label={statusLabel(t, status)}
          struck={status === RmaStatus.cancelled}
        />
        {outcome && (
          <button
            type="button"
            className="cell-link"
            onClick={() => navigate(outcome.to)}
          >
            {t('rma.settledBy', { number: outcome.label })}
          </button>
        )}
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <Panel title={t('rma.details')}>
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
            <dt className="text-ink-muted">{t('rma.direction')}</dt>
            <dd>{fromCustomer ? t('rma.fromCustomer') : t('rma.toSupplier')}</dd>
            <dt className="text-ink-muted">{t('rma.date')}</dt>
            <dd>{rma.summary.date}</dd>
            <dt className="text-ink-muted">
              {fromCustomer ? t('rma.customer') : t('rma.supplier')}
            </dt>
            <dd>{rma.summary.partyName}</dd>
            {godownsOn && (
              <>
                <dt className="text-ink-muted">{t('rma.godown')}</dt>
                <dd>{rma.warehouseName}</dd>
              </>
            )}
            <dt className="text-ink-muted">{t('rma.invoice')}</dt>
            <dd>
              {[rma.linkedInvoiceNumber, rma.summary.invoiceReference]
                .filter((value, index, all) => value && all.indexOf(value) === index)
                .join(' · ') || '—'}
            </dd>
            <dt className="text-ink-muted">{t('rma.reason')}</dt>
            <dd>{rma.summary.reason}</dd>
            {rma.narration && (
              <>
                <dt className="text-ink-muted">{t('rma.narration')}</dt>
                <dd>{rma.narration}</dd>
              </>
            )}
          </dl>
        </Panel>

        <Panel title={t('rma.inspection')}>
          {awaitsOutcome && can('service:rma:edit') ? (
            <div className="space-y-3">
              <SelectField<number>
                label={t('rma.condition')}
                value={condition}
                onChange={setCondition}
                options={Object.entries(CONDITION_KEYS).map(([value, key]) => ({
                  value: Number(value),
                  label: t(`rma.conditions.${key}`),
                }))}
              />
              <TextAreaField
                label={t('rma.inspectionNotes')}
                rows={3}
                value={inspection}
                onChange={setInspection}
              />
              <button
                type="button"
                disabled={act.isPending}
                onClick={() =>
                  act.mutate(async () => {
                    await inspectRma(rma.summary.id, condition, blank(inspection));
                    return { message: t('rma.notices.inspected'), close: false };
                  })
                }
                className="btn-secondary"
              >
                {t('rma.saveInspection')}
              </button>
            </div>
          ) : (
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
              <dt className="text-ink-muted">{t('rma.condition')}</dt>
              <dd>
                {rma.summary.condition
                  ? t(`rma.conditions.${CONDITION_KEYS[rma.summary.condition] ?? 'good'}`)
                  : '—'}
              </dd>
              <dt className="text-ink-muted">{t('rma.inspectedOn')}</dt>
              <dd>{rma.inspectedOn ?? '—'}</dd>
              <dt className="text-ink-muted">{t('rma.inspectionNotes')}</dt>
              <dd>{rma.inspectionNotes ?? '—'}</dd>
            </dl>
          )}
        </Panel>

        <Panel title={t('rma.photos')}>
          <Photos
            rma={rma}
            editable={!finished && can('service:rma:edit')}
            onError={setError}
          />
        </Panel>
      </div>

      <Panel title={t('rma.goods')}>
        <div className="overflow-x-auto">
          <table className="line-table">
            <thead className="bg-surface-2 text-[13px] text-ink-muted">
              <tr>
                <th className="px-3 py-2 text-start font-semibold">{t('rma.product')}</th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('stock.quantity')}
                </th>
                <th className="px-3 py-2 text-start font-semibold">{t('rma.serial')}</th>
                <th className="px-3 py-2 text-start font-semibold">{t('stock.batch')}</th>
                <th className="px-3 py-2 text-start font-semibold">
                  {t('rma.lineReason')}
                </th>
              </tr>
            </thead>
            <tbody>
              {rma.lines.map((line) => (
                <tr
                  key={`${line.productId}-${line.serialNumber ?? ''}`}
                  className="border-t border-line"
                >
                  <td data-label={t('rma.product')} className="px-3 py-2">
                    <span className="font-mono text-xs">{line.productCode}</span>{' '}
                    {line.productName}
                  </td>
                  <td data-label={t('stock.quantity')} className="cell-numeric">
                    {trim(line.quantity)} {line.stockUnitCode}
                  </td>
                  <td
                    data-label={t('rma.serial')}
                    className="px-3 py-2 font-mono text-xs"
                  >
                    {line.serialNumber ?? ''}
                  </td>
                  <td data-label={t('stock.batch')} className="px-3 py-2">
                    {line.batchNumber ?? ''}
                  </td>
                  <td data-label={t('rma.lineReason')} className="px-3 py-2">
                    {line.reason ?? ''}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>

      {awaitsOutcome && can('service:rma:approve') && (
        <Panel title={t('rma.outcomeTitle')}>
          <div className="flex flex-wrap items-end gap-3">
            <button
              type="button"
              disabled={act.isPending}
              onClick={() => step('repair')}
              className="btn-secondary"
            >
              {t('rma.repaired')}
            </button>
            <div className={godownsOn ? 'min-w-56' : 'hidden'}>
              <SelectField<string>
                label={t('rma.stockGodown')}
                value={
                  shelf || (godowns.find((warehouse) => warehouse.isDefault)?.id ?? '')
                }
                onChange={setShelf}
                size="sm"
                options={godowns.map((warehouse) => ({
                  value: warehouse.id,
                  label: warehouse.name,
                }))}
              />
            </div>
            <button
              type="button"
              disabled={act.isPending}
              onClick={() =>
                act.mutate(async () => {
                  const chosen =
                    shelf || (godowns.find((warehouse) => warehouse.isDefault)?.id ?? '');
                  const done = await replaceRma(rma.summary.id, chosen, blank(note));
                  return {
                    message: done.number
                      ? t('rma.notices.replacedWith', { number: done.number })
                      : t('rma.notices.replaced'),
                    close: false,
                  };
                })
              }
              className="btn-secondary"
            >
              {t('rma.replace')}
            </button>
            <button
              type="button"
              disabled={act.isPending}
              onClick={() =>
                act.mutate(async () => {
                  const done = await refundRma(rma.summary.id, blank(note));
                  await refresh();
                  if (done.documentId) {
                    navigate(
                      fromCustomer
                        ? `/sales/returns?open=${done.documentId}`
                        : `/purchase/returns?open=${done.documentId}`,
                    );
                  }
                  return { message: null, close: false };
                })
              }
              className="btn-primary"
            >
              {fromCustomer ? t('rma.creditNote') : t('rma.debitNote')}
            </button>
          </div>
        </Panel>
      )}

      {!finished && (
        <TextField
          label={t('rma.stepNote')}
          value={note}
          onChange={setNote}
          hint={t('rma.stepNoteHint')}
        />
      )}

      {notice && <Alert tone="ok">{notice}</Alert>}
      {error && <Alert tone="error">{error}</Alert>}

      <div className="form-actions">
        {status === RmaStatus.approved && can('service:rma:edit') && (
          <button
            type="button"
            disabled={act.isPending}
            onClick={() => step('receive')}
            className="btn-primary"
          >
            {fromCustomer ? t('rma.receive') : t('rma.handOver')}
          </button>
        )}
        {(status === RmaStatus.repaired ||
          status === RmaStatus.replaced ||
          status === RmaStatus.refunded) &&
          can('service:rma:edit') && (
            <button
              type="button"
              disabled={act.isPending}
              onClick={() => step('close')}
              className="btn-primary"
            >
              {t('rma.close')}
            </button>
          )}
        {(status === RmaStatus.approved || awaitsOutcome) &&
          can('service:rma:delete') && (
            <button
              type="button"
              disabled={act.isPending}
              onClick={() => step('cancel')}
              className="btn-secondary"
            >
              {t('rma.cancel')}
            </button>
          )}
        <button type="button" onClick={() => onClose()} className="btn-secondary ms-auto">
          {t('common.close')}
        </button>
      </div>

      <Panel title={t('rma.history')}>
        <ol className="space-y-2 text-sm">
          {rma.history.map((entry) => (
            <li key={`${entry.at}-${entry.status}`} className="flex flex-wrap gap-x-3">
              <span className="font-mono text-xs text-ink-muted">
                {new Date(entry.at).toLocaleString()}
              </span>
              <span className="font-medium">{statusLabel(t, entry.status)}</span>
              <span className="text-ink-muted">{entry.userName}</span>
              {entry.note && <span>— {entry.note}</span>}
            </li>
          ))}
        </ol>
      </Panel>
    </div>
  );
}

/** The photographs kept on a return: thumbnails, one opened large, and adding more. */
function Photos({
  rma,
  editable,
  onError,
}: {
  readonly rma: RmaDetail;
  readonly editable: boolean;
  readonly onError: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [caption, setCaption] = useState('');
  const [busy, setBusy] = useState(false);
  const [large, setLarge] = useState<string | null>(null);

  const add = async (file: File | undefined): Promise<void> => {
    if (!file) return;
    setBusy(true);
    try {
      const content = await shrinkEvidence(file);
      await addRmaPhoto(rma.summary.id, content, blank(caption));
      setCaption('');
      await queryClient.invalidateQueries({ queryKey: ['rma', rma.summary.id] });
    } catch (failure) {
      const problem = failure as Partial<ApiError>;
      onError(problem.detail || problem.code || t('rma.photoFailed'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-3">
      {rma.photos.length === 0 ? (
        <p className="text-sm text-ink-muted">{t('rma.noPhotos')}</p>
      ) : (
        <div className="grid grid-cols-3 gap-2">
          {rma.photos.map((photo) => (
            <Thumbnail
              key={photo.id}
              rmaId={rma.summary.id}
              photoId={photo.id}
              caption={photo.caption}
              onOpen={setLarge}
              onRemove={
                editable
                  ? async () => {
                      await removeRmaPhoto(rma.summary.id, photo.id);
                      await queryClient.invalidateQueries({
                        queryKey: ['rma', rma.summary.id],
                      });
                    }
                  : null
              }
            />
          ))}
        </div>
      )}
      {editable && rma.photos.length < 6 && (
        <div className="space-y-2">
          <TextField
            label={t('rma.photoCaption')}
            value={caption}
            onChange={setCaption}
          />
          <label className="btn-secondary inline-flex cursor-pointer">
            {busy ? t('common.saving') : t('rma.addPhoto')}
            <input
              type="file"
              accept="image/*"
              capture="environment"
              className="sr-only"
              disabled={busy}
              onChange={(event) => {
                void add(event.target.files?.[0]);
                event.target.value = '';
              }}
            />
          </label>
        </div>
      )}
      {large && (
        <Modal title={t('rma.photos')} size="wide" onClose={() => setLarge(null)}>
          <img src={large} alt="" className="mx-auto max-h-[70vh] rounded-lg" />
        </Modal>
      )}
    </div>
  );
}

function Thumbnail({
  rmaId,
  photoId,
  caption,
  onOpen,
  onRemove,
}: {
  readonly rmaId: string;
  readonly photoId: string;
  readonly caption: string | null;
  readonly onOpen: (content: string) => void;
  readonly onRemove: (() => Promise<void>) | null;
}): React.JSX.Element {
  const { t } = useTranslation();
  const photo = useQuery<string, ApiError>({
    queryKey: ['rma-photo', rmaId, photoId],
    queryFn: () => getRmaPhoto(rmaId, photoId),
    staleTime: Infinity,
  });

  return (
    <figure className="space-y-1">
      <button
        type="button"
        onClick={() => photo.data && onOpen(photo.data)}
        className="block aspect-square w-full overflow-hidden rounded-lg border border-line bg-surface-3"
      >
        {photo.data && (
          <img src={photo.data} alt={caption ?? ''} className="size-full object-cover" />
        )}
      </button>
      <figcaption className="flex items-center justify-between gap-1 text-xs text-ink-muted">
        <span className="truncate">{caption ?? ''}</span>
        {onRemove && (
          <button type="button" onClick={() => void onRemove()} className="text-red-600">
            {t('rma.removePhoto')}
          </button>
        )}
      </figcaption>
    </figure>
  );
}
