import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { UseQueryResult } from '@tanstack/react-query';
import '@/i18n';
import { BalanceBadge, ReportFrame, money, moneyAlways } from '@/components/ReportFrame';
import type { ApiError } from '@/lib/api';

/*
  The frame decides what every report screen shows when the figures are absent,
  late, or wrong. Those paths are the ones nobody exercises by hand — a report is
  usually opened when it works — so they are the ones that rot.
*/

interface Data {
  readonly rows: readonly string[];
}

/** A query result in whichever state a test needs. Only the fields the frame reads. */
function queryIn(
  state: 'pending' | 'error' | 'success' | 'refetching',
  data?: Data,
  refetch = vi.fn(),
): UseQueryResult<Data, ApiError> {
  return {
    isPending: state === 'pending',
    isError: state === 'error',
    isFetching: state === 'pending' || state === 'refetching',
    data:
      state === 'success' || state === 'refetching' ? (data ?? { rows: [] }) : undefined,
    error:
      state === 'error'
        ? ({ code: 'Report.Failed', detail: 'The period is not open.' } as ApiError)
        : null,
    refetch,
  } as unknown as UseQueryResult<Data, ApiError>;
}

function frame(query: UseQueryResult<Data, ApiError>, isEmpty?: (data: Data) => boolean) {
  return (
    <ReportFrame
      title="Trial balance"
      controls={<button type="button">Run</button>}
      query={query}
      {...(isEmpty ? { isEmpty } : {})}
    >
      {(data) => <p>{data.rows.length} rows</p>}
    </ReportFrame>
  );
}

describe('while the figures are on their way', () => {
  it('holds the shape of the table rather than saying "loading"', () => {
    render(frame(queryIn('pending')));

    // A skeleton, so the screen settles into place instead of jumping when the
    // figures land. Nothing of the body should be on screen yet.
    expect(screen.queryByText(/rows/)).toBeNull();
    expect(document.querySelectorAll('.skeleton').length).toBeGreaterThan(0);
  });
});

describe('when it fails', () => {
  it('shows the code and the detail, not a shrug', () => {
    render(frame(queryIn('error')));

    expect(screen.getByRole('alert')).toBeTruthy();
    expect(screen.getByText('Report.Failed')).toBeTruthy();
    expect(screen.getByText('The period is not open.')).toBeTruthy();
  });

  it('offers a retry that actually refetches', async () => {
    const user = userEvent.setup();
    const refetch = vi.fn();

    render(frame(queryIn('error', undefined, refetch)));
    await user.click(screen.getByRole('button', { name: /try again/i }));

    expect(refetch).toHaveBeenCalledOnce();
  });
});

describe('when a refetch is in flight over figures already on screen', () => {
  it('leaves the figures up and shows a hairline instead', () => {
    render(frame(queryIn('refetching', { rows: ['a', 'b'] })));

    // Blanking a statement somebody is reading in order to say "loading" costs
    // them their place, so the body must survive the refetch.
    expect(screen.getByText('2 rows')).toBeTruthy();
    expect(document.querySelector('.progress-sweep')).toBeTruthy();
  });

  it('does not show the hairline on the first load, where the skeleton speaks', () => {
    render(frame(queryIn('pending')));
    expect(document.querySelector('.progress-sweep')).toBeNull();
  });
});

describe('when the query succeeds but matched nothing', () => {
  it('says so rather than drawing an empty body', () => {
    render(frame(queryIn('success', { rows: [] }), (data) => data.rows.length === 0));

    expect(screen.getByText(/no postings/i)).toBeTruthy();
    expect(screen.queryByText('0 rows')).toBeNull();
  });

  it('draws the body when there is something to draw', () => {
    render(frame(queryIn('success', { rows: ['a'] }), (data) => data.rows.length === 0));

    expect(screen.getByText('1 rows')).toBeTruthy();
  });
});

describe('the print control', () => {
  it('is offered on every report, and opens the dialog', async () => {
    const user = userEvent.setup();
    const print = vi.fn();
    window.print = print;

    render(frame(queryIn('success', { rows: ['a'] })));
    await user.click(screen.getByRole('button', { name: /print/i }));

    expect(print).toHaveBeenCalledOnce();
  });
});

describe('the balance indicator', () => {
  it('does not rely on colour alone to say the books are broken', () => {
    const { container } = render(<BalanceBadge isBalanced={false} currency="AED" />);

    // Roughly one man in twelve cannot tell this green from this red, so the
    // state has to be carried by the words as well as the hue.
    expect(screen.getByText(/out of balance/i)).toBeTruthy();
    expect(container.querySelector('.animate-breathe')).toBeTruthy();
  });

  it('names the currency when it balances, since that is what was checked', () => {
    render(<BalanceBadge isBalanced currency="AED" />);
    expect(screen.getByText(/AED/)).toBeTruthy();
  });
});

describe('money formatting', () => {
  it('blanks a zero so the eye follows the figures down a column', () => {
    expect(money(0)).toBe('');
    expect(moneyAlways(0)).toBe('0.00');
  });

  it('always carries two decimals, which is what makes a column line up', () => {
    expect(money(1234.5)).toMatch(/1,?234\.50/);
    expect(moneyAlways(-99)).toMatch(/-99\.00/);
  });
});
