import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { MasterFrame, RowAction } from '@/components/MasterFrame';
import { ArabicNameField } from '@/components/ArabicNameField';
import { SelectField, TextField } from '@/components/Form';
import type { GridColumn } from '@/components/DataGrid';
import { ActiveBadge } from '@/components/StatusBadge';
import {
  createMaster,
  listMaster,
  setMasterActive,
  type BrandSummary,
  type CategorySummary,
} from '@/lib/inventory';
import { collect, maxLength, required, useValidation } from '@/lib/validation';

/**
 * Product categories and sub-classes.
 *
 * One master, not two. A sub-class is a category with a parent, which is what lets a
 * third level exist the day a reporting hierarchy wants one — without a third table,
 * a third screen, and a third set of rules that drift apart.
 */
export function CategoriesPage(): React.JSX.Element {
  const { t } = useTranslation();

  const columns = (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
  ): readonly GridColumn<CategorySummary>[] => [
    { key: 'code', header: t('masters.code'), value: (row) => row.code },
    { key: 'name', header: t('masters.name'), value: (row) => row.name },
    {
      key: 'nameArabic',
      header: t('masters.nameArabic'),
      value: (row) => row.nameArabic ?? '',
    },
    {
      key: 'parent',
      header: t('categories.parent'),
      value: (row) => row.parentName ?? t('categories.topLevel'),
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
        <RowAction
          label={row.isActive ? t('masters.withdraw') : t('masters.restore')}
          tone={row.isActive ? 'danger' : 'neutral'}
          disabled={busy}
          onClick={() =>
            run(async () => {
              await setMasterActive('Category', row.id, !row.isActive);
            })
          }
        />
      ),
    },
  ];

  return (
    <MasterFrame<CategorySummary>
      title={t('nav.categories')}
      addTitle={t('masters.newCategory')}
      queryKey="categories"
      fetchRows={(includeInactive) =>
        listMaster<CategorySummary>('categories', includeInactive)
      }
      columns={columns}
      rowKey={(row) => row.id}
      addForm={(run, busy, rows) => <AddCategory run={run} busy={busy} rows={rows} />}
    />
  );
}

function AddCategory({
  run,
  busy,
  rows,
}: {
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
  readonly rows: readonly CategorySummary[];
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [nameArabic, setNameArabic] = useState('');
  const [parentId, setParentId] = useState('');

  const { errors, submit } = useValidation<{ code: string; name: string }>((values) =>
    collect({
      code:
        required(values.code, t('categories.codeRequired')) ??
        maxLength(values.code, 20, t('categories.codeTooLong')),
      name:
        required(values.name, t('categories.nameRequired')) ??
        maxLength(values.name, 100, t('categories.nameTooLong')),
    }),
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        if (!submit({ code, name })) {
          return;
        }

        run(async () => {
          await createMaster('categories', {
            code: code.trim(),
            name: name.trim(),
            nameArabic: nameArabic.trim() || null,
            parentId: parentId || null,
          });
        });
      }}
    >
      <div className="form-grid">
        <TextField
          label={t('masters.code')}
          required
          autoFocus
          value={code}
          onChange={setCode}
          error={errors['code']}
        />
        <TextField
          label={t('masters.name')}
          required
          value={name}
          onChange={setName}
          error={errors['name']}
        />

        {/*
          Filled in from the English name where the firm has turned translation on
          in Settings, and an ordinary editable box either way. A machine
          translation of a category name is a first draft — the firm printing it on
          an invoice decides whether it is right.
        */}
        <ArabicNameField
          label={t('masters.nameArabic')}
          source={name}
          value={nameArabic}
          onChange={setNameArabic}
        />

        <SelectField
          label={t('categories.parent')}
          value={parentId}
          onChange={setParentId}
          options={[
            { value: '', label: t('categories.topLevel') },
            ...rows
              .filter((row) => row.isActive)
              .map((row) => ({ value: row.id, label: `${row.code} — ${row.name}` })),
          ]}
        />
      </div>

      <div className="form-actions">
        <button type="submit" disabled={busy} className="btn-primary">
          {busy ? t('common.saving') : t('masters.add')}
        </button>
      </div>
    </form>
  );
}

/**
 * Brands.
 *
 * Flat, unlike categories: a brand is a name a product carries rather than a place in
 * a hierarchy, and nothing in the reporting asks for a brand beneath a brand.
 */
export function BrandsPage(): React.JSX.Element {
  const { t } = useTranslation();

  const columns = (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
  ): readonly GridColumn<BrandSummary>[] => [
    { key: 'code', header: t('masters.code'), value: (row) => row.code },
    { key: 'name', header: t('masters.name'), value: (row) => row.name },
    {
      key: 'nameArabic',
      header: t('masters.nameArabic'),
      value: (row) => row.nameArabic ?? '',
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
        <RowAction
          label={row.isActive ? t('masters.withdraw') : t('masters.restore')}
          tone={row.isActive ? 'danger' : 'neutral'}
          disabled={busy}
          onClick={() =>
            run(async () => {
              await setMasterActive('Brand', row.id, !row.isActive);
            })
          }
        />
      ),
    },
  ];

  return (
    <MasterFrame<BrandSummary>
      title={t('nav.brands')}
      addTitle={t('masters.newBrand')}
      queryKey="brands"
      fetchRows={(includeInactive) => listMaster<BrandSummary>('brands', includeInactive)}
      columns={columns}
      rowKey={(row) => row.id}
      addForm={(run, busy) => <AddBrand run={run} busy={busy} />}
    />
  );
}

function AddBrand({
  run,
  busy,
}: {
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [nameArabic, setNameArabic] = useState('');

  const { errors, submit } = useValidation<{ code: string; name: string }>((values) =>
    collect({
      code:
        required(values.code, t('categories.codeRequired')) ??
        maxLength(values.code, 20, t('categories.codeTooLong')),
      name:
        required(values.name, t('categories.nameRequired')) ??
        maxLength(values.name, 100, t('categories.nameTooLong')),
    }),
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        if (!submit({ code, name })) {
          return;
        }

        run(async () => {
          await createMaster('brands', {
            code: code.trim(),
            name: name.trim(),
            nameArabic: nameArabic.trim() || null,
          });
        });
      }}
    >
      <div className="form-grid">
        <TextField
          label={t('masters.code')}
          required
          autoFocus
          value={code}
          onChange={setCode}
          error={errors['code']}
        />
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
          {busy ? t('common.saving') : t('masters.add')}
        </button>
      </div>
    </form>
  );
}
