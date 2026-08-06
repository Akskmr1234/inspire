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
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
