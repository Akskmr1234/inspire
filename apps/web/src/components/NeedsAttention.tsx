import { useQueries } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import type { ApiError } from '@/lib/api';
import { listPurchaseInvoices, PurchaseInvoiceStatus } from '@/lib/purchase';
import { listPurchaseOrders, listSalesOrders } from '@/lib/orders';
import { fetchStockValuation } from '@/lib/stock';
import { fetchPostDatedCheques } from '@/lib/cheques';

/**
 * What is waiting to be done, on the screen everybody opens first.
 *
 * The dashboard was four figures and six hundred pixels of nothing. Figures are
 * what a dashboard is usually built from, but a figure is a thing to know and this
 * application is a thing to work in — the question somebody actually opens it with
 * is "what is outstanding", and answering that turns the landing screen from a
 * report into a starting point.
 *
 * Every count here comes from an endpoint that already exists and a screen that
 * already acts on it, so each row is a number and the way to go and clear it.
 * Nothing is invented: a row appears only when its query has answered and found
 * something, and a query that fails takes only its own row with it — five separate
 * requests rather than one, precisely so that the panel degrades a row at a time
 * instead of all at once.
 */

/** One thing waiting, once its query has answered. */
interface Waiting {
  readonly key: string;
  readonly count: number;
  readonly label: string;
  readonly route: string;
  /** `warn` for what is merely pending, `alert` for what is running out. */
  readonly tone: 'warn' | 'alert' | 'info';
}

/** How long a count stays fresh. Long enough not to refetch on every visit. */
const STALE = 2 * 60 * 1000;

export function NeedsAttention(): React.JSX.Element | null {
  const { t } = useTranslation();

  const today = new Date().toISOString().slice(0, 10);
  const monthsAgo = new Date(Date.now() - 180 * 24 * 3600 * 1000)
    .toISOString()
    .slice(0, 10);

  const results = useQueries({
    queries: [
      {
        queryKey: ['attention', 'purchase-drafts'],
        queryFn: () =>
          listPurchaseInvoices(
            { status: PurchaseInvoiceStatus.draft, from: monthsAgo, to: today },
            1,
            1,
          ),
        staleTime: STALE,
        retry: false,
      },
      {
        queryKey: ['attention', 'purchase-orders'],
        queryFn: () =>
          listPurchaseOrders({ outstandingOnly: true, from: monthsAgo, to: today }, 1, 1),
        staleTime: STALE,
        retry: false,
      },
      {
        queryKey: ['attention', 'sales-orders'],
        queryFn: () =>
          listSalesOrders({ outstandingOnly: true, from: monthsAgo, to: today }, 1, 1),
        staleTime: STALE,
        retry: false,
      },
      {
        queryKey: ['attention', 'reorder'],
        queryFn: () => fetchStockValuation('', '', false),
        staleTime: STALE,
        retry: false,
      },
      {
        queryKey: ['attention', 'cheques'],
        queryFn: () => fetchPostDatedCheques(today),
        staleTime: STALE,
        retry: false,
      },
    ],
  });

  const [drafts, purchaseOrders, salesOrders, valuation, cheques] = results;

  const waiting: readonly Waiting[] = [
    {
      key: 'drafts',
      count: drafts.data?.totalCount ?? 0,
      label: t('attention.purchaseDrafts', { count: drafts.data?.totalCount ?? 0 }),
      route: '/purchase/invoices',
      tone: 'warn',
    },
    {
      key: 'purchase-orders',
      count: purchaseOrders.data?.totalCount ?? 0,
      label: t('attention.purchaseOrders', {
        count: purchaseOrders.data?.totalCount ?? 0,
      }),
      route: '/purchase/orders',
      tone: 'info',
    },
    {
      key: 'sales-orders',
      count: salesOrders.data?.totalCount ?? 0,
      label: t('attention.salesOrders', { count: salesOrders.data?.totalCount ?? 0 }),
      route: '/sales/orders',
      tone: 'info',
    },
    {
      key: 'reorder',
      // Counted here rather than asked for: the valuation already carries the flag
      // per row, and there is no endpoint that answers "how many are low" on its own.
      count: (valuation.data?.rows ?? []).filter((row) => row.isBelowReorderLevel).length,
      label: t('attention.belowReorder', {
        count: (valuation.data?.rows ?? []).filter((row) => row.isBelowReorderLevel)
          .length,
      }),
      route: '/inventory/valuation',
      tone: 'alert',
    },
    {
      key: 'cheques',
      count: cheques.data?.cheques.length ?? 0,
      label: t('attention.chequesDue', { count: cheques.data?.cheques.length ?? 0 }),
      route: '/accounting/post-dated-cheques',
      tone: 'warn',
    },
  ];

  const shown = waiting.filter((row) => row.count > 0);
  const settled = results.every((result) => !result.isPending);

  // Nothing while the counts are still arriving, and nothing when they all come back
  // zero: a panel headed "needs attention" that lists nothing is a panel saying "you
  // are up to date" in the most roundabout way available, and it costs a section of
  // the screen to do it.
  if (!settled || shown.length === 0) {
    return null;
  }

  return (
    <section className="card card-body space-y-3">
      <header>
        <h2 className="text-sm font-semibold tracking-tight text-ink">
          {t('attention.title')}
        </h2>
        <p className="mt-0.5 text-xs text-ink-muted">{t('attention.hint')}</p>
      </header>

      <ul className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
        {shown.map((row) => (
          <li key={row.key}>
            <Link
              to={row.route}
              className="group flex items-center gap-3 rounded-lg border border-line bg-surface-2 px-3 py-2.5 transition hover:border-line-strong hover:bg-surface-3"
            >
              <span
                className={clsx(
                  'grid size-9 shrink-0 place-items-center rounded-lg font-mono text-sm font-semibold tabular-nums',
                  row.tone === 'alert' &&
                    'bg-red-50 text-red-700 dark:bg-red-500/15 dark:text-red-300',
                  row.tone === 'warn' &&
                    'bg-amber-50 text-amber-800 dark:bg-amber-500/15 dark:text-amber-200',
                  row.tone === 'info' &&
                    'bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-200',
                )}
              >
                {row.count > 99 ? '99+' : row.count}
              </span>

              <span className="min-w-0 flex-1 text-sm text-ink">{row.label}</span>

              <span
                aria-hidden="true"
                className="shrink-0 text-ink-subtle transition group-hover:text-ink rtl:rotate-180"
              >
                ›
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Re-exported so a caller can type the failures it wants to ignore. */
export type { ApiError };
