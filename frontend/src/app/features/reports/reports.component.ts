// ============================================================
// features/reports/reports.component.ts
// ============================================================
import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder, ReactiveFormsModule, Validators
} from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import {
  ReportService, ToastService, downloadBlob
} from '@core/services/api.services';

interface ReportCard {
  id:          string;
  title:       string;
  description: string;
  icon:        string;
  color:       string;
}

@Component({
  selector: 'sp-reports',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatFormFieldModule, MatInputModule, MatSelectModule,
    MatButtonModule, MatProgressBarModule
  ],
  template: `
    <div>
      <div class="page-header">
        <h1>Reports</h1>
        <p style="color:var(--color-text-secondary);font-size:13px;margin:0">
          Generate and download school reports
        </p>
      </div>

      <div class="report-grid">

        <!-- Report Card: Student Report Card -->
        <div class="card report-card" [class.report-card--loading]="loading()['report-card']">
          <div class="rc-icon rc-icon--blue">
            <span class="material-icons-round">school</span>
          </div>
          <div class="rc-body">
            <h3>Student Report Card</h3>
            <p>Generate PDF report card with subject results, attendance summary and remarks</p>
          </div>
          <div class="rc-form" [formGroup]="reportCardForm">
            <mat-form-field>
              <mat-label>Student ID</mat-label>
              <input matInput formControlName="studentId" placeholder="UUID">
            </mat-form-field>
            <mat-form-field>
              <mat-label>Academic Year ID</mat-label>
              <input matInput type="number" formControlName="academicYearId">
            </mat-form-field>
          </div>
          @if (loading()['report-card']) {
            <mat-progress-bar mode="indeterminate"></mat-progress-bar>
          }
          <div class="rc-actions">
            <button class="btn btn-primary btn-sm"
                    (click)="downloadReportCard()"
                    [disabled]="reportCardForm.invalid || loading()['report-card']">
              <span class="material-icons-round">download</span>
              Download PDF
            </button>
            <button class="btn btn-ghost btn-sm"
                    (click)="viewReportCard()"
                    [disabled]="reportCardForm.invalid || loading()['report-card']">
              <span class="material-icons-round">visibility</span> View
            </button>
          </div>
        </div>

        <!-- Fee Receipt -->
        <div class="card report-card" [class.report-card--loading]="loading()['receipt']">
          <div class="rc-icon rc-icon--green">
            <span class="material-icons-round">receipt_long</span>
          </div>
          <div class="rc-body">
            <h3>Fee Receipt</h3>
            <p>Regenerate a fee payment receipt PDF with school branding</p>
          </div>
          <div class="rc-form" [formGroup]="receiptForm">
            <mat-form-field>
              <mat-label>Payment ID</mat-label>
              <input matInput type="number" formControlName="paymentId">
            </mat-form-field>
          </div>
          @if (loading()['receipt']) {
            <mat-progress-bar mode="indeterminate"></mat-progress-bar>
          }
          <div class="rc-actions">
            <button class="btn btn-primary btn-sm"
                    (click)="downloadReceipt()"
                    [disabled]="receiptForm.invalid || loading()['receipt']">
              <span class="material-icons-round">download</span> Download
            </button>
            <button class="btn btn-ghost btn-sm"
                    (click)="viewReceipt()"
                    [disabled]="receiptForm.invalid || loading()['receipt']">
              <span class="material-icons-round">visibility</span> View
            </button>
          </div>
        </div>

        <!-- Attendance Excel -->
        <div class="card report-card" [class.report-card--loading]="loading()['attendance']">
          <div class="rc-icon rc-icon--orange">
            <span class="material-icons-round">table_chart</span>
          </div>
          <div class="rc-body">
            <h3>Attendance Register</h3>
            <p>Monthly class attendance sheet with colour-coded cells, totals and print layout</p>
          </div>
          <div class="rc-form" [formGroup]="attendanceForm">
            <mat-form-field>
              <mat-label>Class ID</mat-label>
              <input matInput type="number" formControlName="classId">
            </mat-form-field>
            <mat-form-field>
              <mat-label>Month</mat-label>
              <mat-select formControlName="month">
                @for (m of months; track m.value) {
                  <mat-option [value]="m.value">{{ m.label }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>Year</mat-label>
              <input matInput type="number" formControlName="year">
            </mat-form-field>
          </div>
          @if (loading()['attendance']) {
            <mat-progress-bar mode="indeterminate"></mat-progress-bar>
          }
          <div class="rc-actions">
            <button class="btn btn-primary btn-sm"
                    (click)="downloadAttendance()"
                    [disabled]="attendanceForm.invalid || loading()['attendance']">
              <span class="material-icons-round">table_view</span> Download Excel
            </button>
          </div>
        </div>

        <!-- Exam Results -->
        <div class="card report-card" [class.report-card--loading]="loading()['exam']">
          <div class="rc-icon rc-icon--purple">
            <span class="material-icons-round">grading</span>
          </div>
          <div class="rc-body">
            <h3>Exam Result Sheet</h3>
            <p>Class exam result PDF with per-student marks, grades and class statistics</p>
          </div>
          <div class="rc-form" [formGroup]="examForm">
            <mat-form-field>
              <mat-label>Exam ID</mat-label>
              <input matInput type="number" formControlName="examId">
            </mat-form-field>
          </div>
          @if (loading()['exam']) {
            <mat-progress-bar mode="indeterminate"></mat-progress-bar>
          }
          <div class="rc-actions">
            <button class="btn btn-primary btn-sm"
                    (click)="downloadExamResults()"
                    [disabled]="examForm.invalid || loading()['exam']">
              <span class="material-icons-round">download</span> Download PDF
            </button>
            <button class="btn btn-ghost btn-sm"
                    (click)="viewExamResults()"
                    [disabled]="examForm.invalid || loading()['exam']">
              <span class="material-icons-round">visibility</span> View
            </button>
          </div>
        </div>

      </div>
    </div>
  `,
  styles: [`
    .report-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 16px;
      @media (max-width: 900px) { grid-template-columns: 1fr; }
    }

    .report-card {
      display: flex; flex-direction: column; gap: 14px;
      padding: 20px; background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      transition: box-shadow .15s;

      &:hover { box-shadow: var(--shadow-md); }
      &--loading { opacity: .7; pointer-events: none; }
    }

    .rc-icon {
      width: 44px; height: 44px; border-radius: 10px;
      display: flex; align-items: center; justify-content: center;
      .material-icons-round { font-size: 22px; }

      &--blue   { background: var(--color-primary-50);  .material-icons-round { color: var(--color-primary); } }
      &--green  { background: var(--color-success-bg);  .material-icons-round { color: var(--color-success); } }
      &--orange { background: var(--color-warning-bg);  .material-icons-round { color: var(--color-warning); } }
      &--purple { background: #F5F3FF;                  .material-icons-round { color: #7C3AED; } }
    }

    .rc-body {
      h3 { font-size: 15px; font-weight: 600; margin: 0 0 4px; }
      p  { font-size: 12px; color: var(--color-text-secondary); margin: 0; line-height: 1.5; }
    }

    .rc-form {
      display: flex; flex-wrap: wrap; gap: 10px;
      mat-form-field { min-width: 130px; flex: 1; }
    }

    .rc-actions { display: flex; gap: 8px; flex-wrap: wrap; }
  `]
})
export class ReportsComponent {
  private reportSvc = inject(ReportService);
  private toast     = inject(ToastService);
  private fb        = inject(FormBuilder);

  loading = signal<Record<string, boolean>>({});

  reportCardForm = this.fb.group({
    studentId:      ['', Validators.required],
    academicYearId: [null as number | null, Validators.required]
  });

  receiptForm = this.fb.group({
    paymentId: [null as number | null, Validators.required]
  });

  attendanceForm = this.fb.group({
    classId: [null as number | null, Validators.required],
    month:   [new Date().getMonth() + 1, Validators.required],
    year:    [new Date().getFullYear(), Validators.required]
  });

  examForm = this.fb.group({
    examId: [null as number | null, Validators.required]
  });

  readonly months = Array.from({ length: 12 }, (_, i) => ({
    value: i + 1,
    label: new Date(2024, i, 1).toLocaleString('default', { month: 'long' })
  }));

  // ── Report card ──────────────────────────────────────────────

  downloadReportCard(): void {
    const { studentId, academicYearId } = this.reportCardForm.value;
    this.startLoad('report-card');
    this.reportSvc.getReportCard(studentId!, academicYearId!, true).subscribe({
      next: blob => { downloadBlob(blob, `ReportCard.pdf`); this.endLoad('report-card'); this.toast.success('Downloaded'); },
      error: () => { this.endLoad('report-card'); this.toast.error('Download failed'); }
    });
  }

  viewReportCard(): void {
    const { studentId, academicYearId } = this.reportCardForm.value;
    this.startLoad('report-card');
    this.reportSvc.getReportCard(studentId!, academicYearId!, false).subscribe({
      next: blob => { window.open(URL.createObjectURL(blob), '_blank'); this.endLoad('report-card'); },
      error: () => { this.endLoad('report-card'); this.toast.error('Failed'); }
    });
  }

  // ── Receipt ──────────────────────────────────────────────────

  downloadReceipt(): void {
    const { paymentId } = this.receiptForm.value;
    this.startLoad('receipt');
    this.reportSvc.getFeeReceipt(paymentId!, true).subscribe({
      next: blob => { downloadBlob(blob, `Receipt_${paymentId}.pdf`); this.endLoad('receipt'); this.toast.success('Downloaded'); },
      error: () => { this.endLoad('receipt'); this.toast.error('Download failed'); }
    });
  }

  viewReceipt(): void {
    const { paymentId } = this.receiptForm.value;
    this.startLoad('receipt');
    this.reportSvc.getFeeReceipt(paymentId!, false).subscribe({
      next: blob => { window.open(URL.createObjectURL(blob), '_blank'); this.endLoad('receipt'); },
      error: () => { this.endLoad('receipt'); this.toast.error('Failed'); }
    });
  }

  // ── Attendance Excel ─────────────────────────────────────────

  downloadAttendance(): void {
    const { classId, month, year } = this.attendanceForm.value;
    this.startLoad('attendance');
    this.reportSvc.getAttendanceExcel(classId!, month!, year!).subscribe({
      next: blob => {
        const monthName = new Date(year!, month! - 1, 1).toLocaleString('default', { month: 'short' });
        downloadBlob(blob, `Attendance_Class${classId}_${monthName}${year}.xlsx`);
        this.endLoad('attendance');
        this.toast.success('Excel downloaded');
      },
      error: () => { this.endLoad('attendance'); this.toast.error('Download failed'); }
    });
  }

  // ── Exam Results ─────────────────────────────────────────────

  downloadExamResults(): void {
    const { examId } = this.examForm.value;
    this.startLoad('exam');
    this.reportSvc.getExamResults(examId!, true).subscribe({
      next: blob => { downloadBlob(blob, `ExamResults_${examId}.pdf`); this.endLoad('exam'); this.toast.success('Downloaded'); },
      error: () => { this.endLoad('exam'); this.toast.error('Download failed'); }
    });
  }

  viewExamResults(): void {
    const { examId } = this.examForm.value;
    this.startLoad('exam');
    this.reportSvc.getExamResults(examId!, false).subscribe({
      next: blob => { window.open(URL.createObjectURL(blob), '_blank'); this.endLoad('exam'); },
      error: () => { this.endLoad('exam'); this.toast.error('Failed'); }
    });
  }

  private startLoad(key: string): void {
    this.loading.update(m => ({ ...m, [key]: true }));
  }

  private endLoad(key: string): void {
    this.loading.update(m => ({ ...m, [key]: false }));
  }
}