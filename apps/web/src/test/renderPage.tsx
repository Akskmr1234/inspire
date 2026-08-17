import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { render, type RenderResult } from '@testing-library/react';
import { ErrorBoundary } from '@/components/ErrorBoundary';
import { useSession } from '@/stores/session';
import '@/i18n';

/**
 * Renders one screen with the providers it expects and nothing else.
 *
 * A page is mounted directly rather than through `App`, so a failure names the
 * screen that failed rather than the router. The boundary is included on purpose:
 * a page that throws should be reported as a caught error by the assertion rather
 * than as an unhandled rejection three tests later.
 */
export function renderPage(element: React.ReactNode): RenderResult {
  // Retries would turn one deliberate failure into three, and a stale cache would
  // let a later test read an earlier test's fixture.
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0, staleTime: 0 },
      mutations: { retry: false },
    },
  });

  // Every column and action is exercised: a permission-gated column that never
  // renders is a column no test has looked at.
  useSession.setState({
    status: 'signedIn',
    displayName: 'Test User',
    permissions: new Set<string>(['*']),
  });

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ErrorBoundary>{element}</ErrorBoundary>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

/** Whether the boundary caught something — i.e. the screen threw while rendering. */
export function crashed(container: HTMLElement): string | null {
  const alert = container.querySelector('[role="alert"]');

  if (!alert || !alert.textContent?.includes('could not be displayed')) {
    return null;
  }

  return alert.querySelector('pre')?.textContent ?? 'threw while rendering';
}
