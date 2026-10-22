import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { PageHeading } from '@/components/PageHeading';
import { RowAction } from '@/components/MasterFrame';
import { StatusBadge, type StatusTone } from '@/components/StatusBadge';
import { VoucherCancelDialog, VoucherPrintDialog } from '@/components/VoucherEditDialog';
import type { ApiError } from '@/lib/api';
import { moneyAlways as money } from '@/lib/money';
import {
  KIND_PATH,
  listVouchers,
  TYPE_NAME,
  VoucherStatus,
  type VoucherKind,
  type VoucherList,
  type VoucherListLine,
} from '@/lib/vouchers';
import { useSession } from '@/stores/session';

const STATUS_NAME: Record<number, string> = { 1: 'Draft', 2: 'Posted', 3: 'Cancelled' };
const STATUS_TONES: Record<number, StatusTone> = { 1: 'warn', 2: 'success', 3: 'danger' };

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function startOfMonth(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`;
}

/** The words each kind's list uses, by translation key. */
const WORDS: Record<
  VoucherKind,
  { title: string; add: string; party: string; account: string; none: string }
> = {
  receipt: {
    title: 'voucherList.receipts',
    add: 'voucherList.newReceipt',
    party: 'voucherList.receivedFrom',
    account: 'voucherList.receivedInto',
    none: 'voucherList.noReceipts',
  },
  payment: {
    title: 'voucherList.payments',
    add: 'voucherList.newPayment',
    party: 'voucherList.paidTo',
    account: 'voucherList.paidFrom',
    none: 'voucherList.noPayments',
  },
  journal: {
    title: 'voucherList.journals',
    add: 'voucherList.newJournal',
    party: 'voucherList.debited',
    account: 'voucherList.credited',
    none: 'voucherList.noJournals',
  },
};

/**
 * Receipts, payments or journals: the list a menu entry opens on, with New above it.
 *
 * Each row opens back on the entry screen it was made on, so a correction is made
 * where the voucher was entered rather than on a second form that looks different.
 */
function VoucherListPage({ kind }: { readonly kind: VoucherKind }): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const can = useSession((s) => s.can);
  const [params, setParams] = useSearchParams();
  const words = WORDS[kind];

  const [from, setFrom] = useState(startOfMonth());
  const [to, setTo] = useState(today());
  const [status, setStatus] = useState('');
  const [printingId, setPrintingId] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState<VoucherListLine | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // An entry screen sends somebody back here with what it saved.
  const saved = params.get('saved');
  const held = params.get('held') === '1';

  const list = useQuery<VoucherList, ApiError>({
    queryKey: ['voucher-list', kind, from, to, status],
    queryFn: () => listVouchers(kind, from, to, status),
  });

  // Who the money moved with sits on the opposite side for receipts and payments.
  const partyOf = (row: VoucherListLine): string =>
    (kind === 'receipt' ? row.creditLedgers : row.debitLedgers) ?? '';
  const accountOf = (row: VoucherListLine): string =>
    (kind === 'receipt' ? row.debitLedgers : row.creditLedgers) ?? '';

  const columns: readonly GridColumn<VoucherListLine>[] = [
    { key: 'date', header: t('reports.date'), value: (row) => row.date },
    {
      key: 'number',
      header: t('reports.voucherNo'),
      value: (row) => row.voucherNumber,
      render: (row) => (
        <button
          type="button"
          className="font-medium text-brand-700 hover:underline dark:text-brand-300"
          onClick={() => void navigate(`${KIND_PATH[kind]}/${row.voucherId}`)}
        >
          {row.voucherNumber}
        </button>
      ),
    },
    {
      key: 'type',
      header: t('reports.voucherType'),
      value: (row) => t(`voucherTypes.${TYPE_NAME[row.type] ?? 'Journal'}`),
    },
    { key: 'party', header: t(words.party), value: partyOf, wide: true },
    { key: 'account', header: t(words.account), value: accountOf },
    {
      key: 'amount',
      header: t('reports.amount'),
      numeric: true,
      value: (row) => row.documentAmount,
      render: (row) => money(row.documentAmount),
    },
    {
      key: 'status',
      header: t('reports.status'),
      value: (row) => t(`voucherStatus.${STATUS_NAME[row.status] ?? 'Posted'}`),
      render: (row) => (
        <StatusBadge
          tone={STATUS_TONES[row.status] ?? 'neutral'}
          label={t(`voucherStatus.${STATUS_NAME[row.status] ?? 'Posted'}`)}
          struck={row.status === VoucherStatus.cancelled}
        />
      ),
    },
    {
      key: 'reference',
      header: t('reports.reference'),
      value: (row) => row.referenceNumber ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'narration',
      header: t('vouchers.narration'),
      value: (row) => row.narration ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <div className="flex flex-nowrap justify-end gap-1">
          {row.status !== VoucherStatus.cancelled && can('accounting:voucher:edit') && (
            <RowAction
              label={t('common.edit')}
              disabled={false}
              onClick={() => void navigate(`${KIND_PATH[kind]}/${row.voucherId}`)}
            />
          )}
          <RowAction
            label={t('printing.print')}
            disabled={false}
            onClick={() => setPrintingId(row.voucherId)}
          />
          {row.status !== VoucherStatus.cancelled && can('accounting:voucher:delete') && (
            <RowAction
              label={t('common.cancel')}
              disabled={false}
              tone="danger"
              onClick={() => setCancelling(row)}
            />
          )}
        </div>
      ),
    },
  ];

  const dismissSaved = (): void => {
    const next = new URLSearchParams(params);
    next.delete('saved');
    next.delete('held');
    setParams(next, { replace: true });
  };

  return (
    <section className="page">
      <PageHeading title={t(words.title)} />

      {saved !== null && (
        <p className="alert-success flex flex-wrap items-center gap-x-3 gap-y-1">
          <span>
            {held
              ? t('approvals.heldNotice', { number: saved })
              : t('voucherList.savedNotice', { number: saved })}
          </span>
          <button
            type="button"
            onClick={dismissSaved}
            className="ms-auto text-xs font-medium underline underline-offset-2"
          >
            {t('common.dismiss')}
          </button>
        </p>
      )}

      {notice !== null && (
        <p className="alert-success flex items-center gap-3">
          <span>{notice}</span>
          <button
            type="button"
            onClick={() => setNotice(null)}
            className="ms-auto text-xs font-medium underline underline-offset-2"
          >
            {t('common.dismiss')}
          </button>
        </p>
      )}

      {list.isError && (
        <p className="alert-error">{list.error.detail || list.error.code}</p>
      )}

      <DataGrid
        gridKey={`voucher-list-${kind}`}
        rows={list.data?.vouchers ?? []}
        columns={columns}
        rowKey={(row) => row.voucherId}
        emptyMessage={list.isPending ? t('common.loading') : t(words.none)}
        filters={
          <div className="flex flex-wrap items-center gap-2">
            <input
              type="date"
              aria-label={t('reports.from')}
              value={from}
              onChange={(event) => setFrom(event.target.value)}
              className="field-input-sm w-auto"
            />
            <span className="text-xs text-ink-muted">–</span>
            <input
              type="date"
              aria-label={t('reports.to')}
              value={to}
              onChange={(event) => setTo(event.target.value)}
              className="field-input-sm w-auto"
            />
            <select
              aria-label={t('reports.status')}
              value={status}
              onChange={(event) => setStatus(event.target.value)}
              className="field-input-sm w-auto"
            >
              <option value="">{t('reports.allStatuses')}</option>
              {Object.entries(STATUS_NAME).map(([value, name]) => (
                <option key={value} value={value}>
                  {t(`voucherStatus.${name}`)}
                </option>
              ))}
            </select>
          </div>
        }
        actions={
          can('accounting:voucher:create') && (
            <button
              type="button"
              className="btn-primary btn-sm"
              onClick={() => void navigate(`${KIND_PATH[kind]}/new`)}
            >
              {t(words.add)}
            </button>
          )
        }
      />

      {list.data && list.data.vouchers.length > 0 && (
        <p className="text-end text-sm text-ink-muted">
          {t('vouchers.total')}:{' '}
          <span className="font-mono font-semibold text-ink tabular-nums">
            {money(list.data.totalBaseAmount)} {list.data.currency}
          </span>
        </p>
      )}

      {printingId && (
        <VoucherPrintDialog voucherId={printingId} onClose={() => setPrintingId(null)} />
      )}

      {cancelling && (
        <VoucherCancelDialog
          voucherId={cancelling.voucherId}
          number={cancelling.voucherNumber}
          onClose={() => setCancelling(null)}
          onCancelled={(message) => {
            setCancelling(null);
            setNotice(message);
            void queryClient.invalidateQueries({ queryKey: ['voucher-list'] });
            void queryClient.invalidateQueries({ queryKey: ['voucher-report'] });
          }}
        />
      )}
    </section>
  );
}

export function ReceiptListPage(): React.JSX.Element {
  return <VoucherListPage kind="receipt" />;
}

export function PaymentListPage(): React.JSX.Element {
  return <VoucherListPage kind="payment" />;
}

export function JournalListPage(): React.JSX.Element {
  return <VoucherListPage kind="journal" />;
}
