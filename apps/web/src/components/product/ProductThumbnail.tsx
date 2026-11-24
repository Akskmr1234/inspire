import { useEffect, useRef, useState } from 'react';
import * as Popover from '@radix-ui/react-popover';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import type { ApiError } from '@/lib/api';
import { getProductPhotos, hasThumbnails } from '@/lib/photos';

const SIZES = {
  xs: 'size-6',
  sm: 'size-8',
  md: 'size-12',
} as const;

/**
 * A product's small picture, wherever a product is named: a line, a list, a report.
 *
 * Pointing at it (or tapping it) opens its main photograph, larger. The photograph is
 * fetched only then, and shares its cache with the product's photo tab, so a picture
 * changed there shows here too. A product without a picture gets an empty frame, so a
 * column of them still lines up.
 */
export function ProductThumbnail({
  productId,
  src,
  name,
  size = 'sm',
  className,
}: {
  readonly productId: string;
  readonly src: string | null | undefined;
  /** What the picture shows: the product's description. */
  readonly name: string;
  readonly size?: keyof typeof SIZES;
  readonly className?: string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const timer = useRef<number | undefined>(undefined);

  useEffect(() => () => window.clearTimeout(timer.current), []);

  const box = SIZES[size];

  if (!src) {
    return (
      <span
        aria-hidden="true"
        className={clsx(
          box,
          'inline-flex shrink-0 items-center justify-center rounded-md bg-surface-3 text-ink-subtle',
          className,
        )}
      >
        <svg
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={1.75}
          strokeLinecap="round"
          strokeLinejoin="round"
          className="size-1/2"
        >
          <rect x="3" y="3" width="18" height="18" rx="2" />
          <circle cx="9" cy="9" r="2" />
          <path d="m21 15-3.1-3.1a2 2 0 0 0-2.8 0L6 21" />
        </svg>
      </span>
    );
  }

  // A short pause before opening, so a pointer passing over a list does not flash a
  // picture for every row it crosses; and before closing, so it can move onto the
  // picture without it vanishing.
  const later = (next: boolean): void => {
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setOpen(next), next ? 250 : 120);
  };

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger asChild>
        <button
          type="button"
          // Out of the tab order: a line is filled in from the keyboard, and a stop on
          // every picture would get in the way. The picture adds nothing a reader
          // without it is missing; the name is beside it.
          tabIndex={-1}
          aria-label={t('products.photoOf', { name })}
          onMouseEnter={() => later(true)}
          onMouseLeave={() => later(false)}
          onMouseDown={(event) => event.preventDefault()}
          onClick={(event) => {
            event.stopPropagation();
            window.clearTimeout(timer.current);
            setOpen((current) => !current);
          }}
          className={clsx('shrink-0 cursor-zoom-in rounded-md', className)}
        >
          <img
            src={src}
            alt=""
            loading="lazy"
            draggable={false}
            className={clsx(box, 'rounded-md bg-white object-cover ring-1 ring-line')}
          />
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          side="right"
          align="center"
          sideOffset={8}
          collisionPadding={12}
          onOpenAutoFocus={(event) => event.preventDefault()}
          onCloseAutoFocus={(event) => event.preventDefault()}
          onMouseEnter={() => window.clearTimeout(timer.current)}
          onMouseLeave={() => later(false)}
          className="animate-drop z-[80] rounded-2xl border border-line/80 bg-surface p-2 shadow-2xl ring-1 ring-black/10 dark:border-white/10 dark:ring-white/10"
        >
          <Preview productId={productId} small={src} name={name} />
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}

/**
 * A product's name with its picture before it, for a table or a list. A firm that keeps
 * no pictures gets the name alone, exactly as before.
 */
export function ProductPictureLabel({
  productId,
  name,
  thumbnails,
  size = 'sm',
  children,
}: {
  readonly productId: string;
  readonly name: string;
  /** Every product's thumbnail, from `useProductThumbnails`. */
  readonly thumbnails: Readonly<Record<string, string>>;
  readonly size?: keyof typeof SIZES;
  /** What to show beside the picture; the name when left out. */
  readonly children?: React.ReactNode;
}): React.JSX.Element {
  if (!hasThumbnails(thumbnails)) {
    return <>{children ?? name}</>;
  }

  return (
    <span className="flex min-w-0 items-center gap-2">
      <ProductThumbnail
        productId={productId}
        src={thumbnails[productId]}
        name={name}
        size={size}
      />
      <span className="min-w-0">{children ?? name}</span>
    </span>
  );
}

function Preview({
  productId,
  small,
  name,
}: {
  readonly productId: string;
  readonly small: string;
  readonly name: string;
}): React.JSX.Element {
  // The same query the product's photo tab makes, so each answers from the other.
  const photos = useQuery<readonly (string | null)[], ApiError>({
    queryKey: ['product-photos', productId],
    queryFn: () => getProductPhotos(productId),
    staleTime: 5 * 60_000,
    retry: false,
  });
  const main = photos.data?.find((photo): photo is string => Boolean(photo));

  return (
    <figure className="w-56 space-y-1.5">
      <img
        src={main ?? small}
        alt={name}
        className={clsx(
          'aspect-square w-full rounded-xl bg-white object-contain',
          !main && photos.isFetching && 'opacity-80',
        )}
      />
      <figcaption className="line-clamp-2 px-0.5 text-xs font-medium text-ink">
        {name}
      </figcaption>
    </figure>
  );
}
