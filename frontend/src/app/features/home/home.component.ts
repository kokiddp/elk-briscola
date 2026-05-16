import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n.service';

@Component({
  selector: 'bri-home',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
})
export class HomeComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

  readonly displayName = computed(() => this.auth.currentUser()?.displayName ?? 'player');
  readonly welcomeMessage = computed(() =>
    this.i18n.t('home.welcome', { name: this.displayName() }),
  );
  readonly placeholder = computed(() => this.i18n.t('home.placeholder'));
  readonly logoutLabel = computed(() => this.i18n.t('home.logout'));
  readonly lobbyLinkLabel = computed(() => this.i18n.t('home.goToLobby'));

  async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
