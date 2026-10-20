import { create } from 'zustand';
import { ApiError, request } from '@/lib/api';
import { TaxRegime } from '@/lib/states';
import { NO_DEFAULT_CHARGES, type DefaultCharge } from '@/lib/charges';

/**
 * The preferences that decide how the screens behave, rather than what the server
 * holds.
 *
 * Kept in the browser, per installation, and deliberately so: every one of these is
 * a question about how this workstation should behave — whether to call a
 * translation service, which tax rates to offer on a rate picker, how many rows a
 * page holds — and none of them changes what any document means. The firm's regime
 * and its own state are the exception in kind: they belong on the firm record and
 * the API does not expose it yet, so they are set here and the screens that need
 * them read them from here. When the firm endpoint arrives this store reads from it
 * instead and nothing else moves.
 */
export interface AppSettings {
  /**
   * Whether an Arabic name is filled in automatically from the English one.
   *
   * Off by default. It sends the text somebody typed to a translation service, and
   * that is a decision a firm makes rather than one made for them.
   */
  readonly autoTranslateArabic: boolean;
  /**
   * A Google Cloud Translation API key.
   *
   * Blank uses the public endpoint, which needs no key and is rate-limited by
   * Google as it sees fit. A firm translating a catalogue supplies its own.
   */
  readonly translationApiKey: string;
  /** The regime the firm charges under, which decides the state table and the heads. */
  readonly taxRegime: number;
  /** The firm's own state, which decides intra-state against inter-state. */
  readonly firmStateCode: string;
  /** The tax rates a rate picker offers, in the order it offers them. */
  readonly taxRates: readonly number[];
  /** The rate a new document line starts at. */
  readonly defaultTaxRate: number;
  /**
   * The warehouse a new document starts at, when the master has no default of its
   * own or the user prefers another.
   */
  readonly preferredWarehouseId: string;
  /**
   * The category and the unit of measure a new product starts at.
   *
   * A firm that sells one kind of thing enters the same category on every product
   * it ever adds, and the same unit on most of them. Both stay mandatory — the
   * server needs them and the form still marks them — but a mandatory field that
   * already holds the right answer is one fewer dropdown between somebody and the
   * product they came to add. Blank means "ask", which is what a firm with a real
   * catalogue wants.
   */
  readonly defaultCategoryId: string;
  readonly defaultStockUnitId: string;
  /**
   * The payment mode a new receipt or payment starts at.
   *
   * A counter that takes cash all day was retyping "Cash" onto every voucher. Blank
   * means "ask", which is what a firm taking money four different ways wants.
   */
  readonly defaultPaymentMode: string;
  /**
   * The charge heads a new document starts with, keyed by document kind.
   *
   * Freight on every purchase, delivery on every invoice, a standing discount — the
   * heads a firm puts on the same documents every day and was adding by hand each
   * time, with the row forgotten often enough to matter. Held here rather than on
   * the server because the server has no endpoint for it: the charge matrix it does
   * hold says which way a head moves a total, not which heads a document starts
   * with.
   */
  readonly defaultCharges: Readonly<Record<string, readonly DefaultCharge[]>>;
  /** How many rows a paged list holds. */
  readonly pageSize: number;
  /**
   * How many decimal places money is shown to, when a branch says nothing else.
   *
   * Two for most of the world, three where a currency subdivides by a thousand —
   * the dinars of Bahrain, Kuwait, Jordan and Iraq, and the Omani rial — and zero
   * where it does not subdivide at all, which is how a yen or a franc price list
   * reads. It is a presentation decision throughout: the server keeps and posts the
   * full precision it was given, and this decides what a screen prints.
   */
  readonly decimals: number;
  /**
   * The places a particular branch shows, keyed by branch identifier.
   *
   * Branches keep their own numbering and print formats, and a firm running a
   * wholesale branch beside a retail counter routinely wants different precision on
   * each: three places where goods are priced by the thousand, two at the till. A
   * branch with no entry here follows `decimals`.
   */
  readonly decimalsByBranch: Readonly<Record<string, number>>;
  /** Number of decimal places for quantity values throughout the system (e.g. 2 or 3). */
  readonly quantityDecimals: number;
  /** Whether device attributes (IMEI, Serial Number, Warranty, Model) are enabled. */
  readonly enableDeviceAttributes: boolean;
  /** Default sales rate type: retail, wholesale, or maximum retail price (MRP). */
  readonly defaultSalesRateType: 'retail' | 'wholesale' | 'mrp';
  /** Default tax mode: NT (Non-Tax), TAX (VAT), or GST. */
  readonly defaultTaxMode: 'NT' | 'TAX' | 'GST';
  /** Default customer for sales transactions. */
  readonly defaultCustomerId: string;
  /** Default supplier for purchase transactions. */
  readonly defaultSupplierId: string;
  /** Default salesman identifier. */
  readonly defaultSalesman: string;
  /** Default billingman identifier. */
  readonly defaultBillingman: string;
  /** Whether reverse calculation is enabled on transaction entry. */
  readonly enableReverseCalculation: boolean;
  /** Whether free quantity column is enabled in sales entry. */
  readonly enableFreeQuantity: boolean;
  /** Whether per-item discount column is enabled in sales entry. */
  readonly enableItemDiscount: boolean;
  /** Whether automatic FIFO batch assignment is enabled. */
  readonly enableAutoBatch: boolean;
  /**
   * Whether serial numbers (IMEI) are offered on documents and products at all.
   *
   * Off by default: a firm that does not track handsets by number should never be
   * asked for one. On, the numbers are offered where a product is tracked by serial
   * number and are always optional.
   */
  readonly enableSerialNumbers: boolean;
  /** Default item type (1: Stocked, 2: Service, 3: Non-Stock). */
  readonly defaultItemType: number;
  /** Default brand identifier. */
  readonly defaultBrandId: string;
  /** Default debit account for payment vouchers. */
  readonly defaultDebitAccountId: string;
  /** Default credit account for receipt vouchers. */
  readonly defaultCreditAccountId: string;
  /**
   * The cash account a sale paid in cash is received into. Blank takes the firm's first
   * cash account, which is the only one most shops have.
   */
  readonly defaultCashLedgerId: string;
  /**
   * The bank account an online payment - Google Pay, a card - is received into, and the
   * bank a bank receipt or payment starts at. Blank takes the firm's first bank account.
   */
  readonly defaultBankLedgerId: string;
  /** Whether a receipt shows a bill-wise customer's pending bills. */
  readonly billWiseReceipts: boolean;
  /** Whether a receipt lets those bills be ticked and allocated, filling its amount. */
  readonly fillBillsOnReceipts: boolean;
  /** Whether a payment shows a bill-wise supplier's pending bills. */
  readonly billWisePayments: boolean;
  /** Whether a payment lets those bills be ticked and allocated, filling its amount. */
  readonly fillBillsOnPayments: boolean;
  /** Configured default additional ledgers per transaction type. */
  readonly defaultAdditionalLedgers: readonly DefaultAdditionalLedgerConfig[];
  /** Application-wide default theme ('light' or 'dark') applied for all users. */
  readonly defaultTheme: 'light' | 'dark';
  /**
   * Whether the firm trades in the Gulf: Arabic names beside the English ones on the
   * masters, and documents that can be printed in both languages. Off, the Arabic
   * fields stay out of the way unless one already holds something.
   */
  readonly gulfMode: boolean;
}

export interface DefaultAdditionalLedgerConfig {
  readonly id: string;
  readonly transactionType: string;
  readonly ledgerId: string;
  readonly isAddition: boolean;
  readonly defaultAmount: string;
  readonly isDefaultActive: boolean;
}

/** The places a firm can choose between. Nothing sensible lies outside it. */
export const DECIMAL_CHOICES: readonly number[] = [0, 1, 2, 3, 4];

/** The rates a GST firm charges, and the rates a VAT firm does. */
export const GST_RATES: readonly number[] = [0, 0.25, 3, 5, 12, 18, 28];

/** The Gulf's VAT rates. Two, and one of them is zero. */
export const VAT_RATES: readonly number[] = [0, 5];

const DEFAULTS: AppSettings = {
  autoTranslateArabic: false,
  translationApiKey: '',
  taxRegime: TaxRegime.gccVat,
  firmStateCode: '',
  taxRates: VAT_RATES,
  defaultTaxRate: 5,
  preferredWarehouseId: '',
  defaultCategoryId: '',
  defaultStockUnitId: '',
  defaultPaymentMode: '',
  defaultCharges: NO_DEFAULT_CHARGES,
  pageSize: 25,
  decimals: 2,
  decimalsByBranch: {},
  quantityDecimals: 2,
  enableDeviceAttributes: true,
  defaultSalesRateType: 'retail',
  defaultTaxMode: 'TAX',
  defaultCustomerId: '',
  defaultSupplierId: '',
  defaultSalesman: '',
  defaultBillingman: '',
  enableReverseCalculation: true,
  enableFreeQuantity: true,
  enableItemDiscount: true,
  enableAutoBatch: true,
  enableSerialNumbers: false,
  defaultItemType: 1,
  defaultBrandId: '',
  defaultDebitAccountId: '',
  defaultCreditAccountId: '',
  defaultCashLedgerId: '',
  defaultBankLedgerId: '',
  billWiseReceipts: true,
  fillBillsOnReceipts: true,
  billWisePayments: true,
  fillBillsOnPayments: true,
  defaultAdditionalLedgers: [],
  defaultTheme: 'light',
  gulfMode: true,
};

/**
 * Brings settings written by an earlier release up to date.
 *
 * The standing charges each kind of document starts with were once kept in a second,
 * simpler list; they now live with the additional-ledger defaults, and an older copy's
 * list is folded in rather than left applying where nothing can show or change it.
 */
function upgrade(settings: AppSettings): AppSettings {
  const legacy = Object.entries(settings.defaultCharges ?? {}).filter(
    ([, rows]) => rows.length > 0,
  );

  if (legacy.length === 0) {
    return settings;
  }

  const kinds: Record<string, string> = {
    sales: 'Sales',
    salesReturn: 'SalesReturn',
    salesOrder: 'SalesOrder',
    purchase: 'Purchase',
    purchaseReturn: 'PurchaseReturn',
    purchaseOrder: 'PurchaseOrder',
  };

  const folded: DefaultAdditionalLedgerConfig[] = legacy.flatMap(([kind, rows]) =>
    rows
      .filter((row) => row.ledgerId)
      .map((row, index) => ({
        id: `legacy-${kind}-${index}`,
        transactionType: kinds[kind] ?? kind,
        ledgerId: row.ledgerId,
        isAddition: true,
        defaultAmount: row.amount,
        isDefaultActive: true,
      })),
  );

  return {
    ...settings,
    defaultCharges: NO_DEFAULT_CHARGES,
    defaultAdditionalLedgers: [...(settings.defaultAdditionalLedgers ?? []), ...folded],
  };
}

const STORAGE_KEY = 'erp.settings';

function read(): AppSettings {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);

    if (!stored) {
      return DEFAULTS;
    }

    // Merged over the defaults rather than trusted whole: a stored blob written by
    // an older release is missing whatever has been added since, and spreading it
    // over the defaults is what stops a new setting arriving as `undefined`.
    return upgrade({ ...DEFAULTS, ...(JSON.parse(stored) as Partial<AppSettings>) });
  } catch {
    return DEFAULTS;
  }
}

/**
 * Settings that stay with this browser instead of following the firm.
 *
 * How many rows a list shows is a question about the screen in front of somebody,
 * and a laptop and a wall-mounted counter display want different answers.
 */
const DEVICE_ONLY: readonly (keyof AppSettings)[] = ['pageSize'];

/** The name the firm's copy is stored under on the server. */
const SERVER_KEY = 'preferences';

/** Where the firm's copy stands, for the screen that edits it. */
export type SettingsSync =
  /** Not yet read from the server: what is on screen is this browser's copy. */
  | 'local'
  /** Read from, and kept in step with, the firm's copy. */
  | 'firm'
  /** The firm's copy could be read but this user may not change it. */
  | 'read-only';

interface SettingsState extends AppSettings {
  /** Where these settings came from, and whether changes reach the firm. */
  readonly sync: SettingsSync;
  /** Changes some of the settings and writes them back. */
  update: (patch: Partial<AppSettings>) => void;
  /** Puts everything back to what a fresh installation starts with. */
  reset: () => void;
  /**
   * Replaces this browser's copy with the firm's.
   *
   * Called once somebody is signed in. A firm that has never saved its settings is
   * seeded from the first workstation that has, so the counter that was set up
   * before this change carries its setup to every other one.
   */
  hydrate: () => Promise<void>;
}

/** The part of the settings the firm shares. */
function shared(settings: AppSettings): Partial<AppSettings> {
  const copy: Record<string, unknown> = { ...settings };

  for (const key of DEVICE_ONLY) {
    delete copy[key];
  }

  return copy as Partial<AppSettings>;
}

/** Only the settings themselves, without the store's own fields and actions. */
function settingsOf(state: SettingsState): AppSettings {
  const copy: Record<string, unknown> = {};

  for (const key of Object.keys(DEFAULTS) as (keyof AppSettings)[]) {
    copy[key] = state[key];
  }

  return copy as unknown as AppSettings;
}

function store(settings: AppSettings): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
  } catch {
    // A browser with storage disabled still gets the setting for this session.
    // Losing it on reload is a smaller failure than refusing to apply it.
  }
}

let pending: number | undefined;

/**
 * Writes the firm's copy, a moment after the last change.
 *
 * Debounced so a field typed into a character at a time is one request, not one per
 * keystroke. A refusal is remembered rather than retried: a user without the right
 * to change the firm's settings keeps their changes in this browser, and the screen
 * says so.
 */
function saveToFirm(settings: AppSettings, onRefused: () => void): void {
  window.clearTimeout(pending);
  pending = window.setTimeout(() => {
    void request<void>(`/settings/${SERVER_KEY}`, {
      method: 'PUT',
      body: { value: JSON.stringify(shared(settings)) },
    }).catch((failure: unknown) => {
      if (failure instanceof ApiError && failure.status === 403) {
        onRefused();
      }
    });
  }, 600);
}

export const useSettings = create<SettingsState>((set, get) => ({
  ...read(),
  sync: 'local',

  update: (patch) => {
    const next: AppSettings = {
      ...settingsOf(get()),
      ...patch,
    };

    set(next);
    store(next);

    if (get().sync === 'firm') {
      saveToFirm(next, () => set({ sync: 'read-only' }));
    }
  },

  reset: () => {
    set(DEFAULTS);

    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      // As above.
    }

    if (get().sync === 'firm') {
      saveToFirm(DEFAULTS, () => set({ sync: 'read-only' }));
    }
  },

  hydrate: async () => {
    let held: string | null;

    try {
      held = (await request<{ readonly value: string | null }>(`/settings/${SERVER_KEY}`))
        .value;
    } catch {
      // No firm selected, or the server is unreachable: carry on with this browser's
      // copy, which is what every screen used before there was a firm copy at all.
      return;
    }

    const local = settingsOf(get());

    if (held === null) {
      // Nobody has saved the firm's settings yet. If this browser was set up, its
      // setup becomes the firm's; if the user may not do that, it stays local.
      set({ sync: 'firm' });

      if (JSON.stringify(shared(local)) !== JSON.stringify(shared(DEFAULTS))) {
        saveToFirm(local, () => set({ sync: 'read-only' }));
      }

      return;
    }

    let firm: Partial<AppSettings>;

    try {
      firm = JSON.parse(held) as Partial<AppSettings>;
    } catch {
      return;
    }

    const device: Partial<AppSettings> = {};

    for (const key of DEVICE_ONLY) {
      (device as Record<string, unknown>)[key] = local[key];
    }

    // Over the defaults, as `read` does, so a setting added since the firm last saved
    // arrives at its default rather than as `undefined`.
    const next: AppSettings = upgrade({ ...DEFAULTS, ...firm, ...device });

    set({ ...next, sync: 'firm' });
    store(next);
  },
}));

/** The rates that suit a regime, for the screen that resets them. */
export function ratesFor(regime: number): readonly number[] {
  return regime === TaxRegime.indiaGst ? GST_RATES : VAT_RATES;
}
