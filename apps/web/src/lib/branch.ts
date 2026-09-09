/**
 * Which branch the session is working in.
 *
 * The server already decides this: `TenantResolutionMiddleware` reads the
 * `branch_id` claims off the bearer token, honours an `X-Erp-Branch` header when it
 * names one the user holds, and otherwise takes the first claim. Nothing this
 * application sends that header, so the branch is the first claim — and reading it
 * here is reading the same fact the server acted on, not guessing at it.
 *
 * The token is decoded, never verified. There is nothing to verify against in a
 * browser and nothing here depends on it being genuine: the claim decides which
 * saved preference to apply, and a forged one would change how many decimal places
 * this user sees their own figures to. Every request is still authorised by the
 * server against the signed token.
 */

/** Reads one claim out of a JWT payload, or null if it is not there to read. */
function claim(token: string | null, name: string): string | null {
  if (!token) {
    return null;
  }

  const payload = token.split('.')[1];

  if (payload === undefined) {
    return null;
  }

  try {
    // Base64url, which `atob` does not take: the two substitutions and the padding
    // are what make it ordinary base64. `decodeURIComponent` over the escaped bytes
    // is the standard way back to UTF-8, since `atob` yields one character a byte.
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    const json = decodeURIComponent(
      atob(padded)
        .split('')
        .map((character) => `%${character.charCodeAt(0).toString(16).padStart(2, '0')}`)
        .join(''),
    );

    const claims = JSON.parse(json) as Record<string, unknown>;
    const value = claims[name];

    // A claim held more than once arrives as an array. The server takes the first
    // when no header names one, so this does too.
    const first = Array.isArray(value) ? value[0] : value;

    return typeof first === 'string' && first !== '' ? first : null;
  } catch {
    return null;
  }
}

/** The claim the server scopes a request by. */
const BRANCH_CLAIM = 'branch_id';

/**
 * The branch this session is transacting in, or null before sign-in.
 *
 * Null is a real answer, not a failure: a user may hold no branch at all, and the
 * settings that key off this fall back to the firm-wide value when it is null.
 */
export function branchOf(token: string | null): string | null {
  return claim(token, BRANCH_CLAIM);
}
