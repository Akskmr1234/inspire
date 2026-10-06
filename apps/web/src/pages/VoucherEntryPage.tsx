import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { PageHeading } from '@/components/PageHeading';
import { Spinner } from '@/components/ReportFrame';
import { DateField, Field, SelectField, TextField } from '@/components/Form';
import { SearchSelect, type SelectOption } from '@/components/SearchSelect';
import { IconClose, IconPlus } from '@/components/icons';
import { ApiError, request } from '@/lib/api';
import {
  isMoneyAccount,
  isParty,
  LedgerKind,
  listLedgers,
  type LedgerSummary,
} from '@/lib/ledgers';
import { collect, numeric, required, useValidation } from '@/lib/validation';
import { moneyAlways as money } from '@/lib/money';
import { isKnownPaymentMode, PAYMENT_MODES } from '@/lib/paymentModes';
import { useSettings } from '@/stores/settings';

interface CreateVoucherResponse {
  readonly voucherId: string;
  readonly number: string;
  readonly status: number;
  readonly totalDebit: number;
}

/**
 * Where a posted voucher sends somebody: the list it now appears on.
 *
 * Every entry screen here used to stay put and say "posted" in a green line, which
 * reads as an unsaved form to anybody who looks away and back — the fields are empty
 * again and the banner scrolls off. Landing on the list that now holds the document
 * is the confirmation, and the list carries the way back to enter another.
 */
function listedAt(type: number, number: string): string {
  const params = new URLSearchParams({ type: String(type), posted: number });

  return `/accounting/voucher-report?${params.toString()}`;
}

/** Debit is 1 and Credit is 2, matching the API's EntrySide enum. */
const DEBIT = 1;
const CREDIT = 2;

/** Voucher types, matching the API's VoucherType enum. */
export const VoucherType = {
  cashReceipt: 1,
  bankReceipt: 2,
  cashPayment: 3,
  bankPayment: 4,
  journal: 5,
  contra: 6,
  openingBalance: 7,
} as const;

interface DraftLine {
  readonly key: string;
  ledgerId: string;
  side: typeof DEBIT | typeof CREDIT;
  amount: string;
  narration: string;
}

function emptyLine(): DraftLine {
  return {
    key: crypto.randomUUID(),
    ledgerId: '',
    side: DEBIT,
    amount: '',
    narration: '',
  };
}

/** Parses a typed amount, treating anything unparseable as zero. */
function parseAmount(raw: string): number {
  const value = Number.parseFloat(raw);
  return Number.isFinite(value) && value > 0 ? value : 0;
}

/** One ledger as a picker row: the code and name, with its group underneath. */
function ledgerOption(ledger: LedgerSummary): SelectOption {
  return {
    value: ledger.ledgerId,
    label: `${ledger.code} · ${ledger.name}`,
    detail: ledger.groupName,
    keywords: ledger.groupCode,
  };
}

/** Loads the chart of accounts once and shares it across the money screens. */
function useLedgers(): ReturnType<typeof useQuery<readonly LedgerSummary[], ApiError>> {
  return useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => listLedgers(true),
    // The chart of accounts changes rarely; refetching it on every visit to the
    // entry screen would be wasted work.
    staleTime: 5 * 60 * 1000,
  });
}

/**
 * Voucher entry.
 *
 * Mirrors the reference application's journal screen: a Debit/Credit selector per
 * line, separate debit and credit amount columns, a running total per column, and
 * the standing rule that every debit needs a corresponding credit.
 *
 * The running difference is the important part of the design. It is shown as the
 * user types, so a transposed digit is caught at the keyboard rather than by the
 * server after Save. The server still enforces the rule - this is a courtesy, not
 * the guarantee.
 *
 * The party field above the lines is the other. A voucher is very often against a
 * customer or a supplier — a receipt, a payment, a contra entry correcting one — and
 * naming them here fills the line that would otherwise have to be found in a
 * four-hundred-row picker. It is a shortcut into the lines, not a field of its own:
 * what is saved is still the lines, which is what the books hold.
 */
export function VoucherEntryPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  const [type, setType] = useState<number>(VoucherType.journal);
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [narration, setNarration] = useState('');
  const [referenceNumber, setReferenceNumber] = useState('');
  const [partyId, setPartyId] = useState('');
  const [lines, setLines] = useState<readonly DraftLine[]>([emptyLine(), emptyLine()]);

  const ledgers = useLedgers();
  const all = useMemo(() => ledgers.data ?? [], [ledgers.data]);

  const parties = useMemo(() => all.filter(isParty), [all]);
  const ledgerOptions = useMemo(() => all.map(ledgerOption), [all]);

  const totals = useMemo(() => {
    let debit = 0;
    let credit = 0;

    for (const line of lines) {
      const amount = parseAmount(line.amount);

      if (line.side === DEBIT) {
        debit += amount;
      } else {
        credit += amount;
      }
    }

    // Rounded to two places before comparing. Summing typed decimals in binary
    // floating point can leave a residue like 1e-13, which would report a
    // perfectly balanced voucher as unbalanced.
    const difference = Math.round((debit - credit) * 100) / 100;

    /*
      Three states, not two.

      A voucher nobody has typed in yet has a difference of zero and is not
      balanced, and the badge treated that as the unbalanced case — so an empty
      screen opened by announcing "Difference 0.00 (credit heavy)", which is not
      true, not actionable, and the first thing anybody sees on the screen. Empty
      is its own state and says so.
    */
    return {
      debit,
      credit,
      difference,
      isEmpty: debit === 0 && credit === 0,
      isBalanced: difference === 0 && debit > 0,
    };
  }, [lines]);

  const canSubmit =
    totals.isBalanced &&
    lines.filter((l) => l.ledgerId !== '' && parseAmount(l.amount) > 0).length >= 2;

  const update = (key: string, patch: Partial<DraftLine>): void =>
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );

  /*
    Naming a party puts them on the first line that has no ledger yet, or opens one
    if every line is spoken for. Choosing them a second time moves the line rather
    than adding another, so correcting a mis-picked customer does not leave the
    first one behind on a line nobody notices.
  */
  const chooseParty = (ledgerId: string): void => {
    setPartyId(ledgerId);

    if (ledgerId === '') {
      return;
    }

    setLines((current) => {
      const existing = current.findIndex((line) => line.ledgerId === partyId);

      if (existing >= 0) {
        return current.map((line, index) =>
          index === existing ? { ...line, ledgerId } : line,
        );
      }

      const empty = current.findIndex((line) => line.ledgerId === '');

      if (empty >= 0) {
        return current.map((line, index) =>
          index === empty ? { ...line, ledgerId } : line,
        );
      }

      return [...current, { ...emptyLine(), ledgerId }];
    });
  };

  const post = useMutation<CreateVoucherResponse, ApiError>({
    mutationFn: () =>
      request<CreateVoucherResponse>('/accounting/vouchers', {
        method: 'POST',
        body: {
          type,
          date,
          referenceNumber: referenceNumber || null,
          narration: narration || null,
          postImmediately: true,
          lines: lines
            .filter((l) => l.ledgerId !== '' && parseAmount(l.amount) > 0)
            .map((l) => ({
              ledgerId: l.ledgerId,
              side: l.side,
              amount: parseAmount(l.amount),
              narration: l.narration || null,
            })),
        },
      }),
    onSuccess: (response) => {
      setLines([emptyLine(), emptyLine()]);
      setNarration('');
      setReferenceNumber('');
      setPartyId('');

      // The trial balance is now stale by definition, so it is invalidated rather
      // than left showing a position that predates this posting.
      void queryClient.invalidateQueries({ queryKey: ['trial-balance'] });
      void queryClient.invalidateQueries({ queryKey: ['voucher-report'] });

      navigate(listedAt(type, response.number));
    },
  });

  return (
    <section className="page">
      <PageHeading title={t('vouchers.entryTitle')} />

      {post.isError && (
        <div role="alert" className="alert-error">
          <p className="font-semibold">{post.error.code}</p>
          <p className="mt-0.5 opacity-90">{post.error.detail}</p>
        </div>
      )}

      <div className="panel grid gap-x-4 gap-y-3 sm:grid-cols-2 xl:grid-cols-5">
        <SelectField
          label={t('vouchers.type')}
          required
          value={type}
          onChange={setType}
          options={[
            { value: VoucherType.journal, label: t('voucherTypes.Journal') },
            { value: VoucherType.contra, label: t('voucherTypes.Contra') },
            { value: VoucherType.cashReceipt, label: t('voucherTypes.CashReceipt') },
            { value: VoucherType.bankReceipt, label: t('voucherTypes.BankReceipt') },
            { value: VoucherType.cashPayment, label: t('voucherTypes.CashPayment') },
            { value: VoucherType.bankPayment, label: t('voucherTypes.BankPayment') },
          ]}
        />

        <DateField label={t('vouchers.date')} required value={date} onChange={setDate} />

        {/*
          The customer or supplier the voucher is against.

          Section 12 asks for it on this screen and it was not here: every voucher
          against a party meant finding them among every ledger in the firm, in a
          native select with no search, twice — once for the party and once for the
          account. This narrows to the parties, and what it fills in is an ordinary
          line somebody can still change.
        */}
        <Field label={t('vouchers.party')}>
          <SearchSelect
            value={partyId}
            onChange={chooseParty}
            clearable
            label={t('vouchers.party')}
            placeholder={t('vouchers.noParty')}
            options={parties.map(ledgerOption)}
          />
        </Field>

        <TextField
          label={t('vouchers.reference')}
          value={referenceNumber}
          onChange={setReferenceNumber}
        />

        <TextField
          label={t('vouchers.narration')}
          value={narration}
          onChange={setNarration}
        />
      </div>

      <div className="table-wrap">
        {/*
          A floor width, because five editable controls per row cannot be squeezed
          into a phone's width without every select becoming unreadable. The table
          scrolls sideways instead, which keeps each control at a usable size.
        */}
        <table className="table min-w-[56rem]">
          <thead>
            <tr>
              <th className="text-start">{t('vouchers.side')}</th>
              <th className="text-start">{t('vouchers.ledger')}</th>
              <th className="text-start">{t('vouchers.lineNarration')}</th>
              <th className="text-end">{t('reports.debit')}</th>
              <th className="text-end">{t('reports.credit')}</th>
              <th className="w-10 text-end" />
            </tr>
          </thead>

          <tbody>
            {lines.map((line) => (
              <tr key={line.key}>
                <td className="py-2">
                  <SearchSelect
                    label={t('vouchers.side')}
                    className="w-28"
                    size="sm"
                    value={String(line.side)}
                    onChange={(value) =>
                      update(line.key, {
                        side: Number(value) === CREDIT ? CREDIT : DEBIT,
                      })
                    }
                    options={[
                      { value: String(DEBIT), label: t('reports.debit') },
                      { value: String(CREDIT), label: t('reports.credit') },
                    ]}
                  />
                </td>

                <td className="min-w-56 py-2">
                  {/*
                    A picker that can be typed into, in place of a native select of
                    the whole chart of accounts. The browser's own type-ahead
                    matches the start of an option, which here is the account code —
                    so looking a ledger up by its name, which is what everybody
                    does, was impossible.
                  */}
                  <SearchSelect
                    value={line.ledgerId}
                    onChange={(ledgerId) => update(line.key, { ledgerId })}
                    options={ledgerOptions}
                    disabled={ledgers.isPending}
                    size="sm"
                    label={t('vouchers.ledger')}
                    placeholder={
                      ledgers.isPending ? t('common.loading') : t('common.choose')
                    }
                  />
                </td>

                <td className="py-2">
                  <input
                    aria-label={t('vouchers.lineNarration')}
                    className="field-input-sm min-w-40"
                    value={line.narration}
                    onChange={(e) => update(line.key, { narration: e.target.value })}
                  />
                </td>

                {/*
                  A single amount box appears under whichever column the line's side
                  selects, and the other shows a dash. The domain stores one positive
                  amount plus a side, so offering two editable boxes would invite a
                  row with both filled in - which has no meaning.
                */}
                {/* End-aligned like the caption above and the total below. The
                    box is a fixed width rather than the cell's, so without this it
                    floated at the left of a column whose heading and total were
                    both flush right. */}
                <td className="py-2 text-end">
                  {line.side === DEBIT ? (
                    <input
                      aria-label={t('vouchers.debitAmount')}
                      type="number"
                      inputMode="decimal"
                      step="0.01"
                      min="0"
                      className="field-input-sm text-end font-mono tabular-nums"
                      value={line.amount}
                      onChange={(e) => update(line.key, { amount: e.target.value })}
                    />
                  ) : (
                    <span className="block text-end text-ink-subtle">—</span>
                  )}
                </td>

                <td className="py-2 text-end">
                  {line.side === CREDIT ? (
                    <input
                      aria-label={t('vouchers.creditAmount')}
                      type="number"
                      inputMode="decimal"
                      step="0.01"
                      min="0"
                      className="field-input-sm text-end font-mono tabular-nums"
                      value={line.amount}
                      onChange={(e) => update(line.key, { amount: e.target.value })}
                    />
                  ) : (
                    <span className="block text-end text-ink-subtle">—</span>
                  )}
                </td>

                <td className="px-2 py-2 text-end">
                  <button
                    type="button"
                    aria-label={t('vouchers.removeLine')}
                    disabled={lines.length <= 2}
                    onClick={() =>
                      setLines((current) => current.filter((l) => l.key !== line.key))
                    }
                    className="grid size-7 place-items-center rounded-md text-ink-subtle transition hover:bg-red-50 hover:text-red-600 active:scale-90 disabled:pointer-events-none disabled:opacity-30 dark:hover:bg-red-500/10 dark:hover:text-red-400"
                  >
                    ✕
                  </button>
                </td>
              </tr>
            ))}
          </tbody>

          <tfoot>
            <tr>
              <td colSpan={3}>{t('vouchers.total')}</td>
              <td className="cell-numeric">{money(totals.debit)}</td>
              <td className="cell-numeric">{money(totals.credit)}</td>
              <td />
            </tr>
          </tfoot>
        </table>
      </div>

      <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-center sm:justify-between">
        <button
          type="button"
          onClick={() => setLines((current) => [...current, emptyLine()])}
          className="btn-secondary self-start"
        >
          {t('vouchers.addRow')}
        </button>

        <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-center">
          <span
            className={clsx(
              'inline-flex items-center gap-2 rounded-lg border px-3 py-1.5 text-sm font-medium',
              totals.isEmpty
                ? 'border-line bg-surface-3 text-ink-muted'
                : totals.isBalanced
                  ? 'border-emerald-200/70 bg-emerald-50 text-emerald-800 dark:border-emerald-500/30 dark:bg-emerald-500/12 dark:text-emerald-200'
                  : 'border-amber-200/70 bg-amber-50 text-amber-900 dark:border-amber-500/30 dark:bg-amber-500/12 dark:text-amber-200',
            )}
          >
            <span
              aria-hidden="true"
              className={clsx(
                'size-2 shrink-0 rounded-full',
                totals.isEmpty
                  ? 'bg-ink-subtle'
                  : totals.isBalanced
                    ? 'bg-emerald-500'
                    : 'animate-breathe bg-amber-500',
              )}
            />
            {totals.isEmpty
              ? t('vouchers.nothingEntered')
              : totals.isBalanced
                ? t('vouchers.balanced')
                : t(
                    totals.difference > 0
                      ? 'vouchers.differenceDebit'
                      : 'vouchers.differenceCredit',
                    { amount: money(Math.abs(totals.difference)) },
                  )}
          </span>

          <button
            type="button"
            disabled={!canSubmit || post.isPending}
            onClick={() => post.mutate()}
            className="btn-primary"
          >
            {post.isPending && <Spinner />}
            {post.isPending ? t('vouchers.posting') : t('vouchers.post')}
          </button>
        </div>
      </div>

      <p className="text-xs text-ink-muted">{t('vouchers.entryHint')}</p>
    </section>
  );
}

/** Money going out. */
export function PaymentEntryPage(): React.JSX.Element {
  return <MoneyEntry direction="payment" />;
}

/** Money coming in. */
export function ReceiptEntryPage(): React.JSX.Element {
  return <MoneyEntry direction="receipt" />;
}

/** What the payment and receipt form holds while it is being filled in. */
interface MoneyDraft {
  date: string;
  partyId: string;
  accountId: string;
  amount: string;
  reference: string;
  paymentMode: string;
  narration: string;
}

/**
 * Payments and receipts, each on a screen of its own.
 *
 * They were one entry on the menu and one form — the journal screen with its six
 * voucher types in a dropdown — which is how a cashier taking money over a counter
 * ended up building a two-line double entry and choosing a side per line. The two
 * are the commonest documents in the system and the least like a journal: one party,
 * one cash or bank account, one amount, and the sides are decided by which of the
 * two screens somebody opened.
 *
 * One component behind both, because the difference really is only the direction the
 * money moves in. Two screens that shared nothing would be two places for the
 * cheque handling and the bill settlement to arrive, and one of them would not get
 * it.
 */
export interface PaymentEntryLine {
  readonly id: string;
  paymentType: string;
  accountId: string;
  amount: string;
  reference: string;
}

function MoneyEntry({
  direction,
}: {
  readonly direction: 'payment' | 'receipt';
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  const defaultPaymentMode = useSettings((state) => state.defaultPaymentMode);
  const defaultSalesman = useSettings((state) => state.defaultSalesman);

  const [through, setThrough] = useState<'cash' | 'bank'>('bank');
  const [salesman, setSalesman] = useState<string>(defaultSalesman || 'Primary');
  const [paymentError, setPaymentError] = useState<string | null>(null);

  const [draft, setDraft] = useState<MoneyDraft>(() => ({
    date: new Date().toISOString().slice(0, 10),
    partyId: '',
    accountId: '',
    amount: '',
    reference: '',
    paymentMode: defaultPaymentMode,
    narration: '',
  }));

  const vouchersCount = useQuery<{ readonly lines?: readonly unknown[] }, ApiError>({
    queryKey: ['voucher-report', 'count', direction],
    queryFn: () =>
      request<{ readonly lines?: readonly unknown[] }>('/accounting/voucher-report'),
    staleTime: 60000,
  });

  const count = vouchersCount.data?.lines?.length ?? 0;
  const prefix = direction === 'payment' ? 'PAY' : 'REC';
  const transactionNo = `${prefix}-${String(1001 + count).padStart(4, '0')}`;

  const paymentModeOptions = useMemo((): readonly SelectOption[] => {
    const known = PAYMENT_MODES.map((mode) => ({
      value: mode.value,
      label: t(mode.labelKey),
    }));

    if (draft.paymentMode === '' || isKnownPaymentMode(draft.paymentMode)) {
      return known;
    }

    return [...known, { value: draft.paymentMode, label: draft.paymentMode }];
  }, [draft.paymentMode, t]);

  const ledgers = useLedgers();
  const all = useMemo(() => ledgers.data ?? [], [ledgers.data]);

  const partyOptions = useMemo(() => {
    const parties = all.filter(isParty).map(ledgerOption);
    const others = all
      .filter((ledger) => !isParty(ledger) && !isMoneyAccount(ledger))
      .map(ledgerOption);

    return [...parties, ...others];
  }, [all]);

  const accounts = useMemo(
    () =>
      all.filter((ledger) =>
        through === 'cash'
          ? ledger.kind === LedgerKind.cash
          : ledger.kind === LedgerKind.bank,
      ),
    [all, through],
  );

  const moneyAccountOptions = useMemo(
    () => all.filter(isMoneyAccount).map(ledgerOption),
    [all],
  );

  const [paymentEntries, setPaymentEntries] = useState<readonly PaymentEntryLine[]>([
    {
      id: '1',
      paymentType: defaultPaymentMode || 'Cash',
      accountId: '',
      amount: '',
      reference: '',
    },
  ]);

  useEffect(() => {
    if (paymentEntries.length === 1 && !paymentEntries[0]?.accountId && accounts.length > 0) {
      const defaultAcc = accounts[0]?.ledgerId ?? '';
      setPaymentEntries([{ ...paymentEntries[0]!, accountId: defaultAcc }]);
      setDraft((curr) => ({ ...curr, accountId: defaultAcc }));
    }
  }, [accounts, paymentEntries]);

  const totalPaymentAmount = useMemo(() => {
    return paymentEntries.reduce((sum, entry) => {
      const val = Number(entry.amount);
      return Number.isFinite(val) && val > 0 ? sum + val : sum;
    }, 0);
  }, [paymentEntries]);

  const addPaymentEntry = (): void => {
    const defaultAcc = accounts[0]?.ledgerId ?? moneyAccountOptions[0]?.value ?? '';
    setPaymentEntries((current) => [
      ...current,
      {
        id: Math.random().toString(36).slice(2, 9),
        paymentType: draft.paymentMode || 'Cash',
        accountId: defaultAcc,
        amount: '',
        reference: '',
      },
    ]);
  };

  const updatePaymentEntry = (
    id: string,
    patch: Partial<PaymentEntryLine>,
  ): void => {
    setPaymentEntries((current) => {
      const next = current.map((item) => (item.id === id ? { ...item, ...patch } : item));
      const nextTotal = next.reduce((sum, item) => {
        const val = Number(item.amount);
        return Number.isFinite(val) && val > 0 ? sum + val : sum;
      }, 0);
      setDraft((curr) => ({
        ...curr,
        amount: nextTotal > 0 ? String(nextTotal) : '',
        ...(next.length === 1 && patch.accountId !== undefined
          ? { accountId: patch.accountId }
          : {}),
      }));
      return next;
    });
  };

  const removePaymentEntry = (id: string): void => {
    if (paymentEntries.length <= 1) return;
    setPaymentEntries((current) => {
      const next = current.filter((item) => item.id !== id);
      const nextTotal = next.reduce((sum, item) => {
        const val = Number(item.amount);
        return Number.isFinite(val) && val > 0 ? sum + val : sum;
      }, 0);
      setDraft((curr) => ({
        ...curr,
        amount: nextTotal > 0 ? String(nextTotal) : '',
      }));
      return next;
    });
  };

  const handlePaymentModeChange = (mode: string): void => {
    set('paymentMode', mode);
    if (paymentEntries.length > 0 && paymentEntries[0]) {
      updatePaymentEntry(paymentEntries[0].id, { paymentType: mode });
    }
  };

  const { errors, submit, reset } = useValidation<MoneyDraft>((values) =>
    collect({
      date: required(values.date, t('vouchers.dateRequired')),
      partyId: required(values.partyId, t('vouchers.partyRequired')),
      amount:
        required(values.amount, t('vouchers.amountRequired')) ??
        numeric(values.amount, t('vouchers.amountInvalid'), { min: 0.01 }),
    }),
  );

  const type =
    direction === 'receipt'
      ? through === 'cash'
        ? VoucherType.cashReceipt
        : VoucherType.bankReceipt
      : through === 'cash'
        ? VoucherType.cashPayment
        : VoucherType.bankPayment;

  const post = useMutation<CreateVoucherResponse, ApiError, MoneyDraft>({
    mutationFn: (values) => {
      const finalReference = values.reference.trim() || transactionNo;
      const moneySide = direction === 'receipt' ? DEBIT : CREDIT;
      const partySide = direction === 'receipt' ? CREDIT : DEBIT;

      const activeEntries = paymentEntries.filter(
        (e) => e.accountId !== '' && Number(e.amount) > 0,
      );

      const effectiveTotal = activeEntries.reduce((sum, e) => sum + Number(e.amount), 0);

      const partyLine = {
        ledgerId: values.partyId,
        side: partySide,
        amount: effectiveTotal,
        narration: values.narration || null,
      };

      const moneyLines = activeEntries.map((e) => ({
        ledgerId: e.accountId,
        side: moneySide,
        amount: Number(e.amount),
        narration: e.reference ? `${e.paymentType} - Ref: ${e.reference}` : e.paymentType,
      }));

      const lines =
        direction === 'payment' ? [partyLine, ...moneyLines] : [...moneyLines, partyLine];

      return request<CreateVoucherResponse>('/accounting/vouchers', {
        method: 'POST',
        body: {
          type,
          date: values.date,
          referenceNumber: finalReference,
          narration: values.narration || null,
          paymentMode: values.paymentMode || null,
          postImmediately: true,
          lines,
        },
      });
    },
    onSuccess: (response) => {
      reset();
      setDraft((current) => ({
        ...current,
        partyId: '',
        amount: '',
        reference: '',
        narration: '',
      }));
      setPaymentEntries([
        {
          id: Math.random().toString(36).slice(2, 9),
          paymentType: defaultPaymentMode || 'Cash',
          accountId: accounts[0]?.ledgerId ?? '',
          amount: '',
          reference: '',
        },
      ]);
      setPaymentError(null);

      void queryClient.invalidateQueries({ queryKey: ['trial-balance'] });
      void queryClient.invalidateQueries({ queryKey: ['voucher-report'] });

      navigate(listedAt(type, response.number));
    },
  });

  const set = <TField extends keyof MoneyDraft>(
    field: TField,
    value: MoneyDraft[TField],
  ): void => setDraft((current) => ({ ...current, [field]: value }));

  return (
    <section className="page">
      <PageHeading
        title={
          direction === 'receipt'
            ? t('vouchers.receiptTitle')
            : t('vouchers.paymentTitle')
        }
        subtitle={
          direction === 'receipt' ? t('vouchers.receiptHint') : t('vouchers.paymentHint')
        }
      />

      {post.isError && (
        <div role="alert" className="alert-error">
          <p className="font-semibold">{post.error.code}</p>
          <p className="mt-0.5 opacity-90">{post.error.detail}</p>
        </div>
      )}

      <form
        className="card card-body space-y-5"
        onSubmit={(event) => {
          event.preventDefault();
          setPaymentError(null);

          if (!draft.partyId) {
            setPaymentError(t('vouchers.partyRequired'));
            return;
          }

          const activeEntries = paymentEntries.filter((e) => Number(e.amount) > 0);
          if (activeEntries.length === 0 || totalPaymentAmount <= 0) {
            setPaymentError(t('vouchers.amountRequired'));
            return;
          }

          const missingAccount = activeEntries.some((e) => !e.accountId);
          if (missingAccount) {
            setPaymentError(t('vouchers.missingAccount'));
            return;
          }

          if (submit(draft)) {
            post.mutate(draft);
          }
        }}
      >
        {/* Transaction Header Grid (Requirements 9 & 15) */}
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4 rounded-xl border border-line bg-surface-2/40 p-4">
          <TextField
            label={t('vouchers.transactionNo')}
            value={transactionNo}
            onChange={() => {}}
            disabled
            hint={t('vouchers.autoGenerated')}
            className="bg-surface-3/50 font-mono font-semibold"
            size="sm"
          />

          <TextField
            label={t('vouchers.referenceNo')}
            value={draft.reference}
            onChange={(value) => set('reference', value)}
            placeholder={transactionNo}
            hint={t('vouchers.referenceNoHint')}
            size="sm"
          />

          <DateField
            label={t('vouchers.date')}
            required
            value={draft.date}
            onChange={(value) => set('date', value)}
            error={errors['date']}
            size="sm"
          />

          <TextField
            label={t('vouchers.salesman')}
            value={salesman}
            onChange={setSalesman}
            placeholder={t('vouchers.primarySalesman')}
            size="sm"
          />
        </div>

        {/* Primary Account & Mode Selection (Requirement 10) */}
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <div className="sm:col-span-2">
            <Field
              label={
                direction === 'payment'
                  ? t('vouchers.debitAccount')
                  : t('vouchers.creditAccount')
              }
              required
              hint={
                direction === 'payment'
                  ? t('vouchers.debitAccountHint')
                  : t('vouchers.creditAccountHint')
              }
              error={errors['partyId']}
            >
              <SearchSelect
                value={draft.partyId}
                onChange={(value) => set('partyId', value)}
                options={partyOptions}
                disabled={ledgers.isPending}
                invalid={errors['partyId'] !== undefined}
                label={
                  direction === 'payment'
                    ? t('vouchers.debitAccount')
                    : t('vouchers.creditAccount')
                }
                placeholder={ledgers.isPending ? t('common.loading') : t('common.choose')}
              />
            </Field>
          </div>

          <Field label={t('vouchers.paymentMode')}>
            <SearchSelect
              value={draft.paymentMode}
              onChange={handlePaymentModeChange}
              options={paymentModeOptions}
              label={t('vouchers.paymentMode')}
              placeholder={t('vouchers.paymentModeNone')}
              clearable
            />
          </Field>

          <SelectField
            label={t('vouchers.through')}
            required
            value={through}
            onChange={(value) => {
              setThrough(value as 'cash' | 'bank');
              const relevant = all.filter((ledger) =>
                value === 'cash'
                  ? ledger.kind === LedgerKind.cash
                  : ledger.kind === LedgerKind.bank,
              );
              if (relevant.length > 0 && paymentEntries.length === 1) {
                const nextAcc = relevant[0]!.ledgerId;
                updatePaymentEntry(paymentEntries[0]!.id, { accountId: nextAcc });
              }
            }}
            options={[
              { value: 'bank', label: t('vouchers.throughBank') },
              { value: 'cash', label: t('vouchers.throughCash') },
            ]}
          />
        </div>

        {/* Multiple Payments Table (Requirements 11 & 16) */}
        <div className="flex flex-col gap-3 rounded-xl border border-line bg-surface p-4">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-line pb-2">
            <div>
              <span className="text-xs font-bold uppercase tracking-wider text-ink">
                {t('vouchers.multiplePayments')}
              </span>
              <p className="text-xs text-ink-muted">
                {t('vouchers.multiplePaymentsHint')}
              </p>
            </div>
            <button
              type="button"
              onClick={addPaymentEntry}
              className="btn-secondary btn-xs flex items-center gap-1 text-xs"
            >
              <IconPlus className="size-3" />
              {t('vouchers.addPaymentEntry')}
            </button>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-xs">
              <thead className="border-b border-line bg-surface-2 text-ink-muted">
                <tr>
                  <th className="w-32 px-3 py-2 text-start font-semibold">
                    {t('vouchers.paymentType')}
                  </th>
                  <th className="px-3 py-2 text-start font-semibold">
                    {t('vouchers.account')}
                  </th>
                  <th className="w-36 px-3 py-2 text-end font-semibold">
                    {t('vouchers.amount')}
                  </th>
                  <th className="w-48 px-3 py-2 text-start font-semibold">
                    {t('vouchers.chequeOrRef')}
                  </th>
                  <th className="w-10 px-2 py-2" />
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {paymentEntries.map((entry) => (
                  <tr key={entry.id} className="hover:bg-surface-2/40">
                    <td className="px-3 py-1.5">
                      <select
                        value={entry.paymentType}
                        onChange={(e) =>
                          updatePaymentEntry(entry.id, { paymentType: e.target.value })
                        }
                        className="field-input-sm w-full py-1 text-xs font-medium"
                      >
                        <option value="Cash">{t('vouchers.cash')}</option>
                        <option value="Credit">{t('vouchers.credit')}</option>
                        <option value="Bank">{t('vouchers.bank')}</option>
                        <option value="Cheque">{t('vouchers.cheque')} payment</option>
                        <option value="Card">{t('vouchers.card')}</option>
                      </select>
                    </td>
                    <td className="px-3 py-1.5">
                      <SearchSelect
                        value={entry.accountId}
                        onChange={(accountId) =>
                          updatePaymentEntry(entry.id, { accountId })
                        }
                        options={moneyAccountOptions}
                        size="sm"
                        label={t('vouchers.account')}
                        placeholder={t('common.choose')}
                      />
                    </td>
                    <td className="px-3 py-1.5 text-end">
                      <input
                        type="number"
                        inputMode="decimal"
                        step="0.01"
                        min="0"
                        value={entry.amount}
                        onChange={(e) =>
                          updatePaymentEntry(entry.id, { amount: e.target.value })
                        }
                        placeholder="0.00"
                        className="field-input-sm w-32 text-end font-mono tabular-nums"
                      />
                    </td>
                    <td className="px-3 py-1.5">
                      <input
                        type="text"
                        value={entry.reference}
                        onChange={(e) =>
                          updatePaymentEntry(entry.id, { reference: e.target.value })
                        }
                        placeholder={t('vouchers.chequeOrRef')}
                        className="field-input-sm w-full"
                      />
                    </td>
                    <td className="px-2 py-1.5 text-end">
                      {paymentEntries.length > 1 && (
                        <button
                          type="button"
                          onClick={() => removePaymentEntry(entry.id)}
                          className="rounded p-1 text-ink-muted transition hover:text-red-600"
                          title="Remove entry"
                        >
                          <IconClose className="size-3.5" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Total Payment Amount Bar */}
          <div className="flex items-center justify-between border-t border-line pt-2 text-sm">
            <span className="font-semibold text-ink-muted">
              {t('vouchers.totalPaymentAmount')}:
            </span>
            <span className="font-mono text-base font-bold text-ink">
              {money(totalPaymentAmount)}
            </span>
          </div>
        </div>

        <TextField
          label={t('vouchers.narration')}
          value={draft.narration}
          onChange={(value) => set('narration', value)}
          className="w-full"
        />

        {paymentError && <p className="alert-error text-xs">{paymentError}</p>}

        <p className="rounded-lg border border-line bg-surface-2 px-3 py-2 text-xs text-ink-muted">
          {t(
            direction === 'receipt'
              ? 'vouchers.receiptEffect'
              : 'vouchers.paymentEffect',
          )}
        </p>

        <div className="form-actions">
          <button type="submit" disabled={post.isPending} className="btn-primary">
            {post.isPending && <Spinner />}
            {post.isPending
              ? t('vouchers.posting')
              : direction === 'receipt'
                ? t('vouchers.postReceipt')
                : t('vouchers.postPayment')}
          </button>
        </div>
      </form>
    </section>
  );
}
