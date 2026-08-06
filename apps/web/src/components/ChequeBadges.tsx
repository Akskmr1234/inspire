import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import {
  DIRECTION_NAME,
  STATUS_NAME,
  type ChequeDirection,
  type ChequeStatus,
} from '@/lib/cheques';

/**
 * The small coloured pills that carry a cheque's direction and status.
 *
 * Shared because all three reports show direction and the register shows status,
 * and a colour that meant "bounced" on one screen and something else on another
 * would be worse than no colour at all.
 */

const DIRECTION_STYLES: Record<ChequeDirection, string> = {
  1: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300',
  2: 'bg-amber-50 text-amber-700 dark:bg-amber-950 dark:text-amber-300',
};

/** Received or issued, coloured so a mixed list reads at a glance. */
export function DirectionBadge({
  direction,
}: {
  readonly direction: ChequeDirection;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <span
      className={clsx(
        'inline-block rounded px-2 py-0.5 text-xs font-medium',
        DIRECTION_STYLES[direction],
      )}
    >
      {t(`cheques.direction.${DIRECTION_NAME[direction]}`)}
    </span>
  );
}

const STATUS_STYLES: Record<ChequeStatus, string> = {
  1: 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300',
  2: 'bg-sky-50 text-sky-700 dark:bg-sky-950 dark:text-sky-300',
  3: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300',
  4: 'bg-red-50 text-red-700 dark:bg-red-950 dark:text-red-300',
  5: 'bg-amber-50 text-amber-700 dark:bg-amber-950 dark:text-amber-300',
  6: 'bg-slate-100 text-slate-500 line-through dark:bg-slate-800 dark:text-slate-500',
};

/** Where a cheque stands, coloured green through red as it resolves well or badly. */
export function StatusBadge({
  status,
}: {
  readonly status: ChequeStatus;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <span
      className={clsx(
        'inline-block rounded px-2 py-0.5 text-xs font-medium',
        STATUS_STYLES[status],
      )}
    >
      {t(`cheques.status.${STATUS_NAME[status]}`)}
    </span>
  );
}
