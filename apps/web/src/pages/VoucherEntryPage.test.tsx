import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderPage } from '@/test/renderPage';
import { fixtureFor } from '@/test/fixtures';
import { setMatchingMedia } from '@/test/setup';

/*
  The running balance is the whole point of this screen: it is what catches a
  transposed digit at the keyboard rather than at the server. What it says when
  there is nothing to say yet matters as much as what it says when the voucher is
  wrong, because it is the first thing on the screen every time it is opened.
*/

vi.mock('@/lib/api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');

  return {
    ...actual,
    request: vi.fn((path: string) => {
      const fixture = fixtureFor(path);

      return fixture === undefined
        ? Promise.reject(new actual.ApiError(404, 'Test.NoFixture', path))
        : Promise.resolve(fixture);
    }),
  };
});

const { PaymentEntryPage, VoucherEntryPage } = await import('@/pages/VoucherEntryPage');

describe('the balance badge', () => {
  it('says nothing has been entered rather than naming a difference of zero', async () => {
    setMatchingMedia();
    renderPage(<VoucherEntryPage />);

    await waitFor(() => expect(screen.getByText(/nothing entered yet/i)).toBeTruthy());

    // The bug this pins: an untouched voucher has a difference of exactly zero and
    // is not balanced, so the two-way badge fell through to the unbalanced branch
    // and opened the screen with "Difference 0.00 (credit heavy)" — a statement
    // that is neither true nor actionable.
    expect(screen.queryByText(/credit heavy|debit heavy/i)).toBeNull();
  });

  it('names the difference once there is one', async () => {
    setMatchingMedia();
    const user = userEvent.setup();
    renderPage(<VoucherEntryPage />);

    await waitFor(() => expect(screen.getByText(/nothing entered yet/i)).toBeTruthy());

    await user.type(screen.getAllByLabelText(/debit amount/i)[0]!, '250');

    await waitFor(() => expect(screen.getByText(/debit heavy/i)).toBeTruthy());
  });

  it('reports a voucher that balances', async () => {
    setMatchingMedia();
    const user = userEvent.setup();
    renderPage(<VoucherEntryPage />);

    await waitFor(() => expect(screen.getByText(/nothing entered yet/i)).toBeTruthy());

    await user.type(screen.getAllByLabelText(/debit amount/i)[0]!, '250');

    // Turn the second line over to credit, then enter the matching amount.
    const sides = screen.getAllByRole('combobox', { name: /dr \/ cr/i });
    await user.click(sides[1]!);
    await user.click(screen.getByRole('option', { name: /^credit$/i }));
    await user.type(screen.getAllByLabelText(/credit amount/i)[0]!, '250');

    await waitFor(() => expect(screen.getByText(/^balanced$/i)).toBeTruthy());
  });
});

describe('the payment mode', () => {
  it('offers the modes rather than a box to type one into', async () => {
    setMatchingMedia();
    const user = userEvent.setup();
    renderPage(<PaymentEntryPage />);

    const field = await screen.findByRole('combobox', { name: /payment mode/i });
    await user.click(field);

    /*
      The field this replaced was free text, which is how "Cash", "cash" and "By
      cash" became three modes to a report and one to the cashier. The list is the
      fix, so what it is worth pinning is that the list is there at all.
    */
    expect(screen.getByRole('option', { name: 'Cheque' })).toBeTruthy();

    await user.click(screen.getByRole('option', { name: 'Bank transfer' }));

    expect((field as HTMLInputElement).value).toBe('Bank transfer');
  });
});

const { VoucherReportPage } = await import('@/pages/VoucherReportPage');

describe('the list a posted voucher lands on', () => {
  it('names the voucher and narrows to its type', async () => {
    setMatchingMedia();
    renderPage(
      <VoucherReportPage />,
      '/accounting/voucher-report?type=3&posted=CPV-0007',
    );

    /*
      The redirect is the only confirmation a posting now gets: the entry screen no
      longer stays put with a green line on it. If the number does not arrive here,
      somebody who looked away sees an empty form and re-enters the voucher.
    */
    expect(await screen.findByText(/CPV-0007/)).toBeTruthy();

    const type = screen.getByRole('combobox', { name: /voucher type/i });
    expect((type as HTMLInputElement).value).toBe('Cash payment');
  });

  it('says nothing when somebody opens the list on its own', () => {
    setMatchingMedia();
    renderPage(<VoucherReportPage />, '/accounting/voucher-report');

    expect(screen.queryByText(/is posted, and is in the list below/i)).toBeNull();
  });
});
