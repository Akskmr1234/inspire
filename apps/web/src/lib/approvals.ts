import { request } from '@/lib/api';

/** The kinds of posted document whose changes can be held for approval. */
export const EditDocumentKind = {
  salesInvoice: 1,
  purchaseInvoice: 2,
  voucher: 3,
} as const;

/** What a held change asks for. */
export const EditAction = { edit: 1, cancel: 2 } as const;

/** Where a held change stands. */
export const EditRequestStatus = {
  pending: 1,
  approved: 2,
  rejected: 3,
  withdrawn: 4,
} as const;

/** A change to a posted document held for approval. */
export interface EditRequestSummary {
  readonly id: string;
  readonly kind: number;
  readonly documentId: string;
  readonly documentNumber: string;
  readonly action: number;
  readonly reason: string | null;
  readonly status: number;
  readonly requestedByName: string;
  readonly requestedAtUtc: string;
  readonly decidedByName: string | null;
  readonly decidedAtUtc: string | null;
  readonly decisionNote: string | null;
  readonly canDecide: boolean;
  readonly isOwn: boolean;
}

/** A held change with the change itself. */
export interface EditRequestDetail {
  readonly summary: EditRequestSummary;
  readonly payload: string;
}

/**
 * Whether what an edit or a cancellation answered is a change held for approval rather
 * than the change made: the server answers 202 with the request it is waiting in.
 */
export function isHeldForApproval(value: unknown): value is EditRequestSummary {
  return (
    typeof value === 'object' &&
    value !== null &&
    'documentNumber' in value &&
    'canDecide' in value &&
    'requestedByName' in value
  );
}

const REQUESTS = '/platform/edit-requests';

/** Lists the held changes the user may decide, and those they asked for. */
export function listEditRequests(
  status: number | '',
): Promise<readonly EditRequestSummary[]> {
  return request<readonly EditRequestSummary[]>(
    status === '' ? REQUESTS : `${REQUESTS}?status=${status}`,
  );
}

/** Reads one held change. */
export function getEditRequest(id: string): Promise<EditRequestDetail> {
  return request<EditRequestDetail>(`${REQUESTS}/${id}`);
}

/** Approves a held change: it is made exactly as it was asked for. */
export function approveEditRequest(id: string, note: string | null): Promise<void> {
  return request<void>(`${REQUESTS}/${id}/approve`, { method: 'POST', body: { note } });
}

/** Refuses a held change. */
export function rejectEditRequest(id: string, note: string): Promise<void> {
  return request<void>(`${REQUESTS}/${id}/reject`, { method: 'POST', body: { note } });
}

/** Withdraws a change the user asked for. */
export function withdrawEditRequest(id: string): Promise<void> {
  return request<void>(`${REQUESTS}/${id}/withdraw`, { method: 'POST', body: {} });
}
