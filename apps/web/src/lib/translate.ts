/**
 * Machine translation, for the Arabic name fields.
 *
 * A bilingual master is a real cost: every category, brand and product carries a
 * second name, and somebody who does not read Arabic is the person most often
 * entering them. Filling the field in from the English one turns that from a
 * blocker into a review — which is the whole point, and why what comes back lands in
 * an ordinary editable box rather than a read-only one. A machine translation of a
 * product name is a first draft; a firm printing it on an invoice will want to
 * correct some of them, and it must be able to.
 *
 * Off unless a firm turns it on in Settings. It sends what somebody typed to a
 * service outside the installation, and that is not a decision to make on their
 * behalf.
 */

/** How long to wait before giving up on a translation. */
const TIMEOUT_MS = 6000;

/** The official endpoint, used when a firm has supplied a key. */
const CLOUD_ENDPOINT = 'https://translation.googleapis.com/language/translate/v2';

/**
 * The endpoint the web translator itself calls, used when no key is supplied.
 *
 * No key, no billing, and no promises: Google rate-limits it as it sees fit and may
 * refuse outright. That is why a failure here is silent — the field is left for
 * somebody to type into, which is exactly where they were before.
 */
const PUBLIC_ENDPOINT = 'https://translate.googleapis.com/translate_a/single';

interface CloudTranslateResponse {
  readonly data?: {
    readonly translations?: readonly { readonly translatedText?: string }[];
  };
}

/**
 * Translates a phrase, or answers null when it cannot.
 *
 * Never throws. Every caller is a text box being filled in as a convenience, and a
 * form that refuses to submit because a translation service was unreachable would
 * be a worse form than one with an empty Arabic field.
 */
export async function translate(
  text: string,
  target = 'ar',
  source = 'en',
  apiKey = '',
): Promise<string | null> {
  const phrase = text.trim();

  if (phrase === '') {
    return null;
  }

  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), TIMEOUT_MS);

  try {
    if (apiKey.trim() !== '') {
      const response = await fetch(
        `${CLOUD_ENDPOINT}?key=${encodeURIComponent(apiKey.trim())}`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ q: phrase, source, target, format: 'text' }),
          signal: controller.signal,
        },
      );

      if (!response.ok) {
        return null;
      }

      const body = (await response.json()) as CloudTranslateResponse;

      return body.data?.translations?.[0]?.translatedText ?? null;
    }

    const query = new URLSearchParams({
      client: 'gtx',
      sl: source,
      tl: target,
      dt: 't',
      q: phrase,
    });

    const response = await fetch(`${PUBLIC_ENDPOINT}?${query.toString()}`, {
      signal: controller.signal,
    });

    if (!response.ok) {
      return null;
    }

    // The shape is a nest of arrays rather than an object: [[[translated, original,
    // …], …], …]. Read defensively — it is an undocumented endpoint and the only
    // guarantee about its shape is that nobody promised one.
    const body: unknown = await response.json();

    if (!Array.isArray(body) || !Array.isArray(body[0])) {
      return null;
    }

    const pieces = (body[0] as unknown[])
      .map((segment) =>
        Array.isArray(segment) && typeof segment[0] === 'string' ? segment[0] : '',
      )
      .join('');

    return pieces.trim() === '' ? null : pieces;
  } catch {
    // Aborted, offline, blocked by a content policy, refused by the service. All
    // one outcome here: no translation, and a field somebody types into instead.
    return null;
  } finally {
    window.clearTimeout(timer);
  }
}
