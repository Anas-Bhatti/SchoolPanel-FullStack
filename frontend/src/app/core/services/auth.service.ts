// ============================================================
// core/services/auth.service.ts
// Manages the complete auth lifecycle using Angular signals.
// - JWT storage in localStorage
// - Auto-refresh via timer
// - Permission bitmask parsed from JWT claims
// ============================================================

import { Injectable, inject, signal, computed, effect } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, throwError, timer } from 'rxjs';
import { tap, catchError, switchMap } from 'rxjs/operators';
import { jwtDecode } from 'jwt-decode';
import { environment } from '@env/environment';
import type {
  LoginRequest, LoginResponse, AuthUser, Permission,
  TwoFactorVerifyRequest, GoogleLoginRequest,
  RefreshTokenRequest, TwoFactorSetupResponse,
  ApiResult
} from '../models';

// ─── JWT payload shape ────────────────────────────────────────
interface JwtPayload {
  sub: string;
  uid: string;
  email: string;
  name: string;
  role: string | string[];
  perm: string | string[];
  exp: number;
  iat: number;
}

const ACCESS_TOKEN_KEY  = 'sp_access_token';
const REFRESH_TOKEN_KEY = 'sp_refresh_token';
const USER_KEY          = 'sp_user';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http   = inject(HttpClient);
  private router = inject(Router);
  private api    = `${environment.apiUrl}/auth`;

  // ── Signals ───────────────────────────────────────────────
  readonly currentUser   = signal<AuthUser | null>(this.loadUser());
  readonly isLoggedIn    = computed(() => this.currentUser() !== null);
  readonly permissions   = computed(() => this.currentUser()?.permissions ?? []);
  readonly roles         = computed(() => this.currentUser()?.roles ?? []);
  readonly isSuperAdmin  = computed(() => this.roles().includes('SuperAdmin'));

  // 2FA pending state
  readonly pendingToken  = signal<string | null>(null);
  readonly requires2fa   = signal<boolean>(false);

  // Token refresh timer reference
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    // Auto-schedule refresh on startup if token exists
    effect(() => {
      if (this.isLoggedIn()) {
        this.scheduleRefresh();
      }
    });
  }


  /**
 * Build an AuthUser object from the access token.
 */
private buildUserFromToken(token: string): AuthUser {
  const decoded = jwtDecode<JwtPayload>(token);
  const roles = Array.isArray(decoded.role) ? decoded.role : [decoded.role];
  const permissions = this.parsePermissions(decoded.perm);
  return {
    userId: decoded.uid,
    email: decoded.email,
    fullName: decoded.name,
    roles: roles,
    permissions: permissions
  };
}

/**
 * Convert the permission string array from the token into the Permission[] format.
 * The token gives permissions as e.g. "Students:11111" where the 5‑digit string
 * represents canView (pos0), canCreate (pos1), canEdit (pos2), canDelete (pos3), canExport (pos4).
 */
private parsePermissions(permArray: string | string[]): Permission[] {
  const perms: Permission[] = [];
  const items = Array.isArray(permArray) ? permArray : [permArray];
  for (const item of items) {
    const [module, bits] = item.split(':');
    const canView   = bits[0] === '1';
    const canCreate = bits[1] === '1';
    const canEdit   = bits[2] === '1';
    const canDelete = bits[3] === '1';
    const canExport = bits[4] === '1';
    perms.push({ module, canView, canCreate, canEdit, canDelete, canExport });
  }
  return perms;
}

  // ═══════════════════════════════════════════════════════════
  // Login
  // ═══════════════════════════════════════════════════════════
  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.api}/login`, request).pipe(
      tap(response => {
        if (response.requiresTwoFactor && response.pendingToken) {
          this.pendingToken.set(response.pendingToken);
          this.requires2fa.set(true);
        } else if (response.accessToken) 
      {
        const user = response.user || this.buildUserFromToken(response.accessToken);
        this.handleAuthSuccess(
        response.accessToken,
        response.refreshToken!,
        user
        );
     }
      })
    );
  }

  // ═══════════════════════════════════════════════════════════
  // Two-Factor Verify
  // ═══════════════════════════════════════════════════════════
  verifyTwoFactor(code: string): Observable<LoginResponse> {
    const body: TwoFactorVerifyRequest = {
      pendingToken: this.pendingToken()!,
      code
    };

    return this.http.post<LoginResponse>(`${this.api}/login/2fa-verify`, body).pipe(
      tap(response => {
        if (response.accessToken && response.user) {
          this.pendingToken.set(null);
          this.requires2fa.set(false);
          this.handleAuthSuccess(
            response.accessToken,
            response.refreshToken!,
            response.user
          );
        }
      })
    );
  }

  // ═══════════════════════════════════════════════════════════
  // Google OAuth
  // ═══════════════════════════════════════════════════════════
  googleLogin(idToken: string): Observable<LoginResponse> {
    const body = {
      IdToken: idToken,
      DeviceInfo: navigator.userAgent
    };

    return this.http.post<LoginResponse>(`${this.api}/google-login`, body).pipe(
      tap(response => {
        if (response.accessToken && response.user) {
          this.handleAuthSuccess(
            response.accessToken,
            response.refreshToken!,
            response.user
          );
        }
      })
    );
  }

  // ═══════════════════════════════════════════════════════════
  // 2FA Setup
  // ═══════════════════════════════════════════════════════════
  setup2fa(): Observable<TwoFactorSetupResponse> {
    return this.http.post<TwoFactorSetupResponse>(`${this.api}/2fa/setup`, {});
  }

  verifySetup2fa(code: string): Observable<any> {
    return this.http.post(`${this.api}/2fa/verify-setup`, { code });
  }

  disable2fa(code: string): Observable<any> {
    return this.http.post(`${this.api}/2fa/disable`, { code });
  }

  // ═══════════════════════════════════════════════════════════
  // Refresh Token
  // ═══════════════════════════════════════════════════════════
  refreshToken(): Observable<{ accessToken: string; refreshToken: string }> {
    const token = this.getRefreshToken();
    if (!token) return throwError(() => new Error('No refresh token'));

    const body = {
      RefreshToken: token,
      DeviceInfo: navigator.userAgent
    };

    return this.http.post<any>(`${this.api}/refresh`, body).pipe(
      tap(response => {
        if (response.accessToken) {
          localStorage.setItem(ACCESS_TOKEN_KEY, response.accessToken);
          localStorage.setItem(REFRESH_TOKEN_KEY, response.refreshToken);
          this.scheduleRefresh();
        }
      }),
      catchError(err => {
        this.logout();
        return throwError(() => err);
      })
    );
  }

  // ═══════════════════════════════════════════════════════════
  // Logout
  // ═══════════════════════════════════════════════════════════
  logout(): void {
    // Best-effort server logout (don't wait)
    const token = this.getAccessToken();
    if (token) {
      this.http.post(`${this.api}/logout`, {}).subscribe({ error: () => {} });
    }

    this.clearSession();
    this.router.navigate(['/auth/login']);
  }

  // ═══════════════════════════════════════════════════════════
  // Permission helpers
  // ═══════════════════════════════════════════════════════════
  hasPermission(module: string, action: 'canView' | 'canCreate' | 'canEdit' | 'canDelete' | 'canExport'): boolean {
    if (this.isSuperAdmin()) return true;
    const perm = this.permissions().find(
      p => p.module.toLowerCase() === module.toLowerCase()
    );
    return perm?.[action] ?? false;
  }

  hasRole(role: string): boolean {
    return this.roles().includes(role);
  }

  canAccess(module: string): boolean {
    return this.hasPermission(module, 'canView');
  }

  // ═══════════════════════════════════════════════════════════
  // Token accessors
  // ═══════════════════════════════════════════════════════════
  getAccessToken(): string | null {
    return localStorage.getItem(ACCESS_TOKEN_KEY);
  }

  getRefreshToken(): string | null {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  }

  isTokenExpired(): boolean {
    const token = this.getAccessToken();
    if (!token) return true;

    try {
      const decoded = jwtDecode<JwtPayload>(token);
      // Add 30s buffer
      return (decoded.exp * 1000) < (Date.now() + 30_000);
    } catch {
      return true;
    }
  }

  // ═══════════════════════════════════════════════════════════
  // Private helpers
  // ═══════════════════════════════════════════════════════════
  private handleAuthSuccess(
    accessToken: string,
    refreshToken: string,
    user: AuthUser
  ): void {
    localStorage.setItem(ACCESS_TOKEN_KEY,  accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
    localStorage.setItem(USER_KEY, JSON.stringify(user));

    this.currentUser.set(user);
    this.scheduleRefresh();
  }

  private clearSession(): void {
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    this.currentUser.set(null);

    if (this.refreshTimer) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = null;
    }
  }

  private loadUser(): AuthUser | null {
    try {
      const raw = localStorage.getItem(USER_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }

  private scheduleRefresh(): void {
    if (this.refreshTimer) {
      clearTimeout(this.refreshTimer);
    }

    const token = this.getAccessToken();
    if (!token) return;

    try {
      const decoded = jwtDecode<JwtPayload>(token);
      const expiresIn = (decoded.exp * 1000) - Date.now();
      // Refresh 2 minutes before expiry
      const refreshIn = Math.max(expiresIn - 120_000, 10_000);

      this.refreshTimer = setTimeout(() => {
        this.refreshToken().subscribe({ error: () => {} });
      }, refreshIn);
    } catch {
      // Invalid token — let interceptor handle it
    }
  }
}