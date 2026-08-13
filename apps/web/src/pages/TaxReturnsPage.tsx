import { useState } from 'react';
import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { ReportFrame, moneyAlways } from '@/components/ReportFrame';
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
      className="flex flex-wrap items-end gap-3"
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

            <span className="ms-auto text-xs text-slate-500">
              {regimeName(data.regime, t)} · {data.currency}
            </span>
          </div>

          {view === 'summary' && <Summary data={data} />}

          {view === 'output' && (
            <Detail query={output}>
              {(report) => <OutputRows report={report} />}
            </Detail>
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
          'inline-block rounded-lg px-3 py-1.5 text-sm font-medium',
          data.isReconciled
            ? 'bg-emerald-50 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200'
            : 'bg-red-100 text-red-900 dark:bg-red-950 dark:text-red-100',
        )}
      >
        {data.isReconciled ? t('tax.reconciled') : t('tax.notReconciled')}
      </p>

      <div className="grid gap-3 sm:grid-cols-3">
        <Figure label={t('tax.taxableSupplies')} value={data.taxableSupplies} />
        <Figure label={t('tax.zeroRated')} value={data.zeroRatedSupplies} />
        <Figure label={t('tax.netPayable')} value={data.netPayable} emphasis />
      </div>

      <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800">
        <table className="w-full border-collapse text-sm">
          <thead className="bg-slate-100 dark:bg-slate-800">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">{t('tax.head')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.outputTax')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.inputTax')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.netPayable')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.onLedger')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.difference')}</th>
            </tr>
          </thead>

          <tbody>
            {data.lines.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-3 py-6 text-center text-sm text-slate-500">
                  {t('tax.nothingCharged')}
                </td>
              </tr>
            ) : (
              data.lines.map((line) => (
                <tr
                  key={line.component}
                  className="border-t border-slate-100 dark:border-slate-900"
                >
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
                  <td className="px-3 py-1.5 text-end font-mono text-slate-500">
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
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      <p className="text-xs text-slate-500">{t('tax.differenceHint')}</p>
    </div>
  );
}

/** The documents behind the output tax. */
function OutputRows({ report }: { readonly report: OutputTaxReport }): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800">
      <table className="w-full border-collapse text-sm">
        <thead className="bg-slate-100 dark:bg-slate-800">
          <tr>
            <th className="px-3 py-2 text-start font-semibold">{t('tax.document')}</th>
            <th className="px-3 py-2 text-start font-semibold">{t('sales.date')}</th>
            <th className="px-3 py-2 text-start font-semibold">{t('sales.customer')}</th>
            <th className="px-3 py-2 text-start font-semibold">{t('tax.registration')}</th>
            <th className="px-3 py-2 text-start font-semibold">{t('tax.head')}</th>
            <th className="px-3 py-2 text-end font-semibold">{t('tax.rate')}</th>
            <th className="px-3 py-2 text-end font-semibold">{t('tax.taxableValue')}</th>
            <th className="px-3 py-2 text-end font-semibold">{t('tax.tax')}</th>
          </tr>
        </thead>

        <tbody>
          {report.rows.length === 0 ? (
            <tr>
              <td colSpan={8} className="px-3 py-6 text-center text-sm text-slate-500">
                {t('tax.noSupplies')}
              </td>
            </tr>
          ) : (
            report.rows.map((row, index) => (
              <tr
                key={`${row.documentId}-${row.component}-${index}`}
                className="border-t border-slate-100 dark:border-slate-900"
              >
                <td className="px-3 py-1.5">{row.number}</td>
                <td className="px-3 py-1.5">{row.date}</td>
                <td className="px-3 py-1.5">{row.customerName}</td>
                <td className="px-3 py-1.5 text-slate-500">
                  {row.taxRegistrationNumber ?? '—'}
                  {row.stateCode ? ` · ${row.stateCode}` : ''}
                </td>
                <td className="px-3 py-1.5">{componentName(row.component)}</td>
                <td className="px-3 py-1.5 text-end font-mono">{row.percentage}%</td>
                <td className="px-3 py-1.5 text-end font-mono">
                  {moneyAlways(row.taxableAmount)}
                </td>
                <td className="px-3 py-1.5 text-end font-mono">{moneyAlways(row.taxAmount)}</td>
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
      {/* Said once, plainly, where somebody reading the column would otherwise wonder
          why it is missing. */}
      <p className="rounded-md bg-amber-50 px-3 py-2 text-xs text-amber-800 dark:bg-amber-950 dark:text-amber-200">
        {t('tax.inputHint')}
      </p>

      <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800">
        <table className="w-full border-collapse text-sm">
          <thead className="bg-slate-100 dark:bg-slate-800">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">{t('tax.voucher')}</th>
              <th className="px-3 py-2 text-start font-semibold">{t('sales.date')}</th>
              <th className="px-3 py-2 text-start font-semibold">{t('tax.account')}</th>
              <th className="px-3 py-2 text-start font-semibold">{t('tax.head')}</th>
              <th className="px-3 py-2 text-start font-semibold">{t('tax.narration')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('tax.tax')}</th>
            </tr>
          </thead>

          <tbody>
            {report.rows.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-3 py-6 text-center text-sm text-slate-500">
                  {t('tax.noInput')}
                </td>
              </tr>
            ) : (
              report.rows.map((row, index) => (
                <tr
                  key={`${row.voucherId}-${index}`}
                  className="border-t border-slate-100 dark:border-slate-900"
                >
                  <td className="px-3 py-1.5">{row.number}</td>
                  <td className="px-3 py-1.5">{row.date}</td>
                  <td className="px-3 py-1.5">
                    {row.ledgerCode} {row.ledgerName}
                  </td>
                  <td className="px-3 py-1.5">{componentName(row.component)}</td>
                  <td className="px-3 py-1.5 text-slate-500">{row.narration ?? '—'}</td>
                  <td className="px-3 py-1.5 text-end font-mono">{moneyAlways(row.taxAmount)}</td>
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
  const { t } = useTranslation();

  if (query.error) {
    return (
      <p className="rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-700 dark:bg-rose-950 dark:text-rose-300">
        {query.error.detail || query.error.code}
      </p>
    );
  }

  if (query.isLoading || !query.data) {
    return <p className="text-sm text-slate-500">{t('common.loading')}</p>;
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
        'rounded-md px-3 py-1.5 text-sm transition',
        current === value
          ? 'bg-slate-900 text-white dark:bg-slate-100 dark:text-slate-900'
          : 'border border-slate-300 text-slate-600 hover:bg-slate-100 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800',
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
    <div className="rounded-xl border border-slate-200 px-4 py-3 dark:border-slate-800">
      <div className="text-xs text-slate-500">{label}</div>
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
