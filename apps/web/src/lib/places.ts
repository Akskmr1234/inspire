import { useQuery } from '@tanstack/react-query';
import { request, type ApiError } from '@/lib/api';

/**
 * Where a firm's parties are: countries with their currencies, the states of each, and
 * the areas and routes the firm divides its customers into.
 *
 * A party records a country by its ISO code and a state by its code - the same code a
 * GSTIN begins with in India - so these lists can be corrected without anybody's
 * address having to be found again.
 */

const PARTIES = '/parties';

/** Area or route, matching the server's enum. */
export const TerritoryKind = { area: 1, route: 2 } as const;

export interface CountrySummary {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  readonly currencyCode: string | null;
  readonly currencyName: string | null;
  readonly stateCount: number;
  readonly isActive: boolean;
}

export interface StateSummary {
  readonly id: string;
  readonly countryId: string;
  readonly countryCode: string;
  readonly countryName: string;
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
  readonly isActive: boolean;
}

export interface TerritorySummary {
  readonly id: string;
  readonly kind: number;
  readonly name: string;
  readonly isActive: boolean;
}

export interface CountryInput {
  readonly name: string;
  readonly nameArabic: string | null;
  readonly currencyCode: string | null;
  readonly currencyName: string | null;
}

export const listCountries = (
  includeInactive = false,
): Promise<readonly CountrySummary[]> =>
  request(`${PARTIES}/countries?includeInactive=${String(includeInactive)}`);

export const createCountry = (
  country: CountryInput & { readonly code: string },
): Promise<string> => request(`${PARTIES}/countries`, { method: 'POST', body: country });

export const updateCountry = (id: string, country: CountryInput): Promise<void> =>
  request(`${PARTIES}/countries/${id}`, { method: 'PUT', body: country });

export const setCountryActive = (id: string, isActive: boolean): Promise<void> =>
  request(`${PARTIES}/countries/${id}/active`, { method: 'PUT', body: { isActive } });

export const listStates = (
  countryId: string | null,
  includeInactive = false,
): Promise<readonly StateSummary[]> => {
  const query = new URLSearchParams({ includeInactive: String(includeInactive) });
  if (countryId) query.set('countryId', countryId);
  return request(`${PARTIES}/states?${query.toString()}`);
};

export const createState = (state: {
  readonly countryId: string;
  readonly code: string;
  readonly name: string;
  readonly nameArabic: string | null;
}): Promise<string> => request(`${PARTIES}/states`, { method: 'POST', body: state });

export const updateState = (
  id: string,
  state: {
    readonly countryId: string;
    readonly name: string;
    readonly nameArabic: string | null;
  },
): Promise<void> => request(`${PARTIES}/states/${id}`, { method: 'PUT', body: state });

export const setStateActive = (id: string, isActive: boolean): Promise<void> =>
  request(`${PARTIES}/states/${id}/active`, { method: 'PUT', body: { isActive } });

export const listTerritories = (
  kind: number | null,
  includeInactive = false,
): Promise<readonly TerritorySummary[]> => {
  const query = new URLSearchParams({ includeInactive: String(includeInactive) });
  if (kind) query.set('kind', String(kind));
  return request(`${PARTIES}/territories?${query.toString()}`);
};

export const createTerritory = (kind: number, name: string): Promise<string> =>
  request(`${PARTIES}/territories`, { method: 'POST', body: { kind, name } });

export const renameTerritory = (id: string, name: string): Promise<void> =>
  request(`${PARTIES}/territories/${id}`, { method: 'PUT', body: { name } });

export const setTerritoryActive = (id: string, isActive: boolean): Promise<void> =>
  request(`${PARTIES}/territories/${id}/active`, { method: 'PUT', body: { isActive } });

/** The active countries, shared by every picker on the page. */
export function useCountries(): readonly CountrySummary[] {
  return (
    useQuery<readonly CountrySummary[], ApiError>({
      queryKey: ['countries', false],
      queryFn: () => listCountries(false),
      staleTime: 10 * 60_000,
    }).data ?? []
  );
}

/** Every active state; a picker narrows them to its country. */
export function useStates(): readonly StateSummary[] {
  return (
    useQuery<readonly StateSummary[], ApiError>({
      queryKey: ['states', false, 'all'],
      queryFn: () => listStates(null, false),
      staleTime: 10 * 60_000,
    }).data ?? []
  );
}

/** The active areas or routes. */
export function useTerritories(kind: number): readonly TerritorySummary[] {
  return (
    useQuery<readonly TerritorySummary[], ApiError>({
      queryKey: ['territories', false, kind],
      queryFn: () => listTerritories(kind, false),
      staleTime: 5 * 60_000,
    }).data ?? []
  );
}
