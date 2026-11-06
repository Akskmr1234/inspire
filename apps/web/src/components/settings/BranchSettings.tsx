import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Modal, ModalButton } from '@/components/Modal';
import { CheckField, TextField } from '@/components/Form';
import { ActiveBadge } from '@/components/StatusBadge';
import { SettingsCard } from '@/components/settings/SettingsSections';
import type { ApiError } from '@/lib/api';
import {
  createBranch,
  listBranches,
  updateBranch,
  type BranchSummary,
} from '@/lib/branches';
import { useSession } from '@/stores/session';

/**
 * The firm's branches: adding one, correcting one, and putting one away.
 *
 * Who may work in which branch is set on the users screen; whoever adds a branch may
 * work in it at once, so it is never made out of everybody's reach.
 */
export function BranchSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const mayEdit = useSession((state) => state.can)('platform:firm:edit');
  const [editing, setEditing] = useState<BranchSummary | 'new' | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const branches = useQuery<readonly BranchSummary[], ApiError>({
    queryKey: ['platform-branches'],
    queryFn: listBranches,
  });

  return (
    <div className="grid grid-cols-1 gap-4">
      <SettingsCard title={t('branches.title')} wide>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="text-xs text-ink-muted">{t('branches.accessHint')}</p>
          {mayEdit && (
            <button
              type="button"
              className="btn-primary btn-sm"
              onClick={() => setEditing('new')}
            >
              + {t('branches.add')}
            </button>
          )}
        </div>

        {notice && <p className="alert-success">{notice}</p>}
        {branches.isError && (
          <p className="alert-error">{branches.error.detail || branches.error.code}</p>
        )}

        {branches.isPending ? (
          <p className="text-xs text-ink-muted">{t('common.loading')}</p>
        ) : (
          <div className="table-wrap">
            <table className="table text-sm">
              <thead>
                <tr>
                  <th className="text-start">{t('branches.code')}</th>
                  <th className="text-start">{t('branches.name')}</th>
                  <th className="text-start">{t('branches.phone')}</th>
                  <th className="text-end">{t('branches.users')}</th>
                  <th className="text-start">{t('branches.status')}</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {(branches.data ?? []).map((branch) => (
                  <tr key={branch.branchId}>
                    <td className="font-mono text-xs">{branch.code}</td>
                    <td>
                      <span className="font-medium text-ink">{branch.name}</span>
                      {branch.isHeadOffice && (
                        <span className="ms-2 rounded bg-brand-500/10 px-1.5 py-0.5 text-[11px] font-medium text-brand-700 dark:text-brand-300">
                          {t('branches.headOffice')}
                        </span>
                      )}
                      {branch.address && (
                        <span className="block text-xs text-ink-muted">
                          {branch.address}
                        </span>
                      )}
                    </td>
                    <td className="text-ink-muted">{branch.phone ?? '—'}</td>
                    <td className="text-end tabular-nums">{branch.userCount}</td>
                    <td>
                      <ActiveBadge isActive={branch.isActive} />
                    </td>
                    <td className="text-end">
                      {mayEdit && (
                        <button
                          type="button"
                          className="btn-ghost btn-xs"
                          onClick={() => setEditing(branch)}
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
        <BranchDialog
          branch={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={(saved, created) => {
            setEditing(null);
            setNotice(
              t(created ? 'branches.createdNotice' : 'branches.updatedNotice', {
                name: saved.name,
              }),
            );
            void queryClient.invalidateQueries({ queryKey: ['platform-branches'] });
            void queryClient.invalidateQueries({ queryKey: ['my-branches'] });
          }}
        />
      )}
    </div>
  );
}

function BranchDialog({
  branch,
  onClose,
  onSaved,
}: {
  readonly branch: BranchSummary | null;
  readonly onClose: () => void;
  readonly onSaved: (saved: BranchSummary, created: boolean) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState(branch?.code ?? '');
  const [name, setName] = useState(branch?.name ?? '');
  const [nameArabic, setNameArabic] = useState(branch?.nameArabic ?? '');
  const [address, setAddress] = useState(branch?.address ?? '');
  const [phone, setPhone] = useState(branch?.phone ?? '');
  const [email, setEmail] = useState(branch?.email ?? '');
  const [active, setActive] = useState(branch?.isActive ?? true);

  const save = useMutation<BranchSummary, ApiError>({
    mutationFn: () => {
      const body = {
        code: code.trim(),
        name: name.trim(),
        nameArabic: nameArabic.trim() || null,
        address: address.trim() || null,
        phone: phone.trim() || null,
        email: email.trim() || null,
        isActive: active,
      };
      return branch ? updateBranch(branch.branchId, body) : createBranch(body);
    },
    onSuccess: (saved) => onSaved(saved, branch === null),
  });

  return (
    <Modal
      title={branch ? t('branches.editTitle', { name: branch.name }) : t('branches.add')}
      size="form"
      onClose={onClose}
    >
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <TextField
          label={t('branches.code')}
          value={code}
          onChange={setCode}
          disabled={branch !== null}
          required
          maxLength={20}
          {...(branch ? { hint: t('branches.codeFixed') } : {})}
        />
        <TextField
          label={t('branches.name')}
          value={name}
          onChange={setName}
          required
          maxLength={100}
        />
        <TextField
          label={t('branches.nameArabic')}
          value={nameArabic}
          onChange={setNameArabic}
          dir="rtl"
          maxLength={100}
        />
        <TextField
          label={t('branches.phone')}
          type="tel"
          value={phone}
          onChange={setPhone}
          maxLength={30}
        />
        <TextField
          label={t('branches.address')}
          value={address}
          onChange={setAddress}
          maxLength={200}
          className="sm:col-span-2"
        />
        <TextField
          label={t('branches.email')}
          type="email"
          value={email}
          onChange={setEmail}
          maxLength={256}
        />
      </div>

      {!branch?.isHeadOffice && (
        <CheckField label={t('branches.active')} checked={active} onChange={setActive} />
      )}

      {save.isError && (
        <p className="alert-error">{save.error.detail || save.error.code}</p>
      )}

      <div className="form-actions">
        <ModalButton onClick={onClose}>{t('common.cancel')}</ModalButton>
        <ModalButton
          primary
          disabled={save.isPending || code.trim() === '' || name.trim() === ''}
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.saving') : t('common.save')}
        </ModalButton>
      </div>
    </Modal>
  );
}
