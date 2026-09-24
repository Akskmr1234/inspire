/**
 * The ways money actually moves, for the payment mode on a receipt or a payment.
 *
 * The field behind this is free text on the server — thirty characters, no
 * vocabulary — and free text is what made it useless: "Cash", "cash", "CASH" and
 * "By Cash" are four modes to a report and one to the person who typed them, so the
 * only question the field exists to answer ("how much came in by card last month?")
 * is the one it cannot. The list is fixed here rather than made a master because it
 * is a closed set: a firm invents products and ledgers, it does not invent a fifth
 * way for money to arrive.
 *
 * The stored value is the English token, never the label. A voucher entered in
 * Arabic and read in English has to come back as the same mode, and a report
 * grouping on the column has to group across both.
 */
export interface PaymentMode {
  /** What goes to the server, and what a report groups on. */
  readonly value: string;
  /** The translation key for what a person reads. */
  readonly labelKey: string;
}

export const PAYMENT_MODES: readonly PaymentMode[] = [
  { value: 'Cash', labelKey: 'paymentModes.cash' },
  { value: 'Cheque', labelKey: 'paymentModes.cheque' },
  { value: 'Bank transfer', labelKey: 'paymentModes.bankTransfer' },
  { value: 'Card', labelKey: 'paymentModes.card' },
  { value: 'Online', labelKey: 'paymentModes.online' },
  { value: 'Credit', labelKey: 'paymentModes.credit' },
];

/**
 * Whether a stored value is one of the modes above.
 *
 * A voucher saved before the list existed holds whatever somebody typed. The picker
 * offers that value back as a row of its own rather than dropping it, because an
 * editor that silently blanks a field it does not recognise loses data on save.
 */
export function isKnownPaymentMode(value: string): boolean {
  return PAYMENT_MODES.some((mode) => mode.value === value);
}
