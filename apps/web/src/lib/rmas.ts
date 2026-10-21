import { request } from '@/lib/api';

/** Which way the goods go back. */
export const RmaDirection = { fromCustomer: 1, toSupplier: 2 } as const;

/** Where a return stands. */
export const RmaStatus = {
  requested: 1,
  approved: 2,
  received: 3,
  underInspection: 4,
  repaired: 5,
  replaced: 6,
  refunded: 7,
  closed: 8,
  rejected: 9,
  cancelled: 10,
} as const;

/** What the inspection found. */
export const RmaCondition = { good: 1, repairable: 2, defective: 3, damaged: 4 } as const;

/** A return as the list shows it. */
export interface RmaSummary {
  readonly id: string;
  readonly number: string;
  readonly date: string;
  readonly direction: number;
  readonly partyLedgerId: string;
  readonly partyName: string;
  readonly invoiceReference: string | null;
  readonly reason: string;
  readonly quantity: number;
  readonly status: number;
  readonly condition: number | null;
  readonly outcomeNumber: string | null;
}

/** A product going back. */
export interface RmaLineDetail {
  readonly productId: string;
  readonly productCode: string;
  readonly productName: string;
  readonly stockUnitCode: string;
  readonly quantity: number;
  readonly reason: string | null;
  readonly serialNumber: string | null;
  readonly tracksBatches: boolean;
  readonly batchId: string | null;
  readonly batchNumber: string | null;
}

/** One step in a return's history. */
export interface RmaEventDetail {
  readonly status: number;
  readonly at: string;
  readonly userName: string;
  readonly note: string | null;
}

/** A return in full. */
export interface RmaDetail {
  readonly summary: RmaSummary;
  readonly warehouseId: string;
  readonly warehouseName: string;
  readonly salesInvoiceId: string | null;
  readonly purchaseInvoiceId: string | null;
  readonly linkedInvoiceNumber: string | null;
  readonly narration: string | null;
  readonly inspectionNotes: string | null;
  readonly inspectedOn: string | null;
  readonly replacementDocumentId: string | null;
  readonly creditNoteId: string | null;
  readonly debitNoteId: string | null;
  readonly lines: readonly RmaLineDetail[];
  readonly photos: readonly { readonly id: string; readonly caption: string | null }[];
  readonly history: readonly RmaEventDetail[];
}

/** A return as entered. Date, direction, party and godown are read only when it is created. */
export interface RmaInput {
  readonly date: string;
  readonly direction: number;
  readonly partyLedgerId: string;
  readonly warehouseId: string;
  readonly reason: string;
  readonly narration: string | null;
  readonly invoiceReference: string | null;
  readonly salesInvoiceId: string | null;
  readonly purchaseInvoiceId: string | null;
  readonly lines: readonly {
    readonly productId: string;
    readonly quantity: number;
    readonly reason: string | null;
    readonly serialNumber: string | null;
    readonly batchId: string | null;
  }[];
}

/** The document an outcome raised. */
export interface RmaOutcome {
  readonly documentId: string | null;
  readonly number: string | null;
}

/** The steps that need nothing but a note. */
export type RmaStep = 'approve' | 'reject' | 'cancel' | 'receive' | 'repair' | 'close';

const RMAS = '/service/rmas';

/** Lists returns, newest first. */
export function listRmas(
  direction: number | '',
  status: number | '',
  search: string,
  from: string,
  to: string,
): Promise<readonly RmaSummary[]> {
  const query = new URLSearchParams();

  if (direction !== '') query.set('direction', String(direction));
  if (status !== '') query.set('status', String(status));
  if (search.trim()) query.set('search', search.trim());
  if (from) query.set('from', from);
  if (to) query.set('to', to);

  return request<readonly RmaSummary[]>(`${RMAS}?${query.toString()}`);
}

/** Reads one return. */
export function getRma(id: string): Promise<RmaDetail> {
  return request<RmaDetail>(`${RMAS}/${id}`);
}

/** Asks for a return. */
export function createRma(input: RmaInput): Promise<string> {
  return request<string>(RMAS, { method: 'POST', body: input });
}

/** Replaces what a return awaiting approval says. */
export function updateRma(id: string, input: RmaInput): Promise<void> {
  return request<void>(`${RMAS}/${id}`, { method: 'PUT', body: input });
}

/** Takes a step that needs only a note. */
export function stepRma(id: string, step: RmaStep, note: string | null): Promise<void> {
  return request<void>(`${RMAS}/${id}/${step}`, { method: 'POST', body: { note } });
}

/** Records what the inspection found. */
export function inspectRma(
  id: string,
  condition: number,
  notes: string | null,
): Promise<void> {
  return request<void>(`${RMAS}/${id}/inspect`, {
    method: 'POST',
    body: { condition, notes },
  });
}

/** Keeps a photograph as evidence. */
export function addRmaPhoto(
  id: string,
  content: string,
  caption: string | null,
): Promise<string> {
  return request<string>(`${RMAS}/${id}/photos`, {
    method: 'POST',
    body: { content, caption },
  });
}

/** Removes a photograph. */
export function removeRmaPhoto(id: string, photoId: string): Promise<void> {
  return request<void>(`${RMAS}/${id}/photos/${photoId}`, { method: 'DELETE' });
}

/** Reads one photograph's picture. */
export async function getRmaPhoto(id: string, photoId: string): Promise<string> {
  const photo = await request<{ readonly content: string }>(
    `${RMAS}/${id}/photos/${photoId}`,
  );
  return photo.content;
}

/** Exchanges the goods through the godown of saleable stock. */
export function replaceRma(
  id: string,
  stockWarehouseId: string,
  note: string | null,
): Promise<RmaOutcome> {
  return request<RmaOutcome>(`${RMAS}/${id}/replace`, {
    method: 'POST',
    body: { stockWarehouseId, note },
  });
}

/** Settles the return with a draft credit note or debit note. */
export function refundRma(id: string, note: string | null): Promise<RmaOutcome> {
  return request<RmaOutcome>(`${RMAS}/${id}/refund`, { method: 'POST', body: { note } });
}

/**
 * Scales a photograph down to a JPEG small enough to keep, at most 1280 pixels on its
 * longer side - plenty to show a scratch or a cracked corner.
 */
export function shrinkEvidence(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new Error('unreadable'));
    reader.onload = () => {
      const image = new Image();
      image.onerror = () => reject(new Error('not a picture'));
      image.onload = () => {
        const scale = Math.min(1, 1280 / Math.max(image.width, image.height));
        const canvas = document.createElement('canvas');
        canvas.width = Math.round(image.width * scale);
        canvas.height = Math.round(image.height * scale);
        canvas.getContext('2d')?.drawImage(image, 0, 0, canvas.width, canvas.height);
        resolve(canvas.toDataURL('image/jpeg', 0.8));
      };
      image.src = String(reader.result);
    };
    reader.readAsDataURL(file);
  });
}
