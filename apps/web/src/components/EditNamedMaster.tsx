import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ArabicNameField } from '@/components/ArabicNameField';
import { TextField } from '@/components/Form';
import { updateMaster } from '@/lib/inventory';
import { collect, maxLength, required, useValidation } from '@/lib/validation';

/**
 * Corrects a category, brand or warehouse. The code is shown and not offered: it is
 * what imports and a firm's own lists refer to the record by.
 */
export function EditNamedMaster({
  kind,
  record,
  run,
  busy,
}: {
  readonly kind: 'Category' | 'Brand' | 'Warehouse';
  readonly record: {
    readonly id: string;
    readonly code: string;
    readonly name: string;
    readonly nameArabic: string | null;
    readonly address?: string | null;
  };
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [name, setName] = useState(record.name);
  const [nameArabic, setNameArabic] = useState(record.nameArabic ?? '');
  const [address, setAddress] = useState(record.address ?? '');

  const { errors, submit } = useValidation<{ name: string }>((values) =>
    collect({
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

        if (!submit({ name })) {
          return;
        }

        run(async () => {
          await updateMaster(kind, record.id, {
            name: name.trim(),
            nameArabic: nameArabic.trim() || null,
            ...(kind === 'Warehouse' ? { address: address.trim() || null } : {}),
          });
        });
      }}
    >
      <div className="form-grid">
        <TextField
          label={t('masters.code')}
          value={record.code}
          onChange={() => undefined}
          disabled
        />
        <TextField
          label={t('masters.name')}
          required
          autoFocus
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
        {kind === 'Warehouse' && (
          <TextField
            label={t('warehouses.address')}
            value={address}
            onChange={setAddress}
          />
        )}
      </div>

      <div className="form-actions">
        <button type="submit" disabled={busy} className="btn-primary">
          {busy ? t('common.saving') : t('common.save')}
        </button>
      </div>
    </form>
  );
}
