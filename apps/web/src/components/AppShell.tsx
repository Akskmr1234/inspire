import { Suspense, useEffect, useRef, useState } from 'react';
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import * as Dialog from '@radix-ui/react-dialog';
import { useQuery } from '@tanstack/react-query';
import clsx from 'clsx';
import i18next from '@/i18n';
import { fetchMenu, labelFor, type Menu, type MenuEntry } from '@/lib/menu';
import { prefetchScreens } from '@/lib/screens';
import { useSettings } from '@/stores/settings';
import { ReportSkeleton } from '@/components/ReportFrame';
import { currentDisplayName, type ApiError } from '@/lib/api';
import { ErrorBoundary } from '@/components/ErrorBoundary';
import { HeadingSlotProvider, useHeadingSlot } from '@/components/PageHeading';
import { CommandPalette, opensPalette } from '@/components/CommandPalette';
import { useSession, type Language, type Theme } from '@/stores/session';
import {
  IconChevron,
  IconClose,
  IconLogout,
  IconMenu,
  IconMoon,
  IconSun,
  iconFor,
} from '@/components/icons';

/** Below this width the sidebar is a drawer rather than a column. */
const DESKTOP = '(min-width: 1024px)';

/** Wide enough for the full sidebar and the full bar beside a list's own columns. */
const WIDE = '(min-width: 1280px)';

/** Where this browser remembers whether the sidebar was folded to its icons. */
const SIDEBAR_KEY = 'erp.sidebar.collapsed';

/**
 * Whether the sidebar starts as a rail of icons.
 *
 * What the user last chose, where they chose; otherwise folded below 1280px. On a
 * 1024px tablet the full sidebar took a quarter of the screen and left the lists
 * three-quarters of a width they were already too wide for, so every grid scrolled
 * sideways and every heading in the bar was cut to a few letters.
 */
function initialCollapsed(): boolean {
  try {
    const stored = localStorage.getItem(SIDEBAR_KEY);

    if (stored !== null) {
      return stored === 'true';
    }
  } catch {
    // Storage switched off: fall back to the width.
  }

  return typeof window.matchMedia === 'function' && !window.matchMedia(WIDE).matches;
}

/**
 * The application chrome: the navigation, a header carrying the theme and language
 * switches, and an outlet for the active screen.
 *
 * The navigation is one component in two modes rather than two components. On a wide
 * screen it is a column that can be narrowed to icons; below `lg` the same markup
 * becomes a drawer over a backdrop, because a 240px column on a 390px phone leaves
 * no room for the ledger it is there to open. Writing it twice would mean every menu
 * change had to be made in both, and one of them would drift.
 *
 * Layout uses logical properties throughout - `ms`/`me`, `start`/`end` - rather than
 * left and right, so switching to Arabic mirrors the whole interface without a second
 * stylesheet.
 */
export function AppShell(): React.JSX.Element {
  const { t } = useTranslation();
  const location = useLocation();

  // Once signed in, fetch the rest of the screens behind the one on show. See
  // `prefetchScreens`.
  useEffect(() => prefetchScreens(), []);

  // And read the firm's settings over this browser's copy, so every counter works to
  // the same setup. See `useSettings.hydrate`.
  useEffect(() => {
    void useSettings.getState().hydrate();
  }, []);
  const [collapsed, setCollapsed] = useState(initialCollapsed);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [paletteOpen, setPaletteOpen] = useState(false);
  // The bar carries the active screen's title. See `PageHeading` for the half of
  // this that lives at the other end.
  const { slot, setElement, occupied } = useHeadingSlot();
  const {
    displayName,
    tenantCode,
    mustChangePassword,
    theme,
    language,
    setTheme,
    setLanguage,
    signOut,
  } = useSession();

  // The menu is data, not code: it is fetched once a session begins and rendered as
  // it comes back. Kept fresh for a working session rather than refetched on every
  // navigation, because it changes when an administrator edits it and not otherwise.
  const menu = useQuery<Menu, ApiError>({
    queryKey: ['menu'],
    queryFn: fetchMenu,
    staleTime: 5 * 60 * 1000,
  });

  // Navigating closes the drawer. Leaving it open over the screen the user has just
  // asked for is the single most common way an off-canvas menu annoys people.
  useEffect(() => {
    setDrawerOpen(false);
  }, [location.pathname]);

  /*
    Ctrl-K, or Cmd-K on a Mac, opens the palette.

    Registered on the window rather than on a focused element, because the whole
    point is that it works wherever you are. `opensPalette` declines while the caret
    is in a field, so it never eats the keystroke from somebody typing.
  */
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (opensPalette(event)) {
        event.preventDefault();
        setPaletteOpen(true);
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  // A drawer that survives a rotation to landscape would sit as a permanent overlay
  // beside the sidebar it duplicates, so crossing into desktop width dismisses it.
  useEffect(() => {
    const query = window.matchMedia(DESKTOP);
    const onChange = (event: MediaQueryListEvent): void => {
      if (event.matches) {
        setDrawerOpen(false);
      }
    };

    query.addEventListener('change', onChange);
    return () => query.removeEventListener('change', onChange);
  }, []);

  // Escape, the scroll lock and the focus handling are Radix's, in the drawer
  // component below — it mounts only while the drawer is open, so its own
  // mount and unmount are the drawer's lifetime.

  const changeLanguage = async (next: Language): Promise<void> => {
    setLanguage(next);
    await i18next.changeLanguage(next);
  };

  const toggleCollapsed = (): void =>
    setCollapsed((value) => {
      try {
        localStorage.setItem(SIDEBAR_KEY, String(!value));
      } catch {
        // Remembered for this visit only.
      }

      return !value;
    });

  // The drawer is never folded: it is opened to be read, and a rail of icons on a
  // phone is a menu nobody can tell the entries of apart.
  const navigation = (folded: boolean): React.JSX.Element => (
    <Navigation
      menu={menu}
      language={language}
      collapsed={folded}
      onNavigate={() => setDrawerOpen(false)}
    />
  );

  return (
    <div className="flex min-h-screen bg-canvas">
      {/*
        Off-screen until focused, then the first thing Tab reaches. Without it a
        keyboard user walks the entire navigation tree — thirty-odd links — before
        arriving at the report they opened, on every single navigation.

        Parked off the top edge with a transform rather than hidden with `sr-only`
        and revealed with `focus:not-sr-only`. That pairing works only while
        Tailwind happens to emit its `position` utility after `not-sr-only`: the two
        rules have identical specificity, so source order decides, and if it ever
        flips the link reverts to `position: static` and shoves the whole layout
        down the moment it takes focus. A transform cannot collide with anything —
        the element is always fixed, always laid out the same, and simply slides
        into view.
      */}
      <a
        href="#main"
        className="fixed start-3 top-3 z-[60] -translate-y-24 rounded-lg bg-brand-600 px-4 py-2 text-sm font-semibold text-white shadow-float transition-transform duration-200 focus:translate-y-0"
      >
        {t('nav.skipToContent')}
      </a>

      {/*
        The desktop column. Hidden rather than unmounted below `lg` so its scroll
        position and any future expanded state survive a trip through a narrow
        viewport.
      */}
      <aside
        className={clsx(
          'no-print sticky top-0 hidden h-screen shrink-0 flex-col border-e border-line lg:flex',
          'glass transition-[width] duration-300 ease-out',
          collapsed ? 'w-[4.5rem]' : 'w-64',
        )}
      >
        <Brand collapsed={collapsed} />
        {navigation(collapsed)}

        <button
          type="button"
          onClick={toggleCollapsed}
          className="flex items-center gap-2 border-t border-line px-4 py-3 text-xs font-medium text-ink-muted transition hover:bg-surface-3 hover:text-ink"
          aria-label={collapsed ? t('nav.expandSidebar') : t('nav.collapseSidebar')}
        >
          <IconChevron
            className={clsx(
              'size-4 shrink-0 transition-transform duration-300',
              // Points the way it will move. Mirrored under Arabic, where "back"
              // is the other direction.
              collapsed ? 'rotate-0 rtl:rotate-180' : 'rotate-180 rtl:rotate-0',
            )}
          />
          {!collapsed && <span>{t('nav.collapseSidebar')}</span>}
        </button>
      </aside>

      {drawerOpen && (
        <NavDrawer onClose={() => setDrawerOpen(false)}>{navigation(false)}</NavDrawer>
      )}

      {paletteOpen && (
        <CommandPalette
          menu={menu.data}
          language={language}
          onClose={() => setPaletteOpen(false)}
        />
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        {/*
          Sticky, so the theme switch and the sign-out stay reachable at the bottom
          of a four-thousand-row stock ledger.
        */}
        <header className="no-print sticky top-0 z-30 flex h-14 items-center gap-2 border-b border-line px-3 glass sm:gap-3 sm:px-4">
          <button
            type="button"
            onClick={() => setDrawerOpen(true)}
            className="btn-icon lg:hidden"
            aria-label={t('nav.openMenu')}
            aria-expanded={drawerOpen}
          >
            <IconMenu />
          </button>

          {/*
            The wordmark rides in the header only where the sidebar is not showing
            it — and only until a screen puts its own name there, because on a 390px
            phone "Aider ERP" and "Cheque register" cannot both have the bar, and
            the second is the one saying where the user is.
          */}
          {!occupied && (
            <span className="truncate text-sm font-semibold text-ink lg:hidden">
              {t('app.name')}
            </span>
          )}

          {/*
            Where the active screen puts its title, subtitle and the one control
            that belongs beside them — portalled in from wherever it renders its
            `PageHeading`, and left empty by anything that does not name itself.

            Nothing of the shell's own goes inside: React places a portal's children
            by appending them, so a node holding both would have the two sets
            fighting over the same positions.
          */}
          <div
            ref={setElement}
            className="flex min-w-0 flex-1 items-center gap-2 overflow-hidden"
          />

          <div className="flex shrink-0 items-center gap-1.5 sm:gap-2">
            {/*
              The palette's own doorway. A shortcut nobody is told about is a
              shortcut nobody uses, and a button is also the only way to reach it
              on a phone, where there is no Ctrl to hold.
            */}
            <button
              type="button"
              onClick={() => setPaletteOpen(true)}
              aria-label={t('palette.open')}
              title={t('palette.open')}
              className="inline-flex h-9 shrink-0 items-center gap-2 rounded-lg border border-line bg-surface-2/80 px-2.5 text-xs text-ink-muted transition duration-150 hover:border-line-strong hover:bg-surface-3 hover:text-ink active:scale-95 xl:w-56 xl:justify-between"
            >
              <span className="flex items-center gap-2">
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth={1.8}
                  strokeLinecap="round"
                  aria-hidden="true"
                  className="size-4 shrink-0 text-ink-subtle"
                >
                  <circle cx="11" cy="11" r="7" />
                  <path d="m20 20-3.6-3.6" />
                </svg>
                <span className="hidden text-xs text-ink-subtle xl:inline">
                  {t('palette.searchModule')}
                </span>
              </span>
              <kbd className="hidden rounded border border-line bg-surface px-1.5 py-0.5 font-sans text-[0.65rem] font-semibold tracking-wide text-ink-muted xl:inline">
                {t('palette.shortcut')}
              </kbd>
            </button>

            {/*
              User profile trigger and menu. Below 1280px it is the avatar alone and
              the menu it opens carries the theme, the language and signing out,
              because the bar cannot hold those as well as the screen's own title
              and button: at 360px they pushed the button under themselves and slid
              the whole page sideways, and on a 768px tablet they cut every title to
              a few letters and sat on top of the screen's own button.
            */}
            <UserProfileDropdown
              displayName={displayName}
              tenantCode={tenantCode}
              theme={theme}
              language={language}
              onThemeChange={setTheme}
              onLanguageChange={(next) => void changeLanguage(next)}
              onSignOut={() => void signOut()}
            />

            <div className="hidden items-center gap-2 xl:flex">
              <ThemeSwitch theme={theme} onChange={setTheme} />

              <Switch<Language>
                value={language}
                options={[
                  ['en', 'EN'],
                  ['ar', 'ع'],
                ]}
                onChange={(next) => void changeLanguage(next)}
                label={t('common.language')}
              />

              {/*
                Flex rather than the grid `.btn-icon` uses: that class centres a single
                glyph in a square, and a second child in the same grid cell column
                stacks under the first instead of sitting beside it.
              */}
              <button
                type="button"
                onClick={() => void signOut()}
                className="btn-icon text-ink-muted hover:bg-surface-3 hover:text-red-600 dark:hover:text-red-400"
                aria-label={t('nav.signOut')}
                title={t('nav.signOut')}
              >
                <IconLogout />
              </button>
            </div>
          </div>
        </header>

        {mustChangePassword && (
          <div className="border-b border-amber-200 bg-amber-50 px-4 py-2 text-sm text-amber-900 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-200">
            {t('common.mustChangePassword')}
          </div>
        )}

        {/*
          `min-w-0` matters more than it looks: without it a wide table inside the
          outlet sets this column's minimum width and pushes the whole page into a
          horizontal scroll, instead of scrolling inside its own container.
        */}
        <main
          id="main"
          tabIndex={-1}
          className="min-w-0 flex-1 p-3 outline-none sm:p-4 lg:p-6"
        >
          {/*
              1920px was too generous: at that width a report's own header spans the
              monitor and its columns drift apart until the figures stop relating to
              their labels. 1600 still holds every table this application has, and
              the wide ones scroll inside their own container anyway.
            */}
          <div className="mx-auto w-full max-w-[100rem]">
            {/*
              Inside the shell, not around it: a screen that throws should leave the
              navigation and the header standing so the user can go somewhere else.
              Keyed on the path so walking away from the broken screen clears it.
            */}
            <ErrorBoundary resetKey={location.pathname}>
              {/*
                Only the screens need the slot, so only they are given it — and a
                screen that throws takes its heading out of the bar with it, rather
                than leaving the bar naming a page that is no longer there.
              */}
              <HeadingSlotProvider value={slot}>
                {/*
                  Its own boundary, inside the shell. The one in `App` sits outside
                  the shell, so a screen whose code was still downloading took the
                  menu and the header down with it and put a blank skeleton where
                  the whole application had been — the delay after a click was the
                  menu vanishing. Here only the content area waits.
                */}
                <Suspense
                  fallback={
                    <div className="page">
                      <ReportSkeleton rows={5} />
                    </div>
                  }
                >
                  <Outlet />
                </Suspense>
              </HeadingSlotProvider>
            </ErrorBoundary>
          </div>
        </main>
      </div>
    </div>
  );
}

/**
 * The signed-in user's menu: who they are, their profile, and — on a phone, where the
 * bar has no room for them — the theme, the language and signing out.
 *
 * The trigger is the avatar alone below `md`. A name truncated to three letters says
 * less than the initials do, and the width it took was the screen's own title's.
 */
function UserProfileDropdown({
  displayName,
  tenantCode,
  theme,
  language,
  onThemeChange,
  onLanguageChange,
  onSignOut,
}: {
  readonly displayName: string | null;
  readonly tenantCode: string | null;
  readonly theme: Theme;
  readonly language: Language;
  readonly onThemeChange: (next: Theme) => void;
  readonly onLanguageChange: (next: Language) => void;
  readonly onSignOut: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  useEffect(() => {
    if (!open) return;
    const handleClickOutside = (event: MouseEvent): void => {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [open]);

  const effectiveName = displayName || currentDisplayName() || t('profile.user');
  const initials = initialsOf(effectiveName);

  const go = (path: string): void => {
    setOpen(false);
    navigate(path);
  };

  const item =
    'flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm font-medium text-ink transition hover:bg-surface-2 hover:text-brand-600 sm:text-xs';

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        onClick={() => setOpen((prev) => !prev)}
        aria-expanded={open}
        aria-haspopup="true"
        aria-label={`${effectiveName} - ${t('profile.title')}`}
        title={effectiveName}
        className={clsx(
          'flex items-center gap-2 rounded-full border border-line/80 bg-surface-2/90 p-0.5 text-xs font-medium transition duration-150 hover:border-line-strong hover:bg-surface-3 active:scale-95 xl:rounded-lg xl:px-2.5 xl:py-1.5',
          open && 'border-brand-500 bg-surface-3 ring-2 ring-brand-500/20 shadow-xs',
        )}
      >
        <span className="relative flex size-8 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-brand-600 to-indigo-700 text-[11px] font-bold text-white shadow-xs xl:size-6 xl:text-[10px]">
          {initials}
          <span className="absolute -bottom-0.5 -end-0.5 size-2 rounded-full border-2 border-surface bg-emerald-500" />
        </span>
        <span className="hidden max-w-44 truncate font-semibold text-ink xl:inline">
          {effectiveName}
        </span>
        <IconChevron
          className={clsx(
            'hidden size-3.5 text-ink-muted transition-transform duration-200 xl:block',
            open && 'rotate-180',
          )}
        />
      </button>

      {open && (
        <div
          role="menu"
          className="absolute end-0 top-full z-50 mt-2 w-72 max-w-[calc(100vw-1.5rem)] rounded-xl border border-line bg-surface p-2 shadow-float animate-pop outline-none"
        >
          <div className="flex items-center gap-3 border-b border-line px-2 pb-3 pt-1.5">
            <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 text-sm font-bold text-white shadow-raised">
              {initials}
            </span>
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-bold text-ink">{effectiveName}</p>
              {tenantCode && (
                <p className="truncate text-xs text-ink-muted">{tenantCode}</p>
              )}
            </div>
          </div>

          <div className="py-1">
            <button type="button" onClick={() => go('/profile')} className={item}>
              <IconPerson className="size-4 text-ink-muted" />
              <span>{t('profile.myProfile')}</span>
            </button>
            <button
              type="button"
              onClick={() => go('/profile?tab=security')}
              className={item}
            >
              <IconPadlock className="size-4 text-ink-muted" />
              <span>{t('changePassword.title')}</span>
            </button>
          </div>

          {/*
            The switches the bar shows beside the avatar from `xl` up. Below that the
            bar is the screen's title and its button, and these are rows here with a
            word each, which a thumb finds more easily than two glyphs anyway.
          */}
          <div className="space-y-2 border-t border-line px-2.5 py-2.5 xl:hidden">
            <div className="flex items-center justify-between gap-3">
              <span className="text-sm font-medium text-ink">{t('common.theme')}</span>
              <Switch<Theme>
                value={theme}
                options={[
                  ['light', t('profile.light')],
                  ['dark', t('profile.dark')],
                ]}
                onChange={onThemeChange}
                label={t('common.theme')}
              />
            </div>
            <div className="flex items-center justify-between gap-3">
              <span className="text-sm font-medium text-ink">{t('common.language')}</span>
              <Switch<Language>
                value={language}
                options={[
                  ['en', 'English'],
                  ['ar', 'العربية'],
                ]}
                onChange={onLanguageChange}
                label={t('common.language')}
              />
            </div>
          </div>

          <div className="border-t border-line pt-1">
            <button
              type="button"
              onClick={() => {
                setOpen(false);
                onSignOut();
              }}
              className="flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm font-medium text-red-600 transition hover:bg-red-50 hover:text-red-700 sm:text-xs dark:hover:bg-red-950/30"
            >
              <IconLogout className="size-4" />
              <span>{t('nav.signOut')}</span>
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

/** Up to two initials for an avatar: the first letters of the first two words. */
function initialsOf(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  const letters = words
    .slice(0, 2)
    .map((word) => word[0]?.toUpperCase() ?? '')
    .join('');
  return letters || name.slice(0, 2).toUpperCase() || '?';
}

function IconPerson(props: { readonly className?: string }): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      aria-hidden="true"
      className={props.className}
    >
      <circle cx="12" cy="8" r="5" />
      <path d="M20 21a8 8 0 0 0-16 0" />
    </svg>
  );
}

function IconPadlock(props: { readonly className?: string }): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      aria-hidden="true"
      className={props.className}
    >
      <rect x="5" y="11" width="14" height="10" rx="2" />
      <path d="M8 11V7a4 4 0 0 1 8 0v4" />
    </svg>
  );
}

/**
 * The navigation as a drawer, with the backdrop that dismisses it.
 *
 * A component of its own rather than markup inline in the shell, because it is
 * mounted only while open — so its own mount and unmount are the drawer opening and
 * closing, which is what `Dialog.Root open` reads.
 *
 * A sheet is a dialog: it takes the screen, the page behind it should not be
 * reachable while it is there, Escape should shut it and focus should come back to
 * whatever opened it. All of that is Radix's now rather than a hundred lines here.
 */
function NavDrawer({
  onClose,
  children,
}: {
  readonly onClose: () => void;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();
  const displayName = useSession((s) => s.displayName);
  const effectiveName = displayName || currentDisplayName() || t('profile.user');
  const drawerInitials = initialsOf(effectiveName);

  return (
    <Dialog.Root open onOpenChange={(next) => !next && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="no-print fixed inset-0 z-50 animate-fade-in bg-slate-950/50 backdrop-blur-[2px] lg:hidden" />

        <Dialog.Content
          aria-describedby={undefined}
          className="no-print fixed inset-y-0 start-0 z-50 flex w-[17rem] max-w-[85vw] animate-sheet-in flex-col border-e border-line bg-surface shadow-float outline-none lg:hidden"
        >
          <Dialog.Title className="sr-only">{t('nav.menu')}</Dialog.Title>

          <div className="flex items-center justify-between gap-2 border-b border-line pe-2">
            <Brand collapsed={false} />
            <Dialog.Close className="btn-icon" aria-label={t('nav.closeMenu')}>
              <IconClose />
            </Dialog.Close>
          </div>
          {children}

          {/* Mobile Profile Footer Link */}
          <div className="mt-auto border-t border-line p-3">
            <NavLink
              to="/profile"
              onClick={onClose}
              className="flex items-center gap-3 rounded-xl border border-line bg-surface-2 p-2.5 transition hover:bg-surface-3"
            >
              <span className="relative flex size-8 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-brand-500 to-brand-700 text-xs font-bold text-white shadow-xs">
                {drawerInitials}
                <span className="absolute -bottom-0.5 -end-0.5 size-2 rounded-full border-2 border-surface bg-emerald-500" />
              </span>
              <div className="min-w-0 flex-1">
                <p className="truncate text-xs font-semibold text-ink">{effectiveName}</p>
                <p className="truncate text-[11px] text-ink-muted">
                  {t('profile.myProfile')}
                </p>
              </div>
            </NavLink>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function Brand({ collapsed }: { readonly collapsed: boolean }): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <div className="flex h-14 shrink-0 items-center gap-2.5 px-4">
      <div className="relative grid size-8 shrink-0 place-items-center rounded-lg bg-gradient-to-br from-brand-500 via-brand-600 to-brand-700 text-xs font-bold text-white shadow-xs ring-1 ring-white/20">
        AE
      </div>
      {!collapsed && (
        <div className="flex min-w-0 items-center gap-1.5">
          <span className="truncate text-sm font-bold tracking-tight text-ink">
            {t('app.name')}
          </span>
          <span className="rounded bg-brand-500/10 px-1 py-0.5 text-[9px] font-bold text-brand-600 uppercase tracking-wider dark:bg-brand-500/20 dark:text-brand-300">
            PRO
          </span>
        </div>
      )}
    </div>
  );
}

function Navigation({
  menu,
  language,
  collapsed,
  onNavigate,
}: {
  readonly menu: ReturnType<typeof useQuery<Menu, ApiError>>;
  readonly language: string;
  readonly collapsed: boolean;
  readonly onNavigate: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <nav className="flex-1 space-y-0.5 overflow-y-auto overscroll-contain p-2">
      {/*
        A skeleton rather than the word "Loading". The menu is the same handful of
        rows every time, so holding its shape means the screen does not jump when it
        lands.
      */}
      {menu.isPending &&
        !collapsed &&
        Array.from({ length: 7 }, (_, index) => (
          <div key={index} className="flex items-center gap-2.5 px-3 py-2">
            <span className="skeleton size-[18px] rounded" />
            <span
              className="skeleton h-3 rounded"
              style={{ width: `${55 + ((index * 13) % 35)}%` }}
            />
          </div>
        ))}

      {menu.isError && !collapsed && (
        <p className="px-3 py-2 text-xs text-red-600 dark:text-red-400">
          {t('nav.menuUnavailable')}
        </p>
      )}

      {menu.data?.items.map((entry) => (
        <SidebarEntry
          key={entry.id}
          entry={entry}
          language={language}
          collapsed={collapsed}
          onNavigate={onNavigate}
        />
      ))}
    </nav>
  );
}

/**
 * One menu entry and everything beneath it.
 *
 * Recursive because the menu is a tree of arbitrary depth - the specification allows
 * an administrator to create their own groups, so two levels is a guess rather than a
 * limit. An entry with a route is a link; one without is a heading that labels the
 * entries under it.
 *
 * The server has already removed anything the user cannot reach, so there is no
 * filtering here. Nothing arrives that should not be shown.
 */
function SidebarEntry({
  entry,
  language,
  collapsed,
  onNavigate,
  depth = 0,
}: {
  readonly entry: MenuEntry;
  readonly language: string;
  readonly collapsed: boolean;
  readonly onNavigate: () => void;
  readonly depth?: number;
}): React.JSX.Element {
  const label = labelFor(entry, language);
  const Icon = iconFor(entry.icon, entry.route);

  // Indented by depth so a nested group reads as nested, on the logical start edge so
  // Arabic indents from the right without a second rule. Withdrawn when collapsed,
  // where there is no room to spend on hierarchy. An empty object rather than
  // undefined: the project compiles with exactOptionalPropertyTypes, which treats
  // "explicitly undefined" and "absent" as different things.
  const indent: React.CSSProperties =
    !collapsed && depth > 1 ? { paddingInlineStart: `${0.75 + depth * 0.5}rem` } : {};

  const children = entry.children.map((child) => (
    <SidebarEntry
      key={child.id}
      entry={child}
      language={language}
      collapsed={collapsed}
      onNavigate={onNavigate}
      depth={depth + 1}
    />
  ));

  if (!entry.route) {
    return (
      <div className="space-y-0.5">
        {/*
          A heading is hidden rather than abbreviated when the sidebar is collapsed:
          one letter of "Accounts reports" tells a reader nothing, while the icons of
          the links beneath it still do. A hairline stands in for it instead, so the
          groups stay visibly separate.
        */}
        {collapsed ? (
          <hr className="mx-3 my-2 border-line" />
        ) : (
          <p className={depth > 0 ? 'nav-subheading' : 'nav-heading'}>{label}</p>
        )}
        {children}
      </div>
    );
  }

  return (
    <div className="space-y-0.5">
      <NavLink
        to={entry.route}
        onClick={onNavigate}
        title={collapsed ? label : undefined}
        style={indent}
        className={({ isActive }) =>
          clsx(
            'nav-link',
            isActive && 'nav-link-active',
            collapsed && 'justify-center px-0',
          )
        }
      >
        <Icon />
        {!collapsed && <span className="truncate">{label}</span>}
      </NavLink>
      {children}
    </div>
  );
}

/**
 * The theme switch.
 *
 * A single button that toggles rather than a pair that selects. There are two themes,
 * so a two-option segmented control spends twice the width to say the same thing, and
 * the glyph showing what you will get if you press it is the clearer affordance.
 */
function ThemeSwitch({
  theme,
  onChange,
}: {
  readonly theme: Theme;
  readonly onChange: (next: Theme) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const next = theme === 'dark' ? 'light' : 'dark';

  return (
    <button
      type="button"
      onClick={() => onChange(next)}
      className="btn-icon relative overflow-hidden"
      aria-label={t('common.theme')}
      title={t('common.theme')}
    >
      {/*
        Both glyphs are rendered and one is rotated out, so the change is a movement
        the eye can follow rather than a swap it can only notice afterwards.
      */}
      <IconSun
        className={clsx(
          'absolute size-[18px] transition-all duration-300',
          theme === 'dark'
            ? 'rotate-90 scale-0 opacity-0'
            : 'rotate-0 scale-100 opacity-100',
        )}
      />
      <IconMoon
        className={clsx(
          'absolute size-[18px] transition-all duration-300',
          theme === 'dark'
            ? 'rotate-0 scale-100 opacity-100'
            : '-rotate-90 scale-0 opacity-0',
        )}
      />
    </button>
  );
}

/**
 * A segmented control.
 *
 * The selected segment is marked by a panel that slides between positions rather than
 * by recolouring each button, so the selection reads as one thing moving.
 */
function Switch<T extends string>({
  value,
  options,
  onChange,
  label,
}: {
  readonly value: T;
  readonly options: readonly (readonly [T, string])[];
  readonly onChange: (next: T) => void;
  readonly label: string;
}): React.JSX.Element {
  const index = Math.max(
    options.findIndex(([option]) => option === value),
    0,
  );

  return (
    <div
      role="group"
      aria-label={label}
      className="relative flex rounded-lg border border-line bg-surface-2 p-0.5"
    >
      <span
        aria-hidden="true"
        className="absolute inset-y-0.5 rounded-md bg-brand-600 shadow-xs transition-[inset-inline-start] duration-300 ease-out"
        style={{
          width: `calc((100% - 0.25rem) / ${options.length})`,
          // Offset with a logical property rather than a translate. `translateX` is
          // a physical axis and would send the marker off the wrong end under
          // Arabic; `inset-inline-start` already means "from the reading edge", so
          // the same expression is correct in both directions.
          insetInlineStart: `calc(0.125rem + ${index} * ((100% - 0.25rem) / ${options.length}))`,
        }}
      />

      {options.map(([option, glyph]) => (
        <button
          key={option}
          type="button"
          onClick={() => onChange(option)}
          aria-pressed={value === option}
          className={clsx(
            'relative z-10 min-w-8 rounded-md px-2 py-1 text-xs font-semibold transition-colors duration-200',
            value === option ? 'text-white' : 'text-ink-muted hover:text-ink',
          )}
        >
          {glyph}
        </button>
      ))}
    </div>
  );
}
