import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Modal } from '@/components/Modal';
import type { ApiError } from '@/lib/api';
import {
  fetchRoleDashboard,
  fetchWidgetCatalogue,
  move,
  saveRoleDashboard,
  type RoleDashboard,
  type WidgetDefinition,
} from '@/lib/dashboards';
import { listRoles, type RoleSummary } from '@/lib/users';
import { useSession } from '@/stores/session';

/** A widget as it is being arranged: from the catalogue, or a custom one the role already has. */
interface Arranged {
  readonly key: string;
  readonly metricCode: string | null;
  readonly widgetId: string | null;
  readonly title: string;
  readonly span: number;
}

/**
 * Chooses and arranges the widgets a role's dashboard shows.
 *
 * Every user with the role sees what is arranged here. A widget is added from the
 * catalogue, taken off with its cross, and moved with its arrows; its width is one to
 * four columns of the dashboard's grid.
 */
export function DashboardCustomizer({
  onClose,
}: {
  readonly onClose: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const { language } = useSession();
  const queryClient = useQueryClient();
  const [roleId, setRoleId] = useState('');
  const [items, setItems] = useState<readonly Arranged[] | null>(null);

  const roles = useQuery<readonly RoleSummary[], ApiError>({
    queryKey: ['roles'],
    queryFn: listRoles,
  });

  const catalogue = useQuery<readonly WidgetDefinition[], ApiError>({
    queryKey: ['dashboard-catalogue'],
    queryFn: fetchWidgetCatalogue,
  });

  const role = useQuery<RoleDashboard, ApiError>({
    queryKey: ['role-dashboard', roleId],
    queryFn: () => fetchRoleDashboard(roleId),
    enabled: roleId !== '',
  });

  useEffect(() => {
    const first = roles.data?.[0];
    if (roleId === '' && first) setRoleId(first.roleId);
  }, [roles.data, roleId]);

  const titleOf = (definition: {
    readonly title: string;
    readonly titleArabic: string | null;
  }): string =>
    language === 'ar' && definition.titleArabic
      ? definition.titleArabic
      : definition.title;

  // What the role shows now, each time a role is chosen.
  useEffect(() => {
    if (!role.data) return;
    setItems(
      role.data.widgets.map((widget) => ({
        key: widget.id,
        metricCode: widget.isCustom ? null : widget.metricCode,
        widgetId: widget.isCustom ? widget.id : null,
        title: titleOf(widget),
        span: widget.span,
      })),
    );
  }, [role.data, language]);

  const available = useMemo(
    () =>
      (catalogue.data ?? []).filter(
        (definition) =>
          !(items ?? []).some((item) => item.metricCode === definition.metricCode),
      ),
    [catalogue.data, items],
  );

  const save = useMutation<void, ApiError>({
    mutationFn: () =>
      saveRoleDashboard(
        roleId,
        (items ?? []).map((item) =>
          item.widgetId
            ? { widgetId: item.widgetId, span: item.span }
            : { metricCode: item.metricCode ?? '', span: item.span },
        ),
      ),
    onSuccess: async () => {
      // The list first. A role arranged for the first time is given a dashboard of its
      // own, and the one it shared is no longer its to read: refetching that one's
      // figures would only be refused. A dashboard rearranged in place has its figures
      // read again; a new one is read when the page turns to it.
      const showing = (): string | undefined =>
        queryClient.getQueryData<{
          readonly dashboards?: readonly { readonly id: string }[];
        }>(['dashboards'])?.dashboards?.[0]?.id;
      const before = showing();

      await queryClient.invalidateQueries({ queryKey: ['dashboards'] });
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ['dashboard-data'],
          refetchType: showing() === before ? 'active' : 'none',
        }),
        queryClient.invalidateQueries({ queryKey: ['role-dashboard'] }),
      ]);
      onClose();
    },
  });

  const update = (index: number, patch: Partial<Arranged>): void =>
    setItems((current) =>
      (current ?? []).map((item, at) => (at === index ? { ...item, ...patch } : item)),
    );

  return (
    <Modal title={t('dashboard.customize')} size="form" onClose={onClose}>
      <div className="space-y-4">
        <label className="field">
          <span className="field-label">{t('dashboard.role')}</span>
          <select
            value={roleId}
            onChange={(event) => {
              setItems(null);
              setRoleId(event.target.value);
            }}
            className="field-input"
          >
            {(roles.data ?? []).map((option) => (
              <option key={option.roleId} value={option.roleId}>
                {option.name}
              </option>
            ))}
          </select>
        </label>

        <div className="grid gap-4 sm:grid-cols-2">
          <section aria-labelledby="dashboard-shown">
            <h3
              id="dashboard-shown"
              className="mb-2 text-xs font-bold tracking-wide text-ink-muted uppercase"
            >
              {t('dashboard.shown')}
            </h3>
            {items === null ? (
              <p className="text-sm text-ink-subtle">{t('common.loading')}</p>
            ) : items.length === 0 ? (
              <p className="text-sm text-ink-subtle">{t('dashboard.noWidgets')}</p>
            ) : (
              <ol className="space-y-1.5">
                {items.map((item, index) => (
                  <li
                    key={item.key}
                    className="flex items-center gap-1.5 rounded-lg border border-line bg-surface px-2 py-1.5"
                  >
                    <span className="w-5 shrink-0 text-center text-xs text-ink-subtle tabular-nums">
                      {index + 1}
                    </span>
                    <span className="min-w-0 flex-1 truncate text-sm text-ink">
                      {item.title}
                    </span>
                    <select
                      value={item.span}
                      onChange={(event) =>
                        update(index, { span: Number(event.target.value) })
                      }
                      aria-label={`${t('dashboard.width')} — ${item.title}`}
                      title={t('dashboard.width')}
                      className="field-input-sm w-14"
                    >
                      {[1, 2, 3, 4].map((span) => (
                        <option key={span} value={span}>
                          {span}
                        </option>
                      ))}
                    </select>
                    <button
                      type="button"
                      disabled={index === 0}
                      onClick={() =>
                        setItems((current) => move(current ?? [], index, -1))
                      }
                      aria-label={`${t('dashboard.moveUp')} — ${item.title}`}
                      className="btn-ghost btn-sm px-1.5"
                    >
                      ↑
                    </button>
                    <button
                      type="button"
                      disabled={index === items.length - 1}
                      onClick={() => setItems((current) => move(current ?? [], index, 1))}
                      aria-label={`${t('dashboard.moveDown')} — ${item.title}`}
                      className="btn-ghost btn-sm px-1.5"
                    >
                      ↓
                    </button>
                    <button
                      type="button"
                      onClick={() =>
                        setItems((current) =>
                          (current ?? []).filter((_, at) => at !== index),
                        )
                      }
                      aria-label={`${t('dashboard.remove')} — ${item.title}`}
                      className="btn-ghost btn-sm px-1.5 text-red-600 dark:text-red-400"
                    >
                      ×
                    </button>
                  </li>
                ))}
              </ol>
            )}
          </section>

          <section aria-labelledby="dashboard-available">
            <h3
              id="dashboard-available"
              className="mb-2 text-xs font-bold tracking-wide text-ink-muted uppercase"
            >
              {t('dashboard.available')}
            </h3>
            <ul className="space-y-1.5">
              {available.map((definition) => (
                <li
                  key={definition.metricCode}
                  className="flex items-center gap-2 rounded-lg border border-dashed border-line px-2 py-1.5"
                >
                  <span className="min-w-0 flex-1 truncate text-sm text-ink">
                    {titleOf(definition)}
                  </span>
                  <button
                    type="button"
                    disabled={items === null}
                    onClick={() =>
                      setItems((current) => [
                        ...(current ?? []),
                        {
                          key: `new-${definition.metricCode}`,
                          metricCode: definition.metricCode,
                          widgetId: null,
                          title: titleOf(definition),
                          span: definition.span,
                        },
                      ])
                    }
                    aria-label={`${t('dashboard.add')} — ${titleOf(definition)}`}
                    className="btn-secondary btn-sm"
                  >
                    + {t('dashboard.add')}
                  </button>
                </li>
              ))}
            </ul>
          </section>
        </div>

        {save.isError && (
          <p role="alert" className="alert-error">
            {save.error.detail || save.error.code}
          </p>
        )}

        <div className="form-actions flex justify-end gap-2">
          <button type="button" onClick={onClose} className="btn-secondary">
            {t('common.cancel')}
          </button>
          <button
            type="button"
            disabled={save.isPending || items === null || roleId === ''}
            onClick={() => save.mutate()}
            className="btn-primary"
            data-shortcut="save"
          >
            {save.isPending ? t('common.saving') : t('common.save')}
          </button>
        </div>
      </div>
    </Modal>
  );
}
