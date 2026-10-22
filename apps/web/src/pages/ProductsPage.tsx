import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { Modal } from '@/components/Modal';
import { useSerialNumbersEnabled } from '@/components/SerialNumbers';
import { ReportFrame } from '@/components/ReportFrame';
import { SelectField, TextField } from '@/components/Form';
import { MasterPicker, useMasterRows } from '@/components/MasterPicker';
import { StatusBadge } from '@/components/StatusBadge';
import { ArabicNameField } from '@/components/ArabicNameField';
import { ProductEditor } from '@/pages/ProductEditor';
import type { ApiError } from '@/lib/api';
import type { CategorySummary, UnitSummary } from '@/lib/inventory';
import type { TaxRate } from '@/lib/taxRates';
import {
  createProduct,
  listProducts,
  saveProductTab,
  type ProductSummary,
} from '@/lib/products';
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
  const serialsOn = useSerialNumbersEnabled();

  const [editingId, setEditingId] = useState<string | null>(null);
  const [initialEditorTab, setInitialEditorTab] = useState<
    'description' | 'details' | 'barcodes' | 'openingStock' | undefined
  >(undefined);
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const query = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products'],
    queryFn: () => listProducts('', '', false),
  });

  // Shared cache keys with the master screens, so opening this page after those does
  // not refetch what they already hold.
  const mutation = useMutation<
    { id: string; mode: 'save' | 'detail' },
    ApiError,
    { action: () => Promise<string>; mode: 'save' | 'detail' }
  >({
    mutationFn: async ({ action, mode }) => {
      const id = await action();
      return { id, mode };
    },
    onSuccess: async ({ id, mode }) => {
      setError(null);
      setAdding(false);
      await queryClient.invalidateQueries({ queryKey: ['products'] });
      if (mode === 'detail') {
        setInitialEditorTab('description');
        setEditingId(id);
      }
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
          onClick={() => {
            setInitialEditorTab(undefined);
            setEditingId(row.id);
          }}
          className="cell-link"
        >
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
          serialsOn && row.tracksSerialNumbers ? t('products.serialShort') : '',
        ]
          .filter(Boolean)
          .join(' · '),
      render: (row) => (
        <div className="flex flex-wrap items-center gap-1">
          {serialsOn && row.tracksSerialNumbers && (
            <span className="inline-flex items-center rounded bg-purple-50 dark:bg-purple-500/10 px-1.5 py-0.5 text-[10px] font-semibold text-purple-700 dark:text-purple-300 border border-purple-200 dark:border-purple-500/20">
              {t('products.serialShort')}
            </span>
          )}
          {row.tracksBatches && (
            <span className="inline-flex items-center rounded bg-blue-50 dark:bg-blue-500/10 px-1.5 py-0.5 text-[10px] font-semibold text-blue-700 dark:text-blue-300 border border-blue-200 dark:border-blue-500/20">
              {t('products.batchShort')}
            </span>
          )}
          {!(serialsOn && row.tracksSerialNumbers) && !row.tracksBatches && (
            <span className="text-ink-muted text-xs">—</span>
          )}
        </div>
      ),
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

  if (editingId) {
    return (
      <ProductEditor
        productId={editingId}
        initialTab={initialEditorTab}
        onClose={() => {
          setEditingId(null);
          setInitialEditorTab(undefined);
        }}
      />
    );
  }

  return (
    <>
      <ReportFrame title={t('nav.products')} controls={null} query={query}>
        {(rows) => (
          <div className="w-full space-y-3">
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
            busy={mutation.isPending}
            onCancel={() => setAdding(false)}
            onSubmit={(body, mode, tracksSerialNumbers) => {
              setError(null);
              mutation.mutate({
                action: async () => {
                  const id = await createProduct(body);
                  if (tracksSerialNumbers && id) {
                    try {
                      const cast = body as { stockUnitId: string };
                      await saveProductTab(id, 'stocking', {
                        purchaseUnitId: cast.stockUnitId,
                        salesUnitId: cast.stockUnitId,
                        minimumLevel: 0,
                        reorderLevel: 0,
                        maximumLevel: 0,
                        movement: 0,
                        tracksBatches: false,
                        tracksSerialNumbers: true,
                        shelfLifeDays: null,
                        isPacking: false,
                      });
                    } catch {
                      // Non-fatal
                    }
                  }
                  return id;
                },
                mode,
              });
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
 * is room to explain it. The cursor starts in Code, because a shop with its own codes
 * types one first and a shop without them presses Tab past an empty box. Every list
 * here can be added to in place, and each starts at the choice a new product most
 * likely wants.
 */
function AddProduct({
  busy,
  onCancel,
  onSubmit,
}: {
  readonly busy: boolean;
  readonly onCancel: () => void;
  readonly onSubmit: (
    body: object,
    mode: 'save' | 'detail',
    tracksSerialNumbers: boolean,
  ) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const categories = useMasterRows<CategorySummary>('category');
  const units = useMasterRows<UnitSummary>('unit');
  const taxRates = useMasterRows<TaxRate>('taxRate');

  const { defaultCategoryId, defaultStockUnitId, defaultItemType, defaultBrandId } =
    useSettings();

  const [code, setCode] = useState('');
  const [description, setDescription] = useState('');
  const [descriptionArabic, setDescriptionArabic] = useState('');
  const [categoryId, setCategoryId] = useState(defaultCategoryId ?? '');
  const [stockUnitId, setStockUnitId] = useState(defaultStockUnitId ?? '');
  const [brandId, setBrandId] = useState(defaultBrandId ?? '');
  const [taxSlabId, setTaxSlabId] = useState('');
  const [itemType, setItemType] = useState(String(defaultItemType ?? 1));
  const [tracksSerialNumbers, setTracksSerialNumbers] = useState(false);
  const serialsOn = useSerialNumbersEnabled();

  /*
    The starting choices, once the lists have arrived: the firm's own defaults from
    Settings where they still exist, otherwise what a new product most likely wants -
    a general category, a counted unit, the default tax rate.
  */
  useEffect(() => {
    if (categoryId === '' || !categories.rows.some((row) => row.id === categoryId)) {
      const active = categories.rows.filter((row) => row.isActive);
      const choice =
        active.find((row) => row.id === defaultCategoryId) ??
        active.find((row) => /^general$/i.test(row.name)) ??
        active[0];
      if (choice && choice.id !== categoryId) setCategoryId(choice.id);
    }
  }, [categories.rows, categoryId, defaultCategoryId]);

  useEffect(() => {
    if (stockUnitId === '' || !units.rows.some((row) => row.id === stockUnitId)) {
      const bases = units.rows.filter((row) => row.isActive && !row.baseUnitId);
      const choice =
        bases.find((row) => row.id === defaultStockUnitId) ??
        bases.find((row) => /^(pcs|pc|nos|no|ea|each|unit)$/i.test(row.code)) ??
        bases[0];
      if (choice && choice.id !== stockUnitId) setStockUnitId(choice.id);
    }
  }, [units.rows, stockUnitId, defaultStockUnitId]);

  const [taxTouched, setTaxTouched] = useState(false);
  useEffect(() => {
    if (!taxTouched && taxSlabId === '') {
      const preferred = taxRates.rows.find((row) => row.isDefault && row.isActive);
      if (preferred) setTaxSlabId(preferred.id);
    }
  }, [taxRates.rows, taxSlabId, taxTouched]);

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

  const doSubmit = (mode: 'save' | 'detail'): void => {
    if (!submit(draft)) {
      return;
    }

    onSubmit(
      {
        code: code.trim() || null,
        description: description.trim(),
        descriptionArabic: descriptionArabic.trim() || null,
        categoryId,
        stockUnitId,
        brandId: brandId || null,
        taxSlabId: taxSlabId || null,
        itemType: Number(itemType),
      },
      mode,
      tracksSerialNumbers,
    );
  };

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        doSubmit('save');
      }}
    >
      {/* Code and type, then the descriptions across the full width - English over
          the Arabic it translates - then how it is filed beside the unit and tax it
          is counted and charged in. */}
      <div className="form-grid">
        <TextField
          label={t('masters.code')}
          autoFocus
          value={code}
          onChange={setCode}
          error={errors['code']}
          placeholder={t('masters.codeAuto')}
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

        <div className="sm:col-span-2">
          <TextField
            label={t('products.description')}
            required
            value={description}
            onChange={setDescription}
            error={errors['description']}
          />
        </div>

        {/* Filled in from the English description where the firm has turned
            translation on in Settings, and editable either way. */}
        <div className="empty:hidden sm:col-span-2">
          <ArabicNameField
            label={t('products.descriptionArabic')}
            source={description}
            value={descriptionArabic}
            onChange={setDescriptionArabic}
          />
        </div>

        <MasterPicker
          kind="category"
          label={t('products.category')}
          required
          value={categoryId}
          onChange={setCategoryId}
          error={errors['categoryId']}
        />

        <MasterPicker
          kind="brand"
          label={t('products.brand')}
          clearable
          placeholder={t('products.noBrand')}
          value={brandId}
          onChange={setBrandId}
        />

        <MasterPicker
          kind="unit"
          label={t('products.stockUnit')}
          required
          value={stockUnitId}
          onChange={setStockUnitId}
          error={errors['stockUnitId']}
          include={(row) => !(row as UnitSummary).baseUnitId}
        />

        <MasterPicker
          kind="taxRate"
          label={t('products.taxRate')}
          clearable
          placeholder={t('taxRates.none')}
          value={taxSlabId}
          onChange={(value) => {
            setTaxTouched(true);
            setTaxSlabId(value);
          }}
        />

        {/* Offered only where the firm has switched serial numbers on in Settings. */}
        {serialsOn && (
          <label className="field-check sm:col-span-2">
            <input
              type="checkbox"
              checked={tracksSerialNumbers}
              onChange={(e) => setTracksSerialNumbers(e.target.checked)}
            />
            {t('products.tracksSerialNumbers')}
          </label>
        )}
      </div>

      <div className="form-actions flex items-center justify-end gap-2">
        <button type="button" onClick={onCancel} className="btn-secondary">
          {t('common.cancel')}
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={() => doSubmit('save')}
          className="btn-secondary"
        >
          {busy ? t('common.saving') : t('products.save')}
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={() => doSubmit('detail')}
          className="btn-primary"
        >
          {busy ? t('common.saving') : t('products.detail')}
        </button>
      </div>
    </form>
  );
}
