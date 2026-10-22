import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import clsx from 'clsx';
import {
  CheckField,
  Field,
  NumberField,
  SelectField,
  TextField,
} from '@/components/Form';
import { NumberingSettings } from '@/components/NumberingSettings';
import { PolicySettings } from '@/components/PolicySettings';
import { SearchSelect } from '@/components/SearchSelect';
import { FinancialYearsSettings } from '@/components/settings/FinancialYearsSettings';
import {
  IconCheck,
  IconClose,
  IconMoon,
  IconPlus,
  IconSparkles,
  IconSun,
} from '@/components/icons';
import { currentBranchId, type ApiError } from '@/lib/api';
import { listCustomers, type CustomerSummary } from '@/lib/customers';
import {
  listMaster,
  type BrandSummary,
  type CategorySummary,
  type UnitSummary,
  type WarehouseSummary,
} from '@/lib/inventory';
import {
  AccountNature,
  LedgerKind,
  listLedgers,
  type LedgerSummary,
} from '@/lib/ledgers';
import {
  chargeSetupKey,
  mapCharge,
  setupName,
  useChargeSetup,
  withdrawCharge,
  type ChargeDocument,
  type ChargeSetup,
} from '@/lib/charges';
import { useMoney } from '@/lib/money';
import { PAYMENT_MODES } from '@/lib/paymentModes';
import { populateSampleTransactions } from '@/lib/sampleData';
import { statesFor, TaxRegime } from '@/lib/states';
import { listSuppliers, type SupplierSummary } from '@/lib/suppliers';
import { setDefaultAppTheme, useSession } from '@/stores/session';
import { DECIMAL_CHOICES, ratesFor, useSettings } from '@/stores/settings';

/** The figure the decimal places are demonstrated on. Big enough to show a grouping separator. */
const SAMPLE = 1234.5678;

/**
 * A card on the settings screen: a heading and its controls.
 *
 * The hint is accepted and not shown: the firm asked for short labels and no
 * descriptions, so a card's title and its fields' labels say what it is for.
 */
export function SettingsCard({
  title,
  hint: _hint,
  wide = false,
  children,
}: {
  readonly title: string;
  readonly hint?: string;
  /** Spans both columns, for a card whose controls are a table or a grid of their own. */
  readonly wide?: boolean;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <section className={clsx('card card-body space-y-4', wide && 'lg:col-span-2')}>
      <header className="border-b border-line pb-3">
        <h2 className="text-sm font-semibold tracking-tight text-ink">{title}</h2>
      </header>

      <div className="space-y-3">{children}</div>
    </section>
  );
}

/** Two columns of cards on a wide screen, one on a narrow one. */
function Cards({ children }: { readonly children: React.ReactNode }): React.JSX.Element {
  return <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">{children}</div>;
}

// ------------------------------------------------------------------------- general

/** The firm's region and look, its tax, its decimals, its lists and its translation. */
export function GeneralSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();
  const theme = useSession((state) => state.theme);
  const [ratesDraft, setRatesDraft] = useState(settings.taxRates.join(', '));

  // The branch this session is working in, whose decimal places the control below
  // sets. Null where the user holds none, and then the firm-wide value is edited.
  const branchId = currentBranchId();
  const { places } = useMoney();
  const states = statesFor(settings.taxRegime);

  const applyRates = (raw: string): void => {
    const parsed = raw
      .split(/[,\s]+/)
      .map((piece) => Number(piece))
      .filter((rate) => Number.isFinite(rate) && rate >= 0 && rate <= 100);

    // An unparseable list leaves the stored rates alone. Emptying the picker because
    // somebody was midway through typing "1" of "18" would be worse than ignoring the
    // keystroke.
    if (parsed.length > 0) {
      settings.update({ taxRates: parsed });
    }
  };

  const decimalsLabel = (choice: number): string =>
    `${t('prefs.decimalPlaces', { count: choice })} — ${SAMPLE.toLocaleString(undefined, {
      minimumFractionDigits: choice,
      maximumFractionDigits: choice,
    })}`;

  return (
    <Cards>
      <SettingsCard title={t('prefs.regionTitle')} hint={t('prefs.regionHint')}>
        <CheckField
          label={t('prefs.gulfMode')}
          checked={settings.gulfMode}
          onChange={(gulfMode) => settings.update({ gulfMode })}
        />

        <Field label={t('prefs.theme')} hint={t('prefs.themeHint')}>
          <div className="flex flex-wrap gap-2">
            {(['light', 'dark'] as const).map((choice) => (
              <button
                key={choice}
                type="button"
                onClick={() => {
                  settings.update({ defaultTheme: choice });
                  setDefaultAppTheme(choice);
                }}
                aria-pressed={theme === choice}
                className={clsx(
                  'flex items-center gap-2 rounded-lg border px-3 py-1.5 text-sm font-medium transition',
                  theme === choice
                    ? 'border-brand-600 bg-brand-500/10 text-brand-700 ring-1 ring-brand-600 dark:text-brand-300'
                    : 'border-line bg-surface text-ink hover:bg-surface-2',
                )}
              >
                {choice === 'light' ? (
                  <IconSun className="size-4 text-amber-500" />
                ) : (
                  <IconMoon className="size-4 text-indigo-400" />
                )}
                {choice === 'light' ? t('prefs.light') : t('prefs.dark')}
              </button>
            ))}
          </div>
        </Field>
      </SettingsCard>

      <SettingsCard title={t('settings.taxTitle')} hint={t('settings.taxHint')}>
        <SelectField
          label={t('settings.regime')}
          value={settings.taxRegime}
          onChange={(regime) => {
            // The rates and the home state belong to the regime: a firm switched from
            // VAT to GST would otherwise be offering 5% on a GST invoice and holding an
            // emirate in the field that decides IGST.
            const rates = ratesFor(regime);

            settings.update({
              taxRegime: regime,
              taxRates: rates,
              defaultTaxRate: rates.includes(settings.defaultTaxRate)
                ? settings.defaultTaxRate
                : (rates[0] ?? 0),
              firmStateCode: '',
            });

            setRatesDraft(rates.join(', '));
          }}
          options={[
            { value: TaxRegime.gccVat, label: t('settings.regimeVat') },
            { value: TaxRegime.indiaGst, label: t('settings.regimeGst') },
            { value: TaxRegime.none, label: t('settings.regimeNone') },
          ]}
        />

        <Field
          label={t('settings.firmState')}
          hint={t('settings.firmStateHint')}
          className={states.length === 0 ? 'opacity-50' : ''}
        >
          <SearchSelect
            value={settings.firmStateCode}
            onChange={(code) => settings.update({ firmStateCode: code })}
            disabled={states.length === 0}
            clearable
            placeholder={t('settings.noState')}
            label={t('settings.firmState')}
            options={states.map((state) => ({
              value: state.code,
              label: `${state.code} — ${state.name}`,
            }))}
          />
        </Field>

        <TextField
          label={t('settings.taxRates')}
          hint={t('settings.taxRatesHint')}
          value={ratesDraft}
          onChange={(value) => {
            setRatesDraft(value);
            applyRates(value);
          }}
        />

        <SelectField
          label={t('settings.defaultTaxRate')}
          value={settings.defaultTaxRate}
          onChange={(rate) => settings.update({ defaultTaxRate: rate })}
          options={settings.taxRates.map((rate) => ({ value: rate, label: `${rate}%` }))}
        />
      </SettingsCard>

      <SettingsCard
        title={t('settings.decimalsTitle')}
        hint={
          branchId === null
            ? t('settings.decimalsHintFirm')
            : t('settings.decimalsHintBranch')
        }
      >
        <SelectField
          label={t('settings.decimals')}
          hint={t('settings.decimalsFieldHint')}
          value={String(places)}
          onChange={(value) => {
            const chosen = Number(value);

            if (!DECIMAL_CHOICES.includes(chosen)) {
              return;
            }

            // A branch keeps its own answer; a session with no branch sets the firm's.
            if (branchId === null) {
              settings.update({ decimals: chosen });
              return;
            }

            settings.update({
              decimalsByBranch: { ...settings.decimalsByBranch, [branchId]: chosen },
            });
          }}
          options={DECIMAL_CHOICES.map((choice) => ({
            value: String(choice),
            label: decimalsLabel(choice),
          }))}
        />

        {branchId !== null && settings.decimalsByBranch[branchId] !== undefined && (
          <div className="form-actions">
            <button
              type="button"
              className="btn-secondary btn-sm"
              onClick={() => {
                const { [branchId]: _removed, ...rest } = settings.decimalsByBranch;
                settings.update({ decimalsByBranch: rest });
              }}
            >
              {t('settings.decimalsFollowFirm', { count: settings.decimals })}
            </button>
          </div>
        )}

        <SelectField
          label={t('settings.quantityDecimals')}
          hint={t('settings.quantityDecimalsHint')}
          value={String(settings.quantityDecimals ?? 2)}
          onChange={(value) => settings.update({ quantityDecimals: Number(value) })}
          options={DECIMAL_CHOICES.map((choice) => ({
            value: String(choice),
            label: t('prefs.decimalPlaces', { count: choice }),
          }))}
        />
      </SettingsCard>

      <SettingsCard title={t('settings.listsTitle')} hint={t('settings.listsHint')}>
        <NumberField
          label={t('settings.pageSize')}
          hint={t('settings.pageSizeHint')}
          min={5}
          step="5"
          value={String(settings.pageSize)}
          onChange={(value) => {
            const parsed = Number(value);

            if (Number.isFinite(parsed) && parsed >= 5 && parsed <= 500) {
              settings.update({ pageSize: Math.round(parsed) });
            }
          }}
        />

        <div className="form-actions">
          <button
            type="button"
            className="btn-secondary btn-sm"
            onClick={() => {
              settings.reset();
              setRatesDraft(useSettings.getState().taxRates.join(', '));
            }}
          >
            {t('settings.reset')}
          </button>
        </div>
      </SettingsCard>

      <SettingsCard
        title={t('settings.translationTitle')}
        hint={t('settings.translationHint')}
      >
        <CheckField
          label={t('settings.autoTranslate')}
          hint={t('settings.autoTranslateHint')}
          checked={settings.autoTranslateArabic}
          onChange={(checked) => settings.update({ autoTranslateArabic: checked })}
        />

        <TextField
          label={t('settings.apiKey')}
          hint={t('settings.apiKeyHint')}
          value={settings.translationApiKey}
          onChange={(value) => settings.update({ translationApiKey: value })}
          placeholder={t('settings.apiKeyOptional')}
        />
      </SettingsCard>
      <SettingsCard title={t('prefs.approvalsTitle')} hint={t('policies.hint')}>
        <PolicySettings only={['editsRequireApproval']} />
      </SettingsCard>
    </Cards>
  );
}

// ------------------------------------------------------------------------- defaults

/** What a new document or master starts with: who, where, and which accounts. */
export function DefaultSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();

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
  const suppliers = useQuery<readonly SupplierSummary[], ApiError>({
    queryKey: ['suppliers', false],
    queryFn: () => listSuppliers('', false),
  });
  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers', false],
    queryFn: () => listLedgers(false),
  });

  const ofKind = (kind: number): readonly { value: string; label: string }[] =>
    (ledgers.data ?? [])
      .filter((ledger) => ledger.kind === kind)
      .map((ledger) => ({
        value: ledger.ledgerId,
        label: `${ledger.code} — ${ledger.name}`,
      }));

  const allLedgers = (ledgers.data ?? []).map((ledger) => ({
    value: ledger.ledgerId,
    label: `${ledger.code} — ${ledger.name}`,
  }));

  const picker = (
    label: string,
    value: string,
    onChange: (value: string) => void,
    options: readonly { value: string; label: string }[],
    placeholder = t('settings.askEachTime'),
  ): React.JSX.Element => (
    <Field label={label}>
      <SearchSelect
        value={value}
        onChange={onChange}
        clearable
        placeholder={placeholder}
        label={label}
        options={options}
      />
    </Field>
  );

  return (
    <Cards>
      <SettingsCard title={t('prefs.partiesTitle')} hint={t('prefs.partiesHint')}>
        {picker(
          t('settings.defaultCustomer'),
          settings.defaultCustomerId,
          (id) => settings.update({ defaultCustomerId: id }),
          (customers.data ?? []).map((customer) => ({
            value: customer.customerId,
            label: `${customer.code} — ${customer.name}`,
          })),
        )}
        {picker(
          t('settings.defaultSupplier'),
          settings.defaultSupplierId,
          (id) => settings.update({ defaultSupplierId: id }),
          (suppliers.data ?? []).map((supplier) => ({
            value: supplier.supplierId,
            label: `${supplier.code} — ${supplier.name}`,
          })),
        )}
        {picker(
          t('settings.preferredWarehouse'),
          settings.preferredWarehouseId,
          (id) => settings.update({ preferredWarehouseId: id }),
          (warehouses.data ?? [])
            .filter((warehouse) => !warehouse.isTransit)
            .map((warehouse) => ({
              value: warehouse.id,
              label: `${warehouse.code} — ${warehouse.name}`,
            })),
          t('settings.warehouseFromMaster'),
        )}
        {picker(
          t('settings.defaultSalesman'),
          settings.defaultSalesman,
          (id) => settings.update({ defaultSalesman: id }),
          ofKind(LedgerKind.employee),
        )}
        {picker(
          t('settings.defaultBillingman'),
          settings.defaultBillingman,
          (id) => settings.update({ defaultBillingman: id }),
          ofKind(LedgerKind.employee),
        )}
      </SettingsCard>

      <SettingsCard title={t('settings.productsTitle')} hint={t('settings.productsHint')}>
        {picker(
          t('settings.defaultCategory'),
          settings.defaultCategoryId,
          (id) => settings.update({ defaultCategoryId: id }),
          (categories.data ?? []).map((row) => ({
            value: row.id,
            label: `${row.code} — ${row.name}`,
          })),
        )}
        {picker(
          t('settings.defaultStockUnit'),
          settings.defaultStockUnitId,
          (id) => settings.update({ defaultStockUnitId: id }),
          (units.data ?? []).map((row) => ({
            value: row.id,
            label: `${row.code} — ${row.name}`,
          })),
        )}
        {picker(
          t('settings.defaultBrand'),
          settings.defaultBrandId,
          (id) => settings.update({ defaultBrandId: id }),
          (brands.data ?? []).map((row) => ({
            value: row.id,
            label: `${row.code} — ${row.name}`,
          })),
        )}
        <SelectField
          label={t('products.itemType')}
          value={String(settings.defaultItemType ?? 1)}
          onChange={(value) => settings.update({ defaultItemType: Number(value) })}
          options={[
            { value: '1', label: t('products.itemStock') },
            { value: '2', label: t('products.itemService') },
            { value: '3', label: t('products.itemNonStock') },
          ]}
        />
      </SettingsCard>

      <SettingsCard title={t('prefs.accountsTitle')} hint={t('prefs.accountsHint')}>
        {picker(
          t('prefs.defaultCash'),
          settings.defaultCashLedgerId,
          (id) => settings.update({ defaultCashLedgerId: id }),
          ofKind(LedgerKind.cash),
          t('prefs.firstCash'),
        )}
        {picker(
          t('prefs.defaultBank'),
          settings.defaultBankLedgerId,
          (id) => settings.update({ defaultBankLedgerId: id }),
          ofKind(LedgerKind.bank),
          t('prefs.firstBank'),
        )}
        <Field
          label={t('settings.defaultPaymentMode')}
          hint={t('settings.defaultPaymentModeHint')}
        >
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
        {picker(
          t('vouchers.debitAccount'),
          settings.defaultDebitAccountId,
          (id) => settings.update({ defaultDebitAccountId: id }),
          allLedgers,
        )}
        {picker(
          t('vouchers.creditAccount'),
          settings.defaultCreditAccountId,
          (id) => settings.update({ defaultCreditAccountId: id }),
          allLedgers,
        )}
      </SettingsCard>
    </Cards>
  );
}

// ------------------------------------------------------------------------- sales

/** How a sale is entered, and the rule the server holds a sale on credit to. */
export function SalesSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();

  return (
    <Cards>
      <SettingsCard title={t('prefs.salesRulesTitle')} hint={t('policies.hint')}>
        <PolicySettings only={['creditLimit']} />
      </SettingsCard>

      <SettingsCard title={t('prefs.salesEntryTitle')} hint={t('prefs.salesEntryHint')}>
        <SelectField
          label={t('settings.defaultTaxMode')}
          value={settings.defaultTaxMode}
          onChange={(mode) => settings.update({ defaultTaxMode: mode })}
          options={[
            { value: 'NT', label: t('prefs.taxModeNone') },
            { value: 'TAX', label: t('prefs.taxModeVat') },
            { value: 'GST', label: t('prefs.taxModeGst') },
          ]}
        />
        <SelectField
          label={t('settings.defaultSalesRateType')}
          value={settings.defaultSalesRateType}
          onChange={(type) => settings.update({ defaultSalesRateType: type })}
          options={[
            { value: 'retail', label: t('prefs.rateRetail') },
            { value: 'wholesale', label: t('prefs.rateWholesale') },
            { value: 'mrp', label: t('prefs.rateMrp') },
            { value: 'lastQuotation', label: t('prefs.rateLastQuotation') },
            { value: 'customerRate', label: t('prefs.rateCustomer') },
            { value: 'batch', label: t('prefs.rateBatch') },
          ]}
        />
        <CheckField
          label={t('settings.enableReverseCalculation')}
          hint={t('settings.enableReverseCalculationHint')}
          checked={settings.enableReverseCalculation}
          onChange={(checked) => settings.update({ enableReverseCalculation: checked })}
        />
        <CheckField
          label={t('settings.enableFreeQuantity')}
          hint={t('settings.enableFreeQuantityHint')}
          checked={settings.enableFreeQuantity}
          onChange={(checked) => settings.update({ enableFreeQuantity: checked })}
        />
        <CheckField
          label={t('settings.enableItemDiscount')}
          hint={t('settings.enableItemDiscountHint')}
          checked={settings.enableItemDiscount}
          onChange={(checked) => settings.update({ enableItemDiscount: checked })}
        />
      </SettingsCard>
    </Cards>
  );
}

// ------------------------------------------------------------------------- purchase

/** What a purchase does to the product master. */
export function PurchaseSettings(): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <Cards>
      <SettingsCard title={t('prefs.purchaseRulesTitle')} hint={t('policies.hint')}>
        <PolicySettings only={['purchasesUpdateProductRates']} />
      </SettingsCard>
    </Cards>
  );
}

// ------------------------------------------------------------------------- inventory

/** The rules stock moves under, and what the firm tracks. */
export function InventorySettings(): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();

  return (
    <Cards>
      <SettingsCard title={t('prefs.stockRulesTitle')} hint={t('policies.hint')}>
        <PolicySettings only={['negativeStock', 'transfersRequireApproval']} />
      </SettingsCard>

      <SettingsCard title={t('prefs.trackingTitle')} hint={t('prefs.trackingHint')}>
        <CheckField
          label={t('settings.enableAutoBatch')}
          hint={t('settings.enableAutoBatchHint')}
          checked={settings.enableAutoBatch}
          onChange={(checked) => settings.update({ enableAutoBatch: checked })}
        />
        <CheckField
          label={t('settings.enableSerialNumbers')}
          hint={t('settings.enableSerialNumbersHint')}
          checked={settings.enableSerialNumbers}
          onChange={(checked) => settings.update({ enableSerialNumbers: checked })}
        />
        <CheckField
          label={t('settings.enableDeviceAttributes')}
          hint={t('settings.enableDeviceAttributesHint')}
          checked={settings.enableDeviceAttributes}
          onChange={(checked) => settings.update({ enableDeviceAttributes: checked })}
        />
      </SettingsCard>
    </Cards>
  );
}

// ------------------------------------------------------------------------- accounts

/** The periods the books are kept in. */
export function AccountsSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();

  return (
    <Cards>
      <SettingsCard
        title={t('prefs.billWiseReceiptsTitle')}
        hint={t('prefs.billWiseHint')}
      >
        <CheckField
          label={t('prefs.billWise')}
          hint={t('prefs.billWiseReceiptsHint')}
          checked={settings.billWiseReceipts}
          onChange={(billWiseReceipts) => settings.update({ billWiseReceipts })}
        />
        <CheckField
          label={t('prefs.fillBills')}
          hint={t('prefs.fillBillsHint')}
          checked={settings.fillBillsOnReceipts}
          disabled={!settings.billWiseReceipts}
          onChange={(fillBillsOnReceipts) => settings.update({ fillBillsOnReceipts })}
        />
      </SettingsCard>

      <SettingsCard
        title={t('prefs.billWisePaymentsTitle')}
        hint={t('prefs.billWiseHint')}
      >
        <CheckField
          label={t('prefs.billWise')}
          hint={t('prefs.billWisePaymentsHint')}
          checked={settings.billWisePayments}
          onChange={(billWisePayments) => settings.update({ billWisePayments })}
        />
        <CheckField
          label={t('prefs.fillBills')}
          hint={t('prefs.fillBillsHint')}
          checked={settings.fillBillsOnPayments}
          disabled={!settings.billWisePayments}
          onChange={(fillBillsOnPayments) => settings.update({ fillBillsOnPayments })}
        />
      </SettingsCard>

      <SettingsCard title={t('years.title')} hint={t('years.hint')} wide>
        <FinancialYearsSettings />
      </SettingsCard>
    </Cards>
  );
}

// ------------------------------------------------------------------------- additional ledgers

/** The setups additional ledgers are kept in; a return shares its sale's or purchase's. */
const CHARGE_TABS: readonly ChargeDocument[] = [
  'sales',
  'salesOrder',
  'purchase',
  'purchaseOrder',
];

/**
 * The additional ledgers each kind of transaction carries: freight, packing, a standing
 * discount. Set up here once, with which way each moves the total and what it starts
 * at; every new document of that kind then shows them, waiting for an amount.
 */
export function AdditionalLedgerSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();
  const can = useSession((state) => state.can);
  const queryClient = useQueryClient();
  const [document, setDocument] = useState<ChargeDocument>('sales');
  const [ledgerId, setLedgerId] = useState('');
  const [adds, setAdds] = useState(true);

  const canEdit = can('accounting:additional-ledger:create');
  const setup = useChargeSetup(document);
  const type = setupName(document);

  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => listLedgers(true),
    staleTime: 5 * 60 * 1000,
  });

  // Charge ledgers, and income and expense ledgers, not already on this setup: a
  // party, cash, bank or tax account has its own place on a document already.
  const offered = useMemo(() => {
    const already = new Set((setup ?? []).map((charge) => charge.ledgerId));

    return (ledgers.data ?? [])
      .filter(
        (ledger) =>
          (ledger.kind === LedgerKind.additionalCharge ||
            (ledger.kind === LedgerKind.general &&
              (ledger.nature === AccountNature.income ||
                ledger.nature === AccountNature.expense))) &&
          !already.has(ledger.ledgerId),
      )
      .map((ledger) => ({
        value: ledger.ledgerId,
        label: `${ledger.code} — ${ledger.name}`,
        detail: ledger.groupName,
      }));
  }, [ledgers.data, setup]);

  const all = settings.defaultAdditionalLedgers ?? [];
  const defaultOf = (id: string): string =>
    all.find((row) => row.transactionType === type && row.ledgerId === id)
      ?.defaultAmount ?? '';

  const setDefault = (charge: ChargeSetup, amount: string): void => {
    const exists = all.some(
      (row) => row.transactionType === type && row.ledgerId === charge.ledgerId,
    );

    settings.update({
      defaultAdditionalLedgers: exists
        ? all.map((row) =>
            row.transactionType === type && row.ledgerId === charge.ledgerId
              ? { ...row, defaultAmount: amount, isDefaultActive: true }
              : row,
          )
        : [
            ...all,
            {
              id: crypto.randomUUID(),
              transactionType: type,
              ledgerId: charge.ledgerId,
              isAddition: charge.isAddition,
              defaultAmount: amount,
              isDefaultActive: true,
            },
          ],
    });
  };

  const refresh = (): Promise<void> =>
    queryClient.invalidateQueries({ queryKey: chargeSetupKey(document) });

  const add = useMutation<ChargeSetup, ApiError>({
    mutationFn: () => mapCharge(document, ledgerId, adds),
    onSuccess: async () => {
      setLedgerId('');
      setAdds(true);
      await refresh();
    },
  });

  const remove = useMutation<void, ApiError, string>({
    mutationFn: (id) => withdrawCharge(document, id),
    onSuccess: async (_, id) => {
      settings.update({
        defaultAdditionalLedgers: all.filter(
          (row) => !(row.transactionType === type && row.ledgerId === id),
        ),
      });
      await refresh();
    },
  });

  const failure = add.error ?? remove.error;

  return (
    <Cards>
      <SettingsCard title={t('settings.chargesTitle')} wide>
        <div className="flex flex-wrap gap-1 rounded-lg border border-line bg-surface-2 p-1">
          {CHARGE_TABS.map((tab) => (
            <button
              key={tab}
              type="button"
              onClick={() => setDocument(tab)}
              aria-pressed={document === tab}
              className={clsx(
                'rounded-md px-2.5 py-1 text-xs font-medium transition',
                document === tab
                  ? 'bg-surface text-ink shadow-sm ring-1 ring-line'
                  : 'text-ink-muted hover:bg-surface/50 hover:text-ink',
              )}
            >
              {t(`settings.chargeTabs.${tab}`)}
            </button>
          ))}
        </div>

        {failure && <p className="alert-error">{failure.detail || failure.code}</p>}

        {setup === undefined ? (
          <p className="text-xs text-ink-muted">{t('common.loading')}</p>
        ) : setup.length === 0 ? (
          <p className="rounded-lg border border-dashed border-line p-4 text-center text-xs text-ink-muted">
            {t('documents.noChargesSetUp')}
          </p>
        ) : (
          <div className="table-wrap">
            <table className="table text-sm">
              <thead>
                <tr>
                  <th className="text-start">{t('documents.chargeAccount')}</th>
                  <th className="text-start">{t('settings.chargeEffect')}</th>
                  <th className="text-end">{t('settings.chargeDefault')}</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {setup.map((charge) => (
                  <tr key={charge.ledgerId}>
                    <td className="py-1.5">
                      {charge.code} — {charge.name}
                    </td>
                    <td
                      className={clsx(
                        'py-1.5 text-xs font-medium',
                        charge.isAddition
                          ? 'text-emerald-700 dark:text-emerald-400'
                          : 'text-red-600 dark:text-red-400',
                      )}
                    >
                      {charge.isAddition
                        ? t('documents.chargeAdds')
                        : t('documents.chargeDeducts')}
                    </td>
                    <td className="py-1.5 text-end">
                      {!charge.isComputed && (
                        <input
                          type="number"
                          inputMode="decimal"
                          min="0"
                          step="any"
                          aria-label={t('settings.chargeDefault')}
                          placeholder="0.00"
                          value={defaultOf(charge.ledgerId)}
                          onChange={(event) => setDefault(charge, event.target.value)}
                          className="field-input-sm w-28 text-end font-mono tabular-nums"
                        />
                      )}
                    </td>
                    <td className="py-1.5 text-end">
                      {canEdit && !charge.isComputed && (
                        <button
                          type="button"
                          disabled={remove.isPending}
                          onClick={() => remove.mutate(charge.ledgerId)}
                          aria-label={t('documents.removeCharge')}
                          title={t('documents.removeCharge')}
                          className="rounded p-1 text-ink-muted transition hover:text-red-600"
                        >
                          <IconClose className="size-4" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {canEdit && (
          <div className="flex flex-wrap items-end gap-2 border-t border-line pt-3">
            <div className="min-w-56 flex-1">
              <SearchSelect
                value={ledgerId}
                onChange={setLedgerId}
                options={offered}
                size="sm"
                label={t('documents.chargeAccount')}
                placeholder={t('documents.chooseCharge')}
              />
            </div>
            <select
              value={adds ? 'add' : 'deduct'}
              onChange={(event) => setAdds(event.target.value === 'add')}
              aria-label={t('settings.chargeEffect')}
              className="field-input-sm w-auto"
            >
              <option value="add">{t('documents.chargeAdds')}</option>
              <option value="deduct">{t('documents.chargeDeducts')}</option>
            </select>
            <button
              type="button"
              disabled={ledgerId === '' || add.isPending}
              onClick={() => add.mutate()}
              className="btn-secondary btn-sm flex items-center gap-1"
            >
              <IconPlus className="size-3.5" />
              {t('documents.addCharge')}
            </button>
          </div>
        )}
      </SettingsCard>
    </Cards>
  );
}

// ------------------------------------------------------------------------- printing

/** Where printing is set up: its own screens, per document and tax mode. */
export function PrintingSettings(): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <Cards>
      <SettingsCard title={t('printing.settingsTitle')} hint={t('printing.movedHint')}>
        <div className="flex flex-wrap gap-2">
          <Link to="/settings/printing" className="btn-secondary btn-sm">
            {t('printing.settingsTitle')}
          </Link>
          <Link to="/settings/print-templates" className="btn-secondary btn-sm">
            {t('printing.templatesTitle')}
          </Link>
        </div>
      </SettingsCard>
    </Cards>
  );
}

// ------------------------------------------------------------------------- numbering

/** The firm's document numbering series. */
export function NumberingTab(): React.JSX.Element {
  return (
    <div className="card card-body">
      <NumberingSettings />
    </div>
  );
}

// ------------------------------------------------------------------------- data

/** Bringing data in, clearing it out, and demonstration data to try the system on. */
export function DataSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [seeding, setSeeding] = useState(false);
  const [result, setResult] = useState<string | null>(null);
  const [problems, setProblems] = useState<readonly string[]>([]);

  const seed = async (): Promise<void> => {
    try {
      setSeeding(true);
      setResult(null);
      setProblems([]);
      const made = await populateSampleTransactions();
      await queryClient.invalidateQueries();
      setResult(
        t('prefs.sampleDone', {
          sales: made.salesInvoicesCreated,
          purchases: made.purchaseInvoicesCreated,
          orders: made.ordersCreated,
          vouchers: made.vouchersCreated,
        }),
      );
      setProblems(made.problems);
    } catch {
      setResult(t('prefs.sampleFailed'));
    } finally {
      setSeeding(false);
    }
  };

  return (
    <Cards>
      <SettingsCard title={t('prefs.dataTitle')} hint={t('prefs.dataHint')}>
        <div className="flex flex-wrap gap-2">
          <Link to="/settings/import" className="btn-secondary btn-sm">
            {t('prefs.importData')}
          </Link>
          <Link to="/settings/data" className="btn-secondary btn-sm">
            {t('prefs.clearData')}
          </Link>
        </div>
      </SettingsCard>

      <SettingsCard title={t('prefs.sampleTitle')} hint={t('prefs.sampleHint')}>
        <button
          type="button"
          disabled={seeding}
          onClick={() => void seed()}
          className="btn-primary btn-sm flex items-center gap-1.5"
        >
          <IconSparkles className={clsx('size-3.5', seeding && 'animate-spin')} />
          {seeding ? t('prefs.sampleRunning') : t('prefs.sampleRun')}
        </button>

        {result && (
          <p className="flex items-center gap-2 rounded-lg border border-emerald-500/30 bg-emerald-500/10 px-3 py-2 text-xs text-emerald-700 dark:text-emerald-300">
            <IconCheck className="size-4 shrink-0" />
            {result}
          </p>
        )}

        {problems.length > 0 && (
          <ul className="alert-warn list-disc space-y-0.5 ps-6 text-xs">
            {problems.map((problem) => (
              <li key={problem}>{problem}</li>
            ))}
          </ul>
        )}
      </SettingsCard>
    </Cards>
  );
}
