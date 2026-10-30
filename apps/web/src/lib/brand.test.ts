import { describe, expect, it } from 'vitest';
import { brandFor, brandKeyFromHost } from '@/lib/brand';

describe('the brand a host name stands for', () => {
  it('is the first label of a sub-domain', () => {
    expect(brandKeyFromHost('aider.wishkraft.app')).toBe('aider');
    expect(brandKeyFromHost('inspire.wishkraft.app')).toBe('inspire');
    expect(brandKeyFromHost('INSPIRE.wishkraft.app.')).toBe('inspire');
  });

  it('skips the labels that name the service, not the brand', () => {
    expect(brandKeyFromHost('www.inspire.example.com')).toBe('inspire');
    expect(brandKeyFromHost('app.aider.example.com')).toBe('aider');
  });

  it('is nothing for a machine, localhost or an address', () => {
    expect(brandKeyFromHost('localhost')).toBeNull();
    expect(brandKeyFromHost('devbox')).toBeNull();
    expect(brandKeyFromHost('127.0.0.1')).toBeNull();
    expect(brandKeyFromHost('::1')).toBeNull();
    expect(brandKeyFromHost('')).toBeNull();
  });
});

describe('the brand', () => {
  it('names the application after the domain', () => {
    expect(brandFor('inspire.wishkraft.app').appName).toBe('Inspire ERP');
    expect(brandFor('aider.wishkraft.app').appName).toBe('Aider ERP');
    expect(brandFor('inspire.wishkraft.app').initials).toBe('IE');
  });

  it('spells the known brands in Arabic', () => {
    expect(brandFor('aider.wishkraft.app').appNameArabic).toBe('أيدر إي آر بي');
    expect(brandFor('inspire.wishkraft.app').appNameArabic).toBe('إنسباير إي آر بي');
  });

  it('reads a hyphenated label as words, and keeps it in Arabic as written', () => {
    const shop = brandFor('my-shop.wishkraft.app');

    expect(shop.appName).toBe('My Shop ERP');
    expect(shop.appNameArabic).toBe('My Shop إي آر بي');
  });

  it("falls back to the build's brand, then to Aider, where the host says nothing", () => {
    expect(brandFor('localhost', 'inspire').appName).toBe('Inspire ERP');
    expect(brandFor('localhost').appName).toBe('Aider ERP');
    expect(brandFor('192.168.1.20', '  ').appName).toBe('Aider ERP');
  });
});
