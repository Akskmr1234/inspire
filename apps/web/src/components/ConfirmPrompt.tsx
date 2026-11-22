import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';

/**
 * A yes/no question over the window it belongs to: "Do you want to save?", "Do you
 * want to close?". Yes has the focus (unless `focusCancel`), so Enter answers yes; Escape answers no.
 *
 * Escape is caught at the window ahead of any dialog underneath, so answering "no"
 * does not also close the window the question was about.
 */
export function ConfirmPrompt({
  title,
  body,
  onConfirm,
  onCancel,
  confirmLabel,
  cancelLabel,
  danger = false,
  focusCancel = false,
  children,
}: {
  readonly title: string;
  readonly body?: string;
  readonly onConfirm: () => void;
  readonly onCancel: () => void;
  /** Defaults to Yes. */
  readonly confirmLabel?: string;
  /** Defaults to No. */
  readonly cancelLabel?: string;
  /** Yes loses something (closing with work entered): shown in red. */
  readonly danger?: boolean;
  /** Put the focus on No, so Enter does not throw work away. */
  readonly focusCancel?: boolean;
  /** Further buttons between No and Yes, such as Hold. */
  readonly children?: React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();

  useEffect(() => {
    const escape = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return;
      event.stopPropagation();
      event.preventDefault();
      onCancel();
    };
    window.addEventListener('keydown', escape, true);
    return () => window.removeEventListener('keydown', escape, true);
  }, [onCancel]);

  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-950/40 p-4">
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="confirm-prompt-title"
        className="animate-rise w-full max-w-sm rounded-2xl border border-line bg-surface p-5 shadow-float"
      >
        <h3 id="confirm-prompt-title" className="text-base font-semibold text-ink">
          {title}
        </h3>
        {body && <p className="mt-1.5 text-sm text-ink-muted">{body}</p>}
        <div className="mt-5 flex flex-wrap justify-end gap-2">
          <button
            type="button"
            onClick={onCancel}
            className="btn-secondary"
            autoFocus={focusCancel}
          >
            {cancelLabel ?? t('common.no')}
          </button>
          {children}
          <button
            type="button"
            onClick={onConfirm}
            className={danger ? 'btn-danger' : 'btn-primary'}
            autoFocus={!focusCancel}
          >
            {confirmLabel ?? t('common.yes')}
          </button>
        </div>
      </div>
    </div>
  );
}

interface Question {
  readonly title: string;
  readonly run: () => void;
  readonly danger?: boolean;
}

/**
 * Asking first, for a screen that is not a Modal: `ask(title, run)` shows the
 * question and runs `run` on yes. Render `prompt` anywhere in the screen.
 */
export function useConfirm(): {
  readonly ask: (title: string, run: () => void, options?: { danger?: boolean }) => void;
  readonly prompt: React.ReactNode;
} {
  const [question, setQuestion] = useState<Question | null>(null);

  const ask = useCallback(
    (title: string, run: () => void, options?: { danger?: boolean }): void =>
      setQuestion({ title, run, ...(options?.danger ? { danger: true } : {}) }),
    [],
  );

  const prompt = question ? (
    <ConfirmPrompt
      title={question.title}
      danger={question.danger ?? false}
      onConfirm={() => {
        setQuestion(null);
        question.run();
      }}
      onCancel={() => setQuestion(null)}
    />
  ) : null;

  return { ask, prompt };
}
