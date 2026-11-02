import { useSyncExternalStore } from 'react';
import { currentFirmId, currentUserId, getStoredTenantCode } from '@/lib/api';

/**
 * Bills put on hold: a sale set aside part-way through - the customer has gone back for
 * one more thing - while the counter serves the next one, and taken up again later.
 *
 * Kept in this browser, for the user who held them, in the firm they were held in, and
 * for the day they were held: a held bill is the counter's own scrap paper, not a
 * document - it has no number until it is saved, moves nothing, and is thrown away at
 * the end of the day if nobody came back for it. A bill that should outlive the day is
 * saved as a draft.
 */

/** One bill on hold: what the list shows of it, and everything needed to open it again. */
export interface HeldSale<TDraft = unknown> {
  readonly id: string;
  /** When it was put on hold. */
  readonly heldAt: string;
  /** A sale or a return, as the server numbers the kinds. */
  readonly kind: number;
  /** Who it is for, as the list names them: the billing name, or the customer's. */
  readonly party: string;
  readonly items: number;
  readonly total: number;
  /** The bill as it stood, for the entry screen to open again. */
  readonly draft: TDraft;
}

/** What is held, minus what holding it adds. */
export type HoldingSale<TDraft = unknown> = Omit<HeldSale<TDraft>, 'id' | 'heldAt'> & {
  /** Held again under the same name: a bill opened from the list and put back. */
  readonly id?: string;
};

const PREFIX = 'erp.held-sales';

/** Said on the window whenever the list changes, so every screen showing it redraws. */
const CHANGED = 'erp:held-sales-changed';

/** Whose list this is: the tenant, the firm and the user signed in. */
function storageKey(): string {
  return [
    PREFIX,
    getStoredTenantCode() ?? '',
    currentFirmId() ?? '',
    currentUserId() ?? '',
  ]
    .join('.')
    .toLowerCase();
}

/** A day as the counter's own clock has it: yyyy-mm-dd. */
function dayOf(at: Date): string {
  const month = String(at.getMonth() + 1).padStart(2, '0');
  const day = String(at.getDate()).padStart(2, '0');

  return `${at.getFullYear()}-${month}-${day}`;
}

function isHeldSale(value: unknown): value is HeldSale {
  if (typeof value !== 'object' || value === null) return false;
  const entry = value as Record<string, unknown>;

  return (
    typeof entry['id'] === 'string' &&
    typeof entry['heldAt'] === 'string' &&
    typeof entry['kind'] === 'number' &&
    'draft' in entry
  );
}

/** Everything in storage that parses, whatever day it is from. */
function parse(raw: string | null): readonly HeldSale[] {
  try {
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed.filter(isHeldSale) : [];
  } catch {
    return [];
  }
}

function readRaw(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

/** Today's held bills, newest first. */
function todays(entries: readonly HeldSale[]): readonly HeldSale[] {
  const today = dayOf(new Date());

  return entries
    .filter((entry) => dayOf(new Date(entry.heldAt)) === today)
    .sort((a, b) => b.heldAt.localeCompare(a.heldAt));
}

function write(entries: readonly HeldSale[]): void {
  try {
    localStorage.setItem(storageKey(), JSON.stringify(entries));
  } catch {
    // Storage switched off or full: the bill could not be held, and the window that
    // asked has already said it was. Nothing better can be done from here.
  }

  window.dispatchEvent(new Event(CHANGED));
}

/**
 * Puts a bill on hold, and drops whatever earlier days left behind.
 *
 * @returns The bill as held.
 */
export function holdSale<TDraft>(sale: HoldingSale<TDraft>): HeldSale<TDraft> {
  const { id, ...rest } = sale;
  const held: HeldSale<TDraft> = {
    ...rest,
    id: id ?? crypto.randomUUID(),
    heldAt: new Date().toISOString(),
  };
  const kept = todays(parse(readRaw(storageKey()))).filter(
    (entry) => entry.id !== held.id,
  );

  write([held, ...kept]);

  return held;
}

/** Takes a bill off hold to open it again: it is no longer on the list. */
export function takeHeldSale<TDraft>(id: string): HeldSale<TDraft> | undefined {
  const all = parse(readRaw(storageKey()));
  const found = all.find((entry) => entry.id === id);

  if (found) write(all.filter((entry) => entry.id !== id));

  return found as HeldSale<TDraft> | undefined;
}

/** Throws a held bill away. */
export function discardHeldSale(id: string): void {
  write(parse(readRaw(storageKey())).filter((entry) => entry.id !== id));
}

/** The snapshot last handed out, so an unchanged list is the same list to React. */
let cached: {
  readonly key: string;
  readonly raw: string | null;
  readonly day: string;
  readonly entries: readonly HeldSale[];
} | null = null;

function snapshot(): readonly HeldSale[] {
  const key = storageKey();
  const raw = readRaw(key);
  const day = dayOf(new Date());

  if (cached?.key !== key || cached.raw !== raw || cached.day !== day) {
    cached = { key, raw, day, entries: todays(parse(raw)) };
  }

  return cached.entries;
}

function subscribe(changed: () => void): () => void {
  window.addEventListener(CHANGED, changed);
  // Another tab of the same counter holding or taking up a bill.
  window.addEventListener('storage', changed);

  return () => {
    window.removeEventListener(CHANGED, changed);
    window.removeEventListener('storage', changed);
  };
}

/** Today's held bills of one kind - sales or returns - newest first, kept up to date. */
export function useHeldSales<TDraft = unknown>(
  kind: number,
): readonly HeldSale<TDraft>[] {
  const all = useSyncExternalStore(subscribe, snapshot, snapshot);

  return all.filter((entry) => entry.kind === kind) as readonly HeldSale<TDraft>[];
}
