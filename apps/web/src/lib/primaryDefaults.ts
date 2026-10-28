import { listCustomers } from '@/lib/customers';
import {
  listMaster,
  type BrandSummary,
  type CategorySummary,
  type UnitSummary,
} from '@/lib/inventory';
import { listSuppliers } from '@/lib/suppliers';
import { useSettings, type AppSettings } from '@/stores/settings';

/** Reads a master, or nothing when the user may not read it. */
async function rows<TRow>(
  read: () => Promise<readonly TRow[]>,
): Promise<readonly TRow[] | null> {
  try {
    return await read();
  } catch {
    return null;
  }
}

/**
 * Makes the firm's Primary records its defaults in Settings, once.
 *
 * The Primary customer, supplier, category and brand become the defaults a new
 * document or product starts with, where none has been chosen; a firm with a single
 * unit has that one as its stock unit. The godown needs nothing: left unset, Settings
 * already falls back to the master's default, which the Primary godown is.
 *
 * Run after the firm's settings are read, so what it fills in is the firm's. Each
 * master is read on its own, so one this user may not read leaves its default alone
 * rather than stopping the rest; and nothing is marked done unless one could be read.
 */
export async function applyPrimaryDefaults(): Promise<void> {
  if (useSettings.getState().primaryDefaultsApplied) return;

  const [customers, suppliers, categories, brands, units] = await Promise.all([
    rows(() => listCustomers('', true)),
    rows(() => listSuppliers('', true)),
    rows(() => listMaster<CategorySummary>('categories', false)),
    rows(() => listMaster<BrandSummary>('brands', false)),
    rows(() => listMaster<UnitSummary>('units', false)),
  ]);

  if ([customers, suppliers, categories, brands, units].every((read) => read === null)) {
    return;
  }

  const settings = useSettings.getState();
  const unset = (value: string): boolean => value.trim() === '';

  const customer = customers?.find((row) => row.isPrimary === true);
  const supplier = suppliers?.find((row) => row.isPrimary === true);
  const category = categories?.find((row) => row.isPrimary === true);
  const brand = brands?.find((row) => row.isPrimary === true);
  const unit = units?.length === 1 ? units[0] : undefined;

  const patch: Partial<AppSettings> = {
    primaryDefaultsApplied: true,
    ...(customer && unset(settings.defaultCustomerId)
      ? { defaultCustomerId: customer.customerId }
      : {}),
    ...(supplier && unset(settings.defaultSupplierId)
      ? { defaultSupplierId: supplier.supplierId }
      : {}),
    ...(category && unset(settings.defaultCategoryId)
      ? { defaultCategoryId: category.id }
      : {}),
    ...(brand && unset(settings.defaultBrandId) ? { defaultBrandId: brand.id } : {}),
    ...(unit && unset(settings.defaultStockUnitId)
      ? { defaultStockUnitId: unit.id }
      : {}),
  };

  settings.update(patch);
}
