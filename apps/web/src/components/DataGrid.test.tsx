import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '@/i18n';
import { blobText, setMatchingMedia, objectUrls } from '@/test/setup';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { useSession } from '@/stores/session';

/*
  The grid is the component every list in the application is built on, so the things
  worth pinning are the ones that would be silently wrong rather than visibly broken:
  an export that splits a ledger name in two, a sort that orders by the rendered badge
  instead of the value behind it, a column a user is not entitled to reappearing from
  a stale saved layout.
*/

// The saved-arrangement endpoints. Left unmocked they reach the network, and the
// grid's own error handling would swallow the failure and make every test pass for
// the wrong reason.
vi.mock('@/lib/grid', () => ({
  fetchGridLayout: vi.fn(async () => null),
  saveGridLayout: vi.fn(async () => undefined),
  resetGridLayout: vi.fn(async () => undefined),
}));

interface Row {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly amount: number;
}

const rows: readonly Row[] = [
  { id: '1', code: 'B-002', name: 'Smith, J', amount: 250.5 },
  { id: '2', code: 'A-001', name: 'Adams "Tony" Ltd', amount: 1200 },
  { id: '3', code: 'C-003', name: 'Zephyr\nWorks', amount: 30 },
];

const columns: readonly GridColumn<Row>[] = [
  { key: 'code', header: 'Code', value: (row) => row.code },
  { key: 'name', header: 'Name', value: (row) => row.name },
  {
    key: 'amount',
    header: 'Amount',
    value: (row) => row.amount,
    numeric: true,
    render: (row) => row.amount.toFixed(2),
  },
  {
    key: 'cost',
    header: 'Cost',
    value: () => 'secret',
    requiredPermission: 'inventory:product:edit',
  },
];

function grid(): React.JSX.Element {
  return (
    <DataGrid gridKey="test" rows={rows} columns={columns} rowKey={(row) => row.id} />
  );
}

/** The header cells of the first row only — the second row holds the per-column
    search boxes, whose labels also carry the column name. */
function headerNames(): readonly string[] {
  const first = screen.getAllByRole('row')[0]!;
  return within(first)
    .getAllByRole('columnheader')
    .map((cell) => cell.textContent ?? '');
}

/** The codes as the table currently has them, top to bottom. */
function renderedCodes(): readonly string[] {
  return screen
    .getAllByRole('row')
    .slice(2) // the header row and the per-column search row
    .map((row) => within(row).getAllByRole('cell')[0]?.textContent ?? '');
}

beforeEach(() => {
  setMatchingMedia(); // wide viewport: the table, not the cards
  useSession.setState({ permissions: new Set<string>(['*']) });
});

describe('sorting', () => {
  it('is operable from the keyboard', async () => {
    const user = userEvent.setup();
    render(grid());

    const header = screen.getByRole('button', { name: /code/i });

    // Tab to it and press Enter. A `<th>` with an onClick — which is what this was
    // before — takes no focus and answers no key, so this is the whole point.
    header.focus();
    expect(document.activeElement).toBe(header);

    await user.keyboard('{Enter}');
    expect(renderedCodes()).toEqual(['A-001', 'B-002', 'C-003']);

    await user.keyboard('{Enter}');
    expect(renderedCodes()).toEqual(['C-003', 'B-002', 'A-001']);
  });

  it('reports its direction to assistive technology', async () => {
    const user = userEvent.setup();
    render(grid());

    const cell = within(screen.getAllByRole('row')[0]!).getAllByRole('columnheader')[0]!;
    // Plain attribute reads rather than jest-dom matchers, to keep this suite to
    // the four devDependencies it actually needs.
    expect(cell.getAttribute('aria-sort')).toBeNull();

    await user.click(within(cell).getByRole('button'));
    expect(cell.getAttribute('aria-sort')).toBe('ascending');

    await user.click(within(cell).getByRole('button'));
    expect(cell.getAttribute('aria-sort')).toBe('descending');
  });

  it('orders by the underlying value, not the rendered text', async () => {
    const user = userEvent.setup();
    render(grid());

    // `amount` renders as a fixed-decimal string. Sorted as text "1200.00" precedes
    // "250.50"; sorted as a number it does not.
    await user.click(screen.getByRole('button', { name: /amount/i }));

    expect(renderedCodes()).toEqual(['C-003', 'B-002', 'A-001']);
  });

  it('sorts blanks last whichever way the column points', async () => {
    const user = userEvent.setup();
    const withBlank = [...rows, { id: '4', code: '', name: 'No code', amount: 0 }];

    render(
      <DataGrid
        gridKey="test"
        rows={withBlank}
        columns={columns}
        rowKey={(row) => row.id}
      />,
    );

    await user.click(screen.getByRole('button', { name: /code/i }));
    expect(renderedCodes().at(-1)).toBe('');

    await user.click(screen.getByRole('button', { name: /code/i }));
    expect(renderedCodes().at(-1)).toBe('');
  });
});

describe('permissions', () => {
  it('withholds a column the user does not hold the permission for', () => {
    useSession.setState({ permissions: new Set<string>(['inventory:product:read']) });
    render(grid());

    expect(headerNames().some((name) => /cost/i.test(name))).toBe(false);
    expect(screen.queryByText('secret')).toBeNull();
  });

  it('shows it for a super administrator, whose permission list is a wildcard', () => {
    useSession.setState({ permissions: new Set<string>(['*']) });
    render(grid());

    expect(headerNames().some((name) => /cost/i.test(name))).toBe(true);
  });
});

describe('CSV export', () => {
  it('quotes the delimiters, quotes and newlines that would otherwise split a field', async () => {
    const user = userEvent.setup();
    objectUrls.clear();
    render(grid());

    await user.click(screen.getByRole('button', { name: /export/i }));

    const blob = [...objectUrls.values()].at(-1);
    expect(blob).toBeTruthy();

    const text = blobText(blob!);

    // A name holding a comma must not become two columns, an internal quote must be
    // doubled, and an embedded newline must not become a new row.
    expect(text).toContain('"Smith, J"');
    expect(text).toContain('"Adams ""Tony"" Ltd"');
    expect(text).toContain('"Zephyr\nWorks"');

    // The byte-order mark, without which Excel reads UTF-8 as the local code page and
    // turns every Arabic name into punctuation.
    expect(text.startsWith('﻿')).toBe(true);
  });

  it('exports what is on screen rather than the whole list', async () => {
    const user = userEvent.setup();
    objectUrls.clear();
    render(grid());

    await user.type(screen.getByPlaceholderText(/search/i), 'Smith');

    await waitFor(() => expect(renderedCodes()).toEqual(['B-002']));
    await user.click(screen.getByRole('button', { name: /export/i }));

    const text = blobText([...objectUrls.values()].at(-1)!);
    expect(text).toContain('B-002');
    expect(text).not.toContain('A-001');
  });
});

describe('the narrow viewport', () => {
  beforeEach(() => setMatchingMedia('(max-width: 639px)'));

  it('renders cards instead of a table', () => {
    render(grid());

    expect(screen.queryByRole('table')).toBeNull();
    expect(screen.getByText('Smith, J')).toBeTruthy();
  });

  it('still offers a way to sort, since there are no headers to click', async () => {
    const user = userEvent.setup();
    render(grid());

    await user.selectOptions(screen.getByRole('combobox'), 'code');

    const codes = screen.getAllByRole('listitem').map((item) => item.textContent ?? '');
    expect(codes[0]).toContain('A-001');

    await user.click(screen.getByRole('button', { name: /reverse/i }));

    const reversed = screen
      .getAllByRole('listitem')
      .map((item) => item.textContent ?? '');
    expect(reversed[0]).toContain('C-003');
  });

  it('withdraws the freeze control, which means nothing without columns', () => {
    render(grid());
    expect(screen.queryByRole('button', { name: /freeze/i })).toBeNull();
  });

  it('leaves out the fields a row has nothing for', () => {
    render(
      <DataGrid
        gridKey="sparse"
        rows={[{ id: '1', code: 'C-001', name: 'Al Noor Trading', amount: 0 }]}
        columns={[
          { key: 'code', header: 'Code', value: (row) => row.code },
          { key: 'name', header: 'Name', value: (row) => row.name },
          { key: 'mobile', header: 'Mobile', value: () => '' },
          { key: 'state', header: 'State', value: () => null },
          { key: 'amount', header: 'Amount', value: (row) => row.amount, numeric: true },
        ]}
        rowKey={(row) => row.id}
      />,
    );

    // Scoped to the card: the columns are still there to be sorted by, which is
    // why the toolbar's sort list still names them.
    const card = within(screen.getByRole('listitem'));

    // A table can afford an empty cell; the column above it carries the meaning.
    // On a card the label travels with the row, so a blank one is a line saying
    // nothing — and a record with five of them is mostly the shape of the schema.
    expect(card.queryByText('Mobile')).toBeNull();
    expect(card.queryByText('State')).toBeNull();

    // Zero is a figure, not a blank.
    expect(card.getByText('Amount')).toBeTruthy();
    expect(card.getByText('Name')).toBeTruthy();
  });
});

describe('the empty result', () => {
  it('says so rather than drawing an empty table', async () => {
    const user = userEvent.setup();
    render(
      <DataGrid
        gridKey="test"
        rows={rows}
        columns={columns}
        rowKey={(row) => row.id}
        emptyMessage="No such ledger"
      />,
    );

    await user.type(screen.getByPlaceholderText(/search/i), 'nothing matches this');

    await waitFor(() => expect(screen.getByText('No such ledger')).toBeTruthy());
  });
});
