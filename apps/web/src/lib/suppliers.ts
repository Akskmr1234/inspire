import { request } from '@/lib/api';

/**
 * The supplier master of section 13.
 *
 * A supplier is a sub-ledger, which is why these live under purchase rather than under the
 * chart of accounts: a purchase is billed by one, a payment settles against one, and the
 * creditors report sums them.
 */
export interface SupplierContact {
  readonly mobileNumber: string | null;
  readonly phone: string | null;
  readonly email: string | null;
  readonly addressLine1: string | null;
  readonly addressLine2: string | null;
}

/** What the firm owes a supplier on, and for how long. */
export interface SupplierTerms {
  readonly creditLimit: number | null;
  readonly creditDays: number | null;
  readonly isBillWise: boolean;
}

/** The registration details a tax document needs. */
export interface SupplierTaxDetails {
  readonly registrationNumber: string | null;
  /** Compared with the firm's to decide IGST against CGST plus SGST. */
  readonly stateCode: string | null;
}

/** A supplier as the system holds them. */
export interface SupplierSummary {
  readonly supplierId: string;
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  readonly contact: SupplierContact;
  readonly terms: SupplierTerms;
  readonly taxDetails: SupplierTaxDetails;
  readonly currency: string;
  readonly openingBalance: number;
  readonly isActive: boolean;
}

/** What is needed to create one. */
export interface SupplierInput {
  readonly code: string;
  readonly name: string;
  readonly nameArabic?: string | null;
  readonly contact?: Partial<SupplierContact>;
  readonly terms?: Partial<SupplierTerms>;
  readonly taxDetails?: Partial<SupplierTaxDetails>;
  readonly openingBalance?: number;
}

/** Lists suppliers, or finds one by code, name or number. */
export async function listSuppliers(
  search = '',
  activeOnly = true,
): Promise<readonly SupplierSummary[]> {
  const query = new URLSearchParams({ activeOnly: String(activeOnly) });

  if (search.trim()) {
    query.set('search', search.trim());
  }

  return request<readonly SupplierSummary[]>(`/purchase/suppliers?${query}`);
}

/** Creates a supplier. */
export async function createSupplier(input: SupplierInput): Promise<SupplierSummary> {
  return request<SupplierSummary>('/purchase/suppliers', {
    method: 'POST',
    body: input,
  });
}

/** Changes a supplier's details. The code is not among them. */
export async function updateSupplier(
  supplierId: string,
  input: Omit<SupplierInput, 'code' | 'openingBalance'>,
): Promise<SupplierSummary> {
  return request<SupplierSummary>(`/purchase/suppliers/${supplierId}`, {
    method: 'PUT',
    body: input,
  });
}

/**
 * Withdraws a supplier from use, or puts them back.
 *
 * Never deletes: every past purchase and the creditors report point at them, so this only
 * decides whether a new document may name them.
 */
export async function setSupplierActive(
  supplierId: string,
  isActive: boolean,
): Promise<SupplierSummary> {
  return request<SupplierSummary>(`/purchase/suppliers/${supplierId}/active`, {
    method: 'PUT',
    body: { isActive },
  });
}
