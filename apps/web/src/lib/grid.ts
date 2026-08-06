import { request } from '@/lib/api';

/**
 * A user's arrangement of one grid.
 *
 * The server stores this as opaque JSON and hands it back unread, so the shape is
 * owned here. That is what lets the grid grow new capabilities — grouping, per-column
 * widths — without a schema migration each time.
 */
export interface GridLayoutState {
  readonly order?: readonly string[];
  readonly hidden?: readonly string[];
  readonly sortKey?: string | null;
  readonly sortDescending?: boolean;
  readonly frozen?: number;
}

interface GridLayoutEnvelope {
  readonly gridKey: string;
  readonly state: string | null;
}

/**
 * Reads back a saved arrangement.
 *
 * Returns null both when nothing has been saved and when what was saved no longer
 * parses. A layout is a convenience, and the correct response to a corrupt one is the
 * grid's default arrangement rather than a broken screen.
 */
export async function fetchGridLayout(gridKey: string): Promise<GridLayoutState | null> {
  const envelope = await request<GridLayoutEnvelope>(`/grid-layouts/${gridKey}`);

  if (!envelope.state) {
    return null;
  }

  try {
    return JSON.parse(envelope.state) as GridLayoutState;
  } catch {
    return null;
  }
}

/** Records an arrangement. */
export function saveGridLayout(
  gridKey: string,
  state: GridLayoutState,
): Promise<void> {
  return request<void>(`/grid-layouts/${gridKey}`, {
    method: 'PUT',
    body: { state: JSON.stringify(state) },
  });
}

/** Forgets an arrangement, returning the grid to its default. */
export function resetGridLayout(gridKey: string): Promise<void> {
  return request<void>(`/grid-layouts/${gridKey}`, { method: 'DELETE' });
}
