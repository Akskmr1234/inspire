import { useState } from 'react';
import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { ReportFrame, ReportSkeleton, moneyAlways } from '@/components/ReportFrame';
import type { ApiError } from '@/lib/api';
import {
  componentName,
  getInputTax,
  getOutputTax,
  getTaxSummary,
  TaxRegime,
  type InputTaxReport,
  type OutputTaxReport,
  type TaxSummaryReport,
} from '@/lib/taxReturns';

/** Which of the three §7.3 reports is on screen. */
type View = 'summary' | 'output' | 'input';

function monthStart(): string {
  const now = new Date();

  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`;
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * The VAT and GST returns of section 7.3.
 *
 * One screen for both regimes, because there is one set of endpoints: a Qatar firm is
 * answered in VAT and an Indian firm in CGST, SGST, IGST and cess, and neither is ever
 * shown a head it does not charge. That is what open question 1 asked for — report menus
 * filtered by regime — arrived at by the report answering honestly rather than by a
 * second screen somebody has to keep in step.
 *
 * Three reports rather than three screens, because they are one period asked three
 * questions: what was charged, what may be recovered, and what is left to pay. Running
 * them from one set of dates is also what stops somebody filing a summary for June
 * against a listing for May.
 */
export function TaxReturnsPage(): React.JSX.Element {
  const { t } = useTranslation();

  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(today());
  const [range, setRange] = useState({ from: monthStart(), to: today() });
  const [view, setView] = useState<View>('summary');

  const summary = useQuery<TaxSummaryReport, ApiError>({
    queryKey: ['tax-summary', range.from, range.to],
    queryFn: () => getTaxSummary(range.from, range.to),
  });

  const output = useQuery<OutputTaxReport, ApiError>({
    queryKey: ['output-tax', range.from, range.to],
    queryFn: () => getOutputTax(range.from, range.to),
    enabled: view === 'output',
  });

  const input = useQuery<InputTaxReport, ApiError>({
    queryKey: ['input-tax', range.from, range.to],
    queryFn: () => getInputTax(range.from, range.to),
    enabled: view === 'input',
  });

  const controls = (
    <form
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();
        setRange({ from, to });
      }}
    >
      <div>
        <label htmlFor="tax-from" className="field-label">
          {t('reports.from')}
        </label>
        <input
          id="tax-from"
          type="date"
          className="field-input"
          value={from}
          onChange={(event) => setFrom(event.target.value)}
        />
      </div>

      <div>
        <label htmlFor="tax-to" className="field-label">
          {t('reports.to')}
        </label>
        <input
          id="tax-to"
          type="date"
          className="field-input"
          value={to}
          onChange={(event) => setTo(event.target.value)}
        />
      </div>

      <button type="submit" disabled={summary.isFetching} className="btn-primary">
        {summary.isFetching ? t('reports.running') : t('reports.run')}
      </button>
    </form>
  );

  return (
    <ReportFrame title={t('nav.taxReturns')} controls={controls} query={summary}>
      {(data) => (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <ViewTab current={view} value="summary" onChange={setView}>
              {t('tax.summary')}
            </ViewTab>
            <ViewTab current={view} value="output" onChange={setView}>
              {t('tax.outputTax')}
            </ViewTab>
            <ViewTab current={view} value="input" onChange={setView}>
              {t('tax.inputTax')}
            </ViewTab>

            <span className="ms-auto text-xs text-ink-muted">
              {regimeName(data.regime, t)} · {data.currency}
            </span>
          </div>

          {view === 'summary' && <Summary data={data} />}

          {view === 'output' && (
            <Detail query={output}>{(report) => <OutputRows report={report} />}</Detail>
          )}

          {view === 'input' && (
            <Detail query={input}>{(report) => <InputRows report={report} />}</Detail>
          )}
        </div>
      )}
    </ReportFrame>
  );
}

/** What is owed, head by head, with the reconciliation the return turns on. */
function Summary({ data }: { readonly data: TaxSummaryReport }): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <div className="space-y-4">
      {/* The banner is the point of the screen. A return built from documents cannot see
          a journal somebody wrote straight into a tax account, so when the ledger and
          the documents disagree the only honest thing is to say so before it is filed. */}
      <p
        className={clsx(
          'inline-flex animate-pop items-center gap-2 rounded-lg px-3 py-1.5 text-sm font-medium',
          data.isReconciled
            ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-500/12 dark:text-emerald-200'
            : 'bg-red-50 text-red-800 dark:bg-red-500/12 dark:text-red-200',
        )}
      >
        <span
          aria-hidden="true"
          className={clsx(
            'size-2 shrink-0 rounded-full',
            data.isReconciled ? 'bg-emerald-500' : 'animate-breathe bg-red-500',
          )}
        />
        {data.isReconciled ? t('tax.reconciled') : t('tax.notReconciled')}
      </p>

      <div className="grid gap-3 sm:grid-cols-3 lg:grid-cols-5">
        <Figure label={t('tax.taxableSupplies')} value={data.taxableSupplies} />
        <Figure label={t('tax.zeroRated')} value={data.zeroRatedSupplies} />
        <Figure label={t('tax.taxablePurchases')} value={data.taxablePurchases} />
        <Figure label={t('tax.zeroRatedPurchases')} value={data.zeroRatedPurchases} />
        <Figure label={t('tax.netPayable')} value={data.netPayable} emphasis />
      </div>

      <div className="table-wrap table-wrap-tall">
        <table className="table">
          <thead className="bg-surface-3">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">{t('tax.head')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.outputTax')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.inputTax')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.netPayable')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.onLedger')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.difference')}</th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('tax.inputOnLedger')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('tax.inputDifference')}
              </th>
            </tr>
          </thead>

          <tbody>
            {data.lines.length === 0 ? (
              <tr>
                <td colSpan={8} className="px-3 py-6 text-center text-sm text-ink-muted">
                  {t('tax.nothingCharged')}
                </td>
              </tr>
            ) : (
              data.lines.map((line) => (
                <tr key={line.component} className="border-t border-line">
                  <td className="px-3 py-1.5">{componentName(line.component)}</td>
                  <td className="px-3 py-1.5 text-end font-mono">
                    {moneyAlways(line.outputTax)}
                  </td>
                  <td className="px-3 py-1.5 text-end font-mono">
                    {moneyAlways(line.inputTax)}
                  </td>
                  <td className="px-3 py-1.5 text-end font-mono font-semibold">
                    {moneyAlways(line.netPayable)}
                  </td>
                  <td className="px-3 py-1.5 text-end font-mono text-ink-muted">
                    {moneyAlways(line.outputTaxPosted)}
                  </td>
                  <td
                    className={clsx(
                      'px-3 py-1.5 text-end font-mono',
                      line.difference !== 0 && 'text-red-700 dark:text-red-400',
                    )}
                  >
                    {moneyAlways(line.difference)}
                  </td>
                  <td className="px-3 py-1.5 text-end font-mono text-ink-muted">
                    {moneyAlways(line.inputTaxPosted)}
                  </td>
                  <td
                    className={clsx(
                      'px-3 py-1.5 text-end font-mono',
                      line.inputDifference !== 0 && 'text-red-700 dark:text-red-400',
                    )}
                  >
                    {moneyAlways(line.inputDifference)}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      <p className="text-xs text-ink-muted">{t('tax.differenceHint')}</p>
    </div>
  );
}

/** The documents behind the output tax. */
function OutputRows({ report }: { readonly report: OutputTaxReport }): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <div className="table-wrap table-wrap-tall">
      <table className="table">
        <thead className="bg-surface-3">
          <tr>
            <th className="px-3 py-2 text-start font-semibold">{t('tax.document')}</th>
            <th className="px-3 py-2 text-start font-semibold">{t('sales.date')}</th>
            <th className="px-3 py-2 text-start font-semibold">{t('sales.customer')}</th>
            <th className="px-3 py-2 text-start font-semibold">
              {t('tax.registration')}
            </th>
            <th className="px-3 py-2 text-start font-semibold">{t('tax.head')}</th>
            <th className="px-3 py-2 text-end font-semibold">{t('tax.rate')}</th>
            <th className="px-3 py-2 text-end font-semibold">{t('tax.taxableValue')}</th>
            <th className="px-3 py-2 text-end font-semibold">{t('tax.tax')}</th>
          </tr>
        </thead>

        <tbody>
          {report.rows.length === 0 ? (
            <tr>
              <td colSpan={8} className="px-3 py-6 text-center text-sm text-ink-muted">
                {t('tax.noSupplies')}
              </td>
            </tr>
          ) : (
            report.rows.map((row, index) => (
              <tr
                key={`${row.documentId}-${row.component}-${index}`}
                className="border-t border-line"
              >
                <td className="px-3 py-1.5">{row.number}</td>
                <td className="px-3 py-1.5">{row.date}</td>
                <td className="px-3 py-1.5">{row.customerName}</td>
                <td className="px-3 py-1.5 text-ink-muted">
                  {row.taxRegistrationNumber ?? '—'}
                  {row.stateCode ? ` · ${row.stateCode}` : ''}
                </td>
                <td className="px-3 py-1.5">{componentName(row.component)}</td>
                <td className="px-3 py-1.5 text-end font-mono">{row.percentage}%</td>
                <td className="px-3 py-1.5 text-end font-mono">
                  {moneyAlways(row.taxableAmount)}
                </td>
                <td className="px-3 py-1.5 text-end font-mono">
                  {moneyAlways(row.taxAmount)}
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

/** The postings behind the input tax. */
function InputRows({ report }: { readonly report: InputTaxReport }): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <div className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <Figure label={t('tax.taxablePurchases')} value={report.taxablePurchases} />
        <Figure label={t('tax.zeroRatedPurchases')} value={report.zeroRatedPurchases} />
      </div>

      {/* Only where there is one, and only about the rows it applies to: a listing built
          from purchases carries its base, and a hand-written journal cannot. */}
      {report.rows.some((row) => row.taxableAmount === null) && (
        <p className="alert-warn text-xs">{t('tax.inputHint')}</p>
      )}

      <div className="table-wrap table-wrap-tall">
        <table className="table">
          <thead className="bg-surface-3">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">{t('tax.document')}</th>
              <th className="px-3 py-2 text-start font-semibold">{t('sales.date')}</th>
              <th className="px-3 py-2 text-start font-semibold">
                {t('purchase.supplier')}
              </th>
              <th className="px-3 py-2 text-start font-semibold">
                {t('purchase.supplierInvoice')}
              </th>
              <th className="px-3 py-2 text-start font-semibold">{t('tax.head')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.rate')}</th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('tax.taxableValue')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.tax')}</th>
            </tr>
          </thead>

          <tbody>
            {report.rows.length === 0 ? (
              <tr>
                <td colSpan={8} className="px-3 py-6 text-center text-sm text-ink-muted">
                  {t('tax.noInput')}
                </td>
              </tr>
            ) : (
              report.rows.map((row, index) => (
                <tr
                  key={`${row.documentId}-${row.component}-${index}`}
                  className="border-t border-line"
                >
                  <td className="px-3 py-1.5">{row.number}</td>
                  <td className="px-3 py-1.5">{row.date}</td>
                  <td className="px-3 py-1.5">
                    {/* A journal has no supplier; what it has is whatever the line said,
                        which is the only context that row carries. */}
                    {row.kind === null
                      ? (row.narration ?? t('tax.byJournal'))
                      : row.supplierName}
                  </td>
                  <td className="px-3 py-1.5">{row.supplierInvoiceNumber ?? '—'}</td>
                  <td className="px-3 py-1.5">{componentName(row.component)}</td>
                  <td className="px-3 py-1.5 text-end font-mono">
                    {row.kind === null ? '—' : `${row.percentage}%`}
                  </td>
                  <td className="px-3 py-1.5 text-end font-mono">
                    {row.taxableAmount === null ? '—' : moneyAlways(row.taxableAmount)}
                  </td>
                  <td className="px-3 py-1.5 text-end font-mono">
                    {moneyAlways(row.taxAmount)}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/** Renders a listing once it has arrived, and says so while it has not. */
function Detail<TReport>({
  query,
  children,
}: {
  readonly query: UseQueryResult<TReport, ApiError>;
  readonly children: (report: TReport) => React.ReactNode;
}): React.JSX.Element {
  // No `t` here any more: the waiting state is a skeleton shaped like the listing
  // rather than the word "Loading", so there is nothing left to translate.
  if (query.error) {
    return <p className="alert-error">{query.error.detail || query.error.code}</p>;
  }

  if (query.isLoading || !query.data) {
    return (
      <div className="space-y-4" aria-busy="true">
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-5">
          {Array.from({ length: 5 }, (_, index) => (
            <div key={index} className="card px-4 py-3">
              <span className="skeleton block h-2.5 w-3/5 rounded" />
              <span className="skeleton mt-3 block h-5 w-4/5 rounded" />
            </div>
          ))}
        </div>
        <ReportSkeleton rows={4} />
      </div>
    );
  }

  return <>{children(query.data)}</>;
}

function ViewTab({
  current,
  value,
  onChange,
  children,
}: {
  readonly current: View;
  readonly value: View;
  readonly onChange: (view: View) => void;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <button
      type="button"
      onClick={() => onChange(value)}
      className={clsx(
        'rounded-lg px-3 py-1.5 text-sm font-medium transition duration-150 active:scale-95',
        current === value
          ? 'bg-brand-600 text-white shadow-xs'
          : 'border border-line text-ink-muted hover:border-line-strong hover:bg-surface-3 hover:text-ink',
      )}
    >
      {children}
    </button>
  );
}

function Figure({
  label,
  value,
  emphasis,
}: {
  readonly label: string;
  readonly value: number;
  readonly emphasis?: boolean;
}): React.JSX.Element {
  return (
    <div className="card px-4 py-3">
      <div className="text-xs text-ink-muted">{label}</div>
      <div className={clsx('font-mono', emphasis ? 'text-xl font-semibold' : 'text-lg')}>
        {moneyAlways(value)}
      </div>
    </div>
  );
}

function regimeName(regime: number, t: (key: string) => string): string {
  if (regime === TaxRegime.gccVat) return t('tax.regimeVat');
  if (regime === TaxRegime.indiaGst) return t('tax.regimeGst');

  return t('tax.regimeNone');
}
