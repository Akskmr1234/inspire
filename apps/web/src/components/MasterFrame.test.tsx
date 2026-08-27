import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '@/i18n';
import { setMatchingMedia } from '@/test/setup';
import { renderPage } from '@/test/renderPage';
import { MasterFrame, MasterField } from '@/components/MasterFrame';
import { ApiError } from '@/lib/api';

/*
  What the masters are for is the list. The fields that add to one used to sit above
  it permanently — seven of them on the customer master — so the thing being read
  was read through whatever the form left over. They live in a dialog now, and these
  are the parts of that move that would be silently wrong rather than visibly broken:
  a form still on the page, a dialog that outlives the record it created, or one that
  closes over the message explaining why it failed.
*/

vi.mock('@/lib/grid', () => ({
  fetchGridLayout: vi.fn(async () => null),
  saveGridLayout: vi.fn(async () => undefined),
  resetGridLayout: vi.fn(async () => undefined),
}));

interface Row {
  readonly id: string;
  readonly code: string;
}

const rows: readonly Row[] = [
  { id: '1', code: 'C-001' },
  { id: '2', code: 'C-002' },
];

/** A master whose add form is one field and a button, standing in for six real ones. */
function frame(create: () => Promise<void>) {
  return (
    <MasterFrame<Row>
      title="Customers"
      addTitle="New customer"
      queryKey="test-customers"
      fetchRows={async () => rows}
      columns={() => [{ key: 'code', header: 'Code', value: (row) => row.code }]}
      rowKey={(row) => row.id}
      addForm={(run, busy) => (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            run(create);
          }}
        >
          <MasterField label="Code" value="" onChange={() => undefined} />
          <button type="submit" disabled={busy}>
            Add
          </button>
        </form>
      )}
    />
  );
}

beforeEach(() => {
  // Desktop, so the grid renders as a table rather than as cards.
  setMatchingMedia();
});

describe('the add form', () => {
  it('is not on the page until it is asked for', async () => {
    renderPage(frame(async () => undefined));

    await waitFor(() => expect(screen.getByText('C-001')).toBeTruthy());

    // The whole content area is the list. Nothing of the form is taking room from
    // it — not the fields, and not a row holding a lone submit button either.
    expect(screen.queryByLabelText('Code')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Add' })).toBeNull();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('opens in a dialog from the button beside the title', async () => {
    const user = userEvent.setup();
    renderPage(frame(async () => undefined));

    await waitFor(() => expect(screen.getByText('C-001')).toBeTruthy());
    await user.click(screen.getByRole('button', { name: 'New customer' }));

    const dialog = screen.getByRole('dialog');

    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(screen.getByRole('button', { name: 'Add' })).toBeTruthy();
  });

  it('closes once the record is created, since the list behind it now says so', async () => {
    const user = userEvent.setup();
    renderPage(frame(async () => undefined));

    await waitFor(() => expect(screen.getByText('C-001')).toBeTruthy());
    await user.click(screen.getByRole('button', { name: 'New customer' }));
    await user.click(screen.getByRole('button', { name: 'Add' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('stays open on a refusal, with the reason in front of the fields', async () => {
    const user = userEvent.setup();
    const refuse = async (): Promise<void> => {
      throw new ApiError(409, 'Master.CodeTaken', 'That code is already in use.');
    };

    renderPage(frame(refuse));

    await waitFor(() => expect(screen.getByText('C-001')).toBeTruthy());
    await user.click(screen.getByRole('button', { name: 'New customer' }));
    await user.click(screen.getByRole('button', { name: 'Add' }));

    // Behind the dialog the message would be unreadable, and closing the dialog to
    // show it would take the fields away from the person about to correct them.
    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy());

    const dialog = screen.getByRole('dialog');

    expect(dialog.textContent).toContain('That code is already in use.');
    expect(screen.getByRole('button', { name: 'Add' })).toBeTruthy();
  });

  it('is dismissed by Escape, like every other dialog here', async () => {
    const user = userEvent.setup();
    renderPage(frame(async () => undefined));

    await waitFor(() => expect(screen.getByText('C-001')).toBeTruthy());
    await user.click(screen.getByRole('button', { name: 'New customer' }));
    await user.keyboard('{Escape}');

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });
});
