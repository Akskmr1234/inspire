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

/*
  The dark half of each pill is a low-alpha tint of the same hue rather than the
  darkest step of its ramp. A `red-950` slab reads as a dark grey rectangle with
  faint warmth, which is the wrong signal for "bounced": the point of the colour is
  that it is legible across the room, and it has to stay that way in both themes.
*/

const DIRECTION_STYLES: Record<ChequeDirection, string> = {
  1: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
  2: 'bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
};

/** Received or issued, coloured so a mixed list reads at a glance. */
export function DirectionBadge({
  direction,
}: {
  readonly direction: ChequeDirection;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <span className={clsx('badge', DIRECTION_STYLES[direction])}>
      {t(`cheques.direction.${DIRECTION_NAME[direction]}`)}
    </span>
  );
}

const STATUS_STYLES: Record<ChequeStatus, string> = {
  1: 'bg-surface-3 text-ink-muted',
  2: 'bg-sky-50 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300',
  3: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
  4: 'bg-red-50 text-red-700 dark:bg-red-500/15 dark:text-red-300',
  5: 'bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
  6: 'bg-surface-3 text-ink-subtle line-through',
};

/** A dot the colour of the status, so the state survives a colour-blind reader. */
const STATUS_DOTS: Record<ChequeStatus, string> = {
  1: 'bg-ink-subtle',
  2: 'bg-sky-500',
  3: 'bg-emerald-500',
  4: 'bg-red-500',
  5: 'bg-amber-500',
  6: 'bg-ink-subtle',
};

/** Where a cheque stands, coloured green through red as it resolves well or badly. */
export function StatusBadge({
  status,
}: {
  readonly status: ChequeStatus;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <span className={clsx('badge', STATUS_STYLES[status])}>
      <span
        aria-hidden="true"
        className={clsx('size-1.5 shrink-0 rounded-full', STATUS_DOTS[status])}
      />
      {t(`cheques.status.${STATUS_NAME[status]}`)}
    </span>
  );
}
