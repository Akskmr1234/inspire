/**
 * Shared shapes for the three cheque reports: the post-dated cheque report, the
 * PDC calendar, and the cheque register.
 *
 * Direction and status are the API's C# enums, and they arrive as numbers - there
 * is no string-enum converter registered on the server, so `Received` is 1 and
 * `Cleared` is 3 on the wire. The same number sent back as a query-string filter
 * binds cleanly onto the enum, so it is the one representation used throughout.
 */

/** Received (1) or issued (2). */
export type ChequeDirection = 1 | 2;

/** Pending (1), Deposited (2), Cleared (3), Bounced (4), Stopped (5), Cancelled (6). */
export type ChequeStatus = 1 | 2 | 3 | 4 | 5 | 6;

/** The direction names, keyed by wire value, for the i18n lookup `cheques.direction.<name>`. */
export const DIRECTION_NAME: Record<ChequeDirection, string> = {
  1: 'Received',
  2: 'Issued',
};

/** The status names, keyed by wire value, for the i18n lookup `cheques.status.<name>`. */
export const STATUS_NAME: Record<ChequeStatus, string> = {
  1: 'Pending',
  2: 'Deposited',
  3: 'Cleared',
  4: 'Bounced',
  5: 'Stopped',
  6: 'Cancelled',
};

/** The directions a filter offers, in wire order. */
export const CHEQUE_DIRECTIONS: readonly { readonly value: ChequeDirection; readonly name: string }[] = [
  { value: 1, name: 'Received' },
  { value: 2, name: 'Issued' },
];

/** The statuses a filter offers, in lifecycle order. */
export const CHEQUE_STATUSES: readonly { readonly value: ChequeStatus; readonly name: string }[] = [
  { value: 1, name: 'Pending' },
  { value: 2, name: 'Deposited' },
  { value: 3, name: 'Cleared' },
  { value: 4, name: 'Bounced' },
  { value: 5, name: 'Stopped' },
  { value: 6, name: 'Cancelled' },
];

/**
 * One cheque as a report presents it. Mirrors the API's `ChequeReportLine`.
 *
 * `bankName` is the firm's own account once a cheque has one, and the payer's bank
 * named on a received cheque still in hand otherwise - the server has already
 * chosen between them, so a line only ever has the one to show.
 */
export interface ChequeReportLine {
  readonly chequeId: string;
  readonly chequeNumber: string;
  readonly direction: ChequeDirection;
  readonly status: ChequeStatus;
  readonly partyLedgerId: string;
  readonly partyCode: string;
  readonly partyName: string;
  readonly instrumentDate: string;
  readonly recordedOn: string;
  readonly amount: number;
  readonly currency: string;
  readonly bankName: string | null;
  readonly depositedOn: string | null;
  readonly closedOn: string | null;
  readonly closureReason: string | null;
  readonly daysUntilDue: number;
}
