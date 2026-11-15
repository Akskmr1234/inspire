import { useTranslation } from 'react-i18next';
import { PageHeading } from '@/components/PageHeading';
import {
  OnlinePaymentTypes,
  useAllLedgers,
} from '@/components/settings/SettingsSections';

/**
 * The online payment master: Google Pay, PhonePe, Paytm, Talabat Pay and any other way
 * customers pay online, each with the bank account it settles into. The active ones are
 * offered as the Online payment type wherever a sale is paid online.
 */
export function OnlinePaymentTypesPage(): React.JSX.Element {
  const { t } = useTranslation();
  const ledgers = useAllLedgers();

  return (
    <section className="page">
      <PageHeading title={t('prefs.onlineTypesTitle')} />
      <div className="card card-body space-y-3">
        <OnlinePaymentTypes ledgers={ledgers} />
      </div>
    </section>
  );
}
