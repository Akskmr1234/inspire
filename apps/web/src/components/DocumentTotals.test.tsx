import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import '@/i18n';
import { DocumentTotals } from '@/components/DocumentLines';

/*
  The arithmetic a bill is read for. What matters is that the ladder adds up — that
  net is gross less the discount and the total is everything after it — and that a
  rung with nothing on it is left out rather than printed as a zero, because a bill
  listing a discount of nothing invites the question of what discount.
*/

/** The figure beside a word, as the reader sees it. */
function figure(label: string): string {
  const term = screen.getByText(label);
  return term.nextElementSibling?.textContent ?? '';
}

describe('the totals ladder', () => {
  it('takes the discount off the gross to reach the net', () => {
    render(<DocumentTotals gross={1000} discount={150} tax={42.5} charges={0} />);

    expect(figure('Gross')).toBe('1,000.00');
    expect(figure('Discount')).toBe('-150.00');
    expect(figure('Net')).toBe('850.00');
  });

  it('adds tax and charges to the net to reach what is owed', () => {
    render(<DocumentTotals gross={1000} discount={150} tax={42.5} charges={50} />);

    expect(figure('Total')).toBe('942.50');
  });

  it('carries the rounding the server applied into the total', () => {
    render(
      <DocumentTotals gross={100} discount={0} tax={5} charges={0} rounding={-0.25} />,
    );

    expect(figure('Round off')).toBe('-0.25');
    expect(figure('Total')).toBe('104.75');
  });

  it('leaves out a rung with nothing on it', () => {
    render(<DocumentTotals gross={100} discount={0} tax={5} charges={0} />);

    expect(screen.queryByText('Discount')).toBeNull();
    expect(screen.queryByText('Charges')).toBeNull();
    expect(screen.queryByText('Round off')).toBeNull();

    // Gross, net and total stay whatever the figures are: they are what the bill is
    // read for, and a missing one reads as a figure that failed to arrive.
    expect(figure('Gross')).toBe('100.00');
    expect(figure('Net')).toBe('100.00');
    expect(figure('Total')).toBe('105.00');
  });

  it('names the currency on the total, and only there', () => {
    render(
      <DocumentTotals gross={100} discount={0} tax={0} charges={0} currency="AED" />,
    );

    // Once, on the figure that is owed. Repeated down the ladder it is noise on
    // every rung; left off entirely it is a bill that does not say what in.
    expect(figure('Total')).toBe('100.00 AED');
    expect(figure('Gross')).toBe('100.00');
  });

  it('shows a charge that comes off the document as the deduction it is', () => {
    render(<DocumentTotals gross={1000} discount={0} tax={0} charges={-40} />);

    expect(figure('Charges')).toBe('-40.00');
    expect(figure('Total')).toBe('960.00');
  });
});
