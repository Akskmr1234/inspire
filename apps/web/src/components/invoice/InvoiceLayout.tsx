import { createContext, useContext, useEffect, useId, useRef, useState } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { Field } from '@/components/Form';
import {
  IconBarcode,
  IconClose,
  IconMenu,
  IconPause,
  IconPlus,
  IconReceipt,
  IconSearch,
  IconTrash,
} from '@/components/icons';
import { openMenu } from '@/lib/shell';
import {
  activeDialog,
  actOn,
  buttonFor,
  shortcutFor,
  shortcutProps,
  type EntryAction,
} from '@/lib/shortcuts';

/*
  The pieces every invoice is built from - sales and purchase, their returns, orders
  and quotations - so the screens read alike: a header that names the document, the
  party in two lines under it, the items taking whatever height is left, the money
  under them, and the buttons that save it along the very foot.
*/

/** Asks to close the window: straight away, or after asking when there is work in it. */
const RequestClose = createContext<() => void>(() => {});

/** Whether the window sits beside the sidebar, leaving the menu to be used. */
const Docked = createContext(false);

/**
 * The whole window an invoice is entered in.
 *
 * A dialog still - Escape closes it, focus stays inside, the list stays underneath -
 * but with no title bar of its own: the invoice's header is the title bar, and the
 * bar along its foot holds the buttons that finish it.
 *
 * With work in it (`dirty`), Escape, the cross and Cancel ask before throwing it
 * away - or, where the bill can be held (`onHold`), whether to hold it instead. A box
 * of figures takes the whole of what is in it when it is entered, so a quantity of 1
 * is typed over rather than typed after.
 *
 * `docked` leaves the application's menu where it can be used: the window sits beside
 * the sidebar rather than over it, and on a phone its header has the menu button the
 * window covers. It is for a bill that holds itself when its screen is left, so going
 * somewhere else from the menu loses nothing.
 */
export function InvoiceWindow({
  title,
  onClose,
  dirty = false,
  onHold,
  docked = false,
  children,
}: {
  /** What a screen reader announces the window as. */
  readonly title: string;
  readonly onClose: () => void;
  /** Something has been entered that closing would lose. */
  readonly dirty?: boolean;
  /** Puts the bill on hold and closes the window; offered beside discarding it. */
  readonly onHold?: () => void;
  /** Beside the sidebar, with the menu left usable. */
  readonly docked?: boolean;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  const [confirming, setConfirming] = useState(false);
  const clicked = useRef<EventTarget | null>(null);

  const requestClose = (): void => {
    if (dirty) setConfirming(true);
    else onClose();
  };

  return (
    <Dialog.Root open modal={!docked} onOpenChange={(open) => !open && requestClose()}>
      <Dialog.Portal>
        {!docked && (
          <Dialog.Overlay className="fixed inset-0 z-50 bg-slate-950/65 backdrop-blur-md dark:bg-black/80" />
        )}
        <Dialog.Content
          aria-describedby={undefined}
          {...(docked ? { 'data-holds-on-leave': '' } : {})}
          // Docked, the menu beside it is there to be used: a click or a focus out
          // there is not a request to close.
          {...(docked
            ? { onInteractOutside: (event: Event) => event.preventDefault() }
            : {})}
          className={clsx(
            'invoice-window animate-rise fixed flex flex-col bg-canvas outline-none',
            docked
              ? 'inset-y-0 end-0 start-0 z-40 lg:start-[var(--app-sidebar-width,0px)] lg:border-s lg:border-line lg:shadow-float'
              : 'inset-0 z-50',
          )}
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
          <Docked.Provider value={docked}>
            <RequestClose.Provider value={requestClose}>
              <div className="flex min-h-0 flex-1 flex-col overflow-y-auto overscroll-contain">
                {children}
              </div>
            </RequestClose.Provider>
          </Docked.Provider>
          {confirming && (
            <DiscardPrompt
              onKeep={() => setConfirming(false)}
              onDiscard={onClose}
              {...(onHold ? { onHold } : {})}
            />
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

/**
 * Whether to throw away an invoice with work in it - or hold it, where it can be held.
 * Keeping it is the default.
 */
function DiscardPrompt({
  onKeep,
  onDiscard,
  onHold,
}: {
  readonly onKeep: () => void;
  readonly onDiscard: () => void;
  readonly onHold?: () => void;
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
        <div className="mt-5 flex flex-wrap justify-end gap-2">
          <button type="button" onClick={onKeep} className="btn-secondary" autoFocus>
            {t('invoice.keepEditing')}
          </button>
          {onHold && (
            <button type="button" onClick={onHold} className="btn-secondary">
              <IconPause className="size-4" />
              {t('invoice.hold')}
            </button>
          )}
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
  const docked = useContext(Docked);

  return (
    <header className="sticky top-0 z-20 border-b border-line/70 bg-canvas">
      <div className="flex w-full items-center gap-3 px-3 py-2 sm:px-4 short:py-1.5">
        {/* The window covers the bar's menu button; on a wide screen the sidebar is
            beside it instead. */}
        {docked && (
          <button
            type="button"
            onClick={openMenu}
            aria-label={t('nav.openMenu')}
            title={t('invoice.menuHolds')}
            className="btn-icon -ms-1 size-9 shrink-0 lg:hidden"
          >
            <IconMenu />
          </button>
        )}
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
 * on the bar beside the buttons that save it - and on a wide screen too, whenever
 * the window is too short for the bill's own total to be in view.
 */
export function InvoiceActions({
  primary,
  secondary = [],
  hints,
  total,
  start,
}: {
  readonly primary: InvoiceAction;
  readonly secondary?: readonly InvoiceAction[];
  readonly hints?: React.ReactNode;
  readonly total?: { readonly label: string; readonly amount: string };
  /** At the bar's start, away from the buttons that save: Hold, and Load from. */
  readonly start?: React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();
  const onCancel = useContext(RequestClose);
  const bar = useRef<HTMLElement>(null);
  const billInView = useBillTotalInView(bar);

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
    <footer
      ref={bar}
      className="sticky bottom-0 z-20 border-t border-line/70 bg-canvas shadow-[0_-4px_12px_-8px_rgb(15_23_42/0.15)]"
    >
      <div className="flex w-full flex-wrap items-center gap-x-3 gap-y-2 px-3 py-2 sm:flex-nowrap sm:px-4">
        {start && <div className="flex shrink-0 items-center gap-2">{start}</div>}
        {total && (
          <div
            className={clsx(
              'flex min-w-0 items-baseline justify-between gap-2 sm:basis-auto sm:flex-col sm:items-start sm:gap-0',
              // Beside the buttons at the start on a phone, rather than a row of its own.
              start ? 'flex-1 sm:flex-none' : 'basis-full',
              billInView && 'lg:hidden',
            )}
          >
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
 * Whether the bill's total, in its card at the bottom right, is in view: not
 * scrolled away, and not under the bar along the window's foot. Assumed so until the
 * browser says otherwise, and always so where it cannot say.
 */
function useBillTotalInView(bar: React.RefObject<HTMLElement | null>): boolean {
  const [inView, setInView] = useState(true);

  useEffect(() => {
    const footer = bar.current;
    const scroller = footer?.parentElement;
    const figure = scroller?.querySelector('[data-bill-total]');

    if (!footer || !scroller || !figure || typeof IntersectionObserver === 'undefined') {
      return;
    }

    const watcher = new IntersectionObserver(
      ([entry]) => setInView(entry !== undefined && entry.intersectionRatio > 0.99),
      {
        root: scroller,
        // The bar sits over the foot of the scroll; under it is out of view.
        rootMargin: `0px 0px -${footer.offsetHeight}px 0px`,
        threshold: [0, 1],
      },
    );

    watcher.observe(figure);
    return () => watcher.disconnect();
  }, [bar]);

  return inView;
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
    <div className="flex w-full flex-1 flex-col gap-2.5 px-3 py-2.5 sm:px-4 short:gap-2 short:py-2">
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
        <div className="flex items-center gap-2.5 border-b border-line/70 px-3 py-1.5 sm:px-4 short:py-1">
          <span className="shrink-0 text-brand-600 dark:text-brand-300">{icon}</span>
          <h3 className="text-[15px] font-semibold text-ink">{title}</h3>
          {meta && <span className="text-xs text-ink-muted">{meta}</span>}
          {actions && <div className="ms-auto flex items-center gap-2">{actions}</div>}
        </div>
      )}
      <div
        className={clsx(
          'p-3 sm:p-3.5 short:p-2.5',
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
 * itself two of them wide. Narrower screens take six, then four, then two - two even
 * on a phone, where one to a row put the lines a dozen boxes down the screen.
 */
export function InvoiceFields({
  children,
}: {
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <div className="invoice-fields grid grid-cols-2 gap-x-3 gap-y-3.5 sm:gap-x-4 md:grid-cols-4 lg:grid-cols-6 xl:grid-cols-8 short:gap-y-2.5">
      {children}
    </div>
  );
}

/**
 * Where the lines' table goes, inside the items card.
 *
 * On a wide screen it stretches to the card's height and scrolls inside it, under a
 * header row that stays put - laid out of the flow, so a long bill grows the
 * scroll rather than the window. It keeps room for what it holds, up to four lines
 * or so: in a window too short for that and the money under it, the window scrolls
 * as a whole, rather than leave the lines a slot one line high - while a bill of a
 * line or two leaves the money in view. On a narrow screen it is as long as its
 * lines.
 *
 * Enter moves on, as Tab does, through a line's boxes and on to the next line's
 * product - adding a line after the last one, once it names a product. Its
 * picker's Enter is its own until it has chosen; then it moves on too.
 */
export function InvoiceLines({
  children,
}: {
  readonly children: React.ReactNode;
}): React.JSX.Element {
  const content = useRef<HTMLDivElement>(null);
  const [room, setRoom] = useState<number | null>(null);

  useEffect(() => {
    const box = content.current;
    if (!box || typeof ResizeObserver === 'undefined') return;

    const watcher = new ResizeObserver(() =>
      setRoom(Math.min(box.offsetHeight, LINES_ROOM)),
    );
    watcher.observe(box);
    return () => watcher.disconnect();
  }, []);

  return (
    <div
      className="relative lg:min-h-[max(10rem,var(--lines-room,10rem))] lg:flex-1"
      style={
        room === null
          ? undefined
          : ({ '--lines-room': `${room}px` } as React.CSSProperties)
      }
      onKeyDown={moveOnEnter}
    >
      <div className="-mx-3 overflow-x-auto px-3 sm:mx-0 sm:px-0 lg:absolute lg:inset-0 lg:overflow-auto">
        <div ref={content}>{children}</div>
      </div>
    </div>
  );
}

/** The most the lines keep for themselves, in pixels: the header, four lines, the totals. */
const LINES_ROOM = 288;

/** The boxes a line is typed into, its own row and the batch row under it. */
function lineBoxes(row: Element): HTMLInputElement[] {
  const rows: Element[] = [row];

  for (
    let next = row.nextElementSibling;
    next && !next.matches('[data-line-row]');
    next = next.nextElementSibling
  ) {
    rows.push(next);
  }

  return rows.flatMap((part) =>
    Array.from(
      part.querySelectorAll<HTMLInputElement>(
        'input:not([disabled]):not([readonly]):not([type="checkbox"]):not([type="hidden"])',
      ),
    ),
  );
}

/** The line a box belongs to: the row it is in, or the one over its batch row. */
function lineOf(box: HTMLElement): Element | null {
  let row: Element | null = box.closest('tr');

  while (row && !row.matches('[data-line-row]')) {
    row = row.previousElementSibling;
  }

  return row;
}

/** Enter in a line: the next box, the next line's product, or a new line. */
function moveOnEnter(event: React.KeyboardEvent<HTMLElement>): void {
  if (
    event.key !== 'Enter' ||
    event.altKey ||
    event.ctrlKey ||
    event.metaKey ||
    event.shiftKey ||
    event.nativeEvent.isComposing
  ) {
    return;
  }

  const box = event.target;
  const row = box instanceof HTMLInputElement ? lineOf(box) : null;
  if (!(box instanceof HTMLInputElement) || !row) return;

  // A picker chose something only if it took the key for itself.
  if (box.getAttribute('role') === 'combobox' && !event.defaultPrevented) return;

  const boxes = lineBoxes(row);
  const next = boxes[boxes.indexOf(box) + 1];

  if (next) {
    next.focus();
    return;
  }

  let following = row.nextElementSibling;
  while (following && !following.matches('[data-line-row]')) {
    following = following.nextElementSibling;
  }

  if (following) {
    lineBoxes(following)[0]?.focus();
    return;
  }

  // The last box of the last line: another line, unless this one is still empty.
  const product = row.querySelector<HTMLInputElement>('[role="combobox"]');
  if (product && product.value.trim() !== '') buttonFor('add-line')?.click();
}

/**
 * Picks out a line once it is on screen - scrolled to, where the lines scroll, and
 * lit up for a moment - and, asked to, puts the cursor in its product: the line a
 * scan just added to, or the one Add item just made. `bodyRef` goes on the lines'
 * `<tbody>`, whose rows carry `data-line-row`.
 */
export function useLineAttention(): {
  readonly bodyRef: React.RefObject<HTMLTableSectionElement | null>;
  readonly attend: (
    line: number | 'last',
    options?: { readonly focus?: boolean },
  ) => void;
} {
  const bodyRef = useRef<HTMLTableSectionElement>(null);
  const [wanted, setWanted] = useState<{
    readonly line: number | 'last';
    readonly focus: boolean;
  } | null>(null);

  useEffect(() => {
    if (!wanted) return;
    setWanted(null);

    const rows = bodyRef.current?.querySelectorAll<HTMLElement>('[data-line-row]');
    if (!rows || rows.length === 0) return;

    const row = wanted.line === 'last' ? rows[rows.length - 1] : rows[wanted.line];
    if (!row) return;

    // Only where the lines scroll inside their card; on a phone the page would jump
    // away from the scan box the next scan is typed into.
    if (window.matchMedia('(min-width: 64rem)').matches) {
      row.scrollIntoView({ block: 'nearest' });
    }

    row.classList.remove('line-flash');
    // Read back, so the browser starts the animation again for a line lit twice.
    void row.offsetWidth;
    row.classList.add('line-flash');

    if (wanted.focus) {
      row.querySelector<HTMLInputElement>('[role="combobox"]')?.focus();
    }
  }, [wanted]);

  return {
    bodyRef,
    attend: (line, options) => setWanted({ line, focus: options?.focus ?? false }),
  };
}

/** Adds a line: in the items card's heading, where it stays in view, with its key. */
export function AddLineButton({
  onClick,
}: {
  readonly onClick: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <button
      {...shortcutProps('add-line')}
      type="button"
      onClick={onClick}
      title={`${t('invoice.addItem')} (${shortcutFor('add-line')})`}
      className="inline-flex items-center gap-1.5 rounded-lg border border-brand-500/30 bg-brand-50 px-2.5 py-1 text-xs font-semibold whitespace-nowrap text-brand-700 transition hover:border-brand-500/50 hover:bg-brand-100 dark:bg-brand-500/15 dark:text-brand-200 dark:hover:bg-brand-500/25"
    >
      <IconPlus className="size-3.5" />
      {t('invoice.addItem')}
      <kbd className="hidden rounded bg-white/70 px-1 py-px font-mono text-[10px] font-medium text-brand-700/80 md:inline dark:bg-white/10 dark:text-brand-200/80">
        {shortcutFor('add-line')}
      </kbd>
    </button>
  );
}

/**
 * Under the lines while none of them names a product yet: the two ways to start,
 * with their keys, where the first item will appear.
 */
export function LinesEmptyHint({
  scan = true,
}: {
  /** Whether the screen has a scan box to point at. */
  readonly scan?: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <div className="mt-2 flex flex-wrap items-center justify-center gap-x-4 gap-y-1.5 rounded-xl border border-dashed border-line-strong/70 px-4 py-3 text-center text-sm text-ink-muted">
      <p className="inline-flex items-center gap-2 font-medium text-ink">
        <IconBarcode className="size-5 text-brand-600 dark:text-brand-300" />
        {t('invoice.emptyLinesTitle')}
      </p>
      <p className="flex flex-wrap items-center justify-center gap-x-3 gap-y-1 text-xs">
        {scan && (
          <span className="inline-flex items-center gap-1">
            <kbd className="rounded border border-line bg-surface-2 px-1.5 py-px font-mono text-[10px]">
              {shortcutFor('scan')}
            </kbd>
            {t('invoice.emptyLinesScan')}
          </span>
        )}
        <span className="inline-flex items-center gap-1">
          <kbd className="rounded border border-line bg-surface-2 px-1.5 py-px font-mono text-[10px]">
            {shortcutFor('add-line')}
          </kbd>
          {t('invoice.emptyLinesAdd')}
        </span>
        <span className="inline-flex items-center gap-1">
          <kbd className="rounded border border-line bg-surface-2 px-1.5 py-px font-mono text-[10px]">
            Enter
          </kbd>
          {t('invoice.emptyLinesEnter')}
        </span>
      </p>
    </div>
  );
}

/** The heading over the lines' numbers. */
export function LineNumberHeader(): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <th className="line-number-column rounded-s-lg py-2.5 ps-3 pe-1 text-start font-medium">
      <span aria-hidden="true">#</span>
      <span className="sr-only">{t('invoice.lineNumber')}</span>
    </th>
  );
}

/** A line's number, so "the third one" can be found and said. */
export function LineNumberCell({
  number,
}: {
  readonly number: number;
}): React.JSX.Element {
  return (
    <td className="line-number-cell py-1 ps-3 pe-1 text-start text-xs font-medium text-ink-subtle tabular-nums">
      {number}
    </td>
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
        'flex min-w-0 flex-col rounded-2xl border p-3.5 shadow-card short:p-3',
        highlight
          ? 'border-brand-500/35 bg-brand-50 dark:bg-brand-500/10'
          : 'border-line bg-surface',
        className,
      )}
    >
      {title && (
        <div className="mb-2 flex items-center gap-2 short:mb-1.5">
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

/** A quantity as the lines show it: no more decimals than it has, up to three. */
export function formatQuantity(quantity: number): string {
  return Number(quantity.toFixed(3)).toString();
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
  /**
   * The goods' total - after their discounts, with their tax - set out as a total of its
   * own before the additional ledgers and the round off, as a sale's bill reads: Total,
   * the ledgers, Round off, Grand total.
   */
  readonly subtotal?: boolean;
  /** What rounding moved the total by; absent where the document is not rounded. */
  readonly roundOff?: number;
}

/**
 * The bill, picked out: how it comes to its total - the items and their quantity,
 * the goods, what came off them, the tax, and each additional ledger with what it
 * adds or takes off - and the total itself at the foot, in the bottom corner, where
 * a bill's total is read.
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
  const {
    items,
    quantity,
    gross,
    discount,
    taxable,
    tax,
    showTax,
    taxLabel,
    charges,
    subtotal = false,
    roundOff,
  } = breakdown;
  const signed = (value: number): string =>
    `${value < 0 ? '−' : '+'}${money(Math.abs(value))}`;

  return (
    <FooterSection
      highlight
      icon={<IconReceipt className="size-5" />}
      title={t('invoice.bill')}
      className={className}
    >
      <div className="divide-y divide-brand-500/15">
        <div className="pb-1">
          <FigureRow
            label={t('invoice.items')}
            value={t('invoice.itemsQty', { count: items, qty: formatQuantity(quantity) })}
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
        {subtotal && (
          <div className="py-1">
            <FigureRow
              label={t('invoice.total')}
              value={money(taxable + tax)}
              muted={false}
            />
          </div>
        )}
        {(charges.length > 0 || roundOff !== undefined) && (
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
            {roundOff !== undefined && (
              <FigureRow label={t('invoice.roundOff')} value={signed(roundOff)} />
            )}
          </div>
        )}
      </div>
      <div
        data-bill-total
        className="mt-auto flex items-baseline justify-between gap-3 border-t border-brand-500/25 pt-2"
      >
        <span className="text-sm font-semibold text-ink">{label}</span>
        <span className="truncate text-2xl leading-tight font-bold text-brand-600 tabular-nums sm:text-3xl short:text-2xl dark:text-brand-300">
          {money(total)}
        </span>
      </div>
    </FooterSection>
  );
}

/** One figure in the row of totals under the lines, under the column it adds up. */
export interface LinesTotalCell {
  readonly key: string;
  /** The column's caption, for the phone, where the row is a card. */
  readonly label?: string;
  readonly value?: string;
}

/**
 * The row under the lines that adds them up, column by column - quantity, rate,
 * discount, tax, net, total - held at the foot of the lines however far they
 * scroll, as the header row is held at their head.
 */
export function LinesTotalRow({
  label,
  span,
  cells,
}: {
  readonly label: string;
  /** How many columns the caption covers: the product's and any beside it. */
  readonly span: number;
  /** The rest of the row, one per column, the last the actions column. */
  readonly cells: readonly LinesTotalCell[];
}): React.JSX.Element {
  return (
    <tfoot className="sticky bottom-0 z-10 bg-surface-2 text-sm font-semibold text-ink">
      <tr>
        <td colSpan={span} className="rounded-s-lg px-3 py-2.5 text-start">
          {label}
        </td>
        {cells.map((cell, index) => (
          <td
            key={cell.key}
            {...(cell.label ? { 'data-label': cell.label } : {})}
            className={clsx(
              'px-2 py-2.5 text-end whitespace-nowrap tabular-nums',
              index === cells.length - 1 && 'rounded-e-lg',
            )}
          >
            {cell.value ?? ''}
          </td>
        ))}
      </tr>
    </tfoot>
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
 * Puts the cursor in the open invoice's party picker - the customer or supplier box,
 * marked `data-party-field` - with its list open.
 */
export function focusPartyField(): void {
  const field = activeDialog()?.querySelector<HTMLElement>('[data-party-field]');
  if (field) actOn(field);
}

/**
 * Who the document is for, at a glance: the party's name and code, how to reach
 * them, their tax registration, and the credit they are allowed. With nobody chosen,
 * a button that goes to the box to choose them in.
 */
export function PartyCard({
  icon,
  title,
  party,
  empty,
  choose,
  money,
  className,
  children,
}: {
  readonly icon: React.ReactNode;
  readonly title: string;
  readonly party: PartyDetails | undefined;
  /** Said when nobody is chosen yet. */
  readonly empty: string;
  /** The button's caption when nobody is chosen: "Choose customer". */
  readonly choose?: string;
  readonly money: (value: number) => string;
  readonly className?: string;
  /** Fields that belong with the party, under its details: the billing name, the narration. */
  readonly children?: React.ReactNode;
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
        <div className="flex flex-1 flex-col items-start gap-2.5">
          <p className="text-sm text-ink-muted">{empty}</p>
          {choose && (
            <button
              type="button"
              onClick={focusPartyField}
              className="btn-secondary btn-sm"
            >
              <IconSearch className="size-3.5" />
              {choose}
            </button>
          )}
        </div>
      )}
      {children && (
        <div className="mt-3 space-y-2.5 border-t border-line pt-3 short:mt-2 short:pt-2">
          {children}
        </div>
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
    <div className="flex items-center justify-between gap-3 py-0.5 text-sm short:py-px">
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
  compact = false,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly label: string;
  readonly className?: string;
  /** The small box of a card in the foot, rather than the full height of the fields. */
  readonly compact?: boolean;
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
        className={compact ? 'field-input-sm' : 'field-input'}
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
