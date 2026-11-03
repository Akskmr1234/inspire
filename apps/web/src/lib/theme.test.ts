import { describe, expect, it } from 'vitest';
import { resolveThemeDefault, themeDefaultFrom } from '@/lib/theme';

describe("the deployment's theme", () => {
  it('reads light, dark and system, whatever the case', () => {
    expect(themeDefaultFrom('dark')).toBe('dark');
    expect(themeDefaultFrom(' Dark ')).toBe('dark');
    expect(themeDefaultFrom('LIGHT')).toBe('light');
    expect(themeDefaultFrom('system')).toBe('system');
  });

  it('is light where the variable is unset or says anything else', () => {
    expect(themeDefaultFrom(undefined)).toBe('light');
    expect(themeDefaultFrom('')).toBe('light');
    expect(themeDefaultFrom('midnight')).toBe('light');
  });

  it("follows the device's preference only when told to", () => {
    expect(resolveThemeDefault('system', true)).toBe('dark');
    expect(resolveThemeDefault('system', false)).toBe('light');
    expect(resolveThemeDefault('light', true)).toBe('light');
    expect(resolveThemeDefault('dark', false)).toBe('dark');
  });
});
