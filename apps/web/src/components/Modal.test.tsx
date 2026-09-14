import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '@/i18n';
import { Modal } from '@/components/Modal';

/*
  The focus handling is Radix's now rather than a hundred lines of our own, and these
  are here so that stays true. Every one of them is behaviour a keyboard user cannot
  work without and a mouse user never notices, which is exactly the kind that gets
  quietly switched off — a stray `modal={false}`, a portal moved, a `Dialog.Title`
  dropped because it looked redundant beside the heading.
*/

function open(onClose = vi.fn()): { readonly onClose: ReturnType<typeof vi.fn> } {
  render(
    <Modal title="New customer" onClose={onClose}>
      <input aria-label="Name" />
      <button type="button">Add</button>
    </Modal>,
  );

  return { onClose };
}

describe('a dialog', () => {
  it('names itself from its own title, rather than repeating it in a label', () => {
    open();

    const dialog = screen.getByRole('dialog');
    const labelledBy = dialog.getAttribute('aria-labelledby');

    expect(labelledBy).toBeTruthy();
    expect(document.getElementById(labelledBy ?? '')?.textContent).toBe('New customer');
  });

  it('takes the page behind it out of the accessibility tree', () => {
    open();

    const dialog = screen.getByRole('dialog');
    const behind = [...document.body.children].filter(
      (element) => !element.contains(dialog),
    );

    expect(behind.length).toBeGreaterThan(0);
    expect(
      behind.every((element) => element.getAttribute('aria-hidden') === 'true'),
    ).toBe(true);
  });

  it('moves focus into itself, so the first Tab is inside and not behind', async () => {
    open();

    await waitFor(() => {
      expect(screen.getByRole('dialog').contains(document.activeElement)).toBe(true);
    });
  });

  it('closes on Escape', async () => {
    const user = userEvent.setup();
    const { onClose } = open();

    await user.keyboard('{Escape}');

    expect(onClose).toHaveBeenCalled();
  });

  it('closes from the button that says it does', async () => {
    const user = userEvent.setup();
    const { onClose } = open();

    await user.click(screen.getByRole('button', { name: 'Close' }));

    expect(onClose).toHaveBeenCalled();
  });

  it('cycles Tab within itself rather than walking into the page behind', async () => {
    const user = userEvent.setup();
    open();

    const dialog = screen.getByRole('dialog');

    for (let press = 0; press < 6; press += 1) {
      await user.tab();
      expect(dialog.contains(document.activeElement)).toBe(true);
    }
  });
});
