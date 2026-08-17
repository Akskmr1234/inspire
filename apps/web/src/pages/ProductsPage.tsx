import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { ReportFrame } from '@/components/ReportFrame';
import { ProductEditor } from '@/pages/ProductEditor';
import type { ApiError } from '@/lib/api';
import {
  listMaster,
  type BrandSummary,
  type CategorySummary,
  type UnitSummary,
} from '@/lib/inventory';
import { createProduct, listProducts, type ProductSummary } from '@/lib/products';

/**
 * The product master.
 *
 * The list is searched on the server, unlike the other masters. A chart of accounts is
 * a few hundred rows and the grid filters it instantly; a product master runs to tens
 * of thousands, and the slow part would be sending them all so the browser could
 * discard all but three. The search reaches barcodes as well as codes and
 * descriptions, because on a counter the label is scanned and the number on it is
 * frequently not the code in the master.
 */
export function ProductsPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  const [search, setSearch] = useState('');
  const [applied, setApplied] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [includeInactive, setIncludeInactive] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const query = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', applied, categoryId, includeInactive],
    queryFn: () => listProducts(applied, categoryId, includeInactive),
  });

  // Shared cache keys with the master screens, so opening this page after those does
  // not refetch what they already hold.
  const categories = useQuery<readonly CategorySummary[], ApiError>({
    queryKey: ['categories', false],
    queryFn: () => listMaster<CategorySummary>('categories', false),
  });

  const mutation = useMutation<string, ApiError, () => Promise<string>>({
    mutationFn: (action) => action(),
    onSuccess: async (id) => {
      setError(null);
      setAdding(false);
      await queryClient.invalidateQueries({ queryKey: ['products'] });

      // Straight into the editor. A product created from the minimum fields still
      // needs its rates and its levels, and making somebody find it again first is
      // how a master fills up with half-entered records.
      setEditingId(id);
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const columns: readonly GridColumn<ProductSummary>[] = [
    {
      key: 'code',
      header: t('masters.code'),
      value: (row) => row.code,
      render: (row) => (
        <button
          type="button"
          onClick={() => setEditingId(row.id)}
          className="font-mono text-brand-700 underline-offset-2 hover:underline dark:text-brand-100"
        >
          {row.code}
        </button>
      ),
    },
    {
      key: 'description',
      header: t('products.description'),
      value: (row) => row.description,
    },
    {
      key: 'descriptionArabic',
      header: t('products.descriptionArabic'),
      value: (row) => row.descriptionArabic ?? '',
      hiddenByDefault: true,
    },
    { key: 'category', header: t('products.category'), value: (row) => row.categoryName },
    {
      key: 'brand',
      header: t('products.brand'),
      value: (row) => row.brandName ?? '',
      hiddenByDefault: true,
    },
    { key: 'unit', header: t('products.stockUnit'), value: (row) => row.stockUnitCode },
    {
      key: 'cost',
      header: t('products.cost'),
      value: (row) => row.cost,
      numeric: true,
      render: (row) => row.cost.toFixed(2),
      // Cost is what the firm pays, and plenty of people who may see a price list
      // may not see it. A courtesy rather than a boundary — the figure is already in
      // the response — but the right default.
      requiredPermission: 'inventory:product:edit',
    },
    {
      key: 'retail',
      header: t('products.retailRate'),
      value: (row) => row.retailRate,
      numeric: true,
      render: (row) => row.retailRate.toFixed(2),
    },
    {
      key: 'reorder',
      header: t('products.reorderLevel'),
      value: (row) => row.reorderLevel,
      numeric: true,
      hiddenByDefault: true,
    },
    {
      key: 'barcodes',
      header: t('products.barcodes'),
      value: (row) => row.barcodeCount,
      numeric: true,
    },
    {
      key: 'tracking',
      header: t('products.tracking'),
      value: (row) =>
        [
          row.tracksBatches ? t('products.batchShort') : '',
          row.tracksSerialNumbers ? t('products.serialShort') : '',
        ]
          .filter(Boolean)
          .join(' · '),
    },
    {
      key: 'status',
      header: t('masters.status'),
      // Three states rather than two: a discontinued product is still on the shelf
      // and still sold, which "withdrawn" would misreport.
      value: (row) =>
        !row.isActive
          ? t('masters.withdrawn')
          : row.isDiscontinued
            ? t('products.discontinued')
            : t('masters.active'),
    },
  ];

  const controls = (
    <form
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();
        setApplied(search);
      }}
    >
      <label className="field">
        <span className="field-label">{t('products.search')}</span>
        <input
          type="search"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder={t('products.searchHint')}
          className="field-input-sm w-full sm:w-64"
        />
      </label>

      <label className="field">
        <span className="field-label">{t('products.category')}</span>
        <select
          value={categoryId}
          onChange={(event) => setCategoryId(event.target.value)}
          className="field-input-sm"
        >
          <option value="">{t('products.allCategories')}</option>
          {(categories.data ?? []).map((row) => (
            <option key={row.id} value={row.id}>
              {row.name}
            </option>
          ))}
        </select>
      </label>

      <label className="flex items-center gap-2 pb-1 text-sm">
        <input
          type="checkbox"
          checked={includeInactive}
          onChange={(event) => setIncludeInactive(event.target.checked)}
        />
        {t('masters.includeWithdrawn')}
      </label>

      <button type="submit" className="btn-primary">
        {t('products.find')}
      </button>
    </form>
  );

  if (editingId) {
    return <ProductEditor productId={editingId} onClose={() => setEditingId(null)} />;
  }

  return (
    <ReportFrame title={t('nav.products')} controls={controls} query={query}>
      {(rows) => (
        <div className="space-y-4">
          {error && (
            <div role="alert" className="alert-error">
              {error}
            </div>
          )}

          {adding ? (
            <AddProduct
              categories={categories.data ?? []}
              busy={mutation.isPending}
              onCancel={() => setAdding(false)}
              onSubmit={(body) => {
                setError(null);
                mutation.mutate(() => createProduct(body));
              }}
            />
          ) : (
            <button type="button" onClick={() => setAdding(true)} className="btn-primary">
              {t('products.new')}
            </button>
          )}

          <DataGrid
            gridKey="products"
            rows={rows}
            columns={columns}
            rowKey={(row) => row.id}
            emptyMessage={t('products.noneFound')}
          />
        </div>
      )}
    </ReportFrame>
  );
}

/**
 * The add form.
 *
 * Deliberately short. A product needs a description, a category and a unit to exist at
 * all; everything else has a sensible default and belongs on the editor, where there
 * is room to explain it. Asking for forty fields before the record exists is how a
 * master ends up with rows somebody abandoned halfway.
 */
function AddProduct({
  categories,
  busy,
  onCancel,
  onSubmit,
}: {
  readonly categories: readonly CategorySummary[];
  readonly busy: boolean;
  readonly onCancel: () => void;
  readonly onSubmit: (body: object) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const units = useQuery<readonly UnitSummary[], ApiError>({
    queryKey: ['units', false],
    queryFn: () => listMaster<UnitSummary>('units', false),
  });

  const brands = useQuery<readonly BrandSummary[], ApiError>({
    queryKey: ['brands', false],
    queryFn: () => listMaster<BrandSummary>('brands', false),
  });

  const [code, setCode] = useState('');
  const [description, setDescription] = useState('');
  const [descriptionArabic, setDescriptionArabic] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [stockUnitId, setStockUnitId] = useState('');
  const [brandId, setBrandId] = useState('');
  const [itemType, setItemType] = useState('1');

  return (
    <form
      className="panel animate-drop space-y-3"
      onSubmit={(event) => {
        event.preventDefault();

        if (!description.trim() || !categoryId || !stockUnitId) {
          return;
        }

        onSubmit({
          // Blank is not an omission here — it is the instruction to issue the next
          // code in the firm's own sequence.
          code: code.trim() || null,
          description: description.trim(),
          descriptionArabic: descriptionArabic.trim() || null,
          categoryId,
          stockUnitId,
          brandId: brandId || null,
          itemType: Number(itemType),
        });
      }}
    >
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <label className="block">
          <span className="field-label">{t('masters.code')}</span>
          <input
            value={code}
            onChange={(event) => setCode(event.target.value)}
            placeholder={t('products.codeAuto')}
            className="field-input"
          />
        </label>

        <label className="block">
          <span className="field-label">{t('products.description')}</span>
          <input
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            className="field-input"
            required
          />
        </label>

        <label className="block">
          <span className="field-label">{t('products.descriptionArabic')}</span>
          <input
            dir="rtl"
            value={descriptionArabic}
            onChange={(event) => setDescriptionArabic(event.target.value)}
            className="field-input"
          />
        </label>

        <label className="block">
          <span className="field-label">{t('products.itemType')}</span>
          <select
            value={itemType}
            onChange={(event) => setItemType(event.target.value)}
            className="field-input"
          >
            <option value="1">{t('products.itemStock')}</option>
            <option value="2">{t('products.itemService')}</option>
            <option value="3">{t('products.itemNonStock')}</option>
          </select>
        </label>

        <label className="block">
          <span className="field-label">{t('products.category')}</span>
          <select
            value={categoryId}
            onChange={(event) => setCategoryId(event.target.value)}
            className="field-input"
            required
          >
            <option value="">{t('products.choose')}</option>
            {categories.map((row) => (
              <option key={row.id} value={row.id}>
                {row.code} — {row.name}
              </option>
            ))}
          </select>
        </label>

        <label className="block">
          <span className="field-label">{t('products.stockUnit')}</span>
          <select
            value={stockUnitId}
            onChange={(event) => setStockUnitId(event.target.value)}
            className="field-input"
            required
          >
            <option value="">{t('products.choose')}</option>
            {(units.data ?? []).map((row) => (
              <option key={row.id} value={row.id}>
                {row.code} — {row.name}
              </option>
            ))}
          </select>
        </label>

        <label className="block">
          <span className="field-label">{t('products.brand')}</span>
          <select
            value={brandId}
            onChange={(event) => setBrandId(event.target.value)}
            className="field-input"
          >
            <option value="">{t('products.noBrand')}</option>
            {(brands.data ?? []).map((row) => (
              <option key={row.id} value={row.id}>
                {row.name}
              </option>
            ))}
          </select>
        </label>
      </div>

      <div className="flex gap-3">
        <button type="submit" disabled={busy} className="btn-primary">
          {t('masters.add')}
        </button>
        <button type="button" onClick={onCancel} className="btn-secondary">
          {t('products.cancel')}
        </button>
      </div>
    </form>
  );
}
