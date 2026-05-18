import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { LobbyService } from './features/lobby/lobby.service';
import { TopNavComponent } from './shared/nav/top-nav.component';
import { ToastHostComponent } from './shared/toast-host.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastHostComponent, TopNavComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  // Eager-instantiate the lobby singleton so its auto-connect + auto-
  // route effects are live on every route — not just /lobby. Without
  // this the service is never created on a cold /profile (or /game)
  // load, so a creator who refreshed away from /lobby would never get
  // the gameStarted push that routes them into the table.
  private readonly _lobby = inject(LobbyService);
}
