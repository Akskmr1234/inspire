import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import '@/i18n';
import i18n from 'i18next';
import { ProductDetailCells } from '@/components/DocumentLines';
import type { ProductSummary } from '@/lib/products';

/*
  The product's own figures beside a document line: code, unit, retail and MRP.

  What is worth pinning is that each one follows its own toggle, and that a figure
  the master does not hold reads as a dash rather than as 0.00 — an MRP of nothing
  printed as zero is a price somebody will quote.
*/

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
};

function row(shown: readonly string[]): void {
  render(
    <table>
      <tbody>
        <tr>
          <ProductDetailCells
            product={product}
            shows={(key) => shown.includes(key)}
            labels={(key) => i18n.t(key)}
          />
        </tr>
      </tbody>
    </table>,
  );
}

describe('the product detail cells', () => {
  it('draws only the columns that are turned on', () => {
    row(['productCode', 'productRetail']);

    expect(screen.getByText('P0001')).toBeTruthy();
    expect(screen.getByText('15.00')).toBeTruthy();
    expect(screen.queryByText('BOX')).toBeNull();
    expect(screen.getAllByRole('cell')).toHaveLength(2);
  });

  it('shows a dash for a price the master does not hold, not a zero', () => {
    row(['productMrp']);

    expect(screen.getByRole('cell').textContent).toBe('—');
  });

  it('captions each cell for the stacked phone layout', () => {
    row(['productCode', 'productUnit', 'productRetail', 'productMrp']);

    expect(
      screen.getAllByRole('cell').map((cell) => cell.getAttribute('data-label')),
    ).toEqual(['Code', 'Unit', 'Retail', 'MRP']);
  });
});
