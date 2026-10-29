import { useEffect, useRef, useState } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { IconChevron, IconClose, IconTrash } from '@/components/icons';
import { shortcutFor, shortcutProps, type EntryAction } from '@/lib/shortcuts';

/*
  The pieces every invoice is built from - sales and purchase, their returns, orders
  and quotations - so the screens read alike: a header that names the document and
  holds its buttons, a column of cards for the party and the items, and a column
  beside it with the money.
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
}: {
  readonly icon: React.ReactNode;
  readonly title: string;
  readonly subtitle?: string;
  readonly onCancel: () => void;
  readonly primary: InvoiceAction;
  /** Under the arrow beside the main button: the other ways to save. */
  readonly more?: readonly InvoiceAction[];
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <header className="sticky top-0 z-20 border-b border-line/70 bg-canvas/90 backdrop-blur-md">
      <div className="mx-auto flex w-full max-w-[1600px] items-center gap-3 px-3 py-3 sm:px-6 sm:py-4">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-brand-500/10 text-brand-600 sm:size-12 dark:text-brand-300">
          {icon}
        </span>
        <div className="min-w-0 flex-1">
          <h2 className="truncate text-lg font-bold tracking-tight text-ink sm:text-2xl">
            {title}
          </h2>
          {subtitle && (
            <p className="hidden truncate text-sm text-ink-muted sm:block">{subtitle}</p>
          )}
        </div>
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
 * The body of an invoice: its cards in a column, the money in a narrower one beside
 * it on a wide screen and under it on a narrow one.
 */
export function InvoiceBody({
  main,
  side,
  footer,
}: {
  readonly main: React.ReactNode;
  readonly side: React.ReactNode;
  /** Under both columns: the keys the screen answers to. */
  readonly footer?: React.ReactNode;
}): React.JSX.Element {
  return (
    <div className="mx-auto flex w-full max-w-[1600px] flex-1 flex-col gap-4 px-3 py-4 sm:px-6 sm:py-5">
      <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,1fr)_22rem] xl:grid-cols-[minmax(0,1fr)_26rem]">
        <div className="flex min-w-0 flex-col gap-4">{main}</div>
        {/* Held in view beside the items on a wide screen, so the total and the
            payment are never a scroll away from the line being typed. */}
        <aside className="flex min-w-0 flex-col gap-4 lg:sticky lg:top-24">{side}</aside>
      </div>
      {footer}
    </div>
  );
}

/** A card of an invoice, under a heading with its icon. */
export function InvoiceCard({
  icon,
  title,
  meta,
  actions,
  children,
  className,
}: {
  readonly icon: React.ReactNode;
  readonly title: string;
  /** Beside the title, quieter: "3 items". */
  readonly meta?: React.ReactNode;
  /** At the end of the heading row. */
  readonly actions?: React.ReactNode;
  readonly children: React.ReactNode;
  readonly className?: string;
}): React.JSX.Element {
  return (
    <section
      className={clsx(
        'min-w-0 rounded-2xl border border-line bg-surface shadow-card',
        className,
      )}
    >
      <div className="flex items-center gap-2.5 border-b border-line/70 px-4 py-3.5 sm:px-5">
        <span className="shrink-0 text-brand-600 dark:text-brand-300">{icon}</span>
        <h3 className="text-base font-semibold text-ink">{title}</h3>
        {meta && <span className="text-xs text-ink-muted">{meta}</span>}
        {actions && <div className="ms-auto flex items-center gap-2">{actions}</div>}
      </div>
      <div className="p-4 sm:p-5">{children}</div>
    </section>
  );
}

/** A smaller card inside a card: the narration, the bill. */
export function InvoicePanel({
  icon,
  title,
  children,
  className,
}: {
  readonly icon: React.ReactNode;
  readonly title: string;
  readonly children: React.ReactNode;
  readonly className?: string;
}): React.JSX.Element {
  return (
    <div
      className={clsx(
        'flex min-w-0 flex-col rounded-xl border border-line p-3.5 sm:p-4',
        className,
      )}
    >
      <div className="mb-3 flex items-center gap-2">
        <span className="shrink-0 text-brand-600 dark:text-brand-300">{icon}</span>
        <h4 className="text-sm font-semibold text-ink">{title}</h4>
      </div>
      {children}
    </div>
  );
}

/** The figure a card is about, picked out: the bill total, the new balance. */
export function TotalBanner({
  icon,
  label,
  amount,
  size = 'lg',
  tone = 'brand',
}: {
  readonly icon?: React.ReactNode;
  readonly label: string;
  readonly amount: string;
  readonly size?: 'md' | 'lg';
  /** `danger` for a balance left owing. */
  readonly tone?: 'brand' | 'danger';
}): React.JSX.Element {
  return (
    <div
      className={clsx(
        'flex items-center gap-3 rounded-xl',
        tone === 'danger' ? 'bg-red-500/10' : 'bg-brand-500/10',
        size === 'lg' ? 'px-4 py-4 sm:px-5' : 'px-3.5 py-3',
      )}
    >
      {icon && (
        <span className="shrink-0 text-brand-600 dark:text-brand-300">{icon}</span>
      )}
      <span
        className={clsx(
          'font-semibold text-ink',
          size === 'lg' ? 'text-sm sm:text-base' : 'text-sm',
        )}
      >
        {label}
      </span>
      <span
        className={clsx(
          'ms-auto font-bold tabular-nums',
          tone === 'danger'
            ? 'text-red-600 dark:text-red-400'
            : 'text-brand-600 dark:text-brand-300',
          size === 'lg' ? 'text-2xl sm:text-3xl' : 'text-lg',
        )}
      >
        {amount}
      </span>
    </div>
  );
}

/** One line of figures: a caption, and its value or the control that sets it. */
export function DetailRow({
  label,
  children,
  strong = false,
}: {
  readonly label: React.ReactNode;
  readonly children: React.ReactNode;
  readonly strong?: boolean;
}): React.JSX.Element {
  return (
    <div className="grid grid-cols-[minmax(0,2fr)_minmax(0,3fr)] items-center gap-3 py-2 text-sm">
      <span
        className={clsx('min-w-0', strong ? 'font-semibold text-ink' : 'text-ink-muted')}
      >
        {label}
      </span>
      <div
        className={clsx(
          'min-w-0 tabular-nums',
          strong ? 'font-semibold text-ink' : 'font-medium text-ink',
        )}
      >
        {children}
      </div>
    </div>
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

/** A heading inside a card, over a group of rows. */
export function GroupHeading({
  children,
}: {
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return <h4 className="pt-1 text-sm font-semibold text-ink">{children}</h4>;
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
        rows={4}
        value={value}
        maxLength={NARRATION_LIMIT}
        aria-label={label}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        className="field-input min-h-28 flex-1 resize-none pb-6"
      />
      <span className="pointer-events-none absolute end-3 bottom-2 text-[11px] text-ink-subtle tabular-nums">
        {value.length}/{NARRATION_LIMIT}
      </span>
    </div>
  );
}

/**
 * A bill's figures in the order it is built: the goods, what came off them, the
 * net the tax is worked on, the tax, then the additional ledgers - typed in place,
 * beside the figures they change - and the total, picked out.
 */
export function BillFigures({
  gross,
  discount,
  tax,
  showTax,
  charges,
  total,
  totalLabel,
  money,
}: {
  readonly gross: number;
  /** What came off the lines. Positive: it is shown as a deduction. */
  readonly discount: number;
  readonly tax: number;
  readonly showTax: boolean;
  /** The additional ledgers, as a list to type into. */
  readonly charges: React.ReactNode;
  readonly total: number;
  readonly totalLabel: string;
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
      <div className="mt-2">{charges}</div>
      <div className="mt-auto pt-3">
        <TotalBanner label={totalLabel} amount={money(total)} size="md" />
      </div>
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
