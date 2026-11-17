import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import '@/i18n';
import { TouchProductGrid } from './TouchProductGrid';
import type { ProductSummary } from '@/lib/products';

const sampleProducts: readonly ProductSummary[] = [
  {
    id: 'prod-1',
    code: 'BEV-001',
    description: 'Fresh Orange Juice',
    descriptionArabic: 'عصير برتقال طازج',
    itemType: 1,
    categoryId: 'cat-bev',
    categoryName: 'Beverages',
    brandName: null,
    stockUnitCode: 'BTL',
    currency: 'SAR',
    cost: 5,
    retailRate: 10,
    reorderLevel: 5,
    tracksBatches: false,
    tracksSerialNumbers: false,
    isDiscontinued: false,
    isActive: true,
    barcodeCount: 1,
    wholesaleRate: 8,
    maximumRetailPrice: 12,
    taxPercentage: 15,
    stockQuantity: 45,
  },
  {
    id: 'prod-2',
    code: 'BEV-002',
    description: 'Cold Brew Coffee',
    descriptionArabic: 'قهوة باردة',
    itemType: 1,
    categoryId: 'cat-bev',
    categoryName: 'Beverages',
    brandName: null,
    stockUnitCode: 'CUP',
    currency: 'SAR',
    cost: 7,
    retailRate: 15,
    reorderLevel: 5,
    tracksBatches: false,
    tracksSerialNumbers: false,
    isDiscontinued: false,
    isActive: true,
    barcodeCount: 1,
    wholesaleRate: 12,
    maximumRetailPrice: 18,
    taxPercentage: 15,
    stockQuantity: 20,
  },
  {
    id: 'prod-3',
    code: 'SNK-001',
    description: 'Chocolate Croissant',
    descriptionArabic: 'كرواسون شوكولاتة',
    itemType: 1,
    categoryId: 'cat-snk',
    categoryName: 'Bakery',
    brandName: null,
    stockUnitCode: 'PC',
    currency: 'SAR',
    cost: 4,
    retailRate: 8,
    reorderLevel: 2,
    tracksBatches: false,
    tracksSerialNumbers: false,
    isDiscontinued: false,
    isActive: true,
    barcodeCount: 1,
    wholesaleRate: 6,
    maximumRetailPrice: 10,
    taxPercentage: 15,
    stockQuantity: 0,
  },
];

describe('TouchProductGrid', () => {
  it('renders all products and category pills', () => {
    const onSelect = vi.fn();
    render(<TouchProductGrid products={sampleProducts} onSelectProduct={onSelect} />);

    expect(screen.getByText('Fresh Orange Juice')).toBeTruthy();
    expect(screen.getByText('Cold Brew Coffee')).toBeTruthy();
    expect(screen.getByText('Chocolate Croissant')).toBeTruthy();

    expect(screen.getByRole('tab', { name: /All/i })).toBeTruthy();
    expect(screen.getByRole('tab', { name: /Beverages/i })).toBeTruthy();
    expect(screen.getByRole('tab', { name: /Bakery/i })).toBeTruthy();
  });

  it('filters products when a category tab is clicked', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<TouchProductGrid products={sampleProducts} onSelectProduct={onSelect} />);

    const bakeryTab = screen.getByRole('tab', { name: /Bakery/i });
    await user.click(bakeryTab);

    expect(screen.getByText('Chocolate Croissant')).toBeTruthy();
    expect(screen.queryByText('Fresh Orange Juice')).toBeNull();
    expect(screen.queryByText('Cold Brew Coffee')).toBeNull();
  });

  it('filters products by search input', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<TouchProductGrid products={sampleProducts} onSelectProduct={onSelect} />);

    const searchInput = screen.getByRole('searchbox');
    await user.type(searchInput, 'Coffee');

    expect(screen.getByText('Cold Brew Coffee')).toBeTruthy();
    expect(screen.queryByText('Fresh Orange Juice')).toBeNull();
    expect(screen.queryByText('Chocolate Croissant')).toBeNull();
  });

  it('calls onSelectProduct when a product card is clicked', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<TouchProductGrid products={sampleProducts} onSelectProduct={onSelect} />);

    const card = screen.getByText('Fresh Orange Juice').closest('button');
    expect(card).not.toBeNull();
    if (card) {
      await user.click(card);
    }

    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect).toHaveBeenCalledWith(sampleProducts[0]);
  });

  it('shows badge when item is already in cart', () => {
    const onSelect = vi.fn();
    const cart = new Map([
      ['prod-1', 3],
      ['prod-2', 1],
    ]);

    render(
      <TouchProductGrid
        products={sampleProducts}
        onSelectProduct={onSelect}
        quantitiesInCart={cart}
      />,
    );

    expect(screen.getByText('×3')).toBeTruthy();
    expect(screen.getByText('×1')).toBeTruthy();
  });

  it('displays out of stock status appropriately', () => {
    const onSelect = vi.fn();
    render(<TouchProductGrid products={sampleProducts} onSelectProduct={onSelect} />);

    expect(screen.getByText('Out of stock')).toBeTruthy();
    expect(screen.getByText(/45 in stock/i)).toBeTruthy();
  });
});
