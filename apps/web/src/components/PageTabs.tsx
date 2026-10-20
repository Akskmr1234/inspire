import clsx from 'clsx';

/** Tabs across the top of a screen that does several related things. */
export function PageTabs<TTab extends string>({
  tabs,
  value,
  onChange,
}: {
  readonly tabs: readonly { readonly value: TTab; readonly label: string }[];
  readonly value: TTab;
  readonly onChange: (tab: TTab) => void;
}): React.JSX.Element {
  return (
    <nav role="tablist" className="flex gap-1 overflow-x-auto border-b border-line">
      {tabs.map((tab) => (
        <button
          key={tab.value}
          type="button"
          role="tab"
          aria-selected={tab.value === value}
          onClick={() => onChange(tab.value)}
          className={clsx(
            '-mb-px shrink-0 border-b-2 px-3 py-2.5 text-sm font-medium whitespace-nowrap transition',
            tab.value === value
              ? 'border-brand-600 text-brand-700 dark:border-brand-400 dark:text-brand-300'
              : 'border-transparent text-ink-muted hover:border-line-strong hover:text-ink',
          )}
        >
          {tab.label}
        </button>
      ))}
    </nav>
  );
}
