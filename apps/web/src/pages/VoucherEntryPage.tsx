import { Fragment, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { PageHeading } from '@/components/PageHeading';
import { Spinner } from '@/components/ReportFrame';
import { DateField, Field, SelectField, TextField } from '@/components/Form';
import { SearchSelect, type SelectOption } from '@/components/SearchSelect';
import { useIsNarrow } from '@/components/DataGrid';
import { IconClose, IconPlus } from '@/components/icons';
import { ApiError, request } from '@/lib/api';
import {
  isCashOrBank,
  isParty,
  LedgerKind,
  listLedgers,
  type LedgerSummary,
} from '@/lib/ledgers';
import { moneyAlways as money } from '@/lib/money';
import { PAYMENT_MODES } from '@/lib/paymentModes';
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

/** One line of a journal: a ledger and an amount on one side or the other. */
interface JournalLine {
  readonly key: string;
  ledgerId: string;
  debit: string;
  credit: string;
  narration: string;
}

function emptyJournalLine(): JournalLine {
  return { key: crypto.randomUUID(), ledgerId: '', debit: '', credit: '', narration: '' };
}

/**
 * Where the lines become cards rather than a table. Higher than the grid's own
 * breakpoint: the ledger, two amounts and a narration need a portrait tablet's width
 * before they fit side by side, and on a phone in landscape the table scrolled.
 */
const CARDS_BELOW = '(max-width: 767px)';

/**
 * Journal entry.
 *
 * The same layout as payments and receipts - the date and reference above, a grid of
 * ledgers below, the total under it and one button - with the one difference a journal
 * has: every line can be a debit or a credit, so the grid carries a column for each.
 * Typing an amount in one clears the other, because a line is one or the other and
 * the books hold a side and a positive amount.
 *
 * The running difference is shown as the user types, so a transposed digit is caught
 * at the keyboard rather than by the server after Save. The server still enforces the
 * rule - this is a courtesy, not the guarantee. The voucher's number is issued when it
 * is saved, from the branch's series, and the list it lands on shows it.
 */
export function VoucherEntryPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const narrow = useIsNarrow(CARDS_BELOW);

  const [type, setType] = useState<number>(VoucherType.journal);
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [narration, setNarration] = useState('');
  const [referenceNumber, setReferenceNumber] = useState('');
  const [lines, setLines] = useState<readonly JournalLine[]>([
    emptyJournalLine(),
    emptyJournalLine(),
  ]);

  const ledgers = useLedgers();
  const all = useMemo(() => ledgers.data ?? [], [ledgers.data]);
  const ledgerOptions = useMemo(() => all.map(ledgerOption), [all]);

  const totals = useMemo(() => {
    let debit = 0;
    let credit = 0;

    for (const line of lines) {
      debit += parseAmount(line.debit);
      credit += parseAmount(line.credit);
    }

    // Rounded to two places before comparing. Summing typed decimals in binary
    // floating point can leave a residue like 1e-13, which would report a
    // perfectly balanced voucher as unbalanced.
    const difference = Math.round((debit - credit) * 100) / 100;

    // Three states, not two: an untouched voucher has a difference of zero and is
    // not balanced, and saying "Difference 0.00" about it is neither true nor useful.
    return {
      debit,
      credit,
      difference,
      isEmpty: debit === 0 && credit === 0,
      isBalanced: difference === 0 && debit > 0,
    };
  }, [lines]);

  const entered = lines.filter(
    (line) => parseAmount(line.debit) > 0 || parseAmount(line.credit) > 0,
  );
  const unnamed = entered.some((line) => line.ledgerId === '');
  const canSubmit = totals.isBalanced && entered.length >= 2 && !unnamed;

  const update = (key: string, patch: Partial<JournalLine>): void =>
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );

  const post = useMutation<CreateVoucherResponse, ApiError>({
    mutationFn: () =>
      request<CreateVoucherResponse>('/accounting/vouchers', {
        method: 'POST',
        body: {
          type,
          date,
          referenceNumber: referenceNumber.trim() || null,
          narration: narration.trim() || null,
          postImmediately: true,
          lines: entered.map((line) => {
            const debit = parseAmount(line.debit);
            return {
              ledgerId: line.ledgerId,
              side: debit > 0 ? DEBIT : CREDIT,
              amount: debit > 0 ? debit : parseAmount(line.credit),
              narration: line.narration.trim() || null,
            };
          }),
        },
      }),
    onSuccess: (response) => {
      // The trial balance is now stale by definition.
      void queryClient.invalidateQueries({ queryKey: ['trial-balance'] });
      void queryClient.invalidateQueries({ queryKey: ['voucher-report'] });

      navigate(listedAt(type, response.number));
    },
  });

  const ledgerControl = (line: JournalLine): React.JSX.Element => (
    <SearchSelect
      value={line.ledgerId}
      onChange={(ledgerId) => update(line.key, { ledgerId })}
      options={ledgerOptions}
      disabled={ledgers.isPending}
      size="sm"
      label={t('vouchers.ledger')}
      placeholder={ledgers.isPending ? t('common.loading') : t('common.choose')}
    />
  );

  const amountControl = (
    line: JournalLine,
    side: 'debit' | 'credit',
  ): React.JSX.Element => (
    <AmountInput
      label={side === 'debit' ? t('vouchers.debitAmount') : t('vouchers.creditAmount')}
      value={line[side]}
      onChange={(value) =>
        update(
          line.key,
          side === 'debit' ? { debit: value, credit: '' } : { credit: value, debit: '' },
        )
      }
    />
  );

  const narrationControl = (line: JournalLine): React.JSX.Element => (
    <input
      aria-label={t('vouchers.lineNarration')}
      className="field-input-sm w-full"
      value={line.narration}
      onChange={(e) => update(line.key, { narration: e.target.value })}
    />
  );

  const removeControl = (line: JournalLine): React.JSX.Element => (
    <RemoveButton
      label={t('vouchers.removeLine')}
      disabled={lines.length <= 2}
      onClick={() => setLines((current) => current.filter((l) => l.key !== line.key))}
    />
  );

  return (
    <section className="page">
      <PageHeading title={t('vouchers.entryTitle')} />

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
          if (canSubmit) post.mutate();
        }}
      >
        <div className="grid grid-cols-1 gap-3 rounded-xl border border-line bg-surface-2/40 p-4 sm:grid-cols-2 lg:grid-cols-4">
          <SelectField
            label={t('vouchers.type')}
            required
            value={type}
            onChange={setType}
            options={[
              { value: VoucherType.journal, label: t('voucherTypes.Journal') },
              { value: VoucherType.contra, label: t('voucherTypes.Contra') },
            ]}
            size="sm"
          />
          <DateField
            label={t('vouchers.date')}
            required
            value={date}
            onChange={setDate}
            size="sm"
          />
          <TextField
            label={t('vouchers.referenceNo')}
            value={referenceNumber}
            onChange={setReferenceNumber}
            size="sm"
          />
          <TextField
            label={t('vouchers.narration')}
            value={narration}
            onChange={setNarration}
            size="sm"
          />
        </div>

        <LinesFrame
          title={t('vouchers.journalDetails')}
          hint={t('vouchers.journalDetailsHint')}
          addLabel={t('vouchers.addLedger')}
          onAdd={() => setLines((current) => [...current, emptyJournalLine()])}
        >
          {narrow ? (
            /*
              A card per line on a phone. Five controls per row do not fit across a
              phone, and scrolled sideways the amount - the one thing being entered -
              started off the screen.
            */
            <div className="space-y-3">
              {lines.map((line, index) => (
                <div
                  key={line.key}
                  className="space-y-2.5 rounded-lg border border-line p-3"
                >
                  <div className="flex items-center gap-2">
                    <span className="text-xs font-semibold text-ink-muted">
                      #{index + 1}
                    </span>
                    <div className="min-w-0 flex-1">{ledgerControl(line)}</div>
                    {removeControl(line)}
                  </div>
                  <div className="grid grid-cols-2 gap-2">
                    <div>
                      <span className="field-label">{t('reports.debit')}</span>
                      {amountControl(line, 'debit')}
                    </div>
                    <div>
                      <span className="field-label">{t('reports.credit')}</span>
                      {amountControl(line, 'credit')}
                    </div>
                  </div>
                  {narrationControl(line)}
                </div>
              ))}
            </div>
          ) : (
            // Narrower columns below `lg`, so a portrait tablet has the credit and
            // the narration on screen rather than behind a sideways scroll.
            <div className="table-wrap">
              <table className="table min-w-[40rem] lg:min-w-[48rem]">
                <thead>
                  <tr>
                    <th className="w-10 text-start">#</th>
                    <th className="text-start">{t('vouchers.ledger')}</th>
                    <th className="w-28 text-end lg:w-36">{t('reports.debit')}</th>
                    <th className="w-28 text-end lg:w-36">{t('reports.credit')}</th>
                    <th className="text-start">{t('vouchers.lineNarration')}</th>
                    <th className="w-10" />
                  </tr>
                </thead>
                <tbody>
                  {lines.map((line, index) => (
                    <tr key={line.key}>
                      <td className="py-2 text-xs text-ink-muted">{index + 1}</td>
                      <td className="min-w-48 py-2 lg:min-w-56">{ledgerControl(line)}</td>
                      <td className="py-2">{amountControl(line, 'debit')}</td>
                      <td className="py-2">{amountControl(line, 'credit')}</td>
                      <td className="min-w-32 py-2 lg:min-w-40">
                        {narrationControl(line)}
                      </td>
                      <td className="px-2 py-2 text-end">{removeControl(line)}</td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr>
                    <td colSpan={2}>{t('vouchers.total')}</td>
                    <td className="cell-numeric">{money(totals.debit)}</td>
                    <td className="cell-numeric">{money(totals.credit)}</td>
                    <td colSpan={2} />
                  </tr>
                </tfoot>
              </table>
            </div>
          )}

          {narrow && (
            <dl className="grid grid-cols-2 gap-2 border-t border-line pt-2 text-sm">
              <div>
                <dt className="text-xs text-ink-muted">{t('reports.debit')}</dt>
                <dd className="font-mono font-semibold tabular-nums">
                  {money(totals.debit)}
                </dd>
              </div>
              <div className="text-end">
                <dt className="text-xs text-ink-muted">{t('reports.credit')}</dt>
                <dd className="font-mono font-semibold tabular-nums">
                  {money(totals.credit)}
                </dd>
              </div>
            </dl>
          )}
        </LinesFrame>

        {unnamed && <p className="alert-error text-xs">{t('vouchers.ledgerRequired')}</p>}

        <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-center sm:justify-end">
          <BalanceBadge totals={totals} />
          <button
            type="submit"
            disabled={!canSubmit || post.isPending}
            className="btn-primary"
          >
            {post.isPending && <Spinner />}
            {post.isPending ? t('vouchers.posting') : t('vouchers.post')}
          </button>
        </div>

        <p className="text-xs text-ink-muted">{t('vouchers.entryHint')}</p>
      </form>
    </section>
  );
}

/** Says whether a journal balances, and by how much it does not. */
function BalanceBadge({
  totals,
}: {
  readonly totals: {
    readonly difference: number;
    readonly isEmpty: boolean;
    readonly isBalanced: boolean;
  };
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
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
  );
}

/** The bordered block a grid of ledgers sits in, with its heading and Add button. */
function LinesFrame({
  title,
  hint,
  addLabel,
  onAdd,
  children,
}: {
  readonly title: string;
  readonly hint: string;
  readonly addLabel: string;
  readonly onAdd: () => void;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <div className="flex flex-col gap-3 rounded-xl border border-line bg-surface p-4">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-line pb-2">
        <div>
          <span className="text-xs font-bold tracking-wider text-ink uppercase">
            {title}
          </span>
          <p className="text-xs text-ink-muted">{hint}</p>
        </div>
        <button
          type="button"
          onClick={onAdd}
          className="btn-secondary btn-sm flex items-center gap-1"
        >
          <IconPlus className="size-3.5" />
          {addLabel}
        </button>
      </div>
      {children}
    </div>
  );
}

/** An amount box, end-aligned like the column's heading and its total. */
function AmountInput({
  label,
  value,
  onChange,
  disabled = false,
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
  /** Where the amount is what something else adds up to, such as the bills ticked. */
  readonly disabled?: boolean;
}): React.JSX.Element {
  return (
    <input
      aria-label={label}
      type="number"
      inputMode="decimal"
      step="0.01"
      min="0"
      placeholder="0.00"
      className="field-input-sm w-full text-end font-mono tabular-nums"
      value={value}
      disabled={disabled}
      onChange={(e) => onChange(e.target.value)}
    />
  );
}

function RemoveButton({
  label,
  disabled,
  onClick,
}: {
  readonly label: string;
  readonly disabled: boolean;
  readonly onClick: () => void;
}): React.JSX.Element {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={onClick}
      className="grid size-7 place-items-center rounded-md text-ink-subtle transition hover:bg-red-50 hover:text-red-600 active:scale-90 disabled:pointer-events-none disabled:opacity-30 dark:hover:bg-red-500/10 dark:hover:text-red-400"
    >
      <IconClose className="size-3.5" />
    </button>
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

/** One ledger being paid or received from. */
interface MoneyLine {
  readonly key: string;
  ledgerId: string;
  amount: string;
  narration: string;
  /** What is set against each of the party's open bills, by bill, as typed. */
  allocations: Readonly<Record<string, string>>;
}

function emptyMoneyLine(): MoneyLine {
  return {
    key: crypto.randomUUID(),
    ledgerId: '',
    amount: '',
    narration: '',
    allocations: {},
  };
}

/** What a line's ticked bills come to. */
function allocated(line: MoneyLine): number {
  return Object.values(line.allocations).reduce(
    (sum, value) => sum + parseAmount(value),
    0,
  );
}

/** One of a party's bills still open. */
interface OpenBill {
  readonly billId: string;
  readonly billNumber: string;
  readonly billDate: string;
  readonly dueDate: string;
  readonly outstandingAmount: number;
  readonly daysOverdue: number;
}

/**
 * The bills a bill-wise party still has open, under the line that pays or receives
 * against them. Where bills may be filled, each can be ticked and given the part of the
 * money that goes to it; the line's amount is then what the ticked bills come to.
 */
function PendingBills({
  ledgerId,
  billType,
  fillable,
  allocations,
  onChange,
}: {
  readonly ledgerId: string;
  readonly billType: number;
  readonly fillable: boolean;
  readonly allocations: Readonly<Record<string, string>>;
  readonly onChange: (allocations: Readonly<Record<string, string>>) => void;
}): React.JSX.Element | null {
  const { t } = useTranslation();
  const bills = useQuery<readonly OpenBill[], ApiError>({
    queryKey: ['open-bills', ledgerId, billType],
    queryFn: () =>
      request<readonly OpenBill[]>(
        `/accounting/vouchers/open-bills?ledgerId=${ledgerId}&type=${billType}`,
      ),
  });

  if (bills.isPending) {
    return <p className="text-xs text-ink-muted">{t('common.loading')}</p>;
  }

  const rows = bills.data ?? [];

  if (rows.length === 0) {
    return <p className="text-xs text-ink-muted">{t('vouchers.noPendingBills')}</p>;
  }

  const outstanding = rows.reduce((sum, bill) => sum + bill.outstandingAmount, 0);
  const set = (billId: string, value: string | null): void => {
    const next = { ...allocations };
    if (value === null) delete next[billId];
    else next[billId] = value;
    onChange(next);
  };

  return (
    <div className="space-y-1 rounded-lg border border-line bg-surface-2/40 p-2">
      <p className="text-xs font-semibold text-ink-muted">
        {t('vouchers.pendingBills', { count: rows.length, total: money(outstanding) })}
      </p>
      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <thead className="text-ink-muted">
            <tr>
              {fillable && <th className="w-8" />}
              <th className="px-2 py-1 text-start font-semibold">
                {t('vouchers.billNumber')}
              </th>
              <th className="px-2 py-1 text-start font-semibold">
                {t('vouchers.billDate')}
              </th>
              <th className="px-2 py-1 text-start font-semibold">
                {t('vouchers.billDue')}
              </th>
              <th className="px-2 py-1 text-end font-semibold">
                {t('vouchers.billOutstanding')}
              </th>
              {fillable && (
                <th className="px-2 py-1 text-end font-semibold">
                  {t('vouchers.billAllocated')}
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {rows.map((bill) => {
              const ticked = allocations[bill.billId] !== undefined;
              return (
                <tr key={bill.billId} className="border-t border-line/60">
                  {fillable && (
                    <td className="px-2 py-1">
                      <input
                        type="checkbox"
                        aria-label={t('vouchers.settleBill', { number: bill.billNumber })}
                        checked={ticked}
                        onChange={(event) =>
                          set(
                            bill.billId,
                            event.target.checked ? String(bill.outstandingAmount) : null,
                          )
                        }
                      />
                    </td>
                  )}
                  <td className="px-2 py-1 font-mono">{bill.billNumber}</td>
                  <td className="px-2 py-1">{bill.billDate}</td>
                  <td
                    className={clsx('px-2 py-1', bill.daysOverdue > 0 && 'text-red-600')}
                  >
                    {bill.dueDate}
                    {bill.daysOverdue > 0 ? ` (${bill.daysOverdue}d)` : ''}
                  </td>
                  <td className="px-2 py-1 text-end font-mono tabular-nums">
                    {money(bill.outstandingAmount)}
                  </td>
                  {fillable && (
                    <td className="px-2 py-1 text-end">
                      {ticked && (
                        <input
                          inputMode="decimal"
                          aria-label={t('vouchers.billAllocated')}
                          value={allocations[bill.billId] ?? ''}
                          onChange={(event) => {
                            const value = event.target.value.replace(/[^\d.]/g, '');
                            set(
                              bill.billId,
                              parseAmount(value) > bill.outstandingAmount
                                ? String(bill.outstandingAmount)
                                : value,
                            );
                          }}
                          className="field-input-sm w-28 text-end font-mono tabular-nums"
                        />
                      )}
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

interface ChequeDetails {
  number: string;
  accountNumber: string;
  date: string;
  inFavourOf: string;
}

interface CardDetails {
  type: string;
  lastDigits: string;
  approval: string;
}

/**
 * Payments and receipts, each on a screen of its own.
 *
 * The cash or bank account the money leaves or arrives in is chosen once, at the top,
 * from the firm's cash and bank ledgers only; the ledgers it is paid to or received
 * from are a grid below, as many as the one payment covers - a cheque for rent and
 * cleaning together is one payment of two lines. The sides are decided by which of
 * the two screens somebody opened, and whether it is a cash or a bank voucher by the
 * account they chose.
 *
 * How the money moved decides what else is asked. A cheque needs its number, the
 * account it is drawn on, its date and who it is made out to, and each ledger line
 * carries it into the cheque register; a card may note its last four digits and the
 * approval code; cash needs nothing more.
 *
 * The voucher's number is issued when it is saved, and the list it lands on shows it.
 */
function MoneyEntry({
  direction,
}: {
  readonly direction: 'payment' | 'receipt';
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const narrow = useIsNarrow(CARDS_BELOW);
  const defaultPaymentMode = useSettings((state) => state.defaultPaymentMode);
  const billWise = useSettings((state) =>
    direction === 'receipt' ? state.billWiseReceipts : state.billWisePayments,
  );
  const fillBills = useSettings((state) =>
    direction === 'receipt' ? state.fillBillsOnReceipts : state.fillBillsOnPayments,
  );
  // A receipt settles what customers owe; a payment what is owed to suppliers.
  const billType = direction === 'receipt' ? 1 : 2;

  const today = new Date().toISOString().slice(0, 10);
  const [date, setDate] = useState(today);
  const [reference, setReference] = useState('');
  const [narration, setNarration] = useState('');
  const [accountId, setAccountId] = useState('');
  const [mode, setMode] = useState(
    defaultPaymentMode && defaultPaymentMode !== 'Credit' ? defaultPaymentMode : 'Cash',
  );
  const [cheque, setCheque] = useState<ChequeDetails>({
    number: '',
    accountNumber: '',
    date: '',
    inFavourOf: '',
  });
  const [card, setCard] = useState<CardDetails>({
    type: '',
    lastDigits: '',
    approval: '',
  });
  const [lines, setLines] = useState<readonly MoneyLine[]>([emptyMoneyLine()]);
  const [problem, setProblem] = useState<string | null>(null);

  const ledgers = useLedgers();
  const all = useMemo(() => ledgers.data ?? [], [ledgers.data]);

  const moneyAccounts = useMemo(() => all.filter(isCashOrBank), [all]);
  const account = moneyAccounts.find((ledger) => ledger.ledgerId === accountId);
  const isBank = account?.kind === LedgerKind.bank;

  // Cash for cash, a bank for everything else, until somebody chooses.
  const [accountTouched, setAccountTouched] = useState(false);
  useEffect(() => {
    if (accountTouched || moneyAccounts.length === 0) return;
    const wantsBank = mode !== 'Cash';
    const preferred =
      moneyAccounts.find((ledger) =>
        wantsBank ? ledger.kind === LedgerKind.bank : ledger.kind === LedgerKind.cash,
      ) ?? moneyAccounts[0];
    if (preferred && preferred.ledgerId !== accountId) setAccountId(preferred.ledgerId);
  }, [moneyAccounts, mode, accountTouched, accountId]);

  const accountOptions = useMemo(
    () =>
      moneyAccounts.map((ledger) => ({
        ...ledgerOption(ledger),
        detail:
          ledger.kind === LedgerKind.bank
            ? t('vouchers.groupBank')
            : t('vouchers.groupCash'),
      })),
    [moneyAccounts, t],
  );

  // Parties first, then everything else that is not itself cash or bank.
  const ledgerOptions = useMemo(() => {
    const parties = all.filter(isParty).map(ledgerOption);
    const others = all
      .filter((ledger) => !isParty(ledger) && !isCashOrBank(ledger))
      .map(ledgerOption);
    return [...parties, ...others];
  }, [all]);

  const modeOptions = useMemo(
    (): readonly SelectOption[] =>
      PAYMENT_MODES.filter((option) => option.value !== 'Credit').map((option) => ({
        value: option.value,
        label: t(option.labelKey),
      })),
    [t],
  );

  const entered = lines.filter((line) => parseAmount(line.amount) > 0);
  const total = entered.reduce((sum, line) => sum + parseAmount(line.amount), 0);

  const type =
    direction === 'receipt'
      ? isBank
        ? VoucherType.bankReceipt
        : VoucherType.cashReceipt
      : isBank
        ? VoucherType.bankPayment
        : VoucherType.cashPayment;

  const update = (key: string, patch: Partial<MoneyLine>): void =>
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );

  /** What is wrong with the form, or null when it can be saved. */
  const check = (): string | null => {
    if (!date) return t('vouchers.dateRequired');
    if (!accountId) return t('vouchers.moneyAccountRequired');
    if (entered.length === 0) return t('vouchers.amountRequired');
    if (entered.some((line) => !line.ledgerId)) return t('vouchers.ledgerRequired');
    if (mode === 'Cheque') {
      if (!cheque.number.trim()) return t('vouchers.chequeNumberRequired');
      if (direction === 'payment' && !isBank) return t('vouchers.chequeNeedsBank');
    }
    return null;
  };

  /** The card details, as the money line's narration, when any were given. */
  const cardNote = (): string | null => {
    if (mode !== 'Card') return null;
    const parts = [
      card.type.trim(),
      card.lastDigits.trim() ? `****${card.lastDigits.trim()}` : '',
      card.approval.trim() ? `${t('vouchers.cardApproval')} ${card.approval.trim()}` : '',
    ].filter(Boolean);
    return parts.length > 0 ? `${t('paymentModes.card')}: ${parts.join(' · ')}` : null;
  };

  const post = useMutation<CreateVoucherResponse, ApiError>({
    mutationFn: () => {
      const partySide = direction === 'receipt' ? CREDIT : DEBIT;
      const moneySide = direction === 'receipt' ? DEBIT : CREDIT;
      const chequeDate = cheque.date || date;

      const partyLines = entered.map((line) => {
        const amount = parseAmount(line.amount);
        const settled = Object.entries(line.allocations)
          .map(([billId, value]) => ({ billId, amount: parseAmount(value) }))
          .filter((allocation) => allocation.amount > 0);
        return {
          ledgerId: line.ledgerId,
          side: partySide,
          amount,
          narration: line.narration.trim() || null,
          // The bills ticked against this line, settled by what was set against each.
          ...(settled.length > 0
            ? {
                billReferences: settled.map((allocation) => ({
                  kind: 2,
                  amount: allocation.amount,
                  billId: allocation.billId,
                })),
              }
            : {}),
          // Each ledger the cheque pays carries its share into the cheque register,
          // under the cheque's own number.
          ...(mode === 'Cheque'
            ? {
                cheques: [
                  {
                    chequeNumber: cheque.number.trim(),
                    instrumentDate: chequeDate,
                    amount,
                    bankLedgerId: direction === 'payment' ? accountId : null,
                    accountNumber: cheque.accountNumber.trim() || null,
                    inFavourOf: cheque.inFavourOf.trim() || null,
                  },
                ],
              }
            : {}),
        };
      });

      const moneyLine = {
        ledgerId: accountId,
        side: moneySide,
        amount: Math.round(total * 100) / 100,
        narration: cardNote(),
      };

      return request<CreateVoucherResponse>('/accounting/vouchers', {
        method: 'POST',
        body: {
          type,
          date,
          referenceNumber: reference.trim() || null,
          narration: narration.trim() || null,
          paymentMode: mode || null,
          postImmediately: true,
          lines:
            direction === 'payment'
              ? [...partyLines, moneyLine]
              : [moneyLine, ...partyLines],
        },
      });
    },
    onSuccess: (response) => {
      void queryClient.invalidateQueries({ queryKey: ['trial-balance'] });
      void queryClient.invalidateQueries({ queryKey: ['voucher-report'] });
      void queryClient.invalidateQueries({ queryKey: ['cheques'] });

      navigate(listedAt(type, response.number));
    },
  });

  const ledgerControl = (line: MoneyLine): React.JSX.Element => (
    <SearchSelect
      value={line.ledgerId}
      onChange={(ledgerId) => update(line.key, { ledgerId, allocations: {} })}
      options={ledgerOptions}
      disabled={ledgers.isPending}
      size="sm"
      label={t('vouchers.ledger')}
      placeholder={ledgers.isPending ? t('common.loading') : t('common.choose')}
    />
  );

  const amountControl = (line: MoneyLine): React.JSX.Element => (
    <AmountInput
      label={t('vouchers.amount')}
      value={line.amount}
      onChange={(amount) => update(line.key, { amount })}
      disabled={Object.keys(line.allocations).length > 0}
    />
  );

  /** The party's open bills, where the party is kept bill-wise and the setting shows them. */
  const billsControl = (line: MoneyLine): React.JSX.Element | null => {
    const party = all.find((ledger) => ledger.ledgerId === line.ledgerId);
    if (!billWise || !party?.isBillWise) return null;
    return (
      <PendingBills
        ledgerId={party.ledgerId}
        billType={billType}
        fillable={fillBills}
        allocations={line.allocations}
        onChange={(allocations) => {
          const next = { ...line, allocations };
          const sum = allocated(next);
          update(line.key, {
            allocations,
            ...(Object.keys(allocations).length > 0
              ? { amount: sum > 0 ? String(Math.round(sum * 100) / 100) : '' }
              : {}),
          });
        }}
      />
    );
  };

  const narrationControl = (line: MoneyLine): React.JSX.Element => (
    <input
      aria-label={t('vouchers.lineNarration')}
      className="field-input-sm w-full"
      value={line.narration}
      onChange={(e) => update(line.key, { narration: e.target.value })}
    />
  );

  const removeControl = (line: MoneyLine): React.JSX.Element => (
    <RemoveButton
      label={t('vouchers.removeLine')}
      disabled={lines.length <= 1}
      onClick={() => setLines((current) => current.filter((l) => l.key !== line.key))}
    />
  );

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
          const wrong = check();
          setProblem(wrong);
          if (wrong === null) post.mutate();
        }}
      >
        <div className="grid grid-cols-1 gap-3 rounded-xl border border-line bg-surface-2/40 p-4 sm:grid-cols-2 lg:grid-cols-4">
          <DateField
            label={t('vouchers.date')}
            required
            value={date}
            onChange={setDate}
            size="sm"
          />
          <TextField
            label={t('vouchers.referenceNo')}
            value={reference}
            onChange={setReference}
            size="sm"
          />
          <Field
            label={
              direction === 'payment'
                ? t('vouchers.paidFrom')
                : t('vouchers.receivedInto')
            }
            required
          >
            <SearchSelect
              value={accountId}
              onChange={(value) => {
                setAccountTouched(true);
                setAccountId(value);
              }}
              options={accountOptions}
              disabled={ledgers.isPending}
              size="sm"
              label={
                direction === 'payment'
                  ? t('vouchers.paidFrom')
                  : t('vouchers.receivedInto')
              }
              placeholder={ledgers.isPending ? t('common.loading') : t('common.choose')}
            />
          </Field>
          <Field label={t('vouchers.paymentMode')}>
            <SearchSelect
              value={mode}
              onChange={(value) => setMode(value || 'Cash')}
              options={modeOptions}
              size="sm"
              label={t('vouchers.paymentMode')}
            />
          </Field>
        </div>

        {mode === 'Cheque' && (
          <fieldset className="grid grid-cols-1 gap-3 rounded-xl border border-line p-4 sm:grid-cols-2 lg:grid-cols-4">
            <legend className="px-1 text-xs font-bold tracking-wider text-ink uppercase">
              {t('vouchers.chequeDetails')}
            </legend>
            <TextField
              label={t('vouchers.chequeNumber')}
              required
              value={cheque.number}
              onChange={(value) => setCheque({ ...cheque, number: value })}
              size="sm"
            />
            <TextField
              label={t('vouchers.chequeAccountNumber')}
              value={cheque.accountNumber}
              onChange={(value) => setCheque({ ...cheque, accountNumber: value })}
              size="sm"
            />
            <DateField
              label={t('vouchers.chequeDate')}
              value={cheque.date || date}
              onChange={(value) => setCheque({ ...cheque, date: value })}
              size="sm"
            />
            <TextField
              label={t('vouchers.inFavourOf')}
              value={cheque.inFavourOf}
              onChange={(value) => setCheque({ ...cheque, inFavourOf: value })}
              size="sm"
            />
          </fieldset>
        )}

        {mode === 'Card' && (
          <fieldset className="grid grid-cols-1 gap-3 rounded-xl border border-line p-4 sm:grid-cols-3">
            <legend className="px-1 text-xs font-bold tracking-wider text-ink uppercase">
              {t('vouchers.cardDetails')}
            </legend>
            <TextField
              label={t('vouchers.cardType')}
              value={card.type}
              onChange={(value) => setCard({ ...card, type: value })}
              placeholder="Visa, Mastercard…"
              size="sm"
            />
            <TextField
              label={t('vouchers.cardLastDigits')}
              value={card.lastDigits}
              onChange={(value) =>
                setCard({ ...card, lastDigits: value.replace(/\D/g, '').slice(0, 4) })
              }
              size="sm"
            />
            <TextField
              label={t('vouchers.cardApproval')}
              value={card.approval}
              onChange={(value) => setCard({ ...card, approval: value })}
              size="sm"
            />
          </fieldset>
        )}

        <LinesFrame
          title={
            direction === 'payment'
              ? t('vouchers.paymentDetails')
              : t('vouchers.receiptDetails')
          }
          hint={
            direction === 'payment'
              ? t('vouchers.paymentDetailsHint')
              : t('vouchers.receiptDetailsHint')
          }
          addLabel={t('vouchers.addLedger')}
          onAdd={() => setLines((current) => [...current, emptyMoneyLine()])}
        >
          {narrow ? (
            <div className="space-y-3">
              {lines.map((line, index) => (
                <div
                  key={line.key}
                  className="space-y-2.5 rounded-lg border border-line p-3"
                >
                  <div className="flex items-center gap-2">
                    <span className="text-xs font-semibold text-ink-muted">
                      #{index + 1}
                    </span>
                    <div className="min-w-0 flex-1">{ledgerControl(line)}</div>
                    {removeControl(line)}
                  </div>
                  <div className="grid grid-cols-2 gap-2">
                    {amountControl(line)}
                    {narrationControl(line)}
                  </div>
                  {billsControl(line)}
                </div>
              ))}
            </div>
          ) : (
            <div className="table-wrap">
              <table className="table min-w-[40rem]">
                <thead>
                  <tr>
                    <th className="w-10 text-start">#</th>
                    <th className="text-start">{t('vouchers.ledger')}</th>
                    <th className="w-40 text-end">{t('vouchers.amount')}</th>
                    <th className="text-start">{t('vouchers.lineNarration')}</th>
                    <th className="w-10" />
                  </tr>
                </thead>
                <tbody>
                  {lines.map((line, index) => {
                    const bills = billsControl(line);
                    return (
                      <Fragment key={line.key}>
                        <tr>
                          <td className="py-2 text-xs text-ink-muted">{index + 1}</td>
                          <td className="min-w-48 py-2 lg:min-w-56">
                            {ledgerControl(line)}
                          </td>
                          <td className="py-2">{amountControl(line)}</td>
                          <td className="min-w-32 py-2 lg:min-w-40">
                            {narrationControl(line)}
                          </td>
                          <td className="px-2 py-2 text-end">{removeControl(line)}</td>
                        </tr>
                        {bills && (
                          <tr>
                            <td />
                            <td colSpan={4} className="pb-3">
                              {bills}
                            </td>
                          </tr>
                        )}
                      </Fragment>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}

          <div className="flex items-center justify-between border-t border-line pt-2 text-sm">
            <span className="font-semibold text-ink-muted">{t('vouchers.total')}</span>
            <span className="font-mono text-base font-bold text-ink tabular-nums">
              {money(total)}
            </span>
          </div>
        </LinesFrame>

        <TextField
          label={t('vouchers.narration')}
          value={narration}
          onChange={setNarration}
          className="w-full"
        />

        {problem && <p className="alert-error text-xs">{problem}</p>}

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
