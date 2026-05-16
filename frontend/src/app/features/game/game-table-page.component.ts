import { Component, OnDestroy, OnInit, computed, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ErrorToastService } from '../../core/error-toast.service';
import { I18nService } from '../../core/i18n.service';
import { I18nPipe } from '../../shared/i18n.pipe';
import { GameService } from './game.service';

@Component({
  selector: 'bri-game-table-page',
  standalone: true,
  imports: [I18nPipe],
  templateUrl: './game-table-page.component.html',
  styleUrl: './game-table-page.component.scss',
})
export class GameTablePageComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly game = inject(GameService);
  private readonly toast = inject(ErrorToastService);
  private readonly i18n = inject(I18nService);

  readonly state = this.game.state;
  readonly isReady = computed(() => this.state() !== null);
  readonly is4p = computed(() => this.state()?.mode === 'FourPlayerTeams');
  readonly opponentSeats = computed<number[]>(() => {
    const s = this.state();
    if (!s) {
      return [];
    }
    const total = s.handCountsBySeat.length;
    // v1: the snapshot is rendered from the calling player's perspective but
    // the API doesn't yet hand us "mySeat" directly. Phase 9 wires the seat
    // resolver; here we pre-derive the iteration order so the table renders.
    const seats: number[] = [];
    for (let i = 0; i < total; i++) {
      if (i !== s.nextToPlaySeat || total === 1) {
        seats.push(i);
      }
    }
    return seats;
  });

  ngOnInit(): void {
    const gameId = this.route.snapshot.paramMap.get('id');
    if (!gameId) {
      return;
    }
    this.game.connect(gameId).catch(() => {
      this.toast.error(this.i18n.t('game.errors.connectFailed'));
    });
  }

  ngOnDestroy(): void {
    void this.game.disconnect();
  }
}
