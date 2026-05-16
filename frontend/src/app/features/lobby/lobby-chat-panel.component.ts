import {
  AfterViewChecked,
  Component,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { I18nPipe } from '../../shared/i18n.pipe';
import { LobbyService } from './lobby.service';

@Component({
  selector: 'bri-lobby-chat-panel',
  standalone: true,
  imports: [ReactiveFormsModule, I18nPipe],
  templateUrl: './lobby-chat-panel.component.html',
  styleUrl: './lobby-chat-panel.component.scss',
})
export class LobbyChatPanelComponent implements AfterViewChecked {
  private readonly lobby = inject(LobbyService);
  private readonly fb = inject(FormBuilder);

  readonly messages = this.lobby.chatLog;
  readonly sending = signal(false);
  readonly hasMessages = computed(() => this.messages().length > 0);

  readonly form = this.fb.nonNullable.group({
    text: ['', [Validators.required, Validators.maxLength(500)]],
  });

  private readonly logEl = viewChild<ElementRef<HTMLElement>>('log');
  private lastSeenCount = 0;

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

  async send(): Promise<void> {
    if (this.form.invalid || this.sending()) {
      this.form.markAllAsTouched();
      return;
    }
    const text = this.form.controls.text.value.trim();
    if (!text) {
      return;
    }
    this.sending.set(true);
    try {
      await this.lobby.sendChat(text);
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
