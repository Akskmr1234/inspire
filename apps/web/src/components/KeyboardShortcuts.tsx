import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';
import { Modal } from '@/components/Modal';
import {
  actOn,
  buttonFor,
  describe,
  ENTRY_SHORTCUTS,
  entryOpen,
  NEW_ENTRY_SHORTCUTS,
  onMac,
  SHOW_SHORTCUTS_EVENT,
  type EntryShortcut,
} from '@/lib/shortcuts';

/** How long a screen has to draw its New button after a shortcut opened it. */
const PATIENCE_MS = 8000;

function typing(target: EventTarget | null): boolean {
  const element = target as HTMLElement | null;
  const tag = element?.tagName;
  return (
    tag === 'INPUT' ||
    tag === 'TEXTAREA' ||
    tag === 'SELECT' ||
    element?.isContentEditable === true
  );
}

function matches(event: KeyboardEvent, shortcut: EntryShortcut): boolean {
  const mod = onMac() ? event.metaKey : event.ctrlKey;
  return (
    event.code === shortcut.code &&
    mod === (shortcut.mod ?? false) &&
    event.altKey === (shortcut.alt ?? false) &&
    event.shiftKey === (shortcut.shift ?? false)
  );
}

/**
 * The keyboard shortcuts, for the whole application: Alt and a letter opens a new
 * entry from anywhere, and inside an entry Ctrl+S saves it, Ctrl+Shift+S saves a draft,
 * Ctrl+P saves and prints, Ctrl+B bills, Alt+A adds a line and Alt+N starts another.
 * Alt+/ lists them all.
 *
 * Listened for on the window, before the page's own handlers, because a picker that
 * keeps its keystrokes to itself would otherwise swallow them. A shortcut that would
 * leave an entry being keyed in does not: it says to finish or close it first.
 */
export function KeyboardShortcuts(): React.JSX.Element | null {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const [helpOpen, setHelpOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const pending = useRef<{ route: string; until: number } | null>(null);
  const path = useRef(location.pathname);
  path.current = location.pathname;

  // Pressed once the screen a shortcut went to has drawn its New button.
  useEffect(() => {
    const tryPending = (): void => {
      const waiting = pending.current;
      if (!waiting) return;

      if (Date.now() > waiting.until) {
        pending.current = null;
        return;
      }

      if (path.current !== waiting.route) return;

      const button = buttonFor('new');
      if (button) {
        pending.current = null;
        button.click();
      }
    };

    const observer = new MutationObserver(tryPending);
    observer.observe(document.body, { childList: true, subtree: true });
    tryPending();

    return () => observer.disconnect();
  }, [location.pathname]);

  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(null), 2500);
    return () => window.clearTimeout(timer);
  }, [notice]);

  useEffect(() => {
    const show = (): void => setHelpOpen(true);
    window.addEventListener(SHOW_SHORTCUTS_EVENT, show);
    return () => window.removeEventListener(SHOW_SHORTCUTS_EVENT, show);
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.repeat) return;
      const mod = onMac() ? event.metaKey : event.ctrlKey;
      const other = onMac() ? event.ctrlKey : event.metaKey;

      // The list of shortcuts: Alt+/ anywhere, or ? where nothing is being typed.
      if (
        (event.altKey && !mod && event.code === 'Slash') ||
        (!event.altKey &&
          !mod &&
          event.shiftKey &&
          event.code === 'Slash' &&
          !typing(event.target))
      ) {
        event.preventDefault();
        setHelpOpen((open) => !open);
        return;
      }

      // Alt and a letter: a new entry. AltGr - Ctrl and Alt together on some
      // keyboards, and how they type ordinary letters - is left alone.
      if (event.altKey && !mod && !other) {
        const wanted = NEW_ENTRY_SHORTCUTS.find(
          (shortcut) =>
            shortcut.code === event.code && (shortcut.shift ?? false) === event.shiftKey,
        );

        if (wanted) {
          event.preventDefault();

          if (entryOpen()) {
            setNotice(t('shortcuts.finishFirst'));
            return;
          }

          if (wanted.direct) {
            void navigate(wanted.route);
            return;
          }

          pending.current = { route: wanted.route, until: Date.now() + PATIENCE_MS };

          if (path.current === wanted.route) {
            const button = buttonFor('new');
            if (button) {
              pending.current = null;
              button.click();
            }
          } else {
            void navigate(wanted.route);
          }
          return;
        }
      }

      const action = ENTRY_SHORTCUTS.find((shortcut) => matches(event, shortcut));
      if (!action || other) return;

      const button = buttonFor(action.action);

      // Ctrl+S never opens the browser's own "save page", here or anywhere.
      if (action.action === 'save' && action.code === 'KeyS') event.preventDefault();

      if (!button) return;
      event.preventDefault();
      actOn(button);
    };

    window.addEventListener('keydown', onKeyDown, true);
    return () => window.removeEventListener('keydown', onKeyDown, true);
  }, [navigate, t]);

  return (
    <>
      {notice && (
        <div
          role="status"
          className="fixed inset-x-0 bottom-6 z-[80] mx-auto w-fit rounded-lg bg-ink px-4 py-2 text-sm font-medium text-surface shadow-float"
        >
          {notice}
        </div>
      )}
      {helpOpen && <ShortcutList onClose={() => setHelpOpen(false)} />}
    </>
  );
}

/** Every shortcut, in two columns: opening a new entry, and working inside one. */
function ShortcutList({ onClose }: { readonly onClose: () => void }): React.JSX.Element {
  const { t } = useTranslation();

  // Ctrl+Enter is a second key for Save; listed once.
  const inside = ENTRY_SHORTCUTS.filter(
    (shortcut, index, all) =>
      all.findIndex((other) => other.labelKey === shortcut.labelKey) === index,
  );

  const row = (keys: string, label: string): React.JSX.Element => (
    <li
      key={`${keys}-${label}`}
      className="flex items-center justify-between gap-4 py-1.5"
    >
      <span className="text-sm text-ink">{label}</span>
      <kbd className="rounded-md border border-line bg-surface-2 px-2 py-0.5 font-mono text-xs text-ink shadow-sm">
        {keys}
      </kbd>
    </li>
  );

  return (
    <Modal title={t('shortcuts.title')} onClose={onClose} size="form">
      <div className="grid gap-6 sm:grid-cols-2">
        <section>
          <h3 className="mb-1 text-xs font-bold tracking-wide text-ink-muted uppercase">
            {t('shortcuts.newEntries')}
          </h3>
          <ul className="divide-y divide-line">
            {NEW_ENTRY_SHORTCUTS.map((shortcut) =>
              row(
                describe({
                  code: shortcut.code,
                  alt: true,
                  shift: shortcut.shift ?? false,
                }),
                t(shortcut.labelKey),
              ),
            )}
          </ul>
        </section>
        <section>
          <h3 className="mb-1 text-xs font-bold tracking-wide text-ink-muted uppercase">
            {t('shortcuts.inEntry')}
          </h3>
          <ul className="divide-y divide-line">
            {inside.map((shortcut) => row(describe(shortcut), t(shortcut.labelKey)))}
            {row('Esc', t('shortcuts.close'))}
          </ul>
          <h3 className="mt-4 mb-1 text-xs font-bold tracking-wide text-ink-muted uppercase">
            {t('shortcuts.general')}
          </h3>
          <ul className="divide-y divide-line">
            {row(describe({ code: 'KeyK', mod: true }), t('shortcuts.search'))}
            {row(describe({ code: 'Slash', alt: true }), t('shortcuts.list'))}
          </ul>
        </section>
      </div>
    </Modal>
  );
}
