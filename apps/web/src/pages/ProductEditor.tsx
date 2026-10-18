import { useState, useEffect } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ReportSkeleton } from '@/components/ReportFrame';
import { PageHeading } from '@/components/PageHeading';
import { StatusBadge } from '@/components/StatusBadge';
import { ArabicNameField } from '@/components/ArabicNameField';
import { MasterPicker } from '@/components/MasterPicker';
import clsx from 'clsx';
import type { ApiError } from '@/lib/api';
import {
  addProductBarcode,
  getProduct,
  removeProductBarcode,
  saveProductTab,
  setProductFlag,
  type ProductDetail,
} from '@/lib/products';
import { listMaster, type UnitSummary, type WarehouseSummary } from '@/lib/inventory';
import { currentDecimals, moneyAlways } from '@/lib/money';
import { SearchSelect } from '@/components/SearchSelect';
import { useSettings } from '@/stores/settings';
import { useSerialNumbersEnabled } from '@/components/SerialNumbers';
import {
  createStockDocument,
  fetchStockLedger,
  StockDocumentType,
  type StockLedgerReport,
} from '@/lib/stock';

type EditorTab = 'description' | 'details' | 'barcodes' | 'openingStock';

/**
 * The product editor.
 *
 * Four tabs. Description and Details are one form under one Save - what the product is,
 * how it is filed and priced on the first, the particulars and how it is stocked on the
 * second - and Save writes the lot and goes back to the list, which is where somebody
 * entering a catalogue goes next. Barcodes and Opening Stock act as they are used and
 * stay where they are, because each is a list being added to rather than a form.
 */
export function ProductEditor({
  productId,
  initialTab,
  onClose,
}: {
  readonly productId: string;
  readonly initialTab?: EditorTab | undefined;
  readonly onClose: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const product = useQuery<ProductDetail, ApiError>({
    queryKey: ['product', productId],
    queryFn: () => getProduct(productId),
  });

  const units = useQuery<readonly UnitSummary[], ApiError>({
    queryKey: ['units', false],
    queryFn: () => listMaster<UnitSummary>('units', false),
  });

  if (product.isPending) {
    return (
      <section className="page" aria-busy="true">
        {/* Named while it loads, so the bar does not sit empty over a page of grey
            bars and then change its mind about what screen this is. */}
        <PageHeading title={t('nav.products')} />

        <div className="flex gap-2">
          {Array.from({ length: 4 }, (_, index) => (
            <span key={index} className="skeleton h-8 w-28 rounded" />
          ))}
        </div>
        <ReportSkeleton rows={4} />
      </section>
    );
  }

  /*
    The editor replaces the list rather than sitting beside it, so a product that
    will not load must still offer the way back.
  */
  if (product.isError || !product.data) {
    return (
      <section className="page">
        <PageHeading title={t('nav.products')} />
        <Alert>
          {product.error?.detail ?? product.error?.code ?? t('products.notFound')}
        </Alert>

        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => void product.refetch()}
            className="btn-secondary"
          >
            {t('common.retry')}
          </button>
          <button type="button" onClick={onClose} className="btn-secondary">
            {t('products.backToList')}
          </button>
        </div>
      </section>
    );
  }

  /*
    Keyed on the product so opening another one rebuilds the form state. Without it the
    fields would keep the previous product's values until the query resolved, which is
    the shape of an edit landing on the wrong record.
  */
  return (
    <EditorBody
      key={product.data.id}
      product={product.data}
      units={units.data ?? []}
      initialTab={initialTab ?? 'description'}
      onClose={onClose}
    />
  );
}

/** Every field the Description and Details tabs edit, as the boxes hold them. */
interface ProductForm {
  readonly description: string;
  readonly descriptionArabic: string;
  readonly shortDescription: string;
  readonly itemName: string;
  readonly manufacturer: string;
  readonly label: string;
  readonly size: string;
  readonly origin: string;
  readonly rack: string;
  readonly bin: string;
  readonly categoryId: string;
  readonly brandId: string;
  readonly taxSlabId: string;
  readonly purchaseUnitId: string;
  readonly salesUnitId: string;
  readonly costingMethod: string;
  readonly cost: string;
  readonly profitPercentage: string;
  readonly corPercentage: string;
  readonly retailRate: string;
  readonly wholesaleRate: string;
  readonly otherRate: string;
  readonly maximumRetailPrice: string;
  readonly minimumLevel: string;
  readonly reorderLevel: string;
  readonly maximumLevel: string;
  readonly movement: string;
  readonly tracksBatches: boolean;
  readonly tracksSerialNumbers: boolean;
  readonly shelfLifeDays: string;
  readonly isPacking: boolean;
  readonly device: string;
  readonly colour: string;
  readonly battery: string;
  readonly ram: string;
  readonly storage: string;
}

/** The decimal places a percentage is shown to, whatever the currency's are. */
const PERCENT_PLACES = 2;

function formFrom(product: ProductDetail, places: number): ProductForm {
  // Shown to the firm's decimal places - 0.00 rather than 0 - so a rate box reads like
  // every other figure in the application.
  const fixed = (value: number, digits = places): string => value.toFixed(digits);

  return {
    description: product.description,
    descriptionArabic: product.descriptionArabic ?? '',
    shortDescription: product.shortDescription ?? '',
    itemName: product.itemName ?? '',
    manufacturer: product.manufacturer ?? '',
    label: product.label ?? '',
    size: product.size ?? '',
    origin: product.origin ?? '',
    rack: product.rack ?? '',
    bin: product.bin ?? '',
    categoryId: product.categoryId,
    brandId: product.brandId ?? '',
    taxSlabId: product.taxSlabId ?? '',
    purchaseUnitId: product.purchaseUnitId,
    salesUnitId: product.salesUnitId,
    costingMethod: String(product.costingMethod),
    cost: fixed(product.cost),
    profitPercentage: fixed(product.profitPercentage, PERCENT_PLACES),
    corPercentage: fixed(product.corPercentage, PERCENT_PLACES),
    retailRate: fixed(product.retailRate),
    wholesaleRate: fixed(product.wholesaleRate),
    otherRate: fixed(product.otherRate),
    maximumRetailPrice: fixed(product.maximumRetailPrice),
    minimumLevel: String(product.minimumLevel),
    reorderLevel: String(product.reorderLevel),
    maximumLevel: String(product.maximumLevel),
    movement: String(product.movement),
    tracksBatches: product.tracksBatches,
    tracksSerialNumbers: product.tracksSerialNumbers,
    shelfLifeDays: product.shelfLifeDays === null ? '' : String(product.shelfLifeDays),
    isPacking: product.isPacking,
    device: product.device ?? '',
    colour: product.colour ?? '',
    battery: product.battery ?? '',
    ram: product.ram ?? '',
    storage: product.storage ?? '',
  };
}

function EditorBody({
  product,
  units,
  initialTab,
  onClose,
}: {
  readonly product: ProductDetail;
  readonly units: readonly UnitSummary[];
  readonly initialTab: EditorTab;
  readonly onClose: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const enableDeviceAttributes = useSettings((s) => s.enableDeviceAttributes);
  const places = currentDecimals();

  const [tab, setTab] = useState<EditorTab>(initialTab);
  const [form, setForm] = useState<ProductForm>(() => formFrom(product, places));
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const set = (patch: Partial<ProductForm>): void =>
    setForm((previous) => ({ ...previous, ...patch }));

  const refresh = async (): Promise<void> => {
    await queryClient.invalidateQueries({ queryKey: ['product', product.id] });
    await queryClient.invalidateQueries({ queryKey: ['products'] });
  };

  /*
    One Save for both tabs. Sequential rather than parallel, because the API groups the
    fields five ways: if the rates are refused, what went before has landed and the
    message names one thing rather than several.
  */
  const save = useMutation<void, ApiError>({
    mutationFn: async () => {
      await saveProductTab(product.id, 'description', {
        description: form.description.trim(),
        descriptionArabic: blank(form.descriptionArabic),
        shortDescription: blank(form.shortDescription),
        itemName: blank(form.itemName),
        manufacturer: blank(form.manufacturer),
        label: blank(form.label),
        size: blank(form.size),
        origin: blank(form.origin),
        rack: blank(form.rack),
        bin: blank(form.bin),
      });

      await saveProductTab(product.id, 'classification', {
        categoryId: form.categoryId,
        brandId: form.brandId || null,
        taxSlabId: form.taxSlabId || null,
      });

      await saveProductTab(product.id, 'rates', {
        costingMethod: Number(form.costingMethod),
        cost: number(form.cost),
        profitPercentage: number(form.profitPercentage),
        corPercentage: number(form.corPercentage),
        retailRate: number(form.retailRate),
        wholesaleRate: number(form.wholesaleRate),
        otherRate: number(form.otherRate),
        maximumRetailPrice: number(form.maximumRetailPrice),
      });

      await saveProductTab(product.id, 'stocking', {
        purchaseUnitId: form.purchaseUnitId,
        salesUnitId: form.salesUnitId,
        minimumLevel: number(form.minimumLevel),
        reorderLevel: number(form.reorderLevel),
        maximumLevel: number(form.maximumLevel),
        movement: Number(form.movement),
        tracksBatches: form.tracksBatches,
        tracksSerialNumbers: form.tracksSerialNumbers,
        shelfLifeDays: form.shelfLifeDays.trim() ? Number(form.shelfLifeDays) : null,
        isPacking: form.isPacking,
      });

      if (enableDeviceAttributes) {
        await saveProductTab(product.id, 'device', {
          device: blank(form.device),
          colour: blank(form.colour),
          battery: blank(form.battery),
          ram: blank(form.ram),
          storage: blank(form.storage),
        });
      }
    },
    onSuccess: async () => {
      await refresh();
      onClose();
    },
    // The server owns the rules - an MRP a retail rate may not exceed, a unit that
    // does not convert - so its own message is shown rather than one guessed at here.
    onError: (failure) => setError(failure.detail || failure.code),
  });

  // Barcodes, opening stock and the two flags act at once and leave the editor open.
  const action = useMutation<void, ApiError, () => Promise<void>>({
    mutationFn: (run) => run(),
    onSuccess: async () => {
      setError(null);
      setSaved(true);
      window.setTimeout(() => setSaved(false), 2000);
      await refresh();
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const run = (work: () => Promise<void>): void => {
    setError(null);
    action.mutate(work);
  };

  /*
    Three states, not two. A discontinued product is still sold and still counted —
    it is simply not to be reordered — so it is amber rather than filed with the
    withdrawn ones in grey, exactly as the list draws it.
  */
  const status = product.isActive ? (
    product.isDiscontinued ? (
      <StatusBadge tone="warn" label={t('products.discontinued')} />
    ) : (
      <StatusBadge tone="success" label={t('masters.active')} />
    )
  ) : (
    <StatusBadge tone="neutral" label={t('masters.withdrawn')} struck />
  );

  const editing = tab === 'description' || tab === 'details';
  const canSave =
    form.description.trim() !== '' && form.categoryId !== '' && !save.isPending;

  return (
    <section className="page">
      <PageHeading
        title={`${product.code} — ${product.description}`}
        actions={
          <>
            <span className="hidden sm:inline-flex">{status}</span>

            {saved && (
              <span className="text-xs text-emerald-600">{t('products.saved')}</span>
            )}

            {/*
              Two spellings of one control. A phone's bar has room for a title or a
              worded button, not both.
            */}
            <button
              type="button"
              onClick={onClose}
              aria-label={t('products.backToList')}
              title={t('products.backToList')}
              className="icon-button sm:hidden"
            >
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth={1.8}
                strokeLinecap="round"
                strokeLinejoin="round"
                aria-hidden="true"
                className="size-5 rtl:rotate-180"
              >
                <path d="M15 5 8 12l7 7" />
              </svg>
            </button>

            <button
              type="button"
              onClick={onClose}
              className="btn-secondary hidden sm:inline-flex"
            >
              {t('products.backToList')}
            </button>
          </>
        }
      />

      {error && <Alert>{error}</Alert>}

      {/*
        Scrolls rather than wraps below `sm`. Four tabs and a badge do not fit across
        390px, and a tab strip that becomes two rows of buttons stops reading as a strip.
      */}
      <nav className="flex items-center gap-1 overflow-x-auto border-b border-line">
        <Tab active={tab === 'description'} onClick={() => setTab('description')}>
          {t('products.tabDescription')}
        </Tab>
        <Tab active={tab === 'details'} onClick={() => setTab('details')}>
          {t('products.tabDetails')}
        </Tab>
        <Tab active={tab === 'barcodes'} onClick={() => setTab('barcodes')}>
          {t('products.tabBarcodes')} ({product.barcodes.length})
        </Tab>
        <Tab active={tab === 'openingStock'} onClick={() => setTab('openingStock')}>
          {t('products.tabOpeningStock')}
        </Tab>

        <span className="shrink-0 pb-1.5 ps-2 sm:hidden">{status}</span>
      </nav>

      {editing && (
        <form
          className="space-y-6"
          onSubmit={(event) => {
            event.preventDefault();
            if (canSave) {
              setError(null);
              save.mutate();
            }
          }}
        >
          {tab === 'description' ? (
            <DescriptionFields product={product} units={units} form={form} set={set} />
          ) : (
            <DetailsFields
              form={form}
              set={set}
              deviceAttributes={enableDeviceAttributes}
            />
          )}

          <div className="page-actions">
            {/* The two flags mean different things and are routinely confused, so they
                are two buttons with their own words rather than one toggle. */}
            {tab === 'details' && (
              <>
                <button
                  type="button"
                  disabled={action.isPending}
                  className="btn-secondary"
                  onClick={() =>
                    run(async () => {
                      await setProductFlag(
                        product.id,
                        'discontinued',
                        !product.isDiscontinued,
                      );
                    })
                  }
                >
                  {product.isDiscontinued
                    ? t('products.resume')
                    : t('products.discontinue')}
                </button>

                <button
                  type="button"
                  disabled={action.isPending}
                  className="btn-secondary"
                  onClick={() =>
                    run(async () => {
                      await setProductFlag(product.id, 'active', !product.isActive);
                    })
                  }
                >
                  {product.isActive ? t('masters.withdraw') : t('masters.restore')}
                </button>
              </>
            )}

            <button type="button" onClick={onClose} className="btn-secondary">
              {t('common.cancel')}
            </button>
            <button type="submit" disabled={!canSave} className="btn-primary">
              {save.isPending ? t('common.saving') : t('products.save')}
            </button>
          </div>
        </form>
      )}

      {tab === 'barcodes' && (
        <BarcodesTab product={product} run={run} busy={action.isPending} />
      )}

      {tab === 'openingStock' && <OpeningStockTab product={product} units={units} />}
    </section>
  );
}

/**
 * Tab 1 — what the product is, the units it moves in, how it is filed, and what it
 * costs and sells for.
 */
function DescriptionFields({
  product,
  units,
  form,
  set,
}: {
  readonly product: ProductDetail;
  readonly units: readonly UnitSummary[];
  readonly form: ProductForm;
  readonly set: (patch: Partial<ProductForm>) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const places = currentDecimals();

  // The units that convert to this product's stock unit. Offering the rest would let
  // somebody buy in kilograms what is stocked in litres, and the server would refuse
  // it after the fact; better not to offer it.
  const stockUnit = units.find((unit) => unit.id === product.stockUnitId);
  const group = stockUnit?.baseUnitId ?? stockUnit?.id;
  const convertible = (row: { readonly id: string }): boolean => {
    const unit = row as UnitSummary;
    return group !== undefined && (unit.baseUnitId ?? unit.id) === group;
  };

  const money = (key: keyof ProductForm): React.JSX.Element => (
    <FixedField
      key={key}
      label={t(`products.${key}`)}
      value={form[key] as string}
      places={places}
      onChange={(value) => set({ [key]: value } as Partial<ProductForm>)}
    />
  );

  const percent = (key: keyof ProductForm): React.JSX.Element => (
    <FixedField
      key={key}
      label={t(`products.${key}`)}
      value={form[key] as string}
      places={PERCENT_PLACES}
      onChange={(value) => set({ [key]: value } as Partial<ProductForm>)}
    />
  );

  return (
    <>
      <Section title={t('products.identity')} columns={6}>
        {/* Read-only, and not merely disabled in the UI: nothing on the server changes
            it either. The code is how this product is named on every document already
            entered. */}
        <Field label={t('masters.code')} span={2}>
          <input value={product.code} readOnly className="field-input opacity-60" />
        </Field>

        <Field label={t('products.description')} span={4}>
          <input
            value={form.description}
            onChange={(e) => set({ description: e.target.value })}
            className="field-input"
            required
          />
        </Field>

        {/* With the translate button where the firm has turned translation on, as on
            every other master's Arabic name. */}
        <div className="lg:col-span-3">
          <ArabicNameField
            label={t('products.descriptionArabic')}
            source={form.description}
            value={form.descriptionArabic}
            onChange={(value) => set({ descriptionArabic: value })}
          />
        </div>

        <Field label={t('products.shortDescription')} span={3}>
          <input
            value={form.shortDescription}
            onChange={(e) => set({ shortDescription: e.target.value })}
            className="field-input"
          />
        </Field>
      </Section>

      <Section title={t('products.units')}>
        <Field label={t('products.stockUnit')}>
          {/* Fixed once the product exists: changing it would restate every quantity
              ever recorded against this product. */}
          <input
            readOnly
            value={stockUnit ? `${stockUnit.code} — ${stockUnit.name}` : ''}
            className="field-input opacity-60"
          />
        </Field>

        <MasterPicker
          kind="unit"
          label={t('products.purchaseUnit')}
          value={form.purchaseUnitId}
          onChange={(value) => set({ purchaseUnitId: value })}
          include={convertible}
          unitGroup={stockUnit}
        />

        <MasterPicker
          kind="unit"
          label={t('products.salesUnit')}
          value={form.salesUnitId}
          onChange={(value) => set({ salesUnitId: value })}
          include={convertible}
          unitGroup={stockUnit}
        />
      </Section>

      <Section title={t('products.classification')}>
        <MasterPicker
          kind="category"
          label={t('products.category')}
          required
          value={form.categoryId}
          onChange={(value) => set({ categoryId: value })}
        />
        <MasterPicker
          kind="brand"
          label={t('products.brand')}
          clearable
          placeholder={t('products.noBrand')}
          value={form.brandId}
          onChange={(value) => set({ brandId: value })}
        />
        <MasterPicker
          kind="taxRate"
          label={t('products.taxRate')}
          clearable
          placeholder={t('taxRates.none')}
          value={form.taxSlabId}
          onChange={(value) => set({ taxSlabId: value })}
        />
      </Section>

      <Section title={`${t('products.rates')} · ${product.currency}`}>
        <Field label={t('products.costingMethod')}>
          <SearchSelect
            value={form.costingMethod}
            onChange={(value) => set({ costingMethod: value })}
            label={t('products.costingMethod')}
            options={[
              { value: '1', label: t('products.costingLastPurchase') },
              { value: '2', label: t('products.costingAverage') },
            ]}
          />
        </Field>
        {money('cost')}
        {percent('profitPercentage')}
        {percent('corPercentage')}
        {money('retailRate')}
        {money('wholesaleRate')}
        {money('otherRate')}
        {money('maximumRetailPrice')}
      </Section>
    </>
  );
}

/** Tab 2 — the particulars printed on the box, where it sits, and how it is stocked. */
function DetailsFields({
  form,
  set,
  deviceAttributes,
}: {
  readonly form: ProductForm;
  readonly set: (patch: Partial<ProductForm>) => void;
  readonly deviceAttributes: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const serialsOn = useSerialNumbersEnabled();

  const text = (key: keyof ProductForm, label: string): React.JSX.Element => (
    <Field key={key} label={label}>
      <input
        value={form[key] as string}
        onChange={(e) => set({ [key]: e.target.value } as Partial<ProductForm>)}
        className="field-input"
      />
    </Field>
  );

  return (
    <>
      <Section title={t('products.details')}>
        {text('itemName', t('products.itemName'))}
        {text('manufacturer', t('products.manufacturer'))}
        {text('label', t('products.label'))}
        {text('size', t('products.size'))}
        {text('origin', t('products.origin'))}
        {text('rack', t('products.rack'))}
        {text('bin', t('products.bin'))}
      </Section>

      {deviceAttributes && (
        <Section title={t('products.deviceAttributes')}>
          {text('device', t('products.device'))}
          {text('colour', t('products.colour'))}
          {text('battery', t('products.battery'))}
          {text('ram', t('products.ram'))}
          {text('storage', t('products.storage'))}
        </Section>
      )}

      <Section title={t('products.orderLevels')}>
        <NumberField
          label={t('products.minimumLevel')}
          value={form.minimumLevel}
          onChange={(value) => set({ minimumLevel: value })}
        />
        <NumberField
          label={t('products.reorderLevel')}
          value={form.reorderLevel}
          onChange={(value) => set({ reorderLevel: value })}
        />
        <NumberField
          label={t('products.maximumLevel')}
          value={form.maximumLevel}
          onChange={(value) => set({ maximumLevel: value })}
        />

        <Field label={t('products.movement')}>
          <SearchSelect
            value={form.movement}
            onChange={(value) => set({ movement: value })}
            label={t('products.movement')}
            options={[
              { value: '0', label: t('products.movementUnclassified') },
              { value: '1', label: t('products.movementFast') },
              { value: '2', label: t('products.movementNormal') },
              { value: '3', label: t('products.movementSlow') },
              { value: '4', label: t('products.movementDead') },
            ]}
          />
        </Field>
      </Section>

      <Section title={t('products.tracking')}>
        <Check
          label={t('products.tracksBatches')}
          checked={form.tracksBatches}
          onChange={(value) => set({ tracksBatches: value })}
        />
        {/* Both at once is allowed and normal: a handset arrives in a batch and still
            carries its own IMEI. Offered only where the firm has switched serial
            numbers on in Settings. */}
        {serialsOn && (
          <Check
            label={t('products.tracksSerialNumbers')}
            checked={form.tracksSerialNumbers}
            onChange={(value) => set({ tracksSerialNumbers: value })}
          />
        )}
        <Check
          label={t('products.isPacking')}
          checked={form.isPacking}
          onChange={(value) => set({ isPacking: value })}
        />

        {form.tracksBatches && (
          <NumberField
            label={t('products.shelfLifeDays')}
            value={form.shelfLifeDays}
            onChange={(value) => set({ shelfLifeDays: value })}
          />
        )}
      </Section>
    </>
  );
}

/** A figure shown to a fixed number of places, tidied back to them when left. */
function FixedField({
  label,
  value,
  places,
  onChange,
}: {
  readonly label: string;
  readonly value: string;
  readonly places: number;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  return (
    <label className="block">
      <span className="field-label">{label}</span>
      <input
        type="number"
        step="any"
        inputMode="decimal"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        onBlur={() => {
          const parsed = Number(value);
          onChange(
            Number.isFinite(parsed) ? parsed.toFixed(places) : (0).toFixed(places),
          );
        }}
        className="field-input text-end font-mono tabular-nums"
      />
    </label>
  );
}

/**
 * Tab 3 — the multiple-rate barcode grid.
 *
 * A barcode left without rates prices as the product does; one given rates is the
 * same goods sold under another label at another price, which is what the grid is
 * for.
 */
function BarcodesTab({
  product,
  run,
  busy,
}: {
  readonly product: ProductDetail;
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [barcode, setBarcode] = useState('');
  const [cost, setCost] = useState('');
  const [retailRate, setRetailRate] = useState('');
  const [wholesaleRate, setWholesaleRate] = useState('');
  const [maximumRetailPrice, setMaximumRetailPrice] = useState('');

  return (
    <div className="space-y-4">
      <form
        className="panel toolbar"
        onSubmit={(event) => {
          event.preventDefault();

          if (!barcode.trim()) {
            return;
          }

          run(async () => {
            await addProductBarcode(product.id, {
              barcode: barcode.trim(),
              // Null rather than zero when left blank: null means "price as the
              // product does", and zero would mean "free".
              cost: optional(cost),
              retailRate: optional(retailRate),
              wholesaleRate: optional(wholesaleRate),
              maximumRetailPrice: optional(maximumRetailPrice),
            });
          });

          setBarcode('');
          setCost('');
          setRetailRate('');
          setWholesaleRate('');
          setMaximumRetailPrice('');
        }}
      >
        <Compact label={t('products.barcode')} value={barcode} onChange={setBarcode} />
        <Compact label={t('products.cost')} value={cost} onChange={setCost} numeric />
        <Compact
          label={t('products.retailRate')}
          value={retailRate}
          onChange={setRetailRate}
          numeric
        />
        <Compact
          label={t('products.wholesaleRate')}
          value={wholesaleRate}
          onChange={setWholesaleRate}
          numeric
        />
        <Compact
          label={t('products.maximumRetailPrice')}
          value={maximumRetailPrice}
          onChange={setMaximumRetailPrice}
          numeric
        />

        <button type="submit" disabled={busy} className="btn-primary">
          {t('masters.add')}
        </button>
      </form>

      <div className="table-wrap max-h-[70vh] overflow-y-auto">
        <table className="table">
          <thead className="bg-surface-3">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">
                {t('products.barcode')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">{t('products.cost')}</th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('products.retailRate')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('products.wholesaleRate')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">
                {t('products.maximumRetailPrice')}
              </th>
              <th className="text-end" />
            </tr>
          </thead>
          <tbody>
            {product.barcodes.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-3 py-6 text-center text-sm text-ink-muted">
                  {t('products.noBarcodes')}
                </td>
              </tr>
            ) : (
              product.barcodes.map((row) => (
                <tr key={row.id} className="border-t border-line">
                  <td className="px-3 py-1.5 font-mono">{row.barcode}</td>
                  <td className="cell-numeric">{moneyAlways(row.cost)}</td>
                  <td className="cell-numeric">{moneyAlways(row.retailRate)}</td>
                  <td className="cell-numeric">{moneyAlways(row.wholesaleRate)}</td>
                  <td className="cell-numeric">{moneyAlways(row.maximumRetailPrice)}</td>
                  <td className="px-3 py-1.5 text-end">
                    <button
                      type="button"
                      disabled={busy}
                      className="rounded-md border border-line px-2 py-0.5 text-xs font-medium text-ink-muted transition hover:border-line-strong hover:bg-surface-3 hover:text-ink disabled:pointer-events-none disabled:opacity-40"
                      onClick={() =>
                        run(async () => {
                          await removeProductBarcode(product.id, row.id);
                        })
                      }
                    >
                      {t('products.removeBarcode')}
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/** Blank means "not stated", which the API expects as null rather than an empty string. */
function blank(value: string): string | null {
  return value.trim() || null;
}

/** A figure a field must always carry, blank reading as zero. */
function number(value: string): number {
  const parsed = Number(value);

  return Number.isFinite(parsed) ? parsed : 0;
}

/** A figure a field may leave unstated, where zero and blank mean different things. */
function optional(value: string): number | null {
  return value.trim() ? number(value) : null;
}

/**
 * A block of the editor, on a grid of its own.
 *
 * Six columns where one field deserves more room than the rest, three where they are
 * all alike. The identity block is the case that needs it: a code is six characters
 * and the description is the product's name, and on an even three-column grid the
 * two were given the same 384px — a box with five characters in it and four fifths
 * of it empty, beside the one field on the screen a long name will not fit in.
 *
 * Six and not four, so the wide field is still a whole number of the same tracks
 * everything else sits on. The description takes four of six and the code takes two,
 * which leaves every column boundary on the page where the rows below it put theirs:
 * a form whose second row does not line up with its first is the thing that reads as
 * broken, whatever the fields are worth.
 */
function Section({
  title,
  columns = 3,
  children,
}: {
  readonly title: string;
  readonly columns?: 3 | 6;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <fieldset className="space-y-3">
      <legend className="text-sm font-semibold text-ink-muted uppercase">{title}</legend>

      <div
        className={clsx(
          'grid gap-4 sm:grid-cols-2',
          columns === 6 ? 'lg:grid-cols-6' : 'lg:grid-cols-3',
        )}
      >
        {children}
      </div>
    </fieldset>
  );
}

/** The span classes, written out so the stylesheet's scanner can see them. */
const SPAN: Record<number, string> = {
  1: 'lg:col-span-1',
  2: 'lg:col-span-2',
  3: 'lg:col-span-3',
  4: 'lg:col-span-4',
};

function Field({
  label,
  span,
  children,
}: {
  readonly label: string;
  /** How many of a six-column section this field takes, above `lg`. */
  readonly span?: 1 | 2 | 3 | 4;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <label className={clsx('block', span !== undefined && SPAN[span])}>
      <span className="field-label">{label}</span>
      {children}
    </label>
  );
}

function NumberField({
  label,
  value,
  onChange,
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  return (
    <label className="block">
      <span className="field-label">{label}</span>
      <input
        type="number"
        step="any"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="field-input text-end"
      />
    </label>
  );
}

function Check({
  label,
  checked,
  onChange,
}: {
  readonly label: string;
  readonly checked: boolean;
  readonly onChange: (value: boolean) => void;
}): React.JSX.Element {
  return (
    <label className="flex items-center gap-2 self-end pb-2 text-sm">
      <input
        type="checkbox"
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
      />
      {label}
    </label>
  );
}

function Compact({
  label,
  value,
  onChange,
  numeric = false,
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly numeric?: boolean;
}): React.JSX.Element {
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      <input
        {...(numeric ? { type: 'number', step: 'any' } : {})}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={clsx('field-input-sm w-full sm:w-32', numeric && 'text-end')}
      />
    </label>
  );
}

function Tab({
  active,
  onClick,
  children,
}: {
  readonly active: boolean;
  readonly onClick: () => void;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      className={clsx(
        '-mb-px shrink-0 border-b-2 px-3 py-2 text-sm font-medium whitespace-nowrap transition sm:px-4',
        active
          ? 'border-brand-600 text-brand-700 dark:text-brand-100'
          : 'border-transparent text-ink-muted hover:border-line-strong hover:text-ink',
      )}
    >
      {children}
    </button>
  );
}

function Alert({ children }: { readonly children: React.ReactNode }): React.JSX.Element {
  return (
    <div role="alert" className="alert-error">
      {children}
    </div>
  );
}

function OpeningStockTab({
  product,
  units,
}: {
  readonly product: ProductDetail;
  readonly units: readonly UnitSummary[];
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { preferredWarehouseId } = useSettings();
  const serialsOn = useSerialNumbersEnabled();

  const today = new Date().toISOString().slice(0, 10);
  const [date, setDate] = useState(today);
  const [warehouseId, setWarehouseId] = useState(preferredWarehouseId || '');
  const [quantity, setQuantity] = useState('1');
  const [rate, setRate] = useState(String(product.cost || product.retailRate || '0'));
  const [batchNumber, setBatchNumber] = useState('');
  const [expiresOn, setExpiresOn] = useState('');
  const [serialNumbers, setSerialNumbers] = useState('');
  const [remarks, setRemarks] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });

  useEffect(() => {
    if (!warehouseId && warehouses.data && warehouses.data.length > 0) {
      const defaultWh = warehouses.data.find((w) => w.isDefault) ?? warehouses.data[0];
      if (defaultWh) {
        setWarehouseId(defaultWh.id);
      }
    }
  }, [warehouseId, warehouses.data]);

  const ledgerQuery = useQuery<StockLedgerReport, ApiError>({
    queryKey: ['stock-ledger', product.id, today],
    queryFn: () => fetchStockLedger(product.id, '2000-01-01', today, ''),
  });

  const mutation = useMutation({
    mutationFn: async () => {
      const qtyNum = Number(quantity);
      if (!Number.isFinite(qtyNum) || qtyNum <= 0) {
        throw new Error(
          t('stock.quantityPositive') || 'Quantity must be greater than zero',
        );
      }
      const rateNum = Number(rate);
      if (!Number.isFinite(rateNum) || rateNum < 0) {
        throw new Error('Rate cannot be negative');
      }
      if (!warehouseId) {
        throw new Error('Warehouse is required');
      }

      const serials = serialNumbers
        .split(/[\n,]+/)
        .map((s) => s.trim())
        .filter(Boolean);

      return createStockDocument({
        type: StockDocumentType.openingStock,
        date,
        warehouseId,
        postImmediately: true,
        lines: [
          {
            productId: product.id,
            quantity: qtyNum,
            rate: rateNum,
            batchNumber: batchNumber.trim() || null,
            expiresOn: expiresOn || null,
            serialNumbers: serials.length > 0 ? serials : null,
            remarks: remarks.trim() || null,
          },
        ],
      });
    },
    onSuccess: async () => {
      setError(null);
      setSuccess(t('products.saved') || 'Saved');
      window.setTimeout(() => setSuccess(null), 4000);
      setQuantity('1');
      setBatchNumber('');
      setExpiresOn('');
      setSerialNumbers('');
      setRemarks('');
      await queryClient.invalidateQueries({ queryKey: ['stock-ledger', product.id] });
      await queryClient.invalidateQueries({ queryKey: ['stock-valuation'] });
      await queryClient.invalidateQueries({ queryKey: ['product', product.id] });
    },
    onError: (err: any) => {
      setError(
        err?.detail || err?.message || err?.code || 'Failed to post opening stock',
      );
    },
  });

  const totalValue = (Number(quantity) || 0) * (Number(rate) || 0);
  const stockUnit = units.find((u) => u.id === product.stockUnitId)?.code ?? '';

  return (
    <div className="space-y-6">
      {success && (
        <div
          role="status"
          className="rounded-lg border border-emerald-500/20 bg-emerald-500/10 p-3 text-sm text-emerald-700 dark:text-emerald-300"
        >
          {success}
        </div>
      )}
      {error && <Alert>{error}</Alert>}

      <div className="rounded-xl border border-line bg-surface-2/40 p-4">
        <h3 className="text-sm font-semibold text-ink">
          {t('stock.onHand')}: {ledgerQuery.data?.closingQuantity ?? 0} {stockUnit}
        </h3>
        <p className="mt-1 text-xs text-ink-muted">
          {t('stock.opening')}: {ledgerQuery.data?.openingQuantity ?? 0} ·{' '}
          {t('stock.totalIn')}: {ledgerQuery.data?.totalIn ?? 0} · {t('stock.totalOut')}:{' '}
          {ledgerQuery.data?.totalOut ?? 0}
        </p>
      </div>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
        className="card space-y-4 p-5"
      >
        <h4 className="text-sm font-semibold text-ink uppercase tracking-wider">
          {t('products.tabOpeningStock')}
        </h4>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Field label={t('stock.warehouse')}>
            <SearchSelect
              value={warehouseId}
              onChange={setWarehouseId}
              label={t('stock.warehouse')}
              placeholder={t('stock.chooseWarehouse') || 'Choose warehouse'}
              options={(warehouses.data ?? []).map((w) => ({
                value: w.id,
                label: `${w.code} — ${w.name}`,
              }))}
            />
          </Field>

          <Field label={t('stock.date')}>
            <input
              type="date"
              value={date}
              onChange={(e) => setDate(e.target.value)}
              className="field-input"
              required
            />
          </Field>

          <Field label={`${t('stock.quantity')} (${stockUnit || 'Units'})`}>
            <input
              type="number"
              step="any"
              min="0.001"
              value={quantity}
              onChange={(e) => setQuantity(e.target.value)}
              className="field-input text-end"
              required
            />
          </Field>

          <Field label={t('stock.rate')}>
            <input
              type="number"
              step="any"
              min="0"
              value={rate}
              onChange={(e) => setRate(e.target.value)}
              className="field-input text-end"
              required
            />
          </Field>

          <Field
            label={
              t('stock.totalValue', { currency: product.currency, value: '' }) ||
              'Total Value'
            }
          >
            <div className="field-input bg-surface-2 flex items-center justify-end font-semibold text-ink">
              {moneyAlways(totalValue)} {product.currency}
            </div>
          </Field>

          {product.tracksBatches && (
            <>
              <Field label={t('stock.batch')}>
                <input
                  type="text"
                  value={batchNumber}
                  onChange={(e) => setBatchNumber(e.target.value)}
                  placeholder="e.g. BATCH-001"
                  className="field-input"
                />
              </Field>
              <Field label={t('stock.expiresOn')}>
                <input
                  type="date"
                  value={expiresOn}
                  onChange={(e) => setExpiresOn(e.target.value)}
                  className="field-input"
                />
              </Field>
            </>
          )}

          {serialsOn && product.tracksSerialNumbers && (
            <div className="sm:col-span-2">
              <Field label={t('stock.units')}>
                <textarea
                  rows={2}
                  value={serialNumbers}
                  onChange={(e) => setSerialNumbers(e.target.value)}
                  placeholder={
                    t('stock.unitsPlaceholder') ||
                    'Enter serial numbers separated by commas or lines'
                  }
                  className="field-input"
                />
              </Field>
            </div>
          )}

          <div className="sm:col-span-2 lg:col-span-3">
            <Field label={t('stock.narration') || 'Remarks'}>
              <input
                type="text"
                value={remarks}
                onChange={(e) => setRemarks(e.target.value)}
                placeholder="Opening stock initial balance"
                className="field-input"
              />
            </Field>
          </div>
        </div>

        <div className="flex justify-end pt-2">
          <button type="submit" disabled={mutation.isPending} className="btn-primary">
            {mutation.isPending ? t('common.saving') : t('products.tabOpeningStock')}
          </button>
        </div>
      </form>
    </div>
  );
}
