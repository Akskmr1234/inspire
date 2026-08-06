import { request } from '@/lib/api';

/**
 * One entry in the navigation menu, as the API resolves it.
 *
 * The server has already dropped everything the signed-in user cannot reach, and
 * every heading left empty by that filtering, so the client renders what it is given
 * without deciding anything.
 */
export interface MenuEntry {
  readonly id: string;
  readonly code: string;
  readonly label: string;
  readonly labelArabic: string | null;
  readonly icon: string | null;
  /** The client route this entry opens, or null for a heading. */
  readonly route: string | null;
  readonly module: string;
  readonly children: readonly MenuEntry[];
}

export interface Menu {
  readonly items: readonly MenuEntry[];
}

/** Fetches the menu for the signed-in user in the selected firm. */
export function fetchMenu(): Promise<Menu> {
  return request<Menu>('/menu');
}

/**
 * Picks the label to show for the active language.
 *
 * Falls back to the English label when no Arabic one has been recorded. An entry an
 * administrator added themselves will usually have only the one label, and showing it
 * is better than showing a blank row.
 */
export function labelFor(entry: MenuEntry, language: string): string {
  return language === 'ar' && entry.labelArabic ? entry.labelArabic : entry.label;
}

/**
 * One entry as the administration screen sees it.
 *
 * A different shape from {@link MenuEntry} because it answers a different question.
 * That one is what the user may navigate to; this is what exists — including entries
 * switched off, which somebody has to be able to see in order to switch back on.
 */
export interface MenuAdminEntry {
  readonly id: string;
  readonly code: string;
  readonly label: string;
  readonly labelArabic: string | null;
  readonly icon: string | null;
  readonly route: string | null;
  readonly module: string;
  readonly requiredPermission: string | null;
  readonly sortOrder: number;
  readonly isEnabled: boolean;
  /** Seeded entries can be relabelled, moved and hidden, but never deleted. */
  readonly isSystem: boolean;
  readonly children: readonly MenuAdminEntry[];
}

export interface MenuAdmin {
  readonly items: readonly MenuAdminEntry[];
}

const ADMIN_MENU = '/admin/menu';

/** Fetches the whole tree, hidden entries included. */
export function fetchMenuAdmin(): Promise<MenuAdmin> {
  return request<MenuAdmin>(ADMIN_MENU);
}

/** Adds an entry. */
export function createMenuItem(body: {
  readonly code: string;
  readonly label: string;
  readonly module: string;
  readonly parentId?: string | null;
  readonly route?: string | null;
}): Promise<string> {
  return request<string>(ADMIN_MENU, { method: 'POST', body });
}

/**
 * Rewrites an entry.
 *
 * Every field is applied, so anything omitted is cleared rather than kept. The screen
 * sends the entry back as it should now read, which is what makes "remove the Arabic
 * label" expressible at all.
 */
export function updateMenuItem(
  id: string,
  body: {
    readonly label: string;
    readonly route?: string | null;
    readonly labelArabic?: string | null;
    readonly icon?: string | null;
    readonly requiredPermission?: string | null;
  },
): Promise<void> {
  return request<void>(`${ADMIN_MENU}/${id}`, { method: 'PUT', body });
}

/** Moves an entry to a new parent, a new position, or both. */
export function moveMenuItem(
  id: string,
  parentId: string | null,
  sortOrder: number,
): Promise<void> {
  return request<void>(`${ADMIN_MENU}/${id}/move`, {
    method: 'POST',
    body: { parentId, sortOrder },
  });
}

/** Shows or hides an entry. */
export function setMenuItemVisibility(id: string, isEnabled: boolean): Promise<void> {
  return request<void>(`${ADMIN_MENU}/${id}/visibility`, {
    method: 'POST',
    body: { isEnabled },
  });
}

/** Deletes an entry an administrator added. */
export function deleteMenuItem(id: string): Promise<void> {
  return request<void>(`${ADMIN_MENU}/${id}`, { method: 'DELETE' });
}
