/// <reference types="vite/client" />

/**
 * The build-time variables this application reads.
 *
 * Declared rather than left to Vite's permissive default so that a typo in a variable
 * name is a compile error instead of `undefined` at runtime - which, for a value that
 * decides where every API request goes, would surface as a broken page in a browser
 * and nothing at all in a build log.
 */
interface ImportMetaEnv {
  /**
   * The public origin of the ERP API, e.g. `https://api-erp.apps.example.com`.
   *
   * Must be a URL a browser can reach: an internal container hostname resolves inside
   * the platform's network but not on a user's machine. Leave it unset to call the
   * API on the same origin as the UI, which is what the dev-server proxy and a
   * single-origin reverse-proxy deployment both provide.
   */
  readonly VITE_API_URL?: string;

  /**
   * The brand where the address does not name one - `localhost`, an IP address - e.g.
   * `inspire`. On a domain the first label decides: inspire.wishkraft.app is
   * Inspire ERP whatever this says. Unset, it is Aider.
   */
  readonly VITE_APP_BRAND?: string;

  /**
   * The theme the application opens in for anybody who has not picked one: `light`,
   * `dark`, or `system` to follow the device. Unset, it is light. A theme the firm
   * picks in Settings, or a person picks for themselves, still wins.
   */
  readonly VITE_DEFAULT_THEME?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
