import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ArabicNameField } from '@/components/ArabicNameField';
import { NumberField, TextField } from '@/components/Form';
import { createTaxRate, updateTaxRate, type TaxRate } from '@/lib/taxRates';
import { collect, maxLength, numeric, required, useValidation } from '@/lib/validation';

/** Adds a rate, or corrects one. Also used from the product editor to add one in place. */
export function TaxRateForm({
  record,
  run,
  busy,
}: {
  readonly record?: TaxRate;
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState('');
  const [name, setName] = useState(record?.name ?? '');
  const [nameArabic, setNameArabic] = useState(record?.nameArabic ?? '');
  const [percentage, setPercentage] = useState(
    record ? record.percentage.toFixed(2) : '',
  );
  const [isDefault, setIsDefault] = useState(record?.isDefault ?? false);

  const { errors, submit } = useValidation<{ name: string; percentage: string }>(
    (values) =>
      collect({
        name:
          required(values.name, t('taxRates.nameRequired')) ??
          maxLength(values.name, 100, t('categories.nameTooLong')),
        percentage:
          required(values.percentage, t('taxRates.percentageRequired')) ??
          numeric(values.percentage, t('taxRates.percentageInvalid'), {
            min: 0,
            max: 100,
          }),
      }),
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (!submit({ name, percentage })) return;

        const body = {
          name: name.trim(),
          nameArabic: nameArabic.trim() || null,
          percentage: Number(percentage),
          isDefault,
        };

        run(async () => {
          if (record) await updateTaxRate(record.id, body);
          else await createTaxRate({ ...body, code: code.trim() || null });
        });
      }}
    >
      <div className="form-grid">
        {record ? (
          <TextField
            label={t('masters.code')}
            value={record.code}
            onChange={() => undefined}
            disabled
          />
        ) : (
          <TextField
            label={t('masters.code')}
            autoFocus
            value={code}
            onChange={setCode}
            placeholder={t('masters.codeAuto')}
          />
        )}
        <TextField
          label={t('masters.name')}
          required
          value={name}
          onChange={setName}
          error={errors['name']}
          placeholder={t('taxRates.namePlaceholder')}
        />
        <ArabicNameField
          label={t('masters.nameArabic')}
          source={name}
          value={nameArabic}
          onChange={setNameArabic}
        />
        <NumberField
          label={t('taxRates.percentage')}
          required
          value={percentage}
          onChange={setPercentage}
          error={errors['percentage']}
          step="0.01"
          min={0}
        />
        <label className="field-check sm:col-span-2">
          <input
            type="checkbox"
            checked={isDefault}
            onChange={(event) => setIsDefault(event.target.checked)}
          />
          {t('taxRates.isDefault')}
        </label>
      </div>

      <div className="form-actions">
        <button type="submit" disabled={busy} className="btn-primary">
          {busy ? t('common.saving') : record ? t('common.save') : t('masters.add')}
        </button>
      </div>
    </form>
  );
}
