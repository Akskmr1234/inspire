import { describe, expect, it } from 'vitest';
import { amountInWords, amountInWordsArabic } from '@/lib/amountInWords';

describe('amount in words', () => {
  it('writes English', () => {
    expect(amountInWords(1250.5)).toBe('One Thousand Two Hundred Fifty and 50/100 Only');
  });

  it('writes Arabic, units before tens and the scale words in their counted forms', () => {
    expect(amountInWordsArabic(25)).toBe('خمسة وعشرون فقط');
    expect(amountInWordsArabic(100)).toBe('مائة فقط');
    expect(amountInWordsArabic(2000)).toBe('ألفان فقط');
    expect(amountInWordsArabic(3000)).toBe('ثلاثة آلاف فقط');
    expect(amountInWordsArabic(11000)).toBe('أحد عشر ألف فقط');
    expect(amountInWordsArabic(1250.5)).toBe('ألف ومائتان وخمسون و50/100 فقط');
    expect(amountInWordsArabic(1_000_000)).toBe('مليون فقط');
  });
});
