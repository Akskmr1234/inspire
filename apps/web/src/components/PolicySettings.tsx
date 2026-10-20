import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { CheckField, SelectField } from '@/components/Form';
import type { ApiError } from '@/lib/api';
import {
  CreditLimitPolicy,
  DEFAULT_POLICIES,
  getPolicies,
  NegativeStockPolicy,
  savePolicies,
  type FirmPolicies,
} from '@/lib/policies';
import { useSession } from '@/stores/session';

/**
 * The rules the server enforces, as one card.
 *
 * Unlike every other preference on the settings screen these are not the browser's to
 * keep: whether stock may go below zero or a credit limit blocks a sale decides what a
 * document is allowed to do, so they are saved to the server, which reads them when a
 * document posts. Each change is saved as it is made, like the rest of the screen.
 */
export function PolicySettings(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const editable = useSession((state) => state.can)('platform:setting:edit');

  const query = useQuery<FirmPolicies, ApiError>({
    queryKey: ['firm-policies'],
    queryFn: getPolicies,
  });

  const mutation = useMutation<void, ApiError, FirmPolicies>({
    mutationFn: savePolicies,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['firm-policies'] });
    },
  });

  const policies =
    mutation.isPending && mutation.variables
      ? mutation.variables
      : (query.data ?? DEFAULT_POLICIES);
  const disabled = !editable || query.isPending || mutation.isPending;

  const change = (patch: Partial<FirmPolicies>): void =>
    mutation.mutate({ ...policies, ...patch });

  return (
    <div className="space-y-3">
      {!editable && <p className="text-xs text-ink-muted">{t('policies.readOnly')}</p>}
      {mutation.isError && (
        <p className="alert-warn text-sm">
          {mutation.error.detail || mutation.error.code}
        </p>
      )}

      <SelectField
        label={t('policies.negativeStock')}
        hint={t('policies.negativeStockHint')}
        value={policies.negativeStock}
        disabled={disabled}
        onChange={(negativeStock) => change({ negativeStock })}
        options={[
          { value: NegativeStockPolicy.block, label: t('policies.block') },
          { value: NegativeStockPolicy.warn, label: t('policies.warn') },
          { value: NegativeStockPolicy.allow, label: t('policies.ignore') },
        ]}
      />

      <SelectField
        label={t('policies.creditLimit')}
        hint={t('policies.creditLimitHint')}
        value={policies.creditLimit}
        disabled={disabled}
        onChange={(creditLimit) => change({ creditLimit })}
        options={[
          { value: CreditLimitPolicy.ignore, label: t('policies.ignore') },
          { value: CreditLimitPolicy.warn, label: t('policies.warn') },
          { value: CreditLimitPolicy.block, label: t('policies.block') },
        ]}
      />

      <CheckField
        label={t('policies.transfersRequireApproval')}
        checked={policies.transfersRequireApproval}
        disabled={disabled}
        onChange={(transfersRequireApproval) => change({ transfersRequireApproval })}
      />
      <p className="-mt-2 text-xs text-ink-muted">
        {t('policies.transfersRequireApprovalHint')}
      </p>

      <CheckField
        label={t('policies.purchasesUpdateProductRates')}
        checked={policies.purchasesUpdateProductRates}
        disabled={disabled}
        onChange={(purchasesUpdateProductRates) =>
          change({ purchasesUpdateProductRates })
        }
      />
      <p className="-mt-2 text-xs text-ink-muted">
        {t('policies.purchasesUpdateProductRatesHint')}
      </p>
    </div>
  );
}
