import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '@/i18n';
import { renderPage } from '@/test/renderPage';
import { ApiError } from '@/lib/api';
import { ProductEditor } from '@/pages/ProductEditor';
import type { ProductDetail } from '@/lib/products';

/*
  The editor replaces the list rather than opening beside it, so what it does when a
  product will not load is not a detail: whatever it draws is the whole screen, and
  if that is an alert on its own the user has been shown a dead end on the one screen
  where they were holding a list of every product.
*/

const getProduct = vi.fn();

vi.mock('@/lib/products', async () => {
  const actual = await vi.importActual<typeof import('@/lib/products')>('@/lib/products');
  return {
    ...actual,
    getProduct: (id: string) => getProduct(id) as Promise<ProductDetail>,
  };
});

vi.mock('@/lib/inventory', async () => {
  const actual =
    await vi.importActual<typeof import('@/lib/inventory')>('@/lib/inventory');
  return { ...actual, listMaster: vi.fn(async () => []) };
});

const product = {
  id: 'p1',
  code: 'P-0001',
  description: 'A4 copier paper',
  descriptionArabic: null,
  shortDescription: null,
  itemName: null,
  manufacturer: null,
  label: null,
  size: null,
  origin: null,
  itemType: 1,
  categoryId: 'c1',
  brandId: null,
  stockUnitId: 'u1',
  purchaseUnitId: 'u1',
  salesUnitId: 'u1',
  currency: 'AED',
  costingMethod: 2,
  cost: 9.5,
  profitPercentage: 20,
  corPercentage: 0,
  retailRate: 12,
  wholesaleRate: 11,
  otherRate: 0,
  maximumRetailPrice: 14,
  minimumLevel: 0,
  reorderLevel: 5,
  maximumLevel: 0,
  movement: 1,
  device: null,
  colour: null,
  battery: null,
  ram: null,
  storage: null,
  rack: null,
  bin: null,
  tracksBatches: false,
  tracksSerialNumbers: false,
  shelfLifeDays: null,
  isPacking: false,
  isDiscontinued: false,
  isActive: true,
  barcodes: [],
} satisfies ProductDetail;

describe('when the product loads', () => {
  it('names the record and offers its tabs', async () => {
    getProduct.mockResolvedValueOnce(product);
    renderPage(<ProductEditor productId="p1" onClose={() => undefined} />);

    await waitFor(() => expect(screen.getByText(/A4 copier paper/)).toBeTruthy());
    expect(screen.getByRole('button', { name: /barcodes/i })).toBeTruthy();
  });
});

describe('when it will not load', () => {
  it('says why and still offers the way back to the list', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();

    getProduct.mockRejectedValue(
      new ApiError(404, 'Product.NotFound', 'That product no longer exists.'),
    );

    renderPage(<ProductEditor productId="gone" onClose={onClose} />);

    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy());
    expect(screen.getByText('That product no longer exists.')).toBeTruthy();

    // The alert on its own is a dead end: the list it replaced is gone, and the
    // only exits left would be the browser's back button and the menu.
    await user.click(screen.getByRole('button', { name: /back to the list/i }));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('offers a retry, since the usual cause is the network rather than the record', async () => {
    const user = userEvent.setup();

    getProduct.mockRejectedValue(new ApiError(503, 'Http.503', 'Service unavailable.'));
    renderPage(<ProductEditor productId="p1" onClose={() => undefined} />);

    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy());

    const calls = getProduct.mock.calls.length;
    await user.click(screen.getByRole('button', { name: /try again/i }));

    await waitFor(() => expect(getProduct.mock.calls.length).toBeGreaterThan(calls));
  });
});
