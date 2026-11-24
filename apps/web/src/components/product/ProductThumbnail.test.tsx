import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import '@/i18n';
import { productOption } from '@/components/DocumentLines';
import { ProductPictureLabel } from '@/components/product/ProductThumbnail';
import { SearchSelect } from '@/components/SearchSelect';
import type { ProductSummary } from '@/lib/products';

/*
  Product pictures wherever a product is named: the item search on every entry screen,
  the product list and the stock reports.

  What is worth pinning is that a firm keeping no pictures sees nothing new - no empty
  frames - while one that does sees a picture or an empty frame on every row, the
  chosen product's picture in the box, and the full photograph when it asks for it.
*/

vi.mock('@/lib/photos', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/photos')>()),
  getProductPhotos: vi.fn(async () => [null, 'https://files.test/full.jpg', null]),
}));

const product: ProductSummary = {
  id: 'p1',
  code: 'P0001',
  description: 'A4 copier paper',
  descriptionArabic: null,
  itemType: 1,
  categoryId: 'c1',
  categoryName: 'Stationery',
  brandName: null,
  stockUnitCode: 'BOX',
  currency: 'AED',
  cost: 10,
  retailRate: 15,
  reorderLevel: 0,
  tracksBatches: false,
  tracksSerialNumbers: false,
  isDiscontinued: false,
  isActive: true,
  barcodeCount: 1,
  wholesaleRate: 13,
  maximumRetailPrice: 0,
  taxPercentage: null,
  stockQuantity: 0,
};

const other: ProductSummary = {
  ...product,
  id: 'p2',
  code: 'P0002',
  description: 'Stapler',
};

const thumbnails = { p1: 'https://files.test/p1-thumb.jpg' };

function wrapped(ui: React.ReactNode): React.JSX.Element {
  return (
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      {ui}
    </QueryClientProvider>
  );
}

describe('a product in the item search', () => {
  it('carries no picture for a firm that keeps none', () => {
    const option = productOption(product, 3, {});

    expect(option).not.toHaveProperty('image');
    expect(option).not.toHaveProperty('adornment');
  });

  it('carries its picture, or an empty frame, for a firm that keeps them', () => {
    expect(productOption(product, 3, thumbnails).image).toBe(thumbnails.p1);
    expect(productOption(product, 3, thumbnails).adornment).toBeDefined();
    expect(productOption(other, 3, thumbnails).image).toBeNull();
    expect(productOption(other, 3, thumbnails)).not.toHaveProperty('adornment');
  });

  it('shows the pictures in the list and the chosen one in the box', async () => {
    const user = userEvent.setup();
    const { container } = render(
      wrapped(
        <SearchSelect
          value="p1"
          onChange={() => undefined}
          label="Product"
          options={[
            productOption(product, 3, thumbnails),
            productOption(other, 1, thumbnails),
          ]}
        />,
      ),
    );

    // Closed, the chosen product's picture sits in the box.
    expect(
      screen.getByRole('button', { name: 'Picture of A4 copier paper' }),
    ).toBeTruthy();

    await user.click(screen.getByRole('combobox', { name: 'Product' }));

    const rows = screen.getAllByRole('option');
    expect(rows[0]?.querySelector('img')?.getAttribute('src')).toBe(thumbnails.p1);
    // The stapler has no picture: an empty frame keeps its label in line.
    expect(rows[1]?.querySelector('img')).toBeNull();
    expect(rows[1]?.querySelector('span[aria-hidden="true"]')).not.toBeNull();

    // Open, the box holds the search, so the chosen picture steps aside.
    expect(
      container.querySelector('button[aria-label="Picture of A4 copier paper"]'),
    ).toBeNull();
  });
});

describe('a product named in a table', () => {
  it('is just its name for a firm that keeps no pictures', () => {
    render(
      wrapped(
        <ProductPictureLabel productId="p1" name="A4 copier paper" thumbnails={{}} />,
      ),
    );

    expect(screen.getByText('A4 copier paper')).toBeTruthy();
    expect(screen.queryByRole('button')).toBeNull();
    expect(document.querySelector('img')).toBeNull();
  });

  it('has an empty frame where the product has no picture', () => {
    render(
      wrapped(
        <ProductPictureLabel productId="p2" name="Stapler" thumbnails={thumbnails} />,
      ),
    );

    expect(screen.getByText('Stapler')).toBeTruthy();
    expect(screen.queryByRole('button')).toBeNull();
    expect(document.querySelector('span[aria-hidden="true"]')).not.toBeNull();
  });

  it('opens the full photograph from its picture', async () => {
    const user = userEvent.setup();
    render(
      wrapped(
        <ProductPictureLabel
          productId="p1"
          name="A4 copier paper"
          thumbnails={thumbnails}
        />,
      ),
    );

    const picture = screen.getByRole('button', { name: 'Picture of A4 copier paper' });
    expect(picture.querySelector('img')?.getAttribute('src')).toBe(thumbnails.p1);

    await user.click(picture);

    const large = await screen.findByRole('img', { name: 'A4 copier paper' });
    await vi.waitFor(() =>
      expect(large.getAttribute('src')).toBe('https://files.test/full.jpg'),
    );
  });
});
