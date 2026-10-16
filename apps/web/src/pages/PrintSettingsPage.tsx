import { useEffect, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
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

      <p className="text-sm text-ink-muted">{t('printing.settingsHint')}</p>
      <p className="alert-info text-xs">{t('printing.printerHint')}</p>

      {saved && <p className="alert-success">{t('printing.savedNotice')}</p>}
      {save.isError && (
        <p className="alert-error">{save.error.detail || save.error.code}</p>
      )}

      <div className="table-wrap">
        <table className="table min-w-[64rem] text-sm">
          <thead>
            <tr>
              <th className="text-start">{t('printing.document')}</th>
              <th className="text-center">{t('printing.preview')}</th>
              <th className="text-start">{t('printing.template')}</th>
              <th className="text-start">{t('printing.printer')}</th>
              <th className="text-end">{t('printing.copies')}</th>
              <th className="text-start border-s border-line">
                {t('printing.secondPrint')}
              </th>
              <th className="text-start">{t('printing.template')}</th>
              <th className="text-start">{t('printing.printer')}</th>
              <th className="text-end">{t('printing.copies')}</th>
            </tr>
          </thead>
          <tbody>
            {PRINT_DOCUMENTS.flatMap((type) =>
              MODES_FOR[type].map((mode) => {
                const profile = profileFor(draft, type, mode);

                return (
                  <tr key={`${type}.${mode}`}>
                    <td className="whitespace-nowrap">
                      <span className="font-medium">
                        {t(`printing.documents.${type}`)}
                      </span>
                      {MODES_FOR[type].length > 1 && (
                        <span className="ms-1.5 text-xs text-ink-muted">
                          {t(`printing.modes.${mode}`)}
                        </span>
                      )}
                    </td>
                    <td className="text-center">
                      <input
                        type="checkbox"
                        checked={profile.preview}
                        onChange={(event) =>
                          setProfile(type, mode, {
                            ...profile,
                            preview: event.target.checked,
                          })
                        }
                        aria-label={t('printing.preview')}
                      />
                    </td>
                    <JobCells
                      job={profile.first}
                      templates={draft.templates}
                      onChange={(first) => setProfile(type, mode, { ...profile, first })}
                    />
                    <td className="border-s border-line text-center">
                      <input
                        type="checkbox"
                        checked={profile.second.enabled}
                        onChange={(event) =>
                          setProfile(type, mode, {
                            ...profile,
                            second: { ...profile.second, enabled: event.target.checked },
                          })
                        }
                        aria-label={t('printing.secondPrint')}
                      />
                    </td>
                    <JobCells
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
                  </tr>
                );
              }),
            )}
          </tbody>
        </table>
      </div>

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
    </section>
  );
}

function JobCells({
  job,
  templates,
  disabled = false,
  onChange,
}: {
  readonly job: PrintJob;
  readonly templates: readonly PrintTemplate[];
  readonly disabled?: boolean;
  readonly onChange: (job: PrintJob) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <>
      <td>
        <select
          value={job.template}
          disabled={disabled}
          onChange={(event) => onChange({ ...job, template: event.target.value })}
          aria-label={t('printing.template')}
          className="field-input-sm min-w-40"
        >
          {templates.map((template) => (
            <option key={template.id} value={template.id}>
              {template.name} ({template.paper})
            </option>
          ))}
        </select>
      </td>
      <td>
        <input
          value={job.printer}
          disabled={disabled}
          onChange={(event) => onChange({ ...job, printer: event.target.value })}
          placeholder={t('printing.printerPlaceholder')}
          aria-label={t('printing.printer')}
          className="field-input-sm w-36"
        />
      </td>
      <td className="text-end">
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
          aria-label={t('printing.copies')}
          className="field-input-sm w-16 text-end"
        />
      </td>
    </>
  );
}
