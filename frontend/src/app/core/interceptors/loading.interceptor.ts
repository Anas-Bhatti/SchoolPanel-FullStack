// src/app/core/interceptors/loading.interceptor.ts
import {
  HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpEvent
} from '@angular/common/http';
import { inject }    from '@angular/core';
import { Observable, finalize } from 'rxjs';
import { LoadingService } from '@shared/components/loading-bar/loading-bar.component';

/**
 * Drives the global loading bar.
 * Requests tagged with the X-Silent header are excluded
 * (background polling, audit-log writes, notification fetches).
 */
export const loadingInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> => {
  const loader = inject(LoadingService);

  if (req.headers.has('X-Silent')) {
    return next(req.clone({ headers: req.headers.delete('X-Silent') }));
  }

  loader.increment();
  return next(req).pipe(finalize(() => loader.decrement()));
};