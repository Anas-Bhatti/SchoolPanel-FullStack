// src/app/app.config.ts
import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import {
  provideHttpClient,
  withInterceptors,
  withFetch
} from '@angular/common/http';
import {
  provideRouter,
  withPreloading,
  PreloadAllModules,
  withComponentInputBinding,
  withViewTransitions,
  withRouterConfig
} from '@angular/router';
import { provideAnimationsAsync }   from '@angular/platform-browser/animations/async';
import { Title }                     from '@angular/platform-browser';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from '@angular/material/form-field';

import { routes }             from './app.routes';
import { jwtInterceptor }     from './core/interceptors/jwt.interceptor';
import { loadingInterceptor } from './core/interceptors/loading.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    // Zone-based change detection with batching
    provideZoneChangeDetection({ eventCoalescing: true }),

    // Router: lazy preloading + view transitions + input binding
    provideRouter(
      routes,
      withPreloading(PreloadAllModules),
      withComponentInputBinding(),
      withViewTransitions({ skipInitialTransition: true }),
      withRouterConfig({ onSameUrlNavigation: 'reload' })
    ),

    // HTTP: Fetch API + loading bar interceptor (outer) + JWT interceptor (inner)
    // Order is critical: loadingInterceptor wraps everything so the bar appears
    // immediately; jwtInterceptor handles auth on the inner chain.
    provideHttpClient(
      withFetch(),
      withInterceptors([loadingInterceptor, jwtInterceptor])
    ),

    // Async animations — defers the 60 kB animation bundle out of the critical path
    provideAnimationsAsync(),

    // Angular Material: outline fields by default, dynamic subscript sizing
    {
      provide: MAT_FORM_FIELD_DEFAULT_OPTIONS,
      useValue: { appearance: 'outline', subscriptSizing: 'dynamic', floatLabel: 'auto' }
    },

    // Make Title service available globally (used by AppShell)
    Title,
  ]
};