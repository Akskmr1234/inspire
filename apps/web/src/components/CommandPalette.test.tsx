import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import '@/i18n';
import {
  CommandPalette,
  destinationsOf,
  opensPalette,
} from '@/components/CommandPalette';
import type { Menu } from '@/lib/menu';

/*
  The palette is navigation, so what matters is that it finds the screen somebody
  half-remembers the name of, and that it never offers one the server did not put in
  the menu — the menu is already filtered by permission, and a palette that reached
  round that would be a way to walk into a refusal.
*/

const entry = (
  id: string,
  label: string,
  route: string | null,
  children: Menu['items'] = [],
): Menu['items'][number] => ({
  id,
  code: id,
  label,
  labelArabic: null,
  icon: null,
  route,
  module: 'test',
  children,
});

const menu: Menu = {
  items: [
    entry('m1', 'Dashboard', '/dashboard'),
    entry('m2', 'Transactions', null, [
      entry('m2a', 'Purchases', '/purchase/invoices'),
      entry('m2b', 'Purchase returns', '/purchase/returns'),
      entry('m2c', 'Sales invoices', '/sales/invoices'),
    ]),
    entry('m3', 'Accounts reports', null, [
      entry('m3a', 'Cash book', '/accounting/cash-book'),
      entry('m3b', 'Trial balance', '/accounting/trial-balance'),
    ]),
  ],
};

function palette(onClose = vi.fn()): React.JSX.Element {
  return (
    <MemoryRouter>
      <CommandPalette menu={menu} language="en" onClose={onClose} />
    </MemoryRouter>
  );
}

describe('flattening the menu', () => {
  it('offers the screens and not the headings', () => {
    const found = destinationsOf(menu, 'en');

    expect(found.map((d) => d.label)).toEqual([
      'Dashboard',
      'Purchases',
      'Purchase returns',
      'Sales invoices',
      'Cash book',
      'Trial balance',
    ]);
  });

  it('carries the heading down, so a screen can be found by where it lives', () => {
    const found = destinationsOf(menu, 'en');

    expect(found.find((d) => d.label === 'Cash book')?.path).toBe('Accounts reports');
    expect(found.find((d) => d.label === 'Dashboard')?.path).toBe('');
  });

  it('has nothing to offer before the menu arrives', () => {
    expect(destinationsOf(undefined, 'en')).toEqual([]);
  });
});

describe('the palette', () => {
  it('finds a screen from part of each word, in any order', async () => {
    const user = userEvent.setup();
    render(palette());

    await user.keyboard('ret pur');

    const shown = screen.getAllByRole('option').map((node) => node.textContent);
    expect(shown).toHaveLength(1);
    expect(shown[0]).toContain('Purchase returns');
  });

  it('finds a screen by the heading it lives under', async () => {
    const user = userEvent.setup();
    render(palette());

    await user.keyboard('reports cash');

    const shown = screen.getAllByRole('option').map((node) => node.textContent);
    expect(shown).toHaveLength(1);
    expect(shown[0]).toContain('Cash book');
  });

  it('opens the highlighted screen on Enter and shuts itself', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(palette(onClose));

    await user.keyboard('trial{Enter}');

    expect(onClose).toHaveBeenCalled();
  });

  it('says so rather than showing an empty list', async () => {
    const user = userEvent.setup();
    render(palette());

    await user.keyboard('payroll');

    expect(screen.queryAllByRole('option')).toHaveLength(0);
    expect(screen.getByText(/no screen matches/i)).toBeTruthy();
  });
});

describe('the shortcut', () => {
  const press = (init: KeyboardEventInit & { target?: EventTarget }): KeyboardEvent => {
    const event = new KeyboardEvent('keydown', { key: 'k', ...init });

    if (init.target) {
      Object.defineProperty(event, 'target', { value: init.target });
    }

    return event;
  };

  it('answers to Ctrl-K and Cmd-K', () => {
    expect(opensPalette(press({ ctrlKey: true }))).toBe(true);
    expect(opensPalette(press({ metaKey: true }))).toBe(true);
  });

  it('ignores the letter on its own', () => {
    expect(opensPalette(press({}))).toBe(false);
  });

  it('keeps out of the way while somebody is typing', () => {
    // Ctrl-K already means something inside a text box to some people, and taking
    // it mid-sentence is worse than not offering the shortcut there.
    expect(
      opensPalette(press({ ctrlKey: true, target: document.createElement('input') })),
    ).toBe(false);
    expect(
      opensPalette(press({ ctrlKey: true, target: document.createElement('textarea') })),
    ).toBe(false);
  });
});
