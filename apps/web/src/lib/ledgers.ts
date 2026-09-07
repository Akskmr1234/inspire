import { request } from '@/lib/api';

/** What a ledger represents, matching the API's LedgerKind enum. */
export const LedgerKind = {
  general: 1,
  cash: 2,
  bank: 3,
  customer: 4,
  supplier: 5,
  employee: 6,
  tax: 7,
  additionalCharge: 8,
} as const;

/** A ledger as a lookup shows it. */
export interface LedgerSummary {
  readonly ledgerId: string;
  readonly code: string;
  readonly name: string;
  readonly kind: number;
  readonly groupCode: string;
  readonly groupName: string;
  readonly nature: number;
  readonly currency: string;
  readonly isBillWise: boolean;
}

/**
 * Lists the firm's ledgers.
 *
 * Unpaged, which is the API's own decision and the right one: a chart of accounts is
 * a few hundred rows, every caller is a picker being typed into, and a round trip per
 * keystroke is what makes a picker feel broken.
 */
export function listLedgers(activeOnly = true): Promise<readonly LedgerSummary[]> {
  return request<readonly LedgerSummary[]>(
    `/accounting/ledgers?activeOnly=${String(activeOnly)}`,
  );
}

/**
 * A party a payment or a receipt can be against.
 *
 * Customers, suppliers and employees — the three kinds of ledger somebody hands
 * money to or takes it from. A general ledger is not one: paying rent is a payment
 * against an expense account, and the screen offers that separately rather than
 * calling the landlord a party.
 */
export function isParty(ledger: LedgerSummary): boolean {
  return (
    ledger.kind === LedgerKind.customer ||
    ledger.kind === LedgerKind.supplier ||
    ledger.kind === LedgerKind.employee
  );
}

/** Whether a ledger is one of the firm's own cash or bank accounts. */
export function isMoneyAccount(ledger: LedgerSummary): boolean {
  return ledger.kind === LedgerKind.cash || ledger.kind === LedgerKind.bank;
}

/**
 * The codes the standard chart seeds for the charges a document may carry.
 *
 * Read by code because the API exposes no endpoint for the charge matrix — the
 * mapping of ledger to document type lives on the server and is checked there, so a
 * charge this list offers can still be refused, and the server's message says so.
 * Offering the five the seeding creates is what makes freight and a discount
 * reachable at all; without it a purchase could carry neither.
 */
export const CHARGE_LEDGER_CODES: readonly string[] = [
  'FREIGHT',
  'PACKING',
  'DELIVERY',
  'DISC-ALLOWED',
  'ROUND-OFF',
];

/**
 * The ledgers a document's charges may post to.
 *
 * Anything the firm has classified as an additional charge, plus the seeded codes
 * for firms whose charge accounts were created before that classification existed.
 */
export function chargeLedgers(
  ledgers: readonly LedgerSummary[],
): readonly LedgerSummary[] {
  return ledgers.filter(
    (ledger) =>
      ledger.kind === LedgerKind.additionalCharge ||
      CHARGE_LEDGER_CODES.includes(ledger.code.toUpperCase()),
  );
}

/**
 * Whether a charge adds to a document's total or comes off it.
 *
 * The server holds the real answer on the charge matrix, where it is a flag set per
 * ledger per document type — freight a firm pays is a cost, freight it recovers is
 * income. This is the screen's guess for the running total it shows while somebody
 * types, and it is only ever a guess: the figure that reaches the books is the one
 * the server computes.
 */
export function chargeAdds(ledger: LedgerSummary): boolean {
  return !ledger.code.toUpperCase().startsWith('DISC');
}
