import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

const USERNAME_PATTERN = /^[a-zA-Z0-9_-]{3,32}$/;

export const usernameValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  const value = control.value as string | null | undefined;
  if (value == null || value === '') {
    return null;
  }
  return USERNAME_PATTERN.test(value) ? null : { username: true };
};

export const passwordValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  const value = control.value as string | null | undefined;
  if (value == null || value === '') {
    return null;
  }
  const errors: ValidationErrors = {};
  if (value.length < 10) errors['minlength'] = { requiredLength: 10, actualLength: value.length };
  if (!/[A-Za-z]/.test(value)) errors['letter'] = true;
  if (!/[0-9]/.test(value)) errors['digit'] = true;
  return Object.keys(errors).length === 0 ? null : errors;
};

export const displayNameValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  const value = control.value as string | null | undefined;
  if (value == null || value === '') {
    return null;
  }
  return value.length >= 1 && value.length <= 32 ? null : { displayName: true };
};
