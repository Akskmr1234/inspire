import { useTranslation } from 'react-i18next';
import { describe, ENTRY_SHORTCUTS, type EntryAction } from '@/lib/shortcuts';

/**
 * The keys an entry screen answers to, in a line along its foot: learnt by being in
 * front of the person on every bill rather than in a list nobody opens.
 *
 * Wide screens only. On a phone there is no keyboard to press them with.
 */
export function ShortcutHints({
  actions,
}: {
  readonly actions: readonly EntryAction[];
}): React.JSX.Element {
  const { t } = useTranslation();

  const hint = (keys: string, label: string): React.JSX.Element => (
    <li key={keys} className="flex items-center gap-1 whitespace-nowrap">
      <kbd className="rounded border border-line bg-surface-2 px-1.5 py-px font-mono text-[10px] text-ink shadow-xs">
        {keys}
      </kbd>
      <span>{label}</span>
    </li>
  );

  return (
    <ul
      aria-label={t('shortcuts.title')}
      className="hidden min-w-0 flex-1 flex-wrap items-center gap-x-3 gap-y-1 text-[11px] text-ink-muted lg:flex"
    >
      {actions.map((action) => {
        const shortcut = ENTRY_SHORTCUTS.find((candidate) => candidate.action === action);
        return shortcut ? hint(describe(shortcut), t(`shortcuts.hint.${action}`)) : null;
      })}
      {hint(describe({ code: 'Slash', alt: true }), t('shortcuts.hint.all'))}
    </ul>
  );
}
