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

const AR_ONES = [
  '',
  'واحد',
  'اثنان',
  'ثلاثة',
  'أربعة',
  'خمسة',
  'ستة',
  'سبعة',
  'ثمانية',
  'تسعة',
  'عشرة',
  'أحد عشر',
  'اثنا عشر',
  'ثلاثة عشر',
  'أربعة عشر',
  'خمسة عشر',
  'ستة عشر',
  'سبعة عشر',
  'ثمانية عشر',
  'تسعة عشر',
];
const AR_TENS = [
  '',
  '',
  'عشرون',
  'ثلاثون',
  'أربعون',
  'خمسون',
  'ستون',
  'سبعون',
  'ثمانون',
  'تسعون',
];
const AR_HUNDREDS = [
  '',
  'مائة',
  'مائتان',
  'ثلاثمائة',
  'أربعمائة',
  'خمسمائة',
  'ستمائة',
  'سبعمائة',
  'ثمانمائة',
  'تسعمائة',
];

/** Up to 999 in Arabic words, units before tens as the language reads them. */
function arabicGroup(n: number): string {
  const parts: string[] = [];
  const hundreds = Math.floor(n / 100);
  const rest = n % 100;

  if (hundreds > 0) parts.push(AR_HUNDREDS[hundreds]!);

  if (rest > 0 && rest < 20) {
    parts.push(AR_ONES[rest]!);
  } else if (rest >= 20) {
    const unit = rest % 10;
    parts.push(
      unit > 0
        ? `${AR_ONES[unit]!} و${AR_TENS[Math.floor(rest / 10)]!}`
        : AR_TENS[Math.floor(rest / 10)]!,
    );
  }

  return parts.join(' و');
}

/**
 * A scale word for a count of it: ألف, ألفان, three to ten آلاف, eleven and up ألف -
 * the forms an Arabic invoice prints, counting in the masculine.
 */
function arabicScale(
  count: number,
  one: string,
  two: string,
  few: string,
  many: string,
): string {
  if (count === 1) return one;
  if (count === 2) return two;

  const words = arabicGroup(count);
  const lastTwo = count % 100;

  return `${words} ${lastTwo >= 3 && lastTwo <= 10 ? few : many}`;
}

/**
 * The amount in Arabic words, for a document printed in Arabic and English.
 *
 * Whole units in words and the fraction as hundredths, followed by فقط ("only") as Gulf
 * invoices write it. The currency is printed beside it by the caller, as in English.
 */
export function amountInWordsArabic(amount: number): string {
  if (!Number.isFinite(amount) || amount <= 0) return 'صفر فقط';

  const whole = Math.floor(amount);
  const cents = Math.round((amount - whole) * 100);

  const billions = Math.floor(whole / 1_000_000_000);
  const millions = Math.floor((whole % 1_000_000_000) / 1_000_000);
  const thousands = Math.floor((whole % 1_000_000) / 1000);
  const rest = whole % 1000;

  const parts: string[] = [];

  if (billions > 0) {
    parts.push(arabicScale(billions, 'مليار', 'ملياران', 'مليارات', 'مليار'));
  }
  if (millions > 0) {
    parts.push(arabicScale(millions, 'مليون', 'مليونان', 'ملايين', 'مليون'));
  }
  if (thousands > 0) {
    parts.push(arabicScale(thousands, 'ألف', 'ألفان', 'آلاف', 'ألف'));
  }
  if (rest > 0) {
    parts.push(arabicGroup(rest));
  }

  const words = parts.length > 0 ? parts.join(' و') : 'صفر';

  return cents > 0 ? `${words} و${String(cents)}/100 فقط` : `${words} فقط`;
}
