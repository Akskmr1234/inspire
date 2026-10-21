import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { PageHeading } from '@/components/PageHeading';
import { moneyAlways } from '@/lib/money';
import { ApiError } from '@/lib/api';
import {
  PRODUCT_BATCH,
  STOCK_BATCH,
  batches,
  downloadExportScript,
  importBalances,
  importMasters,
  importProducts,
  importStock,
  parseExport,
  readDatabase,
  summarise,
  type EasyRetailExport,
  type ImportBalancesResult,
  type ImportStepResult,
} from '@/lib/importer';

type Source = 'file' | 'database';
type StepName = 'masters' | 'products' | 'balances' | 'stock';
type StepStatus = 'waiting' | 'running' | 'done' | 'failed' | 'stopped' | 'off';

const STEPS: readonly StepName[] = ['masters', 'products', 'balances', 'stock'];

/** Notes kept per step; past this the screen says how many more there were. */
const WARNING_LIMIT = 200;

interface StepState {
  readonly status: StepStatus;
  /** Batches sent so far, and how many there are. */
  readonly done: number;
  readonly total: number;
  readonly created: number;
  readonly matched: number;
  readonly alreadyImported: number;
  readonly skipped: number;
  readonly warnings: readonly string[];
  readonly error: string | null;
}

const idle = (status: StepStatus): StepState => ({
  status,
  done: 0,
  total: 0,
  created: 0,
  matched: 0,
  alreadyImported: 0,
  skipped: 0,
  warnings: [],
  error: null,
});

interface Options {
  readonly products: boolean;
  readonly balances: boolean;
  readonly carry: boolean;
  readonly stock: boolean;
}

const errorText = (error: unknown): string =>
  error instanceof ApiError
    ? error.detail || error.code
    : error instanceof Error
      ? error.message
      : String(error);

/**
 * Brings a firm across from EasyRetail.
 *
 * The data arrives one of two ways: the shop runs the export script on the computer that
 * has EasyRetail and uploads the file it writes, or - where this server can reach the
 * shop's SQL Server - the server reads the database itself. Either way the screen shows
 * what it holds and how its balances add up before anything is written, then sends it in
 * steps small enough to report progress and never outlast a proxy's timeout.
 *
 * Every step can be run again. What came across before is recognised by its EasyRetail
 * identifier and left alone, so an import that stopped part way is finished by running it
 * again rather than by clearing the firm.
 */
export function ImportPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [data, setData] = useState<EasyRetailExport | null>(null);

  return (
    <section className="page">
      <PageHeading title={t('importData.title')} />

      {data ? (
        <ImportRun data={data} onChangeSource={() => setData(null)} />
      ) : (
        <SourcePicker onLoaded={setData} />
      )}
    </section>
  );
}

function SourcePicker({
  onLoaded,
}: {
  readonly onLoaded: (data: EasyRetailExport) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [source, setSource] = useState<Source>('file');

  return (
    <div className="panel space-y-4">
      <div
        role="tablist"
        aria-label={t('importData.sourceLabel')}
        className="flex gap-1 overflow-x-auto border-b border-line"
      >
        {(['file', 'database'] as const).map((name) => (
          <button
            key={name}
            type="button"
            role="tab"
            aria-selected={source === name}
            onClick={() => setSource(name)}
            className={clsx(
              '-mb-px shrink-0 border-b-2 px-3 py-2.5 text-sm font-medium whitespace-nowrap transition',
              source === name
                ? 'border-brand-600 text-brand-700 dark:border-brand-400 dark:text-brand-300'
                : 'border-transparent text-ink-muted hover:border-line-strong hover:text-ink',
            )}
          >
            {name === 'file'
              ? t('importData.sourceFile')
              : t('importData.sourceDatabase')}
          </button>
        ))}
      </div>

      {source === 'file' ? (
        <FileSource onLoaded={onLoaded} />
      ) : (
        <DatabaseSource onLoaded={onLoaded} />
      )}
    </div>
  );
}

function FileSource({
  onLoaded,
}: {
  readonly onLoaded: (data: EasyRetailExport) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [error, setError] = useState<string | null>(null);
  const [reading, setReading] = useState(false);

  const script = useMutation<string, ApiError>({
    mutationFn: downloadExportScript,
    onSuccess: (text) => {
      // The byte order mark again: reading the response as text dropped the one the
      // server sent, and Windows PowerShell 5.1 reads a file without one in the
      // machine's own code page, which turns the queries' Arabic-safe text to noise.
      const blob = new Blob([`﻿${text}`], { type: 'text/plain;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = 'easyretail-export.ps1';
      anchor.click();
      URL.revokeObjectURL(url);
    },
  });

  const choose = (file: File): void => {
    setError(null);
    setReading(true);
    file
      .text()
      .then((text) => onLoaded(parseExport(text)))
      .catch((failure: unknown) =>
        setError(
          failure instanceof SyntaxError ||
            (failure instanceof Error && failure.message === 'not-an-export')
            ? t('importData.notAnExport')
            : t('importData.unreadableFile'),
        ),
      )
      .finally(() => setReading(false));
  };

  return (
    <div className="space-y-4">
      <ol className="list-decimal space-y-2 ps-5 text-sm">
        <li>
          <span>{t('importData.fileStep1')}</span>{' '}
          <button
            type="button"
            onClick={() => script.mutate()}
            disabled={script.isPending}
            className="btn-secondary btn-sm mt-1 max-sm:w-full"
          >
            {t('importData.downloadScript')}
          </button>
          {script.isError && (
            <p className="field-message-error">{errorText(script.error)}</p>
          )}
        </li>
        <li>
          {t('importData.fileStep2')}
          <span className="mt-1 block text-xs text-ink-muted">
            {t('importData.scriptBlocked')}
          </span>
          <code className="mt-1 block overflow-x-auto rounded bg-surface-2 px-2 py-1 font-mono text-xs whitespace-nowrap">
            powershell -ExecutionPolicy Bypass -File .\easyretail-export.ps1
          </code>
        </li>
        <li>{t('importData.fileStep3')}</li>
        <li>{t('importData.fileStep4')}</li>
      </ol>

      <label className="btn-primary cursor-pointer focus-within:ring-2 focus-within:ring-brand-400 max-sm:w-full">
        {reading ? t('importData.reading') : t('importData.chooseFile')}
        <input
          type="file"
          accept=".json,application/json"
          className="sr-only"
          disabled={reading}
          onChange={(event) => {
            const file = event.target.files?.[0];
            // Cleared so choosing the same file again, after fixing it, still fires.
            event.target.value = '';
            if (file) choose(file);
          }}
        />
      </label>

      {error && <p className="alert-error">{error}</p>}
    </div>
  );
}

function DatabaseSource({
  onLoaded,
}: {
  readonly onLoaded: (data: EasyRetailExport) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [server, setServer] = useState('');
  const [database, setDatabase] = useState('');
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [asAt, setAsAt] = useState(() => new Date().toISOString().slice(0, 10));

  const read = useMutation<EasyRetailExport, ApiError>({
    mutationFn: () =>
      readDatabase({
        server: server.trim(),
        database: database.trim(),
        userName: userName.trim() || null,
        password: userName.trim() ? password : null,
        asAt: asAt || null,
      }),
    onSuccess: onLoaded,
  });

  const ready = server.trim() !== '' && database.trim() !== '' && !read.isPending;

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (ready) read.mutate();
      }}
    >
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <label className="field">
          <span className="field-label">{t('importData.server')}</span>
          <input
            value={server}
            onChange={(event) => setServer(event.target.value)}
            placeholder=".\SQLEXPRESS"
            autoComplete="off"
            className="field-input"
          />
        </label>
        <label className="field">
          <span className="field-label">{t('importData.database')}</span>
          <input
            value={database}
            onChange={(event) => setDatabase(event.target.value)}
            placeholder="EasyRetail2025"
            autoComplete="off"
            className="field-input"
          />
        </label>
        <label className="field">
          <span className="field-label">{t('importData.userName')}</span>
          <input
            value={userName}
            onChange={(event) => setUserName(event.target.value)}
            placeholder="sa"
            autoComplete="off"
            className="field-input"
          />
        </label>
        <label className="field">
          <span className="field-label">{t('importData.password')}</span>
          <input
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            disabled={!userName.trim()}
            autoComplete="new-password"
            className="field-input"
          />
        </label>
        <label className="field">
          <span className="field-label">{t('importData.asAt')}</span>
          <input
            type="date"
            value={asAt}
            onChange={(event) => setAsAt(event.target.value)}
            className="field-input"
          />
        </label>
      </div>
      <p className="text-xs text-ink-muted">{t('importData.userHint')}</p>

      {read.isError && <p className="alert-error">{errorText(read.error)}</p>}

      <div className="flex justify-end">
        <button type="submit" disabled={!ready} className="btn-primary max-sm:w-full">
          {read.isPending ? t('importData.reading') : t('importData.readDatabase')}
        </button>
      </div>
    </form>
  );
}

function ImportRun({
  data,
  onChangeSource,
}: {
  readonly data: EasyRetailExport;
  readonly onChangeSource: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const summary = summarise(data);
  const [options, setOptions] = useState<Options>({
    products: true,
    balances: true,
    carry: true,
    stock: true,
  });
  const [steps, setSteps] = useState<Readonly<Record<StepName, StepState>> | null>(null);
  const [balances, setBalances] = useState<ImportBalancesResult | null>(null);
  const [running, setRunning] = useState(false);
  const stopping = useRef(false);
  const [stopRequested, setStopRequested] = useState(false);

  // Leaving the screen stops after the batch in hand, as the stop button does, rather
  // than carrying on with nothing showing how far it has got. Running it again finishes.
  useEffect(
    () => () => {
      stopping.current = true;
    },
    [],
  );

  const chosen = (step: StepName): boolean => step === 'masters' || options[step];
  const difference = Math.round((summary.debit - summary.credit) * 100) / 100;

  const run = async (): Promise<void> => {
    stopping.current = false;
    setStopRequested(false);
    setRunning(true);
    setBalances(null);

    const productBatches = batches(data.products, PRODUCT_BATCH);
    const stockBatches = batches(data.stock, STOCK_BATCH);
    const totals: Record<StepName, number> = {
      masters: 1,
      products: productBatches.length,
      balances: 1,
      stock: stockBatches.length,
    };

    let state = Object.fromEntries(
      STEPS.map((step) => [
        step,
        { ...idle(chosen(step) ? 'waiting' : 'off'), total: totals[step] },
      ]),
    ) as Record<StepName, StepState>;
    setSteps(state);

    const update = (step: StepName, change: Partial<StepState>): void => {
      state = { ...state, [step]: { ...state[step], ...change } };
      setSteps(state);
    };

    const add = (step: StepName, result: ImportStepResult): void => {
      const current = state[step];
      update(step, {
        done: current.done + 1,
        created: current.created + result.created,
        matched: current.matched + result.matched,
        alreadyImported: current.alreadyImported + result.alreadyImported,
        skipped: current.skipped + result.skipped,
        warnings: [...current.warnings, ...result.warnings].slice(0, WARNING_LIMIT),
      });
    };

    const work: Record<StepName, ReadonlyArray<() => Promise<ImportStepResult>>> = {
      masters: [() => importMasters(data)],
      products: productBatches.map((batch) => () => importProducts(data, batch)),
      balances: [
        async () => {
          const result = await importBalances(data, options.carry);
          setBalances(result);
          return result.step;
        },
      ],
      stock: stockBatches.map((batch) => () => importStock(data.asAt, batch)),
    };

    try {
      for (const step of STEPS) {
        if (!chosen(step)) continue;

        update(step, { status: 'running' });
        for (const send of work[step]) {
          if (stopping.current) {
            update(step, { status: 'stopped' });
            return;
          }
          try {
            add(step, await send());
          } catch (failure) {
            update(step, { status: 'failed', error: errorText(failure) });
            return;
          }
        }
        update(step, { status: 'done' });
      }
    } finally {
      setRunning(false);
      // Ledgers, parties, products and stock on every other screen were read before.
      void queryClient.invalidateQueries();
    }
  };

  const finished =
    steps !== null && STEPS.every((step) => ['done', 'off'].includes(steps[step].status));
  const failed = steps !== null && STEPS.some((step) => steps[step].status === 'failed');
  const stopped =
    steps !== null && STEPS.some((step) => steps[step].status === 'stopped');

  return (
    <div className="space-y-4">
      <div className="panel space-y-4">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h3 className="text-base font-semibold text-ink">
              {data.company?.name || data.database}
            </h3>
            <p className="text-sm text-ink-muted">
              {[data.company?.city, data.database].filter(Boolean).join(' · ')}
            </p>
            <p className="text-xs text-ink-muted">
              {t('importData.asAtLine', {
                date: data.asAt,
                exported: data.exportedAt.slice(0, 16).replace('T', ' '),
              })}
            </p>
          </div>
          <button
            type="button"
            onClick={onChangeSource}
            disabled={running}
            className="btn-ghost btn-sm"
          >
            {t('importData.changeSource')}
          </button>
        </div>

        <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
          <div>
            <h4 className="mb-2 text-sm font-semibold text-ink">
              {t('importData.mastersTitle')}
            </h4>
            <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
              {summary.counts.map(([kind, count]) => (
                <div key={kind} className="contents">
                  <dt className="text-ink-muted">{t(`importData.counts.${kind}`)}</dt>
                  <dd className="text-end font-mono tabular-nums">
                    {count.toLocaleString()}
                  </dd>
                </div>
              ))}
            </dl>
          </div>

          <div>
            <h4 className="mb-2 text-sm font-semibold text-ink">
              {t('importData.balancesTitle')}
            </h4>
            <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
              <dt className="text-ink-muted">{t('importData.debit')}</dt>
              <dd className="text-end font-mono tabular-nums">
                {moneyAlways(summary.debit)}
              </dd>
              <dt className="text-ink-muted">{t('importData.credit')}</dt>
              <dd className="text-end font-mono tabular-nums">
                {moneyAlways(summary.credit)}
              </dd>
              <dt className="text-ink-muted">{t('importData.difference')}</dt>
              <dd
                className={clsx(
                  'text-end font-mono tabular-nums',
                  difference !== 0 && 'font-semibold text-amber-700 dark:text-amber-400',
                )}
              >
                {moneyAlways(difference)}
              </dd>
            </dl>
            {difference !== 0 && (
              <p className="mt-2 text-xs text-ink-muted">
                {t('importData.differenceNote', {
                  amount: moneyAlways(Math.abs(difference)),
                })}
              </p>
            )}
          </div>

          <div>
            <h4 className="mb-2 text-sm font-semibold text-ink">
              {t('importData.stockTitle')}
            </h4>
            <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
              <dt className="text-ink-muted">{t('importData.stockLines')}</dt>
              <dd className="text-end font-mono tabular-nums">
                {summary.stockLines.toLocaleString()}
              </dd>
              <dt className="text-ink-muted">{t('importData.stockValue')}</dt>
              <dd className="text-end font-mono tabular-nums">
                {moneyAlways(summary.stockValue)}
              </dd>
              <dt className="text-ink-muted">{t('importData.negativeStock')}</dt>
              <dd className="text-end font-mono tabular-nums">
                {summary.negativeStock.toLocaleString()}
              </dd>
            </dl>
          </div>
        </div>
      </div>

      <div className="panel space-y-3">
        <h3 className="text-base font-semibold text-ink">
          {t('importData.optionsTitle')}
        </h3>
        <div className="flex flex-col gap-1">
          <label className="field-check">
            <input type="checkbox" checked disabled />
            {t('importData.optionMasters')}
          </label>
          <label className="field-check">
            <input
              type="checkbox"
              checked={options.products}
              disabled={running}
              onChange={(event) =>
                setOptions({ ...options, products: event.target.checked })
              }
            />
            {t('importData.optionProducts')}
          </label>
          <label className="field-check">
            <input
              type="checkbox"
              checked={options.balances}
              disabled={running}
              onChange={(event) =>
                setOptions({ ...options, balances: event.target.checked })
              }
            />
            {t('importData.optionBalances', { date: data.asAt })}
          </label>
          <label className="field-check ps-6">
            <input
              type="checkbox"
              checked={options.carry}
              disabled={running || !options.balances}
              onChange={(event) =>
                setOptions({ ...options, carry: event.target.checked })
              }
            />
            {t('importData.optionCarry')}
          </label>
          <label className="field-check">
            <input
              type="checkbox"
              checked={options.stock}
              disabled={running}
              onChange={(event) =>
                setOptions({ ...options, stock: event.target.checked })
              }
            />
            {t('importData.optionStock', { date: data.asAt })}
          </label>
        </div>

        <div className="flex flex-wrap justify-end gap-2">
          {running ? (
            <button
              type="button"
              // Marked rather than disabled: disabling the focused button drops focus
              // to the top of the page, and the same button becomes "Run again".
              aria-disabled={stopRequested}
              onClick={() => {
                stopping.current = true;
                setStopRequested(true);
              }}
              className={clsx(
                'btn-secondary max-sm:w-full',
                stopRequested && 'opacity-50',
              )}
            >
              {stopRequested ? t('importData.stopping') : t('importData.stop')}
            </button>
          ) : (
            <button
              type="button"
              onClick={() => void run()}
              className="btn-primary max-sm:w-full"
            >
              {steps ? t('importData.runAgain') : t('importData.start')}
            </button>
          )}
        </div>
      </div>

      {steps && (
        <div className="panel space-y-3">
          <h3 className="text-base font-semibold text-ink">
            {t('importData.progressTitle')}
          </h3>

          {finished && <p className="alert-success">{t('importData.finished')}</p>}
          {failed && <p className="alert-error">{t('importData.failed')}</p>}
          {stopped && <p className="alert-warn">{t('importData.stopped')}</p>}

          <ul className="divide-y divide-line">
            {STEPS.map((step) => (
              <StepRow key={step} name={step} state={steps[step]} />
            ))}
          </ul>

          {balances && (
            <div className="space-y-2 border-t border-line pt-3">
              <p className="text-sm font-semibold text-ink">
                {t('importData.openingSet')}
              </p>
              <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm sm:grid-cols-4">
                <dt className="text-ink-muted">{t('importData.debit')}</dt>
                <dd className="text-end font-mono tabular-nums">
                  {moneyAlways(balances.totalDebit)}
                </dd>
                <dt className="text-ink-muted">{t('importData.credit')}</dt>
                <dd className="text-end font-mono tabular-nums">
                  {moneyAlways(balances.totalCredit)}
                </dd>
                <dt className="text-ink-muted">{t('importData.profitCarried')}</dt>
                <dd className="text-end font-mono tabular-nums">
                  {moneyAlways(balances.profitCarried)}
                </dd>
                <dt className="text-ink-muted">{t('importData.differenceCarried')}</dt>
                <dd className="text-end font-mono tabular-nums">
                  {moneyAlways(balances.difference)}
                </dd>
              </dl>
            </div>
          )}

          {finished && (
            <div className="flex flex-wrap items-center gap-x-4 gap-y-2 border-t border-line pt-3 text-sm">
              <span className="text-ink-muted">{t('importData.check')}</span>
              <Link to="/accounting/trial-balance" className="font-medium underline">
                {t('importData.links.trialBalance')}
              </Link>
              <Link to="/accounting/ledgers" className="font-medium underline">
                {t('importData.links.ledgers')}
              </Link>
              <Link to="/sales/customers" className="font-medium underline">
                {t('importData.links.customers')}
              </Link>
              <Link to="/purchase/suppliers" className="font-medium underline">
                {t('importData.links.suppliers')}
              </Link>
              <Link to="/inventory/products" className="font-medium underline">
                {t('importData.links.products')}
              </Link>
              <Link to="/inventory/valuation" className="font-medium underline">
                {t('importData.links.valuation')}
              </Link>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

/** The counts worth saying, in the order a reader wants them. */
function tally(
  name: StepName,
  state: StepState,
): ReadonlyArray<readonly [string, number]> {
  // A balance is set on every run, so "created" would read as if the second run had
  // made new ledgers.
  const parts: Array<readonly [string, number]> = [
    [name === 'balances' ? 'balancesSet' : 'created', state.created],
    ['matched', state.matched],
    ['alreadyImported', state.alreadyImported],
    ['skipped', state.skipped],
  ];
  return parts.filter(([, value]) => value > 0);
}

function StepRow({
  name,
  state,
}: {
  readonly name: StepName;
  readonly state: StepState;
}): React.JSX.Element {
  const { t } = useTranslation();
  const percent = state.total === 0 ? 100 : Math.round((state.done / state.total) * 100);

  return (
    <li className="space-y-2 py-3">
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1">
        <span className="font-medium text-ink">{t(`importData.steps.${name}`)}</span>
        <span
          className={clsx(
            'text-xs font-semibold',
            state.status === 'done' && 'text-emerald-700 dark:text-emerald-400',
            state.status === 'failed' && 'text-red-600 dark:text-red-400',
            state.status === 'stopped' && 'text-amber-700 dark:text-amber-400',
            ['waiting', 'off', 'running'].includes(state.status) && 'text-ink-muted',
          )}
        >
          {t(`importData.status.${state.status}`)}
          {state.total > 1 && state.status !== 'off' && (
            <> · {t('importData.batch', { done: state.done, total: state.total })}</>
          )}
        </span>
      </div>

      {state.status !== 'off' && (
        <div
          role="progressbar"
          aria-valuenow={percent}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-label={t(`importData.steps.${name}`)}
          className="h-1.5 overflow-hidden rounded-full bg-surface-3"
        >
          <div
            className={clsx(
              'h-full rounded-full transition-[width] duration-300',
              state.status === 'failed' ? 'bg-red-500' : 'bg-brand-500',
            )}
            style={{ width: `${state.status === 'waiting' ? 0 : percent}%` }}
          />
        </div>
      )}

      {(state.done > 0 || state.status === 'done') && (
        <p className="text-xs text-ink-muted">
          {tally(name, state)
            .map(([part, value]) =>
              t(`importData.tally.${part}`, { value: value.toLocaleString() }),
            )
            .join(' · ') || t('importData.tally.nothing')}
        </p>
      )}

      {state.error && <p className="alert-error">{state.error}</p>}

      {state.warnings.length > 0 && (
        <details className="text-xs">
          <summary className="cursor-pointer text-ink-muted hover:text-ink">
            {t('importData.notes', { count: state.warnings.length })}
          </summary>
          <ul className="mt-2 max-h-64 list-disc space-y-1 overflow-y-auto ps-5 text-ink">
            {state.warnings.map((warning, index) => (
              <li key={index}>{warning}</li>
            ))}
          </ul>
        </details>
      )}
    </li>
  );
}
