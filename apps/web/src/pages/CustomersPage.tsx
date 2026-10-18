import { useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import clsx from 'clsx';
import { MasterFrame, RowAction } from '@/components/MasterFrame';
import { ArabicNameField } from '@/components/ArabicNameField';
import {
  CheckField,
  Field,
  NumberField,
  SelectField,
  TextField,
} from '@/components/Form';
import { SearchSelect } from '@/components/SearchSelect';
import type { GridColumn } from '@/components/DataGrid';
import {
  createCustomer,
  getCustomerPhoto,
  listCustomers,
  setCustomerActive,
  setCustomerPhoto,
  updateCustomer,
  type CustomerProfile,
  type CustomerSummary,
} from '@/lib/customers';
import { TaxRegime } from '@/lib/states';
import {
  TerritoryKind,
  useCountries,
  useStates,
  useTerritories,
  type StateSummary,
} from '@/lib/places';
import { ActiveBadge } from '@/components/StatusBadge';
import type { ApiError } from '@/lib/api';
import {
  collect,
  maxLength,
  nextCode,
  numeric,
  required,
  useValidation,
} from '@/lib/validation';
import { useSettings } from '@/stores/settings';
import {
  savePrintSetup,
  usePrintSetup,
  type CustomerPrint,
  type PrintSetup,
} from '@/lib/printing';

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
  const navigate = useNavigate();
  const { setup: printSetup } = usePrintSetup();
  const countries = useCountries();
  const states = useStates();
  const areas = useTerritories(TerritoryKind.area);
  const routes = useTerritories(TerritoryKind.route);

  const templateName = (customerId: string): string | null => {
    const id = printSetup.customerPrint[customerId]?.template;
    return id
      ? (printSetup.templates.find((template) => template.id === id)?.name ?? null)
      : null;
  };

  const countryName = (code: string | null): string =>
    code ? (countries.find((country) => country.code === code)?.name ?? code) : '';

  // By the state master, falling back to the code for one it does not hold.
  const stateName = (code: string | null): string =>
    code ? (states.find((state) => state.code === code)?.name ?? code) : '';

  const territoryName = (
    id: string | null,
    from: readonly { id: string; name: string }[],
  ) => (id ? (from.find((row) => row.id === id)?.name ?? '') : '');

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
      key: 'contactPerson',
      header: t('customers.contactPerson'),
      value: (row) => row.profile.contactPerson ?? '',
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
      key: 'city',
      header: t('customers.city'),
      value: (row) => row.profile.city ?? '',
    },
    {
      key: 'area',
      header: t('customers.area'),
      value: (row) => territoryName(row.profile.areaId, areas),
      hiddenByDefault: true,
    },
    {
      key: 'route',
      header: t('customers.route'),
      value: (row) => territoryName(row.profile.routeId, routes),
      hiddenByDefault: true,
    },
    {
      key: 'state',
      header: t('customers.state'),
      value: (row) => stateName(row.taxDetails.stateCode),
    },
    {
      key: 'country',
      header: t('places.country'),
      value: (row) => countryName(row.profile.countryCode),
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
      key: 'openingBalance',
      header: t('customers.openingBalance'),
      value: (row) => row.openingBalance,
      numeric: true,
      hiddenByDefault: true,
    },
    {
      key: 'registration',
      header: t('customers.registrationNumber'),
      value: (row) => row.taxDetails.registrationNumber ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'gst',
      header: t('customers.gstNumber'),
      value: (row) => row.profile.gstNumber ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'sms',
      header: t('customers.sms'),
      value: (row) => (row.profile.sendsSms ? t('common.yes') : t('common.no')),
      hiddenByDefault: true,
    },
    {
      key: 'invoiceTemplate',
      header: t('settings.customerInvoiceTemplate'),
      value: (row) => templateName(row.customerId) ?? t('printing.followSettings'),
      render: (row) => {
        const assigned = templateName(row.customerId);
        return (
          <span
            className={clsx(
              'inline-flex items-center rounded px-2 py-0.5 text-xs font-medium',
              assigned
                ? 'bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-300 border border-brand-200 dark:border-brand-500/30'
                : 'text-ink-muted',
            )}
          >
            {assigned ?? t('printing.followSettings')}
          </span>
        );
      },
      hiddenByDefault: true,
    },
    {
      key: 'status',
      header: t('masters.status'),
      value: (row) => (row.isActive ? t('masters.active') : t('masters.withdrawn')),
      render: (row) => <ActiveBadge isActive={row.isActive} />,
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <span className="inline-flex gap-1.5">
          <RowAction label={t('common.edit')} disabled={busy} onClick={() => edit(row)} />
          <RowAction
            label={t('ledgerMaster.statement')}
            disabled={false}
            onClick={() =>
              navigate(`/accounting/ledger-statement?ledger=${row.customerId}`)
            }
          />
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
  contactPerson: string;
  mobile: string;
  phone: string;
  email: string;
  website: string;
  fax: string;
  address1: string;
  address2: string;
  city: string;
  pinCode: string;
  countryCode: string;
  stateCode: string;
  areaId: string;
  routeId: string;
  registrationNumber: string;
  gstNumber: string;
  tinNumber: string;
  cstNumber: string;
  creditDays: string;
  creditLimit: string;
  openingBalance: string;
  sendsSms: boolean;
  printTemplate: string;
  printCopies: string;
  printPreview: '' | 'yes' | 'no';
}

function draftFrom(
  existing: CustomerSummary | undefined,
  print: CustomerPrint | undefined,
  defaultCountry: string,
): CustomerDraft {
  const profile = existing?.profile;

  return {
    code: existing?.code ?? '',
    name: existing?.name ?? '',
    nameArabic: existing?.nameArabic ?? '',
    contactPerson: profile?.contactPerson ?? '',
    mobile: existing?.contact.mobileNumber ?? '',
    phone: existing?.contact.phone ?? '',
    email: existing?.contact.email ?? '',
    website: profile?.website ?? '',
    fax: profile?.fax ?? '',
    address1: existing?.contact.addressLine1 ?? '',
    address2: existing?.contact.addressLine2 ?? '',
    city: profile?.city ?? '',
    pinCode: profile?.pinCode ?? '',
    countryCode: existing ? (profile?.countryCode ?? '') : defaultCountry,
    stateCode: existing?.taxDetails.stateCode ?? '',
    areaId: profile?.areaId ?? '',
    routeId: profile?.routeId ?? '',
    registrationNumber: existing?.taxDetails.registrationNumber ?? '',
    gstNumber: profile?.gstNumber ?? '',
    tinNumber: profile?.tinNumber ?? '',
    cstNumber: profile?.cstNumber ?? '',
    creditDays:
      existing?.terms.creditDays === null ? '' : String(existing?.terms.creditDays ?? 30),
    creditLimit:
      existing?.terms.creditLimit === null || existing?.terms.creditLimit === undefined
        ? ''
        : String(existing.terms.creditLimit),
    openingBalance: existing ? String(existing.openingBalance) : '',
    sendsSms: profile?.sendsSms ?? false,
    printTemplate: print?.template ?? '',
    printCopies: print?.copies ? String(print.copies) : '',
    printPreview: print?.preview === true ? 'yes' : print?.preview === false ? 'no' : '',
  };
}

/** The country most of a firm's customers are in, which a new one most likely is too. */
function usualCountry(rows: readonly CustomerSummary[]): string {
  const counts = new Map<string, number>();

  for (const row of rows) {
    const code = row.profile.countryCode;
    if (code) counts.set(code, (counts.get(code) ?? 0) + 1);
  }

  return [...counts.entries()].sort((a, b) => b[1] - a[1])[0]?.[0] ?? '';
}

/** Scales a picture down to a small JPEG, which is all a customer card needs. */
function shrinkPhoto(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new Error('unreadable'));
    reader.onload = () => {
      const image = new Image();
      image.onerror = () => reject(new Error('not a picture'));
      image.onload = () => {
        const scale = Math.min(1, 320 / Math.max(image.width, image.height));
        const canvas = document.createElement('canvas');
        canvas.width = Math.round(image.width * scale);
        canvas.height = Math.round(image.height * scale);
        canvas.getContext('2d')?.drawImage(image, 0, 0, canvas.width, canvas.height);
        resolve(canvas.toDataURL('image/jpeg', 0.85));
      };
      image.src = String(reader.result);
    };
    reader.readAsDataURL(file);
  });
}

/** A heading between the groups of fields in a long form. */
function Section({ title }: { readonly title: string }): React.JSX.Element {
  return (
    <h3 className="col-span-full border-b border-line pt-2 pb-1 text-xs font-semibold tracking-wider text-ink-muted uppercase">
      {title}
    </h3>
  );
}

/**
 * The customer form, for both creating one and changing one.
 *
 * In sections, because a customer now carries a good deal more than a name and a phone
 * number: where they are (country, then a state of that country, city, area and route),
 * their registration numbers, their terms, how their documents print, and a photograph.
 * The code is stated once, when the record is created; everything else, the opening
 * amount included, can be corrected later.
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
  const settings = useSettings();
  const regime = settings.taxRegime;
  const queryClient = useQueryClient();
  const { setup: printSetup } = usePrintSetup();
  const countries = useCountries();
  const allStates = useStates();
  const areas = useTerritories(TerritoryKind.area);
  const routes = useTerritories(TerritoryKind.route);
  const fileInput = useRef<HTMLInputElement>(null);

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
    ...draftFrom(
      existing,
      existing ? printSetup.customerPrint[existing.customerId] : undefined,
      usualCountry(rows),
    ),
    code: existing?.code ?? suggested,
  }));

  // The photograph is read on its own, so the list of customers stays light.
  const savedPhoto = useQuery<string | null, ApiError>({
    queryKey: ['customer-photo', existing?.customerId],
    queryFn: () => getCustomerPhoto(existing!.customerId),
    enabled: existing !== undefined,
  });
  // Undefined until somebody picks or removes one.
  const [photo, setPhoto] = useState<string | null | undefined>(undefined);
  const [photoError, setPhotoError] = useState<string | null>(null);
  const shownPhoto = photo === undefined ? (savedPhoto.data ?? null) : photo;

  // A customer's state is one of their country's.
  const states: readonly StateSummary[] = useMemo(
    () => allStates.filter((state) => state.countryCode === draft.countryCode),
    [allStates, draft.countryCode],
  );

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
      printCopies: numeric(values.printCopies, t('customers.copiesInvalid'), {
        min: 1,
        integer: true,
      }),
      // Only where the regime makes it decide something: under GST it chooses IGST
      // against CGST plus SGST.
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

  // Saved with the firm's print setup, so every counter prints their documents alike.
  const savePrint = async (customerId: string): Promise<void> => {
    const wanted: CustomerPrint = {
      template: draft.printTemplate,
      copies: draft.printCopies.trim() === '' ? null : Number(draft.printCopies),
      preview: draft.printPreview === '' ? null : draft.printPreview === 'yes',
    };
    const current = printSetup.customerPrint[customerId];
    const blank = !wanted.template && wanted.copies === null && wanted.preview === null;

    if (
      (blank && current === undefined) ||
      (current !== undefined &&
        current.template === wanted.template &&
        current.copies === wanted.copies &&
        current.preview === wanted.preview)
    ) {
      return;
    }

    const customerPrint = { ...printSetup.customerPrint };
    if (blank) delete customerPrint[customerId];
    else customerPrint[customerId] = wanted;

    const next: PrintSetup = { ...printSetup, customerPrint };
    await savePrintSetup(next);
    void queryClient.invalidateQueries({ queryKey: ['print-setup'] });
  };

  const savePhoto = async (customerId: string): Promise<void> => {
    if (photo === undefined) return;

    await setCustomerPhoto(customerId, photo);
    void queryClient.invalidateQueries({ queryKey: ['customer-photo', customerId] });
  };

  const save = (): void => {
    if (!submit(draft)) {
      return;
    }

    const blank = (value: string): string | null => value.trim() || null;

    const contact = {
      mobileNumber: blank(draft.mobile),
      phone: blank(draft.phone),
      email: blank(draft.email),
      addressLine1: blank(draft.address1),
      addressLine2: blank(draft.address2),
    };

    const terms = {
      creditDays: draft.creditDays.trim() === '' ? null : Number(draft.creditDays),
      creditLimit: draft.creditLimit.trim() === '' ? null : Number(draft.creditLimit),
      isBillWise: true,
    };

    const taxDetails = {
      registrationNumber: blank(draft.registrationNumber),
      stateCode: blank(draft.stateCode),
    };

    const profile: CustomerProfile = {
      city: blank(draft.city),
      pinCode: blank(draft.pinCode),
      website: blank(draft.website),
      fax: blank(draft.fax),
      contactPerson: blank(draft.contactPerson),
      gstNumber: blank(draft.gstNumber),
      tinNumber: blank(draft.tinNumber),
      cstNumber: blank(draft.cstNumber),
      countryCode: blank(draft.countryCode),
      areaId: draft.areaId || null,
      routeId: draft.routeId || null,
      sendsSms: draft.sendsSms,
    };

    const openingBalance =
      draft.openingBalance.trim() === '' ? 0 : Number(draft.openingBalance);

    run(async () => {
      if (existing) {
        await updateCustomer(existing.customerId, {
          name: draft.name.trim(),
          nameArabic: blank(draft.nameArabic),
          contact,
          terms,
          taxDetails,
          profile,
          openingBalance,
        });

        await savePrint(existing.customerId);
        await savePhoto(existing.customerId);

        return;
      }

      const created = await createCustomer({
        code: draft.code.trim() || suggested,
        name: draft.name.trim(),
        nameArabic: blank(draft.nameArabic),
        contact,
        terms,
        taxDetails,
        profile,
        openingBalance,
      });

      if (created?.customerId) {
        await savePrint(created.customerId);
        await savePhoto(created.customerId);
      }
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
        <Section title={t('customers.sectionGeneral')} />

        {existing ? (
          <Field label={t('masters.code')}>
            <input value={existing.code} disabled className="field-input" />
          </Field>
        ) : (
          <TextField
            label={t('masters.code')}
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
          label={t('customers.contactPerson')}
          value={draft.contactPerson}
          onChange={(value) => set('contactPerson', value)}
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
          label={t('customers.website')}
          value={draft.website}
          onChange={(value) => set('website', value)}
        />

        <TextField
          label={t('customers.fax')}
          type="tel"
          value={draft.fax}
          onChange={(value) => set('fax', value)}
        />

        <CheckField
          label={t('customers.sendSms')}
          checked={draft.sendsSms}
          onChange={(checked) => set('sendsSms', checked)}
        />

        <Section title={t('customers.sectionAddress')} />

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
          label={t('customers.city')}
          value={draft.city}
          onChange={(value) => set('city', value)}
        />

        <TextField
          label={t('customers.pinCode')}
          value={draft.pinCode}
          onChange={(value) => set('pinCode', value)}
        />

        {/* The country first: it decides which states are offered. */}
        <Field label={t('places.country')}>
          <SearchSelect
            value={draft.countryCode}
            onChange={(code) =>
              setDraft((current) => ({
                ...current,
                countryCode: code,
                // A state of the old country is no state of the new one.
                stateCode: allStates.some(
                  (state) =>
                    state.code === current.stateCode && state.countryCode === code,
                )
                  ? current.stateCode
                  : '',
              }))
            }
            options={countries.map((country) => ({
              value: country.code,
              label: country.name,
              meta: country.currencyCode ?? country.code,
            }))}
            clearable
            label={t('places.country')}
            placeholder={t('common.choose')}
          />
        </Field>

        <Field
          label={t('customers.state')}
          required={regime === TaxRegime.indiaGst}
          error={errors['stateCode']}
        >
          <SearchSelect
            value={draft.stateCode}
            onChange={(value) => set('stateCode', value)}
            options={[
              ...states.map((state) => ({
                value: state.code,
                label: state.name,
                meta: state.code,
              })),
              // A code saved before the state list existed is kept rather than lost.
              ...(draft.stateCode &&
              !states.some((state) => state.code === draft.stateCode)
                ? [{ value: draft.stateCode, label: draft.stateCode }]
                : []),
            ]}
            disabled={draft.countryCode === '' && draft.stateCode === ''}
            clearable
            invalid={errors['stateCode'] !== undefined}
            label={t('customers.state')}
            placeholder={
              draft.countryCode === ''
                ? t('customers.chooseCountryFirst')
                : t('customers.noState')
            }
          />
        </Field>

        <Field label={t('customers.area')}>
          <SearchSelect
            value={draft.areaId}
            onChange={(value) => set('areaId', value)}
            options={areas.map((area) => ({ value: area.id, label: area.name }))}
            clearable
            label={t('customers.area')}
            placeholder={areas.length === 0 ? t('customers.noAreas') : t('common.choose')}
          />
        </Field>

        <Field label={t('customers.route')}>
          <SearchSelect
            value={draft.routeId}
            onChange={(value) => set('routeId', value)}
            options={routes.map((route) => ({ value: route.id, label: route.name }))}
            clearable
            label={t('customers.route')}
            placeholder={
              routes.length === 0 ? t('customers.noRoutes') : t('common.choose')
            }
          />
        </Field>

        <Section title={t('customers.sectionTax')} />

        <TextField
          label={t('customers.registrationNumber')}
          value={draft.registrationNumber}
          onChange={(value) => set('registrationNumber', value)}
        />

        <TextField
          label={t('customers.gstNumber')}
          value={draft.gstNumber}
          onChange={(value) => set('gstNumber', value)}
        />

        <TextField
          label={t('customers.tinNumber')}
          value={draft.tinNumber}
          onChange={(value) => set('tinNumber', value)}
        />

        <TextField
          label={t('customers.cstNumber')}
          value={draft.cstNumber}
          onChange={(value) => set('cstNumber', value)}
        />

        <Section title={t('customers.sectionTerms')} />

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

        <NumberField
          label={t('customers.openingAmount')}
          min={0}
          value={draft.openingBalance}
          onChange={(value) => set('openingBalance', value)}
          error={errors['openingBalance']}
          placeholder="0.00"
        />

        <Section title={t('customers.sectionPrint')} />

        <SelectField
          label={t('settings.customerInvoiceTemplate')}
          value={draft.printTemplate}
          onChange={(value) => set('printTemplate', value)}
          options={[
            { value: '', label: t('printing.followSettings') },
            ...printSetup.templates.map((template) => ({
              value: template.id,
              label: `${template.name} (${template.paper})`,
            })),
          ]}
        />

        <NumberField
          label={t('customers.printCopies')}
          min={1}
          step="1"
          value={draft.printCopies}
          onChange={(value) => set('printCopies', value)}
          error={errors['printCopies']}
          placeholder={t('printing.followSettings')}
        />

        <SelectField<'' | 'yes' | 'no'>
          label={t('customers.printPreview')}
          value={draft.printPreview}
          onChange={(value) => set('printPreview', value)}
          options={[
            { value: '', label: t('printing.followSettings') },
            { value: 'yes', label: t('customers.previewFirst') },
            { value: 'no', label: t('customers.printStraightAway') },
          ]}
        />

        <Section title={t('customers.photo')} />

        <div className="col-span-full flex flex-wrap items-center gap-4">
          <div className="flex size-24 items-center justify-center overflow-hidden rounded-xl border border-line bg-surface-2">
            {shownPhoto ? (
              <img
                src={shownPhoto}
                alt={t('customers.photo')}
                className="size-full object-cover"
              />
            ) : (
              <span className="px-2 text-center text-xs text-ink-muted">
                {t('customers.noPhoto')}
              </span>
            )}
          </div>

          <div className="flex flex-col gap-2">
            <input
              ref={fileInput}
              type="file"
              accept="image/*"
              className="hidden"
              onChange={(event) => {
                const file = event.target.files?.[0];
                event.target.value = '';
                if (!file) return;

                setPhotoError(null);
                shrinkPhoto(file)
                  .then(setPhoto)
                  .catch(() => setPhotoError(t('customers.photoInvalid')));
              }}
            />
            <button
              type="button"
              className="btn-secondary btn-sm"
              onClick={() => fileInput.current?.click()}
            >
              {shownPhoto ? t('customers.changePhoto') : t('customers.addPhoto')}
            </button>
            {shownPhoto && (
              <button
                type="button"
                className="btn-ghost btn-sm text-red-600"
                onClick={() => setPhoto(null)}
              >
                {t('customers.removePhoto')}
              </button>
            )}
            {photoError && <p className="field-message-error">{photoError}</p>}
          </div>
        </div>
      </div>

      <div className="form-actions">
        {/*
          Live rather than disabled until the form is complete. A dead button says
          nothing about which field it is waiting for; pressing this one marks them.
        */}
        <button type="submit" disabled={busy} className="btn-primary">
          {busy ? t('common.saving') : existing ? t('common.save') : t('masters.add')}
        </button>
      </div>
    </form>
  );
}
