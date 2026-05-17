import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/route-guards';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'home' },
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./features/auth/register.component').then((m) => m.RegisterComponent),
  },
  {
    path: 'home',
    canActivate: [authGuard],
    loadComponent: () => import('./features/home/home.component').then((m) => m.HomeComponent),
  },
  {
    path: 'lobby',
    canActivate: [authGuard],
    loadComponent: () => import('./features/lobby/lobby.component').then((m) => m.LobbyComponent),
  },
  {
    path: 'game/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/game/game-table-page.component').then((m) => m.GameTablePageComponent),
  },
  {
    path: 'profile',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/profile/profile.component').then((m) => m.ProfileComponent),
  },
  { path: '**', redirectTo: 'home' },
];
