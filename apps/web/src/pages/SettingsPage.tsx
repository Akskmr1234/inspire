import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { PageHeading } from '@/components/PageHeading';
import {
  CheckField,
  Field,
  NumberField,
  SelectField,
  TextField,
} from '@/components/Form';
import { SearchSelect } from '@/components/SearchSelect';
import type { ApiError } from '@/lib/api';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import { statesFor, TaxRegime } from '@/lib/states';
import { ratesFor, useSettings } from '@/stores/settings';

/**
 * The preferences screen.
 *
 * Everything here decides how the screens behave rather than what a document means,
 * which is why it is a screen of its own and not a strip on each of them: the
 * translation service, the tax rates the pickers offer, the firm's regime and its
 * own state, the warehouse a purchase starts at, and how many rows a page holds.
 *
 * Saved as each control is changed rather than behind a Save button. There is
 * nothing here that is only half-true midway through — turning translation on is
 * complete the moment it is on — and a settings screen that can be left with unsaved
 * changes is a settings screen people leave with unsaved changes.
 */
export function SettingsPage(): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();
  const [ratesDraft, setRatesDraft] = useState(settings.taxRates.join(', '));

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });

  const states = statesFor(settings.taxRegime);

  const applyRates = (raw: string): void => {
    const parsed = raw
      .split(/[,\s]+/)
      .map((piece) => Number(piece))
      .filter((rate) => Number.isFinite(rate) && rate >= 0 && rate <= 100);

    // An unparseable list leaves the stored rates alone. Emptying the picker
    // because somebody was midway through typing "1" of "18" would be worse than
    // ignoring the keystroke.
    if (parsed.length > 0) {
      settings.update({ taxRates: parsed });
    }
  };

  return (
    <section className="page">
      <PageHeading title={t('settings.title')} subtitle={t('settings.subtitle')} />

      <div className="grid gap-4 lg:grid-cols-2">
        <SettingsCard title={t('settings.taxTitle')} hint={t('settings.taxHint')}>
          <SelectField
            label={t('settings.regime')}
            value={settings.taxRegime}
            onChange={(regime) => {
              // The rates and the home state belong to the regime: a firm switched
              // from VAT to GST would otherwise be offering 5% on a GST invoice and
              // holding an emirate in the field that decides IGST.
              const rates = ratesFor(regime);

              settings.update({
                taxRegime: regime,
                taxRates: rates,
                defaultTaxRate: rates.includes(settings.defaultTaxRate)
                  ? settings.defaultTaxRate
                  : (rates[0] ?? 0),
                firmStateCode: '',
              });

              setRatesDraft(rates.join(', '));
            }}
            options={[
              { value: TaxRegime.gccVat, label: t('settings.regimeVat') },
              { value: TaxRegime.indiaGst, label: t('settings.regimeGst') },
              { value: TaxRegime.none, label: t('settings.regimeNone') },
            ]}
          />

          <Field
            label={t('settings.firmState')}
            hint={t('settings.firmStateHint')}
            className={states.length === 0 ? 'opacity-50' : ''}
          >
            <SearchSelect
              value={settings.firmStateCode}
              onChange={(code) => settings.update({ firmStateCode: code })}
              disabled={states.length === 0}
              clearable
              placeholder={t('settings.noState')}
              label={t('settings.firmState')}
              options={states.map((state) => ({
                value: state.code,
                label: `${state.code} — ${state.name}`,
              }))}
            />
          </Field>

          <TextField
            label={t('settings.taxRates')}
            hint={t('settings.taxRatesHint')}
            value={ratesDraft}
            onChange={(value) => {
              setRatesDraft(value);
              applyRates(value);
            }}
          />

          <SelectField
            label={t('settings.defaultTaxRate')}
            value={settings.defaultTaxRate}
            onChange={(rate) => settings.update({ defaultTaxRate: rate })}
            options={settings.taxRates.map((rate) => ({
              value: rate,
              label: `${rate}%`,
            }))}
          />
        </SettingsCard>

        <SettingsCard
          title={t('settings.translationTitle')}
          hint={t('settings.translationHint')}
        >
          <CheckField
            label={t('settings.autoTranslate')}
            hint={t('settings.autoTranslateHint')}
            checked={settings.autoTranslateArabic}
            onChange={(checked) => settings.update({ autoTranslateArabic: checked })}
          />

          <TextField
            label={t('settings.apiKey')}
            hint={t('settings.apiKeyHint')}
            value={settings.translationApiKey}
            onChange={(value) => settings.update({ translationApiKey: value })}
            placeholder={t('settings.apiKeyOptional')}
          />
        </SettingsCard>

        <SettingsCard
          title={t('settings.documentsTitle')}
          hint={t('settings.documentsHint')}
        >
          <Field
            label={t('settings.preferredWarehouse')}
            hint={t('settings.preferredWarehouseHint')}
          >
            <SearchSelect
              value={settings.preferredWarehouseId}
              onChange={(id) => settings.update({ preferredWarehouseId: id })}
              clearable
              placeholder={t('settings.warehouseFromMaster')}
              label={t('settings.preferredWarehouse')}
              options={(warehouses.data ?? []).map((warehouse) => ({
                value: warehouse.id,
                label: `${warehouse.code} — ${warehouse.name}`,
                ...(warehouse.isDefault ? { meta: t('settings.masterDefault') } : {}),
              }))}
            />
          </Field>
        </SettingsCard>

        <SettingsCard title={t('settings.listsTitle')} hint={t('settings.listsHint')}>
          <NumberField
            label={t('settings.pageSize')}
            hint={t('settings.pageSizeHint')}
            min={5}
            step="5"
            value={String(settings.pageSize)}
            onChange={(value) => {
              const parsed = Number(value);

              if (Number.isFinite(parsed) && parsed >= 5 && parsed <= 500) {
                settings.update({ pageSize: Math.round(parsed) });
              }
            }}
          />

          <div className="form-actions">
            <button
              type="button"
              className="btn-secondary btn-sm"
              onClick={() => {
                settings.reset();
                setRatesDraft(useSettings.getState().taxRates.join(', '));
              }}
            >
              {t('settings.reset')}
            </button>
          </div>
        </SettingsCard>
      </div>

      <p className="text-xs text-ink-muted">{t('settings.storedLocally')}</p>
    </section>
  );
}

function SettingsCard({
  title,
  hint,
  children,
}: {
  readonly title: string;
  readonly hint: string;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <section className="card card-body space-y-4">
      <header>
        <h2 className="text-sm font-semibold tracking-tight text-ink">{title}</h2>
        <p className="mt-0.5 text-xs text-ink-muted">{hint}</p>
      </header>

      <div className="space-y-3">{children}</div>
    </section>
  );
}
