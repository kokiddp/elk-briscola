import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { correlationIdInterceptor } from './core/correlation-id.interceptor';
import { httpTokenInterceptor } from './core/http-token.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([correlationIdInterceptor, httpTokenInterceptor])),
    // Lazy-load the animations engine the first time a component declares
    // a trigger. The Phase 8 table uses `@cardEnter` on each played card;
    // CSS `@media (prefers-reduced-motion: reduce)` neutralises transitions
    // when the user opts out.
    provideAnimationsAsync(),
  ],
};
