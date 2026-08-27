import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import {
  HeadingSlotProvider,
  PageHeading,
  useHeadingSlot,
} from '@/components/PageHeading';

/*
  The heading moved out of the page and into the application bar to give the list
  underneath it the whole content area. Two things have to hold for that to be an
  improvement rather than a loss: the name has to arrive in the bar, and it has to
  keep reaching paper — the bar does not print.
*/

/** A stand-in for the shell: a bar that offers the slot, and a body below it. */
function Shell({ children }: { readonly children: React.ReactNode }): React.JSX.Element {
  const { slot, setElement, occupied } = useHeadingSlot();

  return (
    <HeadingSlotProvider value={slot}>
      <header data-testid="bar">
        {!occupied && <span>Inspire ERP</span>}
        <div ref={setElement} data-testid="slot" />
      </header>
      <main data-testid="body">{children}</main>
    </HeadingSlotProvider>
  );
}

describe('inside the shell', () => {
  it('puts the screen’s name in the bar rather than at the top of the page', () => {
    render(
      <Shell>
        <PageHeading title="Trial balance" subtitle="As at 31 Mar" />
      </Shell>,
    );

    const slot = screen.getByTestId('slot');

    expect(slot.textContent).toContain('Trial balance');
    expect(slot.textContent).toContain('As at 31 Mar');

    // The whole point: nothing of the heading is left occupying the content area.
    // The print copy below is the one exception, and every part of it is hidden.
    const inBody = [...screen.getByTestId('body').querySelectorAll('h1')];

    expect(inBody.every((node) => node.closest('[hidden]') !== null)).toBe(true);
  });

  it('carries the controls that belong beside the title up with it', () => {
    render(
      <Shell>
        <PageHeading title="Day book" actions={<button type="button">Print</button>} />
      </Shell>,
    );

    expect(
      screen.getByTestId('slot').contains(screen.getByRole('button', { name: 'Print' })),
    ).toBe(true);
  });

  it('leaves a copy in the page for paper, since the bar does not print', () => {
    render(
      <Shell>
        <PageHeading title="Cheque register" subtitle="1 Jan – 31 Mar" />
      </Shell>,
    );

    // Hidden with the attribute and not merely with `display: none`: `space-y`
    // skips `[hidden]`, so this is also what stops the invisible copy from
    // pushing the list down by the gap the heading used to occupy.
    const copy = screen.getByTestId('body').querySelector('header.print-only');

    expect(copy?.hasAttribute('hidden')).toBe(true);
    expect(copy?.textContent).toContain('Cheque register');
    expect(copy?.textContent).toContain('1 Jan – 31 Mar');
  });

  it('drops the wordmark once a screen has claimed the bar', () => {
    const { rerender } = render(<Shell>{null}</Shell>);

    // Nothing is naming itself yet, so the bar still says which application it is.
    expect(screen.queryByText('Inspire ERP')).not.toBeNull();

    rerender(
      <Shell>
        <PageHeading title="Stock ledger" />
      </Shell>,
    );

    expect(screen.queryByText('Inspire ERP')).toBeNull();
  });
});

describe('outside the shell', () => {
  it('renders the heading in place, so a screen on its own still names itself', () => {
    const { container } = render(<PageHeading title="Products" subtitle="42 items" />);

    const header = container.querySelector('header');

    expect(header?.className).toContain('page-header');
    expect(header?.hasAttribute('hidden')).toBe(false);
    expect(screen.getByRole('heading', { name: 'Products' })).toBeTruthy();
    expect(screen.getByText('42 items')).toBeTruthy();
  });
});
