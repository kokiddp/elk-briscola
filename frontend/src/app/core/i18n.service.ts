import { Injectable, signal } from '@angular/core';
import enMessages from '../../assets/i18n/en.json';
import itMessages from '../../assets/i18n/it.json';

export type Locale = 'en' | 'it';

type Catalog = Record<string, string>;

const CATALOGS: Record<Locale, Catalog> = {
  en: enMessages as Catalog,
  it: itMessages as Catalog,
};

const LOCALE_STORAGE_KEY = 'elk-briscola.locale';

@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly localeSig = signal<Locale>(this.detectInitialLocale());

  readonly current = this.localeSig.asReadonly();

  setLocale(locale: Locale): void {
    this.localeSig.set(locale);
    try {
      localStorage.setItem(LOCALE_STORAGE_KEY, locale);
    } catch {
      // ignore — non-persistent locale is acceptable.
    }
  }

  t(key: string, params?: Record<string, string | number>): string {
    const locale = this.localeSig();
    const value = CATALOGS[locale][key] ?? CATALOGS.en[key] ?? key;
    if (!params) {
      return value;
    }
    return Object.entries(params).reduce(
      (acc, [name, val]) => acc.replaceAll(`{${name}}`, String(val)),
      value,
    );
  }

  private detectInitialLocale(): Locale {
    try {
      const stored = localStorage.getItem(LOCALE_STORAGE_KEY);
      if (stored === 'en' || stored === 'it') {
        return stored;
      }
    } catch {
      // ignore
    }
    if (typeof navigator !== 'undefined' && navigator.language?.startsWith('it')) {
      return 'it';
    }
    return 'en';
  }
}
