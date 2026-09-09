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

const { VoucherEntryPage } = await import('@/pages/VoucherEntryPage');

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
    const sides = screen.getAllByLabelText(/dr \/ cr/i);
    await user.selectOptions(sides[1]!, '2');
    await user.type(screen.getAllByLabelText(/credit amount/i)[0]!, '250');

    await waitFor(() => expect(screen.getByText(/^balanced$/i)).toBeTruthy());
  });
});
