/*
  The theme a deployment opens in.

  VITE_DEFAULT_THEME sets it when the application is built: `light`, `dark`, or
  `system` to follow the device's own setting. It is the theme of everybody nobody has
  chosen one for - neither the firm, in Settings, nor the person themselves, with the
  switch in the top bar. Unset or misspelt, it is light, as it was before there was a
  setting.

  index.html makes the same decision before the first paint, from the same variable;
  the two are kept in step by hand.
*/

/** What the variable may say. */
export type ThemeDefault = 'light' | 'dark' | 'system';

/** Reads the variable, forgiving case and stray spaces. */
export function themeDefaultFrom(raw: string | undefined): ThemeDefault {
  const value = raw?.trim().toLowerCase();

  return value === 'dark' || value === 'system' ? value : 'light';
}

/** The theme this build opens in. */
export const deploymentTheme: ThemeDefault = themeDefaultFrom(
  import.meta.env.VITE_DEFAULT_THEME,
);

/** The theme a default comes to on a device that does, or does not, prefer dark. */
export function resolveThemeDefault(
  choice: ThemeDefault,
  prefersDark: boolean,
): 'light' | 'dark' {
  if (choice === 'system') {
    return prefersDark ? 'dark' : 'light';
  }

  return choice;
}
