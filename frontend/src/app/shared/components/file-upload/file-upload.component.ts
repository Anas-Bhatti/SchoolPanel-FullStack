// src/app/shared/components/file-upload/file-upload.component.ts

import {
  Component, inject, input, output, signal, computed,
  ElementRef, viewChild, OnDestroy
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  HttpClient, HttpEventType, HttpRequest
} from '@angular/common/http';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule }     from '@angular/material/tooltip';
import { Subject }              from 'rxjs';
import { takeUntil }            from 'rxjs/operators';
import { ToastService }         from '@core/services/api.services';

// ── Public config type ────────────────────────────────────────
export interface UploadConfig {
  /** Comma-separated MIME types shown in the file picker. Default: images + pdf. */
  accept:      string;
  /** Maximum file size in megabytes. Default: 5 */
  maxSizeMb:   number;
  /** Label shown inside the drop zone. */
  label:       string;
  /** Optional sub-label / hint text. */
  hint?:       string;
  /** Show an <img> preview after selection. Only meaningful for image types. */
  showPreview: boolean;
  /** Shape of the preview: 'circle' for avatars, 'rect' for logos. Default: 'rect' */
  previewShape?: 'circle' | 'rect';
  /** If provided the component will POST the file here and report progress. */
  endpoint?:   string;
}

const DEFAULT_CONFIG: UploadConfig = {
  accept:      'image/jpeg,image/png,image/webp,application/pdf',
  maxSizeMb:   5,
  label:       'Drag & drop or click to upload',
  showPreview: true,
  previewShape: 'rect'
};

// MIME types that are always allowed for image preview
const IMAGE_MIMES = new Set([
  'image/jpeg', 'image/jpg', 'image/png',
  'image/webp', 'image/gif', 'image/svg+xml'
]);

@Component({
  selector:   'sp-file-upload',
  standalone: true,
  imports:    [CommonModule, MatProgressBarModule, MatTooltipModule],
  template: `
    <!-- Drop zone -->
    <div
      class="upload-zone"
      [class.upload-zone--dragging]="isDragging()"
      [class.upload-zone--has-file]="hasFile()"
      [class.upload-zone--circle]="cfg().previewShape === 'circle'"
      [class.upload-zone--error]="!!error()"
      role="button"
      tabindex="0"
      [attr.aria-label]="cfg().label"
      (click)="openPicker()"
      (keydown.enter)="openPicker()"
      (keydown.space)="openPicker()"
      (dragover)="onDragOver($event)"
      (dragleave)="onDragLeave()"
      (drop)="onDrop($event)">

      <!-- Image preview -->
      @if (cfg().showPreview && previewUrl()) {
        <img
          [src]="previewUrl()"
          alt="File preview"
          class="preview-img"
          [class.preview-img--circle]="cfg().previewShape === 'circle'">
        <div class="preview-overlay" aria-hidden="true">
          <span class="material-icons-round">photo_camera</span>
        </div>
        <button
          class="remove-btn"
          type="button"
          aria-label="Remove selected file"
          (click)="removeFile($event)">
          <span class="material-icons-round" aria-hidden="true">close</span>
        </button>

      <!-- Non-image file row -->
      } @else if (hasFile() && !cfg().showPreview) {
        <div class="file-row">
          <div class="file-icon">
            <span class="material-icons-round" aria-hidden="true">description</span>
          </div>
          <div class="file-meta">
            <span class="file-name">{{ selectedFile()!.name }}</span>
            <span class="file-size">{{ formatSize(selectedFile()!.size) }}</span>
          </div>
          <button
            class="remove-btn remove-btn--inline"
            type="button"
            aria-label="Remove selected file"
            (click)="removeFile($event)">
            <span class="material-icons-round" aria-hidden="true">close</span>
          </button>
        </div>

      <!-- Empty / idle state -->
      } @else {
        <div class="idle-content">
          <div class="idle-icon" [class.idle-icon--drag]="isDragging()" aria-hidden="true">
            <span class="material-icons-round">
              {{ isDragging() ? 'file_download' : 'cloud_upload' }}
            </span>
          </div>
          <p class="idle-label">{{ cfg().label }}</p>
          @if (cfg().hint) {
            <p class="idle-hint">{{ cfg().hint }}</p>
          }
          <p class="idle-limit">Max {{ cfg().maxSizeMb }}&thinsp;MB</p>
        </div>
      }

    </div>

    <!-- Hidden file input -->
    <input
      #fileInput
      type="file"
      [accept]="cfg().accept"
      aria-hidden="true"
      tabindex="-1"
      style="display:none"
      (change)="onFileSelected($event)">

    <!-- Validation error -->
    @if (error()) {
      <p class="upload-error" role="alert">
        <span class="material-icons-round" aria-hidden="true">error_outline</span>
        {{ error() }}
      </p>
    }

    <!-- Upload progress -->
    @if (progress() > 0 && progress() < 100) {
      <mat-progress-bar
        mode="determinate"
        [value]="progress()"
        class="upload-bar"
        [attr.aria-label]="'Upload progress ' + progress() + '%'"
        aria-valuemin="0"
        aria-valuemax="100"
        [attr.aria-valuenow]="progress()">
      </mat-progress-bar>
      <p class="progress-label" aria-live="polite">Uploading… {{ progress() }}%</p>
    }

    @if (progress() === 100) {
      <p class="upload-success" role="status">
        <span class="material-icons-round" aria-hidden="true">check_circle</span>
        Upload complete
      </p>
    }
  `,
  styles: [`
    /* ── Drop zone ───────────────────────────────────────────── */
    .upload-zone {
      position: relative;
      border: 2px dashed var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-2);
      min-height: 120px;
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      overflow: hidden;
      transition:
        border-color var(--transition-fast),
        background   var(--transition-fast);
      outline: none;

      &:hover,
      &:focus-visible,
      &--dragging {
        border-color: var(--color-primary);
        background:   var(--color-primary-50);
      }

      &:focus-visible { box-shadow: 0 0 0 3px rgba(var(--color-primary-rgb), .25); }

      &--has-file  { min-height: unset; padding: 0; }
      &--circle    { border-radius: 50%; }
      &--error     { border-color: var(--color-danger); }
    }

    /* ── Idle / empty state ──────────────────────────────────── */
    .idle-content {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 5px;
      padding: 20px 16px;
      text-align: center;
      pointer-events: none;   /* let clicks reach the zone */
    }

    .idle-icon {
      width: 48px; height: 48px;
      background: var(--color-surface);
      border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
      margin-bottom: 4px;
      transition: background var(--transition-fast);

      .material-icons-round { font-size: 24px; color: var(--color-primary); }

      &--drag { background: var(--color-primary-100); }
    }

    .idle-label {
      font-size: 13px; font-weight: 500;
      color: var(--color-text); margin: 0;
    }

    .idle-hint  { font-size: 11px; color: var(--color-text-secondary); margin: 0; }
    .idle-limit { font-size: 11px; color: var(--color-text-muted);     margin: 0; }

    /* ── Image preview ───────────────────────────────────────── */
    .preview-img {
      display: block;
      width: 100%; height: 100%;
      max-height: 160px;
      object-fit: cover;

      &--circle {
        width: 120px; height: 120px;
        border-radius: 50%;
      }
    }

    .preview-overlay {
      position: absolute; inset: 0;
      background: rgba(0,0,0,.3);
      display: flex; align-items: center; justify-content: center;
      opacity: 0;
      transition: opacity var(--transition-fast);

      .material-icons-round { font-size: 28px; color: #fff; }
    }

    .upload-zone:hover .preview-overlay { opacity: 1; }

    /* ── Non-image file row ──────────────────────────────────── */
    .file-row {
      display: flex; align-items: center; gap: 10px;
      padding: 12px 14px; width: 100%;
    }

    .file-icon {
      width: 36px; height: 36px; border-radius: 8px;
      background: var(--color-primary-50);
      display: flex; align-items: center; justify-content: center;
      flex-shrink: 0;
      .material-icons-round { font-size: 20px; color: var(--color-primary); }
    }

    .file-meta {
      flex: 1; min-width: 0;
      display: flex; flex-direction: column; gap: 2px;
    }

    .file-name {
      font-size: 13px; font-weight: 500;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
    }

    .file-size { font-size: 11px; color: var(--color-text-secondary); }

    /* ── Remove button ───────────────────────────────────────── */
    .remove-btn {
      position: absolute; top: 4px; right: 4px;
      width: 22px; height: 22px; border-radius: 50%;
      background: var(--color-danger); border: none;
      cursor: pointer; z-index: 2;
      display: flex; align-items: center; justify-content: center;
      color: #fff; transition: opacity var(--transition-fast);
      &:hover { opacity: .85; }

      .material-icons-round { font-size: 13px; }

      &--inline {
        position: static; background: var(--color-surface-2);
        color: var(--color-text-muted); flex-shrink: 0;
        width: 28px; height: 28px; border-radius: 6px;

        &:hover { background: var(--color-danger); color: #fff; }
        .material-icons-round { font-size: 16px; }
      }
    }

    /* ── Feedback ────────────────────────────────────────────── */
    .upload-error {
      display: flex; align-items: center; gap: 5px;
      color: var(--color-danger); font-size: 12px; margin: 6px 0 0;
      .material-icons-round { font-size: 15px; }
    }

    .upload-bar { margin-top: 8px; border-radius: 4px; height: 4px; }

    .progress-label {
      font-size: 11px; color: var(--color-text-secondary);
      text-align: center; margin: 3px 0 0;
    }

    .upload-success {
      display: flex; align-items: center; gap: 5px;
      color: var(--color-success); font-size: 12px; margin: 6px 0 0;
      .material-icons-round { font-size: 16px; }
    }
  `]
})
export class FileUploadComponent implements OnDestroy {

  // ── Inputs / outputs ────────────────────────────────────────

  /** Partial config — merged with defaults */
  config = input<Partial<UploadConfig>>({});

  /** Emits the raw File object as soon as the user selects / drops it */
  fileSelected = output<File>();

  /** Emits when the file is removed */
  fileRemoved  = output<void>();

  /**
   * Emits the URL string returned by the server when `endpoint` is set.
   * For manual uploads (no endpoint) this never fires — handle via `fileSelected`.
   */
  uploadDone   = output<string>();

  // ── Private state ────────────────────────────────────────────

  isDragging   = signal(false);
  selectedFile = signal<File | null>(null);
  previewUrl   = signal<string | null>(null);
  error        = signal<string | null>(null);
  progress     = signal(0);

  /** Merged config — keeps defaults for any key not supplied by parent */
  cfg = computed<UploadConfig>(() => ({ ...DEFAULT_CONFIG, ...this.config() }));

  hasFile = computed(() => this.selectedFile() !== null);

  private fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');
  private http      = inject(HttpClient);
  private toast     = inject(ToastService);
  private destroy$  = new Subject<void>();

  // ── Lifecycle ────────────────────────────────────────────────

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    // Revoke any object URLs to prevent memory leaks
    const url = this.previewUrl();
    if (url?.startsWith('blob:')) URL.revokeObjectURL(url);
  }

  // ── Public API (called by template) ─────────────────────────

  openPicker(): void {
    this.fileInput()?.nativeElement.click();
  }

  onDragOver(e: DragEvent): void {
    e.preventDefault();
    e.stopPropagation();
    this.isDragging.set(true);
  }

  onDragLeave(): void {
    this.isDragging.set(false);
  }

  onDrop(e: DragEvent): void {
    e.preventDefault();
    e.stopPropagation();
    this.isDragging.set(false);
    const file = e.dataTransfer?.files?.[0];
    if (file) this.processFile(file);
  }

  onFileSelected(e: Event): void {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (file) this.processFile(file);
    // Reset so the same file can be re-selected if removed
    (e.target as HTMLInputElement).value = '';
  }

  removeFile(e: Event): void {
    e.preventDefault();
    e.stopPropagation();

    const url = this.previewUrl();
    if (url?.startsWith('blob:')) URL.revokeObjectURL(url);

    this.selectedFile.set(null);
    this.previewUrl.set(null);
    this.error.set(null);
    this.progress.set(0);
    this.fileRemoved.emit();
  }

  formatSize(bytes: number): string {
    if (bytes < 1_024)           return `${bytes} B`;
    if (bytes < 1_048_576)       return `${(bytes / 1_024).toFixed(1)} KB`;
    return `${(bytes / 1_048_576).toFixed(2)} MB`;
  }

  // ── Private helpers ──────────────────────────────────────────

  private processFile(file: File): void {
    this.error.set(null);
    this.progress.set(0);

    // ── 1. MIME type validation ──────────────────────────────
    const accepted = this.cfg().accept
      .split(',')
      .map(t => t.trim().toLowerCase());

    const fileMime   = file.type.toLowerCase();
    const fileExt    = '.' + file.name.split('.').pop()?.toLowerCase();

    const typeOk = accepted.some(a => {
      if (a.startsWith('.'))   return a === fileExt;         // extension match
      if (a.endsWith('/*'))    return fileMime.startsWith(a.replace('*', '')); // wildcard
      return a === fileMime;                                  // exact MIME
    });

    if (!typeOk) {
      const msg = `Invalid file type "${file.type || fileExt}". Allowed: ${this.cfg().accept}`;
      this.error.set(msg);
      this.toast.error('Invalid file type', msg);
      return;
    }

    // ── 2. Size validation ───────────────────────────────────
    const maxBytes = this.cfg().maxSizeMb * 1_048_576;
    if (file.size > maxBytes) {
      const msg = `File too large (${this.formatSize(file.size)}). Max ${this.cfg().maxSizeMb} MB.`;
      this.error.set(msg);
      this.toast.error('File too large', msg);
      return;
    }

    // ── 3. Store + preview ───────────────────────────────────
    this.selectedFile.set(file);

    if (this.cfg().showPreview && IMAGE_MIMES.has(file.type)) {
      // Use FileReader for reliable cross-browser preview
      const reader = new FileReader();
      reader.onload = evt => this.previewUrl.set(evt.target?.result as string);
      reader.onerror = () => this.previewUrl.set(null);
      reader.readAsDataURL(file);
    }

    // ── 4. Emit to parent ────────────────────────────────────
    this.fileSelected.emit(file);

    // ── 5. Optional auto-upload ──────────────────────────────
    const ep = this.cfg().endpoint;
    if (ep) this.autoUpload(file, ep);
  }

  /**
   * Auto-uploads to the provided endpoint with upload-progress tracking.
   * Used when the component is configured with an `endpoint`.
   * For manual uploads in forms (student photo, logo) the parent handles
   * the HTTP call itself using the `fileSelected` output.
   */
  private autoUpload(file: File, endpoint: string): void {
    const fd  = new FormData();
    fd.append('file', file);

    const req = new HttpRequest('POST', endpoint, fd, { reportProgress: true });

    this.http.request(req).pipe(
      takeUntil(this.destroy$)
    ).subscribe({
      next: event => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          this.progress.set(Math.round(event.loaded / event.total * 100));
        } else if (event.type === HttpEventType.Response) {
          this.progress.set(100);
          const url = (event.body as any)?.url ?? (event.body as any)?.data?.url ?? '';
          this.uploadDone.emit(url);
          this.toast.success('Uploaded successfully');
        }
      },
      error: err => {
        this.progress.set(0);
        const msg = err?.error?.detail ?? 'Upload failed. Please try again.';
        this.error.set(msg);
        this.toast.error('Upload failed', msg);
      }
    });
  }
}