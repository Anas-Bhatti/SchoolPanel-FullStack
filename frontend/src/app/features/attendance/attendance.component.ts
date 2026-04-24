// src/app/features/attendance/attendance.component.ts
import {
  Component, inject, signal, computed, OnInit
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatTabsModule }        from '@angular/material/tabs';
import { MatFormFieldModule }   from '@angular/material/form-field';
import { MatSelectModule }      from '@angular/material/select';
import { MatInputModule }       from '@angular/material/input';
import { MatButtonModule }      from '@angular/material/button';
import { MatTooltipModule }     from '@angular/material/tooltip';
import { AttendanceService, ToastService } from '@core/services/api.services';
import type { AttendanceEntry } from '@core/models';

type AttStatus = 'P' | 'A' | 'L' | 'H';

interface StudentRow {
  studentId:  string;
  rollNumber: string;
  fullName:   string;
  status:     AttStatus;
  remarks:    string;
}

interface MonthlyStudentRow {
  rollNumber: string;
  fullName:   string;
  days:       { date: string; day: number; status: string }[];
  present:    number;
  absent:     number;
  leave:      number;
  pct:        number;
}

@Component({
  selector:   'sp-attendance',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatTabsModule, MatFormFieldModule, MatSelectModule,
    MatInputModule, MatButtonModule, MatTooltipModule
  ],
  template: `
    <div>
      <div class="page-header">
        <h1>Attendance</h1>
      </div>

      <mat-tab-group color="primary" animationDuration="150ms">

        <!-- ════════════════════════════════════════════════════
             TAB 1 — Mark Attendance
             ════════════════════════════════════════════════════ -->
        <mat-tab label="Mark Attendance">
          <div class="tab-body">

            <!-- Controls bar -->
            <div class="card controls-bar" [formGroup]="markForm">
              <mat-form-field>
                <mat-label>Class ID</mat-label>
                <input matInput type="number" formControlName="classId"
                       placeholder="e.g. 3" aria-label="Class ID">
              </mat-form-field>

              <mat-form-field>
                <mat-label>Date</mat-label>
                <input matInput type="date" formControlName="attendanceDate"
                       aria-label="Attendance date">
              </mat-form-field>

              <button class="btn btn-primary"
                      type="button"
                      (click)="loadClassStudents()"
                      [disabled]="markForm.invalid || loadingClass()">
                @if (loadingClass()) {
                  <span class="spin-sm"></span>
                } @else {
                  <span class="material-icons-round" aria-hidden="true">search</span>
                }
                Load Class
              </button>

              @if (rows().length) {
                <div class="bulk-btns" role="group" aria-label="Bulk status actions">
                  <button class="btn btn-secondary btn-sm" type="button"
                          (click)="markAll('P')"
                          aria-label="Mark all students present">
                    All Present
                  </button>
                  <button class="btn btn-secondary btn-sm" type="button"
                          (click)="markAll('A')"
                          aria-label="Mark all students absent">
                    All Absent
                  </button>
                  <div class="count-pills" aria-live="polite" aria-atomic="true">
                    <span class="badge badge-success" aria-label="{{ presentCount() }} present">
                      {{ presentCount() }} P
                    </span>
                    <span class="badge badge-danger" aria-label="{{ absentCount() }} absent">
                      {{ absentCount() }} A
                    </span>
                  </div>
                </div>
              }
            </div>

            <!-- Student grid -->
            @if (rows().length) {
              <div class="card mark-card">
                <div class="mark-meta">
                  <span>{{ rows().length }} students — Class {{ markForm.value.classId }}</span>
                  <span>{{ markForm.value.attendanceDate }}</span>
                </div>

                <div class="mark-grid" role="list">
                  @for (row of rows(); track row.studentId; let i = $index) {
                    <div
                      class="mark-row"
                      role="listitem"
                      [class.mark-row--p]="row.status === 'P'"
                      [class.mark-row--a]="row.status === 'A'"
                      [class.mark-row--l]="row.status === 'L'">

                      <span class="roll-no" aria-hidden="true">{{ row.rollNumber }}</span>
                      <span class="stud-name">{{ row.fullName }}</span>

                      <div class="status-group"
                           role="group"
                           [attr.aria-label]="'Status for ' + row.fullName">
                        @for (opt of statusOptions; track opt.value) {
                          <button
                            class="status-btn"
                            type="button"
                            [class.status-btn--active]="row.status === opt.value"
                            [style.--s-color]="opt.color"
                            (click)="setStatus(i, opt.value)"
                            [attr.aria-pressed]="row.status === opt.value"
                            [attr.aria-label]="opt.label">
                            {{ opt.value }}
                          </button>
                        }
                      </div>

                    </div>
                  }
                </div>

                <div class="mark-footer">
                  <button class="btn btn-primary" type="button"
                          (click)="submitAttendance()"
                          [disabled]="saving()">
                    @if (saving()) { <span class="spin-sm"></span> }
                    Submit Attendance
                  </button>
                </div>
              </div>

            } @else if (!loadingClass() && classLoaded()) {
              <div class="empty-state card" style="padding:40px">
                <span class="material-icons-round">people_outline</span>
                <h3>No students found</h3>
                <p>Class {{ markForm.value.classId }} has no active students</p>
              </div>
            }

          </div>
        </mat-tab>

        <!-- ════════════════════════════════════════════════════
             TAB 2 — Monthly Report
             ════════════════════════════════════════════════════ -->
        <mat-tab label="Monthly Report">
          <div class="tab-body">

            <div class="card controls-bar" [formGroup]="monthlyForm">
              <mat-form-field>
                <mat-label>Class ID</mat-label>
                <input matInput type="number" formControlName="classId"
                       aria-label="Class ID">
              </mat-form-field>

              <mat-form-field>
                <mat-label>Month</mat-label>
                <mat-select formControlName="month" aria-label="Month">
                  @for (m of months; track m.value) {
                    <mat-option [value]="m.value">{{ m.label }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>

              <mat-form-field>
                <mat-label>Year</mat-label>
                <input matInput type="number" formControlName="year"
                       aria-label="Year">
              </mat-form-field>

              <button class="btn btn-primary" type="button"
                      (click)="loadMonthly()"
                      [disabled]="monthlyForm.invalid || loadingMonthly()">
                @if (loadingMonthly()) {
                  <span class="spin-sm"></span>
                } @else {
                  <span class="material-icons-round" aria-hidden="true">bar_chart</span>
                }
                Load Report
              </button>
            </div>

            @if (loadingMonthly()) {
              <div class="loading-overlay">
                <div class="spinner"></div>
              </div>
            }

            @if (!loadingMonthly() && monthlyStudents().length) {
              <!-- Summary row -->
              <div class="card monthly-summary">
                <div class="ms-items">
                  <div class="ms-item">
                    <span>Working Days</span>
                    <strong>{{ monthlyMeta().totalDays }}</strong>
                  </div>
                  <div class="ms-item">
                    <span>Total Present</span>
                    <strong style="color:var(--color-success)">
                      {{ monthlyMeta().totalPresent }}
                    </strong>
                  </div>
                  <div class="ms-item">
                    <span>Total Absent</span>
                    <strong style="color:var(--color-danger)">
                      {{ monthlyMeta().totalAbsent }}
                    </strong>
                  </div>
                  <div class="ms-item">
                    <span>Students</span>
                    <strong>{{ monthlyStudents().length }}</strong>
                  </div>
                </div>
              </div>

              <!-- Scrollable table -->
              <div class="card table-scroll">
                <table class="sp-table monthly-table" aria-label="Monthly attendance register">
                  <thead>
                    <tr>
                      <th scope="col">Roll</th>
                      <th scope="col">Student</th>
                      @for (d of calendarDays(); track d) {
                        <th scope="col" class="day-hd">{{ d }}</th>
                      }
                      <th scope="col" class="num-hd">P</th>
                      <th scope="col" class="num-hd">A</th>
                      <th scope="col" class="num-hd">L</th>
                      <th scope="col" class="num-hd">%</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of monthlyStudents(); track row.rollNumber) {
                      <tr>
                        <td><code class="roll-code">{{ row.rollNumber }}</code></td>
                        <td class="stud-cell">{{ row.fullName }}</td>
                        @for (d of calendarDays(); track d) {
                          <td class="day-cell"
                              [class.day-p]="getDayStatus(row, d) === 'P'"
                              [class.day-a]="getDayStatus(row, d) === 'A'"
                              [class.day-l]="getDayStatus(row, d) === 'L'"
                              [matTooltip]="getDayStatus(row, d) || '—'">
                            {{ getDayStatus(row, d) || '—' }}
                          </td>
                        }
                        <td class="num-cell" style="color:var(--color-success)">
                          {{ row.present }}
                        </td>
                        <td class="num-cell" style="color:var(--color-danger)">
                          {{ row.absent }}
                        </td>
                        <td class="num-cell" style="color:var(--color-warning)">
                          {{ row.leave }}
                        </td>
                        <td class="num-cell"
                            [style.color]="row.pct >= 75
                              ? 'var(--color-success)'
                              : 'var(--color-danger)'">
                          <strong>{{ row.pct }}%</strong>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }

            @if (!loadingMonthly() && monthlyStudents().length === 0 && monthlyLoaded()) {
              <div class="empty-state card" style="padding:48px">
                <span class="material-icons-round">event_busy</span>
                <h3>No attendance records</h3>
                <p>No data found for Class {{ monthlyForm.value.classId }}
                   — {{ selectedMonthLabel() }}</p>
              </div>
            }

          </div>
        </mat-tab>

      </mat-tab-group>
    </div>
  `,
  styles: [`
    .tab-body {
      padding: 20px 0;
      display: flex; flex-direction: column; gap: 16px;
    }

    .card {
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    /* ── Controls bar ────────────────────────────────────── */
    .controls-bar {
      padding: 14px 16px;
      display: flex; align-items: center; flex-wrap: wrap; gap: 12px;
      mat-form-field { min-width: 130px; }
    }

    .bulk-btns {
      display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
      margin-left: auto;
    }

    .count-pills { display: flex; gap: 6px; }

    /* ── Mark grid ───────────────────────────────────────── */
    .mark-card { overflow: hidden; }

    .mark-meta {
      display: flex; justify-content: space-between;
      padding: 10px 16px; font-size: 12px;
      color: var(--color-text-secondary);
      border-bottom: 1px solid var(--color-border);
      background: var(--color-surface-2);
    }

    .mark-grid { padding: 8px; }

    .mark-row {
      display: grid;
      grid-template-columns: 80px 1fr auto;
      align-items: center; gap: 10px;
      padding: 7px 10px;
      border-radius: var(--radius-sm);
      margin-bottom: 3px;
      border-left: 3px solid transparent;
      transition: background var(--transition-fast);

      &--p { border-left-color: var(--color-success); background: rgba(22,163,74,.05); }
      &--a { border-left-color: var(--color-danger);  background: rgba(220,38,38,.05);  }
      &--l { border-left-color: var(--color-warning); background: rgba(217,119,6,.05);  }
    }

    .roll-no   { font-family: var(--font-mono); font-size: 11px; color: var(--color-text-secondary); }
    .stud-name { font-size: 13px; font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

    .status-group { display: flex; gap: 4px; }

    .status-btn {
      width: 30px; height: 28px; border-radius: 5px;
      font-size: 11px; font-weight: 700;
      border: 1.5px solid var(--color-border);
      background: var(--color-surface);
      cursor: pointer;
      color: var(--color-text-secondary);
      transition: all var(--transition-fast);

      &:hover:not(.status-btn--active) {
        border-color: var(--s-color, var(--color-primary));
        color: var(--s-color, var(--color-primary));
      }

      &--active {
        background: var(--s-color, var(--color-primary));
        border-color: transparent;
        color: #fff;
      }

      &:focus-visible { outline: 2px solid var(--color-primary); }
    }

    .mark-footer {
      padding: 12px 16px;
      border-top: 1px solid var(--color-border);
      display: flex; justify-content: flex-end;
    }

    /* ── Monthly summary ─────────────────────────────────── */
    .monthly-summary { padding: 14px 16px; }

    .ms-items { display: flex; gap: 24px; flex-wrap: wrap; }

    .ms-item {
      display: flex; flex-direction: column; gap: 2px;
      span   { font-size: 11px; text-transform: uppercase; letter-spacing: .04em; color: var(--color-text-secondary); }
      strong { font-size: 20px; font-weight: 700; }
    }

    /* ── Monthly table ───────────────────────────────────── */
    .table-scroll { overflow-x: auto; -webkit-overflow-scrolling: touch; }

    .monthly-table { min-width: 800px; }

    .day-hd, .num-hd {
      width: 28px; text-align: center;
      padding: 8px 4px !important;
      font-size: 10px !important;
    }

    .stud-cell { min-width: 140px; white-space: nowrap; }

    .day-cell {
      text-align: center; font-size: 11px; font-weight: 700;
      padding: 6px 4px !important;
      &.day-p { background: var(--color-success-bg); color: var(--color-success); }
      &.day-a { background: var(--color-danger-bg);  color: var(--color-danger);  }
      &.day-l { background: var(--color-warning-bg); color: var(--color-warning); }
    }

    .num-cell { text-align: center; font-size: 12px; padding: 6px 4px !important; }

    .roll-code {
      font-family: var(--font-mono); font-size: 11px;
      background: var(--color-surface-2); padding: 2px 6px; border-radius: 3px;
    }

    /* ── Spinner ─────────────────────────────────────────── */
    .spin-sm {
      width: 14px; height: 14px;
      border: 2px solid rgba(255,255,255,.35);
      border-top-color: #fff;
      border-radius: 50%;
      animation: sp-spin .5s linear infinite;
      display: inline-block;
    }

    .spinner {
      width: 28px; height: 28px;
      border: 3px solid var(--color-border);
      border-top-color: var(--color-primary);
      border-radius: 50%;
      animation: sp-spin .6s linear infinite;
    }

    @keyframes sp-spin { to { transform: rotate(360deg); } }
  `]
})
export class AttendanceComponent implements OnInit {
  private attSvc = inject(AttendanceService);
  private toast  = inject(ToastService);
  private fb     = inject(FormBuilder);

  // ── Mark tab state ────────────────────────────────────────
  rows         = signal<StudentRow[]>([]);
  loadingClass = signal(false);
  classLoaded  = signal(false);
  saving       = signal(false);

  presentCount = computed(() => this.rows().filter(r => r.status === 'P').length);
  absentCount  = computed(() => this.rows().filter(r => r.status === 'A').length);

  // ── Monthly tab state ─────────────────────────────────────
  monthlyRaw      = signal<any>(null);
  loadingMonthly  = signal(false);
  monthlyLoaded   = signal(false);

  /** Unique sorted day-numbers present in the monthly records */
  calendarDays = computed<number[]>(() => {
    const students = this.monthlyStudents();
    if (!students.length) return [];
    const allDays = new Set<number>();
    for (const s of students) {
      for (const d of s.days) allDays.add(d.day);
    }
    return Array.from(allDays).sort((a, b) => a - b);
  });

  monthlyStudents = computed<MonthlyStudentRow[]>(() => {
    const raw = this.monthlyRaw();
    const records: any[] = raw?.records ?? [];
    if (!records.length) return [];

    const map = new Map<string, MonthlyStudentRow>();

    for (const rec of records) {
      const key = rec.rollNumber as string;
      if (!map.has(key)) {
        map.set(key, {
          rollNumber: rec.rollNumber,
          fullName:   rec.fullName,
          days:       [],
          present:    0, absent: 0, leave: 0, pct: 0
        });
      }
      const entry = map.get(key)!;
      const dateStr: string = rec.attendanceDate ?? '';
      const day = dateStr ? new Date(dateStr).getDate() : 0;
      entry.days.push({ date: dateStr, day, status: rec.status ?? '' });
    }

    return Array.from(map.values()).map(s => {
      const present = s.days.filter(d => d.status === 'P').length;
      const absent  = s.days.filter(d => d.status === 'A').length;
      const leave   = s.days.filter(d => d.status === 'L').length;
      const total   = s.days.length;
      return {
        ...s,
        present,
        absent,
        leave,
        pct: total ? Math.round(present / total * 100) : 0
      };
    }).sort((a, b) => a.rollNumber.localeCompare(b.rollNumber));
  });

  monthlyMeta = computed(() => {
    const raw = this.monthlyRaw();
    return {
      totalDays:    raw?.summary?.totalDays    ?? 0,
      totalPresent: raw?.summary?.totalPresent ?? 0,
      totalAbsent:  raw?.summary?.totalAbsent  ?? 0
    };
  });

  selectedMonthLabel = computed(() => {
    const v = this.monthlyForm.value;
    if (!v.month || !v.year) return '';
    return new Date(v.year, (v.month as number) - 1, 1)
      .toLocaleString('default', { month: 'long', year: 'numeric' });
  });

  // ── Form groups ───────────────────────────────────────────

  markForm = this.fb.group({
    classId:         [null as number | null, [Validators.required, Validators.min(1)]],
    attendanceDate:  [
      new Date().toISOString().split('T')[0],
      Validators.required
    ]
  });

  monthlyForm = this.fb.group({
    classId: [null as number | null, [Validators.required, Validators.min(1)]],
    month:   [new Date().getMonth() + 1, Validators.required],
    year:    [new Date().getFullYear(), Validators.required]
  });

  readonly statusOptions = [
    { value: 'P' as AttStatus, label: 'Present', color: 'var(--color-success)' },
    { value: 'A' as AttStatus, label: 'Absent',  color: 'var(--color-danger)'  },
    { value: 'L' as AttStatus, label: 'Leave',   color: 'var(--color-warning)' },
    { value: 'H' as AttStatus, label: 'Holiday', color: 'var(--color-text-muted)' }
  ];

  readonly months = Array.from({ length: 12 }, (_, i) => ({
    value: i + 1,
    label: new Date(2024, i, 1).toLocaleString('default', { month: 'long' })
  }));

  ngOnInit(): void {}

  // ── Mark tab methods ──────────────────────────────────────

  loadClassStudents(): void {
    const { classId, attendanceDate } = this.markForm.value;
    if (!classId || !attendanceDate) return;

    // Validate not a future date
    const today = new Date().toISOString().split('T')[0];
    if (attendanceDate > today) {
      this.toast.error('Invalid date', 'Cannot mark attendance for a future date.');
      return;
    }

    this.loadingClass.set(true);
    const date   = new Date(attendanceDate);
    const month  = date.getMonth() + 1;
    const year   = date.getFullYear();

    this.attSvc.getMonthly({ classId, month, year }).subscribe({
      next: (data: any) => {
        const records: any[] = data?.records ?? [];
        // Deduplicate by studentId to build unique student list
        const seen = new Set<string>();
        const rows: StudentRow[] = [];
        for (const rec of records) {
          if (!seen.has(rec.studentId)) {
            seen.add(rec.studentId);
            rows.push({
              studentId:  rec.studentId,
              rollNumber: rec.rollNumber,
              fullName:   rec.fullName,
              status:     'P',   // default all to Present
              remarks:    ''
            });
          }
        }
        this.rows.set(rows.sort((a, b) => a.rollNumber.localeCompare(b.rollNumber)));
        this.classLoaded.set(true);
        this.loadingClass.set(false);
      },
      error: () => {
        this.loadingClass.set(false);
        this.toast.error('Failed to load class', 'Check the class ID and try again.');
      }
    });
  }

  setStatus(index: number, status: AttStatus): void {
    this.rows.update(list => {
      const next = [...list];
      next[index] = { ...next[index], status };
      return next;
    });
  }

  markAll(status: AttStatus): void {
    this.rows.update(list => list.map(r => ({ ...r, status })));
  }

  submitAttendance(): void {
    const { classId, attendanceDate } = this.markForm.value;
    if (!classId || !attendanceDate || !this.rows().length) return;

    this.saving.set(true);
    const entries: AttendanceEntry[] = this.rows().map(r => ({
      studentId: r.studentId,
      status:    r.status,
      remarks:   r.remarks || undefined
    }));

    this.attSvc.markAttendance({ classId, attendanceDate, entries }).subscribe({
      next: (res: any) => {
        this.saving.set(false);
        const count = res?.recordsProcessed ?? entries.length;
        this.toast.success('Attendance saved', `${count} records marked successfully.`);
      },
      error: (err) => {
        this.saving.set(false);
        this.toast.error('Failed to save attendance', err?.error?.detail);
      }
    });
  }

  // ── Monthly tab methods ───────────────────────────────────

  loadMonthly(): void {
    const { classId, month, year } = this.monthlyForm.value;
    if (!classId) return;

    this.loadingMonthly.set(true);
    this.monthlyLoaded.set(false);
    this.monthlyRaw.set(null);

    this.attSvc.getMonthly({ classId, month: month!, year: year! }).subscribe({
      next: (data) => {
        this.monthlyRaw.set(data);
        this.loadingMonthly.set(false);
        this.monthlyLoaded.set(true);
      },
      error: () => {
        this.loadingMonthly.set(false);
        this.monthlyLoaded.set(true);
        this.toast.error('Failed to load monthly report');
      }
    });
  }

  /** Lookup status for a student on a specific day number */
  getDayStatus(row: MonthlyStudentRow, dayNum: number): string {
    return row.days.find(d => d.day === dayNum)?.status ?? '';
  }
}