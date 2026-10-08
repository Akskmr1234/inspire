import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { CheckField, Field, SelectField, TextField } from '@/components/Form';
import { SearchSelect } from '@/components/SearchSelect';
import { IconCheck, IconClose, IconMoon, IconPlus, IconSparkles, IconSun } from '@/components/icons';
import { useSession, setDefaultAppTheme } from '@/stores/session';
import { populateSampleTransactions } from '@/lib/sampleData';
import { currentBranchId, type ApiError } from '@/lib/api';
import {
  listMaster,
  type BrandSummary,
  type CategorySummary,
  type UnitSummary,
  type WarehouseSummary,
} from '@/lib/inventory';
import { listCustomers, type CustomerSummary } from '@/lib/customers';
import { listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { PAYMENT_MODES } from '@/lib/paymentModes';
import {
  DECIMAL_CHOICES,
  useSettings,
  type DefaultAdditionalLedgerConfig,
  type InvoicePrintFormat,
} from '@/stores/settings';
import { useMoney } from '@/lib/money';

const SAMPLE_AMOUNT = 1234.5678;

const TRANSACTION_TYPES = [
  { id: 'Sales', label: 'Sales' },
  { id: 'SalesReturn', label: 'Sales Return' },
  { id: 'Purchase', label: 'Purchase' },
  { id: 'PurchaseReturn', label: 'Purchase Return' },
  { id: 'DeliveryNote', label: 'Delivery Note' },
  { id: 'Production', label: 'Production' },
  { id: 'StockTransfer', label: 'Stock Transfer' },
] as const;

/**
 * Master Default Value Settings for all main dropdowns and master entry fields.
 *
 * Configures default values for Customer, Supplier, Salesman, Billingman,
 * Warehouse, Payment Mode, Tax Mode, Sales Rate, Category, and UOM.
 * Also configures Currency and Quantity decimal places and feature toggles.
 */
export function MasterDefaultsSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();
  const branchId = currentBranchId();
  const { places } = useMoney();
  const { theme } = useSession();
  const queryClient = useQueryClient();

  const [selectedTxType, setSelectedTxType] = useState<string>('Sales');
  const [seeding, setSeeding] = useState(false);
  const [seedResult, setSeedResult] = useState<string | null>(null);

  const runSeeder = async (): Promise<void> => {
    try {
      setSeeding(true);
      setSeedResult(null);
      const res = await populateSampleTransactions();
      await queryClient.invalidateQueries();
      setSeedResult(
        `Generated sample data: ${res.salesInvoicesCreated} Sales Invoices, ${res.purchaseInvoicesCreated} Purchase Invoices, ${res.ordersCreated} Orders, ${res.vouchersCreated} Vouchers.`,
      );
    } catch {
      setSeedResult('Failed to populate some sample transactions.');
    } finally {
      setSeeding(false);
    }
  };

  const currentDefaults = (settings.defaultAdditionalLedgers ?? []).filter(
    (entry) => entry.transactionType === selectedTxType,
  );

  const addDefaultLedger = (): void => {
    const newRow: DefaultAdditionalLedgerConfig = {
      id: Math.random().toString(36).slice(2, 9),
      transactionType: selectedTxType,
      ledgerId: '',
      isAddition: true,
      defaultAmount: '0.00',
      isDefaultActive: true,
    };
    settings.update({
      defaultAdditionalLedgers: [...(settings.defaultAdditionalLedgers ?? []), newRow],
    });
  };

  const updateDefaultLedger = (
    id: string,
    patch: Partial<DefaultAdditionalLedgerConfig>,
  ): void => {
    const next = (settings.defaultAdditionalLedgers ?? []).map((row) =>
      row.id === id ? { ...row, ...patch } : row,
    );
    settings.update({ defaultAdditionalLedgers: next });
  };

  const removeDefaultLedger = (id: string): void => {
    const next = (settings.defaultAdditionalLedgers ?? []).filter((row) => row.id !== id);
    settings.update({ defaultAdditionalLedgers: next });
  };

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });

  const categories = useQuery<readonly CategorySummary[], ApiError>({
    queryKey: ['categories', false],
    queryFn: () => listMaster<CategorySummary>('categories', false),
  });

  const units = useQuery<readonly UnitSummary[], ApiError>({
    queryKey: ['units', false],
    queryFn: () => listMaster<UnitSummary>('units', false),
  });

  const brands = useQuery<readonly BrandSummary[], ApiError>({
    queryKey: ['brands', false],
    queryFn: () => listMaster<BrandSummary>('brands', false),
  });

  const customers = useQuery<readonly CustomerSummary[], ApiError>({
    queryKey: ['customers', false],
    queryFn: () => listCustomers('', false),
  });

  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers', false],
    queryFn: () => listLedgers(false),
  });

  return (
    <div className="card space-y-6 p-6">
      <div className="border-b border-line pb-4">
        <h3 className="text-base font-semibold text-ink">
          {t('settings.masterDefaultsTitle')}
        </h3>
        <p className="mt-1 text-sm text-ink-muted">
          {t('settings.masterDefaultsHint')}
        </p>
      </div>

      {/* Main Dropdown Defaults */}
      <div className="space-y-4">
        <h4 className="text-xs font-bold uppercase tracking-wider text-ink-muted">
          {t('settings.documentsTitle')}
        </h4>

        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <Field label={t('settings.defaultCustomer')}>
            <SearchSelect
              value={settings.defaultCustomerId}
              onChange={(id) => settings.update({ defaultCustomerId: id })}
              clearable
              placeholder={t('settings.askEachTime')}
              label={t('settings.defaultCustomer')}
              options={(customers.data ?? []).map((customer) => ({
                value: customer.customerId,
                label: `${customer.code} — ${customer.name}`,
              }))}
            />
          </Field>

          <Field label={t('settings.defaultSupplier')}>
            <SearchSelect
              value={settings.defaultSupplierId}
              onChange={(id) => settings.update({ defaultSupplierId: id })}
              clearable
              placeholder={t('settings.askEachTime')}
              label={t('settings.defaultSupplier')}
              options={(ledgers.data ?? []).map((ledger) => ({
                value: ledger.ledgerId,
                label: `${ledger.code} — ${ledger.name}`,
              }))}
            />
          </Field>

          <Field label={t('settings.preferredWarehouse')}>
            <SearchSelect
              value={settings.preferredWarehouseId}
              onChange={(id) => settings.update({ preferredWarehouseId: id })}
              clearable
              placeholder={t('settings.warehouseFromMaster')}
              label={t('settings.preferredWarehouse')}
              options={(warehouses.data ?? []).map((warehouse) => ({
                value: warehouse.id,
                label: `${warehouse.code} — ${warehouse.name}`,
                ...(warehouse.isDefault ? { meta: t('settings.masterDefault') } : {}),
              }))}
            />
          </Field>

          <Field label={t('settings.defaultPaymentMode')}>
            <SearchSelect
              value={settings.defaultPaymentMode}
              onChange={(mode) => settings.update({ defaultPaymentMode: mode })}
              clearable
              placeholder={t('settings.askEachTime')}
              label={t('settings.defaultPaymentMode')}
              options={PAYMENT_MODES.map((mode) => ({
                value: mode.value,
                label: t(mode.labelKey),
              }))}
            />
          </Field>

          <SelectField
            label={t('settings.defaultTaxMode')}
            value={settings.defaultTaxMode}
            onChange={(mode) =>
              settings.update({ defaultTaxMode: mode as 'NT' | 'TAX' | 'GST' })
            }
            options={[
              { value: 'NT', label: 'NT (Non-Tax 0%)' },
              { value: 'TAX', label: 'TAX (Standard VAT 5%)' },
              { value: 'GST', label: 'GST (Goods and Services Tax)' },
            ]}
          />

          <SelectField
            label={t('settings.defaultSalesRateType')}
            value={settings.defaultSalesRateType}
            onChange={(type) =>
              settings.update({
                defaultSalesRateType: type as 'retail' | 'wholesale' | 'mrp',
              })
            }
            options={[
              { value: 'retail', label: 'Retail Rate' },
              { value: 'wholesale', label: 'Wholesale Rate' },
              { value: 'mrp', label: 'Maximum Retail Price (MRP)' },
            ]}
          />

          <TextField
            label={t('settings.defaultSalesman')}
            value={settings.defaultSalesman}
            onChange={(value) => settings.update({ defaultSalesman: value })}
            placeholder="Primary Salesman"
          />

          <TextField
            label={t('settings.defaultBillingman')}
            value={settings.defaultBillingman}
            onChange={(value) => settings.update({ defaultBillingman: value })}
            placeholder="Primary Billingman"
          />

          <Field label={t('settings.defaultCategory')}>
            <SearchSelect
              value={settings.defaultCategoryId}
              onChange={(id) => settings.update({ defaultCategoryId: id })}
              clearable
              placeholder={t('settings.askEachTime')}
              label={t('settings.defaultCategory')}
              options={(categories.data ?? []).map((row) => ({
                value: row.id,
                label: `${row.code} — ${row.name}`,
              }))}
            />
          </Field>

          <Field label={t('settings.defaultStockUnit')}>
            <SearchSelect
              value={settings.defaultStockUnitId}
              onChange={(id) => settings.update({ defaultStockUnitId: id })}
              clearable
              placeholder={t('settings.askEachTime')}
              label={t('settings.defaultStockUnit')}
              options={(units.data ?? []).map((row) => ({
                value: row.id,
                label: `${row.code} — ${row.name}`,
              }))}
            />
          </Field>
          <Field label={t('settings.defaultBrand')}>
            <SearchSelect
              value={settings.defaultBrandId}
              onChange={(id) => settings.update({ defaultBrandId: id })}
              clearable
              placeholder={t('settings.askEachTime')}
              label={t('settings.defaultBrand')}
              options={(brands.data ?? []).map((row) => ({
                value: row.id,
                label: `${row.code} — ${row.name}`,
              }))}
            />
          </Field>

          <SelectField
            label={t('products.itemType')}
            value={String(settings.defaultItemType ?? 1)}
            onChange={(val) => settings.update({ defaultItemType: Number(val) })}
            options={[
              { value: '1', label: t('products.itemStock') },
              { value: '2', label: t('products.itemService') },
              { value: '3', label: t('products.itemNonStock') },
            ]}
          />

          <Field label={t('vouchers.debitAccount')}>
            <SearchSelect
              value={settings.defaultDebitAccountId}
              onChange={(id) => settings.update({ defaultDebitAccountId: id })}
              clearable
              placeholder={t('settings.askEachTime')}
              label={t('vouchers.debitAccount')}
              options={(ledgers.data ?? []).map((ledger) => ({
                value: ledger.ledgerId,
                label: `${ledger.code} — ${ledger.name}`,
              }))}
            />
          </Field>

          <Field label={t('vouchers.creditAccount')}>
            <SearchSelect
              value={settings.defaultCreditAccountId}
              onChange={(id) => settings.update({ defaultCreditAccountId: id })}
              clearable
              placeholder={t('settings.askEachTime')}
              label={t('vouchers.creditAccount')}
              options={(ledgers.data ?? []).map((ledger) => ({
                value: ledger.ledgerId,
                label: `${ledger.code} — ${ledger.name}`,
              }))}
            />
          </Field>
        </div>
      </div>

      {/* Theme & Appearance */}
      <div className="space-y-3 border-t border-line pt-4">
        <div>
          <div className="flex items-center gap-2">
            <h4 className="text-xs font-bold uppercase tracking-wider text-ink-muted">
              {t('common.theme')} & Appearance (Default for All Users)
            </h4>
            <span className="rounded bg-brand-500/10 px-1.5 py-0.5 text-[10px] font-semibold text-brand-600 dark:bg-brand-500/20 dark:text-brand-300">
              System Wide
            </span>
          </div>
          <p className="mt-0.5 text-xs text-ink-muted">
            Configure the default interface appearance. The selected theme applies as the default for all users and workstations across the application.
          </p>
        </div>

        <div className="flex flex-wrap gap-3">
          <button
            type="button"
            onClick={() => {
              settings.update({ defaultTheme: 'light' });
              setDefaultAppTheme('light');
            }}
            className={clsx(
              'flex items-center gap-2.5 rounded-lg border px-4 py-2 text-sm font-medium transition',
              theme === 'light'
                ? 'border-brand-600 bg-brand-500/10 text-brand-700 font-semibold ring-1 ring-brand-600 dark:text-brand-300'
                : 'border-line bg-surface hover:bg-surface-2 text-ink',
            )}
          >
            <IconSun className="size-4.5 text-amber-500" />
            <span>Light Theme (Default)</span>
            {theme === 'light' && (
              <span className="ms-1 size-1.5 rounded-full bg-brand-600" />
            )}
          </button>

          <button
            type="button"
            onClick={() => {
              settings.update({ defaultTheme: 'dark' });
              setDefaultAppTheme('dark');
            }}
            className={clsx(
              'flex items-center gap-2.5 rounded-lg border px-4 py-2 text-sm font-medium transition',
              theme === 'dark'
                ? 'border-brand-600 bg-brand-500/10 text-brand-700 font-semibold ring-1 ring-brand-600 dark:text-brand-300'
                : 'border-line bg-surface hover:bg-surface-2 text-ink',
            )}
          >
            <IconMoon className="size-4.5 text-indigo-400" />
            <span>Dark Theme (Default)</span>
            {theme === 'dark' && (
              <span className="ms-1 size-1.5 rounded-full bg-brand-600" />
            )}
          </button>
        </div>
      </div>

      {/* Transaction Numbering & Series */}
      <div className="space-y-4 border-t border-line pt-4">
        <div>
          <h4 className="text-xs font-bold uppercase tracking-wider text-ink-muted">
            Transaction Numbering & Series
          </h4>
          <p className="mt-0.5 text-xs text-ink-muted">
            Customise prefix and suffix formats for invoices, returns, orders, and payment vouchers.
          </p>
        </div>

        <div className="grid gap-3.5 sm:grid-cols-2 lg:grid-cols-3">
          {/* Sales Invoice */}
          <div className="rounded-lg border border-line bg-surface-2/40 p-3 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-ink">Sales Invoice</span>
              <span className="font-mono text-[11px] font-bold text-accent bg-accent/10 px-1.5 py-0.5 rounded">
                {`${settings.invoicePrefix || 'INV-'}1001${settings.invoiceSuffix || ''}`}
              </span>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="field-label text-[11px]">Prefix</label>
                <input
                  type="text"
                  value={settings.invoicePrefix}
                  onChange={(e) => settings.update({ invoicePrefix: e.target.value })}
                  placeholder="INV-"
                  className="field-input-sm w-full font-mono text-xs"
                />
              </div>
              <div>
                <label className="field-label text-[11px]">Suffix</label>
                <input
                  type="text"
                  value={settings.invoiceSuffix}
                  onChange={(e) => settings.update({ invoiceSuffix: e.target.value })}
                  placeholder="/2026"
                  className="field-input-sm w-full font-mono text-xs"
                />
              </div>
            </div>
          </div>

          {/* Sales Return */}
          <div className="rounded-lg border border-line bg-surface-2/40 p-3 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-ink">Sales Return</span>
              <span className="font-mono text-[11px] font-bold text-accent bg-accent/10 px-1.5 py-0.5 rounded">
                {`${settings.salesReturnPrefix || 'RET-'}1001${settings.salesReturnSuffix || ''}`}
              </span>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="field-label text-[11px]">Prefix</label>
                <input
                  type="text"
                  value={settings.salesReturnPrefix}
                  onChange={(e) => settings.update({ salesReturnPrefix: e.target.value })}
                  placeholder="RET-"
                  className="field-input-sm w-full font-mono text-xs"
                />
              </div>
              <div>
                <label className="field-label text-[11px]">Suffix</label>
                <input
                  type="text"
                  value={settings.salesReturnSuffix}
                  onChange={(e) => settings.update({ salesReturnSuffix: e.target.value })}
                  placeholder="/2026"
                  className="field-input-sm w-full font-mono text-xs"
                />
              </div>
            </div>
          </div>

          {/* Purchase Invoice */}
          <div className="rounded-lg border border-line bg-surface-2/40 p-3 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-ink">Purchase Invoice</span>
              <span className="font-mono text-[11px] font-bold text-accent bg-accent/10 px-1.5 py-0.5 rounded">
                {`${settings.purchaseInvoicePrefix || 'PI-'}1001${settings.purchaseInvoiceSuffix || ''}`}
              </span>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="field-label text-[11px]">Prefix</label>
                <input
                  type="text"
                  value={settings.purchaseInvoicePrefix}
                  onChange={(e) => settings.update({ purchaseInvoicePrefix: e.target.value })}
                  placeholder="PI-"
                  className="field-input-sm w-full font-mono text-xs"
                />
              </div>
              <div>
                <label className="field-label text-[11px]">Suffix</label>
                <input
                  type="text"
                  value={settings.purchaseInvoiceSuffix}
                  onChange={(e) => settings.update({ purchaseInvoiceSuffix: e.target.value })}
                  placeholder="/2026"
                  className="field-input-sm w-full font-mono text-xs"
                />
              </div>
            </div>
          </div>

          {/* Sales Order */}
          <div className="rounded-lg border border-line bg-surface-2/40 p-3 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-ink">Sales Order</span>
              <span className="font-mono text-[11px] font-bold text-accent bg-accent/10 px-1.5 py-0.5 rounded">
                {`${settings.salesOrderPrefix || 'SO-'}1001${settings.salesOrderSuffix || ''}`}
              </span>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="field-label text-[11px]">Prefix</label>
                <input
                  type="text"
                  value={settings.salesOrderPrefix}
                  onChange={(e) => settings.update({ salesOrderPrefix: e.target.value })}
                  placeholder="SO-"
                  className="field-input-sm w-full font-mono text-xs"
                />
              </div>
              <div>
                <label className="field-label text-[11px]">Suffix</label>
                <input
                  type="text"
                  value={settings.salesOrderSuffix}
                  onChange={(e) => settings.update({ salesOrderSuffix: e.target.value })}
                  placeholder=""
                  className="field-input-sm w-full font-mono text-xs"
                />
              </div>
            </div>
          </div>

          {/* Purchase Order */}
          <div className="rounded-lg border border-line bg-surface-2/40 p-3 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-ink">Purchase Order</span>
              <span className="font-mono text-[11px] font-bold text-accent bg-accent/10 px-1.5 py-0.5 rounded">
                {`${settings.purchaseOrderPrefix || 'PO-'}1001${settings.purchaseOrderSuffix || ''}`}
              </span>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="field-label text-[11px]">Prefix</label>
                <input
                  type="text"
                  value={settings.purchaseOrderPrefix}
                  onChange={(e) => settings.update({ purchaseOrderPrefix: e.target.value })}
                  placeholder="PO-"
                  className="field-input-sm w-full font-mono text-xs"
                />
              </div>
              <div>
                <label className="field-label text-[11px]">Suffix</label>
                <input
                  type="text"
                  value={settings.purchaseOrderSuffix}
                  onChange={(e) => settings.update({ purchaseOrderSuffix: e.target.value })}
                  placeholder=""
                  className="field-input-sm w-full font-mono text-xs"
                />
              </div>
            </div>
          </div>

          {/* Vouchers (Payment & Receipt) */}
          <div className="rounded-lg border border-line bg-surface-2/40 p-3 space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-ink">Payment & Receipt</span>
              <span className="font-mono text-[11px] font-bold text-accent bg-accent/10 px-1.5 py-0.5 rounded">
                {`${settings.paymentVoucherPrefix || 'PAY-'}1001`}
              </span>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <label className="field-label text-[11px]">Pay Prefix</label>
                <input
                  type="text"
                  value={settings.paymentVoucherPrefix}
                  onChange={(e) => settings.update({ paymentVoucherPrefix: e.target.value })}
                  placeholder="PAY-"
                  className="field-input-sm w-full font-mono text-xs"
                />
              </div>
              <div>
                <label className="field-label text-[11px]">Rec Prefix</label>
                <input
                  type="text"
                  value={settings.receiptVoucherPrefix}
                  onChange={(e) => settings.update({ receiptVoucherPrefix: e.target.value })}
                  placeholder="REC-"
                  className="field-input-sm w-full font-mono text-xs"
                />
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Decimal Settings */}
      <div className="space-y-4 border-t border-line pt-4">
        <h4 className="text-xs font-bold uppercase tracking-wider text-ink-muted">
          {t('settings.decimalsTitle')}
        </h4>

        <div className="grid gap-4 sm:grid-cols-2">
          <SelectField
            label={t('settings.decimals')}
            value={String(places)}
            onChange={(value) => {
              const chosen = Number(value);
              if (!DECIMAL_CHOICES.includes(chosen)) return;

              if (branchId === null) {
                settings.update({ decimals: chosen });
              } else {
                settings.update({
                  decimalsByBranch: {
                    ...settings.decimalsByBranch,
                    [branchId]: chosen,
                  },
                });
              }
            }}
            options={DECIMAL_CHOICES.map((choice) => ({
              value: String(choice),
              label:
                choice === 2
                  ? `2 decimals (.00) — ${SAMPLE_AMOUNT.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
                  : choice === 3
                    ? `3 decimals (.000) — ${SAMPLE_AMOUNT.toLocaleString(undefined, { minimumFractionDigits: 3, maximumFractionDigits: 3 })}`
                    : `${choice} decimals — ${SAMPLE_AMOUNT.toLocaleString(undefined, { minimumFractionDigits: choice, maximumFractionDigits: choice })}`,
            }))}
          />

          <SelectField
            label={t('settings.quantityDecimals')}
            value={String(settings.quantityDecimals ?? 2)}
            onChange={(value) =>
              settings.update({ quantityDecimals: Number(value) })
            }
            options={[
              { value: '0', label: '0 decimals (1)' },
              { value: '1', label: '1 decimal (1.0)' },
              { value: '2', label: '2 decimals (1.00)' },
              { value: '3', label: '3 decimals (1.000)' },
              { value: '4', label: '4 decimals (1.0000)' },
            ]}
          />
        </div>
      </div>

      {/* Feature Options & Toggles */}
      <div className="space-y-3 border-t border-line pt-4">
        <h4 className="text-xs font-bold uppercase tracking-wider text-ink-muted">
          {t('settings.title')} Options
        </h4>

        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <CheckField
            label={t('settings.enableDeviceAttributes')}
            checked={settings.enableDeviceAttributes}
            onChange={(checked) => settings.update({ enableDeviceAttributes: checked })}
          />

          <CheckField
            label={t('settings.enableReverseCalculation')}
            checked={settings.enableReverseCalculation}
            onChange={(checked) =>
              settings.update({ enableReverseCalculation: checked })
            }
          />

          <CheckField
            label={t('settings.enableFreeQuantity')}
            checked={settings.enableFreeQuantity}
            onChange={(checked) => settings.update({ enableFreeQuantity: checked })}
          />

          <CheckField
            label={t('settings.enableItemDiscount')}
            checked={settings.enableItemDiscount}
            onChange={(checked) => settings.update({ enableItemDiscount: checked })}
          />

          <CheckField
            label={t('settings.enableAutoBatch')}
            checked={settings.enableAutoBatch}
            onChange={(checked) => settings.update({ enableAutoBatch: checked })}
          />
        </div>
      </div>

      {/* Additional Ledger Defaults (Requirement 12) */}
      <div className="space-y-4 border-t border-line pt-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h4 className="text-xs font-bold uppercase tracking-wider text-ink-muted">
              {t('settings.additionalLedgerDefaults')}
            </h4>
            <p className="mt-0.5 text-xs text-ink-muted">
              {t('settings.additionalLedgerDefaultsHint')}
            </p>
          </div>
          <button
            type="button"
            onClick={addDefaultLedger}
            className="btn-secondary btn-xs flex items-center gap-1 text-xs"
          >
            <IconPlus className="size-3" />
            {t('settings.addDefaultLedger')}
          </button>
        </div>

        {/* Transaction Type Tabs */}
        <div className="flex flex-wrap gap-1 rounded-lg border border-line bg-surface-2 p-1">
          {TRANSACTION_TYPES.map((type) => {
            const count = (settings.defaultAdditionalLedgers ?? []).filter(
              (al) => al.transactionType === type.id,
            ).length;
            const active = selectedTxType === type.id;
            return (
              <button
                key={type.id}
                type="button"
                onClick={() => setSelectedTxType(type.id)}
                className={`flex items-center gap-1.5 rounded-md px-2.5 py-1 text-xs font-medium transition ${
                  active
                    ? 'bg-surface text-ink shadow-sm ring-1 ring-line'
                    : 'text-ink-muted hover:bg-surface/50 hover:text-ink'
                }`}
              >
                <span>{type.label}</span>
                {count > 0 && (
                  <span className="rounded-full bg-accent/15 px-1.5 py-0.2 text-[10px] font-semibold text-accent">
                    {count}
                  </span>
                )}
              </button>
            );
          })}
        </div>

        {/* Defaults Table for Selected Transaction Type */}
        {currentDefaults.length === 0 ? (
          <div className="rounded-lg border border-dashed border-line p-6 text-center">
            <p className="text-xs text-ink-muted">
              No default additional ledgers configured for{' '}
              <span className="font-semibold text-ink">
                {TRANSACTION_TYPES.find((t) => t.id === selectedTxType)?.label}
              </span>
              . Click &quot;Add Default Ledger&quot; above to add one.
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto rounded-lg border border-line bg-surface">
            <table className="w-full text-xs">
              <thead className="border-b border-line bg-surface-2">
                <tr className="text-ink-muted">
                  <th className="px-3 py-2 text-start font-semibold">Ledger</th>
                  <th className="w-32 px-3 py-2 text-center font-semibold">Effect</th>
                  <th className="w-28 px-3 py-2 text-end font-semibold">Default Amount</th>
                  <th className="w-24 px-3 py-2 text-center font-semibold">Active</th>
                  <th className="w-10 px-2 py-2" />
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {currentDefaults.map((row) => (
                  <tr key={row.id} className="hover:bg-surface-2/40">
                    <td className="px-3 py-1.5">
                      <SearchSelect
                        value={row.ledgerId}
                        onChange={(ledgerId) =>
                          updateDefaultLedger(row.id, { ledgerId })
                        }
                        options={(ledgers.data ?? []).map((l) => ({
                          value: l.ledgerId,
                          label: `${l.code} — ${l.name}`,
                        }))}
                        size="sm"
                        label="Ledger"
                        placeholder="Choose ledger"
                      />
                    </td>
                    <td className="px-3 py-1.5 text-center">
                      <select
                        value={row.isAddition ? 'add' : 'deduct'}
                        onChange={(e) =>
                          updateDefaultLedger(row.id, {
                            isAddition: e.target.value === 'add',
                          })
                        }
                        className="field-input-sm w-full py-0.5 text-xs font-medium"
                      >
                        <option value="add">Addition (+)</option>
                        <option value="deduct">Deduction (-)</option>
                      </select>
                    </td>
                    <td className="px-3 py-1.5 text-end">
                      <input
                        type="number"
                        inputMode="decimal"
                        step="any"
                        value={row.defaultAmount}
                        onChange={(e) =>
                          updateDefaultLedger(row.id, {
                            defaultAmount: e.target.value,
                          })
                        }
                        placeholder="0.00"
                        className="field-input-sm w-24 text-end font-mono tabular-nums"
                      />
                    </td>
                    <td className="px-3 py-1.5 text-center">
                      <input
                        type="checkbox"
                        checked={row.isDefaultActive}
                        onChange={(e) =>
                          updateDefaultLedger(row.id, {
                            isDefaultActive: e.target.checked,
                          })
                        }
                        className="size-4 rounded border-line text-accent focus:ring-accent"
                      />
                    </td>
                    <td className="px-2 py-1.5 text-end">
                      <button
                        type="button"
                        onClick={() => removeDefaultLedger(row.id)}
                        className="rounded p-1 text-ink-muted transition hover:text-red-600"
                        title="Delete"
                      >
                        <IconClose className="size-3.5" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Invoice Print & Template Formats */}
      <div className="space-y-4 border-t border-line pt-4">
        <div>
          <h4 className="text-xs font-bold uppercase tracking-wider text-ink-muted">
            {t('settings.invoicePrintFormatsTitle')}
          </h4>
          <p className="mt-0.5 text-xs text-ink-muted">
            {t('settings.invoicePrintFormatsHint')}
          </p>
        </div>

        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <SelectField
            label={t('settings.defaultInvoicePrintFormat')}
            value={settings.defaultInvoicePrintFormat}
            onChange={(val) =>
              settings.update({ defaultInvoicePrintFormat: val as InvoicePrintFormat })
            }
            options={[
              { value: 'A4', label: 'A4 Format (Standard Tax Invoice)' },
              { value: 'A5', label: 'A5 Format (Compact Half-Page)' },
              { value: 'thermal', label: 'Thermal Printer (80mm POS Roll)' },
            ]}
          />
        </div>

        {/* Branch-wise configuration table */}
        {(warehouses.data ?? []).length > 0 && (
          <div className="space-y-2 pt-2">
            <h5 className="text-xs font-semibold text-ink">
              {t('settings.branchInvoicePrintFormats')}
            </h5>
            <div className="overflow-x-auto rounded-lg border border-line bg-surface">
              <table className="w-full text-xs">
                <thead>
                  <tr className="border-b border-line bg-surface-2/60 text-ink-muted text-start">
                    <th className="px-3 py-2 font-medium">Branch / Warehouse</th>
                    <th className="px-3 py-2 font-medium">Code</th>
                    <th className="px-3 py-2 font-medium">Assigned Invoice Print Format</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-line">
                  {(warehouses.data ?? []).map((wh) => {
                    const currentBranchFmt = settings.branchInvoicePrintFormats[wh.id] || '';
                    return (
                      <tr key={wh.id} className="hover:bg-surface-2/30">
                        <td className="px-3 py-2 font-medium text-ink">
                          {wh.name} {wh.isDefault && <span className="text-[10px] text-accent font-normal">(Default)</span>}
                        </td>
                        <td className="px-3 py-2 font-mono text-ink-muted">{wh.code}</td>
                        <td className="px-3 py-2">
                          <select
                            value={currentBranchFmt}
                            onChange={(e) => {
                              const val = e.target.value;
                              const next = { ...settings.branchInvoicePrintFormats };
                              if (val) {
                                next[wh.id] = val as InvoicePrintFormat;
                              } else {
                                delete next[wh.id];
                              }
                              settings.update({ branchInvoicePrintFormats: next });
                            }}
                            className="field-input-sm w-full max-w-xs text-xs"
                          >
                            <option value="">
                              Follow System Default ({settings.defaultInvoicePrintFormat})
                            </option>
                            <option value="A4">A4 Format (Standard Tax Invoice)</option>
                            <option value="A5">A5 Format (Compact Half-Page)</option>
                            <option value="thermal">Thermal Printer (80mm POS Roll)</option>
                          </select>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>

      {/* Sample Demo Data & Transactions Generator */}
      <div className="space-y-3 border-t border-line pt-4">
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
          <div>
            <h4 className="text-xs font-bold uppercase tracking-wider text-ink-muted flex items-center gap-1.5">
              <IconSparkles className="size-4 text-accent" />
              <span>Sample Demo Transactions & Masters</span>
            </h4>
            <p className="mt-0.5 text-xs text-ink-muted">
              Populate realistic demo transactions (Sales Invoices, Returns, Purchases, Orders, and Vouchers) and demo master records.
            </p>
          </div>

          <button
            type="button"
            disabled={seeding}
            onClick={runSeeder}
            className="btn-primary btn-sm flex items-center gap-1.5 shrink-0"
          >
            <IconSparkles className={clsx('size-3.5', seeding && 'animate-spin')} />
            <span>{seeding ? 'Generating Sample Data…' : 'Generate Sample Transactions'}</span>
          </button>
        </div>

        {seedResult && (
          <div className="flex items-center gap-2 rounded-lg border border-emerald-500/30 bg-emerald-500/10 px-3 py-2 text-xs text-emerald-700 dark:text-emerald-300">
            <IconCheck className="size-4 shrink-0" />
            <span>{seedResult}</span>
          </div>
        )}
      </div>
    </div>
  );
}

