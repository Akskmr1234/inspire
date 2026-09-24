import { chargeLedgers, type LedgerSummary } from '@/lib/ledgers';

/**
 * The document kinds a default charge can be set against.
 *
 * Separate rather than one list for everything, because the charges genuinely
 * differ by document: a firm that adds freight to every purchase does not add it to
 * a sales return, and a delivery charge belongs on the sales side only. One list
 * would mean somebody deleting the row off half the documents they enter, which is
 * more work than typing it on the other half.
 */
export const CHARGE_DOCUMENTS = [
  'sales',
  'salesReturn',
  'salesOrder',
  'purchase',
  'purchaseReturn',
  'purchaseOrder',
] as const;

export type ChargeDocument = (typeof CHARGE_DOCUMENTS)[number];

/** One charge a document kind starts with. */
export interface DefaultCharge {
  readonly ledgerId: string;
  /**
   * What the row starts at, as typed.
   *
   * Blank is meaningful and common: a firm that puts freight on every purchase but
   * at a different figure each time wants the row waiting with an empty amount, not
   * a number it has to correct. The row is still applied — it is the head that was
   * being retyped, not the money.
   */
  readonly amount: string;
}

/** Nothing set anywhere, which is what a fresh installation has. */
export const NO_DEFAULT_CHARGES: Readonly<Record<string, readonly DefaultCharge[]>> = {};

/**
 * What a new document of this kind starts with.
 *
 * Filtered against the chart as it stands now: a head somebody set as a default and
 * later removed from the chart would otherwise put a row on every new document with
 * a picker that cannot be satisfied, and the entry would fail at the server with a
 * message about a ledger nobody chose.
 */
export function chargesFor(
  document: ChargeDocument,
  defaults: Readonly<Record<string, readonly DefaultCharge[]>>,
  ledgers: readonly LedgerSummary[],
): readonly DefaultCharge[] {
  const set = defaults[document];

  if (set === undefined || set.length === 0) {
    return [];
  }

  const usable = new Set(chargeLedgers(ledgers).map((ledger) => ledger.ledgerId));

  return set.filter((charge) => usable.has(charge.ledgerId));
}
