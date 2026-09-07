import { describe, expect, it } from 'vitest';
import { collect, maxLength, nextCode, numeric, required } from '@/lib/validation';

/*
  The rules the forms share.

  `nextCode` gets most of the attention, because it is the one that produces a value
  rather than a message: a customer master where the suggested code collides with an
  existing one is a form that is refused by the server every time somebody accepts
  the default, which is worse than not suggesting anything.
*/

describe('required', () => {
  it('rejects whitespace as firmly as an empty string', () => {
    expect(required('   ', 'Needed')).toBe('Needed');
    expect(required('', 'Needed')).toBe('Needed');
    expect(required(' a ', 'Needed')).toBeNull();
  });
});

describe('numeric', () => {
  it('passes an empty value, so optional and numeric are separate questions', () => {
    expect(numeric('', 'Not a number')).toBeNull();
  });

  it('rejects what is not a number, and what is out of bounds', () => {
    expect(numeric('abc', 'Bad')).toBe('Bad');
    expect(numeric('-1', 'Bad', { min: 0 })).toBe('Bad');
    expect(numeric('101', 'Bad', { max: 100 })).toBe('Bad');
    expect(numeric('1.5', 'Bad', { integer: true })).toBe('Bad');
    expect(numeric('18', 'Bad', { min: 0, max: 100 })).toBeNull();
  });
});

describe('maxLength', () => {
  it('measures the trimmed value, because that is what gets sent', () => {
    expect(maxLength('abcd  ', 4, 'Too long')).toBeNull();
    expect(maxLength('abcde', 4, 'Too long')).toBe('Too long');
  });
});

describe('collect', () => {
  it('keeps only the fields that have something wrong with them', () => {
    expect(collect({ a: null, b: 'Bad', c: undefined, d: '' })).toEqual({ b: 'Bad' });
  });
});

describe('nextCode', () => {
  it('continues the firm’s own scheme rather than imposing one', () => {
    expect(nextCode(['CUST-001', 'CUST-002', 'CUST-007'])).toBe('CUST-008');
  });

  it('keeps the width, so codes go on sorting as strings', () => {
    expect(nextCode(['C0009'])).toBe('C0010');
    expect(nextCode(['C0099'])).toBe('C0100');
  });

  it('falls back to the prefix it was given when nothing is numbered', () => {
    expect(nextCode([], 'C', 4)).toBe('C0001');
    expect(nextCode(['CASH', 'PETTY'], 'C', 4)).toBe('C0001');
  });

  it('ignores codes that carry no number at all', () => {
    expect(nextCode(['OPENING', 'C-004', 'MISC'], 'C', 3)).toBe('C-005');
  });

  it('follows the scheme that has got furthest when a master holds two', () => {
    // A master midway through a rename holds both. Continuing the one with the
    // highest number is a guess, but it is the guess that keeps the sequence
    // somebody is actually using unbroken.
    expect(nextCode(['OLD-050', 'NEW-004'])).toBe('OLD-051');
  });

  it('never suggests a code already in use', () => {
    const existing = ['A-001', 'A-002', 'A-003'];

    expect(existing).not.toContain(nextCode(existing));
  });
});
