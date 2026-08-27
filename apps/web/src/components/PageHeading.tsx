import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from 'react';
import { createPortal } from 'react-dom';

/*
  A screen's title and the line describing it, carried by the application bar
  rather than by the screen itself.

  The bar is on every page already and is half empty — a wordmark at one edge and
  three switches at the other. Naming the screen again underneath it spent a whole
  row of the content area saying what the highlighted menu entry and the bar could
  say between them, and that row came out of the list: on a laptop it is the
  difference between eleven rows of a stock ledger and fourteen.

  The heading is moved with a portal rather than by lifting the text into a store
  the shell reads. A screen keeps naming itself where it is rendered, in one place
  with its subtitle and the controls belonging beside it, and there is no window
  during a navigation where the bar shows the departed screen's name because the
  arriving one has not registered yet.
*/

/** The bar's offer to the screens below it. */
interface HeadingSlot {
  /** Where a heading is rendered, or null when there is no shell around it. */
  readonly element: HTMLElement | null;
  /** Marks the slot as filled for as long as a heading is mounted. */
  readonly occupy: () => () => void;
}

const HeadingSlotContext = createContext<HeadingSlot | null>(null);

export const HeadingSlotProvider = HeadingSlotContext.Provider;

/**
 * The shell's half of the arrangement: the node headings are portalled into, and
 * whether one is currently there.
 *
 * `occupied` exists so the bar can drop the wordmark it shows on a narrow screen
 * once a screen has claimed the space. Counted rather than a flag, because a
 * navigation mounts the incoming screen before unmounting the outgoing one and a
 * flag would be switched off by the departing heading a moment after the arriving
 * one switched it on.
 */
export function useHeadingSlot(): {
  readonly slot: HeadingSlot;
  readonly setElement: (element: HTMLElement | null) => void;
  readonly occupied: boolean;
} {
  const [element, setElement] = useState<HTMLElement | null>(null);
  const [count, setCount] = useState(0);

  const occupy = useCallback(() => {
    setCount((value) => value + 1);
    return () => setCount((value) => value - 1);
  }, []);

  const slot = useMemo(() => ({ element, occupy }), [element, occupy]);

  return { slot, setElement, occupied: count > 0 };
}

/**
 * One screen's heading.
 *
 * Rendered into the application bar where there is one, and in place where there is
 * not — a screen mounted on its own in a test still says what it is, and the
 * fallback is what the print copy below is modelled on.
 */
export function PageHeading({
  title,
  subtitle,
  actions,
}: {
  readonly title: string;
  /** A line beside the title, for a period or a scope the title cannot carry. */
  readonly subtitle?: string | undefined;
  /** The one or two controls that belong with the title rather than with the body. */
  readonly actions?: React.ReactNode;
}): React.JSX.Element {
  const slot = useContext(HeadingSlotContext);
  const element = slot?.element ?? null;
  const occupy = slot?.occupy;

  useEffect(() => {
    if (!element || !occupy) {
      return;
    }

    return occupy();
  }, [element, occupy]);

  const inBar = element !== null;
  const hasSubtitle = subtitle !== undefined && subtitle !== '';

  const heading = (
    <>
      {/*
        Title and subtitle side by side once there is room for them. Stacked in a
        56px bar they fit, but only just, and on a phone the subtitle is a period
        that reads perfectly well under the name.
      */}
      <div className={inBar ? 'topbar-heading' : 'min-w-0'}>
        <h1 className={inBar ? 'topbar-title' : 'page-title'}>{title}</h1>
        {hasSubtitle && (
          <p className={inBar ? 'topbar-subtitle' : 'page-subtitle mt-0.5'}>{subtitle}</p>
        )}
      </div>
      {actions}
    </>
  );

  if (!inBar) {
    return <header className="page-header">{heading}</header>;
  }

  return (
    <>
      {createPortal(heading, element)}

      {/*
        The bar is `no-print`, so without this a trial balance would reach the
        auditor as a page of figures with nothing on it saying which report they
        are. Hidden on screen, where the bar is already carrying it.

        The `hidden` attribute as well as the class: `.page` spaces its children
        with `space-y`, whose selector skips `[hidden]` but not an element merely
        set to `display: none`. Without the attribute this invisible header would
        still push the filter bar down by the gap it is owed — a band of nothing
        where the heading used to be, which is the thing being removed.
      */}
      <header hidden className="print-only">
        <h1 className="page-title">{title}</h1>
        {hasSubtitle && <p className="page-subtitle mt-0.5">{subtitle}</p>}
      </header>
    </>
  );
}
