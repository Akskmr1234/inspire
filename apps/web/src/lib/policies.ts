import { request } from '@/lib/api';

/** What happens when goods would leave a position below zero. */
export const NegativeStockPolicy = { block: 1, warn: 2, allow: 3 } as const;

/** What happens when a credit sale would take a customer past their limit. */
export const CreditLimitPolicy = { ignore: 1, warn: 2, block: 3 } as const;

/**
 * The rules a firm chose that the server enforces.
 *
 * Kept apart from the screens' preferences: these change what a document is allowed to
 * do, so the server validates them when they are saved and reads them when a document
 * posts.
 */
export interface FirmPolicies {
  readonly negativeStock: number;
  readonly creditLimit: number;
  readonly transfersRequireApproval: boolean;
  readonly editsRequireApproval: boolean;
  readonly purchasesUpdateProductRates: boolean;
}

/** What a firm starts with. */
export const DEFAULT_POLICIES: FirmPolicies = {
  negativeStock: NegativeStockPolicy.block,
  creditLimit: CreditLimitPolicy.warn,
  transfersRequireApproval: false,
  editsRequireApproval: false,
  purchasesUpdateProductRates: false,
};

/** Reads the firm's policies. */
export function getPolicies(): Promise<FirmPolicies> {
  return request<FirmPolicies>('/settings/policies');
}

/** Replaces the firm's policies, whole. */
export function savePolicies(policies: FirmPolicies): Promise<void> {
  return request<void>('/settings/policies', { method: 'PUT', body: policies });
}
