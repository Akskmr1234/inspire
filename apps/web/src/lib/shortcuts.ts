/**
 * The keyboard shortcuts, in one place: the screen that lists them and the handler
 * that acts on them read the same table, so the two cannot drift apart.
 *
 * Keys are matched by `KeyboardEvent.code` - the key's place on the keyboard - rather
 * than by the character it types, so Alt+S is the same key with the keyboard switched
 * to Arabic as in English.
 */

/** What a button on an entry screen does, as the shortcuts name it. */
export type EntryAction = 'new' | 'save' | 'draft' | 'print' | 'bill' | 'add-line';

/** A shortcut that opens a new entry from anywhere. */
export interface NewEntryShortcut {
  readonly code: string;
  readonly shift?: boolean;
  /** Where the entry is made. */
  readonly route: string;
  /**
   * The route itself is the new entry - a voucher, a service sale - so arriving is
   * enough; otherwise the screen's New button is pressed once it has drawn.
   */
  readonly direct?: boolean;
  readonly labelKey: string;
}

/** Alt (Option on a Mac) and a letter, from anywhere: a new entry of that kind. */
export const NEW_ENTRY_SHORTCUTS: readonly NewEntryShortcut[] = [
  { code: 'KeyS', route: '/sales/invoices', labelKey: 'shortcuts.sale' },
  {
    code: 'KeyS',
    shift: true,
    route: '/sales/returns',
    labelKey: 'shortcuts.salesReturn',
  },
  { code: 'KeyP', route: '/purchase/invoices', labelKey: 'shortcuts.purchase' },
  {
    code: 'KeyP',
    shift: true,
    route: '/purchase/returns',
    labelKey: 'shortcuts.purchaseReturn',
  },
  { code: 'KeyQ', route: '/sales/quotations', labelKey: 'shortcuts.quotation' },
  { code: 'KeyO', route: '/sales/orders', labelKey: 'shortcuts.salesOrder' },
  {
    code: 'KeyO',
    shift: true,
    route: '/purchase/orders',
    labelKey: 'shortcuts.purchaseOrder',
  },
  {
    code: 'KeyR',
    route: '/accounting/receipts/new',
    direct: true,
    labelKey: 'shortcuts.receipt',
  },
  {
    code: 'KeyY',
    route: '/accounting/payments/new',
    direct: true,
    labelKey: 'shortcuts.payment',
  },
  {
    code: 'KeyJ',
    route: '/accounting/journals/new',
    direct: true,
    labelKey: 'shortcuts.journal',
  },
  {
    code: 'KeyV',
    route: '/service/sales/new',
    direct: true,
    labelKey: 'shortcuts.serviceSale',
  },
  { code: 'KeyC', route: '/sales/customers', labelKey: 'shortcuts.customer' },
  { code: 'KeyU', route: '/purchase/suppliers', labelKey: 'shortcuts.supplier' },
  { code: 'KeyI', route: '/inventory/products', labelKey: 'shortcuts.product' },
  { code: 'KeyL', route: '/accounting/ledger-list', labelKey: 'shortcuts.ledger' },
];

/** A shortcut that presses a button of the entry in front of the user. */
export interface EntryShortcut {
  readonly action: EntryAction;
  readonly code: string;
  /** Ctrl on Windows and Linux, Cmd on a Mac. */
  readonly mod?: boolean;
  readonly alt?: boolean;
  readonly shift?: boolean;
  readonly labelKey: string;
}

/** Inside an entry: save, save as draft, save and print, bill, add a line; and New. */
export const ENTRY_SHORTCUTS: readonly EntryShortcut[] = [
  { action: 'new', code: 'KeyN', alt: true, labelKey: 'shortcuts.newHere' },
  { action: 'save', code: 'KeyS', mod: true, labelKey: 'shortcuts.save' },
  { action: 'save', code: 'Enter', mod: true, labelKey: 'shortcuts.save' },
  { action: 'draft', code: 'KeyS', mod: true, shift: true, labelKey: 'shortcuts.draft' },
  { action: 'print', code: 'KeyP', mod: true, labelKey: 'shortcuts.print' },
  { action: 'bill', code: 'KeyB', mod: true, labelKey: 'shortcuts.bill' },
  { action: 'add-line', code: 'KeyA', alt: true, labelKey: 'shortcuts.addLine' },
];

/** Whether this is a Mac, where Ctrl is Cmd and Alt is Option. */
export function onMac(): boolean {
  return typeof navigator !== 'undefined' && /Mac|iPhone|iPad/i.test(navigator.userAgent);
}

/** The key a `code` names, as it is printed on the keycap. */
function keyName(code: string): string {
  if (code.startsWith('Key')) return code.slice(3);
  if (code === 'Slash') return '/';
  return code;
}

/** How a shortcut is written for the person reading it: "Ctrl+Shift+S", or "⌘⇧S". */
export function describe(shortcut: {
  readonly code: string;
  readonly mod?: boolean;
  readonly alt?: boolean;
  readonly shift?: boolean;
}): string {
  const mac = onMac();
  const parts: string[] = [];

  if (shortcut.mod) parts.push(mac ? '⌘' : 'Ctrl');
  if (shortcut.alt) parts.push(mac ? '⌥' : 'Alt');
  if (shortcut.shift) parts.push(mac ? '⇧' : 'Shift');
  parts.push(keyName(shortcut.code));

  return parts.join(mac ? '' : '+');
}

/** The shortcut that presses a button doing `action`, as a button's tooltip names it. */
export function shortcutFor(action: EntryAction): string {
  const found = ENTRY_SHORTCUTS.find((shortcut) => shortcut.action === action);
  return found ? describe(found) : '';
}

/**
 * The attributes that mark a button as the one a shortcut presses, and tell the person
 * hovering it, and a screen reader, which keys do.
 */
export function shortcutProps(action: EntryAction): {
  readonly 'data-shortcut': EntryAction;
  readonly 'aria-keyshortcuts': string;
} {
  return { 'data-shortcut': action, 'aria-keyshortcuts': shortcutFor(action) };
}

/** The open window an entry is in, if there is one: the topmost dialog. */
export function activeDialog(): HTMLElement | null {
  const dialogs = document.querySelectorAll<HTMLElement>('[role="dialog"]');
  return dialogs.length > 0 ? (dialogs[dialogs.length - 1] ?? null) : null;
}

function usable(button: HTMLButtonElement): boolean {
  return !button.disabled && button.offsetParent !== null;
}

/**
 * The button a shortcut presses: the one marked for it in the open window, or on the
 * screen when no window is open. Inside a window with nothing marked, Save falls back
 * to the window's own submit button, which is how every master form saves.
 */
export function buttonFor(action: EntryAction): HTMLButtonElement | null {
  const dialog = activeDialog();
  const root: ParentNode = dialog ?? document;

  for (const button of root.querySelectorAll<HTMLButtonElement>(
    `[data-shortcut="${action}"]`,
  )) {
    if (usable(button)) return button;
  }

  if (action === 'save' && dialog) {
    for (const button of dialog.querySelectorAll<HTMLButtonElement>(
      'button[type="submit"]',
    )) {
      if (usable(button)) return button;
    }
  }

  return null;
}

/** Whether an entry is open - a window, or an entry screen with its Save - so leaving it would lose work. */
export function entryOpen(): boolean {
  return activeDialog() !== null || buttonFor('save') !== null;
}

/** Asks the shell to open the list of shortcuts, from a menu or a button anywhere. */
export const SHOW_SHORTCUTS_EVENT = 'erp:show-shortcuts';
