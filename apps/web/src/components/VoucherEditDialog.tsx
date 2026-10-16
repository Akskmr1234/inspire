import { useMemo, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Modal, ModalButton } from '@/components/Modal';
import { Spinner } from '@/components/ReportFrame';
import { SearchSelect } from '@/components/SearchSelect';
import { IconClose } from '@/components/icons';
import { request, type ApiError } from '@/lib/api';
import { listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { moneyAlways } from '@/lib/money';
import { printableVoucher, type VoucherForPrint } from '@/lib/printing';
import { PrintCenter } from '@/components/print/PrintCenter';

const DEBIT = 1;
const CREDIT = 2;

/** A voucher as the server reads it back. */
interface VoucherDetail {
  readonly voucherId: string;
  readonly number: string;
  readonly type: number;
  readonly status: number;
  readonly date: string;
  readonly currency: string;
  readonly baseCurrency: string;
  readonly exchangeRate: number;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly paymentMode: string | null;
  readonly lines: readonly {
    readonly lineNumber: number;
    readonly ledgerId: string;
    readonly side: number;
    readonly debitAmount: number;
    readonly creditAmount: number;
    readonly narration: string | null;
  }[];
}

interface EditLine {
  readonly key: string;
  ledgerId: string;
  side: number;
  amount: string;
  narration: string;
}

const POSTED = 2;

function parse(raw: string): number {
  const value = Number(raw);

  return Number.isFinite(value) && value > 0 ? value : 0;
}

/**
 * Corrects a receipt, payment, journal or contra, keeping its number.
 *
 * One screen for every type, as the double entry it is: the lines with their ledgers and
 * sides. A posted voucher is reopened, rewritten and posted again by the server in one
 * step; what it settled is given back first, so a corrected receipt settles afresh. A
 * journal an invoice wrote is refused by the server by name, because it is the invoice's.
 */
export function VoucherEditDialog({
  voucherId,
  onClose,
  onSaved,
}: {
  readonly voucherId: string;
  readonly onClose: () => void;
  readonly onSaved: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const voucher = useQuery<VoucherDetail, ApiError>({
    queryKey: ['voucher', voucherId],
    queryFn: () => request<VoucherDetail>(`/accounting/vouchers/${voucherId}`),
  });

  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers', 'all'],
    queryFn: () => listLedgers(false),
  });

  return (
    <Modal
      title={
        voucher.data ? t('vouchers.editTitle', { number: voucher.data.number }) : '…'
      }
      size="form"
      onClose={onClose}
    >
      {(voucher.isPending || ledgers.isPending) && <Spinner />}
      {voucher.isError && (
        <p className="alert-error">{voucher.error.detail || voucher.error.code}</p>
      )}
      {voucher.data && ledgers.data && (
        <VoucherForm
          voucher={voucher.data}
          ledgers={ledgers.data}
          onClose={onClose}
          onSaved={onSaved}
        />
      )}
    </Modal>
  );
}

function VoucherForm({
  voucher,
  ledgers,
  onClose,
  onSaved,
}: {
  readonly voucher: VoucherDetail;
  readonly ledgers: readonly LedgerSummary[];
  readonly onClose: () => void;
  readonly onSaved: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const [date, setDate] = useState(voucher.date);
  const [reference, setReference] = useState(voucher.referenceNumber ?? '');
  const [narration, setNarration] = useState(voucher.narration ?? '');
  const [reason, setReason] = useState('');
  const [lines, setLines] = useState<readonly EditLine[]>(() =>
    voucher.lines.map((line) => ({
      key: crypto.randomUUID(),
      ledgerId: line.ledgerId,
      side: line.side,
      amount: String(line.side === DEBIT ? line.debitAmount : line.creditAmount),
      narration: line.narration ?? '',
    })),
  );

  const options = useMemo(
    () =>
      ledgers.map((ledger) => ({
        value: ledger.ledgerId,
        label: `${ledger.code} — ${ledger.name}`,
      })),
    [ledgers],
  );

  const totals = useMemo(() => {
    let debit = 0;
    let credit = 0;

    for (const line of lines) {
      if (line.side === DEBIT) debit += parse(line.amount);
      else credit += parse(line.amount);
    }

    return { debit, credit, difference: Math.round((debit - credit) * 100) / 100 };
  }, [lines]);

  const usable = lines.filter((line) => line.ledgerId !== '' && parse(line.amount) > 0);
  const canSave = usable.length >= 2 && totals.difference === 0 && totals.debit > 0;

  const update = (key: string, patch: Partial<EditLine>): void =>
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );

  const save = useMutation<{ readonly number: string }, ApiError>({
    mutationFn: () =>
      request<{ readonly number: string }>(`/accounting/vouchers/${voucher.voucherId}`, {
        method: 'PUT',
        body: {
          date,
          currencyCode: voucher.currency,
          exchangeRate: voucher.exchangeRate,
          referenceNumber: reference.trim() || null,
          narration: narration.trim() || null,
          paymentMode: voucher.paymentMode,
          postImmediately: true,
          reason: reason.trim() || null,
          lines: usable.map((line) => ({
            ledgerId: line.ledgerId,
            side: line.side,
            amount: parse(line.amount),
            narration: line.narration.trim() || null,
          })),
        },
      }),
    onSuccess: (saved) => onSaved(t('vouchers.updatedNotice', { number: saved.number })),
  });

  return (
    <>
      {voucher.status === POSTED && (
        <p className="alert-warn text-xs">{t('vouchers.editPostedHint')}</p>
      )}

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <label className="field">
          <span className="field-label">{t('vouchers.date')}</span>
          <input
            type="date"
            value={date}
            onChange={(event) => setDate(event.target.value)}
            className="field-input"
          />
        </label>
        <label className="field">
          <span className="field-label">{t('vouchers.reference')}</span>
          <input
            value={reference}
            onChange={(event) => setReference(event.target.value)}
            maxLength={50}
            className="field-input"
          />
        </label>
        <label className="field">
          <span className="field-label">{t('vouchers.narration')}</span>
          <input
            value={narration}
            onChange={(event) => setNarration(event.target.value)}
            className="field-input"
          />
        </label>
      </div>

      <div className="table-wrap">
        <table className="table min-w-[40rem] text-sm">
          <thead>
            <tr>
              <th className="text-start">{t('vouchers.ledger')}</th>
              <th className="text-start">{t('vouchers.side')}</th>
              <th className="text-end">{t('vouchers.amount')}</th>
              <th className="text-start">{t('vouchers.lineNarration')}</th>
              <th className="w-8" />
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.key}>
                <td className="min-w-56">
                  <SearchSelect
                    value={line.ledgerId}
                    onChange={(value) => update(line.key, { ledgerId: value })}
                    options={options}
                    label={t('vouchers.ledger')}
                    size="sm"
                  />
                </td>
                <td>
                  <select
                    value={line.side}
                    onChange={(event) =>
                      update(line.key, { side: Number(event.target.value) })
                    }
                    aria-label={t('vouchers.side')}
                    className="field-input-sm"
                  >
                    <option value={DEBIT}>Dr</option>
                    <option value={CREDIT}>Cr</option>
                  </select>
                </td>
                <td>
                  <input
                    type="number"
                    min={0}
                    step="0.01"
                    value={line.amount}
                    onChange={(event) => update(line.key, { amount: event.target.value })}
                    aria-label={t('vouchers.amount')}
                    className="field-input-sm w-32 text-end tabular-nums"
                  />
                </td>
                <td>
                  <input
                    value={line.narration}
                    onChange={(event) =>
                      update(line.key, { narration: event.target.value })
                    }
                    aria-label={t('vouchers.lineNarration')}
                    className="field-input-sm"
                  />
                </td>
                <td>
                  <button
                    type="button"
                    onClick={() =>
                      setLines((current) => current.filter((l) => l.key !== line.key))
                    }
                    aria-label={t('vouchers.removeLine')}
                    className="btn-ghost btn-xs"
                  >
                    <IconClose className="size-3.5" />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
        <button
          type="button"
          onClick={() =>
            setLines((current) => [
              ...current,
              {
                key: crypto.randomUUID(),
                ledgerId: '',
                side: DEBIT,
                amount: '',
                narration: '',
              },
            ])
          }
          className="btn-secondary btn-sm"
        >
          {t('vouchers.addRow')}
        </button>
        <span className="tabular-nums">
          Dr {moneyAlways(totals.debit)} · Cr {moneyAlways(totals.credit)}{' '}
          {totals.difference === 0 ? (
            <span className="font-semibold text-emerald-700 dark:text-emerald-400">
              {t('vouchers.balanced')}
            </span>
          ) : (
            <span className="font-semibold text-red-600">
              {totals.difference > 0
                ? t('vouchers.differenceDebit', {
                    amount: moneyAlways(totals.difference),
                  })
                : t('vouchers.differenceCredit', {
                    amount: moneyAlways(-totals.difference),
                  })}
            </span>
          )}
        </span>
      </div>

      {voucher.status === POSTED && (
        <label className="field">
          <span className="field-label">{t('vouchers.editReason')}</span>
          <input
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            maxLength={400}
            className="field-input"
          />
        </label>
      )}

      {save.isError && (
        <p className="alert-error">{save.error.detail || save.error.code}</p>
      )}

      <div className="flex justify-end gap-2 border-t border-line pt-3">
        <ModalButton onClick={onClose}>{t('common.cancel')}</ModalButton>
        <ModalButton
          primary
          disabled={!canSave || save.isPending}
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.saving') : t('common.save')}
        </ModalButton>
      </div>
    </>
  );
}

/** Cancels a voucher, after asking why. */
export function VoucherCancelDialog({
  voucherId,
  number,
  onClose,
  onCancelled,
}: {
  readonly voucherId: string;
  readonly number: string;
  readonly onClose: () => void;
  readonly onCancelled: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [reason, setReason] = useState('');

  const cancel = useMutation<void, ApiError>({
    mutationFn: () =>
      request<void>(`/accounting/vouchers/${voucherId}/cancel`, {
        method: 'POST',
        body: { reason: reason.trim() },
      }),
    onSuccess: () => onCancelled(t('vouchers.cancelledNotice', { number })),
  });

  return (
    <Modal title={t('vouchers.cancelTitle', { number })} size="form" onClose={onClose}>
      <p className="text-sm text-ink-muted">{t('vouchers.cancelHint')}</p>
      <label className="field">
        <span className="field-label">{t('vouchers.cancelReason')}</span>
        <input
          autoFocus
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          maxLength={500}
          className="field-input"
        />
      </label>
      {cancel.isError && (
        <p className="alert-error">{cancel.error.detail || cancel.error.code}</p>
      )}
      <div className="flex justify-end gap-2 border-t border-line pt-3">
        <ModalButton onClick={onClose}>{t('common.close')}</ModalButton>
        <ModalButton
          primary
          disabled={reason.trim() === '' || cancel.isPending}
          onClick={() => cancel.mutate()}
        >
          {t('vouchers.cancelVoucher')}
        </ModalButton>
      </div>
    </Modal>
  );
}

/** A receipt, payment or journal on paper. */
export function VoucherPrintDialog({
  voucherId,
  onClose,
}: {
  readonly voucherId: string;
  readonly onClose: () => void;
}): React.JSX.Element {
  const voucher = useQuery<VoucherForPrint, ApiError>({
    queryKey: ['voucher', voucherId],
    queryFn: () => request<VoucherForPrint>(`/accounting/vouchers/${voucherId}`),
  });

  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers', 'all'],
    queryFn: () => listLedgers(false),
  });

  return (
    <Modal title={voucher.data?.number ?? '…'} size="fullscreen" onClose={onClose}>
      {(voucher.isPending || ledgers.isPending) && <Spinner />}
      {voucher.isError && (
        <p className="alert-error">{voucher.error.detail || voucher.error.code}</p>
      )}
      {voucher.data && ledgers.data && (
        <PrintCenter
          document={printableVoucher(voucher.data, ledgers.data)}
          onDone={onClose}
        />
      )}
    </Modal>
  );
}
