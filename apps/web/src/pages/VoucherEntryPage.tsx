import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { PageHeading } from '@/components/PageHeading';
import { Spinner } from '@/components/ReportFrame';
import { DateField, Field, NumberField, SelectField, TextField } from '@/components/Form';
import { SearchSelect, type SelectOption } from '@/components/SearchSelect';
import { ApiError, request } from '@/lib/api';
import {
  isMoneyAccount,
  isParty,
  LedgerKind,
  listLedgers,
  type LedgerSummary,
} from '@/lib/ledgers';
import { collect, numeric, required, useValidation } from '@/lib/validation';

interface CreateVoucherResponse {
  readonly voucherId: string;
  readonly number: string;
  readonly status: number;
  readonly totalDebit: number;
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

function money(value: number): string {
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
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

  const [type, setType] = useState<number>(VoucherType.journal);
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [narration, setNarration] = useState('');
  const [referenceNumber, setReferenceNumber] = useState('');
  const [partyId, setPartyId] = useState('');
  const [lines, setLines] = useState<readonly DraftLine[]>([emptyLine(), emptyLine()]);
  const [posted, setPosted] = useState<CreateVoucherResponse | null>(null);

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
      setPosted(response);
      setLines([emptyLine(), emptyLine()]);
      setNarration('');
      setReferenceNumber('');
      setPartyId('');

      // The trial balance is now stale by definition, so it is invalidated rather
      // than left showing a position that predates this posting.
      void queryClient.invalidateQueries({ queryKey: ['trial-balance'] });
    },
  });

  return (
    <section className="page">
      <PageHeading title={t('vouchers.entryTitle')} />

      {posted && (
        <p className="alert-success">
          {t('vouchers.postedNotice', {
            number: posted.number,
            total: money(posted.totalDebit),
          })}
        </p>
      )}

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
        <Field label={t('vouchers.party')} hint={t('vouchers.partyHint')}>
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
              <th className="w-10" />
            </tr>
          </thead>

          <tbody>
            {lines.map((line) => (
              <tr key={line.key}>
                <td className="py-2">
                  <select
                    aria-label={t('vouchers.side')}
                    className="field-input-sm w-28"
                    value={line.side}
                    onChange={(e) =>
                      update(line.key, {
                        side: Number(e.target.value) === CREDIT ? CREDIT : DEBIT,
                      })
                    }
                  >
                    <option value={DEBIT}>{t('reports.debit')}</option>
                    <option value={CREDIT}>{t('reports.credit')}</option>
                  </select>
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
                <td className="py-2">
                  {line.side === DEBIT ? (
                    <input
                      aria-label={t('vouchers.debitAmount')}
                      type="number"
                      inputMode="decimal"
                      step="0.01"
                      min="0"
                      className="field-input-sm w-32 text-end font-mono tabular-nums"
                      value={line.amount}
                      onChange={(e) => update(line.key, { amount: e.target.value })}
                    />
                  ) : (
                    <span className="block text-end text-ink-subtle">—</span>
                  )}
                </td>

                <td className="py-2">
                  {line.side === CREDIT ? (
                    <input
                      aria-label={t('vouchers.creditAmount')}
                      type="number"
                      inputMode="decimal"
                      step="0.01"
                      min="0"
                      className="field-input-sm w-32 text-end font-mono tabular-nums"
                      value={line.amount}
                      onChange={(e) => update(line.key, { amount: e.target.value })}
                    />
                  ) : (
                    <span className="block text-end text-ink-subtle">—</span>
                  )}
                </td>

                <td className="px-2 py-2">
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
              'inline-flex items-center gap-2 rounded-lg px-3 py-1.5 text-sm font-medium',
              totals.isEmpty
                ? 'bg-surface-3 text-ink-muted'
                : totals.isBalanced
                  ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-500/12 dark:text-emerald-200'
                  : 'bg-amber-50 text-amber-900 dark:bg-amber-500/12 dark:text-amber-200',
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
function MoneyEntry({
  direction,
}: {
  readonly direction: 'payment' | 'receipt';
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  const [through, setThrough] = useState<'cash' | 'bank'>('bank');
  const [posted, setPosted] = useState<CreateVoucherResponse | null>(null);
  const [draft, setDraft] = useState<MoneyDraft>(() => ({
    date: new Date().toISOString().slice(0, 10),
    partyId: '',
    accountId: '',
    amount: '',
    reference: '',
    paymentMode: '',
    narration: '',
  }));

  const ledgers = useLedgers();
  const all = useMemo(() => ledgers.data ?? [], [ledgers.data]);

  /*
    A payment or a receipt is against somebody: a supplier being paid, a customer
    paying, an employee's advance. Ordinary ledgers are offered too — rent is paid to
    an expense account and not to a party — but the parties come first, because they
    are what most of these documents are against.
  */
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

  const { errors, submit, reset } = useValidation<MoneyDraft>((values) =>
    collect({
      date: required(values.date, t('vouchers.dateRequired')),
      partyId: required(values.partyId, t('vouchers.partyRequired')),
      accountId: required(
        values.accountId,
        through === 'cash' ? t('vouchers.cashRequired') : t('vouchers.bankRequired'),
      ),
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
      const amount = Number(values.amount);

      /*
        Which side each account takes.

        A receipt debits the firm's cash or bank — its money went up — and credits
        the party, whose debt to the firm went down. A payment is the same sentence
        read backwards. Deciding it here rather than asking is the whole reason
        these screens exist: a cashier should not have to know which of two
        accounts is debited to record a customer handing over a note.
      */
      const moneySide = direction === 'receipt' ? DEBIT : CREDIT;
      const partySide = direction === 'receipt' ? CREDIT : DEBIT;

      return request<CreateVoucherResponse>('/accounting/vouchers', {
        method: 'POST',
        body: {
          type,
          date: values.date,
          referenceNumber: values.reference || null,
          narration: values.narration || null,
          paymentMode: values.paymentMode || null,
          postImmediately: true,
          lines: [
            { ledgerId: values.accountId, side: moneySide, amount },
            { ledgerId: values.partyId, side: partySide, amount },
          ],
        },
      });
    },
    onSuccess: (response) => {
      setPosted(response);
      reset();
      setDraft((current) => ({
        ...current,
        partyId: '',
        amount: '',
        reference: '',
        narration: '',
      }));

      void queryClient.invalidateQueries({ queryKey: ['trial-balance'] });
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

      {posted && (
        <p className="alert-success">
          {t('vouchers.postedNotice', {
            number: posted.number,
            total: money(posted.totalDebit),
          })}
        </p>
      )}

      {post.isError && (
        <div role="alert" className="alert-error">
          <p className="font-semibold">{post.error.code}</p>
          <p className="mt-0.5 opacity-90">{post.error.detail}</p>
        </div>
      )}

      <form
        className="card card-body space-y-4"
        onSubmit={(event) => {
          event.preventDefault();

          if (submit(draft)) {
            post.mutate(draft);
          }
        }}
      >
        <div className="form-grid-3">
          <SelectField
            label={t('vouchers.through')}
            required
            value={through}
            onChange={(value) => {
              setThrough(value);
              // The account belongs to the kind that was chosen, so it goes with it
              // rather than staying behind as a bank account on a cash voucher.
              set('accountId', '');
            }}
            options={[
              { value: 'bank', label: t('vouchers.throughBank') },
              { value: 'cash', label: t('vouchers.throughCash') },
            ]}
          />

          <DateField
            label={t('vouchers.date')}
            required
            value={draft.date}
            onChange={(value) => set('date', value)}
            error={errors['date']}
          />

          <NumberField
            label={t('vouchers.amount')}
            required
            min={0}
            step="0.01"
            value={draft.amount}
            onChange={(value) => set('amount', value)}
            error={errors['amount']}
          />

          <Field
            label={
              direction === 'receipt' ? t('vouchers.receivedFrom') : t('vouchers.paidTo')
            }
            required
            error={errors['partyId']}
            hint={t('vouchers.partyHint')}
          >
            <SearchSelect
              value={draft.partyId}
              onChange={(value) => set('partyId', value)}
              options={partyOptions}
              disabled={ledgers.isPending}
              invalid={errors['partyId'] !== undefined}
              label={
                direction === 'receipt'
                  ? t('vouchers.receivedFrom')
                  : t('vouchers.paidTo')
              }
              placeholder={ledgers.isPending ? t('common.loading') : t('common.choose')}
            />
          </Field>

          <Field
            label={
              through === 'cash' ? t('vouchers.cashAccount') : t('vouchers.bankAccount')
            }
            required
            error={errors['accountId']}
          >
            <SearchSelect
              value={draft.accountId}
              onChange={(value) => set('accountId', value)}
              options={accounts.map(ledgerOption)}
              disabled={ledgers.isPending}
              invalid={errors['accountId'] !== undefined}
              label={
                through === 'cash' ? t('vouchers.cashAccount') : t('vouchers.bankAccount')
              }
              placeholder={ledgers.isPending ? t('common.loading') : t('common.choose')}
            />
          </Field>

          <TextField
            label={t('vouchers.paymentMode')}
            value={draft.paymentMode}
            onChange={(value) => set('paymentMode', value)}
            placeholder={t('vouchers.paymentModeHint')}
          />

          <TextField
            label={t('vouchers.reference')}
            value={draft.reference}
            onChange={(value) => set('reference', value)}
          />

          <TextField
            label={t('vouchers.narration')}
            value={draft.narration}
            onChange={(value) => set('narration', value)}
            className="sm:col-span-2"
          />
        </div>

        {/*
          What the screen is about to write, in the words the books will use. A
          two-line double entry made on somebody's behalf should still be shown to
          them: it is the only way a mistake in the direction is catchable before it
          is posted.
        */}
        <p className="rounded-lg border border-line bg-surface-2 px-3 py-2 text-xs text-ink-muted">
          {t(
            direction === 'receipt' ? 'vouchers.receiptEffect' : 'vouchers.paymentEffect',
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
