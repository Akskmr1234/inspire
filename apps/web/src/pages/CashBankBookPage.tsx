import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import clsx from 'clsx';
import { DateRangeControls, ReportFrame } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';
import { money, moneyAlways as balance } from '@/lib/money';
import {
  HierarchicalAccountsTable,
  type ReportColumn,
} from '@/components/HierarchicalAccountsTable';
import { buildCashBankBookHierarchy } from '@/lib/hierarchicalReports';

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
 * Formatted as a unified hierarchical report:
 * Account Head (Assets) → Group (Cash / Bank Accounts) → Ledger/Individual Account → Transactions/Details.
 * Supports expand/collapse with subtotals at all levels and grand total.
 */
export function CashBankBookPage({
  book,
}: {
  readonly book: 'cash-book' | 'bank-book';
}): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [from, setFrom] = useState(startOfMonth());
  const [to, setTo] = useState(today());
  const [range, setRange] = useState({ from: startOfMonth(), to: today() });
  const [viewMode, setViewMode] = useState<'hierarchy' | 'cards'>('hierarchy');

  const query = useQuery<CashBankBook, ApiError>({
    queryKey: [book, range.from, range.to],
    queryFn: () =>
      request<CashBankBook>(
        `/accounting/reports/${book}?from=${range.from}&to=${range.to}`,
      ),
  });

  const columns = useMemo<readonly ReportColumn[]>(
    () => [
      { key: 'openingBalance', header: t('reports.openingBalance'), align: 'end' },
      { key: 'receipts', header: t('reports.receipts'), align: 'end', blankZero: true },
      { key: 'payments', header: t('reports.payments'), align: 'end', blankZero: true },
      { key: 'closingBalance', header: t('reports.closingBalance'), align: 'end' },
    ],
    [t],
  );

  const controls = (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <DateRangeControls
        from={from}
        to={to}
        onFromChange={setFrom}
        onToChange={setTo}
        onApply={() => setRange({ from, to })}
        busy={query.isFetching}
      />

      <div className="flex items-center rounded-lg border border-line bg-surface-2 p-0.5 text-xs">
        <button
          type="button"
          onClick={() => setViewMode('hierarchy')}
          className={clsx(
            'flex items-center gap-1.5 rounded-md px-2.5 py-1 font-medium transition',
            viewMode === 'hierarchy'
              ? 'bg-surface text-ink shadow-2xs font-semibold'
              : 'text-ink-muted hover:text-ink',
          )}
        >
          <span>🌳</span>
          <span>{t('reports.hierarchicalTree') || 'Hierarchical Tree'}</span>
        </button>
        <button
          type="button"
          onClick={() => setViewMode('cards')}
          className={clsx(
            'flex items-center gap-1.5 rounded-md px-2.5 py-1 font-medium transition',
            viewMode === 'cards'
              ? 'bg-surface text-ink shadow-2xs font-semibold'
              : 'text-ink-muted hover:text-ink',
          )}
        >
          <span>📋</span>
          <span>{t('reports.accountsView') || 'Account Cards'}</span>
        </button>
      </div>
    </div>
  );

  return (
    <ReportFrame
      title={t(book === 'cash-book' ? 'nav.cashBook' : 'nav.bankBook')}
      controls={controls}
      query={query}
      isEmpty={(data) => data.accounts.length === 0}
    >
      {(data) => {
        const heads = buildCashBankBookHierarchy(data.accounts, book);
        const grandTotals = {
          openingBalance: data.totalOpeningBalance,
          receipts: data.totalReceipts,
          payments: data.totalPayments,
          closingBalance: data.totalClosingBalance,
        };

        if (viewMode === 'hierarchy') {
          return (
            <HierarchicalAccountsTable
              heads={heads}
              columns={columns}
              currency={data.currency}
              fromDate={range.from}
              toDate={range.to}
              initialExpandLevel={3}
              grandTotals={grandTotals}
              onVoucherClick={(vNum) => {
                navigate(`/accounting/voucher-report?posted=${encodeURIComponent(vNum)}`);
              }}
            />
          );
        }

        return (
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
                          <td className="py-1 whitespace-nowrap">
                            <button
                              type="button"
                              onClick={() =>
                                navigate(
                                  `/accounting/voucher-report?posted=${encodeURIComponent(line.voucherNumber)}`,
                                )
                              }
                              className="font-mono font-medium text-primary-600 dark:text-primary-400 hover:underline"
                            >
                              {line.voucherNumber}
                            </button>
                          </td>
                          <td className="py-1">
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
        );
      }}
    </ReportFrame>
  );
}
