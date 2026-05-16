import { Component, computed, input, output } from '@angular/core';
import { I18nPipe } from '../../shared/i18n.pipe';
import { GameFinishedEvent, GameMode } from './game.models';

interface ScoreRow {
  label: string;
  score: number;
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
    if (this.mode() === 'FourPlayerTeams') {
      return [
        { label: 'Team A', score: (scores[0] ?? 0) + (scores[2] ?? 0) },
        { label: 'Team B', score: (scores[1] ?? 0) + (scores[3] ?? 0) },
      ];
    }
    return scores.map((s, i) => ({ label: `Seat ${i}`, score: s }));
  });

  onBack(): void {
    this.backToLobby.emit();
  }
}
