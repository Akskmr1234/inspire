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
