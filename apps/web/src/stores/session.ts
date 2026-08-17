import { create } from 'zustand';
import * as api from '@/lib/api';

export type Theme = 'light' | 'dark';
export type Language = 'en' | 'ar';

interface SessionState {
  readonly status: 'unknown' | 'signedOut' | 'signedIn';
  readonly displayName: string | null;
  readonly mustChangePassword: boolean;
  readonly permissions: ReadonlySet<string>;
  readonly theme: Theme;
  readonly language: Language;

  restore: () => Promise<void>;
  signIn: (userName: string, password: string, tenantCode: string) => Promise<void>;
  signOut: () => Promise<void>;
  setTheme: (theme: Theme) => void;
  setLanguage: (language: Language) => void;
  /**
   * Whether the user holds a permission, for hiding actions they cannot perform.
   *
   * A convenience, never a security boundary: every endpoint checks for itself.
   * Hiding a button the server would refuse is courtesy; relying on the hiding
   * to enforce the rule would mean anyone with the developer tools could bypass
   * it.
   */
  can: (permissionCode: string) => boolean;
}

/**
 * What the API returns for a role holding every permission.
 *
 * A super administrator's permission list is `["*"]`, not all several hundred
 * codes. Any check that only tested set membership would therefore report false
 * for every specific permission and hide the entire interface from the most
 * privileged user in the system.
 */
const WILDCARD_PERMISSION = '*';

const THEME_KEY = 'erp.theme';
const LANGUAGE_KEY = 'erp.language';

function readTheme(): Theme {
  const stored = localStorage.getItem(THEME_KEY);

  if (stored === 'light' || stored === 'dark') {
    return stored;
  }

  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function readLanguage(): Language {
  return localStorage.getItem(LANGUAGE_KEY) === 'ar' ? 'ar' : 'en';
}

/** Applies the theme and the text direction to the document element. */
export function applyPresentation(theme: Theme, language: Language): void {
  const root = document.documentElement;

  // The class, not a media query: the stylesheet declares a `dark` custom variant
  // bound to this class, so every `dark:` utility in the application keys off what
  // is toggled here rather than off the operating system's setting.
  root.classList.toggle('dark', theme === 'dark');
  root.lang = language;

  // Arabic is written right to left. Setting dir on <html> is what makes the
  // whole layout mirror, which is why the styles use logical properties
  // (start/end) rather than left/right throughout.
  root.dir = language === 'ar' ? 'rtl' : 'ltr';
}

/** How long the cross-fade in the stylesheet runs for. */
const THEME_FADE_MS = 300;

let fadeTimer: number | undefined;

/**
 * Cross-fades the whole document across a theme change.
 *
 * The transition lives on a class that is added for the length of the change and
 * then taken off again, rather than sitting permanently on every element. Left in
 * place it would also animate the ordinary hover and focus colours — a row that
 * takes a quarter of a second to acknowledge the pointer feels broken — and it
 * would put a transition on every cell of a four-thousand-row ledger for the
 * benefit of a switch pressed twice a day.
 */
function withThemeTransition(apply: () => void): void {
  const root = document.documentElement;

  // Honoured here as well as in the stylesheet: this path adds a class rather than
  // declaring a transition, so the media query in the CSS never sees it.
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    apply();
    return;
  }

  root.classList.add('theme-switching');
  apply();

  window.clearTimeout(fadeTimer);
  fadeTimer = window.setTimeout(
    () => root.classList.remove('theme-switching'),
    THEME_FADE_MS,
  );
}

export const useSession = create<SessionState>((set, get) => ({
  status: 'unknown',
  displayName: null,
  mustChangePassword: false,
  permissions: new Set<string>(),
  theme: readTheme(),
  language: readLanguage(),

  restore: async () => {
    const restored = await api.restoreSession();

    if (!restored) {
      set({ status: 'signedOut' });
      return;
    }

    const permissions = await api.fetchPermissions().catch(() => []);

    set({
      status: 'signedIn',
      permissions: new Set(permissions),
    });
  },

  signIn: async (userName, password, tenantCode) => {
    const auth = await api.login(userName, password, tenantCode);
    const permissions = await api.fetchPermissions().catch(() => []);

    set({
      status: 'signedIn',
      displayName: auth.displayName,
      mustChangePassword: auth.mustChangePassword,
      permissions: new Set(permissions),
    });
  },

  signOut: async () => {
    await api.logout();

    set({
      status: 'signedOut',
      displayName: null,
      mustChangePassword: false,
      permissions: new Set<string>(),
    });
  },

  setTheme: (theme) => {
    localStorage.setItem(THEME_KEY, theme);
    withThemeTransition(() => applyPresentation(theme, get().language));
    set({ theme });
  },

  setLanguage: (language) => {
    localStorage.setItem(LANGUAGE_KEY, language);
    applyPresentation(get().theme, language);
    set({ language });
  },

  can: (permissionCode) => {
    const held = get().permissions;
    return held.has(WILDCARD_PERMISSION) || held.has(permissionCode);
  },
}));
