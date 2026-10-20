/** A titled box grouping related fields on an entry screen. */
export function Panel({
  title,
  children,
}: {
  readonly title: string;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <section className="space-y-3 rounded-xl border border-line bg-surface p-4 shadow-card">
      <h3 className="text-sm font-semibold">{title}</h3>
      {children}
    </section>
  );
}
