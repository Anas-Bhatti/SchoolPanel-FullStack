// src/app/shared/components/shared-components.ts
//
// Central barrel — import shared UI pieces from this single path instead of
// reaching into individual component files from feature modules.
//
// Usage:
//   import { HeaderComponent, SidebarComponent } from '@shared/components/shared-components';
//   import { LoadingService }                     from '@shared/components/shared-components';

// ── Layout ────────────────────────────────────────────────────
export { HeaderComponent }           from './components/header/header.component';
export { SidebarComponent }          from './components/sidebar/sidebar.component';

// ── Feedback ──────────────────────────────────────────────────
export { ToastComponent }            from './components/toast/toast.component';
export {
  LoadingBarComponent,
  LoadingService
}                                    from './components/loading-bar/loading-bar.component';

// ── Notifications ─────────────────────────────────────────────
export { NotificationBellComponent } from './components/notification-bell/notification-bell.component';

// ── Forms / Input ─────────────────────────────────────────────
export {
  FileUploadComponent
}                                    from './components/file-upload/file-upload.component';
export type { UploadConfig }         from './components/file-upload/file-upload.component';