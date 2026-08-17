import { Component, type ErrorInfo, type ReactNode } from 'react';

/**
 * Catches a render-time throw from the screen beneath it.
 *
 * Without one of these, React unmounts the whole tree when any component throws:
 * the navigation, the header and the route all disappear together and the user is
 * left on a blank white page with nothing to click and no indication of what
 * happened. That is not a hypothetical — it is what this application did when a
 * single numeric field came back absent and a `toFixed` ran on undefined.
 *
 * The failure modes it is actually for are the ones an ERP meets in the field: a
 * server that adds or renames a field ahead of the client, a report row whose
 * optional figure is null for one branch only, a stale cached response after a
 * deployment. In every case one screen is broken and the rest of the application
 * is fine, so only the screen should break.
 *
 * A class rather than a hook because there is still no hook equivalent —
 * `componentDidCatch` and `getDerivedStateFromError` have no function-component
 * counterpart.
 */
interface ErrorBoundaryProps {
  readonly children: ReactNode;
  /**
   * Changes when the user navigates. The boundary clears itself when this differs
   * from the value it failed on, so moving to another screen recovers without a
   * reload — otherwise the error would outlive the route that caused it.
   */
  readonly resetKey?: string;
  /** Somewhere to send the failure: a logger, an error reporter. */
  readonly onError?: (error: Error, info: ErrorInfo) => void;
}

interface ErrorBoundaryState {
  readonly error: Error | null;
  readonly resetKey: string | undefined;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  override state: ErrorBoundaryState = { error: null, resetKey: undefined };

  static getDerivedStateFromError(error: Error): Partial<ErrorBoundaryState> {
    return { error };
  }

  /**
   * Clears the error when the route changes.
   *
   * Done here rather than in an effect because a boundary in the failed state
   * renders no children, so a child effect would never run to release it.
   */
  static getDerivedStateFromProps(
    props: ErrorBoundaryProps,
    state: ErrorBoundaryState,
  ): Partial<ErrorBoundaryState> | null {
    if (state.error !== null && state.resetKey !== props.resetKey) {
      return { error: null, resetKey: props.resetKey };
    }

    if (state.resetKey !== props.resetKey) {
      return { resetKey: props.resetKey };
    }

    return null;
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    // Logged unconditionally. A boundary that swallows the stack turns a
    // reproducible bug into a user saying "it showed an error page".
    console.error(
      'Unhandled error while rendering a screen:',
      error,
      info.componentStack,
    );
    this.props.onError?.(error, info);
  }

  override render(): ReactNode {
    const { error } = this.state;

    if (!error) {
      return this.props.children;
    }

    return (
      <section className="page" role="alert">
        <div className="card animate-pop mx-auto max-w-2xl p-6 sm:p-8">
          <div className="flex items-start gap-4">
            <div className="grid size-11 shrink-0 place-items-center rounded-full bg-red-50 text-red-600 dark:bg-red-500/12 dark:text-red-400">
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth={1.8}
                strokeLinecap="round"
                className="size-5"
                aria-hidden="true"
              >
                <path d="M12 8v5" />
                <path d="M12 16.5v.01" />
                <path d="M10.3 3.9 2.4 17.4A1.9 1.9 0 0 0 4 20.3h16a1.9 1.9 0 0 0 1.6-2.9L13.7 3.9a1.9 1.9 0 0 0-3.4 0z" />
              </svg>
            </div>

            <div className="min-w-0 flex-1">
              <h1 className="text-lg font-semibold tracking-tight text-ink">
                This screen could not be displayed
              </h1>
              <p className="mt-1 text-sm text-ink-muted">
                The rest of the application is unaffected — the navigation still works,
                and nothing you have entered elsewhere has been lost.
              </p>

              {/*
                The message is shown rather than hidden behind "contact support". An
                ERP is run by people who will paste this into a ticket, and a
                specific line beats "something went wrong" every time.
              */}
              <pre className="mt-4 overflow-x-auto rounded-lg border border-line bg-surface-2 p-3 font-mono text-xs whitespace-pre-wrap text-ink-muted">
                {error.message || String(error)}
              </pre>

              <div className="mt-5 flex flex-wrap gap-2">
                <button
                  type="button"
                  onClick={() => this.setState({ error: null })}
                  className="btn-primary"
                >
                  Try again
                </button>
                <button
                  type="button"
                  onClick={() => window.location.reload()}
                  className="btn-secondary"
                >
                  Reload the page
                </button>
              </div>
            </div>
          </div>
        </div>
      </section>
    );
  }
}
