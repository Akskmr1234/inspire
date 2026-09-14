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

/** Whether a destination answers what was typed. */
function matches(destination: Destination, needle: string): boolean {
  if (needle === '') {
    return true;
  }

  const haystack =
    `${destination.label} ${destination.path} ${destination.route}`.toLowerCase();

  // Every word, in any order, anywhere in the label, its headings or its route. So
  // "pur ret" finds Purchase returns, and so does "returns".
  return needle
    .toLowerCase()
    .split(/\s+/)
    .filter((word) => word !== '')
    .every((word) => haystack.includes(word));
}

/** How many rows the list draws before it stops. */
const LIMIT = 8;

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

  const shown = useMemo(() => {
    const found = destinations.filter((destination) => matches(destination, needle));

    return found.slice(0, LIMIT);
  }, [destinations, needle]);

  const total = useMemo(
    () => destinations.filter((destination) => matches(destination, needle)).length,
    [destinations, needle],
  );

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
                  if (event.key === 'ArrowDown') {
                    event.preventDefault();
                    setActive((index) => Math.min(index + 1, shown.length - 1));
                  } else if (event.key === 'ArrowUp') {
                    event.preventDefault();
                    setActive((index) => Math.max(index - 1, 0));
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
                      'flex w-full items-center gap-3 rounded-lg px-3 py-2 text-start transition-colors',
                      index === active
                        ? 'bg-brand-50 dark:bg-brand-500/15'
                        : 'bg-transparent',
                    )}
                  >
                    <Icon />
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-medium text-ink">
                        {destination.label}
                      </span>
                      {destination.path && (
                        <span className="block truncate text-xs text-ink-muted">
                          {destination.path}
                        </span>
                      )}
                    </span>
                    {index === active && (
                      <span className="shrink-0 text-xs text-ink-subtle">↵</span>
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

            <p className="border-t border-line px-4 py-2 text-xs text-ink-subtle">
              {t('palette.hint')}
            </p>
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
 * a chat client has trained people to try. Ignored while the caret is in a field,
 * because Ctrl-K in a text box already means something to some people and stealing
 * it mid-sentence is worse than not offering the shortcut there.
 */
export function opensPalette(event: KeyboardEvent): boolean {
  if (event.key !== 'k' && event.key !== 'K') {
    return false;
  }

  if (!event.metaKey && !event.ctrlKey) {
    return false;
  }

  const target = event.target as HTMLElement | null;
  const tag = target?.tagName;

  return tag !== 'INPUT' && tag !== 'TEXTAREA' && target?.isContentEditable !== true;
}
