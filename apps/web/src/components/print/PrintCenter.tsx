import { useEffect, useRef, useState } from 'react';
import { createPortal, flushSync } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { IconPrinter } from '@/components/icons';
import { Spinner } from '@/components/ReportFrame';
import {
  profileFor,
  templateFor,
  usePrintSetup,
  type PrintTemplate,
  type PrintableDocument,
} from '@/lib/printing';
import { FitToWidth } from '@/components/print/FitToWidth';
import { PAPER, PrintedDocument } from '@/components/print/PrintedDocument';

interface Run {
  readonly template: PrintTemplate;
  readonly copies: number;
}

/**
 * Previews a document and prints it as the firm set it up for that kind and mode.
 *
 * The settings decide the defaults - which layout, how many copies, whether a second run
 * on another layout follows - and the toolbar lets this one print differ. Each run is
 * rendered on its own into a container that is the only thing on the page while it
 * prints, so the screen behind the dialog never comes out of the printer.
 *
 * The printer itself is the browser's to choose, in its own dialog; the name the firm
 * gave is shown so whoever prints knows which to pick.
 */
export function PrintCenter({
  document,
  onDone,
}: {
  readonly document: PrintableDocument;
  /** Called after an automatic print, when the settings say not to preview. */
  readonly onDone?: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const { setup, loading } = usePrintSetup();
  const profile = profileFor(setup, document.type, document.mode);

  // A customer with print settings of their own gets them on the first run.
  const own = document.customerId ? setup.customerPrint[document.customerId] : undefined;
  const ownTemplate = own?.template || undefined;
  const preview = own?.preview ?? profile.preview;

  const [templateId, setTemplateId] = useState<string | null>(null);
  const [copies, setCopies] = useState<number | null>(null);
  const [withSecond, setWithSecond] = useState<boolean | null>(null);
  const [run, setRun] = useState<Run | null>(null);
  const autoPrinted = useRef(false);

  const firstTemplate = templateFor(
    setup,
    templateId ?? ownTemplate ?? profile.first.template,
  );
  const firstCopies = copies ?? Math.max(1, own?.copies ?? profile.first.copies);
  const second = withSecond ?? profile.second.enabled;
  const secondTemplate = templateFor(setup, profile.second.template);

  const printNow = (): void => {
    const runs: Run[] = [{ template: firstTemplate, copies: firstCopies }];

    if (second) {
      runs.push({ template: secondTemplate, copies: Math.max(1, profile.second.copies) });
    }

    for (const next of runs) {
      // Rendered before the dialog opens: print() reads the page as it stands.
      flushSync(() => setRun(next));
      window.print();
    }

    setRun(null);
  };

  // No preview: print as soon as the settings are known, then hand back.
  useEffect(() => {
    if (loading || preview || autoPrinted.current) return;

    autoPrinted.current = true;

    // Outside the effect, so the run renders before the dialog opens.
    window.setTimeout(() => {
      printNow();
      onDone?.();
    }, 0);
  }, [loading, preview]);

  if (loading) {
    return <Spinner />;
  }

  return (
    <div className="flex min-h-0 flex-col gap-3">
      <div className="no-print flex flex-wrap items-end gap-3 rounded-xl border border-line bg-surface-2/40 p-3 text-sm">
        <label className="field">
          <span className="field-label">{t('printing.template')}</span>
          <select
            value={firstTemplate.id}
            onChange={(event) => setTemplateId(event.target.value)}
            className="field-input-sm"
          >
            {setup.templates.map((template) => (
              <option key={template.id} value={template.id}>
                {template.name} ({template.paper})
              </option>
            ))}
          </select>
        </label>

        <label className="field">
          <span className="field-label">{t('printing.copies')}</span>
          <input
            type="number"
            min={1}
            max={10}
            value={firstCopies}
            onChange={(event) =>
              setCopies(Math.min(10, Math.max(1, Number(event.target.value) || 1)))
            }
            className="field-input-sm w-20"
          />
        </label>

        <label className="field-check">
          <input
            type="checkbox"
            checked={second}
            onChange={(event) => setWithSecond(event.target.checked)}
          />
          <span>
            {t('printing.alsoPrint', {
              template: secondTemplate.name,
              copies: Math.max(1, profile.second.copies),
            })}
          </span>
        </label>

        <div className="ms-auto flex flex-col items-end gap-1">
          {(profile.first.printer || (second && profile.second.printer)) && (
            <span className="text-xs text-ink-muted">
              {t('printing.choosePrinter', {
                printer: [profile.first.printer, second ? profile.second.printer : '']
                  .filter(Boolean)
                  .join(' → '),
              })}
            </span>
          )}
          <button type="button" onClick={printNow} className="btn-primary btn-sm">
            <IconPrinter className="size-4" />
            {t('printing.print')}
          </button>
        </div>
      </div>

      <div className="no-print min-h-0 flex-1 overflow-y-auto rounded-xl bg-slate-200 p-2 sm:p-4 dark:bg-slate-900">
        <FitToWidth>
          <PrintedDocument document={document} template={firstTemplate} />
        </FitToWidth>
      </div>

      {run &&
        createPortal(
          <div className="print-root">
            <style>{`@page { size: ${PAPER[run.template.paper].page}; margin: 0; }`}</style>
            {Array.from({ length: run.copies }, (_, index) => (
              <PrintedDocument
                key={index}
                document={document}
                template={run.template}
                copyLabel={
                  setup.copyLabels && run.copies > 1
                    ? t(`printing.copy.${String(Math.min(index + 1, 4))}`)
                    : null
                }
              />
            ))}
          </div>,
          window.document.body,
        )}
    </div>
  );
}
