import { createContext, useContext, useEffect, useId, useRef, useState } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { Field } from '@/components/Form';
import { IconClose, IconReceipt, IconTrash } from '@/components/icons';
import { shortcutFor, shortcutProps, type EntryAction } from '@/lib/shortcuts';

/*
  The pieces every invoice is built from - sales and purchase, their returns, orders
  and quotations - so the screens read alike: a header that names the document, the
  party in two lines under it, the items taking whatever height is left, the money
  under them, and the buttons that save it along the very foot.
*/

/** Asks to close the window: straight away, or after asking when there is work in it. */
const RequestClose = createContext<() => void>(() => {});

/**
 * The whole window an invoice is entered in.
 *
 * A dialog still - Escape closes it, focus stays inside, the list stays underneath -
 * but with no title bar of its own: the invoice's header is the title bar, and the
 * bar along its foot holds the buttons that finish it.
 *
 * With work in it (`dirty`), Escape, the cross and Cancel ask before throwing it
 * away. A box of figures takes the whole of what is in it when it is entered, so a
 * quantity of 1 is typed over rather than typed after.
 */
export function InvoiceWindow({
  title,
  onClose,
  dirty = false,
  children,
}: {
  /** What a screen reader announces the window as. */
  readonly title: string;
  readonly onClose: () => void;
  /** Something has been entered that closing would lose. */
  readonly dirty?: boolean;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  const [confirming, setConfirming] = useState(false);
  const clicked = useRef<EventTarget | null>(null);

  const requestClose = (): void => {
    if (dirty) setConfirming(true);
    else onClose();
  };

  return (
    <Dialog.Root open onOpenChange={(open) => !open && requestClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-slate-950/65 backdrop-blur-md dark:bg-black/80" />
        <Dialog.Content
          aria-describedby={undefined}
          className="animate-rise fixed inset-0 z-50 flex flex-col bg-canvas outline-none"
          onFocusCapture={(event) => {
            const field = event.target;
            if (
              field instanceof HTMLInputElement &&
              (field.type === 'number' || field.inputMode === 'decimal')
            ) {
              field.select();
              clicked.current = field;
            }
          }}
          // The click that focused the box would otherwise put the caret where it
          // landed and undo the selection.
          onMouseUpCapture={(event) => {
            if (clicked.current === event.target) event.preventDefault();
            clicked.current = null;
          }}
        >
          <Dialog.Title className="sr-only">{title}</Dialog.Title>
          <RequestClose.Provider value={requestClose}>
            <div className="flex min-h-0 flex-1 flex-col overflow-y-auto overscroll-contain">
              {children}
            </div>
          </RequestClose.Provider>
          {confirming && (
            <DiscardPrompt onKeep={() => setConfirming(false)} onDiscard={onClose} />
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

/** Whether to throw away an invoice with work in it. Keeping it is the default. */
function DiscardPrompt({
  onKeep,
  onDiscard,
}: {
  readonly onKeep: () => void;
  readonly onDiscard: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  // Escape answers "keep" - caught at the window, ahead of the dialog's own
  // listener, so it does not ask again.
  useEffect(() => {
    const escape = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return;
      event.stopPropagation();
      onKeep();
    };
    window.addEventListener('keydown', escape, true);
    return () => window.removeEventListener('keydown', escape, true);
  }, [onKeep]);

  return (
    <div className="fixed inset-0 z-40 grid place-items-center bg-slate-950/40 p-4">
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="invoice-discard-title"
        className="animate-rise w-full max-w-sm rounded-2xl border border-line bg-surface p-5 shadow-float"
      >
        <h3 id="invoice-discard-title" className="text-base font-semibold text-ink">
          {t('invoice.discardTitle')}
        </h3>
        <p className="mt-1.5 text-sm text-ink-muted">{t('invoice.discardBody')}</p>
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" onClick={onKeep} className="btn-secondary" autoFocus>
            {t('invoice.keepEditing')}
          </button>
          <button type="button" onClick={onDiscard} className="btn-danger">
            {t('invoice.discard')}
          </button>
        </div>
      </div>
    </div>
  );
}

/** A way to finish the invoice: a button in the bar along its foot. */
export interface InvoiceAction {
  readonly label: string;
  readonly onClick: () => void;
  readonly shortcut?: EntryAction;
  readonly disabled?: boolean;
  readonly icon?: React.ReactNode;
}

/**
 * The top of an invoice: what it is, and the way out of it. The buttons that finish
 * it are along the foot, under the total they save.
 */
export function InvoiceHeader({
  icon,
  title,
  subtitle,
}: {
  readonly icon: React.ReactNode;
  readonly title: string;
  readonly subtitle?: string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const onCancel = useContext(RequestClose);

  return (
    <header className="sticky top-0 z-20 border-b border-line/70 bg-canvas">
      <div className="flex w-full items-center gap-3 px-3 py-2 sm:px-4">
        <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-brand-500/10 text-brand-600 dark:text-brand-300">
          {icon}
        </span>
        <div className="flex min-w-0 flex-1 flex-col sm:flex-row sm:items-baseline sm:gap-3">
          <h2 className="truncate text-lg font-bold tracking-tight text-ink">{title}</h2>
          {subtitle && (
            <p className="hidden truncate text-sm text-ink-muted sm:block">{subtitle}</p>
          )}
        </div>
        <button
          type="button"
          onClick={onCancel}
          aria-label={t('common.close')}
          title={`${t('common.close')} (Esc)`}
          className="btn-icon size-9 shrink-0 rounded-full text-ink-muted hover:bg-surface-3 hover:text-ink"
        >
          <IconClose className="size-5" />
        </button>
      </div>
    </header>
  );
}

/**
 * The bar along the foot of an invoice, held in view however far it scrolls: the
 * keys the screen answers to, and the buttons that finish it - every way to save
 * its own button, its key on it, the main one last and coloured.
 *
 * On a phone or a tablet, where the bill's card may be a scroll away, the total is
 * on the bar beside the buttons that save it.
 */
export function InvoiceActions({
  primary,
  secondary = [],
  hints,
  total,
}: {
  readonly primary: InvoiceAction;
  readonly secondary?: readonly InvoiceAction[];
  readonly hints?: React.ReactNode;
  readonly total?: { readonly label: string; readonly amount: string };
}): React.JSX.Element {
  const { t } = useTranslation();
  const onCancel = useContext(RequestClose);

  const button = (action: InvoiceAction, main: boolean): React.JSX.Element => (
    <button
      key={action.label}
      type="button"
      onClick={action.onClick}
      disabled={action.disabled}
      {...(action.shortcut ? shortcutProps(action.shortcut) : {})}
      title={action.shortcut ? shortcutFor(action.shortcut) : undefined}
      className={clsx(
        main ? 'btn-primary' : 'btn-secondary',
        'flex-1 px-3 sm:flex-none sm:px-4',
      )}
    >
      {action.icon && <span className="hidden sm:inline-flex">{action.icon}</span>}
      {action.label}
      {action.shortcut && (
        <kbd
          className={clsx(
            'hidden rounded px-1.5 py-px font-mono text-[10px] font-medium md:inline',
            main ? 'bg-white/20 text-white' : 'bg-surface-3 text-ink-muted',
          )}
        >
          {shortcutFor(action.shortcut)}
        </kbd>
      )}
    </button>
  );

  return (
    <footer className="sticky bottom-0 z-20 border-t border-line/70 bg-canvas shadow-[0_-4px_12px_-8px_rgb(15_23_42/0.15)]">
      <div className="flex w-full flex-wrap items-center gap-x-3 gap-y-2 px-3 py-2 sm:flex-nowrap sm:px-4">
        {total && (
          <div className="flex min-w-0 basis-full items-baseline justify-between gap-2 sm:basis-auto sm:flex-col sm:items-start sm:gap-0 lg:hidden">
            <span className="text-xs text-ink-muted">{total.label}</span>
            <span className="text-lg leading-tight font-bold text-brand-600 tabular-nums dark:text-brand-300">
              {total.amount}
            </span>
          </div>
        )}
        {hints && <div className="hidden min-w-0 flex-1 lg:flex">{hints}</div>}
        <div className="flex w-full items-center gap-2 sm:ms-auto sm:w-auto">
          <button
            type="button"
            onClick={onCancel}
            className="btn-ghost hidden text-ink-muted sm:inline-flex"
          >
            {t('common.cancel')}
          </button>
          {secondary.map((action) => button(action, false))}
          {button(primary, true)}
        </div>
      </div>
    </footer>
  );
}

/**
 * The body of an invoice, the window's full width: the party, the items, and the
 * money along the foot.
 *
 * On a wide screen it is exactly the window's height - the items card stretches to
 * fill what the party and the money leave, and its lines scroll inside it - and when
 * the window is too short even for that, the whole of it scrolls. On a narrow
 * screen the cards simply stack.
 */
export function InvoiceBody({
  children,
}: {
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <div className="flex w-full flex-1 flex-col gap-2.5 px-3 py-2.5 sm:px-4">
      {children}
    </div>
  );
}

/** A card of an invoice, under a heading with its icon - or with none, for the party. */
export function InvoiceCard({
  icon,
  title,
  meta,
  actions,
  fill = false,
  children,
  className,
}: {
  readonly icon?: React.ReactNode;
  readonly title?: string;
  /** Beside the title, quieter: "3 items". */
  readonly meta?: React.ReactNode;
  /** At the end of the heading row. */
  readonly actions?: React.ReactNode;
  /** Takes whatever height the window has left, on a wide screen: the items. */
  readonly fill?: boolean;
  readonly children: React.ReactNode;
  readonly className?: string;
}): React.JSX.Element {
  return (
    <section
      className={clsx(
        'min-w-0 rounded-2xl border border-line bg-surface shadow-card',
        fill ? 'lg:flex lg:flex-1 lg:flex-col' : 'shrink-0',
        className,
      )}
    >
      {title && (
        <div className="flex items-center gap-2.5 border-b border-line/70 px-3 py-1.5 sm:px-4">
          <span className="shrink-0 text-brand-600 dark:text-brand-300">{icon}</span>
          <h3 className="text-[15px] font-semibold text-ink">{title}</h3>
          {meta && <span className="text-xs text-ink-muted">{meta}</span>}
          {actions && <div className="ms-auto flex items-center gap-2">{actions}</div>}
        </div>
      )}
      <div
        className={clsx(
          'p-3 sm:p-3.5',
          fill && 'lg:flex lg:min-h-0 lg:flex-1 lg:flex-col',
        )}
      >
        {children}
      </div>
    </section>
  );
}

/**
 * The party's fields, in two lines on a wide screen: eight columns, the party
 * itself two of them wide. Narrower screens take four, then two, then one.
 */
export function InvoiceFields({
  children,
}: {
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <div className="grid grid-cols-1 gap-x-4 gap-y-3.5 sm:grid-cols-2 md:grid-cols-4 lg:grid-cols-6 xl:grid-cols-8">
      {children}
    </div>
  );
}

/**
 * Where the lines' table goes, inside the items card.
 *
 * On a wide screen it stretches to the card's height and scrolls inside it, under a
 * header row that stays put - laid out of the flow, so a long bill grows the
 * scroll rather than the window. On a narrow one it is as long as its lines.
 */
export function InvoiceLines({
  children,
}: {
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <div className="relative lg:min-h-40 lg:flex-1">
      <div className="-mx-3 overflow-x-auto px-3 sm:mx-0 sm:px-0 lg:absolute lg:inset-0 lg:overflow-auto">
        {children}
      </div>
    </div>
  );
}

/**
 * The foot of an invoice, under its lines: its cards - the narration, the payment,
 * the bill - side by side on a wide screen, so they take the height of the tallest
 * rather than the sum of them all. `className` sets the columns.
 */
export function InvoiceFooter({
  children,
  className,
}: {
  readonly children: React.ReactNode;
  readonly className?: string;
}): React.JSX.Element {
  return (
    <div className={clsx('grid shrink-0 gap-3 md:grid-cols-2', className)}>
      {children}
    </div>
  );
}

/** One card of the foot, under a heading - with, at its far end, what it comes to. */
export function FooterSection({
  icon,
  title,
  aside,
  highlight = false,
  children,
  className,
}: {
  readonly icon?: React.ReactNode;
  readonly title?: string;
  readonly aside?: React.ReactNode;
  /** Tinted and bordered in the brand colour: the bill's total. */
  readonly highlight?: boolean;
  readonly children: React.ReactNode;
  readonly className?: string | undefined;
}): React.JSX.Element {
  return (
    <section
      className={clsx(
        'flex min-w-0 flex-col rounded-2xl border p-3.5 shadow-card',
        highlight
          ? 'border-brand-500/35 bg-brand-50 dark:bg-brand-500/10'
          : 'border-line bg-surface',
        className,
      )}
    >
      {title && (
        <div className="mb-2 flex items-center gap-2">
          {icon && (
            <span className="shrink-0 text-brand-600 dark:text-brand-300">{icon}</span>
          )}
          <h4 className="text-sm font-semibold text-ink">{title}</h4>
          {aside && <div className="ms-auto min-w-0">{aside}</div>}
        </div>
      )}
      {children}
    </section>
  );
}

/** A small caption over its box: the payment's fields, two to a row on the foot. */
export function CompactField({
  label,
  children,
  className,
}: {
  readonly label: string;
  readonly children: React.ReactNode;
  readonly className?: string;
}): React.JSX.Element {
  return (
    <label className={clsx('flex min-w-0 flex-col gap-1', className)}>
      <span className="truncate text-xs text-ink-muted">{label}</span>
      {children}
    </label>
  );
}

/**
 * A figure in the shape of a field - the caption over a box - for the payment's
 * worked-out amounts, so they line up with the boxes typed into beside them.
 */
export function ValueField({
  label,
  value,
  tone = 'plain',
  className,
}: {
  readonly label: string;
  readonly value: string;
  /** `due` for money still owed, `good` for change to hand back. */
  readonly tone?: 'plain' | 'strong' | 'due' | 'good';
  readonly className?: string;
}): React.JSX.Element {
  return (
    <div className={clsx('flex min-w-0 flex-col gap-1', className)}>
      <span className="truncate text-xs text-ink-muted">{label}</span>
      <span
        className={clsx(
          'flex min-h-8 items-center justify-end rounded-lg bg-surface-2 px-2.5 text-sm tabular-nums',
          tone === 'plain' ? 'font-medium text-ink' : 'font-semibold',
          tone === 'strong' && 'text-ink',
          tone === 'due' && 'text-red-600 dark:text-red-400',
          tone === 'good' && 'text-emerald-600 dark:text-emerald-400',
        )}
      >
        {value}
      </span>
    </div>
  );
}

/** What a bill is built from, for its card to set out line by line. */
export interface BillBreakdown {
  /** Lines with a product on them. */
  readonly items: number;
  readonly quantity: number;
  /** The goods at their rates, before any discount. */
  readonly gross: number;
  /** What came off the lines. Positive: it is shown as a deduction. */
  readonly discount: number;
  /** What the tax is worked on: the lines less their discounts. */
  readonly taxable: number;
  readonly tax: number;
  readonly showTax: boolean;
  /** The tax line's caption where it says more than "Tax": "Tax (GST)". */
  readonly taxLabel?: string;
  /** Each additional ledger with an amount, signed the way it moves the total. */
  readonly charges: readonly { readonly label: string; readonly amount: number }[];
}

/**
 * The bill, picked out: its total at the head, then how it comes to that - the
 * items and their quantity, the goods, what came off them, the tax, and each
 * additional ledger with what it adds or takes off.
 */
export function BillCard({
  label,
  total,
  breakdown,
  money,
  className,
}: {
  readonly label: string;
  readonly total: number;
  readonly breakdown: BillBreakdown;
  readonly money: (value: number) => string;
  readonly className?: string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const { items, quantity, gross, discount, taxable, tax, showTax, taxLabel, charges } =
    breakdown;
  const signed = (value: number): string =>
    `${value < 0 ? '−' : '+'}${money(Math.abs(value))}`;

  return (
    <FooterSection
      highlight
      icon={<IconReceipt className="size-5" />}
      title={label}
      aside={
        <span className="block truncate text-2xl leading-none font-bold text-brand-600 tabular-nums sm:text-3xl dark:text-brand-300">
          {money(total)}
        </span>
      }
      className={className}
    >
      <div className="divide-y divide-brand-500/15">
        <div className="pb-1">
          <FigureRow
            label={t('invoice.items')}
            value={t('invoice.itemsQty', {
              count: items,
              qty: Number(quantity.toFixed(3)).toString(),
            })}
          />
          <FigureRow label={t('invoice.itemsTotal')} value={money(gross)} />
          {discount !== 0 && (
            <FigureRow
              label={t('documents.discount')}
              value={`−${money(Math.abs(discount))}`}
            />
          )}
          {showTax && <FigureRow label={t('documents.taxable')} value={money(taxable)} />}
          {(showTax || tax !== 0) && (
            <FigureRow label={taxLabel ?? t('documents.tax')} value={money(tax)} />
          )}
        </div>
        {charges.length > 0 && (
          <div className="pt-1">
            {charges.map((charge) => (
              <FigureRow
                key={charge.label}
                label={
                  <span className="block truncate" title={charge.label}>
                    {charge.label}
                  </span>
                }
                value={signed(charge.amount)}
              />
            ))}
          </div>
        )}
      </div>
    </FooterSection>
  );
}

/** The fields a party's card can show: a customer's or a supplier's. */
export interface PartyDetails {
  readonly code: string;
  readonly name: string;
  readonly contact: {
    readonly mobileNumber: string | null;
    readonly phone: string | null;
    readonly addressLine1: string | null;
    readonly addressLine2: string | null;
  };
  readonly terms: {
    readonly creditLimit: number | null;
    readonly creditDays: number | null;
  };
  readonly taxDetails: { readonly registrationNumber: string | null };
}

/**
 * Who the document is for, at a glance: the party's name and code, how to reach
 * them, their tax registration, and the credit they are allowed.
 */
export function PartyCard({
  icon,
  title,
  party,
  empty,
  money,
  className,
}: {
  readonly icon: React.ReactNode;
  readonly title: string;
  readonly party: PartyDetails | undefined;
  /** Said when nobody is chosen yet. */
  readonly empty: string;
  readonly money: (value: number) => string;
  readonly className?: string;
}): React.JSX.Element {
  const { t } = useTranslation();

  const rows: readonly (readonly [string, string])[] = party
    ? [
        [
          t('customers.mobile'),
          [party.contact.mobileNumber, party.contact.phone].filter(Boolean).join(' · '),
        ],
        [
          t('customers.address'),
          [party.contact.addressLine1, party.contact.addressLine2]
            .filter(Boolean)
            .join(', '),
        ],
        [t('customers.registrationNumber'), party.taxDetails.registrationNumber ?? ''],
        [
          t('customers.creditLimit'),
          party.terms.creditLimit == null
            ? ''
            : party.terms.creditDays
              ? t('invoice.creditTerms', {
                  limit: money(party.terms.creditLimit),
                  days: party.terms.creditDays,
                })
              : money(party.terms.creditLimit),
        ],
      ]
    : [];

  return (
    <FooterSection icon={icon} title={title} className={className}>
      {party ? (
        <>
          <p className="truncate text-sm font-semibold text-ink" title={party.name}>
            {party.name}
          </p>
          <p className="text-xs text-ink-muted">{party.code}</p>
          <dl className="mt-2 space-y-1 text-sm">
            {rows.map(([label, value]) => (
              <div key={label} className="flex gap-3">
                <dt className="w-32 shrink-0 truncate text-ink-muted">{label}</dt>
                <dd className="min-w-0 truncate text-ink" title={value || undefined}>
                  {value || '—'}
                </dd>
              </div>
            ))}
          </dl>
        </>
      ) : (
        <p className="text-sm text-ink-muted">{empty}</p>
      )}
    </FooterSection>
  );
}

/** A line of figures where the value is right-aligned: the bill's breakdown. */
function FigureRow({
  label,
  value,
  muted = true,
}: {
  readonly label: React.ReactNode;
  readonly value: React.ReactNode;
  readonly muted?: boolean;
}): React.JSX.Element {
  return (
    <div className="flex items-center justify-between gap-3 py-1 text-sm">
      <span className={muted ? 'text-ink-muted' : 'font-medium text-ink'}>{label}</span>
      <span className="font-medium text-ink tabular-nums">{value}</span>
    </div>
  );
}

/** The longest narration any document takes, as the server holds it. */
export const NARRATION_LIMIT = 500;

/**
 * The narration, as one line among the party's fields - with how much of it is
 * used beside the caption once something is typed.
 */
export function NarrationInput({
  value,
  onChange,
  label,
  className,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly label: string;
  readonly className?: string;
}): React.JSX.Element {
  const id = useId();

  return (
    <Field
      label={label}
      htmlFor={id}
      className={className}
      aside={value.length > 0 ? `${value.length}/${NARRATION_LIMIT}` : undefined}
    >
      <input
        id={id}
        value={value}
        maxLength={NARRATION_LIMIT}
        onChange={(event) => onChange(event.target.value)}
        placeholder={label}
        className="field-input"
      />
    </Field>
  );
}

/** Takes a line off the document: a bin, the same on every invoice. */
export function RemoveLineButton({
  onClick,
  label,
  disabled = false,
}: {
  readonly onClick: () => void;
  readonly label: string;
  /** The last line of a document, which cannot go. */
  readonly disabled?: boolean;
}): React.JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={label}
      title={label}
      className="inline-grid size-9 place-items-center rounded-lg border border-line text-ink-muted transition hover:border-red-300 hover:bg-red-50 hover:text-red-600 disabled:pointer-events-none disabled:opacity-40 dark:hover:bg-red-500/10 dark:hover:text-red-400"
    >
      <IconTrash className="size-4" />
    </button>
  );
}
