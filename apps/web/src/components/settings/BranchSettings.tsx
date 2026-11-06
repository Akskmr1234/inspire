import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Modal, ModalButton } from '@/components/Modal';
import { CheckField, SelectField, TextField } from '@/components/Form';
import { ActiveBadge } from '@/components/StatusBadge';
import { SettingsCard } from '@/components/settings/SettingsSections';
import { currentFirmId, type ApiError } from '@/lib/api';
import { createBranch, updateBranch, type BranchSummary } from '@/lib/branches';
import { listFirms, type FirmSummary } from '@/lib/firms';
import { useSession } from '@/stores/session';

/** What the dialog is open on: a branch to correct, or a new one under a firm. */
interface Editing {
  readonly branch: BranchSummary | null;
  readonly firmId: string;
}

/**
 * Every firm's branches, each under its firm: adding one to a firm, correcting one,
 * and putting one away.
 *
 * Who may work in which branch is set on the users screen; whoever adds a branch may
 * work in it at once, so it is never made out of everybody's reach.
 */
export function BranchSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const mayEdit = useSession((state) => state.can)('platform:firm:edit');
  const [editing, setEditing] = useState<Editing | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const current = currentFirmId();

  const firms = useQuery<readonly FirmSummary[], ApiError>({
    queryKey: ['platform-firms'],
    queryFn: listFirms,
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
              onClick={() =>
                setEditing({
                  branch: null,
                  firmId: current ?? firms.data?.[0]?.firmId ?? '',
                })
              }
            >
              + {t('branches.add')}
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
                  <th className="text-start">{t('branches.code')}</th>
                  <th className="text-start">{t('branches.name')}</th>
                  <th className="text-start">{t('branches.phone')}</th>
                  <th className="text-end">{t('branches.users')}</th>
                  <th className="text-start">{t('branches.status')}</th>
                  <th />
                </tr>
              </thead>
              {(firms.data ?? []).map((firm) => (
                <tbody key={firm.firmId}>
                  {/* The firm's name over its branches, with a button to add one to it. */}
                  <tr className="bg-surface-2">
                    <th
                      colSpan={6}
                      scope="rowgroup"
                      className="py-1.5 text-start text-xs font-semibold text-ink"
                    >
                      <span className="inline-flex flex-wrap items-center gap-2">
                        {firm.name}
                        <span className="font-mono font-normal text-ink-muted">
                          {firm.code}
                        </span>
                        {firm.firmId === current && (
                          <span className="rounded bg-brand-500/10 px-1.5 py-0.5 text-[11px] font-medium text-brand-700 dark:text-brand-300">
                            {t('firms.current')}
                          </span>
                        )}
                        {!firm.isActive && (
                          <span className="font-normal text-ink-muted">
                            {t('branches.inactive')}
                          </span>
                        )}
                        {mayEdit && firm.isActive && (
                          <button
                            type="button"
                            className="btn-ghost btn-xs"
                            onClick={() =>
                              setEditing({ branch: null, firmId: firm.firmId })
                            }
                          >
                            + {t('branches.add')}
                          </button>
                        )}
                      </span>
                    </th>
                  </tr>
                  {firm.branches.map((branch) => (
                    <tr key={branch.branchId}>
                      <td className="ps-6 font-mono text-xs">{branch.code}</td>
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
                            onClick={() => setEditing({ branch, firmId: firm.firmId })}
                          >
                            {t('common.edit')}
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              ))}
            </table>
          </div>
        )}
      </SettingsCard>

      {editing && (
        <BranchDialog
          branch={editing.branch}
          firmId={editing.firmId}
          firms={firms.data ?? []}
          onClose={() => setEditing(null)}
          onSaved={(saved, created) => {
            setEditing(null);
            setNotice(
              t(created ? 'branches.createdNotice' : 'branches.updatedNotice', {
                name: saved.name,
              }),
            );
            void queryClient.invalidateQueries({ queryKey: ['platform-firms'] });
            void queryClient.invalidateQueries({ queryKey: ['platform-branches'] });
            void queryClient.invalidateQueries({ queryKey: ['my-branches'] });
            void queryClient.invalidateQueries({ queryKey: ['my-firms'] });
          }}
        />
      )}
    </div>
  );
}

function BranchDialog({
  branch,
  firmId: startingFirm,
  firms,
  onClose,
  onSaved,
}: {
  readonly branch: BranchSummary | null;
  /** The firm a new branch goes under, until another is chosen. */
  readonly firmId: string;
  readonly firms: readonly FirmSummary[];
  readonly onClose: () => void;
  readonly onSaved: (saved: BranchSummary, created: boolean) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [firmId, setFirmId] = useState(startingFirm);
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
        firmId: branch ? null : firmId || null,
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
        {/* A branch stays with its firm, so the firm is chosen when it is made. */}
        <SelectField<string>
          label={t('branches.firm')}
          value={firmId}
          onChange={setFirmId}
          disabled={branch !== null}
          required
          className="sm:col-span-2"
          options={firms
            .filter((firm) => firm.isActive || firm.firmId === firmId)
            .map((firm) => ({
              value: firm.firmId,
              label: `${firm.name} (${firm.code})`,
            }))}
        />
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
          disabled={
            save.isPending || firmId === '' || code.trim() === '' || name.trim() === ''
          }
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.saving') : t('common.save')}
        </ModalButton>
      </div>
    </Modal>
  );
}
