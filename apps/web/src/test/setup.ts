import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';

/**
 * What every suite needs before it can render a component from this application.
 *
 * jsdom implements a browser only as far as the specifications it has got round to,
 * and the gaps below are all ones this code base actually walks into: the shell asks
 * `matchMedia` whether the viewport is narrow, the grid measures scrolling, and the
 * theme store reads a motion preference. Each would otherwise throw a
 * `not a function` from inside a component rather than a legible test failure.
 */

afterEach(() => {
  // Unmounts anything a test rendered. Without it a later query matches an element
  // left behind by an earlier test, which produces failures that move when you
  // reorder the file.
  cleanup();
});

/**
 * A `matchMedia` that answers, and that tests can steer.
 *
 * jsdom has no implementation at all. Returning a permanently false match would tie
 * every test to the desktop branch and leave the card view — half the reason this
 * suite exists — unreachable, so the query is answered from a list the tests set.
 */
const matches = new Set<string>();

export function setMatchingMedia(...queries: readonly string[]): void {
  matches.clear();

  for (const query of queries) {
    matches.add(query);
  }
}

window.matchMedia = ((query: string) => ({
  media: query,
  matches: matches.has(query),
  onchange: null,
  addEventListener: () => undefined,
  removeEventListener: () => undefined,
  addListener: () => undefined,
  removeListener: () => undefined,
  dispatchEvent: () => false,
})) as unknown as typeof window.matchMedia;

/*
  The CSV export builds a blob and clicks an anchor at it. jsdom implements the
  constructor but not `Blob.prototype.text`, so the contents are unreadable once the
  object exists — which is precisely what an export test needs to see. Recording the
  parts as they go in sidesteps that, and keeps the read synchronous.
*/
const contents = new WeakMap<Blob, string>();
const NativeBlob = globalThis.Blob;

globalThis.Blob = class extends NativeBlob {
  constructor(parts?: readonly BlobPart[], options?: BlobPropertyBag) {
    super(parts as BlobPart[], options);
    contents.set(this, (parts ?? []).map(String).join(''));
  }
} as typeof Blob;

/** The text a blob was built from. */
export function blobText(blob: Blob): string {
  return contents.get(blob) ?? '';
}

/*
  The export finishes by clicking an anchor at the blob URL. jsdom has no navigation,
  so it logs a "Not implemented" stack for every export test — noise that buries a
  real failure in CI output. The download itself is the browser's job and nothing
  here asserts on it; what the tests read is the blob, which is already captured.
*/
const nativeClick = HTMLAnchorElement.prototype.click;

HTMLAnchorElement.prototype.click = function click(this: HTMLAnchorElement): void {
  if (this.href.startsWith('blob:')) {
    return;
  }

  nativeClick.call(this);
};

export const objectUrls = new Map<string, Blob>();

URL.createObjectURL = ((blob: Blob) => {
  const url = `blob:${objectUrls.size}`;
  objectUrls.set(url, blob);
  return url;
}) as typeof URL.createObjectURL;

URL.revokeObjectURL = (() => undefined) as typeof URL.revokeObjectURL;
