import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ErrorBoundary } from '@/components/ErrorBoundary';

/*
  The boundary exists because of a specific incident: one absent numeric field threw
  during render and React unmounted the entire tree, leaving a blank page with no
  navigation and no way back. These pin the three behaviours that turn that into a
  contained failure.
*/

function Boom({ fail }: { readonly fail: boolean }): React.JSX.Element {
  if (fail) {
    // The real one was `undefined.toLocaleString()` inside a report total.
    throw new Error("Cannot read properties of undefined (reading 'toLocaleString')");
  }

  return <p>the screen</p>;
}

let consoleError: ReturnType<typeof vi.spyOn>;

beforeEach(() => {
  // React logs the caught error itself. Silenced so a passing run reads as passing,
  // and restored afterwards so a genuine warning elsewhere is still visible.
  consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  consoleError.mockRestore();
});

describe('a screen that throws', () => {
  it('is contained rather than taking the application down', () => {
    render(
      <div>
        <nav>the navigation</nav>
        <ErrorBoundary>
          <Boom fail />
        </ErrorBoundary>
      </div>,
    );

    // The whole point: everything outside the boundary is still standing.
    expect(screen.getByText('the navigation')).toBeTruthy();
    expect(screen.getByRole('alert')).toBeTruthy();
  });

  it('shows the actual message, not a shrug', () => {
    render(
      <ErrorBoundary>
        <Boom fail />
      </ErrorBoundary>,
    );

    // People paste this into a ticket. "Something went wrong" wastes the round trip.
    expect(screen.getByText(/toLocaleString/)).toBeTruthy();
  });

  it('clears when the route changes, so walking away recovers', () => {
    const { rerender } = render(
      <ErrorBoundary resetKey="/accounting/trial-balance">
        <Boom fail />
      </ErrorBoundary>,
    );

    expect(screen.getByRole('alert')).toBeTruthy();

    rerender(
      <ErrorBoundary resetKey="/accounting/day-book">
        <Boom fail={false} />
      </ErrorBoundary>,
    );

    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.getByText('the screen')).toBeTruthy();
  });

  it('retries the same route when asked', async () => {
    const user = userEvent.setup();

    // Steered from outside rather than by counting renders. React re-renders a
    // failed subtree in development to collect the component stack, so a component
    // that flips its own flag on first render is not deterministic here.
    let failing = true;

    function Flaky(): React.JSX.Element {
      if (failing) {
        throw new Error('transient');
      }

      return <p>recovered</p>;
    }

    render(
      <ErrorBoundary resetKey="/same">
        <Flaky />
      </ErrorBoundary>,
    );

    expect(screen.getByRole('alert')).toBeTruthy();

    failing = false;
    await user.click(screen.getByRole('button', { name: /try again/i }));

    expect(screen.getByText('recovered')).toBeTruthy();
  });

  it('reports the failure to whatever is listening', () => {
    const onError = vi.fn();

    render(
      <ErrorBoundary onError={onError}>
        <Boom fail />
      </ErrorBoundary>,
    );

    expect(onError).toHaveBeenCalledOnce();
    expect(onError.mock.calls[0]?.[0]).toBeInstanceOf(Error);
  });
});

describe('a screen that does not throw', () => {
  it('is rendered untouched', () => {
    render(
      <ErrorBoundary>
        <Boom fail={false} />
      </ErrorBoundary>,
    );

    expect(screen.getByText('the screen')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
