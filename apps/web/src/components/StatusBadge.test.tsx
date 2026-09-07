import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import '@/i18n';
import { ActiveBadge, StatusBadge } from '@/components/StatusBadge';

/*
  The badge is the one place a state is drawn, so what is worth holding is that a
  state of a given weight always looks the same — a cancelled invoice and a withdrawn
  customer are struck through by the same rule — and that the colour is never the only
  thing carrying it, because roughly one reader in twelve cannot use the colour.
*/

describe('the status badge', () => {
  it('draws the word, not only the colour', () => {
    render(<StatusBadge tone="success" label="Posted" />);

    expect(screen.getByText('Posted')).toBeTruthy();
  });

  it('carries a dot of the tone, so the state survives a colour-blind reader', () => {
    const { container } = render(<StatusBadge tone="danger" label="Cancelled" />);

    const dot = container.querySelector('[aria-hidden="true"]');
    expect(dot?.className).toContain('bg-red-500');
  });

  it('takes its pill from the tone rather than from a colour the caller picked', () => {
    const { container } = render(<StatusBadge tone="warn" label="Draft" />);

    expect(container.firstElementChild?.className).toContain('badge-warn');
  });

  it('strikes through only what has been voided', () => {
    const { container: plain } = render(<StatusBadge tone="info" label="Confirmed" />);
    expect(plain.firstElementChild?.className).not.toContain('line-through');

    const { container: struck } = render(
      <StatusBadge tone="danger" label="Cancelled" struck />,
    );
    expect(struck.firstElementChild?.className).toContain('line-through');
  });
});

describe('a master record', () => {
  it('reads active in the colour of a good state', () => {
    const { container } = render(<ActiveBadge isActive />);

    expect(screen.getByText('Active')).toBeTruthy();
    expect(container.firstElementChild?.className).toContain('badge-success');
  });

  it('reads withdrawn, struck through, as a cancelled document does', () => {
    const { container } = render(<ActiveBadge isActive={false} />);

    expect(screen.getByText('Withdrawn')).toBeTruthy();
    expect(container.firstElementChild?.className).toContain('line-through');
  });
});
