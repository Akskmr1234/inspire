import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderPage } from '@/test/renderPage';
import type { DashboardCharts as Charts } from '@/lib/dashboardCharts';

/*
  The charts under the dashboard, against answers shaped like the server's.

  What matters is what is drawn and what is left out: a chart the reader may not open
  the report for must not appear at all - a flat line would say nothing was sold - and
  every chart must hand its figures over as a table for anyone who cannot read them
  off a picture.
*/

let answer: Charts;

vi.mock('@/lib/api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');

  return { ...actual, request: vi.fn(async () => answer) };
});

const { DashboardCharts } = await import('@/components/DashboardCharts');

function days(sales: (index: number) => number): Charts['dailySales'] {
  return Array.from({ length: 30 }, (_, index) => {
    const date = new Date(Date.UTC(2026, 9, 5 + index)).toISOString().slice(0, 10);
    return { date, sales: sales(index), bills: sales(index) > 0 ? 3 : 0 };
  });
}

const months = [
  '2025-12',
  '2026-01',
  '2026-02',
  '2026-03',
  '2026-04',
  '2026-05',
  '2026-06',
  '2026-07',
  '2026-08',
  '2026-09',
  '2026-10',
  '2026-11',
];

function everything(): Charts {
  return {
    asAt: '2026-11-03',
    currency: 'QAR',
    dailySales: days((index) => 1000 + index * 10),
    months: months.map((month, index) => ({
      month,
      sales: 20_000 + index * 1_000,
      purchases: 15_000 + index * 500,
      profit: index === 2 ? -1_500 : 3_000 + index * 100,
    })),
    paymentModes: [
      { mode: 2, amount: 600, bills: 6 },
      { mode: 4, amount: 300, bills: 2 },
      { mode: 1, amount: 100, bills: 1 },
    ],
    topProducts: [
      { label: 'Memory card 64 GB', detail: '05813', amount: 450, quantity: 3 },
    ],
    topCategories: [{ label: 'Memory cards', detail: null, amount: 450, quantity: 3 }],
    receivables: [
      { fromDays: 0, toDays: 0, amount: 500 },
      { fromDays: 1, toDays: 30, amount: 300 },
      { fromDays: 31, toDays: 60, amount: 0 },
      { fromDays: 61, toDays: 90, amount: 0 },
      { fromDays: 91, toDays: null, amount: 200 },
    ],
  };
}

beforeEach(() => {
  answer = everything();
});

describe('DashboardCharts', () => {
  it('draws every chart the reader may read', async () => {
    renderPage(<DashboardCharts />);

    for (const title of [
      'Sales, last 30 days',
      'How customers paid',
      'Sales and purchases',
      'Profit',
      'Top products',
      'Top categories',
      'What customers owe',
    ]) {
      expect(await screen.findByRole('heading', { name: title })).toBeTruthy();
    }
  });

  it('leaves out a chart the reader may not read rather than drawing it flat', async () => {
    answer = { ...everything(), months: null, receivables: null };
    renderPage(<DashboardCharts />);

    expect(
      await screen.findByRole('heading', { name: 'Sales, last 30 days' }),
    ).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Profit' })).toBeNull();
    expect(screen.queryByRole('heading', { name: 'Sales and purchases' })).toBeNull();
    expect(screen.queryByRole('heading', { name: 'What customers owe' })).toBeNull();
  });

  it('draws nothing at all for a reader who may read none of them', async () => {
    answer = {
      asAt: '2026-11-03',
      currency: 'QAR',
      dailySales: null,
      months: null,
      paymentModes: null,
      topProducts: null,
      topCategories: null,
      receivables: null,
    };
    const { container } = renderPage(<DashboardCharts />);

    await waitFor(() =>
      expect(container.querySelector('[aria-hidden="true"].grid')).toBeNull(),
    );
    expect(container.querySelector('figure')).toBeNull();
  });

  it('says there were no sales rather than drawing a line along zero', async () => {
    answer = { ...everything(), dailySales: days(() => 0) };
    renderPage(<DashboardCharts />);

    expect(await screen.findByText('No sales in these 30 days.')).toBeTruthy();
  });

  it('names a column of only purchases for what it is', async () => {
    answer = {
      ...everything(),
      months: months.map((month) => ({
        month,
        sales: null,
        purchases: 1_000,
        profit: null,
      })),
    };
    renderPage(<DashboardCharts />);

    expect(await screen.findByRole('heading', { name: 'Purchases' })).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Profit' })).toBeNull();
  });

  it('writes each payment mode with its share of the month', async () => {
    renderPage(<DashboardCharts />);

    const card = (
      await screen.findByRole('heading', { name: 'How customers paid' })
    ).closest('figure');
    const scope = within(card as HTMLElement);

    expect(scope.getByText('Cash')).toBeTruthy();
    expect(scope.getByText('Card')).toBeTruthy();
    expect(scope.getByText('Credit')).toBeTruthy();
    expect(
      scope.getByText((60 / 100).toLocaleString(undefined, { style: 'percent' })),
    ).toBeTruthy();
  });

  it('hands every chart over as a table', async () => {
    const user = userEvent.setup();
    renderPage(<DashboardCharts />);

    const card = (
      await screen.findByRole('heading', { name: 'Sales, last 30 days' })
    ).closest('figure');
    const scope = within(card as HTMLElement);

    await user.click(scope.getByRole('button', { name: 'Table' }));

    const table = scope.getByRole('table');
    // A header and one row a day, the quiet days included.
    expect(within(table).getAllByRole('row')).toHaveLength(31);
    expect(
      scope.getByRole('button', { name: 'Chart' }).getAttribute('aria-pressed'),
    ).toBe('true');
  });

  it('ages what customers owe into the age-wise report columns, in order', async () => {
    const user = userEvent.setup();
    renderPage(<DashboardCharts />);

    const card = (
      await screen.findByRole('heading', { name: 'What customers owe' })
    ).closest('figure');
    const scope = within(card as HTMLElement);

    await user.click(scope.getByRole('button', { name: 'Table' }));

    const names = within(scope.getByRole('table'))
      .getAllByRole('rowheader')
      .map((cell) => cell.textContent);

    expect(names).toEqual([
      'Not yet due',
      '1–30 days',
      '31–60 days',
      '61–90 days',
      'Over 90 days',
    ]);
  });
});
