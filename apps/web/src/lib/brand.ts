/*
  The product's name, read from the address it is served at.

  One build serves every brand: aider.wishkraft.app is Aider ERP and
  inspire.wishkraft.app is Inspire ERP. The first label of the host name is the
  brand, so a new one needs a DNS name pointing at the same deployment rather than a
  build of its own. Where the host says nothing - localhost, an IP address - the
  build's VITE_APP_BRAND stands, and failing that, Aider.
*/

const FALLBACK = 'aider';

/** Labels that name the service rather than the brand: www.aider.example.com is Aider. */
const GENERIC = new Set(['www', 'app', 'erp', 'web']);

/** How a brand is written in Arabic, where it has an accepted spelling. */
const ARABIC: Readonly<Record<string, string>> = {
  aider: 'أيدر',
  inspire: 'إنسباير',
};

/** Everything the interface says about the product it is. */
export interface Brand {
  /** The label it was read from, in lower case: `inspire`. */
  readonly key: string;
  /** Inspire */
  readonly name: string;
  /** Inspire ERP */
  readonly appName: string;
  /** The same in Arabic script, where the brand has a spelling there. */
  readonly appNameArabic: string;
  /** The two letters on the logo tile: IE. */
  readonly initials: string;
}

/**
 * The brand a host name stands for, or nothing where it stands for none: a machine
 * name, `localhost`, or an address.
 */
export function brandKeyFromHost(hostname: string): string | null {
  const host = hostname.trim().toLowerCase().replace(/\.$/, '');

  if (
    host === '' ||
    host === 'localhost' ||
    host.includes(':') ||
    /^[\d.]+$/.test(host)
  ) {
    return null;
  }

  const labels = host.split('.').filter(Boolean);

  // A bare machine name ("devbox") is not a domain anybody chose a brand for.
  if (labels.length < 2) return null;

  return labels.find((label) => !GENERIC.has(label)) ?? null;
}

/** The brand for a host, falling back to the build's own and then to Aider. */
export function brandFor(hostname: string, fallback?: string): Brand {
  const key = brandKeyFromHost(hostname) ?? (fallback?.trim().toLowerCase() || FALLBACK);
  // "my-shop" reads as My Shop.
  const name = key
    .split(/[-_]+/)
    .filter(Boolean)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ');

  return {
    key,
    name,
    appName: `${name} ERP`,
    appNameArabic: `${ARABIC[key] ?? name} إي آر بي`,
    initials: `${name.charAt(0)}E`,
  };
}

/** The brand of the address this page was opened at. */
export const brand: Brand = brandFor(
  typeof window === 'undefined' ? '' : window.location.hostname,
  import.meta.env.VITE_APP_BRAND,
);

/**
 * Names the browser tab, the page's description and its icon after the brand - the
 * parts of the page outside the application, which the HTML file cannot know when one
 * build serves every brand.
 */
export function applyBrandToDocument(current: Brand = brand): void {
  document.title = current.appName;
  document
    .querySelector('meta[name="description"]')
    ?.setAttribute('content', current.appName);

  const icon =
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">' +
    '<defs><linearGradient id="g" x1="0%" y1="0%" x2="100%" y2="100%">' +
    '<stop offset="0%" stop-color="#6366f1"/><stop offset="100%" stop-color="#4338ca"/>' +
    '</linearGradient></defs><rect width="32" height="32" rx="8" fill="url(#g)"/>' +
    '<text x="16" y="21" font-family="system-ui, sans-serif" font-weight="700" ' +
    `font-size="14" fill="white" text-anchor="middle">${current.initials}</text></svg>`;

  document
    .querySelector('link[rel="icon"]')
    ?.setAttribute('href', `data:image/svg+xml,${encodeURIComponent(icon)}`);
}
