import { describe, expect, it, vi } from 'vitest';
import { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import '@/i18n';
import { isSameItem, useLineGuards, type ItemLine } from '@/components/invoice/LineGuards';

/*
  One item, one line - and one empty line at a time. A second line for an item already
  on the document is asked about rather than kept, and Add item does not stack empty
  lines but offers the products for the one there is.
*/

interface Line extends ItemLine {
  readonly productId: string;
  readonly quantity: string;
  readonly batchNumber?: string;
  readonly expiresOn?: string;
}

describe('whether two lines are the same item', () => {
  it('is the same product, batch and expiry', () => {
    expect(isSameItem({ productId: 'p' }, { productId: 'p' })).toBe(true);
    expect(
      isSameItem(
        { productId: 'p', batchNumber: 'b1 ', expiresOn: '2027-01-31' },
        { productId: 'p', batchNumber: 'B1', expiresOn: '2027-01-31' },
      ),
    ).toBe(true);
  });

  it('is not when the batch or the expiry differs, or no product is chosen', () => {
    expect(isSameItem({ productId: 'p', batchNumber: 'B1' }, { productId: 'p', batchNumber: 'B2' })).toBe(false);
    expect(
      isSameItem({ productId: 'p', expiresOn: '2027-01-31' }, { productId: 'p', expiresOn: '2027-02-28' }),
    ).toBe(false);
    expect(isSameItem({ productId: '' }, { productId: '' })).toBe(false);
    expect(isSameItem({ productId: 'p' }, { productId: 'q' })).toBe(false);
  });
});

/** A screen's lines, and the guards over them: two buttons stand in for its pickers. */
function Screen({
  initial,
  attend = vi.fn(),
  choose = vi.fn(),
  onLines,
}: {
  readonly initial: readonly Line[];
  readonly attend?: (
    line: number | 'last',
    options?: { readonly focus?: boolean | 'quantity' },
  ) => void;
  readonly choose?: (index: number, productId: string) => void;
  readonly onLines: (lines: readonly Line[]) => void;
}): React.JSX.Element {
  const [lines, setLines] = useState<readonly Line[]>(initial);
  onLines(lines);

  const guards = useLineGuards<Line>({
    lines,
    setLines,
    blank: () => ({ productId: '', quantity: '1' }),
    attend,
    choose,
    productOptions: [
      { value: 'p', label: 'P — Pen' },
      { value: 'q', label: 'Q — Quill' },
    ],
  });

  return (
    <>
      <button type="button" onClick={() => guards.change(1, { productId: 'p' })}>
        choose pen on line 2
      </button>
      <button
        type="button"
        onClick={() =>
          guards.add(() => setLines((previous) => [...previous, { productId: '', quantity: '1' }]))
        }
      >
        add item
      </button>
      {guards.dialogs}
    </>
  );
}

describe('choosing an item that is already on the document', () => {
  const twoLines: readonly Line[] = [
    { productId: 'p', quantity: '3' },
    { productId: '', quantity: '1' },
  ];

  it('asks, and on yes keeps the one line with the cursor in its quantity', () => {
    let seen: readonly Line[] = [];
    const attend = vi.fn();
    render(<Screen initial={twoLines} attend={attend} onLines={(lines) => (seen = lines)} />);

    fireEvent.click(screen.getByText('choose pen on line 2'));
    expect(screen.getByRole('alertdialog').textContent).toContain('This item has already been added.');
    expect(screen.getByRole('alertdialog').textContent).toContain('Do you want to update the quantity?');

    fireEvent.click(screen.getByRole('button', { name: 'Yes' }));
    expect(seen).toEqual([{ productId: 'p', quantity: '3' }]);
    expect(attend).toHaveBeenLastCalledWith(0, { focus: 'quantity' });
  });

  it('on no leaves the existing line as it was and undoes the new choice', () => {
    let seen: readonly Line[] = [];
    render(<Screen initial={twoLines} onLines={(lines) => (seen = lines)} />);

    fireEvent.click(screen.getByText('choose pen on line 2'));
    fireEvent.click(screen.getByRole('button', { name: 'No' }));

    expect(seen).toEqual([
      { productId: 'p', quantity: '3' },
      { productId: '', quantity: '1' },
    ]);
    expect(screen.queryByRole('alertdialog')).toBeNull();
  });

  it('does not ask when the batch differs', () => {
    render(
      <Screen
        initial={[
          { productId: 'p', quantity: '3', batchNumber: 'B1' },
          { productId: '', quantity: '1' },
        ]}
        onLines={() => undefined}
      />,
    );

    fireEvent.click(screen.getByText('choose pen on line 2'));
    expect(screen.queryByRole('alertdialog')).toBeNull();
  });
});

describe('add item', () => {
  it('adds a line only when every line names a product', () => {
    let seen: readonly Line[] = [];
    render(<Screen initial={[{ productId: 'p', quantity: '1' }]} onLines={(lines) => (seen = lines)} />);

    fireEvent.click(screen.getByText('add item'));
    expect(seen).toHaveLength(2);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('offers the products for the line still without one, instead of another empty line', () => {
    let seen: readonly Line[] = [];
    const choose = vi.fn();
    render(
      <Screen
        initial={[
          { productId: 'p', quantity: '1' },
          { productId: '', quantity: '1' },
        ]}
        choose={choose}
        onLines={(lines) => (seen = lines)}
      />,
    );

    fireEvent.click(screen.getByText('add item'));
    expect(seen).toHaveLength(2);
    const list = screen.getByRole('dialog');
    expect(list.textContent).toContain('Choose a product');

    fireEvent.click(screen.getByRole('option', { name: /Quill/ }));
    expect(choose).toHaveBeenCalledWith(1, 'q');
    expect(screen.queryByRole('dialog')).toBeNull();
  });
});

