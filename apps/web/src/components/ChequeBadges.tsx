import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import {
  DIRECTION_NAME,
  STATUS_NAME,
  type ChequeDirection,
  type ChequeStatus,
} from '@/lib/cheques';
import { StatusBadge as Badge, type StatusTone } from '@/components/StatusBadge';

/**
 * The small coloured pills that carry a cheque's direction and status.
 *
 * Shared because all three reports show direction and the register shows status,
 * and a colour that meant "bounced" on one screen and something else on another
 * would be worse than no colour at all.
 */

/*
  Direction keeps a pill of its own rather than going through `StatusBadge`: it is
  not a state — a cheque does not move from received to issued — and giving it the
  same dotted badge as the status beside it would make two different kinds of fact
  look like one, on the one screen that shows both in adjacent columns.

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

/**
 * Where a cheque stands, green through red as it resolves well or badly.
 *
 * Six states rather than the usual three, but they map onto the same weights every
 * other list here uses: pending is merely waiting, deposited is under way, cleared
 * is done, bounced is the one that needs somebody today, stopped is the one somebody
 * decided about, and a cancelled cheque is struck through as a cancelled invoice is.
 */
const STATUS_TONES: Record<ChequeStatus, StatusTone> = {
  1: 'neutral',
  2: 'info',
  3: 'success',
  4: 'danger',
  5: 'warn',
  6: 'neutral',
};

export function StatusBadge({
  status,
}: {
  readonly status: ChequeStatus;
}): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <Badge
      tone={STATUS_TONES[status]}
      label={t(`cheques.status.${STATUS_NAME[status]}`)}
      struck={status === 6}
    />
  );
}
