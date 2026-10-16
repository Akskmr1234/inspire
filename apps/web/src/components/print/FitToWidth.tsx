import { useLayoutEffect, useRef, useState } from 'react';

/**
 * Shows a fixed-width page scaled down to whatever room it has, and never up.
 *
 * An A4 preview is about 800px wide. Left at that size it either pushed a phone's
 * page sideways or sat in a box the user had to scroll in two directions to read a
 * total; scaled, the whole sheet is in view, which is what a preview is for. The
 * printed copy is rendered separately and is not touched.
 */
export function FitToWidth({
  children,
}: {
  readonly children: React.ReactNode;
}): React.JSX.Element {
  const outer = useRef<HTMLDivElement>(null);
  const inner = useRef<HTMLDivElement>(null);
  const [fit, setFit] = useState({ scale: 1, height: 0, rtl: false });

  useLayoutEffect(() => {
    const box = outer.current;
    const page = inner.current;
    if (!box || !page) return;

    const measure = (): void => {
      const natural = page.offsetWidth;
      const room = box.clientWidth;
      const scale = natural > 0 && room > 0 ? Math.min(1, room / natural) : 1;
      // A page wider than its box hangs off the box's end edge, so it shrinks
      // towards the start edge: the left in English, the right in Arabic.
      const rtl = getComputedStyle(box).direction === 'rtl';
      setFit({ scale, height: page.offsetHeight * scale, rtl });
    };

    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(box);
    observer.observe(page);
    return () => observer.disconnect();
  }, []);

  return (
    <div
      ref={outer}
      className="w-full min-w-0"
      style={{ height: fit.height || undefined }}
    >
      <div
        ref={inner}
        className="mx-auto w-fit shadow-xl"
        style={{
          transform: fit.scale < 1 ? `scale(${fit.scale})` : undefined,
          transformOrigin: fit.rtl ? 'top right' : 'top left',
        }}
      >
        {children}
      </div>
    </div>
  );
}
