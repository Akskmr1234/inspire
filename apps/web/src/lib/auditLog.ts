import { request } from '@/lib/api';

/** One change as the audit trail keeps it. */
export interface AuditLogEntry {
  readonly id: string;
  readonly occurredAtUtc: string;
  readonly userId: string;
  readonly userName: string;
  readonly module: string;
  readonly entityType: string;
  readonly recordId: string;
  readonly recordLabel: string | null;
  readonly action: string;
  readonly oldValues: string | null;
  readonly newValues: string | null;
  readonly batchId: string;
}

/** One page of the trail. */
export interface AuditLogPage {
  readonly items: readonly AuditLogEntry[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

/** What the trail can be filtered by. */
export interface AuditLogFilters {
  readonly modules: readonly string[];
  readonly users: readonly { readonly userId: string; readonly userName: string }[];
  readonly isSeparateDatabase: boolean;
}

/** The filters a search applies. */
export interface AuditLogQuery {
  readonly module: string;
  readonly userId: string;
  readonly action: string;
  readonly from: string;
  readonly to: string;
  readonly search: string;
  readonly page: number;
  readonly pageSize: number;
}

const AUDIT = '/platform/audit-log';

/** Searches the trail, newest first. */
export function listAuditLog(query: AuditLogQuery): Promise<AuditLogPage> {
  const params = new URLSearchParams({
    page: String(query.page),
    pageSize: String(query.pageSize),
  });

  if (query.module) params.set('module', query.module);
  if (query.userId) params.set('userId', query.userId);
  if (query.action) params.set('action', query.action);
  if (query.from) params.set('from', query.from);
  if (query.to) params.set('to', query.to);
  if (query.search.trim()) params.set('search', query.search.trim());

  return request<AuditLogPage>(`${AUDIT}?${params.toString()}`);
}

/** Lists the modules and users the trail can be filtered by. */
export function fetchAuditLogFilters(): Promise<AuditLogFilters> {
  return request<AuditLogFilters>(`${AUDIT}/filters`);
}

/** One field's value before and after a change. */
export interface AuditChange {
  readonly field: string;
  readonly before: unknown;
  readonly after: unknown;
}

/** Lines up the values before and after a change, field by field. */
export function auditChanges(entry: AuditLogEntry): readonly AuditChange[] {
  const before = parse(entry.oldValues);
  const after = parse(entry.newValues);
  const fields = [...new Set([...Object.keys(before), ...Object.keys(after)])];

  return fields.map((field) => ({ field, before: before[field], after: after[field] }));
}

function parse(json: string | null): Record<string, unknown> {
  if (!json) return {};
  try {
    const value: unknown = JSON.parse(json);
    return value && typeof value === 'object' ? (value as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}
