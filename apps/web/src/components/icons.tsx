/**
 * The icon set.
 *
 * Drawn here rather than pulled from a package. An ERP ships to sites that install
 * from a private registry or from a tarball, and a hundred kilobytes of icon
 * dependency to draw the twenty glyphs this navigation actually uses is a poor
 * trade. They are stroke-based on a 24-unit grid with round joins, so they sit at
 * the same visual weight as the medium-weight text beside them.
 *
 * Every path is geometry only — no fills, no baked-in colour — so a glyph takes the
 * colour of whatever it is placed in and therefore follows the theme for free.
 */

/*
  `| undefined` is written out rather than left to the `?`. The project compiles with
  exactOptionalPropertyTypes, under which an optional property may be absent but may
  not be present and undefined — so passing a `className` that is sometimes undefined
  is an error without it.
*/
export interface IconProps {
  readonly className?: string | undefined;
}

function Svg({
  className,
  children,
}: {
  readonly className?: string | undefined;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      // Decorative: every icon in this application sits beside its own label, so
      // announcing it again would just make a screen reader say everything twice.
      aria-hidden="true"
      focusable="false"
      className={className ?? 'size-[18px] shrink-0'}
    >
      {children}
    </svg>
  );
}

export function IconGrid({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <rect x="3" y="3" width="7" height="7" rx="1.5" />
      <rect x="14" y="3" width="7" height="7" rx="1.5" />
      <rect x="3" y="14" width="7" height="7" rx="1.5" />
      <rect x="14" y="14" width="7" height="7" rx="1.5" />
    </Svg>
  );
}

export function IconBook({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H20v15H6.5A2.5 2.5 0 0 0 4 20.5z" />
      <path d="M4 20.5A2.5 2.5 0 0 1 6.5 18H20v3H6.5" />
    </Svg>
  );
}

export function IconScale({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M12 4v16" />
      <path d="M7 20h10" />
      <path d="M5 7h14" />
      <path d="M5 7 2.5 13h5z" />
      <path d="M19 7l-2.5 6h5z" />
    </Svg>
  );
}

export function IconReceipt({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M5 3h14v18l-2.5-1.6L14 21l-2-1.6L10 21l-2.5-1.6L5 21z" />
      <path d="M9 8h6" />
      <path d="M9 12h6" />
    </Svg>
  );
}

export function IconChart({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M3 21h18" />
      <rect x="5" y="11" width="4" height="7" rx="1" />
      <rect x="10" y="6" width="4" height="12" rx="1" />
      <rect x="15" y="14" width="4" height="4" rx="1" />
    </Svg>
  );
}

export function IconCash({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <rect x="2.5" y="6" width="19" height="12" rx="2" />
      <circle cx="12" cy="12" r="2.5" />
      <path d="M6 10v4" />
      <path d="M18 10v4" />
    </Svg>
  );
}

export function IconBank({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M3 10 12 4l9 6" />
      <path d="M5 10v8" />
      <path d="M10 10v8" />
      <path d="M14 10v8" />
      <path d="M19 10v8" />
      <path d="M3 21h18" />
    </Svg>
  );
}

export function IconFlow({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M4 8h13" />
      <path d="m14 5 3 3-3 3" />
      <path d="M20 16H7" />
      <path d="m10 13-3 3 3 3" />
    </Svg>
  );
}

export function IconPercent({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M19 5 5 19" />
      <circle cx="7.5" cy="7.5" r="2.5" />
      <circle cx="16.5" cy="16.5" r="2.5" />
    </Svg>
  );
}

export function IconTag({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M3 12.5V4a1 1 0 0 1 1-1h8.5a1 1 0 0 1 .7.3l7.5 7.5a1 1 0 0 1 0 1.4l-8.5 8.5a1 1 0 0 1-1.4 0L3.3 13.2a1 1 0 0 1-.3-.7z" />
      <circle cx="8" cy="8" r="1.4" />
    </Svg>
  );
}

export function IconCart({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M2.5 4h2l2.2 10.2a1.5 1.5 0 0 0 1.5 1.2h8.4a1.5 1.5 0 0 0 1.5-1.2L20 7H5.5" />
      <circle cx="9" cy="19.5" r="1.4" />
      <circle cx="17" cy="19.5" r="1.4" />
    </Svg>
  );
}

export function IconInbox({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M3 13h5l1.5 3h5L16 13h5" />
      <path d="M5.5 4h13l2.5 9v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-5z" />
    </Svg>
  );
}

export function IconBox({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M12 3 20.5 7.5v9L12 21l-8.5-4.5v-9z" />
      <path d="m3.5 7.5 8.5 4.5 8.5-4.5" />
      <path d="M12 12v9" />
    </Svg>
  );
}

export function IconWarehouse({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M3 21V9l9-5 9 5v12" />
      <path d="M3 21h18" />
      <rect x="8" y="13" width="8" height="8" rx="1" />
    </Svg>
  );
}

export function IconUsers({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <circle cx="9" cy="8" r="3.5" />
      <path d="M2.5 20a6.5 6.5 0 0 1 13 0" />
      <path d="M16.5 5.2a3.5 3.5 0 0 1 0 5.6" />
      <path d="M18.5 14.6A6.5 6.5 0 0 1 21.5 20" />
    </Svg>
  );
}

export function IconTruck({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M2.5 16V6h11v10" />
      <path d="M13.5 9h4l4 4v3h-2" />
      <path d="M2.5 16h2" />
      <path d="M9.5 16h5" />
      <circle cx="6.5" cy="17.5" r="1.8" />
      <circle cx="17.5" cy="17.5" r="1.8" />
    </Svg>
  );
}

export function IconCheque({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <rect x="2.5" y="5" width="19" height="14" rx="2" />
      <path d="M6 10h6" />
      <path d="m14.5 14.5 2 2 4-4.5" />
    </Svg>
  );
}

export function IconCalendar({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <rect x="3" y="5" width="18" height="16" rx="2" />
      <path d="M3 10h18" />
      <path d="M8 3v4" />
      <path d="M16 3v4" />
    </Svg>
  );
}

export function IconLayers({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="m12 3 9 5-9 5-9-5z" />
      <path d="m3 13 9 5 9-5" />
    </Svg>
  );
}

export function IconSettings({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <circle cx="12" cy="12" r="3.2" />
      <path d="M12 2v3" />
      <path d="M12 19v3" />
      <path d="M2 12h3" />
      <path d="M19 12h3" />
      <path d="m5 5 2.1 2.1" />
      <path d="M16.9 16.9 19 19" />
      <path d="M19 5l-2.1 2.1" />
      <path d="M7.1 16.9 5 19" />
    </Svg>
  );
}

export function IconMenu({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M4 6h16" />
      <path d="M4 12h16" />
      <path d="M4 18h16" />
    </Svg>
  );
}

export function IconClose({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M6 6 18 18" />
      <path d="M18 6 6 18" />
    </Svg>
  );
}

export function IconChevron({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="m9 6 6 6-6 6" />
    </Svg>
  );
}

export function IconSun({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2" />
      <path d="M12 20v2" />
      <path d="M2 12h2" />
      <path d="M20 12h2" />
      <path d="m4.9 4.9 1.4 1.4" />
      <path d="m17.7 17.7 1.4 1.4" />
      <path d="m19.1 4.9-1.4 1.4" />
      <path d="m6.3 17.7-1.4 1.4" />
    </Svg>
  );
}

export function IconMoon({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5" />
    </Svg>
  );
}

export function IconLogout({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <path d="M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4" />
      <path d="M10 8 6 12l4 4" />
      <path d="M6 12h9" />
    </Svg>
  );
}

export function IconDot({ className }: IconProps): React.JSX.Element {
  return (
    <Svg className={className}>
      <circle cx="12" cy="12" r="3" />
    </Svg>
  );
}

/**
 * Picks a glyph for a menu entry.
 *
 * The menu carries an `icon` name an administrator can set, so that is honoured
 * first. Where it is unset — which is most entries, since nothing in the seed
 * populates it — the route is matched instead, longest and most specific patterns
 * first, so `/accounting/cash-flow` gets the flow arrows rather than the banknote
 * that `cash` alone would have won.
 *
 * Falls back to a dot. An entry with no recognisable route still needs something
 * occupying the icon column, or the labels beside it stop lining up.
 */
const BY_NAME: Record<string, (props: IconProps) => React.JSX.Element> = {
  grid: IconGrid,
  dashboard: IconGrid,
  book: IconBook,
  scale: IconScale,
  receipt: IconReceipt,
  chart: IconChart,
  cash: IconCash,
  bank: IconBank,
  flow: IconFlow,
  percent: IconPercent,
  tag: IconTag,
  cart: IconCart,
  inbox: IconInbox,
  box: IconBox,
  warehouse: IconWarehouse,
  users: IconUsers,
  truck: IconTruck,
  cheque: IconCheque,
  calendar: IconCalendar,
  layers: IconLayers,
  settings: IconSettings,
};

const BY_ROUTE: readonly (readonly [string, (props: IconProps) => React.JSX.Element])[] =
  [
    ['/dashboard', IconGrid],
    ['cheque-calendar', IconCalendar],
    ['cheque', IconCheque],
    ['cash-flow', IconFlow],
    ['cash-book', IconCash],
    ['bank-book', IconBank],
    ['trial-balance', IconScale],
    ['balance-sheet', IconScale],
    ['profit-and-loss', IconChart],
    ['account-group-summary', IconChart],
    ['transaction-summary', IconChart],
    ['tax-returns', IconPercent],
    ['voucher', IconReceipt],
    ['day-book', IconBook],
    ['ledgers', IconBook],
    ['customers', IconUsers],
    ['suppliers', IconTruck],
    ['sales', IconCart],
    ['purchase', IconInbox],
    ['products', IconTag],
    ['warehouses', IconWarehouse],
    ['categories', IconLayers],
    ['brands', IconLayers],
    ['units', IconLayers],
    ['expiry', IconCalendar],
    ['valuation', IconChart],
    ['stock', IconBox],
    ['batch', IconBox],
    ['item-movement', IconFlow],
    ['inventory', IconBox],
    ['settings', IconSettings],
    ['accounting', IconBook],
  ];

export function iconFor(
  icon: string | null,
  route: string | null,
): (props: IconProps) => React.JSX.Element {
  const named = icon ? BY_NAME[icon.toLowerCase()] : undefined;

  if (named) {
    return named;
  }

  if (route) {
    const match = BY_ROUTE.find(([pattern]) => route.includes(pattern));

    if (match) {
      return match[1];
    }
  }

  return IconDot;
}
