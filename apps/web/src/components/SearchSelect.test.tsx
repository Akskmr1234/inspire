import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import '@/i18n';
import { Modal } from '@/components/Modal';
import {
  SearchSelect,
  useDefaultChoice,
  type SelectOption,
} from '@/components/SearchSelect';

/*
  The picker that replaced every `<select>` reaching a master.

  What is worth pinning is what the native control could not do and what a
  hand-rolled combobox usually gets wrong: matching on the parts of a row that are
  not its label, keeping focus in the input while a row is clicked, and closing on
  Escape without also closing the dialog the picker is sitting in.
*/

const options: readonly SelectOption[] = [
  {
    value: 'p1',
    label: 'A-100 — Blue ballpoint pen',
    detail: 'Stationery · Bic · EA',
    meta: '240',
    keywords: '5901234123457',
  },
  { value: 'p2', label: 'A-200 — Red ballpoint pen', detail: 'Stationery · Bic · EA' },
  { value: 'p3', label: 'B-100 — A4 copier paper', detail: 'Paper · Double A · REAM' },
];

function Picker({
  onChange,
  initial = '',
}: {
  readonly onChange?: (value: string) => void;
  readonly initial?: string;
}): React.JSX.Element {
  const [value, setValue] = useState(initial);

  return (
    <SearchSelect
      value={value}
      onChange={(next) => {
        setValue(next);
        onChange?.(next);
      }}
      options={options}
      label="Product"
    />
  );
}

describe('SearchSelect', () => {
  it('shows the whole list once it has focus', async () => {
    const user = userEvent.setup();
    render(<Picker />);

    await user.click(screen.getByRole('combobox', { name: 'Product' }));

    expect(screen.getAllByRole('option')).toHaveLength(3);
  });

  it('narrows on words in any order, and on the description as well as the label', async () => {
    const user = userEvent.setup();
    render(<Picker />);

    await user.click(screen.getByRole('combobox', { name: 'Product' }));
    await user.keyboard('pen blue');

    // "blue" is in the label and "pen" is too, but in the other order — a
    // substring match on the phrase would find nothing, which is exactly the
    // failure the native control's type-ahead has.
    const shown = screen.getAllByRole('option');
    expect(shown).toHaveLength(1);
    expect(shown[0]?.textContent).toContain('Blue ballpoint');
  });

  it('matches text that is searchable but not shown', async () => {
    const user = userEvent.setup();
    render(<Picker />);

    await user.click(screen.getByRole('combobox', { name: 'Product' }));
    // A barcode. Nobody wants it in the row, and everybody scanning one wants it
    // to find the product.
    await user.keyboard('5901234123457');

    expect(screen.getAllByRole('option')).toHaveLength(1);
  });

  it('reports the chosen value and shows its label afterwards', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<Picker onChange={onChange} />);

    await user.click(screen.getByRole('combobox', { name: 'Product' }));
    await user.click(screen.getByRole('option', { name: /A4 copier paper/ }));

    expect(onChange).toHaveBeenCalledWith('p3');
    expect(
      (screen.getByRole('combobox', { name: 'Product' }) as HTMLInputElement).value,
    ).toBe('B-100 — A4 copier paper');
  });

  it('can be driven from the keyboard', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<Picker onChange={onChange} />);

    await user.click(screen.getByRole('combobox', { name: 'Product' }));
    await user.keyboard('{ArrowDown}{Enter}');

    expect(onChange).toHaveBeenCalledWith('p2');
  });

  it('closes its own list on Escape, and leaves the dialog around it open', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();

    // The real pairing rather than a stand-in listener: the dialog's Escape
    // handler is on the document in the capture phase, so it runs before anything
    // inside can stop it. A picker that did not have this arrangement would shut
    // the entry form — and everything typed into it — on the key somebody pressed
    // to dismiss a dropdown.
    render(
      <Modal title="New purchase" onClose={onClose}>
        <Picker />
      </Modal>,
    );

    await user.click(screen.getByRole('combobox', { name: 'Product' }));
    expect(screen.getAllByRole('option')).toHaveLength(3);

    await user.keyboard('{Escape}');

    expect(screen.queryAllByRole('option')).toHaveLength(0);
    expect(onClose).not.toHaveBeenCalled();

    // And with the list shut, the same key does close the dialog.
    await user.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('says so rather than showing an empty box when nothing matches', async () => {
    const user = userEvent.setup();
    render(<Picker />);

    await user.click(screen.getByRole('combobox', { name: 'Product' }));
    await user.keyboard('typewriter ribbon');

    expect(screen.queryAllByRole('option')).toHaveLength(0);
    expect(screen.getByText(/nothing matches/i)).toBeTruthy();
  });

  it('offers a way back to nothing when the field is optional', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    function Clearable(): React.JSX.Element {
      const [value, setValue] = useState('p1');

      return (
        <SearchSelect
          value={value}
          onChange={(next) => {
            setValue(next);
            onChange(next);
          }}
          options={options}
          label="Product"
          clearable
          placeholder="No product"
        />
      );
    }

    render(<Clearable />);

    await user.click(screen.getByRole('combobox', { name: 'Product' }));
    await user.click(screen.getByRole('option', { name: 'No product' }));

    expect(onChange).toHaveBeenCalledWith('');
  });
});

describe('where the list is drawn', () => {
  /*
    The list is portalled to the body rather than rendered beside the field, and
    that placement is the whole reason a picker inside a dialog lands under its
    field. `position: fixed` is measured from the window only while nothing above
    it has a transform, a filter or a backdrop-filter; every dialog here has a
    blurred overlay and a panel that animates in, so a list rendered inside one was
    positioned from the panel's corner and opened three hundred pixels to the side.
  */
  it('renders outside the field it belongs to, so no ancestor can capture it', async () => {
    const user = userEvent.setup();
    const { container } = render(<Picker />);

    await user.click(screen.getByRole('combobox'));

    const list = screen.getByRole('listbox');
    expect(list).toBeTruthy();
    expect(container.contains(list)).toBe(false);
    expect(document.body.contains(list)).toBe(true);
  });

  it('escapes a dialog too, which is where it was going wrong', async () => {
    const user = userEvent.setup();
    render(
      <Modal title="New purchase" onClose={vi.fn()}>
        <Picker />
      </Modal>,
    );

    await user.click(screen.getByRole('combobox'));

    const dialog = screen.getByRole('dialog');
    const list = screen.getByRole('listbox');
    expect(dialog.contains(list)).toBe(false);
  });
});

describe('opening it again', () => {
  it('reopens on a click, having been shut with Escape', async () => {
    const user = userEvent.setup();
    render(<Picker />);

    const box = screen.getByRole('combobox');

    await user.click(box);
    expect(screen.getByRole('listbox')).toBeTruthy();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('listbox')).toBeNull();

    // Escape leaves the caret in the field, so the next click fires no focus event.
    // Without a click handler of its own the box took typing and showed nothing.
    await user.click(box);
    expect(screen.getByRole('listbox')).toBeTruthy();
  });
});

describe('a field that already knows its answer', () => {
  /*
    A required picker that opens empty asks a question it sometimes already knows the
    answer to — a firm with one unit of measure, or one the firm has named in
    Settings. What must not happen is it answering over the top of a choice somebody
    has already made.
  */
  function Defaulted({
    initial = '',
    only = false,
    preferred,
  }: {
    readonly initial?: string;
    readonly only?: boolean;
    readonly preferred?: string;
  }): React.JSX.Element {
    const [value, setValue] = useState(initial);
    const list = only ? options.slice(0, 1) : options;

    useDefaultChoice(value, list, setValue, preferred);

    return <output>{value === '' ? 'nothing chosen' : value}</output>;
  }

  it('chooses the only option there is', () => {
    render(<Defaulted only />);

    expect(screen.getByRole('status').textContent).toBe('p1');
  });

  it('asks where there is more than one and no default is named', () => {
    render(<Defaulted />);

    expect(screen.getByRole('status').textContent).toBe('nothing chosen');
  });

  it('takes the default the firm named, out of many', () => {
    render(<Defaulted preferred="p3" />);

    expect(screen.getByRole('status').textContent).toBe('p3');
  });

  it('ignores a named default the master no longer offers', () => {
    render(<Defaulted preferred="gone" />);

    expect(screen.getByRole('status').textContent).toBe('nothing chosen');
  });

  it('never writes over a choice already made', () => {
    render(<Defaulted initial="p2" only preferred="p3" />);

    expect(screen.getByRole('status').textContent).toBe('p2');
  });
});
