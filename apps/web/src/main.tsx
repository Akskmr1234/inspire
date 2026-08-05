import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App } from '@/App';
import '@/i18n';
import '@/index.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Financial figures must not be quietly stale: an accountant refreshing a
      // trial balance expects the current position, not a cached one from ten
      // minutes ago.
      staleTime: 0,
      refetchOnWindowFocus: false,

      // The client already retries a 401 once via the token-refresh path.
      // Retrying a 4xx here would just repeat a request the server has already
      // refused on its merits.
      retry: (failureCount, error) => {
        const status = (error as { status?: number }).status;

        if (status !== undefined && status >= 400 && status < 500) {
          return false;
        }

        return failureCount < 2;
      },
    },
  },
});

const container = document.getElementById('root');

if (!container) {
  throw new Error('The #root element is missing from index.html.');
}

createRoot(container).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
