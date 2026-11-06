import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Modal, ModalButton } from '@/components/Modal';
import { CheckField, TextField } from '@/components/Form';
import { ActiveBadge } from '@/components/StatusBadge';
import { SettingsCard } from '@/components/settings/SettingsSections';
import { currentFirmId, type ApiError } from '@/lib/api';
import { createFirm, listFirms, updateFirm, type FirmSummary } from '@/lib/firms';
import { useSession } from '@/stores/session';

/**
 * The tenant's firms: adding one, correcting one, and putting one away.
 *
 * A firm keeps books of its own, so a new one starts the way the first did - a head
 * office, a chart of accounts, an open year - and whoever adds it may work in it at once.
 * Who else may is set on the users screen, branch by branch.
 */
export function FirmSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const can = useSession((state) => state.can);
  const [editing, setEditing] = useState<FirmSummary | 'new' | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const current = currentFirmId();

  const firms = useQuery<readonly FirmSummary[], ApiError>({
    queryKey: ['platform-firms'],
    queryFn: listFirms,
  });

  const currency =
    firms.data?.find((firm) => firm.firmId === current)?.baseCurrency ??
    firms.data?.[0]?.baseCurrency ??
    '';

  return (
    <div className="grid grid-cols-1 gap-4">
      <SettingsCard title={t('firms.title')} wide>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="text-xs text-ink-muted">{t('firms.hint')}</p>
          {can('platform:firm:create') && (
            <button
              type="button"
              className="btn-primary btn-sm"
              onClick={() => setEditing('new')}
            >
              + {t('firms.add')}
            </button>
          )}
        </div>

        {notice && <p className="alert-success">{notice}</p>}
        {firms.isError && (
          <p className="alert-error">{firms.error.detail || firms.error.code}</p>
        )}

        {firms.isPending ? (
          <p className="text-xs text-ink-muted">{t('common.loading')}</p>
        ) : (
          <div className="table-wrap">
            <table className="table text-sm">
              <thead>
                <tr>
                  <th className="text-start">{t('firms.code')}</th>
                  <th className="text-start">{t('firms.name')}</th>
                  <th className="text-start">{t('firms.currency')}</th>
                  <th className="text-start">{t('firms.branches')}</th>
                  <th className="text-end">{t('branches.users')}</th>
                  <th className="text-start">{t('branches.status')}</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {(firms.data ?? []).map((firm) => (
                  <tr key={firm.firmId}>
                    <td className="font-mono text-xs">{firm.code}</td>
                    <td>
                      <span className="font-medium text-ink">{firm.name}</span>
                      {firm.firmId === current && (
                        <span className="ms-2 rounded bg-brand-500/10 px-1.5 py-0.5 text-[11px] font-medium text-brand-700 dark:text-brand-300">
                          {t('firms.current')}
                        </span>
                      )}
                      {firm.taxRegistrationNumber && (
                        <span className="block text-xs text-ink-muted">
                          {firm.taxRegistrationNumber}
                        </span>
                      )}
                    </td>
                    <td className="font-mono text-xs">{firm.baseCurrency}</td>
                    <td className="text-ink-muted">
                      {firm.branches.map((branch) => branch.name).join(', ')}
                    </td>
                    <td className="text-end tabular-nums">{firm.userCount}</td>
                    <td>
                      <ActiveBadge isActive={firm.isActive} />
                    </td>
                    <td className="text-end">
                      {can('platform:firm:edit') && (
                        <button
                          type="button"
                          className="btn-ghost btn-xs"
                          onClick={() => setEditing(firm)}
                        >
                          {t('common.edit')}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </SettingsCard>

      {editing && (
        <FirmDialog
          firm={editing === 'new' ? null : editing}
          currency={currency}
          isCurrent={editing !== 'new' && editing.firmId === current}
          onClose={() => setEditing(null)}
          onSaved={(saved, created) => {
            setEditing(null);
            setNotice(
              t(created ? 'firms.createdNotice' : 'firms.updatedNotice', {
                name: saved.name,
              }),
            );
            void queryClient.invalidateQueries({ queryKey: ['platform-firms'] });
            void queryClient.invalidateQueries({ queryKey: ['my-firms'] });
          }}
        />
      )}
    </div>
  );
}

function FirmDialog({
  firm,
  currency: defaultCurrency,
  isCurrent,
  onClose,
  onSaved,
}: {
  readonly firm: FirmSummary | null;
  /** The currency a new firm starts with: the one in session's. */
  readonly currency: string;
  /** The firm in session, which cannot be put away from under it. */
  readonly isCurrent: boolean;
  readonly onClose: () => void;
  readonly onSaved: (saved: FirmSummary, created: boolean) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState(firm?.code ?? '');
  const [name, setName] = useState(firm?.name ?? '');
  const [nameArabic, setNameArabic] = useState(firm?.nameArabic ?? '');
  const [currency, setCurrency] = useState(firm?.baseCurrency ?? defaultCurrency);
  const [taxNumber, setTaxNumber] = useState(firm?.taxRegistrationNumber ?? '');
  const [active, setActive] = useState(firm?.isActive ?? true);

  const save = useMutation<FirmSummary, ApiError>({
    mutationFn: () => {
      const body = {
        code: code.trim(),
        name: name.trim(),
        nameArabic: nameArabic.trim() || null,
        baseCurrency: currency.trim().toUpperCase(),
        taxRegistrationNumber: taxNumber.trim() || null,
        isActive: active,
      };
      return firm ? updateFirm(firm.firmId, body) : createFirm(body);
    },
    onSuccess: (saved) => onSaved(saved, firm === null),
  });

  return (
    <Modal
      title={firm ? t('firms.editTitle', { name: firm.name }) : t('firms.add')}
      size="form"
      onClose={onClose}
    >
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <TextField
          label={t('firms.code')}
          value={code}
          onChange={setCode}
          disabled={firm !== null}
          required
          maxLength={20}
        />
        <TextField
          label={t('firms.name')}
          value={name}
          onChange={setName}
          required
          maxLength={200}
        />
        <TextField
          label={t('firms.nameArabic')}
          value={nameArabic}
          onChange={setNameArabic}
          dir="rtl"
          maxLength={200}
        />
        <TextField
          label={t('firms.currency')}
          value={currency}
          onChange={setCurrency}
          disabled={firm !== null}
          required
          maxLength={3}
          hint={t('firms.fixedHint')}
        />
        <TextField
          label={t('firms.taxNumber')}
          value={taxNumber}
          onChange={setTaxNumber}
          maxLength={30}
          className="sm:col-span-2"
        />
      </div>

      {!firm && <p className="text-xs text-ink-muted">{t('firms.setUpHint')}</p>}

      {!isCurrent && (
        <CheckField label={t('branches.active')} checked={active} onChange={setActive} />
      )}

      {save.isError && (
        <p className="alert-error">{save.error.detail || save.error.code}</p>
      )}

      <div className="form-actions">
        <ModalButton onClick={onClose}>{t('common.cancel')}</ModalButton>
        <ModalButton
          primary
          disabled={
            save.isPending ||
            code.trim() === '' ||
            name.trim() === '' ||
            currency.trim().length !== 3
          }
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.saving') : t('common.save')}
        </ModalButton>
      </div>
    </Modal>
  );
}
