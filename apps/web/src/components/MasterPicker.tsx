import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ArabicNameField } from '@/components/ArabicNameField';
import { Field, NumberField, SelectField, TextField } from '@/components/Form';
import { IconPlus } from '@/components/icons';
import { Modal, ModalButton } from '@/components/Modal';
import { SearchSelect, type SelectOption } from '@/components/SearchSelect';
import type { ApiError } from '@/lib/api';
import {
  createMaster,
  listMaster,
  type BrandSummary,
  type CategorySummary,
  type UnitSummary,
} from '@/lib/inventory';
import { createTaxRate, listTaxRates, type TaxRate } from '@/lib/taxRates';

/** The masters a product is filed under, each of which can be added from the product. */
export type PickerKind = 'category' | 'brand' | 'unit' | 'taxRate';

type Row = CategorySummary | BrandSummary | UnitSummary | TaxRate;

const QUERY_KEY: Record<PickerKind, string> = {
  category: 'categories',
  brand: 'brands',
  unit: 'units',
  taxRate: 'tax-rates',
};

/** The active rows of one master, shared with every other screen reading the same list. */
export function useMasterRows<T extends Row>(
  kind: PickerKind,
): {
  readonly rows: readonly T[];
  readonly isPending: boolean;
} {
  const query = useQuery<readonly Row[], ApiError>({
    queryKey: [QUERY_KEY[kind], false],
    queryFn: (): Promise<readonly Row[]> => {
      switch (kind) {
        case 'category':
          return listMaster<CategorySummary>('categories', false);
        case 'brand':
          return listMaster<BrandSummary>('brands', false);
        case 'unit':
          return listMaster<UnitSummary>('units', false);
        default:
          return listTaxRates(false);
      }
    },
  });

  return { rows: (query.data ?? []) as readonly T[], isPending: query.isPending };
}

/** Whether a row is its master's Primary record - the default for a new record. */
export function isPrimary(row: Row): boolean {
  return (row as { readonly isPrimary?: boolean }).isPrimary === true;
}

function optionFor(kind: PickerKind, row: Row): SelectOption {
  if (kind === 'taxRate') {
    const rate = row as TaxRate;
    return { value: rate.id, label: rate.name, meta: `${rate.percentage.toFixed(2)}%` };
  }

  if (kind === 'unit') {
    const unit = row as UnitSummary;
    return {
      value: unit.id,
      label: `${unit.code} — ${unit.name}${
        unit.baseUnitId ? ` (×${unit.conversionFactor})` : ''
      }`,
    };
  }

  const named = row as CategorySummary;
  return {
    value: named.id,
    label: `${named.code} — ${named.name}`,
    ...(named.parentName ? { detail: named.parentName } : {}),
  };
}

/**
 * Picks a category, brand, unit or tax rate - and adds one that is not there yet without
 * leaving the product.
 *
 * Somebody entering a product finds the brand missing far more often than they set out
 * to maintain the brand list, and sending them to another screen loses the product half
 * typed. The plus button opens the master's own short form in place, and the new record
 * is chosen the moment it is saved.
 */
export function MasterPicker({
  kind,
  value,
  onChange,
  label,
  required = false,
  error,
  clearable = false,
  placeholder,
  include,
  unitGroup,
  disabled = false,
}: {
  readonly kind: PickerKind;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly label: string;
  readonly required?: boolean;
  readonly error?: string | undefined;
  readonly clearable?: boolean;
  readonly placeholder?: string;
  /** Narrows the rows offered, for units that must convert to the stock unit. */
  readonly include?: (row: Row) => boolean;
  /** The base unit a unit added here converts to, for a purchase or sales unit. */
  readonly unitGroup?: UnitSummary | undefined;
  readonly disabled?: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [adding, setAdding] = useState(false);
  const { rows, isPending } = useMasterRows(kind);

  // The Primary record first: it is what a new record is filed under by default.
  const options = useMemo(
    () =>
      rows
        .filter((row) => row.isActive && (include ? include(row) : true))
        .sort((a, b) => Number(isPrimary(b)) - Number(isPrimary(a)))
        .map((row) => optionFor(kind, row)),
    [rows, include, kind],
  );

  return (
    <Field label={label} required={required} error={error}>
      {/* The Add button joined to the box, so the pair is one control the width of the
          plain fields beside it rather than a shorter box and a loose square. */}
      <div className="flex min-w-0 items-stretch">
        <div className="min-w-0 flex-1">
          <SearchSelect
            value={value}
            onChange={onChange}
            options={options}
            label={label}
            joined
            clearable={clearable}
            invalid={error !== undefined}
            disabled={disabled}
            placeholder={
              isPending ? t('common.loading') : (placeholder ?? t('common.choose'))
            }
          />
        </div>
        <button
          type="button"
          onClick={() => setAdding(true)}
          disabled={disabled}
          className="btn-secondary -ms-px shrink-0 rounded-s-none px-2.5"
          title={t(`masterPicker.add.${kind}`)}
          aria-label={t(`masterPicker.add.${kind}`)}
        >
          <IconPlus className="size-4" />
        </button>
      </div>

      {adding && (
        <QuickCreate
          kind={kind}
          unitGroup={unitGroup}
          onClose={() => setAdding(false)}
          onCreated={(id) => {
            setAdding(false);
            onChange(id);
          }}
        />
      )}
    </Field>
  );
}

/** The short form behind a picker's plus button. */
function QuickCreate({
  kind,
  unitGroup,
  onClose,
  onCreated,
}: {
  readonly kind: PickerKind;
  readonly unitGroup: UnitSummary | undefined;
  readonly onClose: () => void;
  readonly onCreated: (id: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const categories = useMasterRows<CategorySummary>('category');

  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [nameArabic, setNameArabic] = useState('');
  const [parentId, setParentId] = useState('');
  const [symbol, setSymbol] = useState('');
  const [percentage, setPercentage] = useState('');
  const [converts, setConverts] = useState(unitGroup !== undefined);
  const [factor, setFactor] = useState('');

  const base = unitGroup?.baseUnitId ?? unitGroup?.id;

  const save = useMutation<string, ApiError>({
    mutationFn: () => {
      const named = { code: code.trim() || null, name: name.trim() };
      switch (kind) {
        case 'category':
          return createMaster('categories', {
            ...named,
            nameArabic: nameArabic.trim() || null,
            parentId: parentId || null,
          });
        case 'brand':
          return createMaster('brands', {
            ...named,
            nameArabic: nameArabic.trim() || null,
          });
        case 'unit':
          return createMaster('units', {
            ...named,
            symbol: symbol.trim() || null,
            baseUnitId: converts && base ? base : null,
            conversionFactor: converts && base ? Number(factor) || 1 : 1,
            decimalPlaces: 0,
          });
        default:
          return createTaxRate({
            ...named,
            nameArabic: nameArabic.trim() || null,
            percentage: Number(percentage),
            isDefault: false,
          });
      }
    },
    onSuccess: async (id) => {
      await queryClient.invalidateQueries({ queryKey: [QUERY_KEY[kind]] });
      onCreated(id);
    },
  });

  const ready =
    name.trim() !== '' &&
    (kind !== 'taxRate' || (percentage.trim() !== '' && Number(percentage) >= 0)) &&
    (kind !== 'unit' || !converts || Number(factor) > 0) &&
    !save.isPending;

  return (
    <Modal title={t(`masterPicker.add.${kind}`)} size="form" onClose={onClose}>
      <form
        className="space-y-4"
        onSubmit={(event) => {
          // Inside the product's own form in the page, so the submit must not reach it.
          event.preventDefault();
          event.stopPropagation();
          if (ready) save.mutate();
        }}
      >
        {save.isError && (
          <p className="alert-error">{save.error.detail || save.error.code}</p>
        )}

        <div className="form-grid">
          <TextField
            label={t('masters.code')}
            value={code}
            onChange={setCode}
            placeholder={t('masters.codeAuto')}
          />
          <TextField
            label={t('masters.name')}
            required
            autoFocus
            value={name}
            onChange={setName}
            {...(kind === 'taxRate'
              ? { placeholder: t('taxRates.namePlaceholder') }
              : {})}
          />

          {kind !== 'unit' && (
            <ArabicNameField
              label={t('masters.nameArabic')}
              source={name}
              value={nameArabic}
              onChange={setNameArabic}
            />
          )}

          {kind === 'category' && (
            <SelectField
              label={t('categories.parent')}
              value={parentId}
              onChange={setParentId}
              options={[
                { value: '', label: t('categories.topLevel') },
                ...categories.rows
                  .filter((row) => row.isActive)
                  .map((row) => ({ value: row.id, label: `${row.code} — ${row.name}` })),
              ]}
            />
          )}

          {kind === 'unit' && (
            <>
              <TextField label={t('units.symbol')} value={symbol} onChange={setSymbol} />
              {base && (
                <label className="field-check sm:col-span-2">
                  <input
                    type="checkbox"
                    checked={converts}
                    onChange={(event) => setConverts(event.target.checked)}
                  />
                  {t('masterPicker.convertsTo', {
                    unit: unitGroup?.baseUnitCode ?? unitGroup?.code ?? '',
                  })}
                </label>
              )}
              {base && converts && (
                <NumberField
                  label={t('masterPicker.factor', {
                    unit: unitGroup?.baseUnitCode ?? unitGroup?.code ?? '',
                  })}
                  required
                  value={factor}
                  onChange={setFactor}
                  min={0}
                />
              )}
            </>
          )}

          {kind === 'taxRate' && (
            <NumberField
              label={t('taxRates.percentage')}
              required
              value={percentage}
              onChange={setPercentage}
              step="0.01"
              min={0}
            />
          )}
        </div>

        <div className="form-actions">
          <ModalButton onClick={onClose}>{t('common.cancel')}</ModalButton>
          <button type="submit" disabled={!ready} className="btn-primary btn-sm">
            {save.isPending ? t('common.saving') : t('masters.add')}
          </button>
        </div>
      </form>
    </Modal>
  );
}
