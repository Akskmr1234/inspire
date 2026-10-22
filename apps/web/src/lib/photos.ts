import { request } from '@/lib/api';

/** How many photographs a product or a person may have. */
export const PHOTO_SLOTS = 3;

/**
 * Scales a picture down to a JPEG small enough to keep - at most 800 pixels on its
 * longer side, plenty for a product card or a staff record.
 */
export function shrinkPicture(file: File, longest = 800): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new Error('unreadable'));
    reader.onload = () => {
      const image = new Image();
      image.onerror = () => reject(new Error('not a picture'));
      image.onload = () => {
        const scale = Math.min(1, longest / Math.max(image.width, image.height));
        const canvas = document.createElement('canvas');
        canvas.width = Math.round(image.width * scale);
        canvas.height = Math.round(image.height * scale);
        canvas.getContext('2d')?.drawImage(image, 0, 0, canvas.width, canvas.height);
        resolve(canvas.toDataURL('image/jpeg', 0.8));
      };
      image.src = String(reader.result);
    };
    reader.readAsDataURL(file);
  });
}

interface PhotoSet {
  readonly photos: readonly (string | null)[];
}

/** A product's photographs, one entry a slot; null where a slot is empty. */
export async function getProductPhotos(
  productId: string,
): Promise<readonly (string | null)[]> {
  return (await request<PhotoSet>(`/inventory/products/${productId}/photos`)).photos;
}

/** Sets one of a product's photographs; null removes it. */
export function setProductPhoto(
  productId: string,
  slot: number,
  content: string | null,
): Promise<void> {
  return request<void>(`/inventory/products/${productId}/photos/${slot}`, {
    method: 'PUT',
    body: { content },
  });
}

/** An employee's, customer's or supplier's photographs, one entry a slot. */
export async function getLedgerPhotos(
  ledgerId: string,
): Promise<readonly (string | null)[]> {
  return (await request<PhotoSet>(`/accounting/ledgers/${ledgerId}/photos`)).photos;
}

/** Sets one of an employee's, customer's or supplier's photographs; null removes it. */
export function setLedgerPhoto(
  ledgerId: string,
  slot: number,
  content: string | null,
): Promise<void> {
  return request<void>(`/accounting/ledgers/${ledgerId}/photos/${slot}`, {
    method: 'PUT',
    body: { content },
  });
}
