import { useEffect, useRef } from 'react';

/** Everything that can hold focus, minus the things that are focusable but inert. */
const FOCUSABLE = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',');

/**
 * The behaviour that separates a dialog from a div drawn on top of the page.
 *
 * Three things, all of which a sighted mouse user never notices and a keyboard user
 * cannot work without:
 *
 *   Focus moves into the dialog when it opens. Without it the caret stays on the
 *   button behind the overlay, so the first Tab walks the page underneath rather
 *   than the form in front.
 *
 *   Tab cycles within the dialog. Without it focus escapes into the page behind and
 *   there is no way back except a mouse.
 *
 *   Focus returns to whatever opened it on close, so a list of invoices does not
 *   dump the user back at the top of the document after every one they inspect.
 *
 * Escape closes, which is the other half of the contract.
 */
export function useModalBehaviour(
  onClose: () => void,
): React.RefObject<HTMLElement | null> {
  const container = useRef<HTMLElement | null>(null);

  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    const node = container.current;

    if (node) {
      // The first control if there is one, otherwise the panel itself — which is
      // why the caller gives the panel `tabIndex={-1}`.
      const first = node.querySelector<HTMLElement>(FOCUSABLE);
      (first ?? node).focus();
    }

    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        onClose();
        return;
      }

      if (event.key !== 'Tab' || !container.current) {
        return;
      }

      const focusable = [
        ...container.current.querySelectorAll<HTMLElement>(FOCUSABLE),
      ].filter(
        // A control scrolled out of view is still reachable; one that is genuinely
        // not rendered is not, and stopping on it would look like a dead Tab.
        //
        // `checkVisibility` rather than `offsetParent`. That property is null for
        // anything positioned `fixed` as well as for anything hidden, so a dialog
        // that pinned a toolbar inside itself would quietly drop it out of the
        // cycle. Where the method is missing the element is kept: over-including
        // costs one dead Tab stop, under-including strands the user behind the
        // overlay with no way back.
        (element) =>
          typeof element.checkVisibility === 'function'
            ? element.checkVisibility()
            : true,
      );

      if (focusable.length === 0) {
        return;
      }

      const first = focusable[0]!;
      const last = focusable[focusable.length - 1]!;

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    document.addEventListener('keydown', onKeyDown, true);

    return () => {
      document.body.style.overflow = previousOverflow;
      document.removeEventListener('keydown', onKeyDown, true);

      // Guarded: the opener may have been unmounted by whatever the dialog did.
      if (opener && document.contains(opener)) {
        opener.focus();
      }
    };
    // Mount and unmount only. Re-running on a new `onClose` identity would steal
    // focus back to the top of the dialog on every parent render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return container;
}
