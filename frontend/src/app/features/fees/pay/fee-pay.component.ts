// ============================================================
// features/fees/pay/fee-pay.component.ts
// ============================================================
import {
  Component, inject, signal
} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import {
  FormBuilder, Validators, ReactiveFormsModule
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import {
  FeeService, ReportService, ToastService, downloadBlob
} from '@core/services/api.services';

@Component({
  selector: 'sp-fee-pay',
  standalone: true,
  imports: [
    CommonModule, DecimalPipe, ReactiveFormsModule, RouterLink,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule
  ],
  template: `
    <div class="pay-page">
      <div class="page-header">
        <div style="display:flex;align-items:center;gap:10px">
          <a routerLink="/fees" class="btn btn-ghost btn-icon">
            <span class="material-icons-round">arrow_back</span>
          </a>
          <h1>Record Payment</h1>
        </div>
      </div>

      <div class="pay-layout">

        <!-- Form -->
        <form [formGroup]="form" (ngSubmit)="onSubmit()" class="card form-card">

          <div class="form-section-title">Payment Details</div>

          <div class="grid-2">
            <mat-form-field class="full">
              <mat-label>Student ID *</mat-label>
              <input matInput formControlName="studentId" placeholder="UUID">
            </mat-form-field>

            <mat-form-field>
              <mat-label>Fee Type ID *</mat-label>
              <input matInput type="number" formControlName="feeTypeId">
            </mat-form-field>

            <mat-form-field>
              <mat-label>Academic Year ID *</mat-label>
              <input matInput type="number" formControlName="academicYearId">
            </mat-form-field>

            <mat-form-field>
              <mat-label>Amount Due *</mat-label>
              <span matPrefix>Rs.&nbsp;</span>
              <input matInput type="number" formControlName="amountDue" min="0">
            </mat-form-field>

            <mat-form-field>
              <mat-label>Amount Paid *</mat-label>
              <span matPrefix>Rs.&nbsp;</span>
              <input matInput type="number" formControlName="amountPaid" min="0">
            </mat-form-field>

            <mat-form-field>
              <mat-label>Discount</mat-label>
              <span matPrefix>Rs.&nbsp;</span>
              <input matInput type="number" formControlName="discount" min="0">
            </mat-form-field>

            <mat-form-field>
              <mat-label>Fine / Penalty</mat-label>
              <span matPrefix>Rs.&nbsp;</span>
              <input matInput type="number" formControlName="fine" min="0">
            </mat-form-field>

            <mat-form-field>
              <mat-label>Payment Method *</mat-label>
              <mat-select formControlName="paymentMethod">
                <mat-option value="Cash">Cash</mat-option>
                <mat-option value="BankTransfer">Bank Transfer</mat-option>
                <mat-option value="Cheque">Cheque</mat-option>
                <mat-option value="Online">Online</mat-option>
              </mat-select>
            </mat-form-field>

            <mat-form-field>
              <mat-label>Reference Number</mat-label>
              <input matInput formControlName="referenceNumber">
            </mat-form-field>

            <mat-form-field class="full">
              <mat-label>Remarks</mat-label>
              <textarea matInput formControlName="remarks" rows="2"></textarea>
            </mat-form-field>
          </div>

          <!-- Balance preview -->
          <div class="balance-preview">
            <div class="bal-row">
              <span>Amount Due</span>
              <span>Rs. {{ form.value.amountDue || 0 | number:'1.0-2' }}</span>
            </div>
            <div class="bal-row">
              <span>Discount</span>
              <span style="color:var(--color-success)">
                - Rs. {{ form.value.discount || 0 | number:'1.0-2' }}
              </span>
            </div>
            <div class="bal-row">
              <span>Fine</span>
              <span style="color:var(--color-danger)">
                + Rs. {{ form.value.fine || 0 | number:'1.0-2' }}
              </span>
            </div>
            <div class="bal-row bal-row--total">
              <span>Balance After Payment</span>
              <span [style.color]="balance() > 0 ? 'var(--color-danger)' : 'var(--color-success)'">
                Rs. {{ balance() | number:'1.0-2' }}
              </span>
            </div>
          </div>

          <div class="form-actions">
            <a routerLink="/fees" class="btn btn-secondary">Cancel</a>
            <button type="submit" class="btn btn-primary" [disabled]="saving() || form.invalid">
              @if (saving()) { <div class="spin-sm"></div> }
              Record Payment
            </button>
          </div>

        </form>

        <!-- Receipt preview -->
        @if (receiptPaymentId()) {
          <div class="card receipt-card">
            <div class="receipt-success">
              <span class="material-icons-round">check_circle</span>
              <h3>Payment Recorded!</h3>
              <p>Receipt: <strong>{{ receiptNumber() }}</strong></p>
            </div>

            <div class="receipt-actions">
              <button class="btn btn-primary" (click)="downloadReceipt()" [disabled]="dlLoading()">
                <span class="material-icons-round">download</span>
                {{ dlLoading() ? 'Downloading...' : 'Download PDF' }}
              </button>

              <button class="btn btn-secondary" (click)="viewReceipt()" [disabled]="dlLoading()">
                <span class="material-icons-round">visibility</span>
                View Receipt
              </button>

              <button class="btn btn-ghost" (click)="resetForm()">
                <span class="material-icons-round">refresh</span>
                New Payment
              </button>
            </div>
          </div>
        }

      </div>
    </div>
  `,
  styles: [`
    .pay-layout {
      display: grid;
      grid-template-columns: 1fr 320px;
      gap: 20px;
      align-items: flex-start;
      @media (max-width: 900px) { grid-template-columns: 1fr; }
    }

    .card { background: var(--color-surface); border: 1px solid var(--color-border);
            border-radius: var(--radius-md); padding: 20px; }

    .grid-2 {
      display: grid; grid-template-columns: repeat(2, 1fr); gap: 0 16px;
      mat-form-field { width: 100%; }
      .full { grid-column: 1 / -1; }
      @media (max-width: 600px) { grid-template-columns: 1fr; }
    }

    .balance-preview {
      background: var(--color-surface-2); border-radius: var(--radius-sm);
      padding: 12px 16px; margin: 16px 0;
    }

    .bal-row {
      display: flex; justify-content: space-between;
      padding: 5px 0; font-size: 13px;
      border-bottom: 1px solid var(--color-border);
      &:last-child { border-bottom: none; }
      &--total { font-weight: 700; font-size: 14px; padding-top: 8px; }
    }

    .form-actions {
      display: flex; justify-content: flex-end; gap: 10px;
      padding-top: 16px; border-top: 1px solid var(--color-border);
    }

    .receipt-card { display: flex; flex-direction: column; gap: 16px; }

    .receipt-success {
      display: flex; flex-direction: column; align-items: center;
      text-align: center; gap: 8px; padding: 20px;
      background: var(--color-success-bg); border-radius: var(--radius-sm);

      .material-icons-round { font-size: 40px; color: var(--color-success); }
      h3 { font-size: 16px; margin: 0; color: var(--color-success); }
      p  { font-size: 13px; margin: 0; }
    }

    .receipt-actions { display: flex; flex-direction: column; gap: 8px; }

    .spin-sm {
      width: 14px; height: 14px;
      border: 2px solid rgba(255,255,255,.3);
      border-top-color: #fff;
      border-radius: 50%;
      animation: spin .5s linear infinite;
    }

    @keyframes spin { to { transform: rotate(360deg); } }
  `]
})
export class FeePayComponent {
  private feeSvc    = inject(FeeService);
  private reportSvc = inject(ReportService);
  private toast     = inject(ToastService);
  private fb        = inject(FormBuilder);

  saving          = signal(false);
  dlLoading       = signal(false);
  receiptPaymentId = signal<number | null>(null);
  receiptNumber   = signal<string | null>(null);

  form = this.fb.group({
    studentId:      ['', Validators.required],
    feeTypeId:      [null as number | null, Validators.required],
    academicYearId: [null as number | null, Validators.required],
    amountDue:      [null as number | null, [Validators.required, Validators.min(0.01)]],
    amountPaid:     [null as number | null, [Validators.required, Validators.min(0.01)]],
    discount:       [0],
    fine:           [0],
    paymentMethod:  ['Cash', Validators.required],
    referenceNumber: [''],
    remarks:        ['']
  });

  balance() {
    const v = this.form.value;
    return (v.amountDue || 0) - (v.amountPaid || 0) + (v.fine || 0) - (v.discount || 0);
  }

  onSubmit(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.saving.set(true);
    const v = this.form.value;

    this.feeSvc.recordPayment({
      studentId:      v.studentId!,
      feeTypeId:      v.feeTypeId!,
      academicYearId: v.academicYearId!,
      amountDue:      v.amountDue!,
      amountPaid:     v.amountPaid!,
      discount:       v.discount || 0,
      fine:           v.fine || 0,
      paymentMethod:  v.paymentMethod!,
      referenceNumber: v.referenceNumber || undefined,
      remarks:        v.remarks || undefined
    }).subscribe({
      next: (res: any) => {
        this.saving.set(false);
        this.receiptPaymentId.set(res.paymentId);
        this.receiptNumber.set(res.receiptNumber);
        this.toast.success('Payment recorded', `Receipt: ${res.receiptNumber}`);
      },
      error: (err) => {
        this.saving.set(false);
        this.toast.error('Payment failed', err?.error?.detail);
      }
    });
  }

  downloadReceipt(): void {
    const id = this.receiptPaymentId();
    if (!id) return;
    this.dlLoading.set(true);
    this.reportSvc.getFeeReceipt(id, true).subscribe({
      next: blob => {
        downloadBlob(blob, `Receipt_${this.receiptNumber()}.pdf`);
        this.dlLoading.set(false);
      },
      error: () => { this.dlLoading.set(false); this.toast.error('Download failed'); }
    });
  }

  viewReceipt(): void {
    const id = this.receiptPaymentId();
    if (!id) return;
    this.dlLoading.set(true);
    this.reportSvc.getFeeReceipt(id, false).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        window.open(url, '_blank');
        this.dlLoading.set(false);
      },
      error: () => { this.dlLoading.set(false); this.toast.error('Open failed'); }
    });
  }

  resetForm(): void {
    this.form.reset({ discount: 0, fine: 0, paymentMethod: 'Cash' });
    this.receiptPaymentId.set(null);
    this.receiptNumber.set(null);
  }
}