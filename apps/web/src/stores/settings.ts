import { create } from 'zustand';
import { TaxRegime } from '@/lib/states';

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
  /** How many rows a paged list holds. */
  readonly pageSize: number;
}

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
  pageSize: 25,
};

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
    return { ...DEFAULTS, ...(JSON.parse(stored) as Partial<AppSettings>) };
  } catch {
    return DEFAULTS;
  }
}

interface SettingsState extends AppSettings {
  /** Changes some of the settings and writes them back. */
  update: (patch: Partial<AppSettings>) => void;
  /** Puts everything back to what a fresh installation starts with. */
  reset: () => void;
}

export const useSettings = create<SettingsState>((set, get) => ({
  ...read(),

  update: (patch) => {
    const next: AppSettings = {
      autoTranslateArabic: patch.autoTranslateArabic ?? get().autoTranslateArabic,
      translationApiKey: patch.translationApiKey ?? get().translationApiKey,
      taxRegime: patch.taxRegime ?? get().taxRegime,
      firmStateCode: patch.firmStateCode ?? get().firmStateCode,
      taxRates: patch.taxRates ?? get().taxRates,
      defaultTaxRate: patch.defaultTaxRate ?? get().defaultTaxRate,
      preferredWarehouseId: patch.preferredWarehouseId ?? get().preferredWarehouseId,
      pageSize: patch.pageSize ?? get().pageSize,
    };

    set(next);

    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    } catch {
      // A browser with storage disabled still gets the setting for this session.
      // Losing it on reload is a smaller failure than refusing to apply it.
    }
  },

  reset: () => {
    set(DEFAULTS);

    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      // As above.
    }
  },
}));

/** The rates that suit a regime, for the screen that resets them. */
export function ratesFor(regime: number): readonly number[] {
  return regime === TaxRegime.indiaGst ? GST_RATES : VAT_RATES;
}
