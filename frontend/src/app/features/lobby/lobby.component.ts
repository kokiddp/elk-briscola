import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { ErrorToastService } from '../../core/error-toast.service';
import { I18nService } from '../../core/i18n.service';
import { I18nPipe } from '../../shared/i18n.pipe';
import { CreateGameDialogComponent } from './create-game-dialog.component';
import { CreateGameRequest, GameSummary } from './lobby.models';
import { LobbyService } from './lobby.service';

@Component({
  selector: 'bri-lobby',
  standalone: true,
  imports: [I18nPipe, CreateGameDialogComponent],
  templateUrl: './lobby.component.html',
  styleUrl: './lobby.component.scss',
})
export class LobbyComponent implements OnInit, OnDestroy {
  private readonly lobby = inject(LobbyService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ErrorToastService);
  private readonly i18n = inject(I18nService);

  readonly openGames = this.lobby.openGames;
  readonly runningGames = this.lobby.runningGames;
  readonly dialogOpen = signal(false);
  readonly dialogSubmitting = signal(false);
  readonly joiningId = signal<string | null>(null);

  readonly currentUserId = computed(() => this.auth.currentUser()?.id ?? null);
  readonly currentDisplayName = computed(
    () => this.auth.currentUser()?.displayName ?? this.auth.currentUser()?.username ?? '',
  );

  async ngOnInit(): Promise<void> {
    try {
      await this.lobby.connect();
    } catch {
      this.toast.error(this.i18n.t('lobby.errors.connectFailed'));
    }
  }

  async ngOnDestroy(): Promise<void> {
    await this.lobby.disconnect();
  }

  openCreateDialog(): void {
    this.dialogOpen.set(true);
  }

  closeCreateDialog(): void {
    this.dialogOpen.set(false);
    this.dialogSubmitting.set(false);
  }

  async onCreateSubmit(req: CreateGameRequest): Promise<void> {
    if (this.dialogSubmitting()) {
      return;
    }
    this.dialogSubmitting.set(true);
    try {
      const detail = await this.lobby.createGame(req);
      this.dialogOpen.set(false);
      await this.router.navigateByUrl(`/game/${detail.id}`);
    } catch {
      this.toast.error(this.i18n.t('lobby.errors.createFailed'));
    } finally {
      this.dialogSubmitting.set(false);
    }
  }

  async onJoin(game: GameSummary): Promise<void> {
    if (this.joiningId()) {
      return;
    }
    this.joiningId.set(game.id);
    try {
      let password: string | null = null;
      if (game.isPrivate) {
        const prompted =
          typeof window !== 'undefined'
            ? window.prompt(this.i18n.t('lobby.join.passwordPrompt'))
            : null;
        if (prompted === null) {
          return;
        }
        password = prompted;
      }
      await this.lobby.joinGame(game.id, password);
      await this.router.navigateByUrl(`/game/${game.id}`);
    } catch {
      this.toast.error(this.i18n.t('lobby.errors.joinFailed'));
    } finally {
      this.joiningId.set(null);
    }
  }

  trackById(_index: number, item: GameSummary): string {
    return item.id;
  }

  modeLabel(mode: GameSummary['mode']): string {
    return this.i18n.t(
      mode === 'TwoPlayer' ? 'lobby.mode.twoPlayer' : 'lobby.mode.fourPlayerTeams',
    );
  }
}
