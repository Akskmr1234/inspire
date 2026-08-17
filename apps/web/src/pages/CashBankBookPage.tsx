import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DateRangeControls, ReportFrame } from '@/components/ReportFrame';
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
    <DateRangeControls
      from={from}
      to={to}
      onFromChange={setFrom}
      onToChange={setTo}
      onApply={() => setRange({ from, to })}
      busy={query.isFetching}
    />
  );

  return (
    <ReportFrame
      title={t(book === 'cash-book' ? 'nav.cashBook' : 'nav.bankBook')}
      controls={controls}
      query={query}
      isEmpty={(data) => data.accounts.length === 0}
    >
      {(data) => (
        <div className="space-y-6">
          {data.accounts.map((account) => (
            <section key={account.ledgerId} className="card overflow-hidden">
              <header className="flex flex-wrap items-baseline justify-between gap-2 border-b border-line bg-surface-3 px-4 py-3">
                <h2 className="font-semibold text-ink">
                  <span className="text-ink-subtle">{account.ledgerCode}</span>{' '}
                  {account.ledgerName}
                </h2>
                <p className="text-sm text-ink-muted">
                  {t('reports.closingBalance')}:{' '}
                  <span className="font-mono font-medium text-ink tabular-nums">
                    {balance(account.closingBalance)} {data.currency}
                  </span>
                </p>
              </header>

              <div className="overflow-x-auto">
                <table className="table min-w-[52rem]">
                  <thead>
                    <tr>
                      <th className="text-start">{t('reports.date')}</th>
                      <th className="text-start">{t('reports.voucherNo')}</th>
                      <th className="text-start">{t('reports.particulars')}</th>
                      <th className="text-end">{t('reports.receipts')}</th>
                      <th className="text-end">{t('reports.payments')}</th>
                      <th className="text-end">{t('reports.balance')}</th>
                    </tr>
                  </thead>

                  <tbody>
                    <tr className="text-ink-muted">
                      <td colSpan={5}>{t('reports.openingBalance')}</td>
                      <td className="cell-numeric">{balance(account.openingBalance)}</td>
                    </tr>

                    {account.lines.map((line) => (
                      <tr key={`${line.voucherId}-${line.runningBalance}`}>
                        <td className="py-1 text-ink-muted whitespace-nowrap">
                          {line.date}
                        </td>
                        <td className="py-1 whitespace-nowrap">{line.voucherNumber}</td>
                        <td className="py-1">
                          {/*
                            The contra ledgers are what make a cash book readable.
                            "Cash 500.00" says nothing; "Cash 500.00 — Sales Account"
                            says what the money was for.
                          */}
                          {line.contraLedgerNames.join(', ')}
                          {line.narration ? (
                            <span className="text-ink-subtle"> — {line.narration}</span>
                          ) : null}
                        </td>
                        <td className="cell-numeric py-1">{money(line.debit)}</td>
                        <td className="cell-numeric py-1">{money(line.credit)}</td>
                        <td className="cell-numeric py-1">
                          {balance(line.runningBalance)}
                        </td>
                      </tr>
                    ))}
                  </tbody>

                  <tfoot>
                    <tr>
                      <td colSpan={3}>{t('reports.totals')}</td>
                      <td className="cell-numeric">{money(account.totalReceipts)}</td>
                      <td className="cell-numeric">{money(account.totalPayments)}</td>
                      <td className="cell-numeric">{balance(account.closingBalance)}</td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            </section>
          ))}

          {data.accounts.length > 1 && (
            <div className="panel text-sm">
              <p className="font-semibold text-ink">{t('reports.allAccounts')}</p>
              <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-1.5 sm:grid-cols-4">
                <dt className="text-ink-muted">{t('reports.openingBalance')}</dt>
                <dd className="text-end font-mono tabular-nums">
                  {balance(data.totalOpeningBalance)}
                </dd>
                <dt className="text-ink-muted">{t('reports.receipts')}</dt>
                <dd className="text-end font-mono tabular-nums">
                  {balance(data.totalReceipts)}
                </dd>
                <dt className="text-ink-muted">{t('reports.payments')}</dt>
                <dd className="text-end font-mono tabular-nums">
                  {balance(data.totalPayments)}
                </dd>
                <dt className="text-ink-muted">{t('reports.closingBalance')}</dt>
                <dd className="text-end font-mono font-semibold tabular-nums">
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
