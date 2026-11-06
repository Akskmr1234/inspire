import { useTranslation } from 'react-i18next';
import { BranchSettings } from '@/components/settings/BranchSettings';
import { FirmSettings } from '@/components/settings/FirmSettings';
import { useSearchParams } from 'react-router-dom';
import { PageHeading } from '@/components/PageHeading';
import { PageTabs } from '@/components/PageTabs';
import {
  AccountsSettings,
  AdditionalLedgerSettings,
  DataSettings,
  GeneralSettings,
  InventorySettings,
  NumberingTab,
  PrintingSettings,
  PurchaseSettings,
  SalesSettings,
} from '@/components/settings/SettingsSections';
import { useSettings } from '@/stores/settings';

/**
 * The tabs, in the order a firm setting itself up works through them. What a new
 * document starts with sits with the documents it is for - the default customer under
 * Sales, the default supplier under Purchase - rather than in a tab of defaults of its own.
 */
const TABS = [
  'general',
  'sales',
  'purchase',
  'inventory',
  'accounts',
  'charges',
  'firms',
  'branches',
  'printing',
  'numbering',
  'data',
] as const;

type Tab = (typeof TABS)[number];

/**
 * The preferences screen, in tabs.
 *
 * Grouped by the part of the business a setting governs - what a sale does, what a
 * purchase does, what stock may do - rather than by how it is stored, so whoever is
 * setting up sales finds everything about sales in one place. The tab is in the
 * address, so a link can send somebody straight to the setting they need.
 *
 * Saved as each control is changed rather than behind a Save button. There is nothing
 * here that is only half-true midway through, and a settings screen that can be left
 * with unsaved changes is a settings screen people leave with unsaved changes.
 */
export function SettingsPage(): React.JSX.Element {
  const { t } = useTranslation();
  const sync = useSettings((state) => state.sync);
  const [params, setParams] = useSearchParams();

  const asked = params.get('tab');
  const tab: Tab = TABS.includes(asked as Tab) ? (asked as Tab) : 'general';

  return (
    <section className="page">
      <PageHeading title={t('settings.title')} />

      {sync === 'read-only' && (
        <p className="alert-warn text-sm">{t('settings.storedReadOnly')}</p>
      )}

      <PageTabs
        tabs={TABS.map((value) => ({ value, label: t(`prefs.tabs.${value}`) }))}
        value={tab}
        onChange={(next) =>
          setParams(next === 'general' ? {} : { tab: next }, { replace: true })
        }
      />

      {tab === 'general' && <GeneralSettings />}
      {tab === 'sales' && <SalesSettings />}
      {tab === 'purchase' && <PurchaseSettings />}
      {tab === 'inventory' && <InventorySettings />}
      {tab === 'accounts' && <AccountsSettings />}
      {tab === 'charges' && <AdditionalLedgerSettings />}
      {tab === 'firms' && <FirmSettings />}
      {tab === 'branches' && <BranchSettings />}
      {tab === 'printing' && <PrintingSettings />}
      {tab === 'numbering' && <NumberingTab />}
      {tab === 'data' && <DataSettings />}

      {/* Only when something is out of the ordinary; read-only is said at the top. */}
      {sync !== 'firm' && sync !== 'read-only' && (
        <p className="alert-warn text-xs">{t('settings.storedLocally')}</p>
      )}
    </section>
  );
}
