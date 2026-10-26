import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { SearchSelect } from '@/components/SearchSelect';
import type { ApiError } from '@/lib/api';
import {
  listMaster,
  type BrandSummary,
  type CategorySummary,
  type WarehouseSummary,
} from '@/lib/inventory';

/** The godowns, with the branch each belongs to. */
function useWarehouses(): readonly WarehouseSummary[] {
  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });

  return warehouses.data ?? [];
}

function Picker({
  label,
  placeholder,
  value,
  onChange,
  options,
}: {
  readonly label: string;
  readonly placeholder: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly options: readonly { readonly value: string; readonly label: string }[];
}): React.JSX.Element {
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      <SearchSelect
        value={value}
        onChange={onChange}
        clearable
        size="sm"
        label={label}
        placeholder={placeholder}
        options={options}
      />
    </label>
  );
}

/** The branches that have a godown: the only ones a stock report can narrow to. */
export function BranchFilter({
  value,
  onChange,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const branches = new Map<string, string>();

  for (const warehouse of useWarehouses()) {
    if (warehouse.branchId) branches.set(warehouse.branchId, warehouse.branchName ?? '');
  }

  return (
    <Picker
      label={t('stockPosition.branch')}
      placeholder={t('stockPosition.allBranches')}
      value={value}
      onChange={onChange}
      options={[...branches].map(([id, name]) => ({ value: id, label: name }))}
    />
  );
}

/** The godowns - of the chosen branch, where one is chosen. */
export function GodownFilter({
  branchId,
  value,
  onChange,
}: {
  readonly branchId: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const warehouses = useWarehouses().filter(
    (warehouse) => !branchId || warehouse.branchId === branchId,
  );

  return (
    <Picker
      label={t('stockPosition.godown')}
      placeholder={t('stockPosition.allGodowns')}
      value={value}
      onChange={onChange}
      options={warehouses.map((warehouse) => ({
        value: warehouse.id,
        label: warehouse.name,
      }))}
    />
  );
}

export function CategoryFilter({
  value,
  onChange,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const categories = useQuery<readonly CategorySummary[], ApiError>({
    queryKey: ['categories', false],
    queryFn: () => listMaster<CategorySummary>('categories', false),
  });

  return (
    <Picker
      label={t('stockPosition.category')}
      placeholder={t('products.allCategories')}
      value={value}
      onChange={onChange}
      options={(categories.data ?? []).map((category) => ({
        value: category.id,
        label: category.name,
      }))}
    />
  );
}

export function BrandFilter({
  value,
  onChange,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const brands = useQuery<readonly BrandSummary[], ApiError>({
    queryKey: ['brands', false],
    queryFn: () => listMaster<BrandSummary>('brands', false),
  });

  return (
    <Picker
      label={t('stockPosition.brand')}
      placeholder={t('stockPosition.allBrands')}
      value={value}
      onChange={onChange}
      options={(brands.data ?? []).map((brand) => ({
        value: brand.id,
        label: brand.name,
      }))}
    />
  );
}

export function DateFilter({
  label,
  value,
  onChange,
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  return (
    <label className="field">
      <span className="field-label">{label}</span>
      <input
        type="date"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="field-input-sm"
      />
    </label>
  );
}
