import { request } from '@/lib/api';

/** Voucher types, matching the API's VoucherType enum. */
export const VoucherType = {
  cashReceipt: 1,
  bankReceipt: 2,
  cashPayment: 3,
  bankPayment: 4,
  journal: 5,
  contra: 6,
  openingBalance: 7,
} as const;

/** The three kinds of money document, each with a list and an entry screen of its own. */
export type VoucherKind = 'receipt' | 'payment' | 'journal';

/** The voucher types each kind's list shows. */
export const KIND_TYPES: Record<VoucherKind, readonly number[]> = {
  receipt: [VoucherType.cashReceipt, VoucherType.bankReceipt],
  payment: [VoucherType.cashPayment, VoucherType.bankPayment],
  journal: [VoucherType.journal, VoucherType.contra],
};

/** Where each kind's list lives; its entry screen is `/new` beneath it, and `/:id` edits. */
export const KIND_PATH: Record<VoucherKind, string> = {
  receipt: '/accounting/receipts',
  payment: '/accounting/payments',
  journal: '/accounting/journals',
};

/** The kind a voucher type is entered and listed as. */
export function kindOf(type: number): VoucherKind {
  if (type === VoucherType.cashReceipt || type === VoucherType.bankReceipt) {
    return 'receipt';
  }

  if (type === VoucherType.cashPayment || type === VoucherType.bankPayment) {
    return 'payment';
  }

  return 'journal';
}

/** A voucher's names for the `voucherTypes.<name>` lookup, by wire value. */
export const TYPE_NAME: Record<number, string> = {
  1: 'CashReceipt',
  2: 'BankReceipt',
  3: 'CashPayment',
  4: 'BankPayment',
  5: 'Journal',
  6: 'Contra',
};

/** The voucher statuses, keyed by wire value. */
export const VoucherStatus = { draft: 1, posted: 2, cancelled: 3 } as const;

/** One voucher on a list. */
export interface VoucherListLine {
  readonly voucherId: string;
  readonly date: string;
  readonly voucherNumber: string;
  readonly type: number;
  readonly status: number;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly currency: string;
  readonly exchangeRate: number;
  readonly documentAmount: number;
  readonly baseAmount: number;
  /** The ledgers debited, by name. */
  readonly debitLedgers: string | null;
  /** The ledgers credited, by name. */
  readonly creditLedgers: string | null;
}

export interface VoucherList {
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly vouchers: readonly VoucherListLine[];
  readonly voucherCount: number;
  readonly totalBaseAmount: number;
}

/** Lists one kind's vouchers over a period. */
export function listVouchers(
  kind: VoucherKind,
  from: string,
  to: string,
  status: string,
): Promise<VoucherList> {
  const params = new URLSearchParams({ from, to });

  for (const type of KIND_TYPES[kind]) {
    params.append('types', String(type));
  }

  if (status) {
    params.set('status', status);
  }

  return request<VoucherList>(`/accounting/vouchers?${params.toString()}`);
}

/** A bill a voucher settled. */
export interface VoucherSettlement {
  readonly billId: string;
  readonly ledgerId: string;
  readonly billNumber: string;
  readonly billDate: string;
  readonly dueDate: string;
  readonly billAmount: number;
  /** What is still owed after this voucher. */
  readonly outstanding: number;
  /** What this voucher settled. */
  readonly amount: number;
}

/** A cheque a voucher recorded. */
export interface VoucherCheque {
  readonly partyLedgerId: string;
  readonly chequeNumber: string;
  readonly instrumentDate: string;
  readonly amount: number;
  readonly bankLedgerId: string | null;
  readonly drawnOnBank: string | null;
  readonly accountNumber: string | null;
  readonly inFavourOf: string | null;
}

/** A voucher as the server reads it back, to be reopened on its entry screen. */
export interface VoucherDetail {
  readonly voucherId: string;
  readonly number: string;
  readonly type: number;
  readonly status: number;
  readonly date: string;
  readonly currency: string;
  readonly baseCurrency: string;
  readonly exchangeRate: number;
  readonly referenceNumber: string | null;
  readonly narration: string | null;
  readonly paymentMode: string | null;
  readonly lines: readonly {
    readonly lineNumber: number;
    readonly ledgerId: string;
    readonly side: number;
    readonly debitAmount: number;
    readonly creditAmount: number;
    readonly narration: string | null;
  }[];
  readonly settlements?: readonly VoucherSettlement[] | null;
  readonly cheques?: readonly VoucherCheque[] | null;
}

export function getVoucher(voucherId: string): Promise<VoucherDetail> {
  return request<VoucherDetail>(`/accounting/vouchers/${voucherId}`);
}
