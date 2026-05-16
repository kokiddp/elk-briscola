import { FormControl } from '@angular/forms';
import { describe, expect, it } from 'vitest';
import {
  displayNameValidator,
  passwordValidator,
  usernameValidator,
} from './auth-validators';

describe('usernameValidator', () => {
  it.each([
    ['alice', null],
    ['a_b-c', null],
    ['abc123', null],
    ['ab', { username: true }],
    ['a'.repeat(33), { username: true }],
    ['has space', { username: true }],
    ['has!', { username: true }],
  ])('validates %s', (value, expected) => {
    expect(usernameValidator(new FormControl(value))).toEqual(expected);
  });

  it('passes on empty (use Validators.required separately)', () => {
    expect(usernameValidator(new FormControl(''))).toBeNull();
  });
});

describe('passwordValidator', () => {
  it('accepts a valid password', () => {
    expect(passwordValidator(new FormControl('hunter2hunter'))).toBeNull();
  });

  it('rejects short passwords', () => {
    const result = passwordValidator(new FormControl('short1'));
    expect(result).toHaveProperty('minlength');
  });

  it('rejects passwords without a letter', () => {
    expect(passwordValidator(new FormControl('12345678901'))).toEqual(
      expect.objectContaining({ letter: true }),
    );
  });

  it('rejects passwords without a digit', () => {
    expect(passwordValidator(new FormControl('abcdefghijk'))).toEqual(
      expect.objectContaining({ digit: true }),
    );
  });
});

describe('displayNameValidator', () => {
  it('accepts 1-32 chars', () => {
    expect(displayNameValidator(new FormControl('Alice'))).toBeNull();
    expect(displayNameValidator(new FormControl('a'.repeat(32)))).toBeNull();
  });

  it('rejects > 32 chars', () => {
    expect(displayNameValidator(new FormControl('a'.repeat(33)))).toEqual({
      displayName: true,
    });
  });

  it('treats empty as valid (optional field)', () => {
    expect(displayNameValidator(new FormControl(''))).toBeNull();
  });
});
