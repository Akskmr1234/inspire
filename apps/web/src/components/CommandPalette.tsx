import { useEffect, useMemo, useRef, useState } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { labelFor, type Menu, type MenuEntry } from '@/lib/menu';
import { iconFor } from '@/components/icons';

/**
 * Everything the menu can reach, one keystroke away.
 *
 * This application has forty-one screens under nine headings, in a sidebar that has
 * to be scrolled on any laptop. Reaching the purchase returns means finding
 * Transactions, reading past eight siblings, and clicking — every time, all day. A
 * palette turns that into two keystrokes and a word, and it is the one navigation
 * affordance that gets faster the more screens there are rather than slower.
 *
 * Built off the same menu the sidebar draws, so it inherits the server's filtering:
 * a screen the signed-in user may not reach is not in the menu, and so is not here
 * either. Nothing is hard-coded, which also means an entry an administrator renames
 * is searchable under its new name without anything else changing.
 */

/** One reachable screen, flattened out of the menu tree. */
interface Destination {
  readonly id: string;
  readonly label: string;
  /** Its name in the other language, so a screen is found by either. */
  readonly otherLabel: string;
  /** The headings above it, so two screens with the same name are told apart. */
  readonly path: string;
  readonly route: string;
  readonly icon: string | null;
}

/**
 * Flattens the menu into the screens it can reach.
 *
 * Headings are not destinations — they open nothing — but their labels ride along on
 * the entries beneath them, which is what makes "transactions return" find the
 * purchase returns and "reports cash" find the cash book rather than the cash
 * account.
 */
export function destinationsOf(
  menu: Menu | undefined,
  language: string,
): readonly Destination[] {
  const found: Destination[] = [];

  const walk = (entries: readonly MenuEntry[], trail: readonly string[]): void => {
    for (const entry of entries) {
      const label = labelFor(entry, language);

      if (entry.route) {
        found.push({
          id: entry.id,
          label,
          otherLabel: label === entry.label ? (entry.labelArabic ?? '') : entry.label,
          path: trail.join(' › '),
          route: entry.route,
          icon: entry.icon,
        });
      }

      if (entry.children.length > 0) {
        walk(entry.children, entry.route ? trail : [...trail, label]);
      }
    }
  };

  walk(menu?.items ?? [], []);

  return found;
}

/**
 * Other words for the same screens: what a counter calls something that the menu
 * names differently. "Godown" is a warehouse, a "bill" an invoice.
 */
const SYNONYMS: Readonly<Record<string, readonly string[]>> = {
  godown: ['warehouse'],
  godowns: ['warehouses'],
  store: ['warehouse'],
  bill: ['invoice'],
  bills: ['invoices'],
  pos: ['sales invoice'],
  item: ['product'],
  items: ['products'],
  party: ['customer', 'supplier'],
  parties: ['customers', 'suppliers'],
  client: ['customer'],
  vendor: ['supplier'],
  voucher: ['payment', 'receipt', 'journal'],
  vouchers: ['payments', 'receipts', 'journals'],
  gst: ['vat'],
  inventory: ['stock'],
  account: ['ledger'],
  accounts: ['ledgers'],
};

/**
 * Edits between two words, a swap of neighbours counting as one: "invocie" is one
 * from "invoice", which is how fingers actually miss.
 */
function distance(a: string, b: string): number {
  const rows = a.length + 1;
  const cols = b.length + 1;
  const d: number[][] = Array.from({ length: rows }, (_, i) =>
    Array.from({ length: cols }, (_, j) => (i === 0 ? j : j === 0 ? i : 0)),
  );

  for (let i = 1; i < rows; i++) {
    for (let j = 1; j < cols; j++) {
      const cost = a[i - 1] === b[j - 1] ? 0 : 1;
      let best = Math.min(
        (d[i - 1]?.[j] ?? 0) + 1,
        (d[i]?.[j - 1] ?? 0) + 1,
        (d[i - 1]?.[j - 1] ?? 0) + cost,
      );

      if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1]) {
        best = Math.min(best, (d[i - 2]?.[j - 2] ?? 0) + 1);
      }

      (d[i] as number[])[j] = best;
    }
  }

  return d[a.length]?.[b.length] ?? 0;
}

/** Whether a typed word is a slip of the keys away from the start of a word in the text. */
function nearly(word: string, text: string): boolean {
  if (word.length < 4) return false;
  const allowed = word.length >= 7 ? 2 : 1;

  return text
    .split(/[^\p{L}\p{N}]+/u)
    .some(
      (candidate) =>
        candidate.length >= word.length - 1 &&
        distance(word, candidate.slice(0, word.length)) <= allowed,
    );
}

/** How well one typed word fits a destination: nought for not at all. */
function wordScore(destination: Destination, word: string): number {
  const label = destination.label.toLowerCase();
  const other = destination.otherLabel.toLowerCase();
  const path = destination.path.toLowerCase();
  const route = destination.route.toLowerCase();
  const startsAWord = (text: string): boolean =>
    text.split(/[^\p{L}\p{N}]+/u).some((part) => part.startsWith(word));

  // The screen's own name counts for most, and the start of it most of all; the
  // headings above it next; its address last - "/sales/customers" is how a customer
  // master came first for "sales" ahead of the sales invoices.
  if (label.startsWith(word)) return 100;
  if (startsAWord(label)) return 85;
  if (label.includes(word)) return 70;
  if (other.includes(word)) return 65;
  if (startsAWord(path)) return 40;
  if (path.includes(word)) return 30;
  if (route.includes(word)) return 15;
  if (nearly(word, label) || nearly(word, other)) return 10;
  return 0;
}

/**
 * How well a destination answers what was typed, or null when it does not.
 *
 * Every word must fit, in any order: "pur ret" finds Purchase returns, and so does
 * "returns". A word may also be one of its everyday synonyms, or a typing slip.
 */
export function scoreOf(destination: Destination, needle: string): number | null {
  const words = needle
    .toLowerCase()
    .split(/\s+/)
    .filter((word) => word !== '');

  let total = 0;

  for (const word of words) {
    const variants = [word, ...(SYNONYMS[word] ?? [])];
    const best = Math.max(
      ...variants.map((variant) =>
        variant.includes(' ')
          ? Math.min(...variant.split(' ').map((part) => wordScore(destination, part)))
          : wordScore(destination, variant),
      ),
    );

    if (best === 0) return null;
    total += best;
  }

  return total;
}

/** How many rows the list draws before it stops: all of them, short of a runaway menu. */
const LIMIT = 50;

export function CommandPalette({
  menu,
  language,
  onClose,
}: {
  readonly menu: Menu | undefined;
  readonly language: string;
  readonly onClose: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const navigate = useNavigate();

  const [needle, setNeedle] = useState('');
  const [active, setActive] = useState(0);
  const list = useRef<HTMLDivElement>(null);

  const destinations = useMemo(() => destinationsOf(menu, language), [menu, language]);

  // Best first; the menu's own order between equals, so an empty box lists the menu.
  const found = useMemo(
    () =>
      destinations
        .map((destination, index) => ({
          destination,
          index,
          score: scoreOf(destination, needle),
        }))
        .filter((row): row is typeof row & { score: number } => row.score !== null)
        .sort((a, b) => b.score - a.score || a.index - b.index)
        .map((row) => row.destination),
    [destinations, needle],
  );
  const shown = useMemo(() => found.slice(0, LIMIT), [found]);
  const total = found.length;

  // Typing changes what is on the list, so the highlight goes back to the top —
  // otherwise the fourth row stays selected while the list under it becomes one row.
  useEffect(() => setActive(0), [needle]);

  // Keeps the highlighted row in view when it is reached with the arrow keys.
  // Guarded because the method is optional in the DOM specifications and absent in
  // jsdom, and a palette that threw rather than scrolled would be a worse trade than
  // one that occasionally does not scroll.
  useEffect(() => {
    const row = list.current?.querySelector(`[data-index="${active}"]`);

    if (row && typeof row.scrollIntoView === 'function') {
      row.scrollIntoView({ block: 'nearest' });
    }
  }, [active]);

  const go = (destination: Destination | undefined): void => {
    if (!destination) {
      return;
    }

    onClose();
    navigate(destination.route);
  };

  return (
    <Dialog.Root open onOpenChange={(next) => !next && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-[70] flex items-start justify-center bg-slate-950/50 p-4 backdrop-blur-[2px] sm:p-6">
          {/*
            Held a little above the middle rather than centred. A palette that grows
            downwards as it fills should not also move, and the eye is already at the
            top of the screen where the search began.
          */}
          <Dialog.Content
            aria-describedby={undefined}
            /*
              The input below carries `autoFocus`, and it is the right thing to focus:
              a palette exists to be typed into. Letting Radix focus the panel first
              would put the caret nowhere for a frame.
            */
            onOpenAutoFocus={(event) => event.preventDefault()}
            className="animate-drop mt-[8vh] w-full max-w-xl overflow-hidden rounded-2xl border border-line bg-surface shadow-float outline-none"
          >
            <Dialog.Title className="sr-only">{t('palette.title')}</Dialog.Title>
            <div className="flex items-center gap-2 border-b border-line px-4">
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth={1.8}
                strokeLinecap="round"
                aria-hidden="true"
                className="size-4 shrink-0 text-ink-subtle"
              >
                <circle cx="11" cy="11" r="7" />
                <path d="m20 20-3.6-3.6" />
              </svg>

              <input
                // eslint-disable-next-line jsx-a11y/no-autofocus
                autoFocus
                type="text"
                role="combobox"
                aria-expanded
                aria-controls="palette-list"
                aria-label={t('palette.title')}
                value={needle}
                placeholder={t('palette.placeholder')}
                onChange={(event) => setNeedle(event.target.value)}
                onKeyDown={(event) => {
                  // Round from the last row to the first and back, as every list
                  // of this kind does.
                  if (event.key === 'ArrowDown') {
                    event.preventDefault();
                    setActive((index) => (index + 1) % Math.max(shown.length, 1));
                  } else if (event.key === 'ArrowUp') {
                    event.preventDefault();
                    setActive(
                      (index) =>
                        (index - 1 + Math.max(shown.length, 1)) %
                        Math.max(shown.length, 1),
                    );
                  } else if (event.key === 'Enter') {
                    event.preventDefault();
                    go(shown[active]);
                  }
                }}
                className="w-full border-0 bg-transparent py-3.5 text-sm text-ink outline-none placeholder:text-ink-subtle"
              />
            </div>

            <div
              ref={list}
              id="palette-list"
              role="listbox"
              className="max-h-80 overflow-y-auto p-2"
            >
              {shown.length === 0 && (
                <p className="px-3 py-6 text-center text-sm text-ink-muted">
                  {t('palette.nothing')}
                </p>
              )}

              {shown.map((destination, index) => {
                const Icon = iconFor(destination.icon, destination.route);

                return (
                  <button
                    key={destination.id}
                    type="button"
                    role="option"
                    data-index={index}
                    aria-selected={index === active}
                    onMouseEnter={() => setActive(index)}
                    onClick={() => go(destination)}
                    className={clsx(
                      'group/item flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-start transition-all',
                      index === active
                        ? 'bg-brand-50 text-brand-700 shadow-xs dark:bg-brand-500/15 dark:text-brand-200'
                        : 'bg-transparent text-ink hover:bg-surface-3/70',
                    )}
                  >
                    <span
                      className={clsx(
                        'grid size-8 shrink-0 place-items-center rounded-lg transition-colors',
                        index === active
                          ? 'bg-brand-600 text-white shadow-xs dark:bg-brand-500'
                          : 'bg-surface-3 text-ink-subtle group-hover/item:text-ink',
                      )}
                    >
                      <Icon className="size-4" />
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-semibold text-ink">
                        {destination.label}
                      </span>
                      {destination.path && (
                        <span className="block truncate text-xs text-ink-muted">
                          {destination.path}
                        </span>
                      )}
                    </span>
                    {index === active && (
                      <kbd className="shrink-0 rounded border border-brand-300/60 bg-white/80 px-1.5 py-0.5 font-mono text-[10px] font-semibold text-brand-700 shadow-xs dark:border-brand-500/40 dark:bg-slate-900/60 dark:text-brand-300">
                        ↵
                      </kbd>
                    )}
                  </button>
                );
              })}

              {total > shown.length && (
                <p className="px-3 pt-2 text-xs text-ink-subtle">
                  {t('palette.more', { count: total - shown.length })}
                </p>
              )}
            </div>

            <div className="flex items-center justify-between border-t border-line bg-surface-2/60 px-4 py-2 text-xs text-ink-subtle">
              <span>{t('palette.hint')}</span>
              <span className="hidden sm:inline text-[11px] font-medium text-ink-subtle">
                Spotlight
              </span>
            </div>
          </Dialog.Content>
        </Dialog.Overlay>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

/**
 * Whether the palette should open, given a keystroke.
 *
 * Cmd-K on a Mac, Ctrl-K everywhere else — the shortcut everything from an editor to
 * a chat client has trained people to try. From inside a field as well: an entry
 * screen is nearly all fields, and there Edge and Chrome took Ctrl-K for their own
 * web search, so the shortcut looked broken exactly where people were.
 *
 * By the key's place rather than its letter, so it still works with the keyboard
 * switched to Arabic, where that key types "ن". AltGr - Ctrl and Alt together on
 * some keyboards - is left alone.
 */
export function opensPalette(event: KeyboardEvent): boolean {
  if (event.code !== 'KeyK' && event.key !== 'k' && event.key !== 'K') {
    return false;
  }

  return (event.metaKey || event.ctrlKey) && !event.altKey;
}
