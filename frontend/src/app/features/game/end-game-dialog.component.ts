import { Component, computed, input, output } from '@angular/core';
import { I18nPipe } from '../../shared/i18n.pipe';
import { GameFinishedEvent, GameMode, PlayerInfo } from './game.models';

interface ScoreRow {
  label: string;
  score: number;
  elo: number | null;
}

@Component({
  selector: 'bri-end-game-dialog',
  standalone: true,
  imports: [I18nPipe],
  templateUrl: './end-game-dialog.component.html',
  styleUrl: './end-game-dialog.component.scss',
})
export class EndGameDialogComponent {
  readonly result = input.required<GameFinishedEvent>();
  readonly mode = input.required<GameMode>();
  readonly mySeat = input<number | null>(null);
  /** Spectators have no rooting interest — they should see a neutral
   *  "game ended" banner, not the seat-relative win/lose copy. The
   *  game-table page already knows isSpectator from the route + the
   *  GameService, so we forward it here. */
  readonly isSpectator = input<boolean>(false);
  /** Per-seat names + Elos pulled from the final snapshot. The Elo
   *  values are post-game (the rankingUpdated push fires before
   *  GameFinished, see GameRoom.SaveFinishedAsync). */
  readonly seatPlayers = input<(PlayerInfo | null)[]>([]);

  readonly backToLobby = output();

  readonly winnerKey = computed<number | null>(() => this.result().outcome.winnerKey ?? null);

  readonly isWinForMe = computed<boolean | null>(() => {
    const me = this.mySeat();
    const w = this.winnerKey();
    if (me === null || w === null) {
      return null;
    }
    if (this.mode() === 'FourPlayerTeams') {
      return me % 2 === w;
    }
    return me === w;
  });

  readonly isDraw = computed(() => this.result().outcome.kind === 'Draw');

  readonly bannerKey = computed<string>(() => {
    // Spectators never get a personal win/lose banner — they're not
    // playing. Show the neutral game-ended copy regardless of outcome.
    if (this.isSpectator()) {
      return 'game.end.gameEnded';
    }
    if (this.isDraw()) {
      return 'game.end.draw';
    }
    const win = this.isWinForMe();
    if (win === true) return 'game.end.youWin';
    if (win === false) return 'game.end.youLose';
    return 'game.end.result';
  });

  readonly rows = computed<ScoreRow[]>(() => {
    const scores = this.result().seatScores;
    const players = this.seatPlayers();
    const nameOf = (i: number): string => players[i]?.displayName ?? `Seat ${i}`;
    const eloOf = (i: number): number | null => players[i]?.elo ?? null;
    if (this.mode() === 'FourPlayerTeams') {
      // Team labels = the two seats on the team, comma-separated.
      // Team Elo is the post-game average, null if either seat is
      // anonymous. One divide per team — clearer than the previous
      // nested null-guards.
      const teamElo = (a: number, b: number): number | null => {
        const ea = eloOf(a);
        const eb = eloOf(b);
        return ea !== null && eb !== null ? Math.round((ea + eb) / 2) : null;
      };
      return [
        {
          label: [nameOf(0), nameOf(2)].join(' + '),
          score: (scores[0] ?? 0) + (scores[2] ?? 0),
          elo: teamElo(0, 2),
        },
        {
          label: [nameOf(1), nameOf(3)].join(' + '),
          score: (scores[1] ?? 0) + (scores[3] ?? 0),
          elo: teamElo(1, 3),
        },
      ];
    }
    return scores.map((s, i) => ({ label: nameOf(i), score: s, elo: eloOf(i) }));
  });

  onBack(): void {
    this.backToLobby.emit();
  }
}
