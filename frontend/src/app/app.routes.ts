// src/app/app.routes.ts
import { Routes } from '@angular/router';
import { authGuard, guestGuard, permissionGuard } from '@core/guards/auth.guard';

export const routes: Routes = [

  /* ── Auth (guest-only pages) ─────────────────────────────── */
  {
    path: 'auth',
    canActivate: [guestGuard],
    children: [
      {
        path: 'login',
        title: 'Sign In — SchoolPanel',
        loadComponent: () =>
          import('./features/auth/login/login.component')
            .then(m => m.LoginComponent)
      },
      { path: '', redirectTo: 'login', pathMatch: 'full' }
    ]
  },

  /* ── Protected shell ─────────────────────────────────────── */
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./app-shell.component').then(m => m.AppShellComponent),
    children: [

      /* Dashboard */
      {
        path: 'dashboard',
        title: 'Dashboard — SchoolPanel',
        data: { title: 'Dashboard', icon: 'dashboard', permission: 'Dashboard' },
        canActivate: [permissionGuard],
        loadComponent: () =>
          import('./features/dashboard/dashboard.component')
            .then(m => m.DashboardComponent)
      },

      /* Students */
      {
        path: 'students',
        data: { permission: 'Students' },
        canActivate: [permissionGuard],
        children: [
          {
            path: '',
            title: 'Students — SchoolPanel',
            data: { title: 'Students', icon: 'school' },
            loadComponent: () =>
              import('./features/students/list/student-list.component')
                .then(m => m.StudentListComponent)
          },
          {
            path: 'new',
            title: 'New Student — SchoolPanel',
            data: { title: 'New Student', icon: 'person_add', action: 'canCreate' },
            canActivate: [permissionGuard],
            loadComponent: () =>
              import('./features/students/form/student-form.component')
                .then(m => m.StudentFormComponent)
          },
          {
            path: ':id/edit',
            title: 'Edit Student — SchoolPanel',
            data: { title: 'Edit Student', icon: 'edit', action: 'canEdit' },
            canActivate: [permissionGuard],
            loadComponent: () =>
              import('./features/students/form/student-form.component')
                .then(m => m.StudentFormComponent)
          },
          {
            path: ':id',
            title: 'Student Profile — SchoolPanel',
            data: { title: 'Student Profile', icon: 'person' },
            loadComponent: () =>
              import('./features/students/detail/student-detail.component')
                .then(m => m.StudentDetailComponent)
          }
        ]
      },

      /* Attendance */
      {
        path: 'attendance',
        title: 'Attendance — SchoolPanel',
        data: { title: 'Attendance', icon: 'how_to_reg', permission: 'Students' },
        canActivate: [permissionGuard],
        loadComponent: () =>
          import('./features/attendance/attendance.component')
            .then(m => m.AttendanceComponent)
      },

      /* Fees */
      {
        path: 'fees',
        data: { permission: 'Fees' },
        canActivate: [permissionGuard],
        children: [
          {
            path: '',
            title: 'Fees — SchoolPanel',
            data: { title: 'Fees', icon: 'payments' },
            loadComponent: () =>
              import('./features/fees/fees.component')
                .then(m => m.FeesComponent)
          },
          {
            path: 'pay',
            title: 'Record Payment — SchoolPanel',
            data: { title: 'Record Payment', icon: 'point_of_sale', action: 'canCreate' },
            canActivate: [permissionGuard],
            loadComponent: () =>
              import('./features/fees/pay/fee-pay.component')
                .then(m => m.FeePayComponent)
          }
        ]
      },

      /* Reports */
      {
        path: 'reports',
        title: 'Reports — SchoolPanel',
        data: { title: 'Reports', icon: 'assessment', permission: 'Reports' },
        canActivate: [permissionGuard],
        loadComponent: () =>
          import('./features/reports/reports.component')
            .then(m => m.ReportsComponent)
      },

      /* Settings */
      {
        path: 'settings',
        title: 'Settings — SchoolPanel',
        data: { title: 'Settings', icon: 'settings', permission: 'Settings' },
        canActivate: [permissionGuard],
        loadComponent: () =>
          import('./features/settings/settings.component')
            .then(m => m.SettingsComponent)
      },

      /* Default redirect */
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' }
    ]
  },

  /* ── 404 ─────────────────────────────────────────────────── */
  {
    path: '404',
    title: 'Not Found — SchoolPanel',
    loadComponent: () =>
      import('./features/not-found/not-found.component')
        .then(m => m.NotFoundComponent)
  },

  /* Catch-all */
  { path: '**', redirectTo: '/404', pathMatch: 'full' }
];