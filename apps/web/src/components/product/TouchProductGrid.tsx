import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { IconSearch } from '@/components/icons';
import type { ProductSummary } from '@/lib/products';
import type { SalesRateType } from '@/stores/settings';
import { moneyAlways } from '@/lib/money';

export interface TouchProductGridProps {
  readonly products: readonly ProductSummary[];
  readonly onSelectProduct: (product: ProductSummary) => void;
  readonly quantitiesInCart?: ReadonlyMap<string, number>;
  readonly currency?: string;
  readonly rateType?: SalesRateType;
  readonly masterRate?: (product: ProductSummary, rateType: SalesRateType) => number;
  readonly onHand?: ReadonlyMap<string, number>;
  /** Each product's small picture, by product id; products without one show their initials. */
  readonly thumbnails?: Readonly<Record<string, string>>;
  readonly className?: string;
}

/**
 * Touch-optimized product grid with category pills and instant search for POS counter checkout.
 * Allows cashiers to tap product tiles to add them directly to invoice lines.
 */
export function TouchProductGrid({
  products,
  onSelectProduct,
  quantitiesInCart = new Map(),
  rateType = 'retail',
  masterRate,
  onHand = new Map(),
  thumbnails = {},
  className,
}: TouchProductGridProps): React.JSX.Element {
  const { t, i18n } = useTranslation();
  const [selectedCategory, setSelectedCategory] = useState<string>('all');
  const [search, setSearch] = useState<string>('');

  const isRtl = i18n.dir() === 'rtl' || i18n.language === 'ar';

  // Group unique categories present in the offered products
  const categories = useMemo(() => {
    const map = new Map<string, { id: string; name: string; count: number }>();
    for (const p of products) {
      const id = p.categoryId || 'uncategorized';
      const name = p.categoryName || t('common.uncategorized');
      const existing = map.get(id);
      if (existing) {
        existing.count++;
      } else {
        map.set(id, { id, name, count: 1 });
      }
    }
    return Array.from(map.values()).sort((a, b) => a.name.localeCompare(b.name));
  }, [products, t]);

  // Filter products by selected category and search input
  const filteredProducts = useMemo(() => {
    let list = products;
    if (selectedCategory !== 'all') {
      list = list.filter((p) => (p.categoryId || 'uncategorized') === selectedCategory);
    }
    if (search.trim()) {
      const term = search.trim().toLowerCase();
      list = list.filter(
        (p) =>
          p.code.toLowerCase().includes(term) ||
          p.description.toLowerCase().includes(term) ||
          (p.descriptionArabic && p.descriptionArabic.toLowerCase().includes(term)),
      );
    }
    return list;
  }, [products, selectedCategory, search]);

  const getRate = (product: ProductSummary): number => {
    if (masterRate) {
      return masterRate(product, rateType);
    }
    if (rateType === 'wholesale') return product.wholesaleRate || product.retailRate;
    if (rateType === 'mrp') return product.maximumRetailPrice || product.retailRate;
    return product.retailRate;
  };

  return (
    <div className={clsx('flex flex-col gap-2.5 h-full min-h-0', className)}>
      {/* Search Bar */}
      <div className="relative">
        <IconSearch className="pointer-events-none absolute start-3 top-1/2 -translate-y-1/2 size-4 text-ink-muted" />
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t('sales.searchCatalog')}
          className="field-input-sm w-full ps-9 pe-8 bg-surface text-xs"
        />
        {search && (
          <button
            type="button"
            onClick={() => setSearch('')}
            className="absolute end-2.5 top-1/2 -translate-y-1/2 text-xs text-ink-muted hover:text-ink"
            aria-label={t('common.clear')}
          >
            ×
          </button>
        )}
      </div>

      {/* Category Pills */}
      {categories.length > 0 && (
        <div
          role="tablist"
          aria-label={t('categories.title')}
          className="flex items-center gap-1.5 overflow-x-auto pb-1 text-xs no-scrollbar"
        >
          <button
            type="button"
            role="tab"
            aria-selected={selectedCategory === 'all'}
            onClick={() => setSelectedCategory('all')}
            className={clsx(
              'px-2.5 py-1 rounded-full whitespace-nowrap font-medium transition-colors text-xs shrink-0',
              selectedCategory === 'all'
                ? 'bg-brand-500 text-white shadow-2xs'
                : 'bg-surface-2 text-ink-muted hover:text-ink hover:bg-surface-3',
            )}
          >
            {t('sales.allCategories')} ({products.length})
          </button>
          {categories.map((cat) => (
            <button
              key={cat.id}
              type="button"
              role="tab"
              aria-selected={selectedCategory === cat.id}
              onClick={() => setSelectedCategory(cat.id)}
              className={clsx(
                'px-2.5 py-1 rounded-full whitespace-nowrap font-medium transition-colors text-xs shrink-0',
                selectedCategory === cat.id
                  ? 'bg-brand-500 text-white shadow-2xs'
                  : 'bg-surface-2 text-ink-muted hover:text-ink hover:bg-surface-3',
              )}
            >
              {cat.name} ({cat.count})
            </button>
          ))}
        </div>
      )}

      {/* Product Tiles Grid */}
      <div className="flex-1 min-h-0 overflow-y-auto pe-1">
        {filteredProducts.length === 0 ? (
          <div className="flex flex-col items-center justify-center p-8 text-center text-xs text-ink-muted">
            <p className="font-medium text-ink">{t('sales.noProductsFound')}</p>
            <p className="mt-1 text-[11px]">{t('sales.noProductsCategory')}</p>
          </div>
        ) : (
          <div className="grid grid-cols-2 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-3 2xl:grid-cols-4 gap-2">
            {filteredProducts.map((product) => {
              const inCart = quantitiesInCart.get(product.id) ?? 0;
              const stock = onHand.get(product.id) ?? product.stockQuantity;
              const rate = getRate(product);
              const displayName =
                isRtl && product.descriptionArabic
                  ? product.descriptionArabic
                  : product.description;

              return (
                <button
                  key={product.id}
                  type="button"
                  onClick={() => onSelectProduct(product)}
                  className={clsx(
                    'group relative flex flex-col justify-between p-2.5 rounded-xl border text-start transition-all duration-150 select-none shadow-2xs hover:shadow-sm active:scale-[0.97] min-h-[92px]',
                    inCart > 0
                      ? 'border-brand-500 bg-brand-500/8 ring-1 ring-brand-500/30'
                      : 'border-line bg-surface hover:bg-surface-2 hover:border-line-strong',
                  )}
                  title={`${displayName} (${product.code})`}
                >
                  {/* Top: thumbnail, description and inCart Badge */}
                  <div className="flex items-start justify-between gap-1.5 w-full">
                    <span className="flex min-w-0 items-start gap-2">
                      {thumbnails[product.id] ? (
                        <img
                          src={thumbnails[product.id]}
                          alt=""
                          loading="lazy"
                          className="size-9 shrink-0 rounded-md border border-line bg-white object-cover"
                        />
                      ) : (
                        <span
                          aria-hidden="true"
                          className="grid size-9 shrink-0 place-items-center rounded-md bg-surface-2 text-[11px] font-bold text-ink-subtle uppercase"
                        >
                          {displayName.trim().slice(0, 2)}
                        </span>
                      )}
                      <span className="line-clamp-2 text-xs font-medium text-ink leading-snug group-hover:text-brand-600 dark:group-hover:text-brand-400">
                        {displayName}
                      </span>
                    </span>
                    {inCart > 0 && (
                      <span
                        className="shrink-0 badge bg-brand-500 text-white font-mono text-[10px] font-bold px-1.5 py-0.5 rounded-full shadow-2xs"
                        title={`${inCart} ${t('sales.inCart')}`}
                      >
                        ×{inCart}
                      </span>
                    )}
                  </div>

                  {/* Bottom: Code, Price, Stock */}
                  <div className="mt-2 flex flex-col gap-0.5 w-full border-t border-line/60 pt-1.5">
                    <div className="flex items-baseline justify-between gap-1">
                      <span className="font-mono text-[10px] text-ink-subtle truncate max-w-[50%]">
                        {product.code}
                      </span>
                      <span className="font-mono text-xs font-bold text-ink tabular-nums">
                        {moneyAlways(rate)}
                      </span>
                    </div>

                    <div className="flex items-center justify-between text-[10px]">
                      {stock > 0 ? (
                        <span className="text-emerald-700 dark:text-emerald-400 font-medium">
                          {stock} {t('sales.inStock')}
                        </span>
                      ) : (
                        <span className="text-amber-700 dark:text-amber-400 font-medium">
                          {t('sales.outOfStock')}
                        </span>
                      )}
                      {product.stockUnitCode && (
                        <span className="text-ink-subtle">{product.stockUnitCode}</span>
                      )}
                    </div>
                  </div>
                </button>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
