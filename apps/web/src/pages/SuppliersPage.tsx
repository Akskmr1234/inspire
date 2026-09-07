import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { MasterFrame, RowAction } from '@/components/MasterFrame';
import { ArabicNameField } from '@/components/ArabicNameField';
import { Field, NumberField, TextField } from '@/components/Form';
import { SearchSelect } from '@/components/SearchSelect';
import type { GridColumn } from '@/components/DataGrid';
import {
  createSupplier,
  listSuppliers,
  setSupplierActive,
  updateSupplier,
  type SupplierSummary,
} from '@/lib/suppliers';
import { stateName, statesFor, TaxRegime } from '@/lib/states';
import {
  collect,
  maxLength,
  nextCode,
  numeric,
  required,
  useValidation,
} from '@/lib/validation';
import { useSettings } from '@/stores/settings';

/**
 * The supplier master. A peer of the customer master of §12.1, which is what it is
 * modelled on: both are sub-ledgers, and both are reached from the work rather than
 * from the chart of accounts.
 *
 * A supplier is a sub-ledger rather than a record beside one, which is why this screen
 * lives under purchase and not under the chart of accounts: a purchase is billed by a
 * ledger, a payment settles against one, and the creditors report sums them.
 *
 * Nothing here deletes. A supplier with history is what every past purchase points at, so
 * withdrawing one stops new documents naming them and leaves the trail whole.
 */
export function SuppliersPage(): React.JSX.Element {
  const { t } = useTranslation();
  const regime = useSettings((state) => state.taxRegime);

  const columns = (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
    edit: (row: SupplierSummary) => void,
  ): readonly GridColumn<SupplierSummary>[] => [
    { key: 'code', header: t('masters.code'), value: (row) => row.code },
    { key: 'name', header: t('masters.name'), value: (row) => row.name },
    {
      key: 'mobile',
      header: t('customers.mobile'),
      value: (row) => row.contact.mobileNumber ?? '',
    },
    {
      key: 'email',
      header: t('customers.email'),
      value: (row) => row.contact.email ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'address',
      header: t('customers.address'),
      value: (row) => row.contact.addressLine1 ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'registration',
      header: t('suppliers.registration'),
      // What an input tax reclaim is made against, so it is shown rather than buried
      // behind the column picker.
      value: (row) => row.taxDetails.registrationNumber ?? '',
    },
    {
      key: 'creditDays',
      header: t('customers.creditDays'),
      value: (row) => row.terms.creditDays ?? '',
      numeric: true,
    },
    {
      key: 'creditLimit',
      header: t('customers.creditLimit'),
      value: (row) => row.terms.creditLimit ?? '',
      numeric: true,
      hiddenByDefault: true,
    },
    {
      key: 'state',
      header: t('customers.state'),
      value: (row) => stateName(regime, row.taxDetails.stateCode),
    },
    {
      key: 'status',
      header: t('masters.status'),
      value: (row) => (row.isActive ? t('masters.active') : t('masters.withdrawn')),
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <span className="inline-flex gap-1.5">
          <RowAction label={t('common.edit')} disabled={busy} onClick={() => edit(row)} />
          <RowAction
            label={row.isActive ? t('masters.withdraw') : t('masters.restore')}
            tone={row.isActive ? 'danger' : 'neutral'}
            disabled={busy}
            onClick={() =>
              run(async () => {
                await setSupplierActive(row.supplierId, !row.isActive);
              })
            }
          />
        </span>
      ),
    },
  ];

  return (
    <MasterFrame<SupplierSummary>
      title={t('nav.suppliers')}
      addTitle={t('masters.newSupplier')}
      editTitle={t('suppliers.edit')}
      queryKey="suppliers"
      fetchRows={(includeInactive) => listSuppliers('', !includeInactive)}
      columns={columns}
      rowKey={(row) => row.supplierId}
      addForm={(run, busy, rows) => <SupplierForm run={run} busy={busy} rows={rows} />}
      editForm={(row, run, busy, rows) => (
        <SupplierForm run={run} busy={busy} rows={rows} existing={row} />
      )}
    />
  );
}

/** What the form holds while it is being filled in. */
interface SupplierDraft {
  code: string;
  name: string;
  nameArabic: string;
  mobile: string;
  phone: string;
  email: string;
  address1: string;
  address2: string;
  registration: string;
  creditDays: string;
  creditLimit: string;
  stateCode: string;
  openingBalance: string;
}

function draftFrom(existing: SupplierSummary | undefined): SupplierDraft {
  return {
    code: existing?.code ?? '',
    name: existing?.name ?? '',
    nameArabic: existing?.nameArabic ?? '',
    mobile: existing?.contact.mobileNumber ?? '',
    phone: existing?.contact.phone ?? '',
    email: existing?.contact.email ?? '',
    address1: existing?.contact.addressLine1 ?? '',
    address2: existing?.contact.addressLine2 ?? '',
    registration: existing?.taxDetails.registrationNumber ?? '',
    creditDays:
      existing?.terms.creditDays === null ? '' : String(existing?.terms.creditDays ?? 30),
    creditLimit:
      existing?.terms.creditLimit === null || existing?.terms.creditLimit === undefined
        ? ''
        : String(existing.terms.creditLimit),
    stateCode: existing?.taxDetails.stateCode ?? '',
    openingBalance: existing ? String(existing.openingBalance) : '',
  };
}

/** The supplier form, for both creating one and changing one. */
function SupplierForm({
  run,
  busy,
  rows,
  existing,
}: {
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
  readonly rows: readonly SupplierSummary[];
  readonly existing?: SupplierSummary;
}): React.JSX.Element {
  const { t } = useTranslation();
  const regime = useSettings((state) => state.taxRegime);

  // The next code in the firm's own sequence. Offered, editable, and recomputed at
  // submission for the case where the box was cleared — the same arrangement the
  // customer master uses, and the same reason: a code is not something anybody
  // should have to invent while a supplier's delivery note is in their other hand.
  const suggested = useMemo(
    () =>
      nextCode(
        rows.map((row) => row.code),
        'S',
        4,
      ),
    [rows],
  );

  const [draft, setDraft] = useState<SupplierDraft>(() => ({
    ...draftFrom(existing),
    code: existing?.code ?? suggested,
  }));

  const states = statesFor(regime);

  const { errors, submit } = useValidation<SupplierDraft>((values) =>
    collect({
      name:
        required(values.name, t('suppliers.nameRequired')) ??
        maxLength(values.name, 200, t('customers.nameTooLong')),
      code: maxLength(values.code, 30, t('customers.codeTooLong')),
      email:
        values.email.trim() !== '' && !values.email.includes('@')
          ? t('customers.emailInvalid')
          : null,
      creditDays: numeric(values.creditDays, t('customers.creditDaysInvalid'), {
        min: 0,
        integer: true,
      }),
      creditLimit: numeric(values.creditLimit, t('customers.creditLimitInvalid'), {
        min: 0,
      }),
      openingBalance: numeric(values.openingBalance, t('customers.openingInvalid'), {
        min: 0,
      }),
      stateCode:
        regime === TaxRegime.indiaGst && values.stateCode.trim() === ''
          ? t('suppliers.stateRequired')
          : null,
    }),
  );

  const set = <TField extends keyof SupplierDraft>(
    field: TField,
    value: SupplierDraft[TField],
  ): void => setDraft((current) => ({ ...current, [field]: value }));

  const save = (): void => {
    if (!submit(draft)) {
      return;
    }

    const contact = {
      mobileNumber: draft.mobile.trim() || null,
      phone: draft.phone.trim() || null,
      email: draft.email.trim() || null,
      addressLine1: draft.address1.trim() || null,
      addressLine2: draft.address2.trim() || null,
    };

    const terms = {
      creditDays: draft.creditDays.trim() === '' ? null : Number(draft.creditDays),
      creditLimit: draft.creditLimit.trim() === '' ? null : Number(draft.creditLimit),
      isBillWise: true,
    };

    const taxDetails = {
      registrationNumber: draft.registration.trim() || null,
      stateCode: draft.stateCode.trim() || null,
    };

    run(async () => {
      if (existing) {
        await updateSupplier(existing.supplierId, {
          name: draft.name.trim(),
          nameArabic: draft.nameArabic.trim() || null,
          contact,
          terms,
          taxDetails,
        });

        return;
      }

      await createSupplier({
        code: draft.code.trim() || suggested,
        name: draft.name.trim(),
        nameArabic: draft.nameArabic.trim() || null,
        contact,
        terms,
        taxDetails,
        openingBalance:
          draft.openingBalance.trim() === '' ? 0 : Number(draft.openingBalance),
      });
    });
  };

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        save();
      }}
    >
      <div className="form-grid">
        {existing ? (
          <Field label={t('masters.code')} hint={t('customers.codeFixed')}>
            <input value={existing.code} disabled className="field-input" />
          </Field>
        ) : (
          <TextField
            label={t('masters.code')}
            hint={t('customers.codeAuto')}
            value={draft.code}
            onChange={(value) => set('code', value)}
            error={errors['code']}
            placeholder={suggested}
          />
        )}

        <TextField
          label={t('masters.name')}
          required
          autoFocus
          value={draft.name}
          onChange={(value) => set('name', value)}
          error={errors['name']}
        />

        <ArabicNameField
          label={t('masters.nameArabic')}
          source={draft.name}
          value={draft.nameArabic}
          onChange={(value) => set('nameArabic', value)}
        />

        <TextField
          label={t('customers.mobile')}
          type="tel"
          value={draft.mobile}
          onChange={(value) => set('mobile', value)}
        />

        <TextField
          label={t('customers.phone')}
          type="tel"
          value={draft.phone}
          onChange={(value) => set('phone', value)}
        />

        <TextField
          label={t('customers.email')}
          type="email"
          value={draft.email}
          onChange={(value) => set('email', value)}
          error={errors['email']}
        />

        <TextField
          label={t('customers.address')}
          value={draft.address1}
          onChange={(value) => set('address1', value)}
        />

        <TextField
          label={t('customers.address2')}
          value={draft.address2}
          onChange={(value) => set('address2', value)}
        />

        <TextField
          label={t('suppliers.registration')}
          hint={t('suppliers.registrationHint')}
          value={draft.registration}
          onChange={(value) => set('registration', value)}
        />

        <Field
          label={t('customers.state')}
          required={regime === TaxRegime.indiaGst}
          error={errors['stateCode']}
          hint={
            states.length === 0 ? t('customers.stateNoRegime') : t('suppliers.stateHint')
          }
        >
          <SearchSelect
            value={draft.stateCode}
            onChange={(value) => set('stateCode', value)}
            options={states.map((state) => ({
              value: state.code,
              label: `${state.code} — ${state.name}`,
            }))}
            disabled={states.length === 0}
            clearable
            invalid={errors['stateCode'] !== undefined}
            label={t('customers.state')}
            placeholder={t('customers.noState')}
          />
        </Field>

        <NumberField
          label={t('customers.creditDays')}
          min={0}
          step="1"
          value={draft.creditDays}
          onChange={(value) => set('creditDays', value)}
          error={errors['creditDays']}
        />

        <NumberField
          label={t('customers.creditLimit')}
          min={0}
          value={draft.creditLimit}
          onChange={(value) => set('creditLimit', value)}
          error={errors['creditLimit']}
          placeholder={t('customers.noLimit')}
        />

        {!existing && (
          <NumberField
            label={t('customers.openingBalance')}
            hint={t('suppliers.openingHint')}
            min={0}
            value={draft.openingBalance}
            onChange={(value) => set('openingBalance', value)}
            error={errors['openingBalance']}
            placeholder="0.00"
          />
        )}
      </div>

      <div className="form-actions">
        <button type="submit" disabled={busy} className="btn-primary">
          {busy ? t('common.saving') : existing ? t('common.save') : t('masters.add')}
        </button>
      </div>
    </form>
  );
}
