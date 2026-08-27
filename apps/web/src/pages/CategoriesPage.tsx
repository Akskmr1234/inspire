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

  return (
    <form
      className="space-y-4"
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
      }}
    >
      <div className="form-grid">
        <MasterField label={t('masters.code')} value={code} onChange={setCode} />
        <MasterField label={t('masters.name')} value={name} onChange={setName} />
        <MasterField
          label={t('masters.nameArabic')}
          value={nameArabic}
          onChange={setNameArabic}
        />

        <label className="field">
          <span className="field-label">{t('categories.parent')}</span>
          <select
            value={parentId}
            onChange={(event) => setParentId(event.target.value)}
            className="field-input-sm"
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
      </div>

      <div className="form-actions">
        <button type="submit" disabled={busy} className="btn-primary">
          {t('masters.add')}
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

  return (
    <form
      className="space-y-4"
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
      }}
    >
      <div className="form-grid">
        <MasterField label={t('masters.code')} value={code} onChange={setCode} />
        <MasterField label={t('masters.name')} value={name} onChange={setName} />
        <MasterField
          label={t('masters.nameArabic')}
          value={nameArabic}
          onChange={setNameArabic}
        />
      </div>

      <div className="form-actions">
        <button type="submit" disabled={busy} className="btn-primary">
          {t('masters.add')}
        </button>
      </div>
    </form>
  );
}
