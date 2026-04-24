// ============================================================
// core/guards/auth.guard.ts
// Protects routes that require authentication.
// ============================================================

import { inject } from '@angular/core';
import { CanActivateFn, Router, ActivatedRouteSnapshot } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (route, state) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (auth.isLoggedIn()) {
    return true;
  }

  router.navigate(['/auth/login'], {
    queryParams: { returnUrl: state.url }
  });
  return false;
};

// ============================================================
// core/guards/permission.guard.ts
// Protects routes requiring a specific module permission.
// Route data: { permission: 'Students', action: 'canView' }
// ============================================================

export const permissionGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (!auth.isLoggedIn()) {
    router.navigate(['/auth/login']);
    return false;
  }

  const module = route.data['permission'] as string;
  const action = route.data['action'] as
    'canView' | 'canCreate' | 'canEdit' | 'canDelete' | 'canExport'
    ?? 'canView';

  if (!module || auth.hasPermission(module, action)) {
    return true;
  }

  // Redirect to dashboard with access denied toast
  router.navigate(['/dashboard']);
  return false;
};

// ============================================================
// core/guards/guest.guard.ts
// Redirect already-logged-in users away from auth pages.
// ============================================================

export const guestGuard: CanActivateFn = () => {
  const auth   = inject(AuthService);
  const router = inject(Router);

  if (!auth.isLoggedIn()) {
    return true;
  }

  router.navigate(['/dashboard']);
  return false;
};