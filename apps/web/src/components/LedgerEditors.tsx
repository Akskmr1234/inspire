import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Modal } from '@/components/Modal';
import { Field, NumberField, SelectField, TextField } from '@/components/Form';
import { SearchSelect } from '@/components/SearchSelect';
import { Spinner } from '@/components/ReportFrame';
import type { ApiError } from '@/lib/api';
import {
  createAccountGroup,
  createLedger,
  EntrySide,
  getLedger,
  groupPaths,
  LedgerKind,
  listAccountGroups,
  PLAIN_KINDS,
  renameAccountGroup,
  setLedgerActive,
  updateLedger,
  type AccountGroupSummary,
  type LedgerDetail,
  type SaveLedger,
} from '@/lib/ledgers';

const KIND_KEY: Record<number, string> = {
  [LedgerKind.general]: 'General',
  [LedgerKind.cash]: 'Cash',
  [LedgerKind.bank]: 'Bank',
  [LedgerKind.employee]: 'Employee',
};

/** Everything that lists ledgers, refreshed after a change so pickers see it at once. */
function useInvalidateLedgers(): () => Promise<void> {
  const queryClient = useQueryClient();
  return async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['ledgers'] }),
      queryClient.invalidateQueries({ queryKey: ['account-groups'] }),
      queryClient.invalidateQueries({ queryKey: ['trial-balance-for-ledgers'] }),
    ]);
  };
}

export function useAccountGroups(): ReturnType<
  typeof useQuery<readonly AccountGroupSummary[], ApiError>
> {
  return useQuery<readonly AccountGroupSummary[], ApiError>({
    queryKey: ['account-groups'],
    queryFn: listAccountGroups,
    staleTime: 60 * 1000,
  });
}

/**
 * Opens or corrects a ledger: an expense or income head, a bank account, cash, an
 * employee. Customers and suppliers have their own screens, which also hold their terms.
 */
export function LedgerEditor({
  ledgerId,
  onClose,
  kind,
}: {
  /** The ledger to correct, or null to open a new one. */
  readonly ledgerId: string | null;
  readonly onClose: () => void;
  /** A kind the ledger is fixed to, for a screen of one kind - employees. */
  readonly kind?: number;
}): React.JSX.Element {
  const { t } = useTranslation();
  const existing = useQuery<LedgerDetail, ApiError>({
    queryKey: ['ledger', ledgerId],
    queryFn: () => getLedger(ledgerId ?? ''),
    enabled: ledgerId !== null,
  });

  const title =
    kind === LedgerKind.employee
      ? ledgerId
        ? t('employees.edit')
        : t('employees.new')
      : ledgerId
        ? t('ledgerMaster.editLedger')
        : t('ledgerMaster.newLedger');

  return (
    <Modal title={title} onClose={onClose} size="form">
      {ledgerId && existing.isPending ? (
        <p className="flex items-center gap-2 p-4 text-sm text-ink-muted">
          <Spinner /> {t('common.loading')}
        </p>
      ) : existing.isError ? (
        <p className="alert-error">{existing.error.detail}</p>
      ) : (
        <LedgerForm
          existing={existing.data ?? null}
          onClose={onClose}
          {...(kind === undefined ? {} : { fixedKind: kind })}
        />
      )}
    </Modal>
  );
}

/** Where a new ledger of each fixed kind goes until somebody chooses otherwise. */
const DEFAULT_GROUP_FOR_KIND: Readonly<Record<number, string>> = {
  [LedgerKind.employee]: '2100',
};

function LedgerForm({
  existing,
  onClose,
  fixedKind,
}: {
  readonly existing: LedgerDetail | null;
  readonly onClose: () => void;
  readonly fixedKind?: number;
}): React.JSX.Element {
  const { t } = useTranslation();
  const invalidate = useInvalidateLedgers();
  const groups = useAccountGroups();
  const paths = useMemo(() => groupPaths(groups.data ?? []), [groups.data]);

  const [code, setCode] = useState('');
  const [name, setName] = useState(existing?.name ?? '');
  const [nameArabic, setNameArabic] = useState(existing?.nameArabic ?? '');
  const [groupId, setGroupId] = useState(existing?.accountGroupId ?? '');
  const [kind, setKind] = useState<number>(
    existing?.kind ?? fixedKind ?? LedgerKind.general,
  );

  // A new ledger of a fixed kind starts in the group that kind usually lives in, once
  // the groups have arrived.
  const defaultGroup =
    fixedKind === undefined ? undefined : DEFAULT_GROUP_FOR_KIND[fixedKind];
  const defaultGroupId = groups.data?.find(
    (group) => group.code === defaultGroup,
  )?.accountGroupId;
  if (!existing && groupId === '' && defaultGroupId) setGroupId(defaultGroupId);
  const [opening, setOpening] = useState(
    existing && existing.openingBalance !== 0 ? String(existing.openingBalance) : '',
  );
  const [side, setSide] = useState<number>(existing?.openingSide ?? EntrySide.debit);
  const [phone, setPhone] = useState(existing?.phone ?? '');
  const [email, setEmail] = useState(existing?.email ?? '');
  const [address, setAddress] = useState(existing?.address ?? '');

  // Leaf groups first is what people pick; the five heads stay available for a
  // ledger that belongs directly under one.
  const groupOptions = useMemo(
    () =>
      [...(groups.data ?? [])]
        .sort((a, b) =>
          (paths.get(a.accountGroupId) ?? '').localeCompare(
            paths.get(b.accountGroupId) ?? '',
          ),
        )
        .map((group) => ({
          value: group.accountGroupId,
          label: paths.get(group.accountGroupId) ?? group.name,
        })),
    [groups.data, paths],
  );

  // A new cash or bank ledger belongs under Cash and Bank; choosing the kind moves the
  // group there when nothing has been chosen yet.
  const chooseKind = (next: number): void => {
    setKind(next);
    if (!groupId && (next === LedgerKind.cash || next === LedgerKind.bank)) {
      const cashAndBank = groups.data?.find((group) => group.code === '1110');
      if (cashAndBank) setGroupId(cashAndBank.accountGroupId);
    }
  };

  const body = (): SaveLedger => ({
    ...(existing ? {} : { code: code.trim() || null }),
    name: name.trim(),
    nameArabic: nameArabic.trim() || null,
    accountGroupId: groupId,
    kind,
    openingBalance: Number(opening) || 0,
    openingSide: side,
    phone: phone.trim() || null,
    email: email.trim() || null,
    address: address.trim() || null,
  });

  const save = useMutation<LedgerDetail, ApiError>({
    mutationFn: () =>
      existing ? updateLedger(existing.ledgerId, body()) : createLedger(body()),
    onSuccess: async () => {
      await invalidate();
      onClose();
    },
  });

  const toggle = useMutation<LedgerDetail, ApiError>({
    mutationFn: () =>
      setLedgerActive(existing?.ledgerId ?? '', !(existing?.isActive ?? true)),
    onSuccess: async () => {
      await invalidate();
      onClose();
    },
  });

  if (existing && !existing.isEditable) {
    return (
      <div className="space-y-4">
        <p className="alert-info">
          {t('ledgerMaster.editedElsewhere', { name: existing.name })}
        </p>
        <div className="flex justify-end">
          <button type="button" className="btn-secondary btn-sm" onClick={onClose}>
            {t('common.close')}
          </button>
        </div>
      </div>
    );
  }

  const canSave = name.trim() !== '' && groupId !== '' && !save.isPending;
  const showContact = kind === LedgerKind.bank || kind === LedgerKind.employee;

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (canSave) save.mutate();
      }}
    >
      {save.isError && <p className="alert-error">{save.error.detail}</p>}
      {toggle.isError && <p className="alert-error">{toggle.error.detail}</p>}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <TextField
          label={t('ledgerMaster.name')}
          required
          value={name}
          onChange={setName}
          autoFocus
          maxLength={200}
          className="sm:col-span-2"
        />
        {existing ? (
          <Field label={t('ledgerMaster.code')}>
            <p className="field-input bg-surface-2 font-mono text-ink-muted">
              {existing.code}
            </p>
          </Field>
        ) : (
          <TextField
            label={t('ledgerMaster.code')}
            hint={t('ledgerMaster.codeHint')}
            value={code}
            onChange={setCode}
            maxLength={30}
          />
        )}
        <TextField
          label={t('ledgerMaster.nameArabic')}
          value={nameArabic}
          onChange={setNameArabic}
          dir="rtl"
          maxLength={200}
        />
        {fixedKind === undefined && (
          <SelectField<number>
            label={t('ledgerMaster.kind')}
            value={kind}
            onChange={chooseKind}
            options={PLAIN_KINDS.map((value) => ({
              value,
              label: t(`ledgerKinds.${KIND_KEY[value]}`),
            }))}
          />
        )}
        <Field label={t('ledgerMaster.group')} required>
          <SearchSelect
            value={groupId}
            onChange={setGroupId}
            options={groupOptions}
            label={t('ledgerMaster.group')}
            placeholder={groups.isPending ? t('common.loading') : t('common.choose')}
            disabled={groups.isPending}
          />
        </Field>
        <NumberField
          label={t('ledgerMaster.openingBalance')}
          hint={t('ledgerMaster.openingHint')}
          value={opening}
          onChange={setOpening}
          min={0}
          step="0.01"
        />
        <SelectField<number>
          label={t('ledgerMaster.openingSide')}
          value={side}
          onChange={setSide}
          options={[
            { value: EntrySide.debit, label: t('reports.debit') },
            { value: EntrySide.credit, label: t('reports.credit') },
          ]}
        />
        {showContact && (
          <>
            <TextField
              label={t('ledgerMaster.phone')}
              type="tel"
              value={phone}
              onChange={setPhone}
              maxLength={30}
            />
            <TextField
              label={t('ledgerMaster.email')}
              type="email"
              value={email}
              onChange={setEmail}
              maxLength={200}
            />
            <TextField
              label={
                kind === LedgerKind.bank
                  ? t('ledgerMaster.bankDetails')
                  : t('ledgerMaster.address')
              }
              value={address}
              onChange={setAddress}
              maxLength={200}
              className="sm:col-span-2"
            />
          </>
        )}
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2 border-t border-line pt-4">
        {existing ? (
          <button
            type="button"
            className="btn-ghost btn-sm text-red-600 dark:text-red-400"
            disabled={toggle.isPending}
            onClick={() => toggle.mutate()}
          >
            {existing.isActive ? t('ledgerMaster.withdraw') : t('ledgerMaster.restore')}
          </button>
        ) : (
          <span />
        )}
        <div className="flex gap-2">
          <button type="button" className="btn-secondary btn-sm" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" className="btn-primary btn-sm" disabled={!canSave}>
            {save.isPending && <Spinner />}
            {save.isPending ? t('common.saving') : t('common.save')}
          </button>
        </div>
      </div>
    </form>
  );
}

/** Opens a group under another, or renames one. */
export function GroupEditor({
  group,
  onClose,
}: {
  /** The group to rename, or null to open a new one. */
  readonly group: AccountGroupSummary | null;
  readonly onClose: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const invalidate = useInvalidateLedgers();
  const groups = useAccountGroups();
  const paths = useMemo(() => groupPaths(groups.data ?? []), [groups.data]);
  const [name, setName] = useState(group?.name ?? '');
  const [nameArabic, setNameArabic] = useState(group?.nameArabic ?? '');
  const [parentId, setParentId] = useState('');
  const [code, setCode] = useState('');

  const save = useMutation<AccountGroupSummary, ApiError>({
    mutationFn: () =>
      group
        ? renameAccountGroup(group.accountGroupId, {
            name: name.trim(),
            nameArabic: nameArabic.trim() || null,
          })
        : createAccountGroup({
            code: code.trim() || null,
            name: name.trim(),
            nameArabic: nameArabic.trim() || null,
            parentId,
          }),
    onSuccess: async () => {
      await invalidate();
      onClose();
    },
  });

  const canSave =
    name.trim() !== '' && (group !== null || parentId !== '') && !save.isPending;

  return (
    <Modal
      title={group ? t('ledgerMaster.renameGroup') : t('ledgerMaster.newGroup')}
      onClose={onClose}
      size="form"
    >
      <form
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault();
          if (canSave) save.mutate();
        }}
      >
        {save.isError && <p className="alert-error">{save.error.detail}</p>}
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <TextField
            label={t('ledgerMaster.groupName')}
            required
            value={name}
            onChange={setName}
            autoFocus
            maxLength={150}
            className="sm:col-span-2"
          />
          {!group && (
            <>
              <Field
                label={t('ledgerMaster.parentGroup')}
                required
                hint={t('ledgerMaster.parentHint')}
              >
                <SearchSelect
                  value={parentId}
                  onChange={setParentId}
                  options={(groups.data ?? []).map((g) => ({
                    value: g.accountGroupId,
                    label: paths.get(g.accountGroupId) ?? g.name,
                  }))}
                  label={t('ledgerMaster.parentGroup')}
                  placeholder={t('common.choose')}
                />
              </Field>
              <TextField
                label={t('ledgerMaster.code')}
                hint={t('ledgerMaster.codeHint')}
                value={code}
                onChange={setCode}
                maxLength={20}
              />
            </>
          )}
          <TextField
            label={t('ledgerMaster.nameArabic')}
            value={nameArabic}
            onChange={setNameArabic}
            dir="rtl"
            maxLength={150}
            className="sm:col-span-2"
          />
        </div>
        <div className="flex justify-end gap-2 border-t border-line pt-4">
          <button type="button" className="btn-secondary btn-sm" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" className="btn-primary btn-sm" disabled={!canSave}>
            {save.isPending && <Spinner />}
            {save.isPending ? t('common.saving') : t('common.save')}
          </button>
        </div>
      </form>
    </Modal>
  );
}
