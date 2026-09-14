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
   * `wide` for a document with its own lines; `form` for a handful of fields,
   * which at 5xl would be a row of boxes stranded across a metre of dialog.
   */
  readonly size?: 'wide' | 'form';
  readonly children: React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    /*
      Always open: this component is mounted when the dialog should exist and
      unmounted when it should not, which is how every caller already uses it.
      `onOpenChange` therefore only ever fires to close.
    */
    <Dialog.Root open onOpenChange={(open) => !open && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 overflow-y-auto overscroll-contain bg-slate-950/50 backdrop-blur-[2px]">
          {/*
            The panel is inside the overlay so the two scroll as one: a document
            taller than the window is scrolled by dragging anywhere over the dim,
            not only over the card.
          */}
          <div className="flex min-h-full items-start justify-center sm:p-6">
            <Dialog.Content
              aria-describedby={undefined}
              /*
                Radix returns focus to the opener by itself, and would do it after
                our own `onClose` has already re-rendered the list underneath. The
                opener is still there — these dialogs are opened from a toolbar
                button or a row that survives the close — so the default is right and
                only needs to not fight the animation.
              */
              className={clsx(
                'animate-rise flex min-h-full w-full flex-col gap-4 border-line bg-surface p-4',
                'shadow-float outline-none sm:min-h-0 sm:rounded-2xl sm:border sm:p-5',
                size === 'wide' ? 'max-w-5xl' : 'max-w-2xl',
              )}
            >
              <div className="flex items-center justify-between gap-3">
                <Dialog.Title className="truncate text-lg font-semibold tracking-tight text-ink">
                  {title}
                </Dialog.Title>

                <Dialog.Close
                  className="btn-icon"
                  aria-label={t('common.close')}
                  title={t('common.close')}
                >
                  <IconClose />
                </Dialog.Close>
              </div>

              {children}
            </Dialog.Content>
          </div>
        </Dialog.Overlay>
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
        'btn px-3 py-1.5 text-sm',
        primary
          ? 'bg-brand-600 text-white shadow-xs hover:bg-brand-700'
          : 'border border-line-strong bg-surface text-ink hover:bg-surface-3',
      )}
    >
      {children}
    </button>
  );
}
