import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
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
    <section className="space-y-4">
      <h1 className="text-xl font-semibold">Voucher entry</h1>

      {posted && (
        <p className="rounded-lg border border-emerald-300 bg-emerald-50 px-4 py-3 text-sm text-emerald-900 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-200">
          Posted <strong>{posted.number}</strong> for {money(posted.totalDebit)}.
        </p>
      )}

      {post.isError && (
        <div
          role="alert"
          className="rounded-lg border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200"
        >
          <p className="font-medium">{post.error.code}</p>
          <p>{post.error.detail}</p>
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-4">
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

      <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800">
        <table className="w-full border-collapse text-sm">
          <thead className="bg-slate-100 dark:bg-slate-800">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">Dr / Cr</th>
              <th className="px-3 py-2 text-start font-semibold">Ledger</th>
              <th className="px-3 py-2 text-start font-semibold">Line narration</th>
              <th className="px-3 py-2 text-end font-semibold">Debit</th>
              <th className="px-3 py-2 text-end font-semibold">Credit</th>
              <th className="w-10" />
            </tr>
          </thead>

          <tbody>
            {lines.map((line) => (
              <tr
                key={line.key}
                className="border-t border-slate-200 dark:border-slate-800"
              >
                <td className="px-3 py-2">
                  <select
                    aria-label="Debit or credit"
                    className="field-input"
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

                <td className="px-3 py-2">
                  <select
                    aria-label="Ledger"
                    className="field-input"
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

                <td className="px-3 py-2">
                  <input
                    aria-label="Line narration"
                    className="field-input"
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
                <td className="px-3 py-2">
                  {line.side === DEBIT ? (
                    <input
                      aria-label="Debit amount"
                      type="number"
                      step="0.01"
                      min="0"
                      className="field-input text-end font-mono"
                      value={line.amount}
                      onChange={(e) => update(line.key, { amount: e.target.value })}
                    />
                  ) : (
                    <span className="block text-end text-slate-300">—</span>
                  )}
                </td>

                <td className="px-3 py-2">
                  {line.side === CREDIT ? (
                    <input
                      aria-label="Credit amount"
                      type="number"
                      step="0.01"
                      min="0"
                      className="field-input text-end font-mono"
                      value={line.amount}
                      onChange={(e) => update(line.key, { amount: e.target.value })}
                    />
                  ) : (
                    <span className="block text-end text-slate-300">—</span>
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
                    className="rounded px-2 py-1 text-slate-400 hover:bg-slate-100 disabled:opacity-30 dark:hover:bg-slate-800"
                  >
                    ✕
                  </button>
                </td>
              </tr>
            ))}
          </tbody>

          <tfoot className="border-t-2 border-slate-300 bg-slate-100 font-semibold dark:border-slate-700 dark:bg-slate-800">
            <tr>
              <td className="px-3 py-2" colSpan={3}>
                Total
              </td>
              <td className="cell-numeric">{money(totals.debit)}</td>
              <td className="cell-numeric">{money(totals.credit)}</td>
              <td />
            </tr>
          </tfoot>
        </table>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-4">
        <button
          type="button"
          onClick={() => setLines((current) => [...current, emptyLine()])}
          className="rounded-lg border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50 dark:border-slate-700 dark:hover:bg-slate-800"
        >
          + Add row
        </button>

        <div className="flex items-center gap-4">
          <span
            className={clsx(
              'rounded-lg px-3 py-1.5 text-sm font-medium',
              totals.isBalanced
                ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200'
                : 'bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200',
            )}
          >
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
            {post.isPending ? 'Posting…' : 'Post voucher'}
          </button>
        </div>
      </div>

      <p className="text-xs text-slate-500 dark:text-slate-400">
        Every debit has a corresponding credit. The voucher number is issued by the
        branch&apos;s numbering series when it posts.
      </p>
    </section>
  );
}
