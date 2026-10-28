import { cloneElement, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { TextField } from '@/components/Form';
import { IconPlus } from '@/components/icons';
import { LedgerEditor } from '@/components/LedgerEditors';
import { Modal, ModalButton } from '@/components/Modal';
import type { ApiError } from '@/lib/api';
import { createCustomer } from '@/lib/customers';
import { createMaster } from '@/lib/inventory';
import { LedgerKind } from '@/lib/ledgers';
import { createSupplier } from '@/lib/suppliers';
import { useSession } from '@/stores/session';

/** The masters a document's dropdown can add to without leaving the document. */
export type QuickAddKind = 'customer' | 'supplier' | 'employee' | 'ledger' | 'warehouse';

const PERMISSION: Record<QuickAddKind, string> = {
  customer: 'sales:customer:create',
  supplier: 'purchase:supplier:create',
  employee: 'accounting:ledger:create',
  ledger: 'accounting:ledger:create',
  warehouse: 'inventory:warehouse:create',
};

/** The lists a new record of each master appears in, refreshed once it is saved. */
const LISTS: Record<QuickAddKind, readonly string[]> = {
  customer: ['customers', 'ledgers', 'note-parties'],
  supplier: ['suppliers', 'ledgers', 'note-parties'],
  employee: ['ledgers'],
  ledger: ['ledgers'],
  warehouse: ['warehouses'],
};

/**
 * A dropdown with a plus beside it that adds a record of the master it picks from,
 * and chooses the record once it is saved.
 *
 * Whoever is entering a sale finds the customer missing far more often than they set
 * out to maintain the customer list, and sending them to another screen loses the
 * sale half typed. The dropdown goes in as the child and is drawn joined to the plus,
 * so the pair is one control; the plus is left off for a user who may not create the
 * master, and the dropdown stands alone.
 */
export function QuickAdd({
  kind,
  onCreated,
  disabled = false,
  children,
}: {
  readonly kind: QuickAddKind;
  /** Chooses the record just added. */
  readonly onCreated: (id: string) => void;
  readonly disabled?: boolean;
  /** The dropdown: a `SearchSelect`, or anything else that can be drawn joined. */
  readonly children: React.ReactElement<{
    readonly joined?: boolean;
    readonly size?: 'default' | 'sm';
  }>;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const can = useSession((state) => state.can);
  const [open, setOpen] = useState(false);

  if (!can(PERMISSION[kind])) {
    return children;
  }

  const created = async (id: string): Promise<void> => {
    setOpen(false);
    await queryClient.invalidateQueries({
      predicate: (query) => LISTS[kind].includes(String(query.queryKey[0])),
    });
    onCreated(id);
  };

  return (
    <>
      <div className="flex min-w-0 items-stretch">
        <div className="min-w-0 flex-1">{cloneElement(children, { joined: true })}</div>
        <button
          type="button"
          onClick={() => setOpen(true)}
          disabled={disabled}
          // As tall as the dropdown it is joined to: a small one in a dense header
          // takes a small button.
          className={
            children.props.size === 'sm'
              ? 'btn-secondary btn-sm -ms-px shrink-0 rounded-s-none px-2'
              : 'btn-secondary -ms-px shrink-0 rounded-s-none px-2.5'
          }
          title={t(`masterPicker.add.${kind}`)}
          aria-label={t(`masterPicker.add.${kind}`)}
        >
          <IconPlus className="size-4" />
        </button>
      </div>

      {open &&
        (kind === 'employee' || kind === 'ledger' ? (
          // A ledger needs its group and kind, which the ledger master's own form asks.
          <LedgerEditor
            ledgerId={null}
            {...(kind === 'employee' ? { kind: LedgerKind.employee } : {})}
            onClose={() => setOpen(false)}
            onSaved={(ledger) => void created(ledger.ledgerId)}
          />
        ) : (
          <QuickAddForm
            kind={kind}
            onClose={() => setOpen(false)}
            onCreated={(id) => void created(id)}
          />
        ))}
    </>
  );
}

/** The short form behind the plus: a code that may be left to the system, and a name. */
function QuickAddForm({
  kind,
  onClose,
  onCreated,
}: {
  readonly kind: 'customer' | 'supplier' | 'warehouse';
  readonly onClose: () => void;
  readonly onCreated: (id: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [nameArabic, setNameArabic] = useState('');
  const [mobile, setMobile] = useState('');

  const party = kind !== 'warehouse';

  const save = useMutation<string, ApiError>({
    mutationFn: async () => {
      const common = {
        code: code.trim(),
        name: name.trim(),
        nameArabic: nameArabic.trim() || null,
      };

      if (kind === 'customer') {
        return (
          await createCustomer({
            ...common,
            ...(mobile.trim() ? { contact: { mobileNumber: mobile.trim() } } : {}),
          })
        ).customerId;
      }

      if (kind === 'supplier') {
        return (
          await createSupplier({
            ...common,
            ...(mobile.trim() ? { contact: { mobileNumber: mobile.trim() } } : {}),
          })
        ).supplierId;
      }

      return createMaster('warehouses', { ...common, code: common.code || null });
    },
    onSuccess: onCreated,
  });

  const submit = (): void => {
    if (name.trim() && !save.isPending) save.mutate();
  };

  return (
    <Modal title={t(`masterPicker.add.${kind}`)} size="form" onClose={onClose}>
      {/* Not a form: this opens over documents that are forms themselves, and a submit
          would reach theirs as well. Enter saves all the same. */}
      <div
        className="space-y-3"
        onKeyDown={(event) => {
          if (event.key === 'Enter' && !event.nativeEvent.isComposing) {
            event.preventDefault();
            event.stopPropagation();
            submit();
          }
        }}
      >
        <div className="grid gap-3 sm:grid-cols-2">
          <TextField
            label={t('masterPicker.code')}
            value={code}
            onChange={setCode}
            placeholder={t('masterPicker.codeAutomatic')}
          />
          <TextField
            label={t('masterPicker.name')}
            value={name}
            onChange={setName}
            required
            autoFocus
          />
          <TextField
            label={t('masterPicker.nameArabic')}
            value={nameArabic}
            onChange={setNameArabic}
            dir="rtl"
          />
          {party && (
            <TextField
              label={t('masterPicker.mobile')}
              value={mobile}
              onChange={setMobile}
              type="tel"
            />
          )}
        </div>

        {save.isError && (
          <p role="alert" className="alert-error">
            {save.error.detail || save.error.code}
          </p>
        )}

        <div className="form-actions flex justify-end gap-2">
          <ModalButton onClick={onClose}>{t('common.cancel')}</ModalButton>
          <ModalButton
            primary
            onClick={submit}
            disabled={save.isPending || name.trim() === ''}
            shortcut="save"
          >
            {save.isPending ? t('common.saving') : t('common.save')}
          </ModalButton>
        </div>
      </div>
    </Modal>
  );
}
