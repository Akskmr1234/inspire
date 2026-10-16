import { useQuery } from '@tanstack/react-query';
import { request, type ApiError } from '@/lib/api';

/** The document types a firm sets numbering for, as the API names them. */
export const DocumentType = {
  salesInvoice: 'sales.invoice',
  salesReturn: 'sales.return',
  salesOrder: 'sales.order',
  purchaseInvoice: 'purchase.invoice',
  purchaseReturn: 'purchase.return',
  purchaseOrder: 'purchase.order',
  cashReceipt: 'accounting.cash-receipt',
  bankReceipt: 'accounting.bank-receipt',
  cashPayment: 'accounting.cash-payment',
  bankPayment: 'accounting.bank-payment',
  journal: 'accounting.journal',
} as const;

export type DocumentTypeName = (typeof DocumentType)[keyof typeof DocumentType];

/** How one document type is numbered, as the server holds it. */
export interface DocumentNumbering {
  readonly documentType: DocumentTypeName;
  readonly prefix: string | null;
  readonly suffix: string | null;
  readonly separator: string;
  readonly includeYear: boolean;
  readonly nextNumber: number;
  /** The next number, formatted exactly as it would be issued. */
  readonly next: string;
  /** False until the first document of the type is saved: this is the default. */
  readonly isConfigured: boolean;
  /** The financial year's label, as it appears in a number that includes the year. */
  readonly yearLabel: string;
}

/** What may be changed about a type's numbering. */
export interface NumberingFormat {
  readonly prefix: string | null;
  readonly suffix: string | null;
  readonly separator: string;
  readonly includeYear: boolean;
  readonly nextNumber?: number | null;
  readonly date?: string;
}

export function listNumbering(date: string): Promise<readonly DocumentNumbering[]> {
  return request<readonly DocumentNumbering[]>(`/numbering?date=${date}`);
}

export function peekNumber(
  documentType: DocumentTypeName,
  date: string,
): Promise<DocumentNumbering> {
  return request<DocumentNumbering>(`/numbering/${documentType}/next?date=${date}`);
}

export function setNumbering(
  documentType: DocumentTypeName,
  format: NumberingFormat,
): Promise<DocumentNumbering> {
  return request<DocumentNumbering>(`/numbering/${documentType}`, {
    method: 'PUT',
    body: format,
  });
}

/**
 * The number the document on screen will be given, from the server's own series.
 *
 * The entry screens used to make one up from a prefix kept in the browser and a count
 * of the documents they could see - "INV-1001" above a series that would issue
 * "SL/2026/0007" - so the number on the form was never the number on the document.
 * This reads the series without reserving from it, so it is the number the save will
 * take unless somebody else saves first; the saved notice always names the real one.
 */
export function useNextNumber(
  documentType: DocumentTypeName | null,
  date: string,
): { readonly next: string | null; readonly loading: boolean } {
  const query = useQuery<DocumentNumbering, ApiError>({
    queryKey: ['numbering', 'next', documentType, date],
    queryFn: () => peekNumber(documentType as DocumentTypeName, date),
    enabled: documentType !== null && /^\d{4}-\d{2}-\d{2}$/.test(date),
    staleTime: 0,
    retry: false,
  });

  return {
    next: query.data?.next ?? null,
    loading: query.isPending && documentType !== null,
  };
}
