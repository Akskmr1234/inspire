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
        <p className="mt-0.5 text-xs text-ink-muted">{t('numbering.hint')}</p>
      </div>

      {query.isPending && <Spinner />}
      {query.isError && (
        <p className="alert-error text-xs">{query.error.detail || query.error.code}</p>
      )}

      {query.data && (
        <div className="table-wrap">
          <table className="table min-w-[44rem] text-sm">
            <thead>
              <tr>
                <th className="text-start">{t('numbering.document')}</th>
                <th className="text-start">{t('numbering.prefix')}</th>
                <th className="text-start">{t('numbering.separator')}</th>
                <th className="text-start">{t('numbering.year')}</th>
                <th className="text-start">{t('numbering.suffix')}</th>
                <th className="text-end">{t('numbering.nextNumber')}</th>
                <th className="text-start">{t('numbering.preview')}</th>
                <th className="w-20" />
              </tr>
            </thead>
            <tbody>
              {query.data.map((row) => (
                <NumberingRow key={row.documentType} row={row} date={today} />
              ))}
            </tbody>
          </table>
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

  return (
    <>
      <tr>
        <td className="whitespace-nowrap">
          {t(`numbering.types.${row.documentType}`)}
          {!row.isConfigured && (
            <span className="ms-1.5 text-[11px] text-ink-muted">
              ({t('numbering.default')})
            </span>
          )}
        </td>
        <td>
          <input
            value={prefix}
            onChange={(event) => setPrefix(event.target.value)}
            maxLength={20}
            aria-label={`${t('numbering.prefix')} — ${t(`numbering.types.${row.documentType}`)}`}
            className="field-input-sm w-24 font-mono"
          />
        </td>
        <td>
          <input
            value={separator}
            onChange={(event) => setSeparator(event.target.value)}
            maxLength={5}
            aria-label={t('numbering.separator')}
            className="field-input-sm w-12 text-center font-mono"
          />
        </td>
        <td>
          <input
            type="checkbox"
            checked={includeYear}
            onChange={(event) => setIncludeYear(event.target.checked)}
            aria-label={t('numbering.year')}
          />
        </td>
        <td>
          <input
            value={suffix}
            onChange={(event) => setSuffix(event.target.value)}
            maxLength={20}
            aria-label={t('numbering.suffix')}
            className="field-input-sm w-20 font-mono"
          />
        </td>
        <td className="text-end">
          <input
            type="number"
            min={row.nextNumber}
            value={nextNumber}
            onChange={(event) => setNextNumber(event.target.value)}
            aria-label={t('numbering.nextNumber')}
            className="field-input-sm ms-auto w-24 text-end font-mono tabular-nums"
          />
        </td>
        <td className="font-mono text-xs whitespace-nowrap text-ink">{preview}</td>
        <td className="text-end">
          <button
            type="button"
            disabled={!changed || save.isPending}
            onClick={() => save.mutate()}
            className="btn-primary btn-sm"
          >
            {save.isPending ? t('common.saving') : t('common.save')}
          </button>
        </td>
      </tr>
      {save.isError && (
        <tr>
          <td colSpan={8} className="text-xs text-red-600 dark:text-red-400">
            {save.error.detail || save.error.code}
          </td>
        </tr>
      )}
    </>
  );
}
