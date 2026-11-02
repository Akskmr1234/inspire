import { useState } from 'react';
import * as Popover from '@radix-ui/react-popover';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Field } from '@/components/Form';
import { IconDownload } from '@/components/icons';
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
  asField = false,
  className,
}: {
  readonly sources: readonly ReferenceSource[];
  /** Fills the new document from the one picked. */
  readonly onLoad: (sourceKey: string, id: string) => Promise<void>;
  readonly disabled?: boolean;
  /** As one field among the others - a caption over full-height boxes - not a strip. */
  readonly asField?: boolean;
  readonly className?: string;
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

  const kinds = sources.length > 1 && (
    <select
      aria-label={t('references.loadFrom')}
      value={source.key}
      disabled={disabled || loading}
      onChange={(event) => setSourceKey(event.target.value)}
      className={asField ? 'field-input w-auto shrink-0' : 'field-input-sm w-auto'}
    >
      {sources.map((candidate) => (
        <option key={candidate.key} value={candidate.key}>
          {candidate.label}
        </option>
      ))}
    </select>
  );

  const picker = (
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
      size={asField ? 'default' : 'sm'}
      label={`${t('references.loadFrom')}: ${source.label}`}
      placeholder={
        sources.length > 1
          ? t('references.typeNumber')
          : `${source.label} · ${t('references.typeNumber')}`
      }
    />
  );

  if (asField) {
    return (
      <Field
        label={t('references.loadFrom')}
        className={className}
        aside={problem && <span className="text-red-600">{problem}</span>}
      >
        <div className="flex gap-2">
          {kinds}
          <div className="min-w-0 flex-1">{picker}</div>
        </div>
      </Field>
    );
  }

  return (
    <div className="flex flex-wrap items-center gap-2 rounded-lg border border-dashed border-line bg-surface-2/40 px-2 py-1.5">
      <span className="text-xs font-semibold text-ink-muted">
        {t('references.loadFrom')}
      </span>
      {kinds}
      <div className="min-w-56 flex-1">{picker}</div>
      {problem && <span className="text-xs text-red-600">{problem}</span>}
    </div>
  );
}

/**
 * Load from, as a button: the loader opens above it, for the bar along the foot of an
 * entry - out of the way of the fields until somebody wants it - and closes once the
 * document is filled.
 */
export function LoadFromButton({
  sources,
  onLoad,
  disabled = false,
}: {
  readonly sources: readonly ReferenceSource[];
  readonly onLoad: (sourceKey: string, id: string) => Promise<void>;
  readonly disabled?: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger asChild>
        <button
          type="button"
          disabled={disabled}
          aria-label={t('references.loadFrom')}
          title={t('references.loadFrom')}
          className="btn-secondary px-3"
        >
          <IconDownload className="size-4" />
          <span className="hidden sm:inline">{t('references.loadFrom')}</span>
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          side="top"
          align="start"
          sideOffset={8}
          collisionPadding={12}
          className="animate-pop z-50 w-[min(30rem,calc(100vw-1.5rem))] rounded-2xl border border-line bg-surface p-3 shadow-float outline-none"
        >
          <ReferenceLoader
            asField
            sources={sources}
            onLoad={async (sourceKey, id) => {
              await onLoad(sourceKey, id);
              setOpen(false);
            }}
          />
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}
