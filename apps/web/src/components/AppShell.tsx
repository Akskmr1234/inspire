import { useEffect, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import clsx from 'clsx';
import i18next from '@/i18n';
import { fetchMenu, labelFor, type Menu, type MenuEntry } from '@/lib/menu';
import type { ApiError } from '@/lib/api';
import { ErrorBoundary } from '@/components/ErrorBoundary';
import { useModalBehaviour } from '@/components/useModalBehaviour';
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
  const [collapsed, setCollapsed] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const {
    displayName,
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

  // Escape, the scroll lock and the focus handling all live in `useModalBehaviour`,
  // which the drawer component below calls — it mounts only while the drawer is
  // open, so the hook's own mount/unmount is the drawer's lifetime.

  const changeLanguage = async (next: Language): Promise<void> => {
    setLanguage(next);
    await i18next.changeLanguage(next);
  };

  const nav = (
    <Navigation
      menu={menu}
      language={language}
      collapsed={collapsed}
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
        {nav}

        <button
          type="button"
          onClick={() => setCollapsed((value) => !value)}
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

      {drawerOpen && <NavDrawer onClose={() => setDrawerOpen(false)}>{nav}</NavDrawer>}

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

          {/* The wordmark rides in the header only where the sidebar is not showing it. */}
          <span className="truncate text-sm font-semibold text-ink lg:hidden">
            {t('app.name')}
          </span>

          <span className="ms-auto hidden truncate text-sm text-ink-muted sm:block">
            {displayName}
          </span>

          <div className="ms-auto flex items-center gap-1.5 sm:ms-0 sm:gap-2">
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
              className="inline-flex h-9 shrink-0 items-center justify-center gap-2 rounded-lg px-2 text-ink-muted transition duration-150 hover:bg-surface-3 hover:text-ink active:scale-95 sm:px-3"
              aria-label={t('nav.signOut')}
              title={t('nav.signOut')}
            >
              <IconLogout />
              <span className="hidden text-sm font-medium sm:inline">
                {t('nav.signOut')}
              </span>
            </button>
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
              <Outlet />
            </ErrorBoundary>
          </div>
        </main>
      </div>
    </div>
  );
}

/**
 * The navigation as a drawer, with the backdrop that dismisses it.
 *
 * A component of its own rather than markup inline in the shell, because it is
 * mounted only while open — which is what lets `useModalBehaviour` treat its own
 * mount and unmount as the drawer opening and closing, and so move focus in and
 * hand it back afterwards.
 */
function NavDrawer({
  onClose,
  children,
}: {
  readonly onClose: () => void;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  const { t } = useTranslation();
  const panel = useModalBehaviour(onClose);

  return (
    <div className="no-print fixed inset-0 z-50 lg:hidden">
      <button
        type="button"
        aria-label={t('nav.closeMenu')}
        onClick={onClose}
        className="absolute inset-0 animate-fade-in bg-slate-950/50 backdrop-blur-[2px]"
      />

      <aside
        ref={panel as React.RefObject<HTMLElement>}
        tabIndex={-1}
        className="absolute inset-y-0 start-0 flex w-[17rem] max-w-[85vw] animate-sheet-in flex-col border-e border-line bg-surface shadow-float outline-none"
        role="dialog"
        aria-modal="true"
        aria-label={t('nav.menu')}
      >
        <div className="flex items-center justify-between gap-2 border-b border-line pe-2">
          <Brand collapsed={false} />
          <button
            type="button"
            onClick={onClose}
            className="btn-icon"
            aria-label={t('nav.closeMenu')}
          >
            <IconClose />
          </button>
        </div>
        {children}
      </aside>
    </div>
  );
}

function Brand({ collapsed }: { readonly collapsed: boolean }): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <div className="flex h-14 shrink-0 items-center gap-2.5 px-4">
      <div className="grid size-8 shrink-0 place-items-center rounded-lg bg-gradient-to-br from-brand-500 to-brand-700 text-sm font-bold text-white shadow-xs">
        IE
      </div>
      {!collapsed && (
        <span className="truncate text-sm font-semibold tracking-tight text-ink">
          {t('app.name')}
        </span>
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
          <p className="nav-heading">{label}</p>
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
