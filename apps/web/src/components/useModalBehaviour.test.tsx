import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useModalBehaviour } from '@/components/useModalBehaviour';

/*
  Everything here is invisible to a sighted mouse user and load-bearing for everyone
  else. It is also the kind of behaviour that regresses silently, because nothing on
  screen looks different when it stops working.
*/

function Dialog({ onClose }: { readonly onClose: () => void }): React.JSX.Element {
  const panel = useModalBehaviour(onClose);

  return (
    <div
      ref={panel as React.RefObject<HTMLDivElement>}
      tabIndex={-1}
      role="dialog"
      aria-modal="true"
      aria-label="A dialog"
    >
      <button type="button">first</button>
      <button type="button">second</button>
      <button type="button">last</button>
    </div>
  );
}

function Harness(): React.JSX.Element {
  const [open, setOpen] = useState(false);

  return (
    <div>
      <button type="button" onClick={() => setOpen(true)}>
        open it
      </button>
      {open && <Dialog onClose={() => setOpen(false)} />}
    </div>
  );
}

describe('a dialog', () => {
  it('takes focus when it opens', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(screen.getByRole('button', { name: 'open it' }));

    // Not the opener behind the overlay — the first control inside.
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'first' }));
  });

  it('hands focus back to whatever opened it', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    const opener = screen.getByRole('button', { name: 'open it' });
    await user.click(opener);
    await user.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.activeElement).toBe(opener);
  });

  it('closes on Escape', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();

    render(<Dialog onClose={onClose} />);
    await user.keyboard('{Escape}');

    expect(onClose).toHaveBeenCalledOnce();
  });

  it('keeps Tab inside it', async () => {
    const user = userEvent.setup();
    render(<Dialog onClose={vi.fn()} />);

    const last = screen.getByRole('button', { name: 'last' });
    const first = screen.getByRole('button', { name: 'first' });

    last.focus();
    await user.tab();

    // Without the trap this lands on the document body and the user is stranded
    // behind the overlay with no way back.
    expect(document.activeElement).toBe(first);

    await user.tab({ shift: true });
    expect(document.activeElement).toBe(last);
  });

  it('stops the page behind it scrolling, and lets it go again', () => {
    const { unmount } = render(<Dialog onClose={vi.fn()} />);

    expect(document.body.style.overflow).toBe('hidden');

    unmount();
    expect(document.body.style.overflow).toBe('');
  });
});
