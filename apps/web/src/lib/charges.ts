import { useQuery } from '@tanstack/react-query';
import { request, type ApiError } from '@/lib/api';
import { chargeAdds, chargeLedgers, type LedgerSummary } from '@/lib/ledgers';

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

/** One charge a kind of document may carry, as the firm set it up on the server. */
export interface ChargeSetup {
  readonly ledgerId: string;
  readonly code: string;
  readonly name: string;
  /** Adds to the document's total; otherwise it comes off. Decided here, not per row. */
  readonly isAddition: boolean;
  readonly isDefault: boolean;
  readonly appliesUnderTax: boolean;
  readonly appliesUnderNonTax: boolean;
  readonly appliesUnderCst: boolean;
  /** Round off: worked out by the document itself, never typed. */
  readonly isComputed: boolean;
}

/** The server's name for the setup each document kind draws on. Returns use their sale's or purchase's. */
const SETUP_FOR: Readonly<Record<ChargeDocument, string>> = {
  sales: 'Sales',
  salesReturn: 'Sales',
  salesOrder: 'SalesOrder',
  purchase: 'Purchase',
  purchaseReturn: 'Purchase',
  purchaseOrder: 'PurchaseOrder',
};

/**
 * The charges a document may carry and which way each moves its total.
 *
 * The screens used to guess the direction from a ledger's code, or let somebody pick
 * "deduct" for freight, and showed a total the server never posted: it decides from
 * this setup. Read from the same setup, the screen's total is the invoice's.
 */
export function useChargeSetup(
  document: ChargeDocument,
): readonly ChargeSetup[] | undefined {
  const query = useQuery<readonly ChargeSetup[], ApiError>({
    queryKey: chargeSetupKey(document),
    queryFn: () =>
      request<readonly ChargeSetup[]>(
        `/accounting/charges?document=${SETUP_FOR[document]}`,
      ),
    staleTime: 5 * 60_000,
  });

  return query.data;
}

/** The server's numbers for the setups above, which a request body carries. */
const SETUP_NUMBER: Readonly<Record<string, number>> = {
  Sales: 1,
  SalesOrder: 2,
  Purchase: 5,
  PurchaseOrder: 6,
};

/**
 * Sets a ledger up as a charge this kind of document may carry.
 *
 * A ledger already set up for it keeps the direction it has; the reply says which.
 */
export function mapCharge(
  document: ChargeDocument,
  ledgerId: string,
  isAddition: boolean,
): Promise<ChargeSetup> {
  return request<ChargeSetup>('/accounting/charges', {
    method: 'POST',
    body: { document: SETUP_NUMBER[SETUP_FOR[document]], ledgerId, isAddition },
  });
}

/** The query key a document's charge setup is cached under. */
export function chargeSetupKey(document: ChargeDocument): readonly string[] {
  return ['charge-setup', SETUP_FOR[document]];
}

/** The charges that may be entered on a document in a tax mode: typed ones only. */
export function enterableCharges(
  setup: readonly ChargeSetup[],
  nonTax: boolean,
): readonly ChargeSetup[] {
  return setup.filter(
    (charge) =>
      !charge.isComputed && (nonTax ? charge.appliesUnderNonTax : charge.appliesUnderTax),
  );
}

/** Takes a ledger off the charges a kind of document carries. */
export function withdrawCharge(
  document: ChargeDocument,
  ledgerId: string,
): Promise<void> {
  return request<void>(`/accounting/charges/${SETUP_FOR[document]}/${ledgerId}`, {
    method: 'DELETE',
  });
}

/**
 * The name a document's setup goes by, which is also the key its default amounts are
 * kept under in Settings. A return shares its sale's or purchase's.
 */
export function setupName(document: ChargeDocument): string {
  return SETUP_FOR[document];
}

/** Marks a charge as one every new document of its kind shows by itself, or not. */
export function setChargeDefault(
  document: ChargeDocument,
  ledgerId: string,
  isDefault: boolean,
): Promise<ChargeSetup> {
  return request<ChargeSetup>(
    `/accounting/charges/${SETUP_FOR[document]}/${ledgerId}/default`,
    { method: 'PUT', body: { isDefault } },
  );
}

/** One row of a document's additional ledgers: a ledger and which way it moves the total. */
export interface ChargeRow {
  readonly ledgerId: string;
  readonly label: string;
  readonly adds: boolean;
  /** Picked on this document rather than shown by default, so it can be taken off again. */
  readonly removable: boolean;
}

/**
 * The rows a document's additional ledgers show.
 *
 * The ledgers Settings marked as defaults for the document under its tax mode, so
 * nobody picks them one by one on each invoice; then every ledger the document carries
 * besides - one picked on it, or one since taken off the setup - so a saved figure is
 * never hidden.
 */
export function chargeRows(
  setup: readonly ChargeSetup[] | undefined,
  nonTax: boolean,
  carried: readonly { readonly ledgerId: string; readonly amount: string }[],
  ledgers: readonly LedgerSummary[],
): readonly ChargeRow[] {
  const rows: ChargeRow[] = (setup ? enterableCharges(setup, nonTax) : [])
    .filter((charge) => charge.isDefault)
    .map((charge) => ({
      ledgerId: charge.ledgerId,
      label: `${charge.code} — ${charge.name}`,
      adds: charge.isAddition,
      removable: false,
    }));
  const seen = new Set(rows.map((row) => row.ledgerId));

  for (const charge of carried) {
    if (charge.ledgerId === '' || seen.has(charge.ledgerId)) {
      continue;
    }

    const ledger = ledgers.find((candidate) => candidate.ledgerId === charge.ledgerId);
    const set = setup?.find((candidate) => candidate.ledgerId === charge.ledgerId);

    rows.push({
      ledgerId: charge.ledgerId,
      label: set
        ? `${set.code} — ${set.name}`
        : ledger
          ? `${ledger.code} — ${ledger.name}`
          : charge.ledgerId,
      adds: set?.isAddition ?? (ledger ? chargeAdds(ledger) : true),
      removable: true,
    });
    seen.add(charge.ledgerId);
  }

  return rows;
}

/** The ledgers set up for a document that are not on it yet, for picking by hand. */
export function offeredCharges(
  setup: readonly ChargeSetup[] | undefined,
  nonTax: boolean,
  shown: readonly ChargeRow[],
): readonly ChargeSetup[] {
  const on = new Set(shown.map((row) => row.ledgerId));
  return (setup ? enterableCharges(setup, nonTax) : []).filter(
    (charge) => !on.has(charge.ledgerId),
  );
}
