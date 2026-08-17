import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { Spinner } from '@/components/ReportFrame';
import { ApiError, request } from '@/lib/api';

interface LedgerSummary {
  readonly ledgerId: string;
  readonly code: string;
  readonly name: string;
  readonly groupCode: string;
  readonly groupName: string;
}

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
const VOUCHER_TYPES = [
  [1, 'Cash Receipt'],
  [2, 'Bank Receipt'],
  [3, 'Cash Payment'],
  [4, 'Bank Payment'],
  [5, 'Journal'],
  [6, 'Contra'],
] as const;

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

/**
 * Voucher entry.
 *
 * Mirrors the reference application's payment-voucher screen: a Debit/Credit
 * selector per line, separate debit and credit amount columns, a running total
 * per column, and the standing rule that every debit needs a corresponding
 * credit.
 *
 * The running difference is the important part of the design. It is shown as the
 * user types, so a transposed digit is caught at the keyboard rather than by the
 * server after Save. The server still enforces the rule - this is a courtesy, not
 * the guarantee.
 */
export function VoucherEntryPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  const [type, setType] = useState<number>(5);
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [narration, setNarration] = useState('');
  const [referenceNumber, setReferenceNumber] = useState('');
  const [lines, setLines] = useState<readonly DraftLine[]>([emptyLine(), emptyLine()]);
  const [posted, setPosted] = useState<CreateVoucherResponse | null>(null);

  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => request<readonly LedgerSummary[]>('/accounting/ledgers'),
    // The chart of accounts changes rarely; refetching it on every visit to the
    // entry screen would be wasted work.
    staleTime: 5 * 60 * 1000,
  });

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

    return { debit, credit, difference, isBalanced: difference === 0 && debit > 0 };
  }, [lines]);

  const canSubmit =
    totals.isBalanced &&
    lines.filter((l) => l.ledgerId !== '' && parseAmount(l.amount) > 0).length >= 2;

  const update = (key: string, patch: Partial<DraftLine>): void =>
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

      // The trial balance is now stale by definition, so it is invalidated rather
      // than left showing a position that predates this posting.
      void queryClient.invalidateQueries({ queryKey: ['trial-balance'] });
    },
  });

  return (
    <section className="page">
      <header className="page-header">
        <h1 className="page-title">Voucher entry</h1>
      </header>

      {posted && (
        <p className="alert-success">
          Posted <strong>{posted.number}</strong> for {money(posted.totalDebit)}.
        </p>
      )}

      {post.isError && (
        <div role="alert" className="alert-error">
          <p className="font-semibold">{post.error.code}</p>
          <p className="mt-0.5 opacity-90">{post.error.detail}</p>
        </div>
      )}

      <div className="panel grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <div>
          <label htmlFor="type" className="field-label">
            Voucher type
          </label>
          <select
            id="type"
            className="field-input"
            value={type}
            onChange={(e) => setType(Number(e.target.value))}
          >
            {VOUCHER_TYPES.map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="date" className="field-label">
            Date
          </label>
          <input
            id="date"
            type="date"
            className="field-input"
            value={date}
            onChange={(e) => setDate(e.target.value)}
          />
        </div>

        <div>
          <label htmlFor="reference" className="field-label">
            Ref / Inv no.
          </label>
          <input
            id="reference"
            className="field-input"
            value={referenceNumber}
            onChange={(e) => setReferenceNumber(e.target.value)}
          />
        </div>

        <div>
          <label htmlFor="narration" className="field-label">
            Narration
          </label>
          <input
            id="narration"
            className="field-input"
            value={narration}
            onChange={(e) => setNarration(e.target.value)}
          />
        </div>
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
              <th className="text-start">Dr / Cr</th>
              <th className="text-start">Ledger</th>
              <th className="text-start">Line narration</th>
              <th className="text-end">Debit</th>
              <th className="text-end">Credit</th>
              <th className="w-10" />
            </tr>
          </thead>

          <tbody>
            {lines.map((line) => (
              <tr key={line.key}>
                <td className="py-2">
                  <select
                    aria-label="Debit or credit"
                    className="field-input-sm w-28"
                    value={line.side}
                    onChange={(e) =>
                      update(line.key, {
                        side: Number(e.target.value) === CREDIT ? CREDIT : DEBIT,
                      })
                    }
                  >
                    <option value={DEBIT}>Debit</option>
                    <option value={CREDIT}>Credit</option>
                  </select>
                </td>

                <td className="py-2">
                  <select
                    aria-label="Ledger"
                    className="field-input-sm min-w-52"
                    value={line.ledgerId}
                    onChange={(e) => update(line.key, { ledgerId: e.target.value })}
                    disabled={ledgers.isPending}
                  >
                    <option value="">
                      {ledgers.isPending ? t('common.loading') : '— select —'}
                    </option>
                    {(ledgers.data ?? []).map((l) => (
                      <option key={l.ledgerId} value={l.ledgerId}>
                        {l.code} · {l.name} ({l.groupName})
                      </option>
                    ))}
                  </select>
                </td>

                <td className="py-2">
                  <input
                    aria-label="Line narration"
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
                      aria-label="Debit amount"
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
                      aria-label="Credit amount"
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
                    aria-label="Remove line"
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
              <td colSpan={3}>Total</td>
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
          + Add row
        </button>

        <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-center">
          <span
            className={clsx(
              'inline-flex items-center gap-2 rounded-lg px-3 py-1.5 text-sm font-medium',
              totals.isBalanced
                ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-500/12 dark:text-emerald-200'
                : 'bg-amber-50 text-amber-900 dark:bg-amber-500/12 dark:text-amber-200',
            )}
          >
            <span
              aria-hidden="true"
              className={clsx(
                'size-2 shrink-0 rounded-full',
                totals.isBalanced ? 'bg-emerald-500' : 'animate-breathe bg-amber-500',
              )}
            />
            {totals.isBalanced
              ? 'Balanced'
              : `Difference ${money(Math.abs(totals.difference))} ${
                  totals.difference > 0 ? '(debit heavy)' : '(credit heavy)'
                }`}
          </span>

          <button
            type="button"
            disabled={!canSubmit || post.isPending}
            onClick={() => post.mutate()}
            className="btn-primary"
          >
            {post.isPending && <Spinner />}
            {post.isPending ? 'Posting…' : 'Post voucher'}
          </button>
        </div>
      </div>

      <p className="text-xs text-ink-muted">
        Every debit has a corresponding credit. The voucher number is issued by the
        branch&apos;s numbering series when it posts.
      </p>
    </section>
  );
}
