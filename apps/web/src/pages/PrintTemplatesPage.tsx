import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { PageHeading } from '@/components/PageHeading';
import { Spinner } from '@/components/ReportFrame';
import { FitToWidth } from '@/components/print/FitToWidth';
import { PrintedDocument } from '@/components/print/PrintedDocument';
import type { ApiError } from '@/lib/api';
import {
  BUILT_IN_TEMPLATES,
  savePrintSetup,
  usePrintSetup,
  type PaperSize,
  type PrintDocumentType,
  type PrintSetup,
  type PrintTemplate,
  type PrintableDocument,
} from '@/lib/printing';

const PAPERS: readonly PaperSize[] = ['A4', 'A5', '80mm', '58mm'];

/** A document to design against, so the preview shows every part a template can show. */
function sampleDocument(
  type: PrintDocumentType,
  t: (key: string) => string,
): PrintableDocument {
  const voucher = type === 'receipt' || type === 'payment' || type === 'journal';

  return {
    type,
    mode: 'tax',
    number: 'SL/2026/0042',
    date: new Date().toISOString().slice(0, 10),
    reference: 'PO-7781',
    narration: t('printing.sample.narration'),
    currency: 'QAR',
    partyLabel: voucher ? (type === 'payment' ? 'paidTo' : 'receivedFrom') : 'billTo',
    party: {
      name: 'Al-Noor Trading LLC',
      code: 'C-0001',
      lines: ['Building 12, Street 340, Doha', '+974 5555 0101'],
      taxNumber: '300112233400003',
    },
    warehouse: 'Main warehouse',
    goods: voucher
      ? []
      : [
          {
            description: 'Samsung Galaxy S24 128GB',
            code: 'MOB-S24',
            serials: ['356916756701'],
            quantity: 1,
            unit: 'PCS',
            rate: 2999,
            discount: 0,
            taxPercent: 5,
            tax: 149.95,
            total: 3148.95,
          },
          {
            description: 'USB-C charger 25W',
            code: 'CHG-25W',
            serials: [],
            quantity: 2,
            unit: 'PCS',
            rate: 45,
            discount: 5,
            taxPercent: 5,
            tax: 4.25,
            total: 89.25,
          },
        ],
    entries: voucher
      ? [
          { account: 'Cash in Hand', narration: null, debit: 500, credit: 0 },
          {
            account: 'Al-Noor Trading LLC',
            narration: 'Against SL/2026/0042',
            debit: 0,
            credit: 500,
          },
        ]
      : [],
    totals: voucher
      ? []
      : [
          { label: t('printing.taxable'), amount: 3084 },
          { label: t('printing.taxTotal'), amount: 154.2 },
        ],
    grandTotal: voucher ? 500 : 3238.2,
    watermark: null,
    customerId: null,
  };
}

/** Shrinks a logo so it travels inside the settings rather than as a file of its own. */
function shrinkLogo(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new Error('unreadable'));
    reader.onload = () => {
      const image = new Image();
      image.onerror = () => reject(new Error('not an image'));
      image.onload = () => {
        const scale = Math.min(1, 400 / image.width, 160 / image.height);
        const canvas = document.createElement('canvas');
        canvas.width = Math.round(image.width * scale);
        canvas.height = Math.round(image.height * scale);
        canvas.getContext('2d')?.drawImage(image, 0, 0, canvas.width, canvas.height);
        resolve(canvas.toDataURL('image/png'));
      };
      image.src = String(reader.result);
    };
    reader.readAsDataURL(file);
  });
}

/**
 * Designs the layouts documents print on.
 *
 * The firm's own name, address, tax number and logo; which parts of a document appear;
 * the columns of the item table; the terms, note and signature lines; the paper, the
 * type size and the colour. The preview beside it is the same component that prints, so
 * it is what will come out.
 */
export function PrintTemplatesPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { setup, loading, error } = usePrintSetup();

  const [draft, setDraft] = useState<PrintSetup | null>(null);
  const [selectedId, setSelectedId] = useState<string>('');
  const [sampleType, setSampleType] = useState<PrintDocumentType>('salesInvoice');
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    if (!loading) {
      setDraft(setup);
      setSelectedId((current) => current || setup.templates[0]?.id || '');
    }
  }, [loading, setup]);

  const save = useMutation<void, ApiError, PrintSetup>({
    mutationFn: savePrintSetup,
    onSuccess: () => {
      setNotice(t('printing.templatesSaved'));
      void queryClient.invalidateQueries({ queryKey: ['print-setup'] });
    },
  });

  const sample = useMemo(() => sampleDocument(sampleType, t), [sampleType, t]);

  if (loading || !draft) {
    return (
      <section className="page">
        <PageHeading title={t('printing.templatesTitle')} />
        {error ? (
          <p className="alert-error">{error.detail || error.code}</p>
        ) : (
          <Spinner />
        )}
      </section>
    );
  }

  const selected =
    draft.templates.find((template) => template.id === selectedId) ?? draft.templates[0];

  const replace = (next: PrintTemplate): void => {
    setNotice(null);
    setDraft({
      ...draft,
      templates: draft.templates.map((template) =>
        template.id === next.id ? next : template,
      ),
    });
  };

  const add = (from: PrintTemplate): void => {
    const copy: PrintTemplate = {
      ...from,
      id: crypto.randomUUID(),
      name: t('printing.copyOf', { name: from.name }),
    };
    setNotice(null);
    setDraft({ ...draft, templates: [...draft.templates, copy] });
    setSelectedId(copy.id);
  };

  // The company is usually the same on every layout; typing it four times is how the
  // till roll ends up with last year's phone number.
  const shareCompany = (from: PrintTemplate): void => {
    setNotice(t('printing.companyShared'));
    setDraft({
      ...draft,
      templates: draft.templates.map((template) => ({
        ...template,
        company: from.company,
      })),
    });
  };

  const remove = (id: string): void => {
    const templates = draft.templates.filter((template) => template.id !== id);
    if (templates.length === 0) return;
    setNotice(null);
    setDraft({ ...draft, templates });
    setSelectedId(templates[0]!.id);
  };

  return (
    <section className="page">
      <PageHeading
        title={t('printing.templatesTitle')}
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

      <p className="text-sm text-ink-muted">{t('printing.templatesHint')}</p>
      {notice && <p className="alert-success">{notice}</p>}
      {save.isError && (
        <p className="alert-error">{save.error.detail || save.error.code}</p>
      )}

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-[14rem_24rem_minmax(0,1fr)]">
        <div className="space-y-2">
          <ul className="panel divide-y divide-line p-0">
            {draft.templates.map((template) => (
              <li key={template.id}>
                <button
                  type="button"
                  onClick={() => setSelectedId(template.id)}
                  className={clsx(
                    'w-full px-3 py-2 text-start text-sm hover:bg-surface-2',
                    selected?.id === template.id &&
                      'bg-brand-50 font-semibold dark:bg-brand-500/10',
                  )}
                >
                  <span className="block">{template.name}</span>
                  <span className="block text-xs text-ink-muted">{template.paper}</span>
                </button>
              </li>
            ))}
          </ul>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => add(BUILT_IN_TEMPLATES[0]!)}
              className="btn-secondary btn-sm"
            >
              {t('printing.newTemplate')}
            </button>
            {selected && (
              <>
                <button
                  type="button"
                  onClick={() => add(selected)}
                  className="btn-secondary btn-sm"
                >
                  {t('printing.duplicate')}
                </button>
                <button
                  type="button"
                  onClick={() => shareCompany(selected)}
                  className="btn-secondary btn-sm"
                >
                  {t('printing.shareCompany')}
                </button>
                <button
                  type="button"
                  disabled={draft.templates.length <= 1}
                  onClick={() => remove(selected.id)}
                  className="btn-secondary btn-sm text-red-600"
                >
                  {t('printing.deleteTemplate')}
                </button>
              </>
            )}
          </div>
        </div>

        {selected && <TemplateForm template={selected} onChange={replace} />}

        {selected && (
          <div className="min-w-0 space-y-2">
            <label className="field max-w-xs">
              <span className="field-label">{t('printing.previewAs')}</span>
              <select
                value={sampleType}
                onChange={(event) =>
                  setSampleType(event.target.value as PrintDocumentType)
                }
                className="field-input-sm"
              >
                {(['salesInvoice', 'purchaseInvoice', 'receipt'] as const).map((type) => (
                  <option key={type} value={type}>
                    {t(`printing.documents.${type}`)}
                  </option>
                ))}
              </select>
            </label>
            <div className="rounded-xl bg-slate-200 p-2 sm:p-4 dark:bg-slate-900">
              <FitToWidth>
                <PrintedDocument document={sample} template={selected} />
              </FitToWidth>
            </div>
          </div>
        )}
      </div>
    </section>
  );
}

function TemplateForm({
  template,
  onChange,
}: {
  readonly template: PrintTemplate;
  readonly onChange: (template: PrintTemplate) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [logoError, setLogoError] = useState<string | null>(null);

  const set = <K extends keyof PrintTemplate>(key: K, value: PrintTemplate[K]): void =>
    onChange({ ...template, [key]: value });

  const text = (
    label: string,
    value: string,
    change: (value: string) => void,
    multiline = false,
  ): React.JSX.Element => (
    <label className="field">
      <span className="field-label">{label}</span>
      {multiline ? (
        <textarea
          value={value}
          rows={3}
          onChange={(event) => change(event.target.value)}
          className="field-input"
        />
      ) : (
        <input
          value={value}
          onChange={(event) => change(event.target.value)}
          className="field-input"
        />
      )}
    </label>
  );

  const check = (
    label: string,
    value: boolean,
    change: (value: boolean) => void,
  ): React.JSX.Element => (
    <label className="field-check text-sm">
      <input
        type="checkbox"
        checked={value}
        onChange={(event) => change(event.target.checked)}
      />
      <span>{label}</span>
    </label>
  );

  return (
    <div className="panel space-y-4">
      <fieldset className="min-w-0 space-y-2">
        <legend className="text-sm font-semibold">{t('printing.general')}</legend>
        {text(t('printing.templateName'), template.name, (value) => set('name', value))}
        <label className="field">
          <span className="field-label">{t('printing.paper')}</span>
          <select
            value={template.paper}
            onChange={(event) => set('paper', event.target.value as PaperSize)}
            className="field-input"
          >
            {PAPERS.map((paper) => (
              <option key={paper} value={paper}>
                {t(`printing.papers.${paper}`)}
              </option>
            ))}
          </select>
        </label>
        {text(t('printing.titleOverride'), template.title, (value) =>
          set('title', value),
        )}
      </fieldset>

      <fieldset className="min-w-0 space-y-2">
        <legend className="text-sm font-semibold">{t('printing.company')}</legend>
        {text(t('printing.companyName'), template.company.name, (value) =>
          set('company', { ...template.company, name: value }),
        )}
        {text(t('printing.companyNameArabic'), template.company.nameArabic, (value) =>
          set('company', { ...template.company, nameArabic: value }),
        )}
        {text(
          t('printing.companyDetails'),
          template.company.details,
          (value) => set('company', { ...template.company, details: value }),
          true,
        )}
        {text(t('printing.taxNumber'), template.company.taxNumber, (value) =>
          set('company', { ...template.company, taxNumber: value }),
        )}
        <div className="field">
          <span className="field-label">{t('printing.logo')}</span>
          <div className="flex items-center gap-2">
            {template.company.logo && (
              <img
                src={template.company.logo}
                alt=""
                className="max-h-10 rounded border border-line"
              />
            )}
            <input
              type="file"
              accept="image/*"
              onChange={(event) => {
                const file = event.target.files?.[0];
                if (!file) return;
                setLogoError(null);
                shrinkLogo(file)
                  .then((logo) => set('company', { ...template.company, logo }))
                  .catch(() => setLogoError(t('printing.logoUnreadable')));
              }}
              className="w-full max-w-full text-xs"
            />
            {template.company.logo && (
              <button
                type="button"
                onClick={() => set('company', { ...template.company, logo: '' })}
                className="btn-ghost btn-xs"
              >
                {t('printing.removeLogo')}
              </button>
            )}
          </div>
          {logoError && <span className="text-xs text-red-600">{logoError}</span>}
        </div>
      </fieldset>

      <fieldset className="min-w-0 space-y-1">
        <legend className="text-sm font-semibold">{t('printing.showSection')}</legend>
        {(Object.keys(template.show) as (keyof PrintTemplate['show'])[]).map((key) => (
          <div key={key}>
            {check(t(`printing.show.${key}`), template.show[key], (value) =>
              set('show', { ...template.show, [key]: value }),
            )}
          </div>
        ))}
      </fieldset>

      <fieldset className="min-w-0 space-y-1">
        <legend className="text-sm font-semibold">{t('printing.columnsSection')}</legend>
        {(Object.keys(template.columns) as (keyof PrintTemplate['columns'])[]).map(
          (key) => (
            <div key={key}>
              {check(t(`printing.columns.${key}`), template.columns[key], (value) =>
                set('columns', { ...template.columns, [key]: value }),
              )}
            </div>
          ),
        )}
      </fieldset>

      <fieldset className="min-w-0 space-y-2">
        <legend className="text-sm font-semibold">{t('printing.footerSection')}</legend>
        {text(
          t('printing.termsLabel'),
          template.footer.terms,
          (value) => set('footer', { ...template.footer, terms: value }),
          true,
        )}
        {text(t('printing.note'), template.footer.note, (value) =>
          set('footer', { ...template.footer, note: value }),
        )}
        {text(t('printing.signatureLeft'), template.footer.signatureLeft, (value) =>
          set('footer', { ...template.footer, signatureLeft: value }),
        )}
        {text(t('printing.signatureRight'), template.footer.signatureRight, (value) =>
          set('footer', { ...template.footer, signatureRight: value }),
        )}
      </fieldset>

      <fieldset className="min-w-0 space-y-2">
        <legend className="text-sm font-semibold">{t('printing.styleSection')}</legend>
        <label className="field">
          <span className="field-label">{t('printing.fontSize')}</span>
          <select
            value={template.style.fontScale}
            onChange={(event) =>
              set('style', {
                ...template.style,
                fontScale: event.target.value as PrintTemplate['style']['fontScale'],
              })
            }
            className="field-input"
          >
            {(['small', 'normal', 'large'] as const).map((scale) => (
              <option key={scale} value={scale}>
                {t(`printing.fontScales.${scale}`)}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span className="field-label">{t('printing.accent')}</span>
          <input
            type="color"
            value={template.style.accent}
            onChange={(event) =>
              set('style', { ...template.style, accent: event.target.value })
            }
            className="h-9 w-20 rounded border border-line"
          />
        </label>
        {check(t('printing.borders'), template.style.borders, (value) =>
          set('style', { ...template.style, borders: value }),
        )}
      </fieldset>
    </div>
  );
}
