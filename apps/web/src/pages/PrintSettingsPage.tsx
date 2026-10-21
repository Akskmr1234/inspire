import { useEffect, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useSettings } from '@/stores/settings';
import { PageHeading } from '@/components/PageHeading';
import { Spinner } from '@/components/ReportFrame';
import type { ApiError } from '@/lib/api';
import {
  PRINT_DOCUMENTS,
  PRINT_MODES,
  profileFor,
  savePrintSetup,
  usePrintSetup,
  type PrintDocumentType,
  type PrintJob,
  type PrintMode,
  type PrintProfile,
  type PrintSetup,
  type PrintTemplate,
} from '@/lib/printing';

/** Vouchers have no tax mode, so only one row each. */
const MODES_FOR: Readonly<Record<PrintDocumentType, readonly PrintMode[]>> = {
  salesInvoice: PRINT_MODES,
  salesReturn: PRINT_MODES,
  salesQuotation: PRINT_MODES,
  salesOrder: PRINT_MODES,
  purchaseInvoice: PRINT_MODES,
  purchaseReturn: PRINT_MODES,
  receipt: ['tax'],
  payment: ['tax'],
  journal: ['tax'],
};

/**
 * How each document prints: a row per document and tax mode.
 *
 * Preview or print straight away, which layout, the printer it is meant for and how many
 * copies - and a second run after it on another printer and layout, for the shop that
 * hands over a till receipt and files an A4 copy. Held on the server, so every counter
 * prints the same way.
 */
export function PrintSettingsPage(): React.JSX.Element {
  const { t } = useTranslation();
  const gulfMode = useSettings((state) => state.gulfMode);
  const queryClient = useQueryClient();
  const { setup, loading, error } = usePrintSetup();
  const [draft, setDraft] = useState<PrintSetup | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    if (!loading) setDraft(setup);
  }, [loading, setup]);

  const save = useMutation<void, ApiError, PrintSetup>({
    mutationFn: savePrintSetup,
    onSuccess: () => {
      setSaved(true);
      void queryClient.invalidateQueries({ queryKey: ['print-setup'] });
    },
  });

  if (loading || !draft) {
    return (
      <section className="page">
        <PageHeading title={t('printing.settingsTitle')} />
        {error ? (
          <p className="alert-error">{error.detail || error.code}</p>
        ) : (
          <Spinner />
        )}
      </section>
    );
  }

  const setProfile = (
    type: PrintDocumentType,
    mode: PrintMode,
    profile: PrintProfile,
  ): void => {
    setSaved(false);
    setDraft((current) =>
      current
        ? { ...current, profiles: { ...current.profiles, [`${type}.${mode}`]: profile } }
        : current,
    );
  };

  return (
    <section className="page">
      <PageHeading
        title={t('printing.settingsTitle')}
        actions={
          <button
            type="button"
            disabled={save.isPending}
            onClick={() => save.mutate(draft)}
            className="btn-primary btn-sm"
          >
            {save.isPending ? t('common.saving') : t('common.save')}
          </button>
        }
      />

      {saved && <p className="alert-success">{t('printing.savedNotice')}</p>}
      {save.isError && (
        <p className="alert-error">{save.error.detail || save.error.code}</p>
      )}

      {/*
        A card a document rather than one table across the page. The table needed nine
        columns of controls, two sets of template, printer and copies side by side, and
        was wider than the screen at every size short of a wide monitor: the second
        print's printer and copies were off the edge even on a laptop.
      */}
      <ul className="grid gap-3 lg:grid-cols-2">
        {PRINT_DOCUMENTS.flatMap((type) =>
          MODES_FOR[type].map((mode) => {
            const profile = profileFor(draft, type, mode);

            return (
              <li key={`${type}.${mode}`} className="card card-body space-y-3 py-3">
                <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
                  <h2 className="text-sm font-semibold text-ink">
                    {t(`printing.documents.${type}`)}
                    {MODES_FOR[type].length > 1 && (
                      <span className="ms-1.5 text-xs font-normal text-ink-muted">
                        {t(`printing.modes.${mode}`)}
                      </span>
                    )}
                  </h2>
                  <label className="field-check text-sm">
                    <input
                      type="checkbox"
                      checked={profile.preview}
                      onChange={(event) =>
                        setProfile(type, mode, {
                          ...profile,
                          preview: event.target.checked,
                        })
                      }
                    />
                    <span>{t('printing.preview')}</span>
                  </label>
                </div>

                <JobFields
                  label={t('printing.print')}
                  caption={
                    <span className="text-xs font-semibold tracking-wide text-ink-muted uppercase">
                      {t('printing.print')}
                    </span>
                  }
                  job={profile.first}
                  templates={draft.templates}
                  onChange={(first) => setProfile(type, mode, { ...profile, first })}
                />

                <JobFields
                  label={t('printing.secondPrint')}
                  caption={
                    <label className="field-check text-xs font-semibold tracking-wide text-ink-muted uppercase">
                      <input
                        type="checkbox"
                        checked={profile.second.enabled}
                        onChange={(event) =>
                          setProfile(type, mode, {
                            ...profile,
                            second: { ...profile.second, enabled: event.target.checked },
                          })
                        }
                      />
                      <span>{t('printing.secondPrint')}</span>
                    </label>
                  }
                  job={profile.second}
                  templates={draft.templates}
                  disabled={!profile.second.enabled}
                  onChange={(second) =>
                    setProfile(type, mode, {
                      ...profile,
                      second: { ...second, enabled: profile.second.enabled },
                    })
                  }
                />
              </li>
            );
          }),
        )}
      </ul>

      <label className="field-check text-sm">
        <input
          type="checkbox"
          checked={draft.copyLabels}
          onChange={(event) => {
            setSaved(false);
            setDraft({ ...draft, copyLabels: event.target.checked });
          }}
        />
        <span>{t('printing.copyLabels')}</span>
      </label>

      {/* Offered where the firm works in Gulf mode, and applied only there. */}
      {gulfMode && (
        <label className="field-check text-sm">
          <input
            type="checkbox"
            checked={draft.bilingual}
            onChange={(event) => {
              setSaved(false);
              setDraft({ ...draft, bilingual: event.target.checked });
            }}
          />
          <span>{t('printing.bilingual')}</span>
        </label>
      )}
    </section>
  );
}

/**
 * One print run of a document: the template, the printer it is meant for and the copies.
 *
 * The template takes a line of its own on a phone, where the three side by side left
 * each too narrow to read; from a small tablet up they share one.
 */
function JobFields({
  label,
  caption,
  job,
  templates,
  disabled = false,
  onChange,
}: {
  readonly label: string;
  readonly caption: React.ReactNode;
  readonly job: PrintJob;
  readonly templates: readonly PrintTemplate[];
  readonly disabled?: boolean;
  readonly onChange: (job: PrintJob) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <div role="group" aria-label={label} className="space-y-2 border-t border-line pt-3">
      {caption}
      <div className="grid grid-cols-[minmax(0,1fr)_4.5rem] gap-2 sm:grid-cols-[minmax(0,3fr)_minmax(0,2fr)_4.5rem]">
        <label className="col-span-2 min-w-0 sm:col-span-1">
          <span className="mb-1 block text-[11px] text-ink-muted">
            {t('printing.template')}
          </span>
          <select
            value={job.template}
            disabled={disabled}
            onChange={(event) => onChange({ ...job, template: event.target.value })}
            className="field-input-sm w-full"
          >
            {templates.map((template) => (
              <option key={template.id} value={template.id}>
                {template.name} ({template.paper})
              </option>
            ))}
          </select>
        </label>
        <label className="min-w-0">
          <span className="mb-1 block text-[11px] text-ink-muted">
            {t('printing.printer')}
          </span>
          <input
            value={job.printer}
            disabled={disabled}
            onChange={(event) => onChange({ ...job, printer: event.target.value })}
            placeholder={t('printing.printerPlaceholder')}
            className="field-input-sm w-full"
          />
        </label>
        <label className="min-w-0">
          <span className="mb-1 block text-[11px] text-ink-muted">
            {t('printing.copies')}
          </span>
          <input
            type="number"
            min={1}
            max={10}
            value={job.copies}
            disabled={disabled}
            onChange={(event) =>
              onChange({
                ...job,
                copies: Math.min(10, Math.max(1, Number(event.target.value) || 1)),
              })
            }
            className="field-input-sm w-full text-end"
          />
        </label>
      </div>
    </div>
  );
}
