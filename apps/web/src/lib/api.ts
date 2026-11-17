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

import { branchOf, branchesOf, claim, displayNameOf, firmsOf } from '@/lib/branch';

const REFRESH_TOKEN_KEY = 'erp.refreshToken';
const TENANT_CODE_KEY = 'erp.tenantCode';
const DISPLAY_NAME_KEY = 'erp.displayName';

/** Reads the cached display name from previous sign-in. */
export function getStoredDisplayName(): string | null {
  return localStorage.getItem(DISPLAY_NAME_KEY);
}

/**
 * Where the API lives, baked in when the bundle is built.
 *
 * Vite inlines `VITE_*` variables at build time, so this is fixed the moment the
 * image is produced and cannot be changed by setting an environment variable on the
 * running container. Deploying the UI and the API as separate services therefore
 * means supplying the API's public HTTPS origin as a build argument.
 *
 * Left unset it falls back to a same-origin relative path, which is what local
 * development wants - Vite proxies `/api` to the API and the browser issues no
 * preflight - and what a single-origin deployment behind one reverse proxy wants too.
 * Deliberately no localhost default: a default that happens to work on a developer's
 * machine ships a bundle pointing at the user's own computer, which fails only in the
 * browser and leaves nothing in any log.
 */
const API_BASE_URL = (import.meta.env.VITE_API_URL ?? '').replace(/\/+$/, '');

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
  /** Set when the password was right but a second factor is still needed. */
  readonly mfa?: MfaChallenge | null;
}

/** The kinds of second factor, as the server numbers them. */
export const MfaFactorKind = { authenticator: 1, push: 2, backupCodes: 3 } as const;

/** A second factor sign-in can finish with. */
export interface MfaFactorChoice {
  readonly factorId: string;
  readonly kind: number;
  readonly label: string;
}

/** What finishing a sign-in with a second factor needs. */
export interface MfaChallenge {
  readonly ticket: string;
  readonly expiresAtUtc: string;
  readonly factors: readonly MfaFactorChoice[];
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

  const name = auth.displayName || displayNameOf(auth.accessToken);
  if (name) {
    localStorage.setItem(DISPLAY_NAME_KEY, name);
  }

  if (tenantCode) {
    localStorage.setItem(TENANT_CODE_KEY, tenantCode);
  }
}

/**
 * The branch the server would scope this session's requests to.
 *
 * Read from the access token rather than asked for, because the token is where the
 * server reads it from too — see `branchOf`. Null before sign-in, and null for a
 * user who holds no branch.
 */
export function currentBranchId(): string | null {
  const chosen = chosenBranch();
  return chosen !== null && branchesOf(accessToken).includes(chosen)
    ? chosen
    : branchOf(accessToken);
}

/** Where the branch somebody switched to is kept, across reloads, on this device. */
const BRANCH_KEY = 'erp.branch';

function chosenBranch(): string | null {
  try {
    return localStorage.getItem(BRANCH_KEY);
  } catch {
    return null;
  }
}

/** Every branch the session may work in, as the token lists them. */
export function sessionBranchIds(): readonly string[] {
  return branchesOf(accessToken);
}

/**
 * Switches the branch the session works in. Sent to the server on every request,
 * which honours it only for a branch the token already grants.
 */
export function chooseBranch(branchId: string): void {
  try {
    localStorage.setItem(BRANCH_KEY, branchId);
  } catch {
    // Storage refused (a private window): the switch lasts until the page reloads.
  }
}

/** The signed-in user, by the identifier the server knows them by; null before sign-in. */
export function currentUserId(): string | null {
  return claim(accessToken, 'sub');
}

/** The firm the session works in: the one switched to, else the token's first, as the server takes it. */
export function currentFirmId(): string | null {
  const chosen = chosenFirm();
  return chosen !== null && firmsOf(accessToken).includes(chosen)
    ? chosen
    : claim(accessToken, 'firm_id');
}

/** Where the firm somebody switched to is kept, across reloads, on this device. */
const FIRM_KEY = 'erp.firm';

function chosenFirm(): string | null {
  try {
    return localStorage.getItem(FIRM_KEY);
  } catch {
    return null;
  }
}

/** Every firm the session may work in, as the token lists them. */
export function sessionFirmIds(): readonly string[] {
  return firmsOf(accessToken);
}

/**
 * Switches the firm the session works in, and with it the branch - one of that firm's,
 * since a branch of the firm left behind would not be the server's to honour.
 */
export function chooseFirm(firmId: string, branchId: string | null): void {
  try {
    localStorage.setItem(FIRM_KEY, firmId);
    if (branchId) localStorage.setItem(BRANCH_KEY, branchId);
    else localStorage.removeItem(BRANCH_KEY);
  } catch {
    // Storage refused (a private window): the switch lasts until the page reloads.
  }
}

/** A user's default firm and branch, chosen in Settings. */
export interface DefaultSelection {
  readonly firmId: string;
  readonly branchId: string | null;
}

const defaultKey = (userId: string): string => `erp.default.${userId}`;

/** The firm and branch a user starts in at sign-in on this device, if they chose one. */
export function defaultSelection(
  userId: string | null = currentUserId(),
): DefaultSelection | null {
  if (!userId) return null;
  try {
    const stored = localStorage.getItem(defaultKey(userId));
    if (!stored) return null;
    const parsed = JSON.parse(stored) as Partial<DefaultSelection>;
    return typeof parsed.firmId === 'string'
      ? { firmId: parsed.firmId, branchId: parsed.branchId ?? null }
      : null;
  } catch {
    return null;
  }
}

/** Saves, or with null clears, the current user's default firm and branch. */
export function saveDefaultSelection(selection: DefaultSelection | null): void {
  const userId = currentUserId();
  if (!userId) return;
  try {
    if (selection) localStorage.setItem(defaultKey(userId), JSON.stringify(selection));
    else localStorage.removeItem(defaultKey(userId));
  } catch {
    // Storage refused (a private window): nothing to remember it in.
  }
}

/**
 * Moves the session to the user's default firm and branch, where they chose one and
 * this sign-in still grants it. Called at sign-in, so whoever used the device before
 * does not decide where the next person starts.
 */
export function applyDefaultSelection(): void {
  const chosen = defaultSelection();
  if (!chosen || !firmsOf(accessToken).includes(chosen.firmId)) return;
  const branch =
    chosen.branchId !== null && branchesOf(accessToken).includes(chosen.branchId)
      ? chosen.branchId
      : null;
  chooseFirm(chosen.firmId, branch);
}

/**
 * Reads the current user's display name, either from the active token or from local storage.
 */
export function currentDisplayName(): string | null {
  return displayNameOf(accessToken) ?? getStoredDisplayName();
}

/** Clears the session. The company code is kept: it is a convenience, not a secret. */
export function clearSession(): void {
  accessToken = null;
  localStorage.removeItem(REFRESH_TOKEN_KEY);
  localStorage.removeItem(DISPLAY_NAME_KEY);
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

    // The firm and branch switched to, where the token grants them; the server checks again.
    const firm = chosenFirm();
    if (firm !== null && firmsOf(accessToken).includes(firm)) {
      headers['X-Erp-Firm'] = firm;
    }

    const chosen = chosenBranch();
    if (chosen !== null && branchesOf(accessToken).includes(chosen)) {
      headers['X-Erp-Branch'] = chosen;
    }
  }

  return fetch(`${API_BASE_URL}/api/v1${path}`, {
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
        // The tenant code travels with the refresh. Refresh arrives
        // unauthenticated and refresh tokens are tenant-scoped, so without it the
        // server cannot find the token at all.
        body: { refreshToken, tenantCode: getStoredTenantCode() },
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

  if (response.status === 204) return undefined as TResponse;

  // A 202 can come back empty (a push still waiting for the phone), so an empty
  // body reads as nothing rather than failing to parse.
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as TResponse;
}

/** Issues a request for a binary body - a program to download - renewing the token once if needed. */
export async function requestBlob(path: string): Promise<Blob> {
  let response = await send(path, {});

  if (response.status === 401) {
    if (await refreshSession()) {
      response = await send(path, {});
    }
  }

  if (!response.ok) {
    throw await toApiError(response);
  }

  return response.blob();
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

  // A second factor still to pass: no tokens yet, only the ticket.
  if (auth.mfa) return auth;

  setSession(auth, tenantCode);
  return auth;
}

/** Finishes a sign-in with a code from an authenticator app or a backup code. */
export async function verifyMfaCode(
  challenge: MfaChallenge,
  factorId: string,
  code: string,
  tenantCode: string,
): Promise<AuthenticationResponse> {
  const auth = await request<AuthenticationResponse>('/auth/mfa/verify', {
    method: 'POST',
    body: { ticket: challenge.ticket, factorId, code },
    skipRefresh: true,
  });

  setSession(auth, tenantCode);
  return auth;
}

/** Sends a sign-in request to the phone behind a push factor. */
export function sendMfaPush(
  challenge: MfaChallenge,
  factorId: string,
): Promise<{ readonly pushTicket: string; readonly expiresAtUtc: string }> {
  return request('/auth/mfa/push', {
    method: 'POST',
    body: { ticket: challenge.ticket, factorId },
    skipRefresh: true,
  });
}

/** Asks whether the phone approved; signs in once it has. Null while still waiting. */
export async function pollMfaPush(
  challenge: MfaChallenge,
  pushTicket: string,
  tenantCode: string,
): Promise<AuthenticationResponse | null> {
  const auth = await request<AuthenticationResponse | undefined>('/auth/mfa/push/poll', {
    method: 'POST',
    body: { ticket: challenge.ticket, pushTicket },
    skipRefresh: true,
  });

  if (!auth?.accessToken) return null;

  setSession(auth, tenantCode);
  return auth;
}

/** Changes the signed-in user's password. */
export function changePassword(
  currentPassword: string,
  newPassword: string,
): Promise<void> {
  return request('/auth/change-password', {
    method: 'POST',
    body: { currentPassword, newPassword },
  });
}

/** Signs out, revoking the refresh token server-side. */
export async function logout(): Promise<void> {
  const refreshToken = getStoredRefreshToken();

  if (refreshToken) {
    // Best effort. A failure here must not strand the user in a signed-in UI
    // when they have asked to leave, so the local session is cleared regardless.
    await request('/auth/logout', {
      method: 'POST',
      body: { refreshToken, tenantCode: getStoredTenantCode() },
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

export interface UserProfile {
  readonly userId: string;
  readonly userName: string;
  readonly displayName: string;
  readonly email: string;
  readonly tenantId: string;
  readonly firmId: string | null;
  readonly branchId: string | null;
  readonly roles: readonly string[];
  readonly mustChangePassword: boolean;
}

/** Returns the signed-in user's profile details. */
export function fetchCurrentUserProfile(): Promise<UserProfile> {
  return request<UserProfile>('/auth/me');
}
