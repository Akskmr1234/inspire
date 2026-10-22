import { request } from '@/lib/api';

/** Where a job stands, read from the furthest stage ticked. */
export const JobCardStatus = {
  received: 1,
  processing: 2,
  ready: 3,
  notReady: 4,
  informed: 5,
  delivered: 6,
  dead: 7,
  cancelled: 8,
} as const;

/** The stages a job card ticks off, each with its day. */
export const JobCardStage = {
  processing: 1,
  ready: 2,
  notReady: 3,
  informed: 4,
  delivered: 5,
  dead: 6,
} as const;

/** The stages in the order the card shows them, with their key in the stage dates. */
export const JOB_CARD_STAGES = [
  { stage: JobCardStage.processing, key: 'processing' },
  { stage: JobCardStage.ready, key: 'ready' },
  { stage: JobCardStage.notReady, key: 'notReady' },
  { stage: JobCardStage.informed, key: 'informed' },
  { stage: JobCardStage.delivered, key: 'delivered' },
  { stage: JobCardStage.dead, key: 'dead' },
] as const;

/** Who brought the device in. */
export interface JobCardCustomer {
  readonly name: string;
  readonly mobile: string | null;
  readonly alternateMobile: string | null;
  readonly email: string | null;
  readonly addressLine1: string | null;
  readonly addressLine2: string | null;
}

/** The device. */
export interface JobCardDevice {
  readonly itemName: string;
  readonly brandName: string | null;
  readonly modelNumber: string | null;
  readonly colour: string | null;
  readonly serialNumber: string | null;
  readonly accessories: string | null;
}

/** What is wrong, what is to be done, and the estimate given. */
export interface JobCardWork {
  readonly serviceType: string | null;
  readonly fault: string | null;
  readonly problem: string | null;
  readonly remarks: string | null;
  readonly notes: string | null;
  readonly estimatedOn: string | null;
  readonly estimatedTime: string | null;
  readonly estimatedCharge: number;
}

/** The day each stage was reached. */
export interface JobCardStages {
  readonly processing: string | null;
  readonly ready: string | null;
  readonly notReady: string | null;
  readonly informed: string | null;
  readonly delivered: string | null;
  readonly dead: string | null;
}

/** A job card as the list shows it. */
export interface JobCardSummary {
  readonly id: string;
  readonly number: string;
  readonly date: string;
  readonly customerName: string;
  readonly mobile: string | null;
  readonly itemName: string;
  readonly brandName: string | null;
  readonly modelNumber: string | null;
  readonly serialNumber: string | null;
  readonly fault: string | null;
  readonly technicianName: string | null;
  readonly estimatedOn: string | null;
  readonly estimatedCharge: number;
  readonly amount: number;
  readonly status: number;
  readonly salesInvoiceId: string | null;
  readonly salesInvoiceNumber: string | null;
  readonly salesInvoiceStatus: number | null;
  /** What the additional ledgers move the bill by, net. */
  readonly chargeTotal: number;
}

/** A part or labour line. */
export interface JobCardLineDetail {
  readonly productId: string;
  readonly productCode: string;
  readonly productName: string;
  readonly stockUnitCode: string;
  readonly quantity: number;
  readonly rate: number;
  readonly amount: number;
  readonly remarks: string | null;
  readonly isStocked: boolean;
  readonly tracksBatches: boolean;
  readonly batchId: string | null;
  readonly batchNumber: string | null;
}

/** A job card in full. */
export interface JobCardDetail {
  readonly summary: JobCardSummary;
  readonly warehouseId: string;
  readonly warehouseName: string;
  readonly customerLedgerId: string | null;
  readonly customerLedgerName: string | null;
  readonly customer: JobCardCustomer;
  readonly referenceNumber: string | null;
  readonly paymentMode: number;
  readonly device: JobCardDevice;
  readonly work: JobCardWork;
  readonly salesmanLedgerId: string | null;
  readonly technicianLedgerId: string | null;
  readonly stages: JobCardStages;
  readonly cancellationReason: string | null;
  readonly lines: readonly JobCardLineDetail[];
  /** The tax mode it is billed in, or null for the firm's own. */
  readonly mode: number | null;
  readonly charges: readonly JobCardChargeDetail[] | null;
}

/** An additional ledger on a job's bill: a service charge, a discount. */
export interface JobCardChargeDetail {
  readonly ledgerId: string;
  readonly ledgerName: string;
  readonly amount: number;
  readonly isAddition: boolean;
}

/** A job card as entered. Date and godown are read only when it is created. */
export interface JobCardInput {
  readonly date: string;
  readonly warehouseId: string;
  readonly customerLedgerId: string | null;
  readonly customer: JobCardCustomer;
  readonly referenceNumber: string | null;
  readonly paymentMode: number;
  readonly device: JobCardDevice;
  readonly work: JobCardWork;
  readonly salesmanLedgerId: string | null;
  readonly technicianLedgerId: string | null;
  readonly lines: readonly {
    readonly productId: string;
    readonly quantity: number;
    readonly rate: number;
    readonly remarks: string | null;
    readonly batchId: string | null;
  }[];
  /** The tax mode to bill in; the firm's own when null. */
  readonly mode?: number | null;
  readonly charges?: readonly { readonly ledgerId: string; readonly amount: number }[];
}

/** A customer found by mobile number. */
export interface JobCardCustomerMatch {
  readonly customerLedgerId: string | null;
  readonly customer: JobCardCustomer;
}

const CARDS = '/service/job-cards';

/** Lists job cards, newest first. */
export function listJobCards(
  status: number | '',
  search: string,
  from: string,
  to: string,
): Promise<readonly JobCardSummary[]> {
  const query = new URLSearchParams();

  if (status !== '') {
    query.set('status', String(status));
  }
  if (search.trim()) {
    query.set('search', search.trim());
  }
  if (from) {
    query.set('from', from);
  }
  if (to) {
    query.set('to', to);
  }

  return request<readonly JobCardSummary[]>(`${CARDS}?${query.toString()}`);
}

/** Reads one card. */
export function getJobCard(id: string): Promise<JobCardDetail> {
  return request<JobCardDetail>(`${CARDS}/${id}`);
}

/** Finds customers by the mobile number they give. */
export function findJobCardCustomers(
  mobile: string,
): Promise<readonly JobCardCustomerMatch[]> {
  return request<readonly JobCardCustomerMatch[]>(
    `${CARDS}/customers?mobile=${encodeURIComponent(mobile)}`,
  );
}

/** Takes a device in. */
export function createJobCard(input: JobCardInput): Promise<string> {
  return request<string>(CARDS, { method: 'POST', body: input });
}

/** Replaces what a card says. */
export function updateJobCard(id: string, input: JobCardInput): Promise<void> {
  return request<void>(`${CARDS}/${id}`, { method: 'PUT', body: input });
}

/** Ticks a stage on a day, or clears it. */
export function markJobCardStage(
  id: string,
  stage: number,
  on: string | null,
): Promise<void> {
  return request<void>(`${CARDS}/${id}/stage`, { method: 'POST', body: { stage, on } });
}

/** Withdraws a job. */
export function cancelJobCard(id: string, reason: string): Promise<void> {
  return request<void>(`${CARDS}/${id}/cancel`, { method: 'POST', body: { reason } });
}

/** Raises a draft sales invoice for the job's parts and labour. */
export function billJobCard(
  id: string,
  customerLedgerId: string | null,
  payment: {
    readonly paidInFull?: boolean;
    readonly amountReceived?: number;
    readonly receivedIntoLedgerId?: string | null;
    readonly paymentReference?: string | null;
  } = {},
): Promise<{
  readonly salesInvoiceId: string;
  readonly number: string;
  readonly total: number;
}> {
  return request(`${CARDS}/${id}/sales-invoice`, {
    method: 'POST',
    body: { customerLedgerId, ...payment },
  });
}
