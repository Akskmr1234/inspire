import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import clsx from 'clsx';
import { SearchSelect } from '@/components/SearchSelect';
import { IconClose } from '@/components/icons';
import { chargeRows, offeredCharges, type ChargeSetup } from '@/lib/charges';
import type { LedgerSummary } from '@/lib/ledgers';
import { useSession } from '@/stores/session';

/**
 * A document's additional ledgers, one to a line: the ledger, which way it moves the
 * total, and its amount, in the same columns on every screen.
 *
 * The ledgers Settings marks as defaults are always here; any other ledger set up for
 * the document is picked from the list underneath when it is wanted, and can be taken
 * off again. A blank amount is simply not charged.
 */
export function AdditionalLedgerList({
  carried,
  setup,
  nonTax,
  ledgers,
  onAmount,
  onRemove,
  disabled = false,
}: {
  /** What the document carries: picked ledgers and typed amounts. */
  readonly carried: readonly { readonly ledgerId: string; readonly amount: string }[];
  readonly setup: readonly ChargeSetup[] | undefined;
  readonly nonTax: boolean;
  readonly ledgers: readonly LedgerSummary[];
  /** Sets a ledger's amount, adding it to the document when it is not on it yet. */
  readonly onAmount: (ledgerId: string, amount: string, adds: boolean) => void;
  /** Takes a picked ledger off the document. */
  readonly onRemove: (ledgerId: string) => void;
  readonly disabled?: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const can = useSession((state) => state.can);

  const rows = useMemo(
    () => chargeRows(setup, nonTax, carried, ledgers),
    [setup, nonTax, carried, ledgers],
  );
  const offered = useMemo(
    () => offeredCharges(setup, nonTax, rows),
    [setup, nonTax, rows],
  );

  const amountOf = (ledgerId: string): string =>
    carried.find((charge) => charge.ledgerId === ledgerId)?.amount ?? '';

  if (rows.length === 0 && offered.length === 0) {
    return (
      <p className="py-1 text-xs text-ink-muted">
        {setup === undefined ? t('common.loading') : t('documents.noChargesSetUp')}{' '}
        {setup !== undefined && can('platform:setting:view') && (
          <Link to="/settings?tab=charges" className="font-medium underline">
            {t('documents.setUpCharges')}
          </Link>
        )}
      </p>
    );
  }

  return (
    <div className="space-y-1.5">
      {rows.map((row) => (
        <div key={row.ledgerId} className="flex items-center gap-2 text-sm">
          <span className="min-w-0 flex-1 truncate text-ink" title={row.label}>
            {row.label}
          </span>
          <span
            aria-label={
              row.adds ? t('documents.chargeAdds') : t('documents.chargeDeducts')
            }
            className={clsx(
              'w-4 shrink-0 text-center font-bold',
              row.adds
                ? 'text-emerald-700 dark:text-emerald-400'
                : 'text-red-600 dark:text-red-400',
            )}
          >
            {row.adds ? '+' : '−'}
          </span>
          <input
            type="number"
            min="0"
            step="any"
            inputMode="decimal"
            placeholder="0.00"
            aria-label={row.label}
            value={amountOf(row.ledgerId)}
            disabled={disabled}
            onChange={(event) => onAmount(row.ledgerId, event.target.value, row.adds)}
            className="field-input-sm w-28 shrink-0 text-end font-mono tabular-nums"
          />
          <span className="flex w-6 shrink-0 justify-center">
            {row.removable && !disabled && (
              <button
                type="button"
                aria-label={t('documents.removeCharge')}
                title={t('documents.removeCharge')}
                onClick={() => onRemove(row.ledgerId)}
                className="rounded p-1 text-ink-subtle hover:text-red-600"
              >
                <IconClose className="size-3.5" />
              </button>
            )}
          </span>
        </div>
      ))}

      {offered.length > 0 && !disabled && (
        <SearchSelect
          value=""
          onChange={(ledgerId) => {
            const picked = offered.find((charge) => charge.ledgerId === ledgerId);
            if (picked) onAmount(picked.ledgerId, '', picked.isAddition);
          }}
          options={offered.map((charge) => ({
            value: charge.ledgerId,
            label: `${charge.code} — ${charge.name}`,
            detail: charge.isAddition
              ? t('documents.chargeAdds')
              : t('documents.chargeDeducts'),
          }))}
          size="sm"
          label={t('documents.addCharge')}
          placeholder={`+ ${t('documents.addCharge')}`}
        />
      )}
    </div>
  );
}
