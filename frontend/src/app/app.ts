import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TopNavComponent } from './shared/nav/top-nav.component';
import { ToastHostComponent } from './shared/toast-host.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastHostComponent, TopNavComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}
