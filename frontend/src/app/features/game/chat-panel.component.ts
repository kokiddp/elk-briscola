import {
  AfterViewChecked,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { I18nPipe } from '../../shared/i18n.pipe';
import { GameChatMessage } from './game.models';

@Component({
  selector: 'bri-chat-panel',
  standalone: true,
  imports: [ReactiveFormsModule, I18nPipe],
  templateUrl: './chat-panel.component.html',
  styleUrl: './chat-panel.component.scss',
})
export class ChatPanelComponent implements AfterViewChecked {
  private readonly fb = inject(FormBuilder);

  readonly messages = input<readonly GameChatMessage[]>([]);
  readonly tacticalBanner = input(false);
  readonly canSend = input(true);

  readonly send = output<string>();

  readonly sending = signal(false);
  readonly hasMessages = computed(() => this.messages().length > 0);

  readonly form = this.fb.nonNullable.group({
    text: ['', [Validators.required, Validators.maxLength(500)]],
  });

  private readonly logEl = viewChild<ElementRef<HTMLElement>>('log');
  private lastSeenCount = 0;

  constructor() {
    // Reactive FormControl ignores plain `[disabled]` template binding;
    // mirror canSend() onto its enabled/disabled state explicitly.
    effect(() => {
      const enabled = this.canSend();
      const ctrl = this.form.controls.text;
      if (enabled && ctrl.disabled) {
        ctrl.enable({ emitEvent: false });
      } else if (!enabled && ctrl.enabled) {
        ctrl.disable({ emitEvent: false });
      }
    });
  }

  ngAfterViewChecked(): void {
    const el = this.logEl()?.nativeElement;
    if (!el) {
      return;
    }
    const count = this.messages().length;
    if (count !== this.lastSeenCount) {
      el.scrollTop = el.scrollHeight;
      this.lastSeenCount = count;
    }
  }

  async onSubmit(): Promise<void> {
    if (this.form.invalid || this.sending() || !this.canSend()) {
      this.form.markAllAsTouched();
      return;
    }
    const text = this.form.controls.text.value.trim();
    if (!text) {
      return;
    }
    this.sending.set(true);
    try {
      this.send.emit(text);
      this.form.reset({ text: '' });
    } finally {
      this.sending.set(false);
    }
  }

  formatTimestamp(iso: string): string {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) {
      return '';
    }
    return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  }
}
