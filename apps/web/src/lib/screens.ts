import { lazy, type ComponentType } from 'react';

/**
 * The screens, loaded on demand and fetched ahead of the click.
 *
 * Each screen is its own chunk so signing in does not download the whole
 * application. The cost of that was paid on every first visit: clicking a menu entry
 * started the download, and nothing appeared until it arrived — on a server across
 * the internet, the lag between a click and a screen that the menu was blamed for.
 * Every loader registers here so the shell can fetch them all in the background once
 * somebody is signed in, and by the time they click, the code is already in the
 * browser's cache.
 */
const loaders: Array<() => Promise<unknown>> = [];

/**
 * Wraps a named export for `lazy`, which understands default exports only.
 *
 * Several of these modules deliberately export more than one screen — the five
 * stock reports share their column definitions, and brands and categories are the
 * same master with a different noun — so requiring a default export per screen
 * would mean splitting files that belong together.
 */
export function lazyScreen<TProps extends object, TName extends string>(
  load: () => Promise<Record<TName, ComponentType<TProps>>>,
  name: TName,
): React.LazyExoticComponent<ComponentType<TProps>> {
  loaders.push(load);
  return lazy(() => load().then((module) => ({ default: module[name] })));
}

let started = false;

/**
 * Fetches every screen's code in the background, a few at a time.
 *
 * Idle-time and throttled, so it never competes with the screen somebody is
 * actually looking at: the first screen's data requests go first, and the rest of
 * the application follows behind them. Once only per page load; a failure is left
 * for the click to retry, which is what would have happened without this.
 */
export function prefetchScreens(): void {
  if (started) {
    return;
  }

  started = true;

  const queue = [...new Set(loaders)];
  const idle: (callback: () => void) => void =
    typeof window.requestIdleCallback === 'function'
      ? (callback) => window.requestIdleCallback(callback, { timeout: 2000 })
      : (callback) => window.setTimeout(callback, 200);

  const next = (): void => {
    const batch = queue.splice(0, 4);

    if (batch.length === 0) {
      return;
    }

    void Promise.allSettled(batch.map((load) => load())).then(() => idle(next));
  };

  idle(next);
}
