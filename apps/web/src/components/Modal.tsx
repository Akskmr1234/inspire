import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { IconClose } from '@/components/icons';
import { useModalBehaviour } from '@/components/useModalBehaviour';

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
 * Written once here rather than per screen. It began as two identical copies in
 * the sales and purchase screens, and the third caller is what makes that a
 * component: the focus handling in `useModalBehaviour` is the part nobody
 * remembers to repeat, and a dialog missing it is one a keyboard cannot escape.
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
  const panel = useModalBehaviour(onClose);

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto overscroll-contain bg-slate-950/50 backdrop-blur-[2px] sm:p-6"
      role="dialog"
      aria-modal="true"
      aria-label={title}
    >
      {/*
        `tabIndex={-1}` so the panel itself can take focus while a document is still
        loading and has no control to give it to yet.

        Full height and square-cornered on a phone, where a floating card with the
        page showing round its edges is a worse use of 390px than a sheet.
      */}
      <div
        ref={panel as React.RefObject<HTMLDivElement>}
        tabIndex={-1}
        className={clsx(
          'animate-rise flex min-h-full w-full flex-col gap-4 border-line bg-surface p-4',
          'shadow-float outline-none sm:min-h-0 sm:rounded-2xl sm:border sm:p-5',
          size === 'wide' ? 'max-w-5xl' : 'max-w-2xl',
        )}
      >
        <div className="flex items-center justify-between gap-3">
          <h2 className="truncate text-lg font-semibold tracking-tight text-ink">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            className="btn-icon"
            aria-label={t('common.close')}
            title={t('common.close')}
          >
            <IconClose />
          </button>
        </div>
        {children}
      </div>
    </div>
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
