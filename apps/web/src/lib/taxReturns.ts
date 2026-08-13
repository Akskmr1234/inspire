import { request } from '@/lib/api';

/**
 * The VAT and GST returns of section 7.3.
 *
 * One set of endpoints serves both regimes, answering in whichever heads the firm's own
 * one uses — so this file has no notion of "the VAT report" and "the GST report", and
 * neither does the screen. What arrives is what the firm actually charges.
 */
export const TaxRegime = { none: 0, gccVat: 1, indiaGst: 2 } as const;

/** The heads a document can carry, as the wire numbers them. */
export const TaxComponent = {
  vat: 1,
  cgst: 2,
  sgst: 3,
  igst: 4,
  cess: 5,
  foodCess: 6,
  cst: 7,
} as const;

/** What a head is called on screen. */
const COMPONENT_NAMES: Readonly<Record<number, string>> = {
  [TaxComponent.vat]: 'VAT',
  [TaxComponent.cgst]: 'CGST',
  [TaxComponent.sgst]: 'SGST',
  [TaxComponent.igst]: 'IGST',
  [TaxComponent.cess]: 'Cess',
  [TaxComponent.foodCess]: 'Food cess',
  [TaxComponent.cst]: 'CST',
};

/**
 * Names a head.
 *
 * Not translated: CGST is CGST in every language a return is filed in, and a localised
 * spelling of a statutory head is one more thing for somebody to reconcile against a
 * portal that will not have translated it.
 */
export function componentName(component: number): string {
  return COMPONENT_NAMES[component] ?? `#${component}`;
}

/** What one head came to over the period. */
export interface TaxHeadTotal {
  readonly component: number;
  readonly taxAmount: number;
}

/** One document's charge under one head. */
export interface OutputTaxRow {
  readonly documentId: string;
  readonly number: string;
  readonly kind: number;
  readonly date: string;
  readonly customerCode: string;
  readonly customerName: string;
  readonly taxRegistrationNumber: string | null;
  readonly stateCode: string | null;
  readonly component: number;
  readonly percentage: number;
  readonly taxableAmount: number;
  readonly taxAmount: number;
}

/** The output tax of a period. */
export interface OutputTaxReport {
  readonly from: string;
  readonly to: string;
  readonly regime: number;
  readonly currency: string;
  readonly taxableSupplies: number;
  readonly zeroRatedSupplies: number;
  readonly totals: readonly TaxHeadTotal[];
  readonly rows: readonly OutputTaxRow[];
}

/** One posting to an input tax account. */
export interface InputTaxRow {
  readonly voucherId: string;
  readonly number: string;
  readonly date: string;
  readonly component: number;
  readonly ledgerCode: string;
  readonly ledgerName: string;
  readonly taxAmount: number;
  readonly narration: string | null;
}

/** The input tax of a period. */
export interface InputTaxReport {
  readonly from: string;
  readonly to: string;
  readonly regime: number;
  readonly currency: string;
  readonly totals: readonly TaxHeadTotal[];
  readonly rows: readonly InputTaxRow[];
}

/** One head's position for the period. */
export interface TaxSummaryLine {
  readonly component: number;
  readonly outputTax: number;
  readonly inputTax: number;
  readonly netPayable: number;
  readonly outputTaxPosted: number;
  readonly difference: number;
}

/** What the firm owes the state for a period. */
export interface TaxSummaryReport {
  readonly from: string;
  readonly to: string;
  readonly regime: number;
  readonly currency: string;
  readonly taxableSupplies: number;
  readonly zeroRatedSupplies: number;
  readonly lines: readonly TaxSummaryLine[];
  readonly netPayable: number;
  /**
   * Whether every head's ledger agrees with its documents.
   *
   * False is not an error. It means output tax reached the books by some route other
   * than a sales document, and somebody should look before filing.
   */
  readonly isReconciled: boolean;
}

const REPORTS = '/accounting/reports';

/** Reads what the firm owes for a period, head by head. */
export function getTaxSummary(from: string, to: string): Promise<TaxSummaryReport> {
  return request<TaxSummaryReport>(`${REPORTS}/tax-summary?from=${from}&to=${to}`);
}

/** Reads the output tax charged over a period, document by document. */
export function getOutputTax(from: string, to: string): Promise<OutputTaxReport> {
  return request<OutputTaxReport>(`${REPORTS}/output-tax?from=${from}&to=${to}`);
}

/** Reads the input tax incurred over a period, posting by posting. */
export function getInputTax(from: string, to: string): Promise<InputTaxReport> {
  return request<InputTaxReport>(`${REPORTS}/input-tax?from=${from}&to=${to}`);
}
