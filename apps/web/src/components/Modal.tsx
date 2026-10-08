import { useState } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { IconClose } from '@/components/icons';

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
 */
export function Modal({
  title,
  onClose,
  size = 'wide',
  children,
}: {
  readonly title: string;
  readonly onClose: () => void;
  /**
   * `fullscreen` for complete viewport full screen; `full` for wide 7xl tables; `wide` for standard documents; `form` for compact forms.
   */
  readonly size?: 'full' | 'wide' | 'form' | 'fullscreen';
  readonly children: React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [isFullscreen, setIsFullscreen] = useState(size === 'fullscreen');

  return (
    /*
      Always open: this component is mounted when the dialog should exist and
      unmounted when it should not, which is how every caller already uses it.
      `onOpenChange` therefore only ever fires to close.
    */
    <Dialog.Root open onOpenChange={(open) => !open && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-slate-950/65 dark:bg-black/80 backdrop-blur-md transition-opacity duration-200" />
        <div
          className={clsx(
            'fixed inset-0 z-50 overflow-y-auto overscroll-contain flex min-h-full items-center justify-center',
            isFullscreen ? 'p-0' : 'p-2 sm:p-4 md:p-6',
          )}
        >
          <Dialog.Content
            aria-describedby={undefined}
            className={clsx(
              'animate-rise flex w-full flex-col overflow-hidden border border-line/80 bg-surface shadow-2xl ring-1 ring-black/10 dark:border-white/10 dark:ring-white/10 outline-none transition-all duration-200',
              isFullscreen
                ? 'fixed inset-0 h-screen max-h-screen w-screen max-w-none rounded-none border-0 ring-0 m-0 z-50'
                : clsx(
                    'max-h-[92vh] sm:max-h-[90vh] rounded-2xl sm:rounded-3xl my-auto',
                    size === 'full' ? 'max-w-7xl' : size === 'wide' ? 'max-w-5xl' : 'max-w-2xl',
                  ),
            )}
          >
            <div className="sticky top-0 z-10 flex shrink-0 items-center justify-between border-b border-line/60 bg-surface/90 px-5 py-3.5 backdrop-blur-md sm:px-6">
              <Dialog.Title className="truncate text-base sm:text-lg font-bold tracking-tight text-ink">
                {title}
              </Dialog.Title>

              <div className="flex items-center gap-1">
                <button
                  type="button"
                  onClick={() => setIsFullscreen((prev) => !prev)}
                  className="btn-icon size-8 rounded-full hover:bg-surface-3 text-ink-muted hover:text-ink transition-colors"
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
                  className="btn-icon size-8 rounded-full hover:bg-surface-3 text-ink-muted hover:text-ink transition-colors"
                  aria-label={t('common.close')}
                  title={t('common.close')}
                >
                  <IconClose className="size-4" />
                </Dialog.Close>
              </div>
            </div>

            <div className="flex-1 overflow-y-auto overscroll-contain p-4 sm:p-6 min-h-0 flex flex-col gap-4">
              {children}
            </div>
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
}: {
  readonly onClick: () => void;
  readonly children: React.ReactNode;
  readonly primary?: boolean;
  readonly disabled?: boolean;
}): React.JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={clsx(
        'btn px-4 py-2 text-sm',
        primary
          ? 'btn-primary'
          : 'btn-secondary',
      )}
    >
      {children}
    </button>
  );
}
