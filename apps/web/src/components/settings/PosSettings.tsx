import { useTranslation } from 'react-i18next';
import { CheckField, SelectField, TextField } from '@/components/Form';
import { SettingsCard } from '@/components/settings/SettingsSections';
import { useSettings } from '@/stores/settings';

/**
 * Settings › POS: how the counter bills, prints, and what a cashier may do. Who may
 * close the day is a role permission (Point of sale › Approve), set under Roles.
 */
export function PosSettings(): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <SettingsCard title={t('posSettings.billing')}>
        <SelectField<string>
          label={t('posSettings.defaultPayment')}
          value={settings.posDefaultPayment}
          onChange={(value) =>
            settings.update({
              posDefaultPayment: value as 'cash' | 'card' | 'upi' | 'online',
            })
          }
          options={[
            { value: 'cash', label: t('pos.cash') },
            { value: 'card', label: t('pos.card') },
            { value: 'upi', label: t('pos.upi') },
            { value: 'online', label: t('pos.online') },
          ]}
        />
        <TextField
          label={t('posSettings.upiMethod')}
          value={settings.posUpiMethod}
          onChange={(posUpiMethod) => settings.update({ posUpiMethod })}
        />
        <CheckField
          label={t('posSettings.askBeforeSave')}
          checked={settings.posAskBeforeSave}
          onChange={(posAskBeforeSave) => settings.update({ posAskBeforeSave })}
        />
        <CheckField
          label={t('posSettings.showCatalog')}
          checked={settings.posShowCatalog}
          onChange={(posShowCatalog) => settings.update({ posShowCatalog })}
        />
      </SettingsCard>

      <SettingsCard title={t('posSettings.printer')}>
        <CheckField
          label={t('posSettings.autoPrint')}
          checked={settings.posAutoPrint}
          onChange={(posAutoPrint) => settings.update({ posAutoPrint })}
        />
        <p className="text-xs text-ink-muted">{t('posSettings.printerHint')}</p>
      </SettingsCard>

      <SettingsCard title={t('posSettings.cashier')}>
        <CheckField
          label={t('posSettings.allowReturns')}
          checked={settings.posAllowReturns}
          onChange={(posAllowReturns) => settings.update({ posAllowReturns })}
        />
        <CheckField
          label={t('posSettings.allowDiscount')}
          checked={settings.posAllowDiscount}
          onChange={(posAllowDiscount) => settings.update({ posAllowDiscount })}
        />
        <CheckField
          label={t('posSettings.allowRateChange')}
          checked={settings.posAllowRateChange}
          onChange={(posAllowRateChange) => settings.update({ posAllowRateChange })}
        />
        <p className="text-xs text-ink-muted">{t('posSettings.dayCloseHint')}</p>
      </SettingsCard>
    </div>
  );
}
