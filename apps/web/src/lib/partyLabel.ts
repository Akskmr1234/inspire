import { useCallback } from 'react';
import { useSettings } from '@/stores/settings';

/** How a customer or supplier reads in a dropdown: General Settings › Display. */
export type PartyDisplay = 'codeAndName' | 'nameOnly';

/**
 * A customer's or supplier's label in a dropdown: "1000 — Primary Customer", or
 * "Primary Customer" alone. The code is never repeated as the name - a party whose code
 * and name are the same shows it once.
 */
export function partyLabel(
  party: { readonly code: string; readonly name: string },
  display: PartyDisplay,
): string {
  const code = party.code.trim();
  const name = party.name.trim();

  if (!name) return code;
  if (
    display === 'nameOnly' ||
    !code ||
    code.localeCompare(name, undefined, { sensitivity: 'base' }) === 0
  ) {
    return name;
  }
  return `${code} — ${name}`;
}

/**
 * A dropdown option for a customer or supplier, labelled as the firm chose. Searching by
 * code still finds it when only the name shows.
 */
export function usePartyOption(): (party: {
  readonly code: string;
  readonly name: string;
}) => { readonly label: string; readonly keywords: string } {
  const display = useSettings((state) => state.partyDisplay);

  return useCallback(
    (party) => ({ label: partyLabel(party, display), keywords: party.code }),
    [display],
  );
}
