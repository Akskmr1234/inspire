/**
 * The state tables a firm's documents choose from.
 *
 * The state on a customer is not a note about where they are. Under Indian GST it is
 * the field that decides which heads an invoice charges: a customer in the firm's own
 * state is billed CGST plus SGST, one in another state is billed IGST, and getting it
 * wrong misstates a statutory return rather than a mailing label. That is why it is a
 * dropdown here and not a free-text box — a customer whose state was typed as "TN",
 * "T.N." and "Tamilnadu" on three different days is three different answers to the
 * same question.
 *
 * Two tables, chosen by the firm's regime, because the codes are the regime's own:
 * India's are the two-digit census codes the GSTIN begins with, and the Gulf's are
 * the emirate and region codes a VAT return is broken down by. A firm's regime and
 * its own state are set once in Settings.
 */

/** The tax regimes, matching the API's TaxRegime enum. */
export const TaxRegime = { none: 0, gccVat: 1, indiaGst: 2 } as const;

/** One row of a state table. */
export interface StateEntry {
  /** What goes on the record, and what a return is filed against. */
  readonly code: string;
  readonly name: string;
}

/**
 * The GST state codes, as the first two digits of a GSTIN.
 *
 * The full list rather than the states somebody expects to trade with: a customer
 * whose state is missing from the picker is a customer somebody types into the wrong
 * one.
 */
export const INDIA_GST_STATES: readonly StateEntry[] = [
  { code: '01', name: 'Jammu and Kashmir' },
  { code: '02', name: 'Himachal Pradesh' },
  { code: '03', name: 'Punjab' },
  { code: '04', name: 'Chandigarh' },
  { code: '05', name: 'Uttarakhand' },
  { code: '06', name: 'Haryana' },
  { code: '07', name: 'Delhi' },
  { code: '08', name: 'Rajasthan' },
  { code: '09', name: 'Uttar Pradesh' },
  { code: '10', name: 'Bihar' },
  { code: '11', name: 'Sikkim' },
  { code: '12', name: 'Arunachal Pradesh' },
  { code: '13', name: 'Nagaland' },
  { code: '14', name: 'Manipur' },
  { code: '15', name: 'Mizoram' },
  { code: '16', name: 'Tripura' },
  { code: '17', name: 'Meghalaya' },
  { code: '18', name: 'Assam' },
  { code: '19', name: 'West Bengal' },
  { code: '20', name: 'Jharkhand' },
  { code: '21', name: 'Odisha' },
  { code: '22', name: 'Chhattisgarh' },
  { code: '23', name: 'Madhya Pradesh' },
  { code: '24', name: 'Gujarat' },
  { code: '26', name: 'Dadra and Nagar Haveli and Daman and Diu' },
  { code: '27', name: 'Maharashtra' },
  { code: '29', name: 'Karnataka' },
  { code: '30', name: 'Goa' },
  { code: '31', name: 'Lakshadweep' },
  { code: '32', name: 'Kerala' },
  { code: '33', name: 'Tamil Nadu' },
  { code: '34', name: 'Puducherry' },
  { code: '35', name: 'Andaman and Nicobar Islands' },
  { code: '36', name: 'Telangana' },
  { code: '37', name: 'Andhra Pradesh' },
  { code: '38', name: 'Ladakh' },
  { code: '97', name: 'Other territory' },
  { code: '96', name: 'Outside India' },
];

/**
 * The emirates and regions a Gulf VAT return is broken down by.
 *
 * VAT does not split by state the way GST does — there is one rate and one head —
 * but the emirate a supply is made in is reported box by box on the UAE return, so
 * the field earns its place under this regime too.
 */
export const GCC_VAT_STATES: readonly StateEntry[] = [
  { code: 'AE-AZ', name: 'Abu Dhabi' },
  { code: 'AE-DU', name: 'Dubai' },
  { code: 'AE-SH', name: 'Sharjah' },
  { code: 'AE-AJ', name: 'Ajman' },
  { code: 'AE-UQ', name: 'Umm Al Quwain' },
  { code: 'AE-RK', name: 'Ras Al Khaimah' },
  { code: 'AE-FU', name: 'Fujairah' },
  { code: 'SA-01', name: 'Riyadh' },
  { code: 'SA-02', name: 'Makkah' },
  { code: 'SA-03', name: 'Madinah' },
  { code: 'SA-04', name: 'Eastern Province' },
  { code: 'SA-05', name: 'Asir' },
  { code: 'OM', name: 'Oman' },
  { code: 'BH', name: 'Bahrain' },
  { code: 'KW', name: 'Kuwait' },
  { code: 'QA', name: 'Qatar' },
  { code: 'XX', name: 'Outside the GCC' },
];

/** The table a firm on this regime chooses its states from. */
export function statesFor(regime: number): readonly StateEntry[] {
  if (regime === TaxRegime.indiaGst) {
    return INDIA_GST_STATES;
  }

  if (regime === TaxRegime.gccVat) {
    return GCC_VAT_STATES;
  }

  return [];
}

/** Names a state code, falling back to the code itself for one no table holds. */
export function stateName(regime: number, code: string | null | undefined): string {
  if (!code) {
    return '';
  }

  const found = statesFor(regime).find((state) => state.code === code);

  return found ? `${found.code} — ${found.name}` : code;
}

/**
 * Whether a supply between these two states crosses a border.
 *
 * The question an invoice asks to decide IGST against CGST plus SGST. Answered here
 * so a screen can say which heads it expects before the server works it out — a
 * courtesy, never the calculation itself, which stays where the money is.
 */
export function isInterState(
  regime: number,
  firmState: string,
  partyState: string | null | undefined,
): boolean {
  if (regime !== TaxRegime.indiaGst) {
    return false;
  }

  if (!firmState || !partyState) {
    return false;
  }

  return firmState !== partyState;
}
