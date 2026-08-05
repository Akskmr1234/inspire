import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ReportFrame } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';

interface BookLine {
  readonly date: string;
  readonly voucherId: string;
  readonly voucherNumber: string;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly contraLedgerNames: readonly string[];
  readonly debit: number;
  readonly credit: number;
  readonly runningBalance: number;
}

interface BookAccount {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly openingBalance: number;
  readonly closingBalance: number;
  readonly totalReceipts: number;
  readonly totalPayments: number;
  readonly lines: readonly BookLine[];
}

interface CashBankBook {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly accounts: readonly BookAccount[];
  readonly totalOpeningBalance: number;
  readonly totalClosingBalance: number;
  readonly totalReceipts: number;
  readonly totalPayments: number;
}

/** Formats a figure, blanking zero so the eye follows the numbers. */
function money(value: number): string {
  return value === 0
    ? ''
    : value.toLocaleString(undefined, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      });
}

/** Formats a balance, which is worth showing even when it is zero. */
function balance(value: number): string {
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function startOfMonth(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`;
}

/**
 * The cash book and the bank book.
 *
 * One component for both. They are the same report over a different set of
 * accounts, and giving each its own screen would mean maintaining the running
 * balance and the receipt/payment columns in two places.
 */
export function CashBankBookPage({
  book,
}: {
  readonly book: 'cash-book' | 'bank-book';
}): React.JSX.Element {
  const { t } = useTranslation();
  const [from, setFrom] = useState(startOfMonth());
  const [to, setTo] = useState(today());
  const [range, setRange] = useState({ from: startOfMonth(), to: today() });

  const query = useQuery<CashBankBook, ApiError>({
    queryKey: [book, range.from, range.to],
    queryFn: () =>
      request<CashBankBook>(
        `/accounting/reports/${book}?from=${range.from}&to=${range.to}`,
      ),
  });

  const controls = (
    <form
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setRange({ from, to });
      }}
    >
      <label className="flex flex-col gap-1 text-sm">
        <span className="text-slate-600 dark:text-slate-400">{t('reports.from')}</span>
        <input
          type="date"
          value={from}
          onChange={(event) => setFrom(event.target.value)}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
        />
      </label>

      <label className="flex flex-col gap-1 text-sm">
        <span className="text-slate-600 dark:text-slate-400">{t('reports.to')}</span>
        <input
          type="date"
          value={to}
          onChange={(event) => setTo(event.target.value)}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
        />
      </label>

      <button
        type="submit"
        disabled={query.isFetching}
        className="rounded-md bg-sky-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-sky-700 disabled:opacity-60"
      >
        {query.isFetching ? t('reports.running') : t('reports.run')}
      </button>
    </form>
  );

  return (
    <ReportFrame
      title={t(book === 'cash-book' ? 'nav.cashBook' : 'nav.bankBook')}
      controls={controls}
      query={query}
      isEmpty={(data) => data.accounts.length === 0}
    >
      {(data) => (
        <div className="space-y-8">
          {data.accounts.map((account) => (
            <section key={account.ledgerId} className="space-y-2">
              <header className="flex flex-wrap items-baseline justify-between gap-2">
                <h2 className="font-semibold">
                  <span className="text-slate-500 dark:text-slate-500">
                    {account.ledgerCode}
                  </span>{' '}
                  {account.ledgerName}
                </h2>
                <p className="text-sm text-slate-600 dark:text-slate-400">
                  {t('reports.closingBalance')}: {balance(account.closingBalance)}{' '}
                  {data.currency}
                </p>
              </header>

              <div className="overflow-x-auto">
                <table className="w-full min-w-[52rem] border-collapse text-sm">
                  <thead>
                    <tr className="border-b border-slate-300 text-left dark:border-slate-700">
                      <th className="py-2 pe-3 font-medium">{t('reports.date')}</th>
                      <th className="py-2 pe-3 font-medium">{t('reports.voucherNo')}</th>
                      <th className="py-2 pe-3 font-medium">
                        {t('reports.particulars')}
                      </th>
                      <th className="py-2 pe-3 text-end font-medium">
                        {t('reports.receipts')}
                      </th>
                      <th className="py-2 pe-3 text-end font-medium">
                        {t('reports.payments')}
                      </th>
                      <th className="py-2 text-end font-medium">
                        {t('reports.balance')}
                      </th>
                    </tr>
                  </thead>

                  <tbody>
                    <tr className="border-b border-slate-200 text-slate-600 dark:border-slate-800 dark:text-slate-400">
                      <td className="py-1 pe-3" colSpan={5}>
                        {t('reports.openingBalance')}
                      </td>
                      <td className="py-1 text-end tabular-nums">
                        {balance(account.openingBalance)}
                      </td>
                    </tr>

                    {account.lines.map((line) => (
                      <tr
                        key={`${line.voucherId}-${line.runningBalance}`}
                        className="border-b border-slate-100 dark:border-slate-900"
                      >
                        <td className="py-1 pe-3 text-slate-600 dark:text-slate-400">
                          {line.date}
                        </td>
                        <td className="py-1 pe-3">{line.voucherNumber}</td>
                        <td className="py-1 pe-3">
                          {/*
                            The contra ledgers are what make a cash book readable.
                            "Cash 500.00" says nothing; "Cash 500.00 — Sales Account"
                            says what the money was for.
                          */}
                          {line.contraLedgerNames.join(', ')}
                          {line.narration ? (
                            <span className="text-slate-500 dark:text-slate-500">
                              {' '}
                              — {line.narration}
                            </span>
                          ) : null}
                        </td>
                        <td className="py-1 pe-3 text-end tabular-nums">
                          {money(line.debit)}
                        </td>
                        <td className="py-1 pe-3 text-end tabular-nums">
                          {money(line.credit)}
                        </td>
                        <td className="py-1 text-end tabular-nums">
                          {balance(line.runningBalance)}
                        </td>
                      </tr>
                    ))}
                  </tbody>

                  <tfoot>
                    <tr className="border-t-2 border-slate-400 font-semibold dark:border-slate-600">
                      <td className="py-2 pe-3" colSpan={3}>
                        {t('reports.totals')}
                      </td>
                      <td className="py-2 pe-3 text-end tabular-nums">
                        {money(account.totalReceipts)}
                      </td>
                      <td className="py-2 pe-3 text-end tabular-nums">
                        {money(account.totalPayments)}
                      </td>
                      <td className="py-2 text-end tabular-nums">
                        {balance(account.closingBalance)}
                      </td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            </section>
          ))}

          {data.accounts.length > 1 && (
            <div className="rounded-lg border border-slate-300 px-4 py-3 text-sm dark:border-slate-700">
              <p className="font-semibold">{t('reports.allAccounts')}</p>
              <dl className="mt-2 grid grid-cols-2 gap-x-6 gap-y-1 sm:grid-cols-4">
                <dt className="text-slate-600 dark:text-slate-400">
                  {t('reports.openingBalance')}
                </dt>
                <dd className="text-end tabular-nums">
                  {balance(data.totalOpeningBalance)}
                </dd>
                <dt className="text-slate-600 dark:text-slate-400">
                  {t('reports.receipts')}
                </dt>
                <dd className="text-end tabular-nums">{balance(data.totalReceipts)}</dd>
                <dt className="text-slate-600 dark:text-slate-400">
                  {t('reports.payments')}
                </dt>
                <dd className="text-end tabular-nums">{balance(data.totalPayments)}</dd>
                <dt className="text-slate-600 dark:text-slate-400">
                  {t('reports.closingBalance')}
                </dt>
                <dd className="text-end font-semibold tabular-nums">
                  {balance(data.totalClosingBalance)}
                </dd>
              </dl>
            </div>
          )}
        </div>
      )}
    </ReportFrame>
  );
}
