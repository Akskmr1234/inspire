import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { DateRangeControls, EmptyState, ReportFrame } from '@/components/ReportFrame';
import { SearchSelect } from '@/components/SearchSelect';
import type { ApiError } from '@/lib/api';
import {
  fetchLedgerStatement,
  listLedgers,
  type LedgerStatementLine,
  type LedgerStatementResponse,
  type LedgerSummary,
} from '@/lib/ledgers';
import { moneyAlways } from '@/lib/money';

/** The voucher types, keyed by the wire value, for the `voucherTypes.<name>` lookup. */
const TYPE_NAME: Record<number, string> = {
  1: 'CashReceipt',
  2: 'BankReceipt',
  3: 'CashPayment',
  4: 'BankPayment',
  5: 'Journal',
  6: 'Contra',
  7: 'OpeningBalance',
};

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function startOfYear(): string {
  return `${new Date().getFullYear()}-01-01`;
}

/** A balance as an accountant reads it: the amount, and Dr or Cr for its side. */
export function drCr(value: number, dr: string, cr: string): string {
  if (Math.abs(value) < 0.005) return moneyAlways(0);
  return `${moneyAlways(Math.abs(value))} ${value > 0 ? dr : cr}`;
}

/**
 * The statement of one ledger: what stood on it at the start, every posting in the
 * period with the account on the other side, and the running balance after each.
 *
 * The report a customer is sent, an auditor ticks and a bookkeeper reconciles against a
 * bank statement. The ledger and period are in the address, so the chart of accounts
 * can open a ledger's statement directly and a link to one can be passed on.
 */
export function LedgerStatementPage(): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const ledgerId = params.get('ledger') ?? '';
  const range = {
    from: params.get('from') ?? startOfYear(),
    to: params.get('to') ?? today(),
  };
  const [from, setFrom] = useState(range.from);
  const [to, setTo] = useState(range.to);

  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers', 'all'],
    queryFn: () => listLedgers(false),
    staleTime: 60 * 1000,
  });

  const statement = useQuery<LedgerStatementResponse | null, ApiError>({
    queryKey: ['ledger-statement', ledgerId, range.from, range.to],
    queryFn: () =>
      ledgerId
        ? fetchLedgerStatement(ledgerId, range.from, range.to)
        : Promise.resolve(null),
  });

  const apply = (next: { ledger?: string; from?: string; to?: string }): void => {
    const merged = { ledger: ledgerId, from, to, ...next };
    setParams(
      Object.fromEntries(Object.entries(merged).filter(([, value]) => value !== '')),
      { replace: true },
    );
  };

  const options = useMemo(
    () =>
      (ledgers.data ?? []).map((ledger) => ({
        value: ledger.ledgerId,
        label: `${ledger.code} — ${ledger.name}`,
      })),
    [ledgers.data],
  );

  const dr = t('reports.dr');
  const cr = t('reports.cr');

  const columns = useMemo<readonly GridColumn<LedgerStatementLine>[]>(
    () => [
      { key: 'date', header: t('reports.date'), value: (line) => line.date },
      {
        key: 'voucher',
        header: t('statement.voucher'),
        value: (line) => line.voucherNumber,
        render: (line) => (
          <button
            type="button"
            className="font-mono text-brand-700 hover:underline dark:text-brand-300"
            onClick={() =>
              navigate(
                `/accounting/voucher-report?posted=${encodeURIComponent(line.voucherNumber)}`,
              )
            }
          >
            {line.voucherNumber}
          </button>
        ),
      },
      {
        key: 'type',
        header: t('statement.type'),
        value: (line) => {
          const name = TYPE_NAME[Number(line.voucherType)];
          return name
            ? t(`voucherTypes.${name}`, { defaultValue: name })
            : String(line.voucherType);
        },
        hiddenByDefault: true,
      },
      {
        key: 'particulars',
        header: t('statement.particulars'),
        value: (line) => line.contraLedgerNames.join(', '),
        render: (line) => (
          <span>
            <span className="text-ink">{line.contraLedgerNames.join(', ') || '—'}</span>
            {line.narration && (
              <span className="block text-xs text-ink-muted">{line.narration}</span>
            )}
          </span>
        ),
        wide: true,
      },
      {
        key: 'reference',
        header: t('statement.reference'),
        value: (line) => line.referenceNumber ?? '',
        hiddenByDefault: true,
      },
      {
        key: 'debit',
        header: t('reports.debit'),
        value: (line) => line.debit,
        render: (line) => (line.debit ? moneyAlways(line.debit) : ''),
        numeric: true,
      },
      {
        key: 'credit',
        header: t('reports.credit'),
        value: (line) => line.credit,
        render: (line) => (line.credit ? moneyAlways(line.credit) : ''),
        numeric: true,
      },
      {
        key: 'balance',
        header: t('statement.balance'),
        value: (line) => line.runningBalance,
        render: (line) => drCr(line.runningBalance, dr, cr),
        numeric: true,
      },
    ],
    [t, navigate, dr, cr],
  );

  const controls = (
    <div className="flex flex-col gap-3 lg:flex-row lg:items-end">
      <label className="block min-w-0 lg:w-80">
        <span className="field-label">{t('reports.ledger')}</span>
        <SearchSelect
          value={ledgerId}
          onChange={(value) => apply({ ledger: value })}
          options={options}
          label={t('reports.ledger')}
          placeholder={
            ledgers.isPending ? t('common.loading') : t('statement.chooseLedger')
          }
          disabled={ledgers.isPending}
        />
      </label>
      <DateRangeControls
        from={from}
        to={to}
        onFromChange={setFrom}
        onToChange={setTo}
        onApply={() => apply({})}
        busy={statement.isFetching}
      />
    </div>
  );

  return (
    <ReportFrame
      title={t('statement.title')}
      {...(statement.data ? { subtitle: statement.data.ledgerName } : {})}
      controls={controls}
      query={statement}
    >
      {(data) =>
        data === null ? (
          <EmptyState message={t('statement.pickLedger')} />
        ) : (
          <div className="space-y-3">
            <dl className="grid grid-cols-2 gap-3 text-sm lg:grid-cols-4">
              <Figure
                label={t('statement.ledger')}
                value={`${data.ledgerCode} · ${data.groupName}`}
              />
              <Figure
                label={t('reports.openingBalance')}
                value={drCr(data.openingBalance, dr, cr)}
                numeric
              />
              <Figure
                label={t('statement.movement')}
                value={`${moneyAlways(data.totalDebit)} ${dr} · ${moneyAlways(data.totalCredit)} ${cr}`}
                numeric
              />
              <Figure
                label={t('reports.closingBalance')}
                value={`${drCr(data.closingBalance, dr, cr)} ${data.currency}`}
                numeric
                strong
              />
            </dl>
            <DataGrid
              gridKey="ledger-statement"
              rows={data.lines}
              columns={columns}
              rowKey={(line) => `${line.voucherId}-${line.date}-${line.runningBalance}`}
              emptyMessage={t('statement.noPostings')}
            />
          </div>
        )
      }
    </ReportFrame>
  );
}

function Figure({
  label,
  value,
  numeric = false,
  strong = false,
}: {
  readonly label: string;
  readonly value: string;
  readonly numeric?: boolean;
  readonly strong?: boolean;
}): React.JSX.Element {
  return (
    <div className="panel px-3 py-2">
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd
        className={
          (numeric ? 'font-mono tabular-nums ' : '') +
          (strong ? 'font-semibold text-ink' : 'text-ink')
        }
      >
        {value}
      </dd>
    </div>
  );
}
