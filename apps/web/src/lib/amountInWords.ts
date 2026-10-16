/**
 * Converts a numeric amount to English words for invoice grand total.
 */
export function amountInWords(amount: number): string {
  if (!Number.isFinite(amount) || amount <= 0) return 'Zero Only';
  const whole = Math.floor(amount);
  const cents = Math.round((amount - whole) * 100);

  const ones = [
    '',
    'One',
    'Two',
    'Three',
    'Four',
    'Five',
    'Six',
    'Seven',
    'Eight',
    'Nine',
    'Ten',
    'Eleven',
    'Twelve',
    'Thirteen',
    'Fourteen',
    'Fifteen',
    'Sixteen',
    'Seventeen',
    'Eighteen',
    'Nineteen',
  ];
  const tens = [
    '',
    '',
    'Twenty',
    'Thirty',
    'Forty',
    'Fifty',
    'Sixty',
    'Seventy',
    'Eighty',
    'Ninety',
  ];

  function convertGroup(n: number): string {
    let s = '';
    if (n >= 100) {
      s += ones[Math.floor(n / 100)] + ' Hundred ';
      n %= 100;
    }
    if (n >= 20) {
      s += tens[Math.floor(n / 10)] + ' ';
      n %= 10;
    }
    if (n > 0) {
      s += ones[n] + ' ';
    }
    return s.trim();
  }

  let words = '';
  const billions = Math.floor(whole / 1000000000);
  const millions = Math.floor((whole % 1000000000) / 1000000);
  const thousands = Math.floor((whole % 1000000) / 1000);
  const rem = whole % 1000;

  if (billions > 0) words += convertGroup(billions) + ' Billion ';
  if (millions > 0) words += convertGroup(millions) + ' Million ';
  if (thousands > 0) words += convertGroup(thousands) + ' Thousand ';
  if (rem > 0) words += convertGroup(rem) + ' ';

  words = words.trim();
  if (!words) words = 'Zero';

  if (cents > 0) {
    return `${words} and ${cents}/100 Only`;
  }
  return `${words} Only`;
}
