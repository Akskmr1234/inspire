import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { SearchSelect } from '@/components/SearchSelect';
import type { ApiError } from '@/lib/api';
import { moneyAlways } from '@/lib/money';

/** One document a new one can be loaded from. */
export interface ReferenceOption {
  readonly id: string;
  readonly number: string;
  readonly date: string;
  readonly party: string;
  readonly total: number;
}

/** A kind of document a new one can be loaded from: a quotation, an order, an invoice. */
export interface ReferenceSource {
  readonly key: string;
  readonly label: string;
  /** The recent documents of this kind, newest first. */
  readonly list: () => Promise<readonly ReferenceOption[]>;
}

/**
 * Loads a new document from one already entered, found by its number: an order into
 * its purchase, a quotation into its sale, an invoice into its return.
 */
export function ReferenceLoader({
  sources,
  onLoad,
  disabled = false,
}: {
  readonly sources: readonly ReferenceSource[];
  /** Fills the new document from the one picked. */
  readonly onLoad: (sourceKey: string, id: string) => Promise<void>;
  readonly disabled?: boolean;
}): React.JSX.Element | null {
  const { t } = useTranslation();
  const [sourceKey, setSourceKey] = useState(sources[0]?.key ?? '');
  const [loading, setLoading] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);

  const source = sources.find((candidate) => candidate.key === sourceKey) ?? sources[0];

  const options = useQuery<readonly ReferenceOption[], ApiError>({
    queryKey: ['reference-options', source?.key],
    queryFn: () => source?.list() ?? Promise.resolve([]),
    enabled: source !== undefined && !disabled,
    staleTime: 30_000,
  });

  if (!source) return null;

  return (
    <div className="flex flex-wrap items-center gap-2 rounded-lg border border-dashed border-line bg-surface-2/40 px-2 py-1.5">
      <span className="text-xs font-semibold text-ink-muted">
        {t('references.loadFrom')}
      </span>
      {sources.length > 1 && (
        <select
          aria-label={t('references.loadFrom')}
          value={source.key}
          disabled={disabled || loading}
          onChange={(event) => setSourceKey(event.target.value)}
          className="field-input-sm w-auto"
        >
          {sources.map((candidate) => (
            <option key={candidate.key} value={candidate.key}>
              {candidate.label}
            </option>
          ))}
        </select>
      )}
      <div className="min-w-56 flex-1">
        <SearchSelect
          value=""
          disabled={disabled || loading}
          onChange={(id) => {
            if (!id) return;
            setLoading(true);
            setProblem(null);
            onLoad(source.key, id)
              .catch((failure: unknown) =>
                setProblem(
                  failure instanceof Error ? failure.message : t('references.loadFailed'),
                ),
              )
              .finally(() => setLoading(false));
          }}
          options={(options.data ?? []).map((option) => ({
            value: option.id,
            label: option.number,
            detail: [option.date, option.party, moneyAlways(option.total)]
              .filter(Boolean)
              .join(' · '),
          }))}
          size="sm"
          label={`${t('references.loadFrom')}: ${source.label}`}
          placeholder={
            sources.length > 1
              ? t('references.typeNumber')
              : `${source.label} · ${t('references.typeNumber')}`
          }
        />
      </div>
      {problem && <span className="text-xs text-red-600">{problem}</span>}
    </div>
  );
}
