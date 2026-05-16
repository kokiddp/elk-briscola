import { beforeEach, describe, expect, it } from 'vitest';
import { I18nService } from './i18n.service';

describe('I18nService', () => {
  let service: I18nService;

  beforeEach(() => {
    localStorage.clear();
    service = new I18nService();
  });

  it('defaults to English when no locale stored', () => {
    expect(['en', 'it']).toContain(service.current());
  });

  it('switches locale via setLocale', () => {
    service.setLocale('it');
    expect(service.current()).toBe('it');
    expect(service.t('auth.login.title')).toBe('Accedi');
    service.setLocale('en');
    expect(service.t('auth.login.title')).toBe('Sign in');
  });

  it('interpolates {name} parameters', () => {
    service.setLocale('en');
    expect(service.t('home.welcome', { name: 'Alice' })).toBe('Welcome, Alice');
  });

  it('falls back to the key itself for unknown ids', () => {
    expect(service.t('does.not.exist')).toBe('does.not.exist');
  });

  it('falls back to English when the active locale lacks a key', () => {
    service.setLocale('it');
    expect(service.t('lobby.title')).toBe('Lobby');
  });
});
