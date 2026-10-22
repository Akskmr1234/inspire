import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { Modal } from '@/components/Modal';
import { ReportFrame } from '@/components/ReportFrame';
import { SearchSelect } from '@/components/SearchSelect';
import { BatchPick } from '@/components/BatchPick';
import { Panel } from '@/components/Panel';
import { StatusBadge, type StatusTone } from '@/components/StatusBadge';
import { DateField, SelectField, TextAreaField, TextField } from '@/components/Form';
import { productOption } from '@/components/DocumentLines';
import type { ApiError } from '@/lib/api';
import { listCustomers, type CustomerSummary } from '@/lib/customers';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import {
  billJobCard,
  cancelJobCard,
  createJobCard,
  findJobCardCustomers,
  getJobCard,
  JOB_CARD_STAGES,
  JobCardStatus,
  listJobCards,
  markJobCardStage,
  updateJobCard,
  type JobCardCustomerMatch,
  type JobCardDetail,
  type JobCardInput,
  type JobCardStages,
  type JobCardSummary,
} from '@/lib/jobCards';
import { LedgerKind, listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { moneyAlways } from '@/lib/money';
import { listProducts, type ProductSummary } from '@/lib/products';
import { SalesPaymentMode } from '@/lib/sales';
import { useSession } from '@/stores/session';
import { useSettings } from '@/stores/settings';
import { Alert, Labelled } from '@/pages/StockOperationsPage';

const STATUS_KEYS: Record<number, string> = {
  [JobCardStatus.received]: 'received',
  [JobCardStatus.processing]: 'processing',
  [JobCardStatus.ready]: 'ready',
  [JobCardStatus.notReady]: 'notReady',
  [JobCardStatus.informed]: 'informed',
  [JobCardStatus.delivered]: 'delivered',
  [JobCardStatus.dead]: 'dead',
  [JobCardStatus.cancelled]: 'cancelled',
};

const STATUS_TONES: Record<number, StatusTone> = {
  [JobCardStatus.received]: 'neutral',
  [JobCardStatus.processing]: 'info',
  [JobCardStatus.ready]: 'success',
  [JobCardStatus.notReady]: 'warn',
  [JobCardStatus.informed]: 'brand',
  [JobCardStatus.delivered]: 'success',
  [JobCardStatus.dead]: 'danger',
  [JobCardStatus.cancelled]: 'danger',
};

/** Where a job stands from its ticks, by the same rule the server applies. */
function statusFromStages(stages: JobCardStages): number {
  if (stages.delivered) return JobCardStatus.delivered;
  if (stages.dead) return JobCardStatus.dead;
  if (stages.informed) return JobCardStatus.informed;
  if (stages.ready) return JobCardStatus.ready;
  if (stages.notReady) return JobCardStatus.notReady;
  return stages.processing ? JobCardStatus.processing : JobCardStatus.received;
}

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

function blank(value: string): string | null {
  return value.trim() || null;
}

// ------------------------------------------------------------------------------- the list

/**
 * Service job cards: the devices in the workshop.
 *
 * One list for the counter and the bench alike - found by the customer's mobile, the
 * device or its serial - and one card per device, opened to take it in, record what
 * was found and used, tick off how far it has got, and bill it.
 */
export function JobCardsPage(): React.JSX.Element {
  const { t } = useTranslation();
  const can = useSession((state) => state.can);
  const [params, setParams] = useSearchParams();

  const [status, setStatus] = useState<number | ''>('');
  const [search, setSearch] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [editing, setEditing] = useState<string | 'new' | null>(params.get('open'));
  const [notice, setNotice] = useState<string | null>(null);

  const query = useQuery<readonly JobCardSummary[], ApiError>({
    queryKey: ['job-cards', status, search, from, to],
    queryFn: () => listJobCards(status, search, from, to),
  });

  const close = (message?: string): void => {
    setEditing(null);
    if (params.has('open')) {
      params.delete('open');
      setParams(params, { replace: true });
    }
    if (message) {
      setNotice(message);
      window.setTimeout(() => setNotice(null), 4000);
    }
  };

  const columns: readonly GridColumn<JobCardSummary>[] = [
    {
      key: 'number',
      header: t('service.number'),
      value: (row) => row.number,
      render: (row) => (
        <button
          type="button"
          onClick={() => setEditing(row.id)}
          className="cell-link font-mono"
        >
          {row.number}
        </button>
      ),
    },
    { key: 'date', header: t('service.date'), value: (row) => row.date },
    { key: 'customer', header: t('service.customer'), value: (row) => row.customerName },
    { key: 'mobile', header: t('service.mobile'), value: (row) => row.mobile ?? '' },
    {
      key: 'device',
      header: t('service.device'),
      value: (row) =>
        [row.itemName, row.brandName, row.modelNumber].filter(Boolean).join(' '),
    },
    {
      key: 'serial',
      header: t('service.serialNumber'),
      value: (row) => row.serialNumber ?? '',
    },
    { key: 'fault', header: t('service.fault'), value: (row) => row.fault ?? '' },
    {
      key: 'technician',
      header: t('service.technician'),
      value: (row) => row.technicianName ?? '',
    },
    {
      key: 'estimatedOn',
      header: t('service.estimatedOn'),
      value: (row) => row.estimatedOn ?? '',
    },
    {
      key: 'amount',
      header: t('service.amount'),
      value: (row) => row.amount,
      numeric: true,
      render: (row) => moneyAlways(row.amount),
    },
    {
      key: 'status',
      header: t('service.statusLabel'),
      value: (row) => t(`service.status.${STATUS_KEYS[row.status] ?? 'received'}`),
      render: (row) => (
        <StatusBadge
          tone={STATUS_TONES[row.status] ?? 'neutral'}
          label={t(`service.status.${STATUS_KEYS[row.status] ?? 'received'}`)}
          struck={row.status === JobCardStatus.cancelled}
        />
      ),
    },
    {
      key: 'invoice',
      header: t('service.invoice'),
      value: (row) =>
        row.salesInvoiceStatus === 3 ? '' : (row.salesInvoiceNumber ?? ''),
    },
  ];

  const controls = (
    <div className="toolbar">
      <Labelled label={t('service.search')}>
        <input
          type="search"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder={t('service.searchHint')}
          className="field-input-sm min-w-56"
        />
      </Labelled>
      <Labelled label={t('service.statusLabel')}>
        <SearchSelect
          value={status === '' ? '' : String(status)}
          onChange={(next) => setStatus(next === '' ? '' : Number(next))}
          clearable
          size="sm"
          label={t('service.statusLabel')}
          placeholder={t('service.allStatuses')}
          options={Object.entries(STATUS_KEYS).map(([value, key]) => ({
            value,
            label: t(`service.status.${key}`),
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
      <ReportFrame title={t('nav.jobCards')} controls={controls} query={query}>
        {(rows) => (
          <div className="space-y-3">
            {notice && <Alert tone="ok">{notice}</Alert>}
            <DataGrid
              gridKey="job-cards"
              rows={rows}
              columns={columns}
              rowKey={(row) => row.id}
              emptyMessage={t('service.none')}
              actions={
                can('service:job-card:create') ? (
                  <GridAction
                    label={t('service.new')}
                    onClick={() => setEditing('new')}
                  />
                ) : undefined
              }
            />
          </div>
        )}
      </ReportFrame>

      {editing !== null && (
        <JobCardEditor id={editing === 'new' ? null : editing} onClose={close} />
      )}
    </>
  );
}

// ----------------------------------------------------------------------------- the card

interface LineDraft {
  readonly key: string;
  productId: string;
  quantity: string;
  rate: string;
  remarks: string;
  batchId: string;
}

function blankLine(): LineDraft {
  return {
    key: crypto.randomUUID(),
    productId: '',
    quantity: '1',
    rate: '',
    remarks: '',
    batchId: '',
  };
}

interface Draft {
  date: string;
  warehouseId: string;
  customerLedgerId: string;
  name: string;
  mobile: string;
  alternateMobile: string;
  email: string;
  addressLine1: string;
  addressLine2: string;
  referenceNumber: string;
  paymentMode: number;
  itemName: string;
  brandName: string;
  modelNumber: string;
  colour: string;
  serialNumber: string;
  accessories: string;
  serviceType: string;
  fault: string;
  problem: string;
  remarks: string;
  notes: string;
  estimatedOn: string;
  estimatedTime: string;
  estimatedCharge: string;
  salesmanLedgerId: string;
  technicianLedgerId: string;
}

function draftFrom(card: JobCardDetail | null, warehouseId: string): Draft {
  return {
    date: card?.summary.date ?? todayIso(),
    warehouseId: card?.warehouseId ?? warehouseId,
    customerLedgerId: card?.customerLedgerId ?? '',
    name: card?.customer.name ?? '',
    mobile: card?.customer.mobile ?? '',
    alternateMobile: card?.customer.alternateMobile ?? '',
    email: card?.customer.email ?? '',
    addressLine1: card?.customer.addressLine1 ?? '',
    addressLine2: card?.customer.addressLine2 ?? '',
    referenceNumber: card?.referenceNumber ?? '',
    paymentMode: card?.paymentMode ?? SalesPaymentMode.cash,
    itemName: card?.device.itemName ?? '',
    brandName: card?.device.brandName ?? '',
    modelNumber: card?.device.modelNumber ?? '',
    colour: card?.device.colour ?? '',
    serialNumber: card?.device.serialNumber ?? '',
    accessories: card?.device.accessories ?? '',
    serviceType: card?.work.serviceType ?? '',
    fault: card?.work.fault ?? '',
    problem: card?.work.problem ?? '',
    remarks: card?.work.remarks ?? '',
    notes: card?.work.notes ?? '',
    estimatedOn: card?.work.estimatedOn ?? '',
    estimatedTime: card?.work.estimatedTime ?? '',
    estimatedCharge: card ? String(card.work.estimatedCharge || '') : '',
    salesmanLedgerId: card?.salesmanLedgerId ?? '',
    technicianLedgerId: card?.technicianLedgerId ?? '',
  };
}

/** Opens a card, or a blank one, in a dialog. */
function JobCardEditor({
  id,
  onClose,
}: {
  readonly id: string | null;
  readonly onClose: (message?: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const card = useQuery<JobCardDetail, ApiError>({
    queryKey: ['job-card', id],
    queryFn: () => getJobCard(id ?? ''),
    enabled: id !== null,
  });

  const title =
    id === null ? t('service.new') : (card.data?.summary.number ?? t('service.card'));

  return (
    <Modal title={title} size="fullscreen" onClose={() => onClose()}>
      {id !== null && card.isPending ? (
        <p className="text-sm text-ink-muted">{t('common.loading')}</p>
      ) : card.error ? (
        <Alert tone="error">{card.error.detail || card.error.code}</Alert>
      ) : (
        <JobCardForm
          key={card.data?.summary.id ?? 'new'}
          card={card.data ?? null}
          onClose={onClose}
        />
      )}
    </Modal>
  );
}

function JobCardForm({
  card,
  onClose,
}: {
  readonly card: JobCardDetail | null;
  readonly onClose: (message?: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const can = useSession((state) => state.can);
  const defaultCustomerId = useSettings((state) => state.defaultCustomerId);

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
  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => listLedgers(true),
    staleTime: 5 * 60 * 1000,
  });

  const godowns = useMemo(
    () => (warehouses.data ?? []).filter((warehouse) => !warehouse.isTransit),
    [warehouses.data],
  );
  const defaultGodown =
    godowns.find((warehouse) => warehouse.isDefault)?.id ?? godowns[0]?.id ?? '';

  const [draft, setDraft] = useState<Draft>(() => draftFrom(card, defaultGodown));
  const [lines, setLines] = useState<readonly LineDraft[]>(() =>
    card && card.lines.length > 0
      ? card.lines.map((line) => ({
          key: crypto.randomUUID(),
          productId: line.productId,
          quantity: String(line.quantity),
          rate: String(line.rate),
          remarks: line.remarks ?? '',
          batchId: line.batchId ?? '',
        }))
      : [blankLine()],
  );
  const [stages, setStages] = useState<JobCardStages | null>(card?.stages ?? null);
  const [error, setError] = useState<string | null>(null);
  const [billTo, setBillTo] = useState(card?.customerLedgerId ?? defaultCustomerId ?? '');
  const [cancelling, setCancelling] = useState(false);
  const [reason, setReason] = useState('');

  // The godown list arrives after the form opens; a new card takes the default one.
  useEffect(() => {
    if (!draft.warehouseId && defaultGodown) {
      setDraft((current) => ({ ...current, warehouseId: defaultGodown }));
    }
  }, [defaultGodown, draft.warehouseId]);

  const set = <TKey extends keyof Draft>(key: TKey, value: Draft[TKey]): void =>
    setDraft((current) => ({ ...current, [key]: value }));

  const employees = useMemo(
    () =>
      (ledgers.data ?? [])
        .filter((ledger) => ledger.kind === LedgerKind.employee)
        .map((ledger) => ({
          value: ledger.ledgerId,
          label: ledger.name,
          detail: ledger.code,
        })),
    [ledgers.data],
  );
  const customerOptions = useMemo(
    () =>
      (customers.data ?? []).map((customer) => ({
        value: customer.customerId,
        label: customer.name,
        detail: [customer.code, customer.contact.mobileNumber]
          .filter(Boolean)
          .join(' · '),
      })),
    [customers.data],
  );
  const productOptions = useMemo(
    () => (products.data ?? []).map((product) => productOption(product, undefined)),
    [products.data],
  );
  const productById = useMemo(
    () => new Map((products.data ?? []).map((product) => [product.id, product])),
    [products.data],
  );

  const billed =
    card?.summary.salesInvoiceId != null && card.summary.salesInvoiceStatus !== 3;
  const cancelled = card?.summary.status === JobCardStatus.cancelled;
  const linesLocked = billed || cancelled;
  const editable =
    !cancelled && can(card ? 'service:job-card:edit' : 'service:job-card:create');

  const total = lines.reduce(
    (sum, line) => sum + (Number(line.quantity) || 0) * (Number(line.rate) || 0),
    0,
  );

  const updateLine = (key: string, patch: Partial<LineDraft>): void =>
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );

  const input = (): JobCardInput => ({
    date: draft.date,
    warehouseId: draft.warehouseId,
    customerLedgerId: draft.customerLedgerId || null,
    customer: {
      name: draft.name.trim(),
      mobile: blank(draft.mobile),
      alternateMobile: blank(draft.alternateMobile),
      email: blank(draft.email),
      addressLine1: blank(draft.addressLine1),
      addressLine2: blank(draft.addressLine2),
    },
    referenceNumber: blank(draft.referenceNumber),
    paymentMode: draft.paymentMode,
    device: {
      itemName: draft.itemName.trim(),
      brandName: blank(draft.brandName),
      modelNumber: blank(draft.modelNumber),
      colour: blank(draft.colour),
      serialNumber: blank(draft.serialNumber),
      accessories: blank(draft.accessories),
    },
    work: {
      serviceType: blank(draft.serviceType),
      fault: blank(draft.fault),
      problem: blank(draft.problem),
      remarks: blank(draft.remarks),
      notes: blank(draft.notes),
      estimatedOn: draft.estimatedOn || null,
      estimatedTime: blank(draft.estimatedTime),
      estimatedCharge: Number(draft.estimatedCharge) || 0,
    },
    salesmanLedgerId: draft.salesmanLedgerId || null,
    technicianLedgerId: draft.technicianLedgerId || null,
    lines: lines
      .filter((line) => line.productId && Number(line.quantity) > 0)
      .map((line) => ({
        productId: line.productId,
        quantity: Number(line.quantity),
        rate: Number(line.rate) || 0,
        remarks: blank(line.remarks),
        batchId: line.batchId || null,
      })),
  });

  const refresh = async (): Promise<void> => {
    await queryClient.invalidateQueries({ queryKey: ['job-cards'] });
    await queryClient.invalidateQueries({ queryKey: ['job-card'] });
  };

  const save = useMutation<string, ApiError, void>({
    mutationFn: async () => {
      if (card) {
        await updateJobCard(card.summary.id, input());
        return card.summary.id;
      }
      return createJobCard(input());
    },
    onSuccess: async () => {
      await refresh();
      onClose(t('service.savedNotice'));
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  // The tick shows at once and is taken back if the server refuses it.
  const stage = useMutation<
    void,
    ApiError,
    { stage: number; key: keyof JobCardStages; on: string | null },
    JobCardStages | null
  >({
    mutationFn: ({ stage: which, on }) =>
      markJobCardStage(card?.summary.id ?? '', which, on),
    onMutate: ({ key, on }) => {
      const before = stages;
      setStages((current) => (current ? { ...current, [key]: on } : current));
      return before;
    },
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ['job-cards'] });
    },
    onError: (failure, _, before) => {
      setStages(before ?? null);
      setError(failure.detail || failure.code);
    },
  });

  const bill = useMutation<void, ApiError, void>({
    mutationFn: async () => {
      if (!card) {
        return;
      }
      // Whatever was changed on the card is what gets billed.
      if (editable && !linesLocked) {
        await updateJobCard(card.summary.id, input());
      }
      const raised = await billJobCard(
        card.summary.id,
        card.customerLedgerId ? null : billTo || null,
      );
      await refresh();
      navigate(`/sales/invoices?open=${raised.salesInvoiceId}`);
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const withdraw = useMutation<void, ApiError, void>({
    mutationFn: () => cancelJobCard(card?.summary.id ?? '', reason.trim()),
    onSuccess: async () => {
      await refresh();
      onClose(t('service.cancelledNotice'));
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const submit = (): void => {
    if (!draft.name.trim() || !draft.itemName.trim()) {
      setError(t('service.incomplete'));
      return;
    }
    if (!card && !draft.warehouseId) {
      setError(t('service.chooseGodown'));
      return;
    }
    setError(null);
    save.mutate();
  };

  const busy = save.isPending || bill.isPending || withdraw.isPending;
  const shownStatus =
    cancelled || !stages
      ? (card?.summary.status ?? JobCardStatus.received)
      : statusFromStages(stages);

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
    >
      {card && (
        <div className="flex flex-wrap items-center gap-3 text-sm">
          <StatusBadge
            tone={STATUS_TONES[shownStatus] ?? 'neutral'}
            label={t(`service.status.${STATUS_KEYS[shownStatus] ?? 'received'}`)}
            struck={cancelled}
          />
          {billed && card.summary.salesInvoiceId && (
            <button
              type="button"
              className="cell-link"
              onClick={() =>
                navigate(`/sales/invoices?open=${card.summary.salesInvoiceId}`)
              }
            >
              {t('service.billedBy', { number: card.summary.salesInvoiceNumber })}
            </button>
          )}
          {cancelled && card.cancellationReason && (
            <span className="text-ink-muted">
              {t('service.cancelledBecause', { reason: card.cancellationReason })}
            </span>
          )}
        </div>
      )}

      <fieldset disabled={!editable} className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-5">
          <DateField
            label={t('service.date')}
            value={draft.date}
            onChange={(value) => set('date', value)}
            disabled={card !== null}
          />
          <SelectField<string>
            label={t('service.godown')}
            value={draft.warehouseId}
            onChange={(value) => set('warehouseId', value)}
            disabled={card !== null}
            options={godowns.map((warehouse) => ({
              value: warehouse.id,
              label: warehouse.name,
            }))}
          />
          <TextField
            label={t('service.reference')}
            value={draft.referenceNumber}
            onChange={(value) => set('referenceNumber', value)}
          />
          <SelectField<number>
            label={t('service.paymentMode')}
            value={draft.paymentMode}
            onChange={(value) => set('paymentMode', value)}
            options={[
              { value: SalesPaymentMode.cash, label: t('service.cash') },
              { value: SalesPaymentMode.credit, label: t('service.credit') },
              { value: SalesPaymentMode.online, label: t('service.online') },
              { value: SalesPaymentMode.card, label: t('paymentModes.card') },
            ]}
          />
          <SelectField<string>
            label={t('service.salesman')}
            value={draft.salesmanLedgerId}
            onChange={(value) => set('salesmanLedgerId', value)}
            options={[{ value: '', label: '—' }, ...employees]}
          />
        </div>

        <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
          <Panel title={t('service.customerDetails')}>
            <MobileSearch
              value={draft.mobile}
              onChange={(value) => set('mobile', value)}
              onPick={(match) =>
                setDraft((current) => ({
                  ...current,
                  customerLedgerId: match.customerLedgerId ?? current.customerLedgerId,
                  name: match.customer.name,
                  mobile: match.customer.mobile ?? current.mobile,
                  alternateMobile: match.customer.alternateMobile ?? '',
                  email: match.customer.email ?? '',
                  addressLine1: match.customer.addressLine1 ?? '',
                  addressLine2: match.customer.addressLine2 ?? '',
                }))
              }
            />
            <TextField
              label={t('service.customerName')}
              required
              value={draft.name}
              onChange={(value) => set('name', value)}
            />
            <TextField
              label={t('service.alternateMobile')}
              type="tel"
              value={draft.alternateMobile}
              onChange={(value) => set('alternateMobile', value)}
            />
            <TextField
              label={t('service.email')}
              type="email"
              value={draft.email}
              onChange={(value) => set('email', value)}
            />
            <TextField
              label={t('service.address1')}
              value={draft.addressLine1}
              onChange={(value) => set('addressLine1', value)}
            />
            <TextField
              label={t('service.address2')}
              value={draft.addressLine2}
              onChange={(value) => set('addressLine2', value)}
            />
            <label className="block">
              <span className="field-label">{t('service.account')}</span>
              <SearchSelect
                value={draft.customerLedgerId}
                onChange={(value) => {
                  const chosen = customers.data?.find(
                    (customer) => customer.customerId === value,
                  );
                  setDraft((current) => ({
                    ...current,
                    customerLedgerId: value,
                    name: current.name || chosen?.name || '',
                    mobile: current.mobile || chosen?.contact.mobileNumber || '',
                  }));
                }}
                label={t('service.account')}
                placeholder={t('service.walkIn')}
                options={[{ value: '', label: t('service.walkIn') }, ...customerOptions]}
              />
            </label>
          </Panel>

          <Panel title={t('service.deviceDetails')}>
            <TextField
              label={t('service.itemName')}
              required
              value={draft.itemName}
              onChange={(value) => set('itemName', value)}
            />
            <TextField
              label={t('service.brand')}
              value={draft.brandName}
              onChange={(value) => set('brandName', value)}
            />
            <TextField
              label={t('service.model')}
              value={draft.modelNumber}
              onChange={(value) => set('modelNumber', value)}
            />
            <TextField
              label={t('service.colour')}
              value={draft.colour}
              onChange={(value) => set('colour', value)}
            />
            <TextField
              label={t('service.serialNumber')}
              value={draft.serialNumber}
              onChange={(value) => set('serialNumber', value)}
            />
            <TextAreaField
              label={t('service.accessories')}
              rows={2}
              value={draft.accessories}
              onChange={(value) => set('accessories', value)}
            />
          </Panel>

          <Panel title={t('service.workDetails')}>
            <TextField
              label={t('service.serviceType')}
              value={draft.serviceType}
              onChange={(value) => set('serviceType', value)}
            />
            <TextAreaField
              label={t('service.fault')}
              rows={2}
              value={draft.fault}
              onChange={(value) => set('fault', value)}
            />
            <TextAreaField
              label={t('service.problem')}
              rows={2}
              value={draft.problem}
              onChange={(value) => set('problem', value)}
            />
            <SelectField<string>
              label={t('service.technician')}
              value={draft.technicianLedgerId}
              onChange={(value) => set('technicianLedgerId', value)}
              options={[{ value: '', label: '—' }, ...employees]}
            />
            <div className="grid grid-cols-2 gap-3">
              <DateField
                label={t('service.estimatedOn')}
                value={draft.estimatedOn}
                onChange={(value) => set('estimatedOn', value)}
              />
              <TextField
                label={t('service.estimatedTime')}
                value={draft.estimatedTime}
                onChange={(value) => set('estimatedTime', value)}
              />
            </div>
            <TextField
              label={t('service.estimatedCharge')}
              value={draft.estimatedCharge}
              onChange={(value) => set('estimatedCharge', value.replace(/[^\d.]/g, ''))}
            />
            <TextAreaField
              label={t('service.remarks')}
              rows={2}
              value={draft.remarks}
              onChange={(value) => set('remarks', value)}
            />
            <TextAreaField
              label={t('service.notes')}
              rows={2}
              value={draft.notes}
              onChange={(value) => set('notes', value)}
            />
          </Panel>
        </div>
      </fieldset>

      <Panel title={t('service.stages')}>
        {card && stages ? (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {JOB_CARD_STAGES.map(({ stage: which, key }) => {
              const on = stages[key];
              const allowed =
                !cancelled && can('service:job-card:edit') && !stage.isPending;

              return (
                <div key={key} className="flex items-center gap-3">
                  <label className="flex min-w-32 items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={on !== null}
                      disabled={!allowed}
                      onChange={(event) =>
                        stage.mutate({
                          stage: which,
                          key,
                          on: event.target.checked ? todayIso() : null,
                        })
                      }
                    />
                    {t(`service.stage.${key}`)}
                  </label>
                  {on !== null && (
                    <input
                      type="date"
                      value={on}
                      disabled={!allowed}
                      aria-label={t(`service.stage.${key}`)}
                      onChange={(event) =>
                        event.target.value &&
                        stage.mutate({ stage: which, key, on: event.target.value })
                      }
                      className="field-input-sm w-40"
                    />
                  )}
                </div>
              );
            })}
          </div>
        ) : (
          <p className="text-sm text-ink-muted">{t('service.stagesAfterSave')}</p>
        )}
      </Panel>

      <div className="space-y-2">
        <h3 className="text-sm font-semibold">{t('service.partsAndLabour')}</h3>
        {linesLocked && card && (
          <p className="text-xs text-ink-muted">{t('service.linesLocked')}</p>
        )}
        <div className="overflow-x-auto sm:rounded-xl sm:border sm:border-line sm:bg-surface sm:shadow-card">
          <table className="line-table">
            <thead className="bg-surface-3 text-xs tracking-wide text-ink-muted uppercase">
              <tr>
                <th className="px-3 py-2 text-start font-semibold">
                  {t('service.part')}
                </th>
                <th className="px-3 py-2 text-start font-semibold">{t('stock.batch')}</th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('stock.quantity')}
                </th>
                <th className="px-3 py-2 text-end font-semibold">{t('service.rate')}</th>
                <th className="px-3 py-2 text-end font-semibold">
                  {t('service.amount')}
                </th>
                <th className="px-3 py-2 text-start font-semibold">
                  {t('stock.remarks')}
                </th>
                <th />
              </tr>
            </thead>
            <tbody>
              {lines.map((line) => {
                const product = productById.get(line.productId);
                const locked = linesLocked || !editable;

                return (
                  <tr key={line.key} className="border-t border-line">
                    <td data-label={t('service.part')} className="min-w-64 px-2 py-1">
                      <SearchSelect
                        value={line.productId}
                        disabled={locked}
                        onChange={(productId) => {
                          const picked = productById.get(productId);
                          updateLine(line.key, {
                            productId,
                            batchId: '',
                            rate: line.rate || String(picked?.retailRate ?? ''),
                          });
                        }}
                        size="sm"
                        label={t('service.part')}
                        placeholder={t('stock.choose')}
                        options={productOptions}
                      />
                    </td>
                    <td data-label={t('stock.batch')} className="min-w-40 px-2 py-1">
                      {product?.tracksBatches && !locked ? (
                        <BatchPick
                          productId={line.productId}
                          warehouseId={draft.warehouseId}
                          value={line.batchId}
                          onChange={(batchId) => updateLine(line.key, { batchId })}
                        />
                      ) : (
                        <span className="text-xs text-ink-muted">
                          {card?.lines.find((saved) => saved.batchId === line.batchId)
                            ?.batchNumber ?? ''}
                        </span>
                      )}
                    </td>
                    <td data-label={t('stock.quantity')} className="px-2 py-1">
                      <input
                        type="number"
                        step="any"
                        min="0"
                        disabled={locked}
                        value={line.quantity}
                        onChange={(event) =>
                          updateLine(line.key, { quantity: event.target.value })
                        }
                        className="field-input-sm text-end font-mono tabular-nums"
                      />
                    </td>
                    <td data-label={t('service.rate')} className="px-2 py-1">
                      <input
                        type="number"
                        step="any"
                        min="0"
                        disabled={locked}
                        value={line.rate}
                        onChange={(event) =>
                          updateLine(line.key, { rate: event.target.value })
                        }
                        className="field-input-sm text-end font-mono tabular-nums"
                      />
                    </td>
                    <td data-label={t('service.amount')} className="cell-numeric">
                      {line.productId
                        ? moneyAlways(
                            (Number(line.quantity) || 0) * (Number(line.rate) || 0),
                          )
                        : ''}
                    </td>
                    <td data-label={t('stock.remarks')} className="px-2 py-1">
                      <input
                        value={line.remarks}
                        disabled={locked}
                        onChange={(event) =>
                          updateLine(line.key, { remarks: event.target.value })
                        }
                        className="field-input-sm"
                      />
                    </td>
                    <td className="px-2 py-1 text-end">
                      <button
                        type="button"
                        disabled={locked || lines.length === 1}
                        onClick={() =>
                          setLines((current) =>
                            current.filter((candidate) => candidate.key !== line.key),
                          )
                        }
                        className="rounded-md border border-line px-2 py-0.5 text-xs text-ink-muted disabled:opacity-40"
                      >
                        {t('stock.removeLine')}
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
            <tfoot>
              <tr className="border-t border-line font-semibold">
                <td colSpan={4} className="px-3 py-2 text-end">
                  {t('service.total')}
                </td>
                <td className="cell-numeric">{moneyAlways(total)}</td>
                <td colSpan={2} className="px-3 py-2 text-xs font-normal text-ink-muted">
                  {draft.estimatedCharge
                    ? t('service.estimateWas', {
                        amount: moneyAlways(Number(draft.estimatedCharge)),
                      })
                    : ''}
                </td>
              </tr>
            </tfoot>
          </table>
        </div>
        {!linesLocked && editable && (
          <button
            type="button"
            onClick={() => setLines((current) => [...current, blankLine()])}
            className="btn-secondary"
          >
            {t('stock.addLine')}
          </button>
        )}
      </div>

      {error && <Alert tone="error">{error}</Alert>}

      {cancelling && card && (
        <div className="flex flex-wrap items-end gap-3 rounded-xl border border-line p-3">
          <TextField
            label={t('service.cancelReason')}
            value={reason}
            onChange={setReason}
            className="min-w-64 flex-1"
          />
          <button
            type="button"
            disabled={!reason.trim() || busy}
            onClick={() => withdraw.mutate()}
            className="btn-danger"
          >
            {t('service.confirmCancel')}
          </button>
          <button
            type="button"
            onClick={() => setCancelling(false)}
            className="btn-secondary"
          >
            {t('service.keepJob')}
          </button>
        </div>
      )}

      <div className="flex flex-wrap items-center gap-3 border-t border-line/60 pt-3">
        {editable && (
          <button type="submit" disabled={busy} className="btn-primary">
            {card ? t('service.save') : t('service.takeIn')}
          </button>
        )}
        {card && !billed && !cancelled && can('sales:invoice:create') && (
          <>
            {!card.customerLedgerId && (
              <div className="min-w-56">
                <SearchSelect
                  value={billTo}
                  onChange={setBillTo}
                  size="sm"
                  label={t('service.billTo')}
                  placeholder={t('service.billTo')}
                  options={customerOptions}
                />
              </div>
            )}
            <button
              type="button"
              disabled={busy || lines.every((line) => !line.productId)}
              onClick={() => {
                if (!card.customerLedgerId && !billTo) {
                  setError(t('service.chooseBillTo'));
                  return;
                }
                setError(null);
                bill.mutate();
              }}
              className="btn-secondary"
            >
              {t('service.raiseInvoice')}
            </button>
          </>
        )}
        {card &&
          !billed &&
          !cancelled &&
          can('service:job-card:delete') &&
          !cancelling && (
            <button
              type="button"
              onClick={() => setCancelling(true)}
              className="btn-secondary"
            >
              {t('service.cancelJob')}
            </button>
          )}
        <button type="button" onClick={() => onClose()} className="btn-secondary ms-auto">
          {t('common.close')}
        </button>
      </div>
    </form>
  );
}

/**
 * The mobile number, which is how the counter finds a returning customer: three digits
 * in, it offers the people who gave a number like it before.
 */
function MobileSearch({
  value,
  onChange,
  onPick,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly onPick: (match: JobCardCustomerMatch) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [typed, setTyped] = useState('');
  const [open, setOpen] = useState(false);

  // Waits for the typing to pause rather than asking on every key.
  useEffect(() => {
    const timer = window.setTimeout(() => setTyped(value.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [value]);

  const matches = useQuery<readonly JobCardCustomerMatch[], ApiError>({
    queryKey: ['job-card-customers', typed],
    queryFn: () => findJobCardCustomers(typed),
    enabled: open && typed.length >= 3,
  });

  const found = matches.data ?? [];

  return (
    <div className="relative">
      <TextField
        label={t('service.mobile')}
        type="tel"
        value={value}
        onChange={(next) => {
          onChange(next);
          setOpen(true);
        }}
        hint={t('service.mobileHint')}
      />
      {open && found.length > 0 && (
        <ul className="absolute inset-x-0 top-full z-20 mt-1 max-h-64 overflow-auto rounded-xl border border-line bg-surface shadow-card">
          {found.map((match) => (
            <li key={`${match.customerLedgerId ?? ''}-${match.customer.mobile ?? ''}`}>
              <button
                type="button"
                onClick={() => {
                  onPick(match);
                  setOpen(false);
                }}
                className="block w-full px-3 py-2 text-start text-sm hover:bg-surface-3"
              >
                <span className="font-medium">{match.customer.name}</span>{' '}
                <span className="font-mono text-xs text-ink-muted">
                  {match.customer.mobile}
                </span>
                {match.customer.addressLine1 && (
                  <span className="block text-xs text-ink-muted">
                    {match.customer.addressLine1}
                  </span>
                )}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
