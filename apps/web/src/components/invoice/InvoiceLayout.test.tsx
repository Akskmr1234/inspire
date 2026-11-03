import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import '@/i18n';
import { InvoiceLines, PartyCard } from '@/components/invoice/InvoiceLayout';

/*
  Enter moving on through an invoice's lines is behaviour a counter learns once and
  then relies on without looking: break it and the next item is typed into the wrong
  box. These pin down where the cursor goes, and where it must not.
*/

function Lines({
  first = 'Pen',
  second = '',
  picks = false,
  onAdd = vi.fn(),
}: {
  readonly first?: string;
  readonly second?: string;
  /** Whether the first line's picker takes Enter, as it does when it chooses. */
  readonly picks?: boolean;
  readonly onAdd?: () => void;
}): React.JSX.Element {
  return (
    <>
      <InvoiceLines>
        <table>
          <tbody>
            <tr data-line-row>
              <td>
                <input
                  role="combobox"
                  aria-label="Product 1"
                  defaultValue={first}
                  onKeyDown={(event) => {
                    if (picks && event.key === 'Enter') event.preventDefault();
                  }}
                />
              </td>
              <td>
                <input type="number" aria-label="Quantity 1" defaultValue="1" />
              </td>
              <td>
                <input type="number" aria-label="Rate 1" defaultValue="5" />
              </td>
              <td>
                <button type="button">Remove 1</button>
              </td>
            </tr>
            <tr>
              <td>
                <input aria-label="Batch 1" />
              </td>
            </tr>
            <tr data-line-row>
              <td>
                <input role="combobox" aria-label="Product 2" defaultValue={second} />
              </td>
              <td>
                <input type="number" aria-label="Quantity 2" defaultValue="1" />
              </td>
            </tr>
          </tbody>
        </table>
      </InvoiceLines>
      <button type="button" data-shortcut="add-line" onClick={onAdd}>
        Add item
      </button>
    </>
  );
}

const enter = (box: HTMLElement, init: KeyboardEventInit = {}): void => {
  box.focus();
  fireEvent.keyDown(box, { key: 'Enter', ...init });
};

describe('Enter in the lines', () => {
  // jsdom lays nothing out, so every element would read as hidden to the shortcut
  // lookup that finds Add item; here everything is on screen.
  beforeEach(() => {
    Object.defineProperty(HTMLElement.prototype, 'offsetParent', {
      configurable: true,
      get(this: HTMLElement) {
        return this.parentNode;
      },
    });
  });

  afterEach(() => {
    delete (HTMLElement.prototype as { offsetParent?: unknown }).offsetParent;
  });

  it("moves to the line's next box", () => {
    render(<Lines />);

    enter(screen.getByLabelText('Quantity 1'));

    expect(document.activeElement).toBe(screen.getByLabelText('Rate 1'));
  });

  it('goes through the batch row under a line, then to the next line', () => {
    render(<Lines />);

    enter(screen.getByLabelText('Rate 1'));
    expect(document.activeElement).toBe(screen.getByLabelText('Batch 1'));

    enter(screen.getByLabelText('Batch 1'));
    expect(document.activeElement).toBe(screen.getByLabelText('Product 2'));
  });

  it("leaves a picker's Enter to the picker until it has chosen", () => {
    const { unmount } = render(<Lines />);

    enter(screen.getByLabelText('Product 1'));
    expect(document.activeElement).toBe(screen.getByLabelText('Product 1'));

    unmount();
    render(<Lines picks />);

    enter(screen.getByLabelText('Product 1'));
    expect(document.activeElement).toBe(screen.getByLabelText('Quantity 1'));
  });

  it('adds a line after the last one once it names a product, and not before', () => {
    const onAdd = vi.fn();
    const { unmount } = render(<Lines onAdd={onAdd} />);

    enter(screen.getByLabelText('Quantity 2'));
    expect(onAdd).not.toHaveBeenCalled();

    unmount();
    render(<Lines second="Ink" onAdd={onAdd} />);

    enter(screen.getByLabelText('Quantity 2'));
    expect(onAdd).toHaveBeenCalledTimes(1);
  });

  it('leaves Enter with a modifier alone: Ctrl+Enter saves', () => {
    render(<Lines />);

    enter(screen.getByLabelText('Quantity 1'), { ctrlKey: true });

    expect(document.activeElement).toBe(screen.getByLabelText('Quantity 1'));
  });
});

describe("the party's card", () => {
  const party = {
    code: 'C001',
    name: 'Rajwa Trading',
    contact: {
      mobileNumber: '0500000000',
      phone: null,
      addressLine1: null,
      addressLine2: null,
    },
    terms: { creditLimit: null, creditDays: null },
    taxDetails: { registrationNumber: null },
  };

  it('offers to choose one when none is chosen, and goes to the box to do it', () => {
    render(
      <div role="dialog">
        <div data-party-field>
          <input aria-label="Customer" />
        </div>
        <PartyCard
          icon={null}
          title="Customer details"
          party={undefined}
          empty="No customer chosen yet."
          choose="Choose customer"
          money={(value) => value.toFixed(2)}
        />
      </div>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Choose customer' }));

    expect(document.activeElement).toBe(screen.getByLabelText('Customer'));
  });

  it('shows who is chosen, and no button', () => {
    render(
      <PartyCard
        icon={null}
        title="Customer details"
        party={party}
        empty="No customer chosen yet."
        choose="Choose customer"
        money={(value) => value.toFixed(2)}
      />,
    );

    expect(screen.getByText('Rajwa Trading')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Choose customer' })).toBeNull();
  });
});
