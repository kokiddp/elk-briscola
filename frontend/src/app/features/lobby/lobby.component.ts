import { Component, OnDestroy, OnInit, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ErrorToastService } from '../../core/error-toast.service';
import { I18nService } from '../../core/i18n.service';
import { I18nPipe } from '../../shared/i18n.pipe';
import { CreateGameDialogComponent } from './create-game-dialog.component';
import { LobbyChatPanelComponent } from './lobby-chat-panel.component';
import { CreateGameRequest, GameSummary } from './lobby.models';
import { LobbyService } from './lobby.service';

@Component({
  selector: 'bri-lobby',
  standalone: true,
  imports: [I18nPipe, CreateGameDialogComponent, LobbyChatPanelComponent],
  templateUrl: './lobby.component.html',
  styleUrl: './lobby.component.scss',
})
export class LobbyComponent implements OnInit, OnDestroy {
  private readonly lobby = inject(LobbyService);
  private readonly router = inject(Router);
  private readonly toast = inject(ErrorToastService);
  private readonly i18n = inject(I18nService);

  readonly openGames = this.lobby.openGames;
  readonly runningGames = this.lobby.runningGames;
  readonly dialogOpen = signal(false);
  readonly dialogSubmitting = signal(false);
  readonly joiningId = signal<string | null>(null);
  /**
   * Game we're currently waiting in (created or joined into an Open seat).
   * GameHub's JoinGame requires the game to be Running, so we hold the
   * route here until the lobby pushes gameStarted/gameUpdated(Running).
   */
  readonly pendingGameId = signal<string | null>(null);
  readonly pendingGame = computed<GameSummary | null>(() => {
    const id = this.pendingGameId();
    if (!id) return null;
    return this.openGames().find((g) => g.id === id) ?? null;
  });
  /** Whether the user is currently sitting in any open game (their own
   *  creation or one they joined). Drives the create + join UX gates. */
  readonly hasPendingGame = computed(() => this.pendingGameId() !== null);

  constructor() {
    // Auto-route once our pending game transitions to Running.
    effect(() => {
      const started = this.lobby.lastStartedGameId();
      const pending = this.pendingGameId();
      if (started && started === pending) {
        this.pendingGameId.set(null);
        this.lobby.clearLastStartedGameId();
        void this.router.navigateByUrl(`/game/${started}`);
      }
    });
  }

  async ngOnInit(): Promise<void> {
    try {
      await this.lobby.connect();
    } catch {
      this.toast.error(this.i18n.t('lobby.errors.connectFailed'));
    }
  }

  ngOnDestroy(): void {
    void this.lobby.disconnect();
  }

  openCreateDialog(): void {
    this.dialogOpen.set(true);
  }

  closeCreateDialog(): void {
    if (this.dialogSubmitting()) {
      return;
    }
    this.dialogOpen.set(false);
  }

  async onCreateSubmit(req: CreateGameRequest): Promise<void> {
    if (this.dialogSubmitting()) {
      return;
    }
    this.dialogSubmitting.set(true);
    try {
      const detail = await this.lobby.createGame(req);
      this.dialogOpen.set(false);
      if (detail.status === 'Running') {
        await this.router.navigateByUrl(`/game/${detail.id}`);
      } else {
        // Stay on the lobby; the effect above will route us in once another
        // player fills the last seat and the lobby pushes gameStarted.
        this.pendingGameId.set(detail.id);
      }
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
      const detail = await this.lobby.joinGame(game.id, password);
      if (detail.status === 'Running') {
        await this.router.navigateByUrl(`/game/${game.id}`);
      } else {
        // Joined a 4p game still waiting on more seats.
        this.pendingGameId.set(game.id);
      }
    } catch {
      this.toast.error(this.i18n.t('lobby.errors.joinFailed'));
    } finally {
      this.joiningId.set(null);
    }
  }

  modeLabel(mode: GameSummary['mode']): string {
    return this.i18n.t(
      mode === 'TwoPlayer' ? 'lobby.mode.twoPlayer' : 'lobby.mode.fourPlayerTeams',
    );
  }
}
