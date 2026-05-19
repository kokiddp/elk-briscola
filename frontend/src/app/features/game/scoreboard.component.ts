import { Component, computed, input } from '@angular/core';
import { I18nPipe } from '../../shared/i18n.pipe';
import { GameMode } from './game.models';

export interface ScoreboardRow {
  label: string;
  score: number;
  highlight: boolean;
}

@Component({
  selector: 'bri-scoreboard',
  standalone: true,
  imports: [I18nPipe],
  templateUrl: './scoreboard.component.html',
  styleUrl: './scoreboard.component.scss',
})
export class ScoreboardComponent {
  readonly seatScores = input.required<readonly number[]>();
  readonly mode = input.required<GameMode>();
  readonly nextToPlaySeat = input<number | null>(null);
  readonly seatNames = input<readonly string[]>([]);

  readonly is4p = computed(() => this.mode() === 'FourPlayerTeams');

  readonly rows = computed<ScoreboardRow[]>(() => {
    const scores = this.seatScores();
    const next = this.nextToPlaySeat();
    const names = this.seatNames();
    if (this.mode() === 'FourPlayerTeams') {
      const teamA = (scores[0] ?? 0) + (scores[2] ?? 0);
      const teamB = (scores[1] ?? 0) + (scores[3] ?? 0);
      const activeTeam = next === null ? -1 : next % 2 === 0 ? 0 : 1;
      const teamLabel = (a: number, b: number, fallback: string): string => {
        const pair = [names[a]?.trim(), names[b]?.trim()].filter((n): n is string => !!n);
        return pair.length === 2 ? pair.join(' + ') : fallback;
      };
      return [
        {
          label: teamLabel(0, 2, 'Team A'),
          score: teamA,
          highlight: activeTeam === 0,
        },
        {
          label: teamLabel(1, 3, 'Team B'),
          score: teamB,
          highlight: activeTeam === 1,
        },
      ];
    }
    return scores.map((score, seatIndex) => ({
      label: names[seatIndex]?.trim() || `Seat ${seatIndex}`,
      score,
      highlight: next === seatIndex,
    }));
  });
}
