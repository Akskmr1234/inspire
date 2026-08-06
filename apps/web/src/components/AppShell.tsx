import { useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import clsx from 'clsx';
import i18next from '@/i18n';
import { fetchMenu, labelFor, type Menu, type MenuEntry } from '@/lib/menu';
import type { ApiError } from '@/lib/api';
import { useSession, type Language, type Theme } from '@/stores/session';

/**
 * The application chrome: a collapsible sidebar, a header carrying the theme and
 * language switches, and an outlet for the active screen.
 *
 * Layout uses logical properties throughout - `ms`/`me`, `start`/`end` - rather
 * than left and right, so switching to Arabic mirrors the whole interface without
 * a second stylesheet.
 */
export function AppShell(): React.JSX.Element {
  const { t } = useTranslation();
  const [collapsed, setCollapsed] = useState(false);
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

  const changeLanguage = async (next: Language): Promise<void> => {
    setLanguage(next);
    await i18next.changeLanguage(next);
  };

  return (
    <div className="flex min-h-screen">
      <aside
        className={clsx(
          'flex flex-col border-e border-slate-200 bg-white transition-all',
          'dark:border-slate-800 dark:bg-slate-900',
          collapsed ? 'w-16' : 'w-60',
        )}
      >
        <div className="flex h-14 items-center gap-2 border-b border-slate-200 px-4 dark:border-slate-800">
          <div className="grid size-8 shrink-0 place-items-center rounded-lg bg-brand-600 text-sm font-bold text-white">
            IE
          </div>
          {!collapsed && (
            <span className="truncate text-sm font-semibold">{t('app.name')}</span>
          )}
        </div>

        <nav className="flex-1 space-y-1 overflow-y-auto p-2">
          {menu.isPending && !collapsed && (
            <p className="px-3 py-2 text-xs text-slate-400">{t('common.loading')}</p>
          )}

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
            />
          ))}
        </nav>

        <button
          type="button"
          onClick={() => setCollapsed((value) => !value)}
          className="border-t border-slate-200 p-3 text-xs text-slate-500 hover:bg-slate-50 dark:border-slate-800 dark:hover:bg-slate-800"
          aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
        >
          {collapsed ? '»' : '«'}
        </button>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 items-center justify-between gap-4 border-b border-slate-200 bg-white px-4 dark:border-slate-800 dark:bg-slate-900">
          <span className="truncate text-sm text-slate-500 dark:text-slate-400">
            {displayName}
          </span>

          <div className="flex items-center gap-2">
            <Switch<Theme>
              value={theme}
              options={[
                ['light', '☀'],
                ['dark', '☾'],
              ]}
              onChange={setTheme}
              label={t('common.theme')}
            />
            <Switch<Language>
              value={language}
              options={[
                ['en', 'EN'],
                ['ar', 'ع'],
              ]}
              onChange={(next) => void changeLanguage(next)}
              label={t('common.language')}
            />
            <button
              type="button"
              onClick={() => void signOut()}
              className="rounded-lg px-3 py-1.5 text-sm text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800"
            >
              {t('nav.signOut')}
            </button>
          </div>
        </header>

        {mustChangePassword && (
          <div className="border-b border-amber-300 bg-amber-50 px-4 py-2 text-sm text-amber-900 dark:border-amber-700 dark:bg-amber-950 dark:text-amber-200">
            {t('common.mustChangePassword')}
          </div>
        )}

        <main className="min-w-0 flex-1 overflow-auto p-4">
          <Outlet />
        </main>
      </div>
    </div>
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
  depth = 0,
}: {
  readonly entry: MenuEntry;
  readonly language: string;
  readonly collapsed: boolean;
  readonly depth?: number;
}): React.JSX.Element {
  const label = labelFor(entry, language);

  // Indented by depth so a nested group reads as nested, on the logical start edge so
  // Arabic indents from the right without a second rule. An empty object rather than
  // undefined: the project compiles with exactOptionalPropertyTypes, which treats
  // "explicitly undefined" and "absent" as different things.
  const indent: React.CSSProperties =
    !collapsed && depth > 1 ? { paddingInlineStart: `${depth * 0.75}rem` } : {};

  const children = entry.children.map((child) => (
    <SidebarEntry
      key={child.id}
      entry={child}
      language={language}
      collapsed={collapsed}
      depth={depth + 1}
    />
  ));

  if (!entry.route) {
    return (
      <div className="space-y-1">
        {/*
          A heading is hidden rather than abbreviated when the sidebar is collapsed:
          one letter of "Accounts reports" tells a reader nothing, while the icons of
          the links beneath it still do.
        */}
        {!collapsed && (
          <p className="px-3 pt-3 pb-1 text-xs font-semibold tracking-wide text-slate-400 uppercase">
            {label}
          </p>
        )}
        {children}
      </div>
    );
  }

  return (
    <div className="space-y-1">
      <NavLink
        to={entry.route}
        title={collapsed ? label : undefined}
        style={indent}
        className={({ isActive }) =>
          clsx(
            'block truncate rounded-lg px-3 py-2 text-sm transition',
            isActive
              ? 'bg-brand-50 font-medium text-brand-700 dark:bg-brand-900/40 dark:text-brand-100'
              : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800',
          )
        }
      >
        {collapsed ? label.slice(0, 1) : label}
      </NavLink>
      {children}
    </div>
  );
}

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
  return (
    <div
      role="group"
      aria-label={label}
      className="flex overflow-hidden rounded-lg border border-slate-300 dark:border-slate-700"
    >
      {options.map(([option, glyph]) => (
        <button
          key={option}
          type="button"
          onClick={() => onChange(option)}
          aria-pressed={value === option}
          className={clsx(
            'px-2.5 py-1 text-xs transition',
            value === option
              ? 'bg-brand-600 text-white'
              : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800',
          )}
        >
          {glyph}
        </button>
      ))}
    </div>
  );
}
