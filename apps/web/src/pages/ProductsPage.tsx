import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { Modal } from '@/components/Modal';
import { ReportFrame } from '@/components/ReportFrame';
import { CheckField, Field, SelectField, TextField } from '@/components/Form';
import { SearchSelect, useDefaultChoice } from '@/components/SearchSelect';
import { StatusBadge } from '@/components/StatusBadge';
import { ArabicNameField } from '@/components/ArabicNameField';
import { ProductEditor } from '@/pages/ProductEditor';
import type { ApiError } from '@/lib/api';
import {
  listMaster,
  type BrandSummary,
  type CategorySummary,
  type UnitSummary,
} from '@/lib/inventory';
import { createProduct, listProducts, type ProductSummary } from '@/lib/products';
import { collect, maxLength, required, useValidation } from '@/lib/validation';
import { useSettings } from '@/stores/settings';
import { moneyAlways } from '@/lib/money';

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
    onSuccess: async () => {
      setError(null);
      setAdding(false);

      /*
        Back to the list, as every other master here does on a save.

        This used to open the new product's editor, on the reasoning that a product
        created from the minimum fields still wants its rates and its levels. That
        is true, and it is still one click away — but it made this one master behave
        unlike all the others, so somebody adding six products in a row was thrown
        into an editor and had to find their way back out five times.
      */
      await queryClient.invalidateQueries({ queryKey: ['products'] });
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const columns: readonly GridColumn<ProductSummary>[] = [
    {
      key: 'code',
      header: t('masters.code'),
      value: (row) => row.code,
      render: (row) => (
        <button type="button" onClick={() => setEditingId(row.id)} className="cell-link">
          {row.code}
        </button>
      ),
    },
    {
      key: 'description',
      header: t('products.description'),
      value: (row) => row.description,
      // Given the width the other columns leave. A product description is the one
      // column on this screen carrying prose, and it was sharing the table's width
      // equally with a column holding "EA" — so a forty-character description wrapped
      // to three lines while half the table sat empty.
      wide: true,
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
      render: (row) => moneyAlways(row.cost),
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
      render: (row) => moneyAlways(row.retailRate),
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
      render: (row) =>
        !row.isActive ? (
          <StatusBadge tone="neutral" label={t('masters.withdrawn')} struck />
        ) : row.isDiscontinued ? (
          // Amber, not grey: the product is still sold and still counted, it is just
          // not to be reordered, and grey would file it with the withdrawn ones.
          <StatusBadge tone="warn" label={t('products.discontinued')} />
        ) : (
          <StatusBadge tone="success" label={t('masters.active')} />
        ),
    },
  ];

  /*
    One search, in one card, with everything that narrows the list beside it.

    The screen had two: this one, which goes to the server and reaches the whole
    master, and the grid's own box directly beneath it, which filters the rows
    already fetched. Two boxes a hand's width apart, narrowing the same list by
    different rules, and no label on either saying which was which — so a search
    that found nothing might mean the product does not exist or might mean it was
    not on this page. The grid's box is withdrawn here and this one says what it
    reaches.
  */
  const controls = (
    /*
      A row that fills its width, rather than a grid of equal columns.

      `filter-grid` lays out tracks of a fixed minimum and fits as many as it can,
      so four controls in a row with room for six left a column and a half of empty
      card at the end — on the one master where the search box is what the screen is
      for, and where a product code, a description and a barcode all have to fit in
      it. Here the box takes whatever the other three do not, and the buttons are
      held at the end, so the card is full at every width.
    */
    <form
      className="toolbar"
      onSubmit={(event) => {
        event.preventDefault();
        setApplied(search);
      }}
    >
      <TextField
        label={t('products.search')}
        hint={t('products.searchHint')}
        type="search"
        size="sm"
        value={search}
        onChange={setSearch}
        placeholder={t('products.searchPlaceholder')}
        className="min-w-56 flex-1"
      />

      <Field label={t('products.category')} className="w-52 shrink-0">
        <SearchSelect
          value={categoryId}
          onChange={setCategoryId}
          clearable
          size="sm"
          label={t('products.category')}
          placeholder={t('products.allCategories')}
          options={(categories.data ?? []).map((row) => ({
            value: row.id,
            label: `${row.code} — ${row.name}`,
          }))}
        />
      </Field>

      <CheckField
        label={t('masters.includeWithdrawn')}
        checked={includeInactive}
        onChange={setIncludeInactive}
        className="shrink-0"
      />

      <div className="ms-auto flex shrink-0 items-center gap-2">
        <button type="submit" className="btn-primary btn-sm">
          {t('products.find')}
        </button>
        {applied !== '' && (
          <button
            type="button"
            className="btn-secondary btn-sm"
            onClick={() => {
              setSearch('');
              setApplied('');
            }}
          >
            {t('products.clear')}
          </button>
        )}
      </div>
    </form>
  );

  if (editingId) {
    return <ProductEditor productId={editingId} onClose={() => setEditingId(null)} />;
  }

  return (
    <>
      <ReportFrame title={t('nav.products')} controls={controls} query={query}>
        {(rows) => (
          <div className="space-y-3">
            {error && !adding && (
              <div role="alert" className="alert-error">
                {error}
              </div>
            )}

            <DataGrid
              gridKey="products"
              rows={rows}
              columns={columns}
              rowKey={(row) => row.id}
              emptyMessage={t('products.noneFound')}
              hideSearch
              actions={
                <GridAction label={t('products.new')} onClick={() => setAdding(true)} />
              }
            />
          </div>
        )}
      </ReportFrame>

      {/*
        A dialog rather than a panel over the list: the seven fields it asks for
        used to hold a third of the screen open on a catalogue that is searched far
        more often than it is added to. Mounted outside the frame, whose children
        are replaced by a skeleton whenever the list goes back to pending — a
        search applied behind the dialog should not empty it.
      */}
      {adding && (
        <Modal title={t('products.new')} size="form" onClose={() => setAdding(false)}>
          {error && (
            <div role="alert" className="alert-error">
              {error}
            </div>
          )}

          <AddProduct
            categories={categories.data ?? []}
            busy={mutation.isPending}
            onCancel={() => setAdding(false)}
            onSubmit={(body) => {
              setError(null);
              mutation.mutate(() => createProduct(body));
            }}
          />
        </Modal>
      )}
    </>
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

  const categoryOptions = useMemo(
    () =>
      categories.map((row) => ({
        value: row.id,
        label: `${row.code} — ${row.name}`,
        ...(row.parentName ? { detail: row.parentName } : {}),
      })),
    [categories],
  );

  const unitOptions = useMemo(
    () =>
      (units.data ?? []).map((row) => ({
        value: row.id,
        label: `${row.code} — ${row.name}`,
      })),
    [units.data],
  );

  /*
    The firm's own answer where it has set one in Settings, and otherwise the only
    one there is. A mandatory field holding the right answer already is one fewer
    dropdown between somebody and the product they came to add.
  */
  const { defaultCategoryId, defaultStockUnitId } = useSettings();

  useDefaultChoice(categoryId, categoryOptions, setCategoryId, defaultCategoryId);
  useDefaultChoice(stockUnitId, unitOptions, setStockUnitId, defaultStockUnitId);

  const draft = { code, description, categoryId, stockUnitId };

  const { errors, submit } = useValidation<typeof draft>((values) =>
    collect({
      description:
        required(values.description, t('products.descriptionRequired')) ??
        maxLength(values.description, 200, t('products.descriptionTooLong')),
      categoryId: required(values.categoryId, t('products.categoryRequired')),
      stockUnitId: required(values.stockUnitId, t('products.unitRequired')),
      code: maxLength(values.code, 30, t('products.codeTooLong')),
    }),
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        if (!submit(draft)) {
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
      <div className="form-grid">
        <TextField
          label={t('masters.code')}
          hint={t('products.codeAuto')}
          value={code}
          onChange={setCode}
          error={errors['code']}
        />

        <TextField
          label={t('products.description')}
          required
          autoFocus
          value={description}
          onChange={setDescription}
          error={errors['description']}
        />

        {/* Filled in from the English description where the firm has turned
            translation on in Settings, and editable either way. */}
        <ArabicNameField
          label={t('products.descriptionArabic')}
          source={description}
          value={descriptionArabic}
          onChange={setDescriptionArabic}
        />

        <SelectField
          label={t('products.itemType')}
          value={itemType}
          onChange={setItemType}
          options={[
            { value: '1', label: t('products.itemStock') },
            { value: '2', label: t('products.itemService') },
            { value: '3', label: t('products.itemNonStock') },
          ]}
        />

        <Field label={t('products.category')} required error={errors['categoryId']}>
          <SearchSelect
            value={categoryId}
            onChange={setCategoryId}
            options={categoryOptions}
            invalid={errors['categoryId'] !== undefined}
            label={t('products.category')}
            placeholder={t('products.choose')}
          />
        </Field>

        <Field label={t('products.stockUnit')} required error={errors['stockUnitId']}>
          <SearchSelect
            value={stockUnitId}
            onChange={setStockUnitId}
            options={unitOptions}
            invalid={errors['stockUnitId'] !== undefined}
            label={t('products.stockUnit')}
            placeholder={t('products.choose')}
          />
        </Field>

        <Field label={t('products.brand')}>
          <SearchSelect
            value={brandId}
            onChange={setBrandId}
            clearable
            label={t('products.brand')}
            placeholder={t('products.noBrand')}
            options={(brands.data ?? []).map((row) => ({
              value: row.id,
              label: `${row.code} — ${row.name}`,
            }))}
          />
        </Field>
      </div>

      <div className="form-actions">
        <button type="button" onClick={onCancel} className="btn-secondary">
          {t('common.cancel')}
        </button>
        <button type="submit" disabled={busy} className="btn-primary">
          {busy ? t('common.saving') : t('masters.add')}
        </button>
      </div>
    </form>
  );
}
