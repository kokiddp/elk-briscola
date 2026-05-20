import { Component, OnInit, effect, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ErrorToastService } from '../../core/error-toast.service';
import { I18nService } from '../../core/i18n.service';
import { I18nPipe } from '../../shared/i18n.pipe';
import { CreateGameDialogComponent } from './create-game-dialog.component';
import { JoinPasswordDialogComponent } from './join-password-dialog.component';
import { LobbyChatPanelComponent } from './lobby-chat-panel.component';
import { CreateGameRequest, GameSummary } from './lobby.models';
import { LobbyService } from './lobby.service';

@Component({
  selector: 'bri-lobby',
  standalone: true,
  imports: [
    I18nPipe,
    RouterLink,
    CreateGameDialogComponent,
    JoinPasswordDialogComponent,
    LobbyChatPanelComponent,
  ],
  templateUrl: './lobby.component.html',
  styleUrl: './lobby.component.scss',
})
export class LobbyComponent implements OnInit {
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
   * Whether the user is sitting in an open game. Read straight from
   * the LobbyService's signal, which is derived from the openGames
   * list + the auth'd user id — so the state survives a page refresh
   * and is correct from any route.
   */
  readonly pendingGame = this.lobby.pendingGame;
  readonly pendingGameId = this.lobby.pendingGameId;
  readonly hasPendingGame = this.lobby.hasPendingGame;
  /** Cancel-in-flight flag — drives the disabled state on the cancel
   *  button so a double-click can't fire two leaveGame requests. */
  readonly cancelling = signal(false);

  constructor() {
    // Abandoned-pending-game toast: when the server reaps our open game
    // for being unfilled, surface the i18n'd notice. The LobbyService
    // already wipes the row from the open list, so the pending banner
    // disappears on its own.
    effect(() => {
      const ended = this.lobby.lastEndedGameId();
      if (!ended) {
        return;
      }
      // We see the ended id BEFORE the openGames signal updates (the
      // dispatcher publishes the event before the SignalR client
      // removes the row), so we can still inspect what just disappeared.
      const pending = this.pendingGameId();
      this.lobby.clearLastEndedGameId();
      if (ended === pending) {
        this.toast.info(this.i18n.t('lobby.errors.openGameAbandoned'));
      }
    });
  }

  async ngOnInit(): Promise<void> {
    // Connection is owned by the LobbyService at the app-singleton
    // level (auto-connects on login, stays up across navigations).
    // We still try connect() here so a hot-loaded /lobby works even
    // before the auth effect fires.
    try {
      await this.lobby.connect();
    } catch {
      this.toast.error(this.i18n.t('lobby.errors.connectFailed'));
    }
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
      }
      // Else: stay on /lobby. The pendingGame computed picks this up
      // once the create response is reflected in the open list (either
      // via SignalR gameCreated or the refreshLists round-trip).
    } catch {
      this.toast.error(this.i18n.t('lobby.errors.createFailed'));
    } finally {
      this.dialogSubmitting.set(false);
    }
  }

  /** Game currently awaiting a password from the user via the join
   *  dialog. Non-null means the modal is open. */
  readonly passwordPromptFor = signal<GameSummary | null>(null);

  async onJoin(game: GameSummary): Promise<void> {
    if (this.joiningId()) {
      return;
    }
    if (game.isPrivate) {
      // Show the modal — the rest of the flow continues from
      // onPasswordSubmitted once the user submits.
      this.passwordPromptFor.set(game);
      return;
    }
    await this.performJoin(game, null);
  }

  async onPasswordSubmitted(password: string): Promise<void> {
    const game = this.passwordPromptFor();
    if (!game) {
      return;
    }
    this.passwordPromptFor.set(null);
    await this.performJoin(game, password);
  }

  onPasswordCancelled(): void {
    this.passwordPromptFor.set(null);
  }

  private async performJoin(game: GameSummary, password: string | null): Promise<void> {
    this.joiningId.set(game.id);
    try {
      const detail = await this.lobby.joinGame(game.id, password);
      if (detail.status === 'Running') {
        await this.router.navigateByUrl(`/game/${game.id}`);
      }
    } catch {
      this.toast.error(this.i18n.t('lobby.errors.joinFailed'));
    } finally {
      this.joiningId.set(null);
    }
  }

  /**
   * Creator-side cancel for an open game that hasn't started yet.
   * Server-side this is a `LeaveAsync` — the seat clears, the game
   * empties, and (when the creator was the only one seated) the open
   * list view drops to zero occupants. The OpenLobbyJanitor will reap
   * the empty record on its next tick.
   */
  async onCancelPending(): Promise<void> {
    const id = this.pendingGameId();
    if (!id || this.cancelling()) {
      return;
    }
    this.cancelling.set(true);
    try {
      await this.lobby.leaveGame(id);
    } catch {
      this.toast.error(this.i18n.t('lobby.errors.cancelFailed'));
    } finally {
      this.cancelling.set(false);
    }
  }

  modeLabel(mode: GameSummary['mode']): string {
    return this.i18n.t(
      mode === 'TwoPlayer' ? 'lobby.mode.twoPlayer' : 'lobby.mode.fourPlayerTeams',
    );
  }
}
