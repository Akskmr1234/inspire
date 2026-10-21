import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Spinner } from '@/components/ReportFrame';
import type { ApiError } from '@/lib/api';
import {
  listNumbering,
  setNumbering,
  type DocumentNumbering,
  type DocumentTypeName,
} from '@/lib/numbering';

/**
 * The columns every row and the heading share from a laptop up, where the series read as
 * a table. Below that each series is a card of labelled fields: eight columns of inputs
 * were wider than a tablet, and on a phone the counter and the save button were off the
 * edge of the screen.
 */
const COLUMNS =
  'lg:grid-cols-[minmax(9rem,1.4fr)_6rem_5.5rem_3.5rem_5rem_6rem_minmax(7rem,1fr)_4.5rem]';

const CAPTION = 'mb-1 block text-[11px] text-ink-muted lg:sr-only';

/**
 * How each document is numbered, as the server issues the numbers.
 *
 * This replaces a card whose prefixes lived in the browser and reached nothing: the
 * server issued "SL/2026/0007" whatever the card said, and the entry screens printed
 * the card's "INV-1001" above it. These rows are the series themselves, so a prefix
 * changed here is the prefix the next document carries.
 *
 * Numbers already issued never change - they are on paper somebody has - and the
 * counter can be moved forward to continue a sequence from an old system, but not
 * back, which would issue a number a document already carries.
 */
export function NumberingSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const today = new Date().toISOString().slice(0, 10);

  const query = useQuery<readonly DocumentNumbering[], ApiError>({
    queryKey: ['numbering', 'list', today],
    queryFn: () => listNumbering(today),
  });

  return (
    <div className="space-y-3 border-t border-line pt-4">
      <div>
        <h4 className="text-xs font-bold tracking-wider text-ink-muted uppercase">
          {t('numbering.title')}
        </h4>
      </div>

      {query.isPending && <Spinner />}
      {query.isError && (
        <p className="alert-error text-xs">{query.error.detail || query.error.code}</p>
      )}

      {query.data && (
        // A laptop with the sidebar open can be narrower than the columns: the box
        // scrolls then, rather than clipping the counter and the save button.
        <div className="overflow-x-auto overscroll-x-contain rounded-xl border border-line bg-surface">
          <div className="lg:min-w-[52rem]">
            <div
              aria-hidden="true"
              className={`hidden gap-x-3 border-b border-line bg-surface-3 px-3 py-2 text-xs font-semibold tracking-wide text-ink-muted uppercase lg:grid ${COLUMNS}`}
            >
              <span>{t('numbering.document')}</span>
              <span>{t('numbering.prefix')}</span>
              <span>{t('numbering.separator')}</span>
              <span>{t('numbering.year')}</span>
              <span>{t('numbering.suffix')}</span>
              <span className="text-end">{t('numbering.nextNumber')}</span>
              <span>{t('numbering.preview')}</span>
              <span />
            </div>
            <ul className="divide-y divide-line">
              {query.data.map((row) => (
                <NumberingRow key={row.documentType} row={row} date={today} />
              ))}
            </ul>
          </div>
        </div>
      )}
    </div>
  );
}

function NumberingRow({
  row,
  date,
}: {
  readonly row: DocumentNumbering;
  readonly date: string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  const [prefix, setPrefix] = useState(row.prefix ?? '');
  const [separator, setSeparator] = useState(row.separator);
  const [includeYear, setIncludeYear] = useState(row.includeYear);
  const [suffix, setSuffix] = useState(row.suffix ?? '');
  const [nextNumber, setNextNumber] = useState(String(row.nextNumber));

  // Follow the server's copy when it changes underneath, for instance after a save.
  useEffect(() => {
    setPrefix(row.prefix ?? '');
    setSeparator(row.separator);
    setIncludeYear(row.includeYear);
    setSuffix(row.suffix ?? '');
    setNextNumber(String(row.nextNumber));
  }, [row]);

  const save = useMutation<DocumentNumbering, ApiError>({
    mutationFn: () =>
      setNumbering(row.documentType as DocumentTypeName, {
        prefix: prefix.trim() || null,
        suffix: suffix.trim() || null,
        separator,
        includeYear,
        nextNumber: Number(nextNumber) !== row.nextNumber ? Number(nextNumber) : null,
        date,
      }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['numbering'] }),
  });

  const changed =
    prefix !== (row.prefix ?? '') ||
    separator !== row.separator ||
    includeYear !== row.includeYear ||
    suffix !== (row.suffix ?? '') ||
    nextNumber !== String(row.nextNumber);

  // The preview is assembled the way the server assembles it, so what is shown before
  // saving is what the next document will carry once it is.
  const padded = String(Number(nextNumber) || row.nextNumber).padStart(4, '0');
  const preview = [prefix.trim(), includeYear ? row.yearLabel : '', padded, suffix.trim()]
    .filter((segment) => segment !== '')
    .join(separator);

  const name = t(`numbering.types.${row.documentType}`);

  return (
    <li
      className={`grid grid-cols-6 items-end gap-x-3 gap-y-2 p-3 lg:items-center lg:py-2 ${COLUMNS}`}
    >
      <div className="col-span-6 text-sm font-medium text-ink lg:col-span-1">
        {name}
        {!row.isConfigured && (
          <span className="ms-1.5 text-[11px] font-normal text-ink-muted">
            ({t('numbering.default')})
          </span>
        )}
      </div>
      <label className="col-span-2 min-w-0 lg:col-span-1">
        <span className={CAPTION}>{t('numbering.prefix')}</span>
        <input
          value={prefix}
          onChange={(event) => setPrefix(event.target.value)}
          maxLength={20}
          aria-label={`${t('numbering.prefix')} — ${name}`}
          className="field-input-sm w-full font-mono"
        />
      </label>
      <label className="col-span-2 min-w-0 lg:col-span-1">
        <span className={CAPTION}>{t('numbering.separator')}</span>
        <input
          value={separator}
          onChange={(event) => setSeparator(event.target.value)}
          maxLength={5}
          aria-label={`${t('numbering.separator')} — ${name}`}
          className="field-input-sm w-full text-center font-mono"
        />
      </label>
      <label className="field-check col-span-2 lg:col-span-1">
        <input
          type="checkbox"
          checked={includeYear}
          onChange={(event) => setIncludeYear(event.target.checked)}
          aria-label={`${t('numbering.year')} — ${name}`}
        />
        <span className="text-xs text-ink-muted lg:sr-only">{t('numbering.year')}</span>
      </label>
      <label className="col-span-3 min-w-0 lg:col-span-1">
        <span className={CAPTION}>{t('numbering.suffix')}</span>
        <input
          value={suffix}
          onChange={(event) => setSuffix(event.target.value)}
          maxLength={20}
          aria-label={`${t('numbering.suffix')} — ${name}`}
          className="field-input-sm w-full font-mono"
        />
      </label>
      <label className="col-span-3 min-w-0 lg:col-span-1">
        <span className={`${CAPTION} text-end`}>{t('numbering.nextNumber')}</span>
        <input
          type="number"
          min={row.nextNumber}
          value={nextNumber}
          onChange={(event) => setNextNumber(event.target.value)}
          aria-label={`${t('numbering.nextNumber')} — ${name}`}
          className="field-input-sm w-full text-end font-mono tabular-nums"
        />
      </label>
      <div className="col-span-4 min-w-0 lg:col-span-1">
        <span className={CAPTION}>{t('numbering.preview')}</span>
        <span className="font-mono text-xs text-ink [overflow-wrap:anywhere]">
          {preview}
        </span>
      </div>
      <div className="col-span-2 text-end lg:col-span-1">
        <button
          type="button"
          disabled={!changed || save.isPending}
          onClick={() => save.mutate()}
          className="btn-primary btn-sm"
        >
          {save.isPending ? t('common.saving') : t('common.save')}
        </button>
      </div>
      {save.isError && (
        <p className="col-span-full text-xs text-red-600 dark:text-red-400">
          {save.error.detail || save.error.code}
        </p>
      )}
    </li>
  );
}
