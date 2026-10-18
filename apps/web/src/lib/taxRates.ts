import { request } from '@/lib/api';

/** A tax rate a product can carry: VAT 5%, Exempt 0%. */
export interface TaxRate {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  readonly percentage: number;
  /** Whether new products start with it. */
  readonly isDefault: boolean;
  readonly isActive: boolean;
}

export interface SaveTaxRate {
  readonly code?: string | null;
  readonly name: string;
  readonly nameArabic: string | null;
  readonly percentage: number;
  readonly isDefault: boolean;
}

const TAX_RATES = '/inventory/tax-rates';

export const listTaxRates = (includeInactive = false): Promise<readonly TaxRate[]> =>
  request<readonly TaxRate[]>(`${TAX_RATES}?includeInactive=${String(includeInactive)}`);

export const createTaxRate = (body: SaveTaxRate): Promise<string> =>
  request<string>(TAX_RATES, { method: 'POST', body });

export const updateTaxRate = (id: string, body: SaveTaxRate): Promise<void> =>
  request<void>(`${TAX_RATES}/${id}`, { method: 'PUT', body });

export const setTaxRateActive = (id: string, isActive: boolean): Promise<void> =>
  request<void>(`${TAX_RATES}/${id}/active`, { method: 'POST', body: { isActive } });
