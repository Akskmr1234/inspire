import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { MasterFrame, RowAction } from '@/components/MasterFrame';
import { ArabicNameField } from '@/components/ArabicNameField';
import { Field, NumberField, TextField } from '@/components/Form';
import { SearchSelect } from '@/components/SearchSelect';
import type { GridColumn } from '@/components/DataGrid';
import {
  createCustomer,
  listCustomers,
  setCustomerActive,
  updateCustomer,
  type CustomerSummary,
} from '@/lib/customers';
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
 * The customer master of section 12.1.
 *
 * A customer is a sub-ledger rather than a record beside one, which is why this screen
 * lives under sales and not under the chart of accounts: an invoice is billed to a
 * ledger, a receipt settles against one, and the debtors report sums them.
 *
 * Nothing here deletes. A customer with history is what every past invoice points at, so
 * withdrawing one stops new documents naming them and leaves the trail whole.
 */
export function CustomersPage(): React.JSX.Element {
  const { t } = useTranslation();
  const regime = useSettings((state) => state.taxRegime);

  const columns = (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
    edit: (row: CustomerSummary) => void,
  ): readonly GridColumn<CustomerSummary>[] => [
    { key: 'code', header: t('masters.code'), value: (row) => row.code },
    { key: 'name', header: t('masters.name'), value: (row) => row.name },
    {
      key: 'nameArabic',
      header: t('masters.nameArabic'),
      value: (row) => row.nameArabic ?? '',
      hiddenByDefault: true,
    },
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
    },
    {
      key: 'state',
      header: t('customers.state'),
      // The field that decides IGST against CGST plus SGST on a GST firm, so it is
      // shown rather than buried behind the column picker — and shown by name, not
      // as the two-digit code somebody would have to look up.
      value: (row) => stateName(regime, row.taxDetails.stateCode),
    },
    {
      key: 'registration',
      header: t('customers.registrationNumber'),
      value: (row) => row.taxDetails.registrationNumber ?? '',
      hiddenByDefault: true,
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
                await setCustomerActive(row.customerId, !row.isActive);
              })
            }
          />
        </span>
      ),
    },
  ];

  return (
    <MasterFrame<CustomerSummary>
      title={t('nav.customers')}
      addTitle={t('masters.newCustomer')}
      editTitle={t('customers.edit')}
      queryKey="customers"
      fetchRows={(includeInactive) => listCustomers('', !includeInactive)}
      columns={columns}
      rowKey={(row) => row.customerId}
      addForm={(run, busy, rows) => <CustomerForm run={run} busy={busy} rows={rows} />}
      editForm={(row, run, busy, rows) => (
        <CustomerForm run={run} busy={busy} rows={rows} existing={row} />
      )}
    />
  );
}

/** What the form holds while it is being filled in. */
interface CustomerDraft {
  code: string;
  name: string;
  nameArabic: string;
  mobile: string;
  phone: string;
  email: string;
  address1: string;
  address2: string;
  creditDays: string;
  creditLimit: string;
  stateCode: string;
  registrationNumber: string;
  openingBalance: string;
}

function draftFrom(existing: CustomerSummary | undefined): CustomerDraft {
  return {
    code: existing?.code ?? '',
    name: existing?.name ?? '',
    nameArabic: existing?.nameArabic ?? '',
    mobile: existing?.contact.mobileNumber ?? '',
    phone: existing?.contact.phone ?? '',
    email: existing?.contact.email ?? '',
    address1: existing?.contact.addressLine1 ?? '',
    address2: existing?.contact.addressLine2 ?? '',
    creditDays:
      existing?.terms.creditDays === null ? '' : String(existing?.terms.creditDays ?? 30),
    creditLimit:
      existing?.terms.creditLimit === null || existing?.terms.creditLimit === undefined
        ? ''
        : String(existing.terms.creditLimit),
    stateCode: existing?.taxDetails.stateCode ?? '',
    registrationNumber: existing?.taxDetails.registrationNumber ?? '',
    openingBalance: existing ? String(existing.openingBalance) : '',
  };
}

/**
 * The customer form, for both creating one and changing one.
 *
 * One component rather than two, because the two forms differ in exactly two fields:
 * the code and the opening balance are stated once when the record is created and
 * refused afterwards — the code is what a firm's own paperwork and any imported
 * history refer to the customer by, and the opening balance is a fact about the day
 * the books were taken on, which does not change because somebody edits an address.
 */
function CustomerForm({
  run,
  busy,
  rows,
  existing,
}: {
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
  readonly rows: readonly CustomerSummary[];
  readonly existing?: CustomerSummary;
}): React.JSX.Element {
  const { t } = useTranslation();
  const regime = useSettings((state) => state.taxRegime);

  /*
    The next code in the firm's own sequence, offered rather than demanded.

    The specification's answer to a counter creating a customer while somebody waits:
    the code is not mandatory, and leaving it blank means "the next one". It is
    filled in here rather than left empty so the reader can see what they are about
    to get and change it, and it is recomputed at submission for the case where it
    was cleared. The product master already works this way — the difference was that
    a product's code is issued by the server and a customer's was not, so this
    screen has to do the counting.
  */
  const suggested = useMemo(
    () =>
      nextCode(
        rows.map((row) => row.code),
        'C',
        4,
      ),
    [rows],
  );

  const [draft, setDraft] = useState<CustomerDraft>(() => ({
    ...draftFrom(existing),
    code: existing?.code ?? suggested,
  }));

  const states = statesFor(regime);

  const { errors, submit } = useValidation<CustomerDraft>((values) =>
    collect({
      name:
        required(values.name, t('customers.nameRequired')) ??
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
      // Only where the regime makes it decide something. A firm charging no tax at
      // all has no state table to choose from, and demanding a state it cannot
      // offer would be a form nobody can submit.
      stateCode:
        regime === TaxRegime.indiaGst && values.stateCode.trim() === ''
          ? t('customers.stateRequired')
          : null,
    }),
  );

  const set = <TField extends keyof CustomerDraft>(
    field: TField,
    value: CustomerDraft[TField],
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
      registrationNumber: draft.registrationNumber.trim() || null,
      stateCode: draft.stateCode.trim() || null,
    };

    run(async () => {
      if (existing) {
        await updateCustomer(existing.customerId, {
          name: draft.name.trim(),
          nameArabic: draft.nameArabic.trim() || null,
          contact,
          terms,
          taxDetails,
        });

        return;
      }

      await createCustomer({
        // Blank means "issue the next one", which is what the suggestion above
        // already is. Recomputed here rather than trusted from state so a cleared
        // box still produces a code.
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

        {/*
          A dropdown rather than a text box, and the reason is arithmetic rather
          than tidiness: under GST this field decides whether an invoice charges
          IGST or CGST plus SGST, and "TN", "T.N." and "Tamilnadu" typed on three
          different days are three different answers to that question.
        */}
        <Field
          label={t('customers.state')}
          required={regime === TaxRegime.indiaGst}
          error={errors['stateCode']}
          hint={
            states.length === 0 ? t('customers.stateNoRegime') : t('customers.stateHint')
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

        <TextField
          label={t('customers.registrationNumber')}
          value={draft.registrationNumber}
          onChange={(value) => set('registrationNumber', value)}
        />

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
            hint={t('customers.openingHint')}
            min={0}
            value={draft.openingBalance}
            onChange={(value) => set('openingBalance', value)}
            error={errors['openingBalance']}
            placeholder="0.00"
          />
        )}
      </div>

      <div className="form-actions">
        {/*
          Live rather than disabled until the form is complete. A dead button says
          nothing about which of thirteen fields it is waiting for; pressing this one
          marks them.
        */}
        <button type="submit" disabled={busy} className="btn-primary">
          {busy ? t('common.saving') : existing ? t('common.save') : t('masters.add')}
        </button>
      </div>
    </form>
  );
}
