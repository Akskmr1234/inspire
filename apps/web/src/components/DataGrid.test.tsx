import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '@/i18n';
import { blobText, setMatchingMedia, objectUrls } from '@/test/setup';
import { DataGrid, pageWindow, type GridColumn } from '@/components/DataGrid';
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

/*
  The footer.

  How many records there are and which of them is on screen was a line of small type
  wedged into the top of the toolbar between the filters and the column picker, and
  the pager was two buttons offered only where the server had already paged the list.
  A list of four thousand rows drew all four thousand and said so in a place nobody
  reads. These pin the arrangement that replaced it.
*/
describe('the footer', () => {
  const many = Array.from({ length: 57 }, (_, index) => ({
    id: String(index),
    code: `C-${String(index).padStart(3, '0')}`,
    name: `Row ${index}`,
    amount: index,
  }));

  it('says how many records there are when they all fit', () => {
    render(
      <DataGrid gridKey="small" rows={rows} columns={columns} rowKey={(row) => row.id} />,
    );

    expect(screen.getByText(/3 records/i)).toBeTruthy();
  });

  it('pages a long list in the browser and names the range on screen', () => {
    render(
      <DataGrid
        gridKey="long"
        rows={many}
        columns={columns}
        rowKey={(row) => row.id}
        pageSize={25}
      />,
    );

    expect(screen.getByText(/showing 1–25 of 57/i)).toBeTruthy();
    // The header row and its filter row are rows too, which is why this counts the
    // body rather than the table.
    expect(screen.getAllByRole('row').length).toBe(25 + 2);
  });

  it('offers numbered pages, and goes to the one that is pressed', async () => {
    const user = userEvent.setup();
    render(
      <DataGrid
        gridKey="long"
        rows={many}
        columns={columns}
        rowKey={(row) => row.id}
        pageSize={25}
      />,
    );

    await user.click(screen.getByRole('button', { name: /go to page 3/i }));

    expect(screen.getByText(/showing 51–57 of 57/i)).toBeTruthy();
    expect(screen.getByText('C-056')).toBeTruthy();
  });

  it('says how much of the list a search left, without losing the total', async () => {
    const user = userEvent.setup();
    render(
      <DataGrid
        gridKey="long"
        rows={many}
        columns={columns}
        rowKey={(row) => row.id}
        pageSize={25}
      />,
    );

    await user.type(screen.getByPlaceholderText(/search/i), 'C-04');

    // Ten rows match: C-040 to C-049. The count is what matched, and the total it
    // came out of is kept beside it — a filtered count on its own is a number
    // nobody can calibrate.
    await waitFor(() => expect(screen.getByText(/10 records/i)).toBeTruthy());
    expect(screen.getByText(/filtered from 57/i)).toBeTruthy();
  });

  it('takes the page back to the start when a search narrows the list', async () => {
    const user = userEvent.setup();
    render(
      <DataGrid
        gridKey="long"
        rows={many}
        columns={columns}
        rowKey={(row) => row.id}
        pageSize={10}
      />,
    );

    await user.click(screen.getByRole('button', { name: /go to page 5/i }));
    expect(screen.getByText(/showing 41–50 of 57/i)).toBeTruthy();

    await user.type(screen.getByPlaceholderText(/search/i), 'Row 3');

    // Eleven match — Row 3 and Row 30 to Row 39 — which is two pages, not five.
    await waitFor(() => expect(screen.getByText(/showing 1–10 of 11/i)).toBeTruthy());
  });

  it('withdraws its own search where the screen owns one', () => {
    render(
      <DataGrid
        gridKey="server-searched"
        rows={rows}
        columns={columns}
        rowKey={(row) => row.id}
        hideSearch
      />,
    );

    // Two boxes narrowing the same list by different rules is one more than
    // anybody can use, and neither said which was which.
    expect(screen.queryByPlaceholderText(/search all columns/i)).toBeNull();
  });
});

describe('the page window', () => {
  it('draws every page while they fit', () => {
    expect(pageWindow(1, 5)).toEqual([1, 2, 3, 4, 5]);
  });

  it('keeps the ends reachable and elides the middle', () => {
    expect(pageWindow(50, 100)).toEqual([1, 'gap', 49, 50, 51, 'gap', 100]);
  });

  it('holds its width as the current page moves off an end', () => {
    // A pager that changed width as it was used would shift the button under the
    // pointer between one press and the next.
    expect(pageWindow(1, 100)).toHaveLength(pageWindow(2, 100).length);
    expect(pageWindow(100, 100)).toHaveLength(pageWindow(99, 100).length);
  });

  it('never names a page that does not exist', () => {
    for (const page of [1, 2, 8, 9]) {
      for (const entry of pageWindow(page, 9)) {
        if (entry !== 'gap') {
          expect(entry).toBeGreaterThanOrEqual(1);
          expect(entry).toBeLessThanOrEqual(9);
        }
      }
    }
  });
});

/*
  A column the card view drops.

  Every list here leads its card with the identifying column, which is already the
  link into the record — so an actions column offering "Open" underneath is the
  same action twice, on the screen that can least afford the height.
*/
describe('a narrow-hidden column', () => {
  const withAction: readonly GridColumn<Row>[] = [
    ...columns.slice(0, 2),
    {
      key: 'actions',
      header: '',
      value: () => '',
      hideOnNarrow: true,
      render: () => <button type="button">Open</button>,
    },
  ];

  it('is drawn in the table, where there is room for it', () => {
    setMatchingMedia();
    render(
      <DataGrid
        gridKey="narrow"
        rows={rows}
        columns={withAction}
        rowKey={(row) => row.id}
      />,
    );

    expect(screen.getAllByRole('button', { name: 'Open' }).length).toBe(rows.length);
  });

  it('is dropped from the cards, where the row already leads with its link', () => {
    setMatchingMedia('(max-width: 639px)');
    render(
      <DataGrid
        gridKey="narrow"
        rows={rows}
        columns={withAction}
        rowKey={(row) => row.id}
      />,
    );

    expect(screen.queryAllByRole('button', { name: 'Open' })).toHaveLength(0);
    // The rows are still there — the column went, not the data.
    expect(screen.getAllByRole('listitem')).toHaveLength(rows.length);
  });
});

describe('keeping an arrangement', () => {
  /*
    Arranging the columns and keeping that arrangement are one task in two steps, so
    the button for the second sits beside the first — and pressing it ends the task
    rather than leaving a panel of checkboxes standing over the list.
  */
  it('offers Save layout next to Columns, not across the toolbar', () => {
    render(grid());

    const order = screen
      .getAllByRole('button')
      .map((button) => button.textContent?.trim() ?? '')
      .filter((text) =>
        ['Columns', 'Save layout', 'Freeze first', 'Export CSV', 'Reset'].includes(text),
      );

    expect(order).toEqual([
      'Columns',
      'Save layout',
      'Freeze first',
      'Export CSV',
      'Reset',
    ]);
  });

  it('shuts the column picker once the arrangement is kept', async () => {
    const user = userEvent.setup();
    render(grid());

    const toggle = (): HTMLElement => screen.getByRole('button', { name: 'Columns' });

    expect(toggle().getAttribute('aria-pressed')).toBe('false');

    await user.click(toggle());
    expect(toggle().getAttribute('aria-pressed')).toBe('true');

    await user.click(screen.getByRole('button', { name: 'Save layout' }));

    await waitFor(() => {
      expect(screen.getByText('Layout saved')).toBeTruthy();
    });

    // The toggle is back up, so the panel is down with it.
    expect(toggle().getAttribute('aria-pressed')).toBe('false');
  });
});
