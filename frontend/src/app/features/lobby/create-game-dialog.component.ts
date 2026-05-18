import { Component, inject, input, output } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { I18nPipe } from '../../shared/i18n.pipe';
import { CreateGameRequest, GameMode } from './lobby.models';

@Component({
  selector: 'bri-create-game-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, I18nPipe],
  templateUrl: './create-game-dialog.component.html',
  styleUrl: './create-game-dialog.component.scss',
})
export class CreateGameDialogComponent {
  private readonly fb = inject(FormBuilder);

  readonly submitting = input(false);
  readonly submitted = output<CreateGameRequest>();
  readonly cancelled = output();

  readonly form = this.fb.nonNullable.group({
    mode: ['TwoPlayer' as GameMode, [Validators.required]],
    isPrivate: [false],
    password: [''],
  });

  constructor() {
    this.form.controls.isPrivate.valueChanges.pipe(takeUntilDestroyed()).subscribe((isPrivate) => {
      const pwd = this.form.controls.password;
      if (isPrivate) {
        pwd.addValidators([Validators.required, Validators.minLength(4), Validators.maxLength(64)]);
      } else {
        pwd.clearValidators();
        pwd.setValue('');
      }
      pwd.updateValueAndValidity();
    });
  }

  onSubmit(): void {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    const { mode, isPrivate, password } = this.form.getRawValue();
    this.submitted.emit({
      mode,
      isPrivate,
      password: isPrivate ? password : null,
    });
  }

  onCancel(): void {
    if (this.submitting()) {
      return;
    }
    this.cancelled.emit();
  }
}
