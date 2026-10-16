import { Navigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ReportSkeleton } from '@/components/ReportFrame';
import type { ApiError } from '@/lib/api';
import { fetchMenu, type Menu, type MenuEntry } from '@/lib/menu';

/** The first entry in the tree that opens a screen. */
function firstScreen(entries: readonly MenuEntry[]): string | null {
  for (const entry of entries) {
    if (entry.route) return entry.route;

    const below = firstScreen(entry.children);
    if (below) return below;
  }

  return null;
}

/**
 * Where somebody lands: the first screen their menu offers.
 *
 * Every sign-in used to land on the trial balance, so a cashier whose role reaches only
 * sales invoices opened the app on a refusal. The menu is what the server says this
 * user may see, so the first thing in it is somewhere they can go - the dashboard, for
 * most roles, because it is the first entry of the menu.
 */
export function HomeRedirect(): React.JSX.Element {
  const menu = useQuery<Menu, ApiError>({
    queryKey: ['menu'],
    queryFn: fetchMenu,
    staleTime: 5 * 60 * 1000,
  });

  if (menu.isPending) {
    return (
      <div className="page">
        <ReportSkeleton rows={5} />
      </div>
    );
  }

  return <Navigate to={firstScreen(menu.data?.items ?? []) ?? '/settings'} replace />;
}
