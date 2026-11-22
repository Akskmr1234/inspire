import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react';
import { createPortal } from 'react-dom';
import * as Dialog from '@radix-ui/react-dialog';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { ConfirmPrompt } from '@/components/ConfirmPrompt';
import { IconClose, IconDocument } from '@/components/icons';
import { shortcutFor, shortcutProps, type EntryAction } from '@/lib/shortcuts';

/**
 * A form over the screen rather than above the list.
 *
 * Every list in this application is the point of the screen it is on, and the
 * fields that add a row to it are used once and read never. Left on the page they
 * hold a band of it open permanently — on the customer master, seven fields and a
 * button between the filters and the first row — so a list of several hundred
 * records is read a dozen at a time through the gap left over. In a dialog the
 * fields cost nothing until they are asked for, and the whole content area is the
 * list.
 *
 * Built on Radix rather than by hand. The markup here was always the easy half; the
 * hard half was a hundred and twenty lines of focus management — move focus in, cycle
 * Tab within, put it back on whatever opened the dialog, lock the body's scroll, and
 * decide what Escape means when a picker inside the dialog is also listening for it.
 * All of that is behaviour with a correct answer that somebody else maintains, and
 * every one of those lines was a line that could be wrong without looking wrong.
 *
 * The props are unchanged, so the fifteen screens that open one of these did not
 * have to learn anything.
 *
 * Dressed as an invoice is: a header with the window's icon, its title and what it
 * is for, the content under it, and the row of buttons that finishes it held along
 * the foot - every `.form-actions` row inside one is that bar - so a dialog anywhere
 * in the application reads like the bills do.
 */
interface ModalControl {
  /** Closes the window - asking "Do you want to close?" first in an editor. */
  readonly requestClose: () => void;
  /** Runs a save once "Do you want to save?" is answered yes. */
  readonly confirmSave: (save: () => void) => void;
}

const ModalControlContext = createContext<ModalControl | null>(null);

/** The window's own close, which asks first; outside a Modal, `fallback`. */
export function useModalClose(fallback: () => void): () => void {
  return useContext(ModalControlContext)?.requestClose ?? fallback;
}

/** Wraps a save so it asks "Do you want to save?" first; outside a Modal, as is. */
export function useConfirmedSave(): (save: () => void) => void {
  return useContext(ModalControlContext)?.confirmSave ?? ((save) => save());
}

/** The buttons that save: what makes a window an editor, and what asks first. */
const SAVE_SHORTCUTS = new Set<string>(['save', 'draft', 'print']);

/** A window is an editor when it holds a form or a button that saves. */
function isEditor(content: HTMLElement | null): boolean {
  return (
    content?.querySelector(
      'form:not([data-no-confirm]), [data-shortcut="save"], [data-shortcut="draft"]',
    ) != null
  );
}

/** Cancel beside Save: every `.form-actions` row without one gets it. */
function CancelButton({ onClick }: { readonly onClick: () => void }): React.JSX.Element {
  const { t } = useTranslation();
  return (
    <button
      type="button"
      data-modal-cancel=""
      onClick={onClick}
      className="btn-secondary"
    >
      {t('common.cancel')}
    </button>
  );
}

export function Modal({
  title,
  subtitle,
  icon,
  onClose,
  size = 'wide',
  children,
}: {
  readonly title: string;
  /** Under the title, quieter: what the window is for. */
  readonly subtitle?: string;
  /** In the tile beside the title. A document, unless the window is something else. */
  readonly icon?: React.ReactNode;
  readonly onClose: () => void;
  /**
   * `fullscreen` for complete viewport full screen; `full` for wide 7xl tables; `wide` for standard documents; `form` for compact forms.
   */
  readonly size?: 'full' | 'wide' | 'form' | 'fullscreen';
  readonly children: React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [isFullscreen, setIsFullscreen] = useState(size === 'fullscreen');
  const content = useRef<HTMLDivElement | null>(null);
  // The content as state too: Radix mounts it after this component, and the Cancel
  // slots can only be found once it is there.
  const [contentElement, setContentElement] = useState<HTMLDivElement | null>(null);
  const contentRef = useCallback((element: HTMLDivElement | null) => {
    content.current = element;
    setContentElement(element);
  }, []);
  const [asking, setAsking] = useState<'close' | { readonly save: () => void } | null>(
    null,
  );
  // A form submit that was answered yes, let through on its second pass.
  const confirmedForm = useRef<HTMLFormElement | null>(null);

  const requestClose = useCallback((): void => {
    if (isEditor(content.current)) setAsking('close');
    else onClose();
  }, [onClose]);

  const confirmSave = useCallback((save: () => void): void => {
    setAsking({ save });
  }, []);

  // Enter in a field, a submit button, or the save key: every form submit asks
  // first, then goes through as it would have.
  const onSubmitCapture = (event: React.FormEvent<HTMLDivElement>): void => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || form.hasAttribute('data-no-confirm'))
      return;
    if (confirmedForm.current === form) {
      confirmedForm.current = null;
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    const submitter = (event.nativeEvent as SubmitEvent).submitter ?? null;
    setAsking({
      save: () => {
        confirmedForm.current = form;
        if (submitter instanceof HTMLElement)
          form.requestSubmit(submitter as HTMLButtonElement);
        else form.requestSubmit();
      },
    });
  };

  const cancelHosts = useCancelHosts(contentElement);

  // A form's own Cancel, in its row of buttons, closes the way every Cancel does:
  // asking "Do you want to close?" first.
  const onClickCapture = (event: React.MouseEvent<HTMLDivElement>): void => {
    const button = (event.target as HTMLElement).closest('button');
    if (
      !button ||
      button.hasAttribute('data-modal-cancel') ||
      !button.closest('.form-actions') ||
      button.textContent?.trim() !== t('common.cancel')
    ) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    requestClose();
  };

  return (
    /*
      Always open: this component is mounted when the dialog should exist and
      unmounted when it should not, which is how every caller already uses it.
      `onOpenChange` therefore only ever fires to close.
    */
    <Dialog.Root open onOpenChange={(open) => !open && requestClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-slate-950/65 dark:bg-black/80 backdrop-blur-md transition-opacity duration-200" />
        <div
          className={clsx(
            'fixed inset-0 z-50 overflow-y-auto overscroll-contain flex min-h-full items-center justify-center',
            isFullscreen ? 'p-0' : 'p-2 sm:p-4 md:p-6',
          )}
        >
          <Dialog.Content
            ref={contentRef}
            onSubmitCapture={onSubmitCapture}
            onClickCapture={onClickCapture}
            aria-describedby={undefined}
            /*
              Prevent outside clicks or interactions from closing the modal; it
              should only close via explicit user close actions or submission.
            */
            onPointerDownOutside={(event) => event.preventDefault()}
            onInteractOutside={(event) => event.preventDefault()}
            className={clsx(
              'animate-rise flex w-full flex-col overflow-hidden border border-line/80 bg-surface shadow-2xl ring-1 ring-black/10 dark:border-white/10 dark:ring-white/10 outline-none transition-all duration-200',
              isFullscreen
                ? 'modal-fullscreen fixed inset-0 h-screen max-h-screen w-screen max-w-none rounded-none border-0 ring-0 m-0 z-50'
                : clsx(
                    'max-h-[92vh] sm:max-h-[90vh] rounded-2xl my-auto',
                    size === 'full'
                      ? 'max-w-7xl'
                      : size === 'wide'
                        ? 'max-w-5xl'
                        : 'max-w-2xl',
                  ),
            )}
          >
            <div className="flex shrink-0 items-center gap-3 border-b border-line/70 bg-canvas px-4 py-2 sm:px-5">
              <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-brand-500/10 text-brand-600 dark:text-brand-300">
                {icon ?? <IconDocument className="size-5" />}
              </span>
              <div className="flex min-w-0 flex-1 flex-col sm:flex-row sm:items-baseline sm:gap-3">
                <Dialog.Title className="truncate text-lg font-bold tracking-tight text-ink">
                  {title}
                </Dialog.Title>
                {subtitle && (
                  <p className="hidden truncate text-sm text-ink-muted sm:block">
                    {subtitle}
                  </p>
                )}
              </div>

              <div className="flex shrink-0 items-center gap-1">
                <button
                  type="button"
                  onClick={() => setIsFullscreen((prev) => !prev)}
                  className="btn-icon size-9 rounded-full hover:bg-surface-3 text-ink-muted hover:text-ink transition-colors"
                  aria-label={isFullscreen ? 'Exit Fullscreen' : 'Fullscreen'}
                  title={isFullscreen ? 'Exit Fullscreen' : 'Fullscreen'}
                >
                  {isFullscreen ? (
                    <svg
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth={1.8}
                      className="size-4"
                      aria-hidden="true"
                    >
                      <path d="M8 3v3a2 2 0 0 1-2 2H3m18 0h-3a2 2 0 0 1-2-2V3m0 18v-3a2 2 0 0 1 2-2h3M3 16h3a2 2 0 0 1 2 2v3" />
                    </svg>
                  ) : (
                    <svg
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth={1.8}
                      className="size-4"
                      aria-hidden="true"
                    >
                      <path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7" />
                    </svg>
                  )}
                </button>

                <Dialog.Close
                  className="btn-icon size-9 rounded-full hover:bg-surface-3 text-ink-muted hover:text-ink transition-colors"
                  aria-label={t('common.close')}
                  title={t('common.close')}
                >
                  <IconClose className="size-5" />
                </Dialog.Close>
              </div>
            </div>

            {/*
              `[&>*]:shrink-0`: the body is a column that scrolls, so nothing in it may
              shrink to fit. A flex item with `overflow` set has no automatic minimum
              height, and the line tables sit in an `overflow-x-auto` wrapper so they can
              scroll sideways — so the moment a document was taller than the dialog, the
              flexbox took the missing height out of that wrapper and squeezed it to
              zero. The product row was still there, under the Add line button, which
              is why a purchase could not be given an item.
            */}
            <div
              className={clsx(
                'modal-body flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto overscroll-contain p-4 sm:p-5 [&>*]:shrink-0',
                isFullscreen && 'bg-canvas',
              )}
            >
              {/* Full screen, the content is a card on the canvas, as an invoice's
                  are; in a dialog the dialog is the card. */}
              <ModalControlContext.Provider value={{ requestClose, confirmSave }}>
                {isFullscreen ? (
                  <div className="flex flex-col gap-4 rounded-2xl border border-line bg-surface p-4 shadow-card sm:p-5 [&>*]:shrink-0">
                    {children}
                  </div>
                ) : (
                  children
                )}
              </ModalControlContext.Provider>
            </div>
            {cancelHosts.map((host) =>
              createPortal(<CancelButton onClick={requestClose} />, host),
            )}
            {asking === 'close' && (
              <ConfirmPrompt
                title={t('common.confirmClose')}
                onConfirm={() => {
                  setAsking(null);
                  onClose();
                }}
                onCancel={() => setAsking(null)}
              />
            )}
            {asking !== null && asking !== 'close' && (
              <ConfirmPrompt
                title={t('common.confirmSave')}
                onConfirm={() => {
                  const { save } = asking;
                  setAsking(null);
                  save();
                }}
                onCancel={() => setAsking(null)}
              />
            )}
          </Dialog.Content>
        </div>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

/** A button in a dialog's own row of actions. */
export function ModalButton({
  onClick,
  children,
  primary,
  disabled,
  shortcut,
}: {
  readonly onClick: () => void;
  readonly children: React.ReactNode;
  readonly primary?: boolean;
  readonly disabled?: boolean;
  /** What a keyboard shortcut presses this button for - save, a draft, print. */
  readonly shortcut?: EntryAction;
}): React.JSX.Element {
  const confirmed = useConfirmedSave();
  const saves = shortcut !== undefined && SAVE_SHORTCUTS.has(shortcut);

  return (
    <button
      type="button"
      onClick={saves ? () => confirmed(onClick) : onClick}
      disabled={disabled}
      {...(shortcut ? shortcutProps(shortcut) : {})}
      {...(shortcut ? { title: shortcutFor(shortcut) } : {})}
      className={primary ? 'btn-primary' : 'btn-secondary'}
    >
      {children}
      {/* Its key on it, as on an invoice's buttons. */}
      {shortcut && (
        <kbd
          className={clsx(
            'hidden rounded px-1.5 py-px font-mono text-[10px] font-medium md:inline',
            primary ? 'bg-white/20 text-white' : 'bg-surface-3 text-ink-muted',
          )}
        >
          {shortcutFor(shortcut)}
        </kbd>
      )}
    </button>
  );
}

/**
 * The `.form-actions` rows in the window that have no Cancel of their own, each with
 * a slot at its start for one. Watched, because a tab or a step can bring a form in
 * after the window opened.
 */
function useCancelHosts(root: HTMLElement | null): HTMLElement[] {
  const { t } = useTranslation();
  const [hosts, setHosts] = useState<HTMLElement[]>([]);

  useEffect(() => {
    if (!root) return undefined;
    const cancelLabel = t('common.cancel');

    const scan = (): void => {
      const found: HTMLElement[] = [];
      root.querySelectorAll<HTMLElement>('.form-actions').forEach((row) => {
        // Only a row that saves, and that has no Cancel already.
        const saves = row.querySelector(
          'button[type="submit"], [data-shortcut="save"], [data-shortcut="draft"]',
        );
        if (!saves) return;
        let host = row.querySelector<HTMLElement>(':scope > [data-cancel-host]');
        const hasOwn = Array.from(row.querySelectorAll('button')).some(
          (button) =>
            !host?.contains(button) && button.textContent?.trim() === cancelLabel,
        );
        if (hasOwn) {
          host?.remove();
          return;
        }
        if (!host) {
          host = document.createElement('span');
          host.setAttribute('data-cancel-host', '');
          host.className = 'contents';
          row.insertBefore(host, row.firstChild);
        }
        found.push(host);
      });
      setHosts((current) =>
        current.length === found.length && current.every((h, i) => h === found[i])
          ? current
          : found,
      );
    };

    scan();
    const watcher = new MutationObserver(scan);
    watcher.observe(root, { childList: true, subtree: true });
    return () => watcher.disconnect();
  }, [root, t]);

  return hosts;
}
