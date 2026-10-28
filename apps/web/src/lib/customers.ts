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

/** Where a customer is, and how else they are reached and registered. */
export interface CustomerProfile {
  readonly city: string | null;
  readonly pinCode: string | null;
  readonly website: string | null;
  readonly fax: string | null;
  readonly contactPerson: string | null;
  readonly gstNumber: string | null;
  readonly tinNumber: string | null;
  readonly cstNumber: string | null;
  /** The ISO code of their country, from the country master. */
  readonly countryCode: string | null;
  readonly areaId: string | null;
  readonly routeId: string | null;
  /** Whether they are sent text messages. */
  readonly sendsSms: boolean;
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
  readonly profile: CustomerProfile;
  /** The firm's Primary customer: the default a sale starts with, kept off the master list. */
  readonly isPrimary?: boolean;
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
  readonly profile?: CustomerProfile;
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

/**
 * Changes a customer's details. The code is not among them; the opening amount is, for
 * one typed wrongly when the customer was created.
 */
export async function updateCustomer(
  customerId: string,
  input: Omit<CustomerInput, 'code'>,
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

/** A customer's photograph, as a data URL, or null when there is none. */
export async function getCustomerPhoto(customerId: string): Promise<string | null> {
  const photo = await request<{ readonly content: string | null }>(
    `/sales/customers/${customerId}/photo`,
  );

  return photo.content;
}

/** Sets a customer's photograph; null removes it. */
export async function setCustomerPhoto(
  customerId: string,
  content: string | null,
): Promise<void> {
  await request<void>(`/sales/customers/${customerId}/photo`, {
    method: 'PUT',
    body: { content },
  });
}
