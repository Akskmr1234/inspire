import { createContext, useContext, useEffect, useRef } from 'react';

/**
 * What a tab has typed but not yet added - a barcode in its box, opening stock rows
 * filled in - saved by the editor's one Save along with everything else.
 *
 * A flush does nothing when nothing is pending, and throws (with a message a person
 * can act on) when what is pending cannot be saved.
 */
export type PendingFlush = () => Promise<void>;

type Register = (key: string, flush: PendingFlush | null) => void;

export const PendingSavesContext = createContext<Register | null>(null);

/** Hands the editor this tab's pending work; the latest `flush` is always the one run. */
export function usePendingSave(key: string, flush: PendingFlush): void {
  const register = useContext(PendingSavesContext);
  const latest = useRef(flush);
  latest.current = flush;

  useEffect(() => {
    if (!register) return undefined;
    register(key, () => latest.current());
    return () => register(key, null);
  }, [register, key]);
}
