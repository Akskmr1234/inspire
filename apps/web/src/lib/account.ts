import { request } from '@/lib/api';

/** A role as its holder sees it on their profile. */
export interface MyRole {
  readonly name: string;
  readonly description: string;
  readonly grantsAllPermissions: boolean;
}

/** One place the user is signed in. */
export interface MySession {
  readonly sessionId: string;
  readonly signedInAtUtc: string;
  readonly lastActiveAtUtc: string;
  readonly expiresAtUtc: string;
  readonly userAgent: string | null;
  readonly ipAddress: string | null;
  readonly isCurrent: boolean;
}

/** The signed-in user's own account. */
export interface MyAccount {
  readonly userId: string;
  readonly userName: string;
  readonly displayName: string;
  readonly email: string;
  readonly mobileNumber: string | null;
  readonly tenantCode: string;
  readonly tenantName: string;
  readonly roles: readonly MyRole[];
  readonly createdAtUtc: string;
  readonly lastLoginAtUtc: string | null;
  readonly passwordChangedAtUtc: string | null;
  readonly mustChangePassword: boolean;
  readonly sessions: readonly MySession[];
}

export interface UpdateMyAccount {
  readonly displayName: string;
  readonly email: string;
  readonly mobileNumber: string | null;
}

const ACCOUNT = '/auth/account';

export const fetchMyAccount = (): Promise<MyAccount> => request<MyAccount>(ACCOUNT);

export const updateMyAccount = (body: UpdateMyAccount): Promise<MyAccount> =>
  request<MyAccount>(ACCOUNT, { method: 'PUT', body });

export const endSession = (sessionId: string): Promise<void> =>
  request<void>(`${ACCOUNT}/sessions/${sessionId}`, { method: 'DELETE' });

export const endOtherSessions = (): Promise<{ readonly ended: number }> =>
  request<{ readonly ended: number }>(`${ACCOUNT}/sessions/sign-out-others`, {
    method: 'POST',
  });

/** What a browser's user-agent string says it is, in words a person recognises. */
export interface DeviceDescription {
  readonly browser: string;
  readonly system: string;
  readonly mobile: boolean;
}

/**
 * Names the browser and system behind a user agent.
 *
 * Order matters: Edge and Opera both claim to be Chrome, and Chrome claims to be
 * Safari, so the narrower names are tested first.
 */
export function describeDevice(userAgent: string | null): DeviceDescription {
  const ua = userAgent ?? '';
  const browser = /Edg\//.test(ua)
    ? 'Edge'
    : /OPR\/|Opera/.test(ua)
      ? 'Opera'
      : /Firefox\//.test(ua)
        ? 'Firefox'
        : /Chrome\/|CriOS\//.test(ua)
          ? 'Chrome'
          : /Safari\//.test(ua)
            ? 'Safari'
            : '';
  const system = /Windows/.test(ua)
    ? 'Windows'
    : /iPhone|iPad|iPod/.test(ua)
      ? 'iOS'
      : /Android/.test(ua)
        ? 'Android'
        : /Mac OS X|Macintosh/.test(ua)
          ? 'macOS'
          : /Linux/.test(ua)
            ? 'Linux'
            : '';
  return { browser, system, mobile: /Mobi|Android|iPhone|iPad/.test(ua) };
}

/** How strong a password looks: 0 (empty) to 4 (strong). */
export function passwordStrength(password: string): 0 | 1 | 2 | 3 | 4 {
  if (password.length === 0) return 0;
  let score = 0;
  if (password.length >= 12) score += 1;
  if (password.length >= 16) score += 1;
  if (/[a-z]/.test(password) && /[A-Z]/.test(password)) score += 1;
  if (/\d/.test(password)) score += 0.5;
  if (/[^A-Za-z0-9]/.test(password)) score += 0.5;
  if (password.length < 12) score = Math.min(score, 1);
  return Math.max(1, Math.min(4, Math.round(score))) as 1 | 2 | 3 | 4;
}

/** A permission code read as a module, a resource and a verb. */
export interface ReadablePermission {
  readonly module: string;
  readonly resource: string;
  readonly verb: string;
}

/** Groups permission codes by module and resource, for showing a person what they may do. */
export function groupPermissions(
  codes: Iterable<string>,
): Map<string, Map<string, string[]>> {
  const modules = new Map<string, Map<string, string[]>>();
  for (const code of codes) {
    const [module, resource, verb] = code.split(':');
    if (!module || !resource || !verb) continue;
    const resources = modules.get(module) ?? new Map<string, string[]>();
    const verbs = resources.get(resource) ?? [];
    verbs.push(verb);
    resources.set(resource, verbs);
    modules.set(module, resources);
  }
  return new Map(
    [...modules.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([module, resources]) => [
        module,
        new Map([...resources.entries()].sort(([a], [b]) => a.localeCompare(b))),
      ]),
  );
}

/** "stock-adjustment" → "Stock adjustment". */
export function humanise(code: string): string {
  const words = code.replace(/[-_]/g, ' ').trim();
  return words.charAt(0).toUpperCase() + words.slice(1);
}
