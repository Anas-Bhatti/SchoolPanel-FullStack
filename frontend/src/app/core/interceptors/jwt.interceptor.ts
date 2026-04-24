// ============================================================
// core/interceptors/jwt.interceptor.ts
// Functional HTTP interceptor (Angular 17+ style).
// - Attaches Bearer token to every API request
// - Intercepts 401 → queues requests → refreshes → retries
// - On 403 → logs out (permission denied, not recoverable)
// - Skips auth endpoints to prevent infinite loops
// ============================================================

import {
  HttpInterceptorFn, HttpRequest, HttpHandlerFn,
  HttpErrorResponse, HttpEvent
} from '@angular/common/http';
import { inject } from '@angular/core';
import {
  Observable, throwError, BehaviorSubject, switchMap,
  filter, take, catchError
} from 'rxjs';
import { AuthService } from '../services/auth.service';
import { ToastService } from '../services/api.services';
import { environment } from '@env/environment';

// Shared state for token refresh queue
let isRefreshing      = false;
const refreshSubject  = new BehaviorSubject<string | null>(null);

// Endpoints that should never have a Bearer token attached
const SKIP_URLS = [
  '/auth/login',
  '/auth/refresh',
  '/auth/google-login',
  '/auth/login/2fa-verify'
];

const isApiUrl    = (url: string) => url.startsWith(environment.apiUrl);
const isSkippedUrl = (url: string) => SKIP_URLS.some(s => url.includes(s));

export const jwtInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> => {
  const auth  = inject(AuthService);
  const toast = inject(ToastService);

  // Only intercept our API calls
  if (!isApiUrl(req.url) || isSkippedUrl(req.url)) {
    return next(req);
  }

  const token = auth.getAccessToken();
  const authReq = token ? addToken(req, token) : req;

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401) {
        return handle401(req, next, auth, toast);
      }

      if (error.status === 403) {
        toast.error('Access Denied', 'You do not have permission to perform this action.');
        return throwError(() => error);
      }

      if (error.status === 0) {
        toast.error('Network Error', 'Could not reach the server. Check your connection.');
        return throwError(() => error);
      }

      if (error.status >= 500) {
        toast.error('Server Error', error.error?.detail || 'An unexpected error occurred.');
        return throwError(() => error);
      }

      return throwError(() => error);
    })
  );
};

function addToken(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({
    setHeaders: { Authorization: `Bearer ${token}` }
  });
}

function handle401(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  auth: AuthService,
  toast: ToastService
): Observable<HttpEvent<unknown>> {
  if (!isRefreshing) {
    isRefreshing = true;
    refreshSubject.next(null);

    return auth.refreshToken().pipe(
      switchMap(response => {
        isRefreshing = false;
        refreshSubject.next(response.accessToken);
        return next(addToken(req, response.accessToken));
      }),
      catchError(err => {
        isRefreshing = false;
        refreshSubject.next(null);
        auth.logout();
        toast.error('Session Expired', 'Your session has expired. Please log in again.');
        return throwError(() => err);
      })
    );
  }

  // Queue this request until refresh completes
  return refreshSubject.pipe(
    filter(token => token !== null),
    take(1),
    switchMap(token => next(addToken(req, token!)))
  );
}