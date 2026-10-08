import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import clsx from 'clsx';
import { BalanceBadge, ReportFrame, Spinner } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';
import { money } from '@/lib/money';
import { SearchSelect } from '@/components/SearchSelect';
import {
  HierarchicalAccountsTable,
  type ReportColumn,
} from '@/components/HierarchicalAccountsTable';
import { buildDayBookHierarchy } from '@/lib/hierarchicalReports';
import { listLedgers, type LedgerSummary } from '@/lib/ledgers';

interface DayBookLine {
  readonly ledgerId: string;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly narration: string | null;
  readonly debit: number;
  readonly credit: number;
}

interface DayBookEntry {
  readonly voucherId: string;
  readonly date: string;
  readonly voucherNumber: string;
  readonly voucherType: string;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly amount: number;
  readonly lines: readonly DayBookLine[];
}

interface DayBook {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly totalDebit: number;
  readonly totalCredit: number;
  readonly voucherCount: number;
  readonly entries: readonly DayBookEntry[];
}

const VOUCHER_TYPES = [
  'CashReceipt',
  'BankReceipt',
  'CashPayment',
  'BankPayment',
  'Journal',
  'Contra',
] as const;

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function startOfMonth(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`;
}

/**
 * The day book: every voucher posted in a period, formatted as a unified hierarchical report:
 * Account Head → Group → Ledger/Individual Account → Transactions/Details.
 * Supports expand/collapse with subtotals at all levels and grand total.
 */
export function DayBookPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [from, setFrom] = useState(startOfMonth());
  const [to, setTo] = useState(today());
  const [voucherType, setVoucherType] = useState('');
  const [criteria, setCriteria] = useState({
    from: startOfMonth(),
    to: today(),
    voucherType: '',
  });
  const [viewMode, setViewMode] = useState<'hierarchy' | 'chronological'>('hierarchy');

  const query = useQuery<DayBook, ApiError>({
    queryKey: ['day-book', criteria.from, criteria.to, criteria.voucherType],
    queryFn: () => {
      const params = new URLSearchParams({ from: criteria.from, to: criteria.to });

      if (criteria.voucherType) {
        params.set('voucherType', criteria.voucherType);
      }

      return request<DayBook>(`/accounting/reports/day-book?${params.toString()}`);
    },
  });

  const ledgersQuery = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => listLedgers(false),
    staleTime: 5 * 60 * 1000,
  });

  const ledgersMap = useMemo(() => {
    const map = new Map<string, LedgerSummary>();
    (ledgersQuery.data ?? []).forEach((l) => {
      map.set(l.ledgerId, l);
      map.set(l.code, l);
    });
    return map;
  }, [ledgersQuery.data]);

  const columns = useMemo<readonly ReportColumn[]>(
    () => [
      { key: 'debit', header: t('reports.debit') || 'Debit', align: 'end', blankZero: true },
      { key: 'credit', header: t('reports.credit') || 'Credit', align: 'end', blankZero: true },
    ],
    [t],
  );

  const controls = (
    <form
      className="toolbar flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setCriteria({ from, to, voucherType });
      }}
    >
      <label className="field">
        <span className="field-label">{t('reports.from')}</span>
        <input
          type="date"
          value={from}
          onChange={(event) => setFrom(event.target.value)}
          className="field-input-sm"
        />
      </label>

      <label className="field">
        <span className="field-label">{t('reports.to')}</span>
        <input
          type="date"
          value={to}
          onChange={(event) => setTo(event.target.value)}
          className="field-input-sm"
        />
      </label>

      <label className="field min-w-44">
        <span className="field-label">{t('reports.voucherType')}</span>
        <SearchSelect
          value={voucherType}
          onChange={setVoucherType}
          clearable
          size="sm"
          label={t('reports.voucherType')}
          placeholder={t('reports.allTypes')}
          options={VOUCHER_TYPES.map((type) => ({
            value: type,
            label: t(`voucherTypes.${type}`),
          }))}
        />
      </label>

      <button type="submit" disabled={query.isFetching} className="btn-primary btn-sm">
        {query.isFetching && <Spinner />}
        {query.isFetching ? t('reports.running') : t('reports.run')}
      </button>

      <div className="inline-flex rounded-lg bg-surface-2 p-1 border border-line ms-auto">
        <button
          type="button"
          onClick={() => setViewMode('hierarchy')}
          className={clsx(
            'rounded-md px-2.5 py-1 text-xs font-semibold transition',
            viewMode === 'hierarchy'
              ? 'bg-surface text-ink shadow-2xs'
              : 'text-ink-muted hover:text-ink',
          )}
        >
          {t('reports.hierarchicalView') || 'Hierarchical (Head → Group → Ledger)'}
        </button>
        <button
          type="button"
          onClick={() => setViewMode('chronological')}
          className={clsx(
            'rounded-md px-2.5 py-1 text-xs font-semibold transition',
            viewMode === 'chronological'
              ? 'bg-surface text-ink shadow-2xs'
              : 'text-ink-muted hover:text-ink',
          )}
        >
          {t('reports.chronologicalView') || 'Chronological Register'}
        </button>
      </div>
    </form>
  );

  return (
    <ReportFrame
      title={t('nav.dayBook')}
      controls={controls}
      query={query}
      isEmpty={(data) => data.entries.length === 0}
    >
      {(data) => {
        const heads = buildDayBookHierarchy(data.entries, ledgersMap);
        const grandTotals = {
          debit: data.totalDebit,
          credit: data.totalCredit,
        };

        if (viewMode === 'chronological') {
          return (
            <div className="space-y-4">
              <p className="text-sm text-ink-muted">
                {t('reports.voucherCount', { count: data.voucherCount })} · {data.currency}
              </p>

              <div className="table-wrap table-wrap-tall card overflow-hidden border border-line">
                <table className="table min-w-[56rem] text-xs">
                  <thead>
                    <tr className="bg-surface-3 text-ink-muted font-bold uppercase text-[11px] border-b border-line">
                      <th className="py-2.5 px-3 text-start">{t('reports.date')}</th>
                      <th className="py-2.5 px-3 text-start">{t('reports.voucherNo')}</th>
                      <th className="py-2.5 px-3 text-start">{t('reports.ledger')}</th>
                      <th className="py-2.5 px-3 text-start">{t('reports.particulars')}</th>
                      <th className="py-2.5 px-3 text-end">{t('reports.debit')}</th>
                      <th className="py-2.5 px-3 text-end">{t('reports.credit')}</th>
                    </tr>
                  </thead>

                  {data.entries.map((entry) => (
                    <tbody
                      key={entry.voucherId}
                      className="border-t border-line align-top transition-colors hover:bg-surface-2/60"
                    >
                      {entry.lines.map((line, index) => (
                        <tr
                          key={`${entry.voucherId}-${line.ledgerId}-${index}`}
                          className="border-t-0"
                        >
                          <td className="py-1 px-3 text-ink-muted whitespace-nowrap font-mono">
                            {index === 0 ? entry.date : ''}
                          </td>
                          <td className="py-1 px-3">
                            {index === 0 ? (
                              <button
                                type="button"
                                onClick={() =>
                                  navigate(`/accounting/voucher-report?posted=${encodeURIComponent(entry.voucherNumber)}`)
                                }
                                className="font-semibold text-primary-600 dark:text-primary-400 hover:underline whitespace-nowrap font-mono"
                              >
                                {entry.voucherNumber}
                              </button>
                            ) : (
                              ''
                            )}
                          </td>
                          <td className="py-1 px-3">
                            <span className="text-primary-700 dark:text-primary-400 font-mono font-medium">{line.ledgerCode}</span>{' '}
                            <span className="text-ink">{line.ledgerName}</span>
                          </td>
                          <td className="py-1 px-3 text-ink-muted">
                            {index === 0
                              ? (line.narration ?? entry.narration ?? '—')
                              : (line.narration ?? '—')}
                          </td>
                          <td className="cell-numeric py-1 px-3 font-mono">{money(line.debit)}</td>
                          <td className="cell-numeric py-1 px-3 font-mono">{money(line.credit)}</td>
                        </tr>
                      ))}
                    </tbody>
                  ))}

                  <tfoot className="border-t-2 border-line-strong bg-surface-3 font-bold">
                    <tr>
                      <td colSpan={4} className="py-2.5 px-3 uppercase tracking-wider">{t('reports.totals')}</td>
                      <td className="cell-numeric py-2.5 px-3 font-mono">{money(data.totalDebit)}</td>
                      <td className="cell-numeric py-2.5 px-3 font-mono">{money(data.totalCredit)}</td>
                    </tr>
                  </tfoot>
                </table>
              </div>

              <BalanceBadge
                isBalanced={data.totalDebit === data.totalCredit}
                currency={data.currency}
              />
            </div>
          );
        }

        return (
          <HierarchicalAccountsTable
            heads={heads}
            columns={columns}
            currency={data.currency}
            isBalanced={data.totalDebit === data.totalCredit}
            grandTotals={grandTotals}
            onVoucherClick={(vNum) => {
              navigate(`/accounting/voucher-report?posted=${encodeURIComponent(vNum)}`);
            }}
          />
        );
      }}
    </ReportFrame>
  );
}
