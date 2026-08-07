import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
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
import { listMaster, type BrandSummary, type UnitSummary } from '@/lib/inventory';

/**
 * The product editor.
 *
 * Three tabs, as section 8.1 asks for, and each one saves on its own. That is not
 * only a layout choice: the API takes a product a tab at a time, so repricing sends
 * the rates and nothing else. A single save-everything button would make every edit
 * resend every field and quietly overwrite whatever a colleague changed a minute
 * earlier — on a master this shared, that is a real event rather than a hypothetical.
 *
 * The specification's third tab is Images. It is not here because nothing stores a
 * file yet; showing an empty tab would claim otherwise. Barcodes take the slot in the
 * meantime, since they are a grid of their own with their own add and remove.
 */
export function ProductEditor({
  productId,
  onClose,
}: {
  readonly productId: string;
  readonly onClose: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [tab, setTab] = useState<'description' | 'details' | 'barcodes'>('description');
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const product = useQuery<ProductDetail, ApiError>({
    queryKey: ['product', productId],
    queryFn: () => getProduct(productId),
  });

  const units = useQuery<readonly UnitSummary[], ApiError>({
    queryKey: ['units', false],
    queryFn: () => listMaster<UnitSummary>('units', false),
  });

  const brands = useQuery<readonly BrandSummary[], ApiError>({
    queryKey: ['brands', false],
    queryFn: () => listMaster<BrandSummary>('brands', false),
  });

  const mutation = useMutation<void, ApiError, () => Promise<void>>({
    mutationFn: (action) => action(),
    onSuccess: async () => {
      setError(null);
      setSaved(true);
      window.setTimeout(() => setSaved(false), 2000);

      await queryClient.invalidateQueries({ queryKey: ['product', productId] });
      await queryClient.invalidateQueries({ queryKey: ['products'] });
    },
    // The server owns the rules — an MRP a retail rate may not exceed, a unit that
    // does not convert — so its own message is shown rather than one guessed at here.
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const run = (action: () => Promise<void>): void => {
    setError(null);
    mutation.mutate(action);
  };

  if (product.isPending) {
    return <p className="text-sm text-slate-500">{t('common.loading')}</p>;
  }

  if (product.isError || !product.data) {
    return (
      <Alert>
        {product.error?.detail ?? product.error?.code ?? t('products.notFound')}
      </Alert>
    );
  }

  const row = product.data;

  return (
    <section className="space-y-4">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">
            {row.code} — {row.description}
          </h2>
          <p className="text-xs text-slate-500">
            {row.isActive ? t('masters.active') : t('masters.withdrawn')}
            {row.isDiscontinued ? ` · ${t('products.discontinued')}` : ''}
          </p>
        </div>

        <div className="flex items-center gap-2">
          {saved && (
            <span className="text-xs text-emerald-600">{t('products.saved')}</span>
          )}
          <button type="button" onClick={onClose} className="btn-secondary">
            {t('products.backToList')}
          </button>
        </div>
      </header>

      {error && <Alert>{error}</Alert>}

      <nav className="flex gap-1 border-b border-slate-200 dark:border-slate-800">
        <Tab active={tab === 'description'} onClick={() => setTab('description')}>
          {t('products.tabDescription')}
        </Tab>
        <Tab active={tab === 'details'} onClick={() => setTab('details')}>
          {t('products.tabDetails')}
        </Tab>
        <Tab active={tab === 'barcodes'} onClick={() => setTab('barcodes')}>
          {t('products.tabBarcodes')} ({row.barcodes.length})
        </Tab>
      </nav>

      {/*
        Keyed on the product so switching rows rebuilds the form state. Without it the
        fields would keep the previous product's values until the query resolved, which
        is the shape of an edit landing on the wrong record.
      */}
      {tab === 'description' && (
        <DescriptionTab key={row.id} product={row} run={run} busy={mutation.isPending} />
      )}

      {tab === 'details' && (
        <DetailsTab
          key={row.id}
          product={row}
          units={units.data ?? []}
          brands={brands.data ?? []}
          run={run}
          busy={mutation.isPending}
        />
      )}

      {tab === 'barcodes' && (
        <BarcodesTab product={row} run={run} busy={mutation.isPending} />
      )}
    </section>
  );
}

/**
 * Tab 1 — what the product is, what it costs, and what it sells for.
 *
 * Rack and bin sit here rather than on Details, where section 8.1 lists them. They
 * travel with the descriptive fields in the API, and splitting them across tabs would
 * mean saving Details had to resend the whole description — the exact overwrite the
 * per-tab endpoints exist to avoid.
 */
function DescriptionTab({
  product,
  run,
  busy,
}: {
  readonly product: ProductDetail;
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();

  const [text, setText] = useState({
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
  });

  const [device, setDevice] = useState({
    device: product.device ?? '',
    colour: product.colour ?? '',
    battery: product.battery ?? '',
    ram: product.ram ?? '',
    storage: product.storage ?? '',
  });

  const [rates, setRates] = useState({
    costingMethod: String(product.costingMethod),
    cost: String(product.cost),
    profitPercentage: String(product.profitPercentage),
    corPercentage: String(product.corPercentage),
    retailRate: String(product.retailRate),
    wholesaleRate: String(product.wholesaleRate),
    otherRate: String(product.otherRate),
    maximumRetailPrice: String(product.maximumRetailPrice),
  });

  return (
    <form
      className="space-y-6"
      onSubmit={(event) => {
        event.preventDefault();

        run(async () => {
          // Three calls because the API groups the fields three ways. Sequential
          // rather than parallel: if the rates are refused, the description has
          // already landed and the reason names one thing rather than two.
          await saveProductTab(product.id, 'description', {
            description: text.description.trim(),
            descriptionArabic: blank(text.descriptionArabic),
            shortDescription: blank(text.shortDescription),
            itemName: blank(text.itemName),
            manufacturer: blank(text.manufacturer),
            label: blank(text.label),
            size: blank(text.size),
            origin: blank(text.origin),
            rack: blank(text.rack),
            bin: blank(text.bin),
          });

          await saveProductTab(product.id, 'rates', {
            costingMethod: Number(rates.costingMethod),
            cost: number(rates.cost),
            profitPercentage: number(rates.profitPercentage),
            corPercentage: number(rates.corPercentage),
            retailRate: number(rates.retailRate),
            wholesaleRate: number(rates.wholesaleRate),
            otherRate: number(rates.otherRate),
            maximumRetailPrice: number(rates.maximumRetailPrice),
          });

          await saveProductTab(product.id, 'device', {
            device: blank(device.device),
            colour: blank(device.colour),
            battery: blank(device.battery),
            ram: blank(device.ram),
            storage: blank(device.storage),
          });
        });
      }}
    >
      <Section title={t('products.identity')}>
        {/* Read-only, and not merely disabled in the UI: nothing on the server
            changes it either. The code is how this product is named on every
            document already entered. */}
        <Field label={t('masters.code')}>
          <input value={product.code} readOnly className="field-input opacity-60" />
        </Field>

        <Field label={t('products.description')}>
          <input
            value={text.description}
            onChange={(e) => setText({ ...text, description: e.target.value })}
            className="field-input"
            required
          />
        </Field>

        <Field label={t('products.descriptionArabic')}>
          <input
            dir="rtl"
            value={text.descriptionArabic}
            onChange={(e) => setText({ ...text, descriptionArabic: e.target.value })}
            className="field-input"
          />
        </Field>

        <Field label={t('products.shortDescription')}>
          <input
            value={text.shortDescription}
            onChange={(e) => setText({ ...text, shortDescription: e.target.value })}
            className="field-input"
          />
        </Field>

        <Field label={t('products.itemName')}>
          <input
            value={text.itemName}
            onChange={(e) => setText({ ...text, itemName: e.target.value })}
            className="field-input"
          />
        </Field>

        <Field label={t('products.manufacturer')}>
          <input
            value={text.manufacturer}
            onChange={(e) => setText({ ...text, manufacturer: e.target.value })}
            className="field-input"
          />
        </Field>

        <Field label={t('products.label')}>
          <input
            value={text.label}
            onChange={(e) => setText({ ...text, label: e.target.value })}
            className="field-input"
          />
        </Field>

        <Field label={t('products.size')}>
          <input
            value={text.size}
            onChange={(e) => setText({ ...text, size: e.target.value })}
            className="field-input"
          />
        </Field>

        <Field label={t('products.origin')}>
          <input
            value={text.origin}
            onChange={(e) => setText({ ...text, origin: e.target.value })}
            className="field-input"
          />
        </Field>

        <Field label={t('products.rack')}>
          <input
            value={text.rack}
            onChange={(e) => setText({ ...text, rack: e.target.value })}
            className="field-input"
          />
        </Field>

        <Field label={t('products.bin')}>
          <input
            value={text.bin}
            onChange={(e) => setText({ ...text, bin: e.target.value })}
            className="field-input"
          />
        </Field>
      </Section>

      <Section title={`${t('products.rates')} · ${product.currency}`}>
        <Field label={t('products.costingMethod')}>
          <select
            value={rates.costingMethod}
            onChange={(e) => setRates({ ...rates, costingMethod: e.target.value })}
            className="field-input"
          >
            <option value="1">{t('products.costingLastPurchase')}</option>
            <option value="2">{t('products.costingAverage')}</option>
          </select>
        </Field>

        <NumberField
          label={t('products.cost')}
          value={rates.cost}
          onChange={(value) => setRates({ ...rates, cost: value })}
        />
        <NumberField
          label={t('products.profitPercentage')}
          value={rates.profitPercentage}
          onChange={(value) => setRates({ ...rates, profitPercentage: value })}
        />
        <NumberField
          label={t('products.corPercentage')}
          value={rates.corPercentage}
          onChange={(value) => setRates({ ...rates, corPercentage: value })}
        />
        <NumberField
          label={t('products.retailRate')}
          value={rates.retailRate}
          onChange={(value) => setRates({ ...rates, retailRate: value })}
        />
        <NumberField
          label={t('products.wholesaleRate')}
          value={rates.wholesaleRate}
          onChange={(value) => setRates({ ...rates, wholesaleRate: value })}
        />
        <NumberField
          label={t('products.otherRate')}
          value={rates.otherRate}
          onChange={(value) => setRates({ ...rates, otherRate: value })}
        />
        <NumberField
          label={t('products.maximumRetailPrice')}
          value={rates.maximumRetailPrice}
          onChange={(value) => setRates({ ...rates, maximumRetailPrice: value })}
          hint={t('products.mrpHint')}
        />
      </Section>

      <Section title={t('products.deviceAttributes')}>
        <Field label={t('products.device')}>
          <input
            value={device.device}
            onChange={(e) => setDevice({ ...device, device: e.target.value })}
            className="field-input"
          />
        </Field>
        <Field label={t('products.colour')}>
          <input
            value={device.colour}
            onChange={(e) => setDevice({ ...device, colour: e.target.value })}
            className="field-input"
          />
        </Field>
        <Field label={t('products.battery')}>
          <input
            value={device.battery}
            onChange={(e) => setDevice({ ...device, battery: e.target.value })}
            className="field-input"
          />
        </Field>
        <Field label={t('products.ram')}>
          <input
            value={device.ram}
            onChange={(e) => setDevice({ ...device, ram: e.target.value })}
            className="field-input"
          />
        </Field>
        <Field label={t('products.storage')}>
          <input
            value={device.storage}
            onChange={(e) => setDevice({ ...device, storage: e.target.value })}
            className="field-input"
          />
        </Field>
      </Section>

      <button type="submit" disabled={busy} className="btn-primary">
        {t('products.save')}
      </button>
    </form>
  );
}

/** Tab 2 — how the product is traded, stocked, and tracked. */
function DetailsTab({
  product,
  units,
  brands,
  run,
  busy,
}: {
  readonly product: ProductDetail;
  readonly units: readonly UnitSummary[];
  readonly brands: readonly BrandSummary[];
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();

  const [form, setForm] = useState({
    purchaseUnitId: product.purchaseUnitId,
    salesUnitId: product.salesUnitId,
    minimumLevel: String(product.minimumLevel),
    reorderLevel: String(product.reorderLevel),
    maximumLevel: String(product.maximumLevel),
    movement: String(product.movement),
    tracksBatches: product.tracksBatches,
    tracksSerialNumbers: product.tracksSerialNumbers,
    shelfLifeDays: product.shelfLifeDays === null ? '' : String(product.shelfLifeDays),
    isPacking: product.isPacking,
  });

  // The units that convert to this product's stock unit. Offering the rest would
  // let somebody buy in kilograms what is stocked in litres, and the server would
  // refuse it after the fact; better not to offer it.
  const stockUnit = units.find((unit) => unit.id === product.stockUnitId);
  const group = stockUnit?.baseUnitId ?? stockUnit?.id;

  const convertible = units.filter(
    (unit) => group !== undefined && (unit.baseUnitId ?? unit.id) === group,
  );

  return (
    <form
      className="space-y-6"
      onSubmit={(event) => {
        event.preventDefault();

        run(async () => {
          await saveProductTab(product.id, 'stocking', {
            purchaseUnitId: form.purchaseUnitId,
            salesUnitId: form.salesUnitId,
            minimumLevel: number(form.minimumLevel),
            reorderLevel: number(form.reorderLevel),
            maximumLevel: number(form.maximumLevel),
            movement: Number(form.movement),
            tracksBatches: form.tracksBatches,
            tracksSerialNumbers: form.tracksSerialNumbers,
            shelfLifeDays: form.shelfLifeDays.trim()
              ? Number(form.shelfLifeDays)
              : null,
            isPacking: form.isPacking,
          });
        });
      }}
    >
      <Section title={t('products.units')}>
        <Field label={t('products.stockUnit')}>
          {/* Fixed once stock has been counted in it: changing it would restate
              every quantity ever recorded against this product. */}
          <input
            readOnly
            value={stockUnit ? `${stockUnit.code} — ${stockUnit.name}` : ''}
            className="field-input opacity-60"
          />
        </Field>

        <Field label={t('products.purchaseUnit')}>
          <select
            value={form.purchaseUnitId}
            onChange={(e) => setForm({ ...form, purchaseUnitId: e.target.value })}
            className="field-input"
          >
            {convertible.map((unit) => (
              <option key={unit.id} value={unit.id}>
                {unit.code} — {unit.name}
                {unit.baseUnitId ? ` (×${unit.conversionFactor})` : ''}
              </option>
            ))}
          </select>
        </Field>

        <Field label={t('products.salesUnit')}>
          <select
            value={form.salesUnitId}
            onChange={(e) => setForm({ ...form, salesUnitId: e.target.value })}
            className="field-input"
          >
            {convertible.map((unit) => (
              <option key={unit.id} value={unit.id}>
                {unit.code} — {unit.name}
                {unit.baseUnitId ? ` (×${unit.conversionFactor})` : ''}
              </option>
            ))}
          </select>
        </Field>

        <Field label={t('products.brand')}>
          <input
            readOnly
            value={
              brands.find((brand) => brand.id === product.brandId)?.name ??
              t('products.noBrand')
            }
            className="field-input opacity-60"
          />
        </Field>
      </Section>

      <Section title={t('products.orderLevels')}>
        <NumberField
          label={t('products.minimumLevel')}
          value={form.minimumLevel}
          onChange={(value) => setForm({ ...form, minimumLevel: value })}
        />
        <NumberField
          label={t('products.reorderLevel')}
          value={form.reorderLevel}
          onChange={(value) => setForm({ ...form, reorderLevel: value })}
        />
        <NumberField
          label={t('products.maximumLevel')}
          value={form.maximumLevel}
          onChange={(value) => setForm({ ...form, maximumLevel: value })}
          hint={t('products.maximumLevelHint')}
        />

        <Field label={t('products.movement')}>
          <select
            value={form.movement}
            onChange={(e) => setForm({ ...form, movement: e.target.value })}
            className="field-input"
          >
            <option value="0">{t('products.movementUnclassified')}</option>
            <option value="1">{t('products.movementFast')}</option>
            <option value="2">{t('products.movementNormal')}</option>
            <option value="3">{t('products.movementSlow')}</option>
            <option value="4">{t('products.movementDead')}</option>
          </select>
        </Field>
      </Section>

      <Section title={t('products.tracking')}>
        <Check
          label={t('products.tracksBatches')}
          checked={form.tracksBatches}
          onChange={(value) => setForm({ ...form, tracksBatches: value })}
        />
        {/* Both at once is allowed and normal: a handset arrives in a batch and
            still carries its own IMEI. */}
        <Check
          label={t('products.tracksSerialNumbers')}
          checked={form.tracksSerialNumbers}
          onChange={(value) => setForm({ ...form, tracksSerialNumbers: value })}
        />
        <Check
          label={t('products.isPacking')}
          checked={form.isPacking}
          onChange={(value) => setForm({ ...form, isPacking: value })}
        />

        {form.tracksBatches && (
          <NumberField
            label={t('products.shelfLifeDays')}
            value={form.shelfLifeDays}
            onChange={(value) => setForm({ ...form, shelfLifeDays: value })}
            hint={t('products.shelfLifeHint')}
          />
        )}
      </Section>

      <div className="flex flex-wrap items-center gap-3">
        <button type="submit" disabled={busy} className="btn-primary">
          {t('products.save')}
        </button>

        {/* The two flags mean different things and are routinely confused, so they
            are two buttons with their own words rather than one toggle. */}
        <button
          type="button"
          disabled={busy}
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
          {product.isDiscontinued ? t('products.resume') : t('products.discontinue')}
        </button>

        <button
          type="button"
          disabled={busy}
          className="btn-secondary"
          onClick={() =>
            run(async () => {
              await setProductFlag(product.id, 'active', !product.isActive);
            })
          }
        >
          {product.isActive ? t('masters.withdraw') : t('masters.restore')}
        </button>
      </div>
    </form>
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
        className="flex flex-wrap items-end gap-3 rounded-xl border border-slate-200 p-3 dark:border-slate-800"
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

      <div className="overflow-auto rounded-xl border border-slate-200 dark:border-slate-800">
        <table className="w-full border-collapse text-sm">
          <thead className="bg-slate-100 dark:bg-slate-800">
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
              <th />
            </tr>
          </thead>
          <tbody>
            {product.barcodes.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-3 py-6 text-center text-sm text-slate-500">
                  {t('products.noBarcodes')}
                </td>
              </tr>
            ) : (
              product.barcodes.map((row) => (
                <tr
                  key={row.id}
                  className="border-t border-slate-100 dark:border-slate-900"
                >
                  <td className="px-3 py-1.5 font-mono">{row.barcode}</td>
                  <td className="cell-numeric">{row.cost.toFixed(2)}</td>
                  <td className="cell-numeric">{row.retailRate.toFixed(2)}</td>
                  <td className="cell-numeric">{row.wholesaleRate.toFixed(2)}</td>
                  <td className="cell-numeric">{row.maximumRetailPrice.toFixed(2)}</td>
                  <td className="px-3 py-1.5 text-end">
                    <button
                      type="button"
                      disabled={busy}
                      className="rounded border border-slate-300 px-2 py-0.5 text-xs text-slate-600 transition hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800"
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

function Section({
  title,
  children,
}: {
  readonly title: string;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <fieldset className="space-y-3">
      <legend className="text-sm font-semibold text-slate-500 uppercase">{title}</legend>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">{children}</div>
    </fieldset>
  );
}

function Field({
  label,
  children,
}: {
  readonly label: string;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <label className="block">
      <span className="field-label">{label}</span>
      {children}
    </label>
  );
}

function NumberField({
  label,
  value,
  onChange,
  hint,
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly hint?: string;
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
      {hint && <span className="mt-1 block text-xs text-slate-500">{hint}</span>}
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
    <label className="flex flex-col gap-1 text-sm">
      <span className="text-slate-600 dark:text-slate-400">{label}</span>
      <input
        {...(numeric ? { type: 'number', step: 'any' } : {})}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={clsx(
          'w-32 rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900',
          numeric && 'text-end',
        )}
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
        '-mb-px border-b-2 px-4 py-2 text-sm font-medium transition',
        active
          ? 'border-brand-600 text-brand-700 dark:text-brand-100'
          : 'border-transparent text-slate-500 hover:text-slate-800 dark:hover:text-slate-200',
      )}
    >
      {children}
    </button>
  );
}

function Alert({ children }: { readonly children: React.ReactNode }): React.JSX.Element {
  return (
    <div
      role="alert"
      className="rounded-lg border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200"
    >
      {children}
    </div>
  );
}
