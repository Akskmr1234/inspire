import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { moneyAlways } from '@/lib/money';
import { amountInWords } from '@/lib/amountInWords';
import type { PaperSize, PrintTemplate, PrintableDocument } from '@/lib/printing';

/** How wide each paper prints, and the margin the page rule gives it. */
export const PAPER: Readonly<
  Record<PaperSize, { readonly width: string; readonly page: string }>
> = {
  A4: { width: '210mm', page: 'A4 portrait' },
  A5: { width: '148mm', page: 'A5 portrait' },
  '80mm': { width: '80mm', page: '80mm auto' },
  '58mm': { width: '58mm', page: '58mm auto' },
};

const ROLL: readonly PaperSize[] = ['80mm', '58mm'];

const FONT: Readonly<Record<PrintTemplate['style']['fontScale'], string>> = {
  small: 'text-[10px]',
  normal: 'text-[11.5px]',
  large: 'text-[13px]',
};

/**
 * One document on one template.
 *
 * The same component draws the preview and the printed copies, so what is seen before
 * printing is what comes out of the printer. A till roll gets a narrow, ruled layout
 * rather than a shrunken A4: a 58 mm column cannot hold seven columns of figures.
 */
export function PrintedDocument({
  document,
  template,
  copyLabel,
}: {
  readonly document: PrintableDocument;
  readonly template: PrintTemplate;
  readonly copyLabel?: string | null;
}): React.JSX.Element {
  const { t } = useTranslation();
  const roll = ROLL.includes(template.paper);
  const accent = template.style.accent || '#1e293b';
  const title =
    template.title.trim() || t(`printing.titles.${document.type}.${document.mode}`);
  const arabic = t(`printing.titlesArabic.${document.type}.${document.mode}`);
  const company = template.company;
  const goods = document.goods.length > 0;
  const cols = template.columns;

  return (
    <div
      className={clsx(
        'printed-document relative mx-auto bg-white text-black',
        FONT[template.style.fontScale],
        roll ? 'p-2 font-mono leading-snug' : 'p-[10mm] leading-relaxed',
      )}
      style={{ width: PAPER[template.paper].width }}
    >
      {document.watermark && (
        <div
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 flex items-center justify-center"
        >
          <span className="-rotate-30 text-6xl font-black tracking-widest text-black/10 uppercase">
            {t(`printing.watermark.${document.watermark}`)}
          </span>
        </div>
      )}

      {copyLabel && (
        <div className="mb-1 text-end text-[9px] font-semibold tracking-wider uppercase">
          {copyLabel}
        </div>
      )}

      {/* Company and title */}
      <header
        className={clsx(
          roll ? 'text-center' : 'flex items-start justify-between gap-4 pb-3',
          !roll && template.style.borders && 'border-b-2',
        )}
        style={!roll ? { borderColor: accent } : undefined}
      >
        <div className={clsx(roll ? 'space-y-0.5' : 'flex items-start gap-3')}>
          {company.logo && (
            <img
              src={company.logo}
              alt=""
              className={clsx(
                'object-contain',
                roll ? 'mx-auto max-h-10' : 'max-h-16 max-w-28',
              )}
            />
          )}
          <div>
            {company.name && (
              <div className="text-[1.5em] font-black">{company.name}</div>
            )}
            {company.nameArabic && (
              <div dir="rtl" className="font-semibold">
                {company.nameArabic}
              </div>
            )}
            {company.details
              .split('\n')
              .filter((line) => line.trim() !== '')
              .map((line) => (
                <div key={line}>{line}</div>
              ))}
            {company.taxNumber && (
              <div>
                {t('printing.taxNumber')}:{' '}
                <span className="font-mono">{company.taxNumber}</span>
              </div>
            )}
          </div>
        </div>

        <div
          className={clsx(
            roll ? 'mt-2 border-y border-dashed border-black py-1' : 'text-end',
          )}
        >
          <div
            className={clsx(
              'font-black tracking-wide uppercase',
              roll
                ? 'text-[1.2em]'
                : 'inline-block rounded px-3 py-1 text-[1.25em] text-white',
            )}
            style={!roll ? { backgroundColor: accent } : undefined}
          >
            {title}
          </div>
          {template.show.arabicTitle && arabic && (
            <div dir="rtl" className="font-semibold">
              {arabic}
            </div>
          )}
          <div className={clsx(roll ? '' : 'mt-2', 'font-mono')}>
            {t('printing.number')}: <b>{document.number}</b>
          </div>
          <div className="font-mono">
            {t('printing.date')}: {document.date}
          </div>
          {template.show.reference && document.reference && (
            <div className="font-mono">
              {t('printing.reference')}: {document.reference}
            </div>
          )}
        </div>
      </header>

      {/* Party */}
      {(document.party || (template.show.warehouse && document.warehouse)) && (
        <section
          className={clsx(
            roll
              ? 'border-b border-dashed border-black py-1'
              : 'grid grid-cols-2 gap-4 py-3',
          )}
        >
          {document.party && document.partyLabel && (
            <div>
              <div className="text-[0.85em] font-bold tracking-wider uppercase opacity-70">
                {t(`printing.party.${document.partyLabel}`)}
              </div>
              <div className="font-bold">{document.party.name}</div>
              {template.show.partyAddress &&
                document.party.lines.map((line) => <div key={line}>{line}</div>)}
              {template.show.partyTaxNumber && document.party.taxNumber && (
                <div>
                  {t('printing.taxNumber')}: {document.party.taxNumber}
                </div>
              )}
            </div>
          )}
          {template.show.warehouse && document.warehouse && (
            <div className={clsx(!roll && 'text-end')}>
              <div className="text-[0.85em] font-bold tracking-wider uppercase opacity-70">
                {t('printing.warehouse')}
              </div>
              <div>{document.warehouse}</div>
            </div>
          )}
        </section>
      )}

      {/* Goods */}
      {goods && (
        <table className="mt-1 w-full border-collapse">
          <thead>
            <tr
              className={clsx(
                'text-[0.85em] font-bold uppercase',
                roll
                  ? 'border-b border-dashed border-black'
                  : template.style.borders && 'border-y-2',
              )}
              style={!roll ? { borderColor: accent } : undefined}
            >
              {!roll && <th className="w-6 py-1 text-start">#</th>}
              <th className="py-1 text-start">{t('printing.item')}</th>
              <th className="py-1 text-end">{t('printing.quantity')}</th>
              {cols.unit && !roll && (
                <th className="py-1 text-start ps-1">{t('printing.unit')}</th>
              )}
              {cols.rate && <th className="py-1 text-end">{t('printing.rate')}</th>}
              {cols.discount && !roll && (
                <th className="py-1 text-end">{t('printing.discount')}</th>
              )}
              {cols.taxPercent && !roll && document.mode === 'tax' && (
                <th className="py-1 text-end">{t('printing.taxPercent')}</th>
              )}
              {cols.tax && document.mode === 'tax' && (
                <th className="py-1 text-end">{t('printing.tax')}</th>
              )}
              <th className="py-1 text-end">{t('printing.amount')}</th>
            </tr>
          </thead>
          <tbody>
            {document.goods.map((line, index) => (
              <tr
                key={`${line.description}-${String(index)}`}
                className={clsx(
                  'align-top',
                  template.style.borders && !roll && 'border-b border-black/15',
                )}
              >
                {!roll && <td className="py-1">{index + 1}</td>}
                <td className="py-1 pe-1">
                  <div className="font-semibold">{line.description}</div>
                  {template.show.productCode && line.code && (
                    <div className="font-mono text-[0.85em] opacity-70">{line.code}</div>
                  )}
                  {template.show.serials && line.serials.length > 0 && (
                    <div className="font-mono text-[0.85em]">
                      {t('printing.serials')}: {line.serials.join(', ')}
                    </div>
                  )}
                </td>
                <td className="py-1 text-end font-mono">{line.quantity}</td>
                {cols.unit && !roll && <td className="py-1 ps-1">{line.unit ?? ''}</td>}
                {cols.rate && (
                  <td className="py-1 text-end font-mono">{moneyAlways(line.rate)}</td>
                )}
                {cols.discount && !roll && (
                  <td className="py-1 text-end font-mono">
                    {line.discount > 0 ? moneyAlways(line.discount) : ''}
                  </td>
                )}
                {cols.taxPercent && !roll && document.mode === 'tax' && (
                  <td className="py-1 text-end font-mono">{line.taxPercent}%</td>
                )}
                {cols.tax && document.mode === 'tax' && (
                  <td className="py-1 text-end font-mono">{moneyAlways(line.tax)}</td>
                )}
                <td className="py-1 text-end font-mono font-semibold">
                  {moneyAlways(line.total)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {/* Entries, for a voucher */}
      {document.entries.length > 0 && (
        <table className="mt-2 w-full border-collapse">
          <thead>
            <tr
              className={clsx(
                'text-[0.85em] font-bold uppercase',
                template.style.borders && 'border-y-2',
              )}
              style={{ borderColor: accent }}
            >
              <th className="py-1 text-start">{t('printing.account')}</th>
              <th className="py-1 text-end">{t('printing.debit')}</th>
              <th className="py-1 text-end">{t('printing.credit')}</th>
            </tr>
          </thead>
          <tbody>
            {document.entries.map((entry, index) => (
              <tr
                key={`${entry.account}-${String(index)}`}
                className={clsx(template.style.borders && 'border-b border-black/15')}
              >
                <td className="py-1">
                  <div className="font-semibold">{entry.account}</div>
                  {entry.narration && (
                    <div className="text-[0.85em]">{entry.narration}</div>
                  )}
                </td>
                <td className="py-1 text-end font-mono">
                  {entry.debit > 0 ? moneyAlways(entry.debit) : ''}
                </td>
                <td className="py-1 text-end font-mono">
                  {entry.credit > 0 ? moneyAlways(entry.credit) : ''}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {/* Totals */}
      <section
        className={clsx(
          'mt-2 flex gap-4',
          roll
            ? 'flex-col border-t border-dashed border-black pt-1'
            : 'items-start justify-between',
        )}
      >
        <div className="min-w-0 flex-1 space-y-1">
          {template.show.amountInWords && (
            <div>
              <span className="text-[0.85em] font-bold uppercase opacity-70">
                {t('printing.inWords')}:
              </span>{' '}
              <i>
                {document.currency} {amountInWords(document.grandTotal)}
              </i>
            </div>
          )}
          {template.show.narration && document.narration && (
            <div>
              <span className="text-[0.85em] font-bold uppercase opacity-70">
                {t('printing.narration')}:
              </span>{' '}
              {document.narration}
            </div>
          )}
        </div>
        <div className={clsx(roll ? 'w-full' : 'w-[45%]', 'space-y-0.5 font-mono')}>
          {document.totals.map((row) => (
            <div key={row.label} className="flex justify-between gap-2">
              <span>{row.label}</span>
              <span>{moneyAlways(row.amount)}</span>
            </div>
          ))}
          <div
            className={clsx(
              'flex justify-between gap-2 pt-1 text-[1.2em] font-black',
              template.style.borders && 'border-t-2',
            )}
            style={{ borderColor: accent }}
          >
            <span className="font-sans uppercase">{t('printing.total')}</span>
            <span>
              {moneyAlways(document.grandTotal)} {roll ? '' : document.currency}
            </span>
          </div>
        </div>
      </section>

      {/* Terms, note and signatures */}
      {template.footer.terms.trim() !== '' && (
        <section className="mt-3 text-[0.9em] whitespace-pre-line">
          <div className="font-bold uppercase opacity-70">{t('printing.terms')}</div>
          {template.footer.terms}
        </section>
      )}

      {template.show.signatures &&
        (template.footer.signatureLeft || template.footer.signatureRight) && (
          <section className="mt-10 flex justify-between gap-6">
            {[template.footer.signatureLeft, template.footer.signatureRight].map(
              (label, index) => (
                <div key={`${label}-${String(index)}`} className="w-44 text-center">
                  {label && <div className="border-t border-black pt-1">{label}</div>}
                </div>
              ),
            )}
          </section>
        )}

      {template.footer.note.trim() !== '' && (
        <footer
          className={clsx(
            'mt-3 text-center text-[0.9em]',
            roll && 'border-t border-dashed border-black pt-1',
          )}
        >
          {template.footer.note}
        </footer>
      )}
    </div>
  );
}
