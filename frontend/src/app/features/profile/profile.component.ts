import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CardSetService } from '../../card-sets/card-set.service';
import { AuthService } from '../../core/auth.service';
import { ErrorToastService } from '../../core/error-toast.service';
import { I18nService } from '../../core/i18n.service';
import { I18nPipe } from '../../shared/i18n.pipe';

@Component({
  selector: 'bri-profile',
  standalone: true,
  imports: [RouterLink, I18nPipe],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss',
})
export class ProfileComponent {
  private readonly auth = inject(AuthService);
  private readonly cardSets = inject(CardSetService);
  private readonly toast = inject(ErrorToastService);
  private readonly i18n = inject(I18nService);

  readonly user = this.auth.currentUser;
  readonly manifests = this.cardSets.manifests;
  readonly activeSetId = this.cardSets.activeSetId;

  readonly displayName = computed(() => this.user()?.displayName ?? this.user()?.username ?? '');
  readonly pendingId = signal<string | null>(null);

  trackById(_index: number, manifest: { id: string }): string {
    return manifest.id;
  }

  isActive(id: string): boolean {
    return this.activeSetId() === id;
  }

  isPending(id: string): boolean {
    return this.pendingId() === id;
  }

  previewUrl(manifest: { id: string; preview: string }): string {
    return `/card-sets/${manifest.id}/${manifest.preview}`;
  }

  async onSelect(id: string): Promise<void> {
    if (this.pendingId() || this.isActive(id)) {
      return;
    }
    this.pendingId.set(id);
    try {
      await this.cardSets.setActiveSet(id);
    } catch {
      this.toast.error(this.i18n.t('profile.cardSet.error'));
    } finally {
      this.pendingId.set(null);
    }
  }
}
