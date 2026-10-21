import { request } from '@/lib/api';

/** A bill of materials as the list shows it. */
export interface BomSummary {
  readonly id: string;
  readonly productId: string;
  readonly productCode: string;
  readonly productName: string;
  readonly stockUnitCode: string;
  readonly outputQuantity: number;
  readonly componentCount: number;
  readonly estimatedCost: number;
  readonly isActive: boolean;
}

/** One component, with what a stock unit of it costs today. */
export interface BomComponent {
  readonly productId: string;
  readonly productCode: string;
  readonly productName: string;
  readonly stockUnitCode: string;
  readonly quantity: number;
  readonly unitCost: number;
  readonly tracksBatches: boolean;
  readonly remarks: string | null;
}

/** A bill with its components. */
export interface BomDetail {
  readonly summary: BomSummary;
  readonly narration: string | null;
  readonly components: readonly BomComponent[];
}

/** A bill as entered. Quantities are in each product's stock unit. */
export interface BomInput {
  readonly productId: string;
  readonly outputQuantity: number;
  readonly narration: string | null;
  readonly components: readonly {
    readonly productId: string;
    readonly quantity: number;
    readonly remarks: string | null;
  }[];
}

const BOMS = '/inventory/boms';

/** Lists the firm's bills of materials. */
export function listBoms(includeInactive = false): Promise<readonly BomSummary[]> {
  return request<readonly BomSummary[]>(`${BOMS}?includeInactive=${includeInactive}`);
}

/** Reads one bill with its components. */
export function getBom(id: string): Promise<BomDetail> {
  return request<BomDetail>(`${BOMS}/${id}`);
}

/** Creates a bill for a finished good. */
export function createBom(input: BomInput): Promise<string> {
  return request<string>(BOMS, { method: 'POST', body: input });
}

/** Replaces a bill's output quantity, note and components. */
export function updateBom(id: string, input: BomInput): Promise<void> {
  return request<void>(`${BOMS}/${id}`, { method: 'PUT', body: input });
}

/** Withdraws a bill from use, or brings it back. */
export function setBomActive(id: string, isActive: boolean): Promise<void> {
  return request<void>(`${BOMS}/${id}/active`, { method: 'POST', body: { isActive } });
}
