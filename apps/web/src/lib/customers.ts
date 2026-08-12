import { request } from '@/lib/api';

/**
 * The customer master of section 12.1.
 *
 * A customer is a sub-ledger, which is why these live under sales rather than under the
 * chart of accounts: an invoice is billed to one, a receipt settles against one, and the
 * debtors report sums them.
 */
export interface CustomerContact {
  readonly mobileNumber: string | null;
  readonly phone: string | null;
  readonly email: string | null;
  readonly addressLine1: string | null;
  readonly addressLine2: string | null;
}

/** What a customer owes on, and for how long. */
export interface CustomerTerms {
  readonly creditLimit: number | null;
  readonly creditDays: number | null;
  readonly isBillWise: boolean;
}

/** The registration details a tax document needs. */
export interface CustomerTaxDetails {
  readonly registrationNumber: string | null;
  /** Compared with the firm's to decide IGST against CGST plus SGST. */
  readonly stateCode: string | null;
}

/** A customer as the system holds them. */
export interface CustomerSummary {
  readonly customerId: string;
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  readonly contact: CustomerContact;
  readonly terms: CustomerTerms;
  readonly taxDetails: CustomerTaxDetails;
  readonly currency: string;
  readonly openingBalance: number;
  readonly isActive: boolean;
}

/** What is needed to create one. */
export interface CustomerInput {
  readonly code: string;
  readonly name: string;
  readonly nameArabic?: string | null;
  readonly contact?: Partial<CustomerContact>;
  readonly terms?: Partial<CustomerTerms>;
  readonly taxDetails?: Partial<CustomerTaxDetails>;
  readonly openingBalance?: number;
}

/**
 * Lists customers, or finds one by whatever a counter has to hand.
 *
 * One search across code, name and mobile number, because that is what section 12.1's
 * lookup is: somebody typing what they have into one box while a customer waits.
 */
export async function listCustomers(
  search = '',
  activeOnly = true,
): Promise<readonly CustomerSummary[]> {
  const query = new URLSearchParams({ activeOnly: String(activeOnly) });

  if (search.trim()) {
    query.set('search', search.trim());
  }

  return request<readonly CustomerSummary[]>(`/sales/customers?${query}`);
}

/** Creates a customer. */
export async function createCustomer(input: CustomerInput): Promise<CustomerSummary> {
  return request<CustomerSummary>('/sales/customers', {
    method: 'POST',
    body: input,
  });
}

/** Changes a customer's details. The code is not among them. */
export async function updateCustomer(
  customerId: string,
  input: Omit<CustomerInput, 'code' | 'openingBalance'>,
): Promise<CustomerSummary> {
  return request<CustomerSummary>(`/sales/customers/${customerId}`, {
    method: 'PUT',
    body: input,
  });
}

/**
 * Withdraws a customer from use, or puts them back.
 *
 * Never deletes: every past invoice and the debtors report point at them, so this only
 * decides whether a new document may name them.
 */
export async function setCustomerActive(
  customerId: string,
  isActive: boolean,
): Promise<CustomerSummary> {
  return request<CustomerSummary>(`/sales/customers/${customerId}/active`, {
    method: 'PUT',
    body: { isActive },
  });
}
