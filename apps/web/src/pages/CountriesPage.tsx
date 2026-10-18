import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { MasterFrame, RowAction } from '@/components/MasterFrame';
import { ArabicNameField } from '@/components/ArabicNameField';
import { Field, TextField } from '@/components/Form';
import { SearchSelect } from '@/components/SearchSelect';
import type { GridColumn } from '@/components/DataGrid';
import { ActiveBadge } from '@/components/StatusBadge';
import {
  createCountry,
  createState,
  listCountries,
  listStates,
  setCountryActive,
  setStateActive,
  updateCountry,
  updateState,
  useCountries,
  type CountrySummary,
  type StateSummary,
} from '@/lib/places';
import { collect, maxLength, required, useValidation } from '@/lib/validation';

type Tab = 'countries' | 'states';

/**
 * Countries with their currencies, and the states of each.
 *
 * Every country comes seeded, with the currency used there; a firm corrects or adds what
 * it needs. States are listed under the country they belong to, and a customer's state
 * is chosen from those of the country picked on the customer, never from all of them.
 */
export function CountriesPage(): React.JSX.Element {
  const { t } = useTranslation();
  const [tab, setTab] = useState<Tab>('countries');

  return (
    <div className="space-y-3">
      <div role="tablist" className="flex gap-1 overflow-x-auto border-b border-line">
        {(['countries', 'states'] as const).map((name) => (
          <button
            key={name}
            type="button"
            role="tab"
            aria-selected={tab === name}
            onClick={() => setTab(name)}
            className={clsx(
              '-mb-px shrink-0 border-b-2 px-3 py-2.5 text-sm font-medium whitespace-nowrap transition',
              tab === name
                ? 'border-brand-600 text-brand-700 dark:border-brand-400 dark:text-brand-300'
                : 'border-transparent text-ink-muted hover:border-line-strong hover:text-ink',
            )}
          >
            {t(`places.${name}`)}
          </button>
        ))}
      </div>

      {tab === 'countries' ? <Countries /> : <States />}
    </div>
  );
}

function Countries(): React.JSX.Element {
  const { t } = useTranslation();

  const columns = (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
    edit: (row: CountrySummary) => void,
  ): readonly GridColumn<CountrySummary>[] => [
    { key: 'code', header: t('masters.code'), value: (row) => row.code },
    { key: 'name', header: t('masters.name'), value: (row) => row.name },
    {
      key: 'nameArabic',
      header: t('masters.nameArabic'),
      value: (row) => row.nameArabic ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'currency',
      header: t('places.currency'),
      value: (row) => row.currencyCode ?? '',
    },
    {
      key: 'currencyName',
      header: t('places.currencyName'),
      value: (row) => row.currencyName ?? '',
    },
    {
      key: 'states',
      header: t('places.states'),
      value: (row) => row.stateCount,
      numeric: true,
    },
    {
      key: 'status',
      header: t('masters.status'),
      value: (row) => (row.isActive ? t('masters.active') : t('masters.withdrawn')),
      render: (row) => <ActiveBadge isActive={row.isActive} />,
      hiddenByDefault: true,
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
                await setCountryActive(row.id, !row.isActive);
              })
            }
          />
        </span>
      ),
    },
  ];

  return (
    <MasterFrame<CountrySummary>
      title={t('places.countriesTitle')}
      addTitle={t('places.newCountry')}
      editTitle={t('places.editCountry')}
      queryKey="countries"
      fetchRows={(includeInactive) => listCountries(includeInactive)}
      columns={columns}
      rowKey={(row) => row.id}
      addForm={(run, busy) => <CountryForm run={run} busy={busy} />}
      editForm={(row, run, busy) => <CountryForm run={run} busy={busy} existing={row} />}
    />
  );
}

function CountryForm({
  run,
  busy,
  existing,
}: {
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
  readonly existing?: CountrySummary;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState(existing?.code ?? '');
  const [name, setName] = useState(existing?.name ?? '');
  const [nameArabic, setNameArabic] = useState(existing?.nameArabic ?? '');
  const [currencyCode, setCurrencyCode] = useState(existing?.currencyCode ?? '');
  const [currencyName, setCurrencyName] = useState(existing?.currencyName ?? '');

  const { errors, submit } = useValidation<{
    code: string;
    name: string;
    currencyCode: string;
  }>((values) =>
    collect({
      code:
        required(values.code, t('places.codeRequired')) ??
        maxLength(values.code, 3, t('places.codeTooLong')),
      name: required(values.name, t('places.nameRequired')),
      currencyCode:
        values.currencyCode.trim() !== '' &&
        !/^[A-Za-z]{3}$/.test(values.currencyCode.trim())
          ? t('places.currencyInvalid')
          : null,
    }),
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        if (!submit({ code, name, currencyCode })) return;

        const country = {
          name: name.trim(),
          nameArabic: nameArabic.trim() || null,
          currencyCode: currencyCode.trim().toUpperCase() || null,
          currencyName: currencyName.trim() || null,
        };

        run(async () => {
          if (existing) await updateCountry(existing.id, country);
          else await createCountry({ ...country, code: code.trim().toUpperCase() });
        });
      }}
    >
      <div className="form-grid">
        {existing ? (
          <Field label={t('masters.code')}>
            <input value={existing.code} disabled className="field-input" />
          </Field>
        ) : (
          <TextField
            label={t('masters.code')}
            required
            autoFocus
            value={code}
            onChange={setCode}
            error={errors['code']}
            placeholder="QA"
          />
        )}

        <TextField
          label={t('masters.name')}
          required
          {...(existing ? { autoFocus: true } : {})}
          value={name}
          onChange={setName}
          error={errors['name']}
        />

        <ArabicNameField
          label={t('masters.nameArabic')}
          source={name}
          value={nameArabic}
          onChange={setNameArabic}
        />

        <TextField
          label={t('places.currency')}
          value={currencyCode}
          onChange={setCurrencyCode}
          error={errors['currencyCode']}
          placeholder="QAR"
        />

        <TextField
          label={t('places.currencyName')}
          value={currencyName}
          onChange={setCurrencyName}
          placeholder="Qatari Riyal"
        />
      </div>

      <div className="form-actions">
        <button type="submit" disabled={busy} className="btn-primary">
          {busy ? t('common.saving') : existing ? t('common.save') : t('masters.add')}
        </button>
      </div>
    </form>
  );
}

function States(): React.JSX.Element {
  const { t } = useTranslation();
  const countries = useCountries();
  const [countryId, setCountryId] = useState('');

  const columns = (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
    edit: (row: StateSummary) => void,
  ): readonly GridColumn<StateSummary>[] => [
    { key: 'code', header: t('masters.code'), value: (row) => row.code },
    { key: 'name', header: t('masters.name'), value: (row) => row.name },
    {
      key: 'nameArabic',
      header: t('masters.nameArabic'),
      value: (row) => row.nameArabic ?? '',
      hiddenByDefault: true,
    },
    { key: 'country', header: t('places.country'), value: (row) => row.countryName },
    {
      key: 'status',
      header: t('masters.status'),
      value: (row) => (row.isActive ? t('masters.active') : t('masters.withdrawn')),
      render: (row) => <ActiveBadge isActive={row.isActive} />,
      hiddenByDefault: true,
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
                await setStateActive(row.id, !row.isActive);
              })
            }
          />
        </span>
      ),
    },
  ];

  return (
    <MasterFrame<StateSummary>
      title={t('places.statesTitle')}
      addTitle={t('places.newState')}
      editTitle={t('places.editState')}
      queryKey="states"
      queryArgs={[countryId || 'all']}
      fetchRows={(includeInactive) => listStates(countryId || null, includeInactive)}
      controls={
        <div className="w-full max-w-xs">
          <SearchSelect
            value={countryId}
            onChange={setCountryId}
            options={countries.map((country) => ({
              value: country.id,
              label: country.name,
              meta: country.code,
            }))}
            clearable
            size="sm"
            label={t('places.country')}
            placeholder={t('places.allCountries')}
          />
        </div>
      }
      columns={columns}
      rowKey={(row) => row.id}
      addForm={(run, busy) => <StateForm run={run} busy={busy} countryId={countryId} />}
      editForm={(row, run, busy) => (
        <StateForm run={run} busy={busy} countryId={row.countryId} existing={row} />
      )}
    />
  );
}

function StateForm({
  run,
  busy,
  countryId: initialCountry,
  existing,
}: {
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
  readonly countryId: string;
  readonly existing?: StateSummary;
}): React.JSX.Element {
  const { t } = useTranslation();
  const countries = useCountries();
  const [countryId, setCountryId] = useState(initialCountry);
  const [code, setCode] = useState(existing?.code ?? '');
  const [name, setName] = useState(existing?.name ?? '');
  const [nameArabic, setNameArabic] = useState(existing?.nameArabic ?? '');

  const { errors, submit } = useValidation<{
    countryId: string;
    code: string;
    name: string;
  }>((values) =>
    collect({
      countryId: required(values.countryId, t('places.countryRequired')),
      code:
        required(values.code, t('places.codeRequired')) ??
        maxLength(values.code, 10, t('places.stateCodeTooLong')),
      name: required(values.name, t('places.nameRequired')),
    }),
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        if (!submit({ countryId, code, name })) return;

        run(async () => {
          const state = {
            countryId,
            name: name.trim(),
            nameArabic: nameArabic.trim() || null,
          };

          if (existing) await updateState(existing.id, state);
          else await createState({ ...state, code: code.trim().toUpperCase() });
        });
      }}
    >
      <div className="form-grid">
        <Field label={t('places.country')} required error={errors['countryId']}>
          <SearchSelect
            value={countryId}
            onChange={setCountryId}
            options={countries.map((country) => ({
              value: country.id,
              label: country.name,
              meta: country.code,
            }))}
            invalid={errors['countryId'] !== undefined}
            label={t('places.country')}
            placeholder={t('common.choose')}
          />
        </Field>

        {existing ? (
          <Field label={t('masters.code')}>
            <input value={existing.code} disabled className="field-input" />
          </Field>
        ) : (
          <TextField
            label={t('masters.code')}
            required
            value={code}
            onChange={setCode}
            error={errors['code']}
            placeholder="QA-DA"
          />
        )}

        <TextField
          label={t('masters.name')}
          required
          value={name}
          onChange={setName}
          error={errors['name']}
        />

        <ArabicNameField
          label={t('masters.nameArabic')}
          source={name}
          value={nameArabic}
          onChange={setNameArabic}
        />
      </div>

      <div className="form-actions">
        <button type="submit" disabled={busy} className="btn-primary">
          {busy ? t('common.saving') : existing ? t('common.save') : t('masters.add')}
        </button>
      </div>
    </form>
  );
}
