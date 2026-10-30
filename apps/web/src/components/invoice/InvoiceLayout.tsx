import { useEffect, useRef, useState } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { IconChevron, IconClose, IconReceipt, IconTrash } from '@/components/icons';
import { shortcutFor, shortcutProps, type EntryAction } from '@/lib/shortcuts';

/*
  The pieces every invoice is built from - sales and purchase, their returns, orders
  and quotations - so the screens read alike: a header that names the document and
  holds its buttons, the party in two lines under it, the items taking whatever
  height is left, and the money along the foot.
*/

/**
 * The whole window an invoice is entered in.
 *
 * A dialog still - Escape closes it, focus stays inside, the list stays underneath -
 * but with no title bar of its own: the invoice's header is the title bar, so the
 * buttons that finish it sit where the eye starts rather than at the foot of a page
 * that scrolls.
 */
export function InvoiceWindow({
  title,
  onClose,
  children,
}: {
  /** What a screen reader announces the window as. */
  readonly title: string;
  readonly onClose: () => void;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <Dialog.Root open onOpenChange={(open) => !open && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-slate-950/65 backdrop-blur-md dark:bg-black/80" />
        <Dialog.Content
          aria-describedby={undefined}
          className="animate-rise fixed inset-0 z-50 flex flex-col bg-canvas outline-none"
        >
          <Dialog.Title className="sr-only">{title}</Dialog.Title>
          <div className="flex min-h-0 flex-1 flex-col overflow-y-auto overscroll-contain">
            {children}
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

/** One of the actions under the arrow of the header's main button. */
export interface InvoiceAction {
  readonly label: string;
  readonly onClick: () => void;
  readonly shortcut?: EntryAction;
  readonly disabled?: boolean;
  readonly icon?: React.ReactNode;
}

/**
 * The top of an invoice: what it is, and the buttons that finish it.
 *
 * Held at the top of the window however far the items run. On a phone the subtitle
 * goes and the buttons keep their room.
 */
export function InvoiceHeader({
  icon,
  title,
  subtitle,
  onCancel,
  primary,
  more = [],
  hints,
}: {
  readonly icon: React.ReactNode;
  readonly title: string;
  readonly subtitle?: string;
  readonly onCancel: () => void;
  readonly primary: InvoiceAction;
  /** Under the arrow beside the main button: the other ways to save. */
  readonly more?: readonly InvoiceAction[];
  /** The keys the screen answers to, between the title and the buttons. */
  readonly hints?: React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <header className="sticky top-0 z-20 border-b border-line/70 bg-canvas/90 backdrop-blur-md">
      <div className="flex w-full items-center gap-3 px-3 py-2.5 sm:px-4">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-brand-500/10 text-brand-600 dark:text-brand-300">
          {icon}
        </span>
        <div className="min-w-0 flex-1 2xl:max-w-[18rem] 2xl:flex-none">
          <h2 className="truncate text-lg font-bold tracking-tight text-ink sm:text-xl">
            {title}
          </h2>
          {subtitle && (
            <p className="hidden truncate text-xs text-ink-muted sm:block">{subtitle}</p>
          )}
        </div>
        {/* In the header rather than a strip of their own, so the lines keep the
            height - and only on a screen wide enough to hold them on one line. */}
        {hints && <div className="hidden min-w-0 flex-1 2xl:flex">{hints}</div>}
        <button
          type="button"
          onClick={onCancel}
          className="btn-secondary hidden sm:inline-flex"
        >
          {t('common.cancel')}
        </button>
        <SplitButton primary={primary} more={more} />
        {/* A phone has no Escape key and no room for Cancel beside the save. */}
        <button
          type="button"
          onClick={onCancel}
          aria-label={t('common.cancel')}
          className="btn-icon size-9 shrink-0 rounded-full text-ink-muted hover:bg-surface-3 hover:text-ink sm:hidden"
        >
          <IconClose className="size-5" />
        </button>
      </div>
    </header>
  );
}

/**
 * A main button with its alternatives under an arrow beside it.
 *
 * The alternatives are drawn even while the menu is shut - out of sight and out of
 * reach of Tab, but there - so the keyboard shortcuts that press them (Ctrl+S, a
 * draft) still find them.
 */
function SplitButton({
  primary,
  more,
}: {
  readonly primary: InvoiceAction;
  readonly more: readonly InvoiceAction[];
}): React.JSX.Element {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLDivElement>(null);

  // Shut by a press anywhere else, and by Escape - caught at the window, ahead of
  // the dialog's own listener, so it shuts the menu rather than the invoice.
  useEffect(() => {
    if (!open) return;
    const away = (event: PointerEvent): void => {
      if (!box.current?.contains(event.target as Node)) setOpen(false);
    };
    const escape = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return;
      event.stopPropagation();
      setOpen(false);
    };
    document.addEventListener('pointerdown', away);
    window.addEventListener('keydown', escape, true);
    return () => {
      document.removeEventListener('pointerdown', away);
      window.removeEventListener('keydown', escape, true);
    };
  }, [open]);

  const visible = more;

  return (
    <div ref={box} className="relative flex">
      <button
        type="button"
        onClick={primary.onClick}
        disabled={primary.disabled}
        {...(primary.shortcut ? shortcutProps(primary.shortcut) : {})}
        title={primary.shortcut ? shortcutFor(primary.shortcut) : undefined}
        className={clsx('btn-primary', visible.length > 0 && 'rounded-e-none')}
      >
        {primary.icon}
        {primary.label}
      </button>
      {visible.length > 0 && (
        <>
          <button
            type="button"
            aria-haspopup="menu"
            aria-expanded={open}
            aria-label={t('invoice.moreActions')}
            onClick={() => setOpen((now) => !now)}
            className="btn-primary rounded-s-none border-s border-white/25 px-2.5"
          >
            <IconChevron className="size-4 rotate-90" />
          </button>
          <div
            role="menu"
            className={clsx(
              'absolute end-0 top-full z-30 mt-1.5 w-60 rounded-xl border border-line bg-surface p-1.5 shadow-float transition',
              open ? 'visible opacity-100' : 'pointer-events-none invisible opacity-0',
            )}
          >
            {visible.map((action) => (
              <button
                key={action.label}
                type="button"
                role="menuitem"
                tabIndex={open ? 0 : -1}
                disabled={action.disabled}
                onClick={() => {
                  setOpen(false);
                  action.onClick();
                }}
                {...(action.shortcut ? shortcutProps(action.shortcut) : {})}
                className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-start text-sm font-medium text-ink hover:bg-surface-3 disabled:opacity-50"
              >
                {action.icon}
                <span className="flex-1">{action.label}</span>
                {action.shortcut && (
                  <kbd className="font-mono text-[11px] text-ink-subtle">
                    {shortcutFor(action.shortcut)}
                  </kbd>
                )}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
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
    <div className="flex w-full flex-1 flex-col gap-3 px-3 py-3 sm:px-4">{children}</div>
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
        <div className="flex items-center gap-2.5 border-b border-line/70 px-3 py-2 sm:px-4">
          <span className="shrink-0 text-brand-600 dark:text-brand-300">{icon}</span>
          <h3 className="text-[15px] font-semibold text-ink">{title}</h3>
          {meta && <span className="text-xs text-ink-muted">{meta}</span>}
          {actions && <div className="ms-auto flex items-center gap-2">{actions}</div>}
        </div>
      )}
      <div
        className={clsx('p-3 sm:p-4', fill && 'lg:flex lg:min-h-0 lg:flex-1 lg:flex-col')}
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
    <div className="relative lg:min-h-44 lg:flex-1">
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
        'flex min-w-0 flex-col rounded-2xl border p-4 shadow-card',
        highlight
          ? 'border-brand-500/35 bg-brand-50 dark:bg-brand-500/10'
          : 'border-line bg-surface',
        className,
      )}
    >
      {title && (
        <div className="mb-3 flex items-center gap-2">
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

/**
 * The bill, picked out: its total at the head, then the figures it is built from
 * beside the additional ledgers typed into it.
 */
export function BillCard({
  label,
  total,
  figures,
  charges,
  className,
}: {
  readonly label: string;
  /** Already formatted. */
  readonly total: string;
  readonly figures: React.ReactNode;
  readonly charges: React.ReactNode;
  readonly className?: string;
}): React.JSX.Element {
  return (
    <FooterSection
      highlight
      icon={<IconReceipt className="size-5" />}
      title={label}
      aside={
        <span className="block truncate text-2xl leading-none font-bold text-brand-600 tabular-nums sm:text-3xl dark:text-brand-300">
          {total}
        </span>
      }
      className={className}
    >
      <div className="grid gap-x-6 gap-y-3 xl:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
        <div className="min-w-0">{figures}</div>
        <div className="min-w-0">{charges}</div>
      </div>
    </FooterSection>
  );
}

/** A line of figures where the value is right-aligned: the bill's breakdown. */
export function FigureRow({
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

/** The narration box, with what is left of it counted underneath. */
export function NarrationField({
  value,
  onChange,
  placeholder,
  label,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly placeholder: string;
  readonly label: string;
}): React.JSX.Element {
  return (
    <div className="relative flex flex-1 flex-col">
      <textarea
        rows={3}
        value={value}
        maxLength={NARRATION_LIMIT}
        aria-label={label}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        className="field-input min-h-20 flex-1 resize-none pb-6"
      />
      <span className="pointer-events-none absolute end-3 bottom-2 text-[11px] text-ink-subtle tabular-nums">
        {value.length}/{NARRATION_LIMIT}
      </span>
    </div>
  );
}

/**
 * A bill's figures in the order it is built: the goods, what came off them, the
 * net the tax is worked on, and the tax.
 */
export function BillFigures({
  gross,
  discount,
  tax,
  showTax,
  money,
}: {
  readonly gross: number;
  /** What came off the lines. Positive: it is shown as a deduction. */
  readonly discount: number;
  readonly tax: number;
  readonly showTax: boolean;
  readonly money: (value: number) => string;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <>
      {discount !== 0 && (
        <>
          <FigureRow label={t('documents.gross')} value={money(gross)} />
          <FigureRow label={t('documents.discount')} value={money(-Math.abs(discount))} />
        </>
      )}
      <FigureRow label={t('documents.net')} value={money(gross - discount)} />
      {(showTax || tax !== 0) && (
        <FigureRow label={t('documents.tax')} value={money(tax)} />
      )}
    </>
  );
}

/** Takes a line off the document: a bin, the same on every invoice. */
export function RemoveLineButton({
  onClick,
  label,
}: {
  readonly onClick: () => void;
  readonly label: string;
}): React.JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      title={label}
      className="inline-grid size-9 place-items-center rounded-lg border border-line text-ink-muted transition hover:border-red-300 hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-500/10 dark:hover:text-red-400"
    >
      <IconTrash className="size-4" />
    </button>
  );
}
