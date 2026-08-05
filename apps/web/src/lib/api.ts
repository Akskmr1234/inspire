/**
 * The typed HTTP client for the ERP API.
 *
 * Token handling, deliberately split:
 *
 *   The access token is held in memory only. It is never written to
 *   localStorage, so a cross-site scripting flaw cannot read it out of storage
 *   and it disappears when the tab closes.
 *
 *   The refresh token is held in localStorage, because a signed-in user
 *   reasonably expects to stay signed in across a reload. That is a real
 *   trade-off, not an oversight: an httpOnly, SameSite cookie would be stronger,
 *   but the API does not set cookies, so moving to one is a server change. The
 *   mitigation that does exist is refresh-token rotation with family
 *   revocation - presenting a token twice revokes the whole session family,
 *   because reuse cannot be distinguished from theft.
 */

const REFRESH_TOKEN_KEY = 'erp.refreshToken';
const TENANT_CODE_KEY = 'erp.tenantCode';

let accessToken: string | null = null;

/** An error carrying the API's RFC 9457 problem details. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    /** The stable error code, e.g. `Voucher.NotBalanced`. Branch on this, not the message. */
    readonly code: string,
    readonly detail: string,
  ) {
    super(detail || code);
    this.name = 'ApiError';
  }
}

interface ProblemDetails {
  readonly title?: string;
  readonly detail?: string;
  readonly status?: number;
}

export interface AuthenticationResponse {
  readonly accessToken: string;
  readonly expiresAtUtc: string;
  readonly refreshToken: string;
  readonly mustChangePassword: boolean;
  readonly displayName: string;
}

/** Reads the persisted refresh token, if a previous session left one. */
export function getStoredRefreshToken(): string | null {
  return localStorage.getItem(REFRESH_TOKEN_KEY);
}

/** Reads the last company code used, so the sign-in form can prefill it. */
export function getStoredTenantCode(): string | null {
  return localStorage.getItem(TENANT_CODE_KEY);
}

/** Records the tokens from a successful sign-in or renewal. */
export function setSession(auth: AuthenticationResponse, tenantCode?: string): void {
  accessToken = auth.accessToken;
  localStorage.setItem(REFRESH_TOKEN_KEY, auth.refreshToken);

  if (tenantCode) {
    localStorage.setItem(TENANT_CODE_KEY, tenantCode);
  }
}

/** Clears the session. The company code is kept: it is a convenience, not a secret. */
export function clearSession(): void {
  accessToken = null;
  localStorage.removeItem(REFRESH_TOKEN_KEY);
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {};

  try {
    problem = (await response.json()) as ProblemDetails;
  } catch {
    // A non-JSON body (a proxy error page, an empty 502). Fall through to the
    // status text rather than masking the real status with a parse failure.
  }

  return new ApiError(
    response.status,
    problem.title ?? `Http.${response.status}`,
    problem.detail ?? response.statusText,
  );
}

interface RequestOptions {
  readonly method?: string;
  readonly body?: unknown;
  readonly signal?: AbortSignal;
  /** Set for the auth endpoints themselves, so a 401 does not trigger a refresh loop. */
  readonly skipRefresh?: boolean;
}

async function send(path: string, options: RequestOptions): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json' };

  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  if (accessToken) {
    headers.Authorization = `Bearer ${accessToken}`;
  }

  return fetch(`/api/v1${path}`, {
    method: options.method ?? 'GET',
    headers,
    ...(options.body !== undefined ? { body: JSON.stringify(options.body) } : {}),
    ...(options.signal ? { signal: options.signal } : {}),
  });
}

/**
 * Exchanges the stored refresh token for a new pair.
 *
 * Concurrent 401s share one in-flight attempt. Without that, three parallel
 * queries expiring together would each present the same refresh token, and the
 * server - correctly unable to tell reuse from theft - would revoke the entire
 * session family and sign the user out.
 */
let refreshInFlight: Promise<boolean> | null = null;

function refreshSession(): Promise<boolean> {
  refreshInFlight ??= (async () => {
    try {
      const refreshToken = getStoredRefreshToken();

      if (!refreshToken) {
        return false;
      }

      const response = await send('/auth/refresh', {
        method: 'POST',
        body: { refreshToken },
        skipRefresh: true,
      });

      if (!response.ok) {
        clearSession();
        return false;
      }

      setSession((await response.json()) as AuthenticationResponse);
      return true;
    } finally {
      refreshInFlight = null;
    }
  })();

  return refreshInFlight;
}

/** Issues a request, renewing the access token once if it has expired. */
export async function request<TResponse>(
  path: string,
  options: RequestOptions = {},
): Promise<TResponse> {
  let response = await send(path, options);

  if (response.status === 401 && !options.skipRefresh) {
    if (await refreshSession()) {
      response = await send(path, options);
    }
  }

  if (!response.ok) {
    throw await toApiError(response);
  }

  return response.status === 204
    ? (undefined as TResponse)
    : ((await response.json()) as TResponse);
}

/** Signs in and records the session. */
export async function login(
  userName: string,
  password: string,
  tenantCode: string,
): Promise<AuthenticationResponse> {
  const auth = await request<AuthenticationResponse>('/auth/login', {
    method: 'POST',
    body: { userName, password, tenantCode: tenantCode || null },
    skipRefresh: true,
  });

  setSession(auth, tenantCode);
  return auth;
}

/** Signs out, revoking the refresh token server-side. */
export async function logout(): Promise<void> {
  const refreshToken = getStoredRefreshToken();

  if (refreshToken) {
    // Best effort. A failure here must not strand the user in a signed-in UI
    // when they have asked to leave, so the local session is cleared regardless.
    await request('/auth/logout', {
      method: 'POST',
      body: { refreshToken },
      skipRefresh: true,
    }).catch(() => undefined);
  }

  clearSession();
}

/** Restores a session from a stored refresh token on first load. */
export async function restoreSession(): Promise<boolean> {
  return getStoredRefreshToken() ? refreshSession() : false;
}

/** Returns every permission code the signed-in user holds. */
export function fetchPermissions(): Promise<readonly string[]> {
  return request<readonly string[]>('/auth/permissions');
}
