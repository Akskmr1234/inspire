import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { PageHeading } from '@/components/PageHeading';
import { Modal, ModalButton } from '@/components/Modal';
import { Spinner } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';
import { applyPrimaryDefaults } from '@/lib/primaryDefaults';
import { useSettings } from '@/stores/settings';

const Scope = { transactions: 1, everything: 2 } as const;
type ScopeValue = (typeof Scope)[keyof typeof Scope];

interface Preview {
  readonly firmName: string;
  readonly firmCode: string;
  readonly transactions: Readonly<Record<string, number>>;
  readonly masters: Readonly<Record<string, number>>;
}

interface ResetResult {
  readonly scope: ScopeValue;
  readonly removed: Readonly<Record<string, number>>;
}

/**
 * Clears the firm's data, to hand it to a new customer or to test from nothing.
 *
 * Two levels, each counted before anybody confirms it: the transactions alone, which keep
 * the customers, suppliers and products for a fresh start of trading; or everything the
 * firm entered, which leaves only what setup created. Either one asks for the firm's name
 * typed out, because neither can be undone.
 */
export function DataResetPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState<ScopeValue | null>(null);
  const [done, setDone] = useState<ResetResult | null>(null);

  const preview = useQuery<Preview, ApiError>({
    queryKey: ['data-reset-preview'],
    queryFn: () => request<Preview>('/platform/data'),
  });

  const finished = (result: ResetResult): void => {
    setConfirming(null);
    setDone(result);

    // The defaults named masters that no longer exist; a picker pointing at nothing would
    // show an empty choice that looks chosen. The Primary records are still there, so
    // they become the defaults again.
    if (result.scope === Scope.everything) {
      useSettings.getState().update({
        defaultCustomerId: '',
        defaultSupplierId: '',
        preferredWarehouseId: '',
        defaultCategoryId: '',
        defaultStockUnitId: '',
        defaultBrandId: '',
        primaryDefaultsApplied: false,
      });
      void applyPrimaryDefaults();
    }

    // Everything on screen was read before the reset.
    void queryClient.invalidateQueries();
  };

  return (
    <section className="page">
      <PageHeading title={t('dataReset.title')} />

      <p className="alert-warn">{t('dataReset.warning')}</p>

      {done && (
        <div className="alert-success space-y-1">
          <p className="font-semibold">
            {done.scope === Scope.everything
              ? t('dataReset.everythingDone')
              : t('dataReset.transactionsDone')}
          </p>
          <p className="text-xs">
            {Object.entries(done.removed)
              .map(([table, count]) => `${table.replaceAll('_', ' ')}: ${count}`)
              .join(' · ') || t('dataReset.nothingRemoved')}
          </p>
        </div>
      )}

      {preview.isPending && <Spinner />}
      {preview.isError && (
        <p className="alert-error">{preview.error.detail || preview.error.code}</p>
      )}

      {preview.data && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <ResetCard
            title={t('dataReset.transactionsTitle')}
            body={t('dataReset.transactionsBody')}
            counts={preview.data.transactions}
            action={t('dataReset.clearTransactions')}
            onClick={() => setConfirming(Scope.transactions)}
          />
          <ResetCard
            title={t('dataReset.everythingTitle')}
            body={t('dataReset.everythingBody')}
            counts={{ ...preview.data.transactions, ...preview.data.masters }}
            action={t('dataReset.clearEverything')}
            onClick={() => setConfirming(Scope.everything)}
          />
        </div>
      )}

      {confirming !== null && preview.data && (
        <ConfirmDialog
          scope={confirming}
          firmName={preview.data.firmName}
          onClose={() => setConfirming(null)}
          onDone={finished}
        />
      )}
    </section>
  );
}

function ResetCard({
  title,
  body,
  counts,
  action,
  onClick,
}: {
  readonly title: string;
  readonly body: string;
  readonly counts: Readonly<Record<string, number>>;
  readonly action: string;
  readonly onClick: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <div className="panel flex flex-col gap-3">
      <h3 className="text-base font-semibold text-ink">{title}</h3>
      <p className="text-sm text-ink-muted">{body}</p>
      <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
        {Object.entries(counts).map(([kind, count]) => (
          <div key={kind} className="contents">
            <dt className="text-ink-muted">{t(`dataReset.kinds.${kind}`)}</dt>
            <dd className="text-end font-mono tabular-nums">{count}</dd>
          </div>
        ))}
      </dl>
      <div className="mt-auto flex justify-end">
        <button type="button" onClick={onClick} className="btn-danger btn-sm">
          {action}
        </button>
      </div>
    </div>
  );
}

function ConfirmDialog({
  scope,
  firmName,
  onClose,
  onDone,
}: {
  readonly scope: ScopeValue;
  readonly firmName: string;
  readonly onClose: () => void;
  readonly onDone: (result: ResetResult) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [typed, setTyped] = useState('');

  const reset = useMutation<ResetResult, ApiError>({
    mutationFn: () =>
      request<ResetResult>('/platform/data/reset', {
        method: 'POST',
        body: { scope, confirmation: typed.trim() },
      }),
    onSuccess: onDone,
  });

  const matches = typed.trim().toLowerCase() === firmName.toLowerCase();

  return (
    <Modal
      title={
        scope === Scope.everything
          ? t('dataReset.clearEverything')
          : t('dataReset.clearTransactions')
      }
      size="form"
      onClose={onClose}
    >
      <p className="text-sm">{t('dataReset.confirmHint', { name: firmName })}</p>
      <input
        autoFocus
        value={typed}
        onChange={(event) => setTyped(event.target.value)}
        aria-label={t('dataReset.confirmLabel')}
        placeholder={firmName}
        className="field-input"
      />
      {reset.isError && (
        <p className="alert-error">{reset.error.detail || reset.error.code}</p>
      )}
      <div className="form-actions">
        <ModalButton onClick={onClose}>{t('common.cancel')}</ModalButton>
        <ModalButton
          primary
          disabled={!matches || reset.isPending}
          onClick={() => reset.mutate()}
        >
          {reset.isPending ? t('dataReset.clearing') : t('dataReset.confirm')}
        </ModalButton>
      </div>
    </Modal>
  );
}
