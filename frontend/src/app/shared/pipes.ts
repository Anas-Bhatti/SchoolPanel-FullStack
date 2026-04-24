// src/app/shared/pipes.ts
//
// Central barrel for all standalone pipes.
// Import from this path inside feature components:
//   import { SafeUrlPipe, CurrencyPKRPipe } from '@shared/pipes';
//
// Note: the individual pipe implementations also live in
// src/app/shared/pipes/pipes.ts — both paths export the same classes.

import { Pipe, PipeTransform } from '@angular/core';
import { DomSanitizer, SafeUrl } from '@angular/platform-browser';
import { inject } from '@angular/core';

// ─────────────────────────────────────────────────────────────
// SafeUrlPipe
// Bypasses Angular's DomSanitizer for Azure Blob SAS URLs and
// blob: URLs generated locally (e.g. FileReader output).
// Use in <img [src]="url | safeUrl"> or <a [href]="url | safeUrl">.
// ─────────────────────────────────────────────────────────────
@Pipe({ name: 'safeUrl', standalone: true })
export class SafeUrlPipe implements PipeTransform {
  private sanitizer = inject(DomSanitizer);

  transform(url: string | null | undefined): SafeUrl {
    if (!url) return '';
    return this.sanitizer.bypassSecurityTrustUrl(url);
  }
}

// ─────────────────────────────────────────────────────────────
// SafeResourceUrlPipe
// For embedding PDF reports in <iframe [src]="url | safeResourceUrl">.
// ─────────────────────────────────────────────────────────────
@Pipe({ name: 'safeResourceUrl', standalone: true })
export class SafeResourceUrlPipe implements PipeTransform {
  private sanitizer = inject(DomSanitizer);

  transform(url: string | null | undefined): SafeUrl {
    if (!url) return '';
    return this.sanitizer.bypassSecurityTrustResourceUrl(url);
  }
}

// ─────────────────────────────────────────────────────────────
// CurrencyPKRPipe  (alias: currencyPKR)
// Rs. 12,500.00 — locale-aware comma formatting for Pakistan.
// ─────────────────────────────────────────────────────────────
@Pipe({ name: 'currencyPKR', standalone: true })
export class CurrencyPKRPipe implements PipeTransform {
  transform(
    value:    number | null | undefined,
    symbol:   string  = 'Rs.',
    decimals: number  = 2
  ): string {
    if (value === null || value === undefined || isNaN(value)) {
      return `${symbol} 0.${'0'.repeat(decimals)}`;
    }
    return `${symbol}\u00a0${value.toLocaleString('en-PK', {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals
    })}`;
  }
}

// ─────────────────────────────────────────────────────────────
// TruncateTextPipe  (truncateText)
// Cuts long strings with an ellipsis.
// {{ student.address | truncateText:60 }}
// ─────────────────────────────────────────────────────────────
@Pipe({ name: 'truncateText', standalone: true })
export class TruncateTextPipe implements PipeTransform {
  transform(
    value:  string | null | undefined,
    limit:  number = 80,
    trail:  string = '…'
  ): string {
    if (!value) return '';
    return value.length <= limit ? value : value.slice(0, limit).trimEnd() + trail;
  }
}

// ─────────────────────────────────────────────────────────────
// SpDatePipe  (spDate)
// Null-safe date formatter using Intl.DateTimeFormat (no @angular/common DatePipe dependency).
// {{ student.dateOfBirth | spDate:'short' }}  → "12 Jan 2005"
// {{ payment.createdAt  | spDate }}           → "12 Jan 2005, 14:30"
// ─────────────────────────────────────────────────────────────
@Pipe({ name: 'spDate', standalone: true })
export class SpDatePipe implements PipeTransform {
  transform(
    value:  string | Date | null | undefined,
    format: 'short' | 'medium' | 'long' = 'short'
  ): string {
    if (!value) return '—';
    const date = typeof value === 'string' ? new Date(value) : value;
    if (isNaN(date.getTime())) return '—';

    const opts: Intl.DateTimeFormatOptions = ({
      short:  { day: '2-digit', month: 'short', year: 'numeric' } as Intl.DateTimeFormatOptions,
      medium: { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' } as Intl.DateTimeFormatOptions,
      long:   { weekday: 'short', day: '2-digit', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit' } as Intl.DateTimeFormatOptions
    })[format];

    return date.toLocaleDateString('en-GB', opts);
  }
}

// ─────────────────────────────────────────────────────────────
// TimeAgoPipe  (timeAgo)
// "just now" / "5m ago" / "3h ago" / "2d ago"
// ─────────────────────────────────────────────────────────────
@Pipe({ name: 'timeAgo', standalone: true })
export class TimeAgoPipe implements PipeTransform {
  transform(value: string | Date | null | undefined): string {
    if (!value) return '';
    const date = typeof value === 'string' ? new Date(value) : value;
    const diff = Math.floor((Date.now() - date.getTime()) / 1_000);

    if (diff <    60) return 'just now';
    if (diff <  3_600) return `${Math.floor(diff / 60)}m ago`;
    if (diff < 86_400) return `${Math.floor(diff / 3_600)}h ago`;
    return `${Math.floor(diff / 86_400)}d ago`;
  }
}

// ─────────────────────────────────────────────────────────────
// ShortNumberPipe  (shortNumber)
// 1200 → "1.2K", 1500000 → "1.5M"
// ─────────────────────────────────────────────────────────────
@Pipe({ name: 'shortNumber', standalone: true })
export class ShortNumberPipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    if (value === null || value === undefined) return '0';
    if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(1)}M`;
    if (value >= 1_000)     return `${(value / 1_000).toFixed(1)}K`;
    return value.toString();
  }
}

// ─────────────────────────────────────────────────────────────
// StatusClassPipe  (statusClass)
// Maps status strings to CSS badge class names.
// [class]="student.status | statusClass"
// ─────────────────────────────────────────────────────────────
@Pipe({ name: 'statusClass', standalone: true })
export class StatusClassPipe implements PipeTransform {
  private static readonly MAP: Record<string, string> = {
    // Student status
    Active:       'badge-success',
    Inactive:     'badge-muted',
    Graduated:    'badge-primary',
    Transferred:  'badge-warning',
    // Attendance
    P:            'badge-success',
    A:            'badge-danger',
    L:            'badge-warning',
    H:            'badge-muted',
    // Exam
    Pass:         'badge-success',
    Fail:         'badge-danger',
    // Fee
    Paid:         'badge-success',
    Pending:      'badge-warning',
    Overdue:      'badge-danger',
    Cleared:      'badge-success',
    // Generic
    Enabled:      'badge-success',
    Disabled:     'badge-muted',
    Published:    'badge-success',
    Draft:        'badge-muted',
  };

  transform(value: string | null | undefined): string {
    if (!value) return 'badge-muted';
    return StatusClassPipe.MAP[value] ?? 'badge-muted';
  }
}

// ─────────────────────────────────────────────────────────────
// ─────────────────────────────────────────────────────────────