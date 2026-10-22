import { useEffect, useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import clsx from 'clsx';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { PageHeading } from '@/components/PageHeading';
import { Spinner } from '@/components/ReportFrame';
import { SearchSelect } from '@/components/SearchSelect';
import { StatusBadge, type StatusTone } from '@/components/StatusBadge';
import {
  DateField,
  Field,
  NumberField,
  TextAreaField,
  TextField,
} from '@/components/Form';
import { productOption } from '@/components/DocumentLines';
import { MobileSearch } from '@/components/MobileSearch';
import { IconBarcode, IconClose, IconPlus } from '@/components/icons';
import { ApiError } from '@/lib/api';
import { chargeRows, useChargeSetup } from '@/lib/charges';
import { listCustomers, type CustomerSummary } from '@/lib/customers';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import {
  billJobCard,
  createJobCard,
  getJobCard,
  JOB_CARD_STAGES,
  JobCardStatus,
  listJobCards,
  markJobCardStage,
  updateJobCard,
  type JobCardDetail,
  type JobCardInput,
  type JobCardSummary,
} from '@/lib/jobCards';
import { isCashOrBank, LedgerKind, listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { moneyAlways as money } from '@/lib/money';
import { listProducts, type ProductSummary } from '@/lib/products';
import { postSalesInvoice, SalesPaymentMode, TaxMode } from '@/lib/sales';
import { useSession } from '@/stores/session';
import { useSettings } from '@/stores/settings';

const LIST = '/service/sales';

/** The wire value of each status, for the `service.status.<key>` lookup. */
const STATUS_KEY: Record<number, string> = {
  [JobCardStatus.received]: 'received',
  [JobCardStatus.processing]: 'processing',
  [JobCardStatus.ready]: 'ready',
  [JobCardStatus.notReady]: 'notReady',
  [JobCardStatus.informed]: 'informed',
  [JobCardStatus.delivered]: 'delivered',
  [JobCardStatus.dead]: 'dead',
  [JobCardStatus.cancelled]: 'cancelled',
};

const STATUS_TONE: Record<number, StatusTone> = {
  [JobCardStatus.received]: 'neutral',
  [JobCardStatus.processing]: 'info',
  [JobCardStatus.ready]: 'success',
  [JobCardStatus.notReady]: 'warn',
  [JobCardStatus.informed]: 'brand',
  [JobCardStatus.delivered]: 'success',
  [JobCardStatus.dead]: 'danger',
  [JobCardStatus.cancelled]: 'danger',
};

/** The colour each stage is ticked in, as the counter knows them. */
const STAGE_COLOUR: Record<string, string> = {
  processing: 'bg-sky-500 text-white',
  ready: 'bg-emerald-400 text-white',
  notReady: 'bg-orange-500 text-white',
  informed: 'bg-teal-500 text-white',
  delivered: 'bg-green-600 text-white',
  dead: 'bg-yellow-700 text-white',
};

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * Service sales: devices taken in for repair and billed on the same screen.
 *
 * The list a menu entry opens on, newest first, with New above it. Each row is a job
 * card - the same jobs the workshop works through under Job cards - with what its
 * bill comes to.
 */
export function ServiceSalesPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const can = useSession((s) => s.can);
  const [params, setParams] = useSearchParams();
  const [status, setStatus] = useState('');
  const notice = params.get('saved');

  const sales = useQuery<readonly JobCardSummary[], ApiError>({
    queryKey: ['job-cards', 'service-sales', status],
    queryFn: () => listJobCards(status === '' ? '' : Number(status), '', '', ''),
  });

  const columns: readonly GridColumn<JobCardSummary>[] = [
    {
      key: 'number',
      header: t('serviceSales.jobNo'),
      value: (row) => row.number,
      render: (row) => (
        <button
          type="button"
          className="font-medium text-brand-700 hover:underline dark:text-brand-300"
          onClick={() => void navigate(`${LIST}/${row.id}`)}
        >
          {row.number}
        </button>
      ),
    },
    { key: 'date', header: t('service.date'), value: (row) => row.date },
    {
      key: 'invoice',
      header: t('serviceSales.invoiceNo'),
      value: (row) => row.salesInvoiceNumber ?? '',
    },
    { key: 'customer', header: t('service.customer'), value: (row) => row.customerName },
    {
      key: 'mobile',
      header: t('serviceSales.mobile1'),
      value: (row) => row.mobile ?? '',
    },
    {
      key: 'item',
      header: t('serviceSales.item'),
      value: (row) =>
        [row.itemName, row.brandName, row.modelNumber].filter(Boolean).join(' · '),
    },
    {
      key: 'technician',
      header: t('service.technician'),
      value: (row) => row.technicianName ?? '',
    },
    {
      key: 'status',
      header: t('service.statusLabel'),
      value: (row) => t(`service.status.${STATUS_KEY[row.status] ?? 'received'}`),
      render: (row) => (
        <StatusBadge
          tone={STATUS_TONE[row.status] ?? 'neutral'}
          label={t(`service.status.${STATUS_KEY[row.status] ?? 'received'}`)}
          struck={row.status === JobCardStatus.cancelled}
        />
      ),
    },
    {
      key: 'total',
      header: t('serviceSales.gTotal'),
      numeric: true,
      value: (row) => row.amount + row.chargeTotal,
      render: (row) => money(row.amount + row.chargeTotal),
    },
  ];

  return (
    <section className="page">
      <PageHeading title={t('serviceSales.title')} />

      {notice !== null && (
        <p className="alert-success flex items-center gap-3">
          <span>{notice}</span>
          <button
            type="button"
            onClick={() => setParams({}, { replace: true })}
            className="ms-auto text-xs font-medium underline underline-offset-2"
          >
            {t('common.dismiss')}
          </button>
        </p>
      )}

      {sales.isError && (
        <p className="alert-error">{sales.error.detail || sales.error.code}</p>
      )}

      <DataGrid
        gridKey="service-sales"
        rows={sales.data ?? []}
        columns={columns}
        rowKey={(row) => row.id}
        emptyMessage={sales.isPending ? t('common.loading') : t('serviceSales.none')}
        filters={
          <select
            aria-label={t('service.statusLabel')}
            value={status}
            onChange={(event) => setStatus(event.target.value)}
            className="field-input-sm w-auto"
          >
            <option value="">{t('service.allStatuses')}</option>
            {Object.entries(STATUS_KEY).map(([value, key]) => (
              <option key={value} value={value}>
                {t(`service.status.${key}`)}
              </option>
            ))}
          </select>
        }
        actions={
          can('service:job-card:create') && (
            <button
              type="button"
              className="btn-primary btn-sm"
              onClick={() => void navigate(`${LIST}/new`)}
            >
              {t('serviceSales.new')}
            </button>
          )
        }
      />
    </section>
  );
}

/** A new service sale, or one reopened from the list to work on and bill. */
export function ServiceSaleEntryPage(): React.JSX.Element {
  const { t } = useTranslation();
  const { jobCardId } = useParams<{ jobCardId: string }>();
  const [params] = useSearchParams();

  const existing = useQuery<JobCardDetail, ApiError>({
    queryKey: ['job-card', jobCardId],
    queryFn: () => getJobCard(jobCardId ?? ''),
    enabled: jobCardId !== undefined,
    staleTime: 0,
  });

  if (jobCardId !== undefined && !existing.data) {
    return (
      <section className="page">
        <PageHeading title={t('serviceSales.title')} />
        {existing.isError ? (
          <p className="alert-error">{existing.error.detail || existing.error.code}</p>
        ) : (
          <p className="flex items-center gap-2 text-sm text-ink-muted">
            <Spinner /> {t('common.loading')}
          </p>
        )}
      </section>
    );
  }

  return (
    <ServiceSaleForm
      key={jobCardId ?? 'new'}
      card={existing.data ?? null}
      warning={params.get('warning')}
    />
  );
}

interface LineDraft {
  readonly key: string;
  productId: string;
  quantity: string;
  rate: string;
}

function blankLine(): LineDraft {
  return { key: crypto.randomUUID(), productId: '', quantity: '1', rate: '' };
}

type StageKey = (typeof JOB_CARD_STAGES)[number]['key'];

function ServiceSaleForm({
  card,
  warning,
}: {
  readonly card: JobCardDetail | null;
  /** What went wrong after a new sale was saved, carried over to its own address. */
  readonly warning: string | null;
}): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const settings = useSettings();
  const can = useSession((s) => s.can);

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
  const chargeSetup = useChargeSetup('sales');

  const billed = card?.summary.salesInvoiceId != null;
  const locked = billed || card?.summary.status === JobCardStatus.cancelled;

  // The header.
  const [date, setDate] = useState(card?.summary.date ?? today());
  const [warehouseId, setWarehouseId] = useState(card?.warehouseId ?? '');
  const [reference, setReference] = useState(card?.referenceNumber ?? '');
  const [paymentMode, setPaymentMode] = useState<number>(
    card?.paymentMode ?? SalesPaymentMode.cash,
  );
  const [account, setAccount] = useState(
    card?.customerLedgerId ?? settings.defaultCustomerId ?? '',
  );
  const [mode, setMode] = useState<number>(card?.mode ?? TaxMode.tax);
  const [salesman, setSalesman] = useState(card?.salesmanLedgerId ?? '');
  const [receivedInto, setReceivedInto] = useState('');

  // Who brought it in, and what.
  const [customer, setCustomer] = useState({
    name: card?.customer.name ?? '',
    mobile: card?.customer.mobile ?? '',
    alternateMobile: card?.customer.alternateMobile ?? '',
    email: card?.customer.email ?? '',
    addressLine1: card?.customer.addressLine1 ?? '',
    addressLine2: card?.customer.addressLine2 ?? '',
  });
  const [device, setDevice] = useState({
    itemName: card?.device.itemName ?? '',
    brandName: card?.device.brandName ?? '',
    modelNumber: card?.device.modelNumber ?? '',
    colour: card?.device.colour ?? '',
    serialNumber: card?.device.serialNumber ?? '',
    accessories: card?.device.accessories ?? '',
  });
  const [work, setWork] = useState({
    serviceType: card?.work.serviceType ?? '',
    fault: card?.work.fault ?? '',
    problem: card?.work.problem ?? '',
    remarks: card?.work.remarks ?? '',
    notes: card?.work.notes ?? '',
    estimatedOn: card?.work.estimatedOn ?? '',
    estimatedTime: card?.work.estimatedTime ?? '',
    estimatedCharge:
      card && card.work.estimatedCharge > 0 ? String(card.work.estimatedCharge) : '',
  });
  const [technician, setTechnician] = useState(card?.technicianLedgerId ?? '');

  // The stages, each with the day it was reached.
  const [stages, setStages] = useState<Record<StageKey, string | null>>(() => ({
    processing: card?.stages.processing ?? null,
    ready: card?.stages.ready ?? null,
    notReady: card?.stages.notReady ?? null,
    informed: card?.stages.informed ?? null,
    delivered: card?.stages.delivered ?? null,
    dead: card?.stages.dead ?? null,
  }));

  // The bill: parts and labour, then the additional ledgers.
  const [lines, setLines] = useState<readonly LineDraft[]>(() =>
    card && card.lines.length > 0
      ? card.lines.map((line) => ({
          key: crypto.randomUUID(),
          productId: line.productId,
          quantity: String(line.quantity),
          rate: String(line.rate),
        }))
      : [blankLine()],
  );
  const [charges, setCharges] = useState<readonly { ledgerId: string; amount: string }[]>(
    () =>
      card
        ? (card.charges ?? []).map((charge) => ({
            ledgerId: charge.ledgerId,
            amount: String(charge.amount),
          }))
        : (settings.defaultAdditionalLedgers ?? [])
            .filter(
              (row) =>
                row.transactionType === 'Sales' &&
                row.isDefaultActive &&
                Number(row.defaultAmount) > 0,
            )
            .map((row) => ({ ledgerId: row.ledgerId, amount: row.defaultAmount })),
  );

  const [barcode, setBarcode] = useState('');
  const [notice, setNotice] = useState<string | null>(warning);
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const godowns = useMemo(
    () => (warehouses.data ?? []).filter((warehouse) => !warehouse.isTransit),
    [warehouses.data],
  );

  // A new sale takes the default godown once the list arrives.
  useEffect(() => {
    if (!warehouseId && godowns.length > 0) {
      setWarehouseId(
        godowns.find((warehouse) => warehouse.isDefault)?.id ?? godowns[0]!.id,
      );
    }
  }, [godowns, warehouseId]);

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
      (customers.data ?? []).map((row) => ({
        value: row.customerId,
        label: row.name,
        detail: [row.code, row.contact.mobileNumber].filter(Boolean).join(' · '),
      })),
    [customers.data],
  );
  const productById = useMemo(
    () => new Map((products.data ?? []).map((product) => [product.id, product])),
    [products.data],
  );
  const productOptions = useMemo(
    () => (products.data ?? []).map((product) => productOption(product, undefined)),
    [products.data],
  );

  // Where a payment at the counter goes: the drawer for cash, the bank otherwise.
  const moneyAccounts = useMemo(
    () => (ledgers.data ?? []).filter(isCashOrBank),
    [ledgers.data],
  );
  const wantsBank =
    paymentMode === SalesPaymentMode.card || paymentMode === SalesPaymentMode.online;
  const intoOptions = moneyAccounts.filter((ledger) =>
    wantsBank ? ledger.kind === LedgerKind.bank : ledger.kind === LedgerKind.cash,
  );
  const intoId = intoOptions.some((ledger) => ledger.ledgerId === receivedInto)
    ? receivedInto
    : (intoOptions.find(
        (ledger) =>
          ledger.ledgerId ===
          (wantsBank ? settings.defaultBankLedgerId : settings.defaultCashLedgerId),
      )?.ledgerId ??
      intoOptions[0]?.ledgerId ??
      '');

  const godownName =
    godowns.find((warehouse) => warehouse.id === warehouseId)?.name ?? '';
  const nonTax = mode === TaxMode.nonTax;

  // What the bill comes to, as the server will work it out.
  const totals = useMemo(() => {
    let goods = 0;
    let tax = 0;

    for (const line of lines) {
      const amount = Number(line.quantity) * Number(line.rate);

      if (!Number.isFinite(amount) || amount <= 0) continue;
      goods += amount;

      if (!nonTax) {
        const rate = productById.get(line.productId)?.taxPercentage ?? 0;
        tax += Math.round(amount * rate) / 100;
      }
    }

    const shown = chargeRows(chargeSetup, nonTax, charges, ledgers.data ?? []);
    const extra = shown.reduce((sum, row) => {
      const amount = Number(
        charges.find((charge) => charge.ledgerId === row.ledgerId)?.amount,
      );
      return Number.isFinite(amount) && amount > 0
        ? sum + (row.adds ? amount : -amount)
        : sum;
    }, 0);

    return { goods, tax, extra, grand: goods + tax + extra, shown };
  }, [lines, nonTax, productById, chargeSetup, charges, ledgers.data]);

  const setLine = (key: string, patch: Partial<LineDraft>): void =>
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );

  const chooseProduct = (key: string, productId: string): void => {
    const product = productById.get(productId);
    setLine(key, {
      productId,
      ...(product && product.retailRate > 0 ? { rate: String(product.retailRate) } : {}),
    });
  };

  /** Adds a scanned part: one more of it if it is already on the bill, else a new line. */
  const scan = async (code: string): Promise<void> => {
    const typed = code.trim();
    if (!typed) return;

    const all = products.data ?? [];
    let product = all.find(
      (candidate) => candidate.code.toLowerCase() === typed.toLowerCase(),
    );

    if (!product) {
      try {
        const matches = await listProducts(typed, '', false);
        product = matches.length === 1 ? matches[0] : undefined;
      } catch {
        product = undefined;
      }
    }

    setBarcode('');

    if (!product) {
      setProblem(t('serviceSales.notFound'));
      return;
    }

    const found = product;
    setProblem(null);
    setLines((current) => {
      const same = current.find((line) => line.productId === found.id);

      if (same) {
        return current.map((line) =>
          line === same
            ? { ...line, quantity: String(Number(line.quantity || '0') + 1) }
            : line,
        );
      }

      const blank = current.find((line) => line.productId === '');
      const filled = {
        productId: found.id,
        quantity: '1',
        rate: found.retailRate > 0 ? String(found.retailRate) : '',
      };

      return blank
        ? current.map((line) => (line === blank ? { ...line, ...filled } : line))
        : [...current, { key: crypto.randomUUID(), ...filled }];
    });
  };

  const input = (): JobCardInput => ({
    date,
    warehouseId,
    customerLedgerId: account || null,
    customer: {
      name: customer.name.trim(),
      mobile: customer.mobile.trim() || null,
      alternateMobile: customer.alternateMobile.trim() || null,
      email: customer.email.trim() || null,
      addressLine1: customer.addressLine1.trim() || null,
      addressLine2: customer.addressLine2.trim() || null,
    },
    referenceNumber: reference.trim() || null,
    paymentMode,
    device: {
      itemName: device.itemName.trim(),
      brandName: device.brandName.trim() || null,
      modelNumber: device.modelNumber.trim() || null,
      colour: device.colour.trim() || null,
      serialNumber: device.serialNumber.trim() || null,
      accessories: device.accessories.trim() || null,
    },
    work: {
      serviceType: work.serviceType.trim() || null,
      fault: work.fault.trim() || null,
      problem: work.problem.trim() || null,
      remarks: work.remarks.trim() || null,
      notes: work.notes.trim() || null,
      estimatedOn: work.estimatedOn || null,
      estimatedTime: work.estimatedTime.trim() || null,
      estimatedCharge: Number(work.estimatedCharge) || 0,
    },
    salesmanLedgerId: salesman || null,
    technicianLedgerId: technician || null,
    lines: lines
      .filter((line) => line.productId !== '' && Number(line.quantity) > 0)
      .map((line) => ({
        productId: line.productId,
        quantity: Number(line.quantity),
        rate: Number(line.rate) || 0,
        remarks: null,
        batchId: null,
      })),
    mode,
    charges: totals.shown
      .map((row) => ({
        ledgerId: row.ledgerId,
        amount: Number(
          charges.find((charge) => charge.ledgerId === row.ledgerId)?.amount,
        ),
      }))
      .filter((charge) => Number.isFinite(charge.amount) && charge.amount > 0),
  });

  /** Saves the card, then the stages that changed; answers its id. */
  const persist = async (created: (id: string) => void): Promise<string> => {
    let id = card?.summary.id ?? '';

    if (card) {
      await updateJobCard(id, input());
    } else {
      id = await createJobCard(input());
      created(id);
    }

    for (const { stage, key } of JOB_CARD_STAGES) {
      const was = card?.stages[key] ?? null;

      if (stages[key] !== was) {
        await markJobCardStage(id, stage, stages[key]);
      }
    }

    await queryClient.invalidateQueries({ queryKey: ['job-cards'] });
    await queryClient.invalidateQueries({ queryKey: ['job-card', id] });

    return id;
  };

  const save = async (bill: boolean): Promise<void> => {
    if (!customer.name.trim() || !device.itemName.trim()) {
      setProblem(t('service.incomplete'));
      return;
    }

    if (bill && !account) {
      setProblem(t('serviceSales.billNeedsCustomer'));
      return;
    }

    setBusy(true);
    setProblem(null);

    // Once a new sale is saved, anything still to tell is told on its own screen, so
    // trying again updates it rather than taking the device in a second time.
    let id = card?.summary.id ?? '';
    const warnOnSaved = (message: string): boolean => {
      if (card || id === '') return false;
      void navigate(
        `${LIST}/${id}?${new URLSearchParams({ warning: message }).toString()}`,
        {
          replace: true,
        },
      );
      return true;
    };

    try {
      id = await persist((saved) => {
        id = saved;
      });

      if (!bill) {
        void navigate(
          `${LIST}?${new URLSearchParams({ saved: t('service.savedNotice') }).toString()}`,
        );
        return;
      }

      const paid = paymentMode !== SalesPaymentMode.credit && intoId !== '';
      const invoice = await billJobCard(
        id,
        account || null,
        paid ? { paidInFull: true, receivedIntoLedgerId: intoId } : {},
      );

      try {
        await postSalesInvoice(invoice.salesInvoiceId);
        void navigate(
          `${LIST}?${new URLSearchParams({ saved: t('serviceSales.billed', { number: invoice.number }) }).toString()}`,
        );
      } catch (failure) {
        // The draft invoice stands, linked to the job; the reason posting refused it
        // is what somebody needs to put it right on the sales screen.
        const message = t('serviceSales.postFailed', {
          number: invoice.number,
          reason:
            failure instanceof ApiError
              ? failure.detail || failure.code
              : String(failure),
        });
        await queryClient.invalidateQueries({ queryKey: ['job-card', id] });

        if (!warnOnSaved(message)) setNotice(message);
      }
    } catch (failure) {
      const message =
        failure instanceof ApiError ? failure.detail || failure.code : String(failure);

      if (!warnOnSaved(message)) setProblem(message);
    } finally {
      setBusy(false);
    }
  };

  const invoiceNumber = card?.summary.salesInvoiceNumber ?? null;

  return (
    <section className="page">
      <PageHeading
        title={
          card
            ? t('serviceSales.editTitle', { number: card.summary.number })
            : t('serviceSales.newTitle')
        }
      />

      {notice && <p className="alert-warn">{notice}</p>}
      {problem && <p className="alert-error">{problem}</p>}
      {locked && (
        <p className="alert-warn text-xs">
          {billed
            ? t('service.billedBy', { number: invoiceNumber ?? '' })
            : t('service.cancelledBecause', { reason: card?.cancellationReason ?? '' })}
        </p>
      )}

      <div className="card card-body space-y-4">
        {/* The header: what the bill is, who it is to, and how it is paid. */}
        <div className="grid grid-cols-1 gap-3 rounded-xl border border-line bg-surface-2/40 p-3 sm:grid-cols-2 lg:grid-cols-6">
          <Field label={t('serviceSales.invoiceNo')}>
            <input
              readOnly
              value={invoiceNumber ?? card?.summary.number ?? t('numbering.issuedOnSave')}
              className="field-input-sm opacity-70"
            />
          </Field>
          <Field label={t('service.salesman')}>
            <SearchSelect
              value={salesman}
              onChange={setSalesman}
              options={employees}
              clearable
              size="sm"
              label={t('service.salesman')}
            />
          </Field>
          <Field label={t('service.customer')} className="lg:col-span-2">
            <SearchSelect
              value={account}
              onChange={setAccount}
              options={customerOptions}
              clearable
              size="sm"
              label={t('service.account')}
              placeholder={t('service.walkIn')}
            />
          </Field>
          <DateField
            label={t('service.date')}
            value={date}
            onChange={setDate}
            size="sm"
          />
          <TextField
            label={t('serviceSales.address1')}
            value={customer.addressLine1}
            onChange={(addressLine1) => setCustomer({ ...customer, addressLine1 })}
            size="sm"
          />
          <TextField
            label={t('service.reference')}
            value={reference}
            onChange={setReference}
            size="sm"
          />
          <Field label={t('serviceSales.cashCredit')}>
            <select
              aria-label={t('serviceSales.cashCredit')}
              value={paymentMode}
              disabled={locked}
              onChange={(event) => setPaymentMode(Number(event.target.value))}
              className="field-input-sm"
            >
              <option value={SalesPaymentMode.cash}>{t('paymentModes.cash')}</option>
              <option value={SalesPaymentMode.card}>{t('paymentModes.card')}</option>
              <option value={SalesPaymentMode.online}>{t('paymentModes.online')}</option>
              <option value={SalesPaymentMode.credit}>{t('paymentModes.credit')}</option>
            </select>
          </Field>
          <Field label={t('serviceSales.mode')}>
            <select
              aria-label={t('serviceSales.mode')}
              value={mode}
              disabled={locked}
              onChange={(event) => setMode(Number(event.target.value))}
              className="field-input-sm"
            >
              <option value={TaxMode.tax}>{t('serviceSales.tax')}</option>
              <option value={TaxMode.nonTax}>{t('serviceSales.nonTax')}</option>
            </select>
          </Field>
          <Field label={t('sales.receivedInto')}>
            <select
              aria-label={t('sales.receivedInto')}
              value={intoId}
              disabled={locked || paymentMode === SalesPaymentMode.credit}
              onChange={(event) => setReceivedInto(event.target.value)}
              className="field-input-sm"
            >
              {intoOptions.map((ledger) => (
                <option key={ledger.ledgerId} value={ledger.ledgerId}>
                  {ledger.name}
                </option>
              ))}
            </select>
          </Field>
          <Field label={t('service.godown')}>
            <select
              aria-label={t('service.godown')}
              value={warehouseId}
              disabled={card !== null}
              onChange={(event) => setWarehouseId(event.target.value)}
              className="field-input-sm"
            >
              {godowns.map((warehouse) => (
                <option key={warehouse.id} value={warehouse.id}>
                  {warehouse.name}
                </option>
              ))}
            </select>
          </Field>
          <TextField
            label={t('serviceSales.address2')}
            value={customer.addressLine2}
            onChange={(addressLine2) => setCustomer({ ...customer, addressLine2 })}
            size="sm"
          />
        </div>

        {/* The customer and the device on the left; what is wrong on the right. */}
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:col-span-8">
            <MobileSearch
              label={t('serviceSales.mobile1')}
              size="sm"
              value={customer.mobile}
              onChange={(mobile) => setCustomer({ ...customer, mobile })}
              onPick={(match) => {
                setCustomer({
                  name: match.customer.name,
                  mobile: match.customer.mobile ?? '',
                  alternateMobile: match.customer.alternateMobile ?? '',
                  email: match.customer.email ?? '',
                  addressLine1: match.customer.addressLine1 ?? '',
                  addressLine2: match.customer.addressLine2 ?? '',
                });
                if (match.customerLedgerId) setAccount(match.customerLedgerId);
              }}
            />
            <TextField
              label={t('serviceSales.mobile2')}
              type="tel"
              value={customer.alternateMobile}
              onChange={(alternateMobile) =>
                setCustomer({ ...customer, alternateMobile })
              }
              size="sm"
            />
            <TextField
              label={t('service.customer')}
              required
              value={customer.name}
              onChange={(name) => setCustomer({ ...customer, name })}
              size="sm"
            />
            <TextField
              label={t('serviceSales.mailId')}
              type="email"
              value={customer.email}
              onChange={(email) => setCustomer({ ...customer, email })}
              size="sm"
            />
            <TextField
              label={t('serviceSales.item')}
              required
              value={device.itemName}
              onChange={(itemName) => setDevice({ ...device, itemName })}
              size="sm"
            />
            <TextField
              label={t('service.brand')}
              value={device.brandName}
              onChange={(brandName) => setDevice({ ...device, brandName })}
              size="sm"
            />
            <TextField
              label={t('service.model')}
              value={device.modelNumber}
              onChange={(modelNumber) => setDevice({ ...device, modelNumber })}
              size="sm"
            />
            <TextField
              label={t('service.colour')}
              value={device.colour}
              onChange={(colour) => setDevice({ ...device, colour })}
              size="sm"
            />
            <TextField
              label={t('serviceSales.serialNo')}
              value={device.serialNumber}
              onChange={(serialNumber) => setDevice({ ...device, serialNumber })}
              size="sm"
            />
            <Field label={t('service.technician')}>
              <SearchSelect
                value={technician}
                onChange={setTechnician}
                options={employees}
                clearable
                size="sm"
                label={t('service.technician')}
              />
            </Field>
            <TextField
              label={t('service.serviceType')}
              value={work.serviceType}
              onChange={(serviceType) => setWork({ ...work, serviceType })}
              size="sm"
            />
            <DateField
              label={t('serviceSales.eDate')}
              value={work.estimatedOn}
              onChange={(estimatedOn) => setWork({ ...work, estimatedOn })}
              size="sm"
            />
            <NumberField
              label={t('serviceSales.eRate')}
              value={work.estimatedCharge}
              onChange={(estimatedCharge) => setWork({ ...work, estimatedCharge })}
              size="sm"
            />
            <TextField
              label={t('serviceSales.eTime')}
              value={work.estimatedTime}
              onChange={(estimatedTime) => setWork({ ...work, estimatedTime })}
              placeholder="10:30"
              size="sm"
            />
            <TextField
              label={t('serviceSales.accessories')}
              value={device.accessories}
              onChange={(accessories) => setDevice({ ...device, accessories })}
              className="sm:col-span-2"
              size="sm"
            />
          </div>

          <div className="space-y-3 lg:col-span-4">
            <TextAreaField
              label={t('serviceSales.fault')}
              value={work.fault}
              onChange={(fault) => setWork({ ...work, fault })}
              rows={2}
              size="sm"
            />
            <TextAreaField
              label={t('service.remarks')}
              value={work.remarks}
              onChange={(remarks) => setWork({ ...work, remarks })}
              rows={2}
              size="sm"
            />
            <TextAreaField
              label={t('serviceSales.notes')}
              value={work.notes}
              onChange={(notes) => setWork({ ...work, notes })}
              rows={2}
              size="sm"
            />
            <TextField
              label={t('serviceSales.problem')}
              value={work.problem}
              onChange={(problemFound) => setWork({ ...work, problem: problemFound })}
              size="sm"
            />
          </div>
        </div>

        {/* Where the job stands: each stage ticked with the day it was reached. */}
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
          {JOB_CARD_STAGES.map(({ key }) => {
            const on = stages[key];

            return (
              <div key={key} className="space-y-1">
                <label
                  className={clsx(
                    'flex cursor-pointer items-center gap-2 rounded-md px-2.5 py-1.5 text-sm font-medium transition',
                    on !== null ? STAGE_COLOUR[key] : 'bg-surface-3 text-ink-muted',
                  )}
                >
                  <input
                    type="checkbox"
                    checked={on !== null}
                    disabled={card?.summary.status === JobCardStatus.cancelled}
                    onChange={(event) =>
                      setStages({
                        ...stages,
                        [key]: event.target.checked ? today() : null,
                      })
                    }
                  />
                  {key === 'dead' ? t('serviceSales.isDead') : t(`service.stage.${key}`)}
                </label>
                <input
                  type="date"
                  aria-label={`${t(`service.stage.${key}`)} · ${t('service.date')}`}
                  value={on ?? ''}
                  disabled={on === null}
                  onChange={(event) =>
                    setStages({ ...stages, [key]: event.target.value || null })
                  }
                  className="field-input-sm w-full"
                />
              </div>
            );
          })}
        </div>

        {/* The bill: parts and labour beside the additional ledgers and the total. */}
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
          <div className="space-y-2 rounded-xl border border-line p-3 lg:col-span-8">
            <div className="flex flex-wrap items-end justify-between gap-2">
              <span className="text-xs font-bold tracking-wider text-ink uppercase">
                {t('service.partsAndLabour')}
              </span>
              {!locked && (
                <label className="flex items-center gap-2">
                  <span className="text-xs text-ink-muted">
                    {t('serviceSales.barcode')}
                  </span>
                  <span className="relative">
                    <input
                      value={barcode}
                      onChange={(event) => setBarcode(event.target.value)}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter') {
                          event.preventDefault();
                          void scan(barcode);
                        }
                      }}
                      aria-label={t('serviceSales.barcode')}
                      className="field-input-sm w-48 pe-8 font-mono"
                    />
                    <IconBarcode className="pointer-events-none absolute inset-y-0 end-2 my-auto size-4 text-ink-subtle" />
                  </span>
                </label>
              )}
            </div>

            <div className="table-wrap">
              <table className="table min-w-[44rem] text-sm">
                <thead>
                  <tr>
                    <th className="text-start">{t('serviceSales.product')}</th>
                    <th className="text-start">{t('service.godown')}</th>
                    <th className="text-start">{t('serviceSales.code')}</th>
                    <th className="w-24 text-end">{t('serviceSales.qty')}</th>
                    <th className="w-24 text-end">{t('service.rate')}</th>
                    <th className="text-start">{t('serviceSales.uom')}</th>
                    <th className="w-24 text-end">{t('service.total')}</th>
                    <th className="w-8" />
                  </tr>
                </thead>
                <tbody>
                  {lines.map((line) => {
                    const product = productById.get(line.productId);
                    const amount = Number(line.quantity) * Number(line.rate);

                    return (
                      <tr key={line.key}>
                        <td className="min-w-56 py-1.5">
                          <SearchSelect
                            value={line.productId}
                            onChange={(productId) => chooseProduct(line.key, productId)}
                            options={productOptions}
                            disabled={locked}
                            size="sm"
                            label={t('serviceSales.product')}
                          />
                        </td>
                        <td className="py-1.5 text-ink-muted">
                          {product ? godownName : ''}
                        </td>
                        <td className="py-1.5 font-mono text-xs">
                          {product?.code ?? ''}
                        </td>
                        <td className="py-1.5">
                          <input
                            type="number"
                            min="0"
                            inputMode="decimal"
                            aria-label={t('serviceSales.qty')}
                            value={line.quantity}
                            disabled={locked}
                            onChange={(event) =>
                              setLine(line.key, { quantity: event.target.value })
                            }
                            className="field-input-sm w-full text-end font-mono tabular-nums"
                          />
                        </td>
                        <td className="py-1.5">
                          <input
                            type="number"
                            min="0"
                            inputMode="decimal"
                            aria-label={t('service.rate')}
                            value={line.rate}
                            disabled={locked}
                            onChange={(event) =>
                              setLine(line.key, { rate: event.target.value })
                            }
                            className="field-input-sm w-full text-end font-mono tabular-nums"
                          />
                        </td>
                        <td className="py-1.5 text-ink-muted">
                          {product?.stockUnitCode ?? ''}
                        </td>
                        <td className="cell-numeric py-1.5">
                          {Number.isFinite(amount) && amount > 0 ? money(amount) : ''}
                        </td>
                        <td className="py-1.5 text-end">
                          {!locked && (
                            <button
                              type="button"
                              aria-label={t('sales.removeLine')}
                              onClick={() =>
                                setLines((current) =>
                                  current.length > 1
                                    ? current.filter((other) => other.key !== line.key)
                                    : [blankLine()],
                                )
                              }
                              className="rounded p-1 text-ink-subtle hover:text-red-600"
                            >
                              <IconClose className="size-3.5" />
                            </button>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
                <tfoot>
                  <tr>
                    <td colSpan={3} />
                    <td className="cell-numeric">
                      {lines.reduce((sum, line) => sum + (Number(line.quantity) || 0), 0)}
                    </td>
                    <td colSpan={2} />
                    <td className="cell-numeric">{money(totals.goods)}</td>
                    <td />
                  </tr>
                </tfoot>
              </table>
            </div>

            {!locked && (
              <button
                type="button"
                onClick={() => setLines((current) => [...current, blankLine()])}
                className="btn-secondary btn-sm flex items-center gap-1"
              >
                <IconPlus className="size-3.5" />
                {t('serviceSales.addLine')}
              </button>
            )}
          </div>

          <div className="space-y-2 rounded-xl border border-line bg-surface-2/60 p-3 text-sm lg:col-span-4">
            <div className="flex justify-between text-ink-muted">
              <span>{t('service.total')}</span>
              <span className="font-mono tabular-nums">{money(totals.goods)}</span>
            </div>
            {totals.tax > 0 && (
              <div className="flex justify-between text-ink-muted">
                <span>{t('serviceSales.tax')}</span>
                <span className="font-mono tabular-nums">{money(totals.tax)}</span>
              </div>
            )}

            {totals.shown.map((row) => (
              <label key={row.ledgerId} className="flex items-center gap-2">
                <span className="min-w-0 flex-1 truncate">{row.label}</span>
                <span
                  className={clsx(
                    'text-[11px] font-medium',
                    row.adds ? 'text-emerald-700 dark:text-emerald-400' : 'text-red-600',
                  )}
                >
                  {row.adds ? '+' : '−'}
                </span>
                <input
                  type="number"
                  min="0"
                  inputMode="decimal"
                  placeholder="0.00"
                  aria-label={row.label}
                  value={
                    charges.find((charge) => charge.ledgerId === row.ledgerId)?.amount ??
                    ''
                  }
                  disabled={locked}
                  onChange={(event) => {
                    const amount = event.target.value;
                    setCharges((current) =>
                      current.some((charge) => charge.ledgerId === row.ledgerId)
                        ? current.map((charge) =>
                            charge.ledgerId === row.ledgerId
                              ? { ...charge, amount }
                              : charge,
                          )
                        : [...current, { ledgerId: row.ledgerId, amount }],
                    );
                  }}
                  className="field-input-sm w-24 text-end font-mono tabular-nums"
                />
              </label>
            ))}
            {chargeSetup !== undefined && totals.shown.length === 0 && (
              <p className="text-xs text-ink-muted">{t('documents.noChargesSetUp')}</p>
            )}

            <div className="flex items-center justify-between border-t border-line pt-2 text-base font-bold text-ink">
              <span>{t('serviceSales.gTotal')}</span>
              <span className="font-mono text-brand-700 tabular-nums dark:text-brand-300">
                {money(totals.grand)}
              </span>
            </div>
          </div>
        </div>

        <div className="form-actions flex-wrap">
          <button
            type="button"
            className="btn-secondary"
            onClick={() => void navigate(LIST)}
          >
            {t('common.close')}
          </button>
          {can(card ? 'service:job-card:edit' : 'service:job-card:create') &&
            card?.summary.status !== JobCardStatus.cancelled && (
              <button
                type="button"
                disabled={busy}
                className={billed ? 'btn-primary' : 'btn-secondary'}
                onClick={() => void save(false)}
              >
                {busy && <Spinner />}
                {t('service.save')}
              </button>
            )}
          {!locked && can('sales:invoice:create') && (
            <button
              type="button"
              disabled={busy}
              className="btn-primary"
              onClick={() => void save(true)}
            >
              {busy && <Spinner />}
              {t('serviceSales.saveAndBill')}
            </button>
          )}
        </div>
      </div>
    </section>
  );
}
