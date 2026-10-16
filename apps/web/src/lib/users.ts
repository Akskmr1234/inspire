import { request } from '@/lib/api';

/** A user as the administration screen lists them. */
export interface UserSummary {
  readonly userId: string;
  readonly userName: string;
  readonly email: string;
  readonly displayName: string;
  readonly mobileNumber: string | null;
  readonly isActive: boolean;
  readonly mustChangePassword: boolean;
  readonly isLockedOut: boolean;
  readonly lastLoginAtUtc: string | null;
  readonly roleIds: readonly string[];
}

/** A role, with what it may do and the menus it shows. */
export interface RoleSummary {
  readonly roleId: string;
  readonly name: string;
  readonly description: string;
  readonly isSystemRole: boolean;
  readonly grantsAllPermissions: boolean;
  readonly permissionCodes: readonly string[];
  /** False: the role shows every menu its permissions reach. */
  readonly restrictsMenu: boolean;
  readonly menuCodes: readonly string[];
  readonly userCount: number;
}

/** One permission a role can grant: `module:resource:verb`. */
export interface PermissionDefinition {
  readonly code: string;
  readonly module: string;
  readonly resource: string;
  readonly verb: string;
  readonly description: string;
}

export interface SaveUser {
  readonly userName: string;
  readonly email: string;
  readonly displayName: string;
  readonly mobileNumber: string | null;
  /** A starting password for a new user, changed at their first sign-in. */
  readonly password: string | null;
  readonly roleIds: readonly string[];
  readonly isActive: boolean;
}

export interface SaveRole {
  readonly name: string;
  readonly description: string | null;
  readonly permissionCodes: readonly string[];
  /** Null shows every menu the permissions reach. */
  readonly menuCodes: readonly string[] | null;
}

const USERS = '/platform/users';
const ROLES = '/platform/roles';

export const listUsers = (): Promise<readonly UserSummary[]> =>
  request<readonly UserSummary[]>(USERS);

export const createUser = (body: SaveUser): Promise<UserSummary> =>
  request<UserSummary>(USERS, { method: 'POST', body });

export const updateUser = (id: string, body: SaveUser): Promise<UserSummary> =>
  request<UserSummary>(`${USERS}/${id}`, { method: 'PUT', body });

export const resetPassword = (id: string, password: string): Promise<void> =>
  request<void>(`${USERS}/${id}/reset-password`, { method: 'POST', body: { password } });

export const listRoles = (): Promise<readonly RoleSummary[]> =>
  request<readonly RoleSummary[]>(ROLES);

export const listPermissions = (): Promise<readonly PermissionDefinition[]> =>
  request<readonly PermissionDefinition[]>(`${ROLES}/permissions`);

export const createRole = (body: SaveRole): Promise<RoleSummary> =>
  request<RoleSummary>(ROLES, { method: 'POST', body });

export const updateRole = (id: string, body: SaveRole): Promise<RoleSummary> =>
  request<RoleSummary>(`${ROLES}/${id}`, { method: 'PUT', body });

export const deleteRole = (id: string): Promise<void> =>
  request<void>(`${ROLES}/${id}`, { method: 'DELETE' });
