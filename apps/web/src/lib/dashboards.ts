import { request } from '@/lib/api';

/** How a panel draws its figure, keyed by the wire value. */
export const WIDGET_KIND = { Kpi: 1, Series: 2, Breakdown: 3, List: 4 } as const;

/** A widget a dashboard can be made of. */
export interface WidgetDefinition {
  readonly metricCode: string;
  readonly title: string;
  readonly titleArabic: string;
  readonly kind: number;
  readonly span: number;
}

/** A widget on a role's dashboard. */
export interface RoleWidget {
  readonly id: string;
  readonly metricCode: string | null;
  readonly title: string;
  readonly titleArabic: string | null;
  readonly kind: number;
  readonly span: number;
  readonly isCustom: boolean;
}

export interface RoleDashboard {
  readonly roleId: string;
  readonly roleName: string;
  readonly widgets: readonly RoleWidget[];
}

/** One widget of a role's dashboard as it is to be arranged: a catalogue widget or a kept custom one. */
export type ArrangedWidget =
  | { readonly metricCode: string; readonly span?: number }
  | { readonly widgetId: string; readonly span?: number };

export const fetchWidgetCatalogue = (): Promise<readonly WidgetDefinition[]> =>
  request<readonly WidgetDefinition[]>('/dashboards/catalogue');

export const fetchRoleDashboard = (roleId: string): Promise<RoleDashboard> =>
  request<RoleDashboard>(`/dashboards/roles/${roleId}`);

export const saveRoleDashboard = (
  roleId: string,
  widgets: readonly ArrangedWidget[],
): Promise<void> =>
  request<void>(`/dashboards/roles/${roleId}`, { method: 'PUT', body: { widgets } });

/** Metrics whose figures are counts or quantities rather than money. */
export const QUANTITY_METRICS: ReadonlySet<string> = new Set(['inventory.low-stock']);

/** Moves one item of a list up or down by one place. */
export function move<T>(items: readonly T[], index: number, by: -1 | 1): readonly T[] {
  const target = index + by;

  if (target < 0 || target >= items.length) return items;

  const next = [...items];
  const [item] = next.splice(index, 1);
  if (item !== undefined) next.splice(target, 0, item);
  return next;
}
