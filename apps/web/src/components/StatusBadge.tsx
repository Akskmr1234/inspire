import clsx from 'clsx';
import { useTranslation } from 'react-i18next';

/**
 * One badge for every state in the application.
 *
 * The same fact was reading two ways. A purchase invoice that had not been posted
 * was an amber pill on the purchase list; a customer that had been withdrawn was
 * grey body text on the customer list, in the same column position, under the same
 * "Status" heading. Both are "this record is not in its ordinary state", and a
 * reader who learns the colour on one screen learns nothing that carries to the
 * next. Worse, the plain-text ones disappear: down a list of two hundred customers
 * the withdrawn one is a word among words, which is exactly the row the eye is
 * looking for.
 *
 * So every status goes through here. The screen decides what the state means — only
 * it knows that a discontinued product is still sold while a withdrawn one is not —
 * and this decides how a state of that weight is drawn, once, for all of them.
 */

/**
 * How much the state should stand out, in the order it usually resolves.
 *
 * Deliberately not named after colours. `danger` on a cancelled invoice is not
 * saying "red", it is saying "this one did not complete", and a screen choosing
 * `danger` for that reason keeps choosing correctly if the palette ever moves.
 */
export type StatusTone = 'neutral' | 'brand' | 'success' | 'warn' | 'danger' | 'info';

/*
  A dot in the pill's own colour, alongside the word.

  Colour is doing real work here — it is what makes a cancelled row findable at a
  glance — and roughly one man in twelve cannot use it. The dot is not a second
  encoding of the state so much as an anchor: it puts the hue in a shape of its own
  next to the label, which is what makes the difference between two pale tints
  visible when the tints themselves are not distinguishable.
*/
const DOTS: Record<StatusTone, string> = {
  neutral: 'bg-ink-subtle',
  brand: 'bg-brand-500',
  success: 'bg-emerald-500',
  warn: 'bg-amber-500',
  danger: 'bg-red-500',
  info: 'bg-sky-500',
};

export function StatusBadge({
  tone,
  label,
  struck = false,
}: {
  readonly tone: StatusTone;
  readonly label: string;
  /** For a state that voids the record — a cancelled document, a withdrawn master. */
  readonly struck?: boolean;
}): React.JSX.Element {
  return (
    <span className={clsx(`badge badge-${tone}`, struck && 'line-through')}>
      <span
        aria-hidden="true"
        className={clsx('size-1.5 shrink-0 rounded-full', DOTS[tone])}
      />
      {label}
    </span>
  );
}

/**
 * Active or withdrawn, for the masters — which is most of the screens here.
 *
 * Withdrawn is struck through rather than merely grey: the record is still listed
 * and still opens, but nothing new can be booked against it, and the strike is the
 * one typographic convention that says "on the page, not in play".
 */
export function ActiveBadge({
  isActive,
}: {
  readonly isActive: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();

  return isActive ? (
    <StatusBadge tone="success" label={t('masters.active')} />
  ) : (
    <StatusBadge tone="neutral" label={t('masters.withdrawn')} struck />
  );
}
