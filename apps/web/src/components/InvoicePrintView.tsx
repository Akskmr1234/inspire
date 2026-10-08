import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { useSettings, getEffectiveInvoicePrintFormat, type InvoicePrintFormat } from '@/stores/settings';
import { useSession } from '@/stores/session';
import { moneyAlways } from '@/lib/money';
import { IconClose, IconPrinter } from '@/components/icons';
import type { SalesInvoiceDetail } from '@/lib/sales';
import type { CustomerSummary } from '@/lib/customers';
import type { WarehouseSummary } from '@/lib/inventory';
import type { ProductSummary } from '@/lib/products';

export interface InvoicePrintViewProps {
  readonly invoice: SalesInvoiceDetail;
  readonly customer?: CustomerSummary | null | undefined;
  readonly warehouse?: WarehouseSummary | null | undefined;
  readonly products?: readonly ProductSummary[] | undefined;
  readonly onClose?: (() => void) | undefined;
}

/**
 * Converts a numeric amount to English words for invoice grand total.
 */
export function numberToWords(amount: number): string {
  if (!Number.isFinite(amount) || amount <= 0) return 'Zero Only';
  const whole = Math.floor(amount);
  const cents = Math.round((amount - whole) * 100);

  const ones = [
    '',
    'One',
    'Two',
    'Three',
    'Four',
    'Five',
    'Six',
    'Seven',
    'Eight',
    'Nine',
    'Ten',
    'Eleven',
    'Twelve',
    'Thirteen',
    'Fourteen',
    'Fifteen',
    'Sixteen',
    'Seventeen',
    'Eighteen',
    'Nineteen',
  ];
  const tens = [
    '',
    '',
    'Twenty',
    'Thirty',
    'Forty',
    'Fifty',
    'Sixty',
    'Seventy',
    'Eighty',
    'Ninety',
  ];

  function convertGroup(n: number): string {
    let s = '';
    if (n >= 100) {
      s += ones[Math.floor(n / 100)] + ' Hundred ';
      n %= 100;
    }
    if (n >= 20) {
      s += tens[Math.floor(n / 10)] + ' ';
      n %= 10;
    }
    if (n > 0) {
      s += ones[n] + ' ';
    }
    return s.trim();
  }

  let words = '';
  const billions = Math.floor(whole / 1000000000);
  const millions = Math.floor((whole % 1000000000) / 1000000);
  const thousands = Math.floor((whole % 1000000) / 1000);
  const rem = whole % 1000;

  if (billions > 0) words += convertGroup(billions) + ' Billion ';
  if (millions > 0) words += convertGroup(millions) + ' Million ';
  if (thousands > 0) words += convertGroup(thousands) + ' Thousand ';
  if (rem > 0) words += convertGroup(rem) + ' ';

  words = words.trim();
  if (!words) words = 'Zero';

  if (cents > 0) {
    return `${words} and ${cents}/100 Only`;
  }
  return `${words} Only`;
}

export function InvoicePrintView({
  invoice,
  customer,
  warehouse,
  products = [],
  onClose,
}: InvoicePrintViewProps): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();
  const { tenantCode } = useSession();

  const customerId = invoice.customerLedgerId || customer?.customerId;
  const branchId = invoice.warehouseId || warehouse?.id;

  const defaultResolvedFormat = getEffectiveInvoicePrintFormat(
    settings,
    customerId,
    branchId,
  );

  const [activeFormat, setActiveFormat] = useState<InvoicePrintFormat>(defaultResolvedFormat);

  const handlePrint = (): void => {
    window.print();
  };

  const productMap = new Map<string, ProductSummary>(products.map((p) => [p.id, p]));

  const grandTotal = invoice.header.total;
  const grandTotalWords = numberToWords(grandTotal);

  const isReturnDoc = invoice.mode === 2;
  const docTitle = isReturnDoc ? 'SALES RETURN / CREDIT NOTE' : 'TAX INVOICE';
  const docSubtitle = isReturnDoc ? 'إشعار دائن' : 'فاتورة ضريبية';

  // Format reason badge
  const formatReason = customer?.customerId && settings.customerInvoicePrintFormats[customer.customerId]
    ? 'Customer Assigned Template'
    : branchId && settings.branchInvoicePrintFormats[branchId]
    ? 'Branch Default Template'
    : 'System Default Template';

  return (
    <div className="flex flex-col h-full bg-slate-100 dark:bg-slate-950 text-slate-900 dark:text-slate-100 overflow-hidden">
      {/* Print styles injected for accurate page sizes */}
      <style>{`
        @media print {
          body {
            background: white !important;
            color: black !important;
            margin: 0 !important;
            padding: 0 !important;
          }
          .no-print {
            display: none !important;
          }
          .print-area {
            display: block !important;
            background: white !important;
            color: black !important;
            width: 100% !important;
            max-width: 100% !important;
            box-shadow: none !important;
            border: none !important;
            padding: 0 !important;
            margin: 0 auto !important;
          }
          @page {
            margin: ${activeFormat === 'thermal' ? '3mm' : activeFormat === 'A5' ? '8mm' : '12mm'};
            size: ${
              activeFormat === 'thermal'
                ? '80mm auto'
                : activeFormat === 'A5'
                ? 'A5 portrait'
                : 'A4 portrait'
            };
          }
        }
      `}</style>

      {/* Top action toolbar (Hidden when printing) */}
      <div className="no-print shrink-0 flex flex-wrap items-center justify-between gap-3 border-b border-line bg-surface px-6 py-3 shadow-sm z-20">
        <div className="flex items-center gap-3">
          <div className="flex items-center rounded-lg bg-surface-2 p-1 border border-line">
            <button
              type="button"
              onClick={() => setActiveFormat('A4')}
              className={clsx(
                'rounded-md px-3 py-1.5 text-xs font-semibold transition',
                activeFormat === 'A4'
                  ? 'bg-brand-600 text-white shadow-sm'
                  : 'text-ink-muted hover:text-ink hover:bg-surface-3',
              )}
            >
              A4 Format
            </button>
            <button
              type="button"
              onClick={() => setActiveFormat('A5')}
              className={clsx(
                'rounded-md px-3 py-1.5 text-xs font-semibold transition',
                activeFormat === 'A5'
                  ? 'bg-brand-600 text-white shadow-sm'
                  : 'text-ink-muted hover:text-ink hover:bg-surface-3',
              )}
            >
              A5 Compact
            </button>
            <button
              type="button"
              onClick={() => setActiveFormat('thermal')}
              className={clsx(
                'rounded-md px-3 py-1.5 text-xs font-semibold transition',
                activeFormat === 'thermal'
                  ? 'bg-brand-600 text-white shadow-sm'
                  : 'text-ink-muted hover:text-ink hover:bg-surface-3',
              )}
            >
              Thermal (80mm)
            </button>
          </div>

          <span className="hidden sm:inline-flex items-center rounded-full bg-brand-50 dark:bg-brand-500/10 px-2.5 py-0.5 text-[11px] font-medium text-brand-700 dark:text-brand-300 border border-brand-200 dark:border-brand-500/20">
            {formatReason}
          </span>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={handlePrint}
            className="btn-primary btn-sm flex items-center gap-1.5 shadow-sm"
          >
            <IconPrinter className="size-4" />
            <span>Print Invoice</span>
          </button>

          {onClose && (
            <button
              type="button"
              onClick={onClose}
              className="btn-secondary btn-sm flex items-center gap-1"
            >
              <IconClose className="size-4" />
              <span>{t('common.close')}</span>
            </button>
          )}
        </div>
      </div>

      {/* Main printable scroll viewport */}
      <div className="flex-1 overflow-auto p-4 sm:p-8 flex justify-center items-start">
        {activeFormat === 'thermal' && (
          <ThermalInvoiceLayout
            invoice={invoice}
            customer={customer}
            warehouse={warehouse}
            productMap={productMap}
            docTitle={docTitle}
            tenantCode={tenantCode}
            grandTotal={grandTotal}
          />
        )}

        {activeFormat === 'A5' && (
          <A5InvoiceLayout
            invoice={invoice}
            customer={customer}
            warehouse={warehouse}
            productMap={productMap}
            docTitle={docTitle}
            tenantCode={tenantCode}
            grandTotal={grandTotal}
            grandTotalWords={grandTotalWords}
          />
        )}

        {activeFormat === 'A4' && (
          <A4InvoiceLayout
            invoice={invoice}
            customer={customer}
            warehouse={warehouse}
            productMap={productMap}
            docTitle={docTitle}
            docSubtitle={docSubtitle}
            tenantCode={tenantCode}
            grandTotal={grandTotal}
            grandTotalWords={grandTotalWords}
          />
        )}
      </div>
    </div>
  );
}

// =============================================================================
// A4 Format (Enterprise Standard Tax Invoice)
// =============================================================================
function A4InvoiceLayout({
  invoice,
  customer,
  warehouse,
  productMap,
  docTitle,
  docSubtitle,
  tenantCode,
  grandTotal,
  grandTotalWords,
}: {
  readonly invoice: SalesInvoiceDetail;
  readonly customer?: CustomerSummary | null | undefined;
  readonly warehouse?: WarehouseSummary | null | undefined;
  readonly productMap: Map<string, ProductSummary>;
  readonly docTitle: string;
  readonly docSubtitle: string;
  readonly tenantCode: string | null;
  readonly grandTotal: number;
  readonly grandTotalWords: string;
}): React.JSX.Element {
  return (
    <div className="print-area w-full max-w-[210mm] min-h-[297mm] bg-white text-slate-900 p-8 shadow-xl rounded-lg border border-slate-200 text-xs font-sans leading-relaxed flex flex-col justify-between">
      <div>
        {/* Header Section */}
        <div className="flex items-start justify-between border-b-2 border-slate-900 pb-5">
          <div className="space-y-1">
            <h1 className="text-2xl font-black tracking-tight text-slate-950 uppercase">
              {tenantCode ? `${tenantCode.toUpperCase()} ENTERPRISE` : 'INSPIRE ERP COMMERCE'}
            </h1>
            <p className="text-slate-600 font-medium">
              Commercial Hub & Distribution Centre · General Trading & Services
            </p>
            {warehouse && (
              <p className="text-slate-500">
                Location: <span className="font-semibold text-slate-700">{warehouse.name} ({warehouse.code})</span>
              </p>
            )}
            <p className="text-slate-500">
              Tax / TRN Reg No: <span className="font-mono font-bold text-slate-800">100293847500003</span>
            </p>
          </div>

          <div className="text-right space-y-1">
            <div className="inline-block bg-slate-900 text-white font-black px-3 py-1 rounded text-sm uppercase tracking-wider">
              {docTitle}
            </div>
            <p className="text-slate-500 text-[11px] font-semibold">{docSubtitle}</p>
            <div className="pt-2 font-mono">
              <span className="text-slate-500">Invoice No: </span>
              <span className="font-bold text-slate-950 text-sm">{invoice.header.number}</span>
            </div>
            <div className="font-mono text-slate-600">
              <span>Date: </span>
              <span className="font-semibold text-slate-900">{invoice.date}</span>
            </div>
            {invoice.referenceNumber && (
              <div className="font-mono text-slate-600">
                <span>Ref: </span>
                <span className="font-medium text-slate-800">{invoice.referenceNumber}</span>
              </div>
            )}
          </div>
        </div>

        {/* Bill To & Details */}
        <div className="grid grid-cols-2 gap-6 py-4 border-b border-slate-200">
          <div className="space-y-1 bg-slate-50/80 p-3.5 rounded-lg border border-slate-200/80">
            <span className="text-[10px] font-bold uppercase tracking-wider text-slate-400 block">
              Billed To / Customer Details
            </span>
            <p className="font-bold text-slate-950 text-sm">
              {customer?.name || invoice.customerLedgerId || 'Walk-in Customer'}
            </p>
            {customer?.code && (
              <p className="text-slate-600 font-mono text-[11px]">Customer ID: {customer.code}</p>
            )}
            {customer?.contact.addressLine1 && (
              <p className="text-slate-600">
                {customer.contact.addressLine1}
                {customer.contact.addressLine2 ? `, ${customer.contact.addressLine2}` : ''}
              </p>
            )}
            {(customer?.contact.mobileNumber || customer?.contact.phone) && (
              <p className="text-slate-600 font-mono">
                Phone: {customer.contact.mobileNumber || customer.contact.phone}
              </p>
            )}
            {customer?.taxDetails.registrationNumber && (
              <p className="text-slate-700 font-mono font-medium">
                GSTIN/TRN: {customer.taxDetails.registrationNumber}
              </p>
            )}
          </div>

          <div className="space-y-1.5 p-3.5 rounded-lg border border-slate-200/80 bg-slate-50/80 flex flex-col justify-between">
            <div>
              <span className="text-[10px] font-bold uppercase tracking-wider text-slate-400 block">
                Billing & Supply Details
              </span>
              <div className="grid grid-cols-2 gap-2 mt-2 text-xs">
                <div>
                  <span className="text-slate-500 block">Currency:</span>
                  <span className="font-mono font-bold text-slate-900">{invoice.currency || 'USD'}</span>
                </div>
                <div>
                  <span className="text-slate-500 block">Dispatch Mode:</span>
                  <span className="font-semibold text-slate-900">Direct Delivery</span>
                </div>
              </div>
            </div>
            {invoice.narration && (
              <div className="pt-2 border-t border-slate-200 text-slate-600 italic">
                Note: {invoice.narration}
              </div>
            )}
          </div>
        </div>

        {/* Line Items Table */}
        <div className="pt-4">
          <table className="w-full border-collapse text-left text-xs">
            <thead>
              <tr className="border-b-2 border-slate-800 bg-slate-100 text-slate-800 font-bold uppercase text-[10px]">
                <th className="py-2 px-2 text-center w-8">#</th>
                <th className="py-2 px-3">Item Description & Serial / IMEI</th>
                <th className="py-2 px-2 text-end w-16">Qty</th>
                <th className="py-2 px-2 text-end w-20">Rate</th>
                <th className="py-2 px-2 text-end w-16">Disc</th>
                <th className="py-2 px-2 text-end w-20">Taxable</th>
                <th className="py-2 px-2 text-end w-16">Tax</th>
                <th className="py-2 px-3 text-end w-24">Amount</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200">
              {invoice.lines.map((line, index) => {
                const prod = productMap.get(line.productId);
                const desc = prod?.description || `Product (${line.productId.slice(0, 8)})`;
                const code = prod?.code;
                const lineTotal = line.taxable + line.tax;
                const serials = line.serialNumberIds ?? [];

                return (
                  <tr key={line.lineNumber || index} className="align-top">
                    <td className="py-2.5 px-2 text-center font-mono text-slate-500">{index + 1}</td>
                    <td className="py-2.5 px-3">
                      <div className="font-bold text-slate-900">{desc}</div>
                      {code && <div className="text-[10px] text-slate-500 font-mono">Code: {code}</div>}
                      {serials.length > 0 && (
                        <div className="mt-1 flex flex-wrap items-center gap-1">
                          <span className="text-[10px] font-semibold text-brand-700 bg-brand-50 px-1.5 py-0.2 rounded border border-brand-200">
                            Serial/IMEI:
                          </span>
                          {serials.map((sn, sIdx) => (
                            <span
                              key={sIdx}
                              className="font-mono text-[10px] font-medium bg-slate-100 px-1 rounded border border-slate-200 text-slate-700"
                            >
                              {sn}
                            </span>
                          ))}
                        </div>
                      )}
                    </td>
                    <td className="py-2.5 px-2 text-end font-mono font-medium">{line.quantity}</td>
                    <td className="py-2.5 px-2 text-end font-mono">{moneyAlways(line.rate)}</td>
                    <td className="py-2.5 px-2 text-end font-mono text-slate-500">
                      {line.discount > 0 ? moneyAlways(line.discount) : '—'}
                    </td>
                    <td className="py-2.5 px-2 text-end font-mono">{moneyAlways(line.taxable)}</td>
                    <td className="py-2.5 px-2 text-end font-mono text-slate-600">{moneyAlways(line.tax)}</td>
                    <td className="py-2.5 px-3 text-end font-mono font-bold text-slate-950">
                      {moneyAlways(lineTotal)}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>

      {/* Totals & Summary Block */}
      <div className="pt-6 border-t-2 border-slate-900 space-y-4">
        <div className="grid grid-cols-12 gap-6">
          <div className="col-span-7 flex flex-col justify-between space-y-3">
            <div>
              <span className="text-[10px] font-bold uppercase tracking-wider text-slate-400 block">
                Amount Chargeable (in words):
              </span>
              <p className="font-semibold text-slate-900 text-xs italic mt-0.5">
                {grandTotalWords}
              </p>
            </div>

            <div className="space-y-1 text-[10px] text-slate-500 bg-slate-50 p-3 rounded border border-slate-200">
              <p className="font-bold uppercase text-slate-600">Terms & Conditions:</p>
              <p>1. Goods once sold will not be returned without authorized authorization.</p>
              <p>2. Serial/IMEI carries warranty as specified by the equipment manufacturer.</p>
              <p>3. Disputes subject to exclusive jurisdiction of local commercial courts.</p>
            </div>
          </div>

          <div className="col-span-5 bg-slate-50 p-3 rounded-lg border border-slate-200 space-y-1.5 text-xs font-mono">
            <div className="flex justify-between text-slate-600">
              <span>Taxable Value:</span>
              <span>{moneyAlways(invoice.header.taxable)}</span>
            </div>
            <div className="flex justify-between text-slate-600">
              <span>Tax Amount:</span>
              <span>{moneyAlways(invoice.header.tax)}</span>
            </div>
            {invoice.header.chargeTotal !== 0 && (
              <div className="flex justify-between text-slate-600">
                <span>Additional Charges:</span>
                <span>{moneyAlways(invoice.header.chargeTotal)}</span>
              </div>
            )}
            {invoice.header.roundingDifference !== 0 && (
              <div className="flex justify-between text-slate-600">
                <span>Round Off:</span>
                <span>{moneyAlways(invoice.header.roundingDifference)}</span>
              </div>
            )}
            <div className="flex justify-between text-base font-black text-slate-950 pt-2 border-t-2 border-slate-800">
              <span className="font-sans uppercase text-sm">Grand Total:</span>
              <span>{moneyAlways(grandTotal)}</span>
            </div>
          </div>
        </div>

        {/* Footer Signature */}
        <div className="flex justify-between items-end pt-8 text-[11px] text-slate-600">
          <div>
            <p className="font-semibold">Customer Acknowledgement & Signature</p>
            <div className="mt-8 w-44 border-b border-slate-400" />
          </div>

          <div className="text-right">
            <p className="font-semibold">For {tenantCode ? tenantCode.toUpperCase() : 'INSPIRE ERP'}</p>
            <p className="text-[10px] text-slate-400">Authorized Signatory</p>
            <div className="mt-8 w-48 border-b border-slate-400 ml-auto" />
          </div>
        </div>
      </div>
    </div>
  );
}

// =============================================================================
// A5 Format (Compact Half-Page Trade Invoice)
// =============================================================================
function A5InvoiceLayout({
  invoice,
  customer,
  warehouse,
  productMap,
  docTitle,
  tenantCode,
  grandTotal,
  grandTotalWords,
}: {
  readonly invoice: SalesInvoiceDetail;
  readonly customer?: CustomerSummary | null | undefined;
  readonly warehouse?: WarehouseSummary | null | undefined;
  readonly productMap: Map<string, ProductSummary>;
  readonly docTitle: string;
  readonly tenantCode: string | null;
  readonly grandTotal: number;
  readonly grandTotalWords: string;
}): React.JSX.Element {
  return (
    <div className="print-area w-full max-w-[148mm] min-h-[210mm] bg-white text-slate-900 p-5 shadow-xl rounded-lg border border-slate-200 text-[11px] font-sans leading-tight flex flex-col justify-between">
      <div>
        {/* Compact Header */}
        <div className="flex items-start justify-between border-b border-slate-800 pb-3">
          <div>
            <h2 className="text-base font-black uppercase text-slate-950">
              {tenantCode ? `${tenantCode.toUpperCase()}` : 'INSPIRE ERP'}
            </h2>
            <p className="text-[10px] text-slate-500 font-medium">
              {warehouse ? `${warehouse.name} · ` : ''}Retail & Distribution
            </p>
          </div>

          <div className="text-right">
            <span className="font-bold text-xs uppercase bg-slate-800 text-white px-2 py-0.5 rounded">
              {docTitle}
            </span>
            <div className="font-mono font-bold text-xs pt-1">{invoice.header.number}</div>
            <div className="font-mono text-[10px] text-slate-500">Date: {invoice.date}</div>
          </div>
        </div>

        {/* Customer snippet */}
        <div className="py-2.5 border-b border-slate-200 flex justify-between items-start text-[10px]">
          <div>
            <span className="text-slate-400 uppercase font-bold text-[9px]">To:</span>
            <p className="font-bold text-slate-900 text-xs">
              {customer?.name || invoice.customerLedgerId || 'Walk-in Customer'}
            </p>
            {customer?.contact.mobileNumber && (
              <p className="font-mono text-slate-600">Mob: {customer.contact.mobileNumber}</p>
            )}
          </div>
          {customer?.taxDetails.registrationNumber && (
            <div className="text-right font-mono">
              <span className="text-slate-400 text-[9px]">TRN:</span>
              <p className="font-medium text-slate-700">{customer.taxDetails.registrationNumber}</p>
            </div>
          )}
        </div>

        {/* Compact Items Table */}
        <div className="pt-2">
          <table className="w-full text-[10px] border-collapse">
            <thead>
              <tr className="border-b border-slate-700 bg-slate-100 font-bold uppercase text-slate-700">
                <th className="py-1 px-1 text-center w-6">#</th>
                <th className="py-1 px-1.5">Item & Serial</th>
                <th className="py-1 px-1 text-end w-12">Qty</th>
                <th className="py-1 px-1 text-end w-14">Rate</th>
                <th className="py-1 px-1.5 text-end w-16">Total</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200">
              {invoice.lines.map((line, idx) => {
                const prod = productMap.get(line.productId);
                const desc = prod?.description || `Item #${idx + 1}`;
                const total = line.taxable + line.tax;
                const serials = line.serialNumberIds ?? [];

                return (
                  <tr key={line.lineNumber || idx} className="align-top">
                    <td className="py-1.5 px-1 text-center font-mono text-slate-500">{idx + 1}</td>
                    <td className="py-1.5 px-1.5">
                      <div className="font-bold text-slate-900">{desc}</div>
                      {serials.length > 0 && (
                        <div className="font-mono text-[9px] text-brand-700 flex flex-wrap gap-1 mt-0.5">
                          <span>S/N: {serials.join(', ')}</span>
                        </div>
                      )}
                    </td>
                    <td className="py-1.5 px-1 text-end font-mono font-medium">{line.quantity}</td>
                    <td className="py-1.5 px-1 text-end font-mono">{moneyAlways(line.rate)}</td>
                    <td className="py-1.5 px-1.5 text-end font-mono font-bold text-slate-950">
                      {moneyAlways(total)}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>

      {/* Bottom Summary */}
      <div className="pt-3 border-t border-slate-800 space-y-2">
        <div className="flex justify-between items-center bg-slate-50 p-2 rounded border border-slate-200">
          <div className="space-y-0.5">
            <span className="text-[9px] uppercase font-bold text-slate-400">Net Payable:</span>
            <p className="text-[10px] text-slate-600 italic">{grandTotalWords}</p>
          </div>
          <div className="font-mono font-black text-sm text-slate-950">
            {moneyAlways(grandTotal)}
          </div>
        </div>

        <div className="flex justify-between items-end pt-3 text-[9px] text-slate-500">
          <div>Goods received in good order.</div>
          <div className="text-right font-medium">Auth Signatory</div>
        </div>
      </div>
    </div>
  );
}

// =============================================================================
// Thermal Printer Format (80mm POS Roll Receipt)
// =============================================================================
function ThermalInvoiceLayout({
  invoice,
  customer,
  warehouse,
  productMap,
  docTitle,
  tenantCode,
  grandTotal,
}: {
  readonly invoice: SalesInvoiceDetail;
  readonly customer?: CustomerSummary | null | undefined;
  readonly warehouse?: WarehouseSummary | null | undefined;
  readonly productMap: Map<string, ProductSummary>;
  readonly docTitle: string;
  readonly tenantCode: string | null;
  readonly grandTotal: number;
}): React.JSX.Element {
  return (
    <div className="print-area w-full max-w-[80mm] min-h-[120mm] bg-white text-black p-3.5 shadow-xl rounded-md border border-slate-300 font-mono text-[11px] leading-tight flex flex-col justify-between">
      <div>
        {/* Receipt Header */}
        <div className="text-center space-y-1 pb-2">
          <h2 className="text-sm font-black uppercase tracking-tight">
            {tenantCode ? tenantCode.toUpperCase() : 'INSPIRE POS'}
          </h2>
          <p className="text-[10px]">
            {warehouse?.name || 'Retail Counter'}
          </p>
          <p className="text-[10px]">Tax Reg: 100293847500003</p>
          <div className="border-t border-dashed border-black my-1.5" />
          <div className="font-bold text-xs uppercase">{docTitle}</div>
          <div className="border-b border-dashed border-black my-1.5" />
        </div>

        {/* Metadata */}
        <div className="space-y-0.5 text-[10px] pb-2">
          <div className="flex justify-between">
            <span>Bill No:</span>
            <span className="font-bold">{invoice.header.number}</span>
          </div>
          <div className="flex justify-between">
            <span>Date:</span>
            <span>{invoice.date}</span>
          </div>
          <div className="flex justify-between">
            <span>Cust:</span>
            <span className="truncate max-w-[42mm]">
              {customer?.name || invoice.customerLedgerId || 'Walk-in'}
            </span>
          </div>
        </div>

        <div className="border-t border-dashed border-black pb-1.5" />

        {/* Line Items List */}
        <div className="space-y-2 py-1">
          {invoice.lines.map((line, idx) => {
            const prod = productMap.get(line.productId);
            const desc = prod?.description || `Item #${idx + 1}`;
            const total = line.taxable + line.tax;
            const serials = line.serialNumberIds ?? [];

            return (
              <div key={line.lineNumber || idx} className="space-y-0.5">
                <div className="font-bold truncate text-[11px]">{desc}</div>
                {serials.length > 0 && (
                  <div className="text-[9px] text-slate-700 pl-1">
                    IMEI: {serials.join(', ')}
                  </div>
                )}
                <div className="flex justify-between text-[10px]">
                  <span>
                    {line.quantity} x {moneyAlways(line.rate)}
                  </span>
                  <span className="font-bold">{moneyAlways(total)}</span>
                </div>
              </div>
            );
          })}
        </div>

        <div className="border-t border-dashed border-black my-2" />

        {/* Financial Summary */}
        <div className="space-y-1 text-[10px]">
          <div className="flex justify-between">
            <span>Taxable:</span>
            <span>{moneyAlways(invoice.header.taxable)}</span>
          </div>
          <div className="flex justify-between">
            <span>Tax:</span>
            <span>{moneyAlways(invoice.header.tax)}</span>
          </div>
          {invoice.header.chargeTotal !== 0 && (
            <div className="flex justify-between">
              <span>Charges:</span>
              <span>{moneyAlways(invoice.header.chargeTotal)}</span>
            </div>
          )}
          <div className="border-t border-black pt-1 flex justify-between text-xs font-black">
            <span>TOTAL:</span>
            <span>{moneyAlways(grandTotal)}</span>
          </div>
        </div>
      </div>

      {/* Receipt Footer */}
      <div className="text-center pt-4 space-y-1 text-[10px]">
        <div className="border-t border-dashed border-black pt-2" />
        <p className="font-bold">THANK YOU! VISIT AGAIN</p>
        <p className="text-[9px]">Items with IMEI carry standard warranty</p>
        <p className="text-[8px] text-slate-500">Inspire ERP 2.0</p>
      </div>
    </div>
  );
}
