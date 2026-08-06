import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { MasterField, MasterFrame, RowAction } from '@/components/MasterFrame';
import type { GridColumn } from '@/components/DataGrid';
import {
  createMaster,
  listMaster,
  setMasterActive,
  type BrandSummary,
  type CategorySummary,
} from '@/lib/inventory';

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
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <RowAction
          label={row.isActive ? t('masters.withdraw') : t('masters.restore')}
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

  return (
    <form
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();

        if (!code.trim() || !name.trim()) {
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

        setCode('');
        setName('');
        setNameArabic('');
      }}
    >
      <MasterField label={t('masters.code')} value={code} onChange={setCode} />
      <MasterField label={t('masters.name')} value={name} onChange={setName} width="w-44" />
      <MasterField
        label={t('masters.nameArabic')}
        value={nameArabic}
        onChange={setNameArabic}
        width="w-44"
      />

      <label className="flex flex-col gap-1 text-sm">
        <span className="text-slate-600 dark:text-slate-400">{t('categories.parent')}</span>
        <select
          value={parentId}
          onChange={(event) => setParentId(event.target.value)}
          className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
        >
          <option value="">{t('categories.topLevel')}</option>
          {rows
            .filter((row) => row.isActive)
            .map((row) => (
              <option key={row.id} value={row.id}>
                {row.code} — {row.name}
              </option>
            ))}
        </select>
      </label>

      <button type="submit" disabled={busy} className="btn-primary">
        {t('masters.add')}
      </button>
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
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <RowAction
          label={row.isActive ? t('masters.withdraw') : t('masters.restore')}
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
      queryKey="brands"
      fetchRows={(includeInactive) =>
        listMaster<BrandSummary>('brands', includeInactive)
      }
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

  return (
    <form
      className="flex flex-wrap items-end gap-3"
      onSubmit={(event) => {
        event.preventDefault();

        if (!code.trim() || !name.trim()) {
          return;
        }

        run(async () => {
          await createMaster('brands', {
            code: code.trim(),
            name: name.trim(),
            nameArabic: nameArabic.trim() || null,
          });
        });

        setCode('');
        setName('');
        setNameArabic('');
      }}
    >
      <MasterField label={t('masters.code')} value={code} onChange={setCode} />
      <MasterField label={t('masters.name')} value={name} onChange={setName} width="w-44" />
      <MasterField
        label={t('masters.nameArabic')}
        value={nameArabic}
        onChange={setNameArabic}
        width="w-44"
      />

      <button type="submit" disabled={busy} className="btn-primary">
        {t('masters.add')}
      </button>
    </form>
  );
}
