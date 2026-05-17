import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { I18nPipe } from '../i18n.pipe';

@Component({
  selector: 'bri-top-nav',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, I18nPipe],
  template: `
    @if (visible()) {
      <header class="sticky top-0 z-30 border-b border-stone-200 bg-white/90 backdrop-blur">
        <div class="mx-auto flex h-14 max-w-6xl items-center gap-3 px-4">
          <a
            routerLink="/lobby"
            class="flex items-center gap-2 font-display text-lg font-semibold tracking-tight text-stone-900 hover:text-brand-700"
          >
            <span
              class="grid h-7 w-7 place-items-center rounded-md bg-table-800 text-warm-400"
              aria-hidden="true"
            >
              ♣
            </span>
            <span>elk-briscola</span>
          </a>

          <nav class="ml-2 flex items-center gap-1" aria-label="Primary">
            <a
              routerLink="/lobby"
              routerLinkActive="bg-brand-50 text-brand-700"
              class="rounded-md px-3 py-1.5 text-sm font-medium text-stone-600 hover:bg-stone-100 hover:text-stone-900"
              data-testid="nav-lobby"
            >
              {{ 'nav.lobby' | t }}
            </a>
            <a
              routerLink="/profile"
              routerLinkActive="bg-brand-50 text-brand-700"
              class="rounded-md px-3 py-1.5 text-sm font-medium text-stone-600 hover:bg-stone-100 hover:text-stone-900"
              data-testid="nav-profile"
            >
              {{ 'nav.profile' | t }}
            </a>
          </nav>

          <div class="ml-auto flex items-center gap-3">
            <span class="hidden text-sm text-stone-600 sm:inline" data-testid="nav-user">
              {{ displayName() }}
            </span>
            <button
              type="button"
              (click)="logout()"
              class="rounded-md border border-stone-300 bg-white px-3 py-1.5 text-sm font-medium text-stone-700 shadow-sm transition hover:bg-stone-100 active:bg-stone-200"
              data-testid="nav-logout"
            >
              {{ 'nav.logout' | t }}
            </button>
          </div>
        </div>
      </header>
    }
  `,
})
export class TopNavComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly visible = computed(() => this.auth.isAuthenticated());
  readonly displayName = computed(
    () => this.auth.currentUser()?.displayName ?? this.auth.currentUser()?.username ?? '',
  );

  async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
