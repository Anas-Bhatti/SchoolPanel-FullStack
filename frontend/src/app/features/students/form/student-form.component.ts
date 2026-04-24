// src/app/features/students/form/student-form.component.ts
import {
  Component, inject, signal, OnInit
} from '@angular/core';
import { CommonModule }         from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { RouterLink, ActivatedRoute, Router } from '@angular/router';
import { MatFormFieldModule }   from '@angular/material/form-field';
import { MatInputModule }       from '@angular/material/input';
import { MatSelectModule }      from '@angular/material/select';
import { MatButtonModule }      from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { StudentService, ToastService } from '@core/services/api.services';
import type { CreateStudentRequest, UpdateStudentRequest } from '@core/models';

@Component({
  selector:   'sp-student-form',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, RouterLink,
    MatFormFieldModule, MatInputModule, MatSelectModule,
    MatButtonModule, MatProgressBarModule
  ],
  template: `
    <div class="form-page">
      <div class="page-header">
        <div class="ph-left">
          <a routerLink="/students" class="btn btn-ghost btn-icon"
             aria-label="Back to students list">
            <span class="material-icons-round" aria-hidden="true">arrow_back</span>
          </a>
          <h1>{{ isEdit() ? 'Edit Student' : 'New Student' }}</h1>
        </div>
      </div>

      <form [formGroup]="form" (ngSubmit)="onSubmit()" novalidate>
        <div class="form-layout">

          <!-- Photo card -->
          <div class="card photo-card">
            <div class="photo-upload">
              <div
                class="photo-preview"
                role="button"
                tabindex="0"
                aria-label="Click to upload photo"
                (click)="fileInput.click()"
                (keydown.enter)="fileInput.click()"
                (keydown.space)="fileInput.click()">
                @if (photoPreview()) {
                  <img [src]="photoPreview()" alt="Student photo preview">
                } @else {
                  <div class="photo-placeholder">
                    <span class="material-icons-round" aria-hidden="true">
                      add_photo_alternate
                    </span>
                    <span>Upload Photo</span>
                  </div>
                }
              </div>

              <input
                #fileInput
                type="file"
                accept="image/jpeg,image/png,image/webp"
                aria-label="Choose photo file"
                hidden
                (change)="onPhotoSelected($event)">

              @if (uploadProgress() > 0 && uploadProgress() < 100) {
                <mat-progress-bar
                  mode="determinate"
                  [value]="uploadProgress()"
                  aria-label="Upload progress"
                  style="width:160px">
                </mat-progress-bar>
                <p class="upload-pct" aria-live="polite">{{ uploadProgress() }}%</p>
              }

              @if (photoPreview()) {
                <button
                  class="btn btn-ghost btn-sm"
                  type="button"
                  (click)="clearPhoto()"
                  aria-label="Remove photo">
                  <span class="material-icons-round" aria-hidden="true">clear</span>
                  Remove
                </button>
              }
            </div>
          </div>

          <!-- Main form card -->
          <div class="card form-card">

            <!-- Account info -->
            <fieldset class="form-section" aria-labelledby="account-legend">
              <div class="form-section-title" id="account-legend">Account Information</div>
              <div class="grid-2">
                <mat-form-field class="col-full">
                  <mat-label>Full Name *</mat-label>
                  <input matInput formControlName="fullName" autocomplete="name">
                  @if (f['fullName'].errors?.['required'] && f['fullName'].touched) {
                    <mat-error>Full name is required</mat-error>
                  }
                </mat-form-field>

                <mat-form-field>
                  <mat-label>Email Address *</mat-label>
                  <input matInput type="email" formControlName="email"
                         autocomplete="email"
                         [readonly]="isEdit()">
                  @if (f['email'].errors?.['email']) {
                    <mat-error>Enter a valid email address</mat-error>
                  }
                  @if (f['email'].errors?.['required'] && f['email'].touched) {
                    <mat-error>Email is required</mat-error>
                  }
                </mat-form-field>

                @if (!isEdit()) {
                  <mat-form-field>
                    <mat-label>Password *</mat-label>
                    <input matInput type="password" formControlName="password"
                           autocomplete="new-password">
                    <mat-hint>Minimum 8 characters</mat-hint>
                    @if (f['password'].errors?.['minlength']) {
                      <mat-error>Password must be at least 8 characters</mat-error>
                    }
                  </mat-form-field>
                }

                <mat-form-field>
                  <mat-label>Phone Number</mat-label>
                  <input matInput type="tel" formControlName="phoneNumber"
                         autocomplete="tel">
                </mat-form-field>
              </div>
            </fieldset>

            <!-- Academic info -->
            <fieldset class="form-section" aria-labelledby="academic-legend">
              <div class="form-section-title" id="academic-legend">Academic Details</div>
              <div class="grid-2">
                <mat-form-field>
                  <mat-label>Roll Number *</mat-label>
                  <input matInput formControlName="rollNumber">
                  @if (f['rollNumber'].errors?.['required'] && f['rollNumber'].touched) {
                    <mat-error>Roll number is required</mat-error>
                  }
                </mat-form-field>

                <mat-form-field>
                  <mat-label>Class ID *</mat-label>
                  <input matInput type="number" formControlName="classId" min="1">
                </mat-form-field>

                <mat-form-field>
                  <mat-label>Academic Year ID *</mat-label>
                  <input matInput type="number" formControlName="academicYearId" min="1">
                </mat-form-field>

                <mat-form-field>
                  <mat-label>Status</mat-label>
                  <mat-select formControlName="status">
                    <mat-option value="Active">Active</mat-option>
                    <mat-option value="Inactive">Inactive</mat-option>
                    <mat-option value="Graduated">Graduated</mat-option>
                    <mat-option value="Transferred">Transferred</mat-option>
                  </mat-select>
                </mat-form-field>
              </div>
            </fieldset>

            <!-- Personal info -->
            <fieldset class="form-section" aria-labelledby="personal-legend">
              <div class="form-section-title" id="personal-legend">Personal Information</div>
              <div class="grid-2">
                <mat-form-field>
                  <mat-label>Gender</mat-label>
                  <mat-select formControlName="gender">
                    <mat-option value="Male">Male</mat-option>
                    <mat-option value="Female">Female</mat-option>
                    <mat-option value="Other">Other</mat-option>
                  </mat-select>
                </mat-form-field>

                <mat-form-field>
                  <mat-label>Date of Birth</mat-label>
                  <input matInput type="date" formControlName="dateOfBirth">
                </mat-form-field>

                <mat-form-field>
                  <mat-label>Blood Group</mat-label>
                  <mat-select formControlName="bloodGroup">
                    <mat-option value="">Unknown</mat-option>
                    @for (bg of bloodGroups; track bg) {
                      <mat-option [value]="bg">{{ bg }}</mat-option>
                    }
                  </mat-select>
                </mat-form-field>

                <mat-form-field>
                  <mat-label>Emergency Contact</mat-label>
                  <input matInput type="tel" formControlName="emergencyContact">
                </mat-form-field>

                <mat-form-field class="col-full">
                  <mat-label>Address</mat-label>
                  <textarea matInput formControlName="address" rows="2"></textarea>
                </mat-form-field>
              </div>
            </fieldset>

            <!-- Form actions -->
            <div class="form-actions">
              <a routerLink="/students" class="btn btn-secondary">Cancel</a>
              <button
                type="submit"
                class="btn btn-primary"
                [disabled]="saving() || form.invalid">
                @if (saving()) { <span class="spin-sm"></span> }
                {{ isEdit() ? 'Update Student' : 'Enroll Student' }}
              </button>
            </div>

          </div>

        </div>
      </form>
    </div>
  `,
  styles: [`
    .ph-left { display: flex; align-items: center; gap: 10px; }

    .form-layout {
      display: grid;
      grid-template-columns: 220px 1fr;
      gap: 20px;
      align-items: flex-start;
      @media (max-width: 900px) { grid-template-columns: 1fr; }
    }

    .card {
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      padding: 20px;
    }

    /* Photo card */
    .photo-upload {
      display: flex; flex-direction: column;
      align-items: center; gap: 10px;
    }

    .photo-preview {
      width: 150px; height: 150px; border-radius: 50%;
      background: var(--color-surface-2);
      border: 2px dashed var(--color-border);
      cursor: pointer; overflow: hidden;
      display: flex; align-items: center; justify-content: center;
      transition: border-color var(--transition-fast);

      &:hover, &:focus-visible {
        border-color: var(--color-primary);
        outline: 2px solid var(--color-primary);
        outline-offset: 2px;
      }

      img { width: 100%; height: 100%; object-fit: cover; }
    }

    .photo-placeholder {
      display: flex; flex-direction: column;
      align-items: center; gap: 6px;
      color: var(--color-text-muted); text-align: center;
      .material-icons-round { font-size: 28px; }
      span { font-size: 12px; }
    }

    .upload-pct { font-size: 12px; color: var(--color-text-secondary); }

    /* Form card */
    fieldset { border: none; padding: 0; margin: 0; }

    .grid-2 {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 0 16px;
      mat-form-field { width: 100%; }
      .col-full { grid-column: 1 / -1; }
      @media (max-width: 600px) { grid-template-columns: 1fr; }
    }

    .form-actions {
      display: flex; justify-content: flex-end; gap: 10px;
      padding-top: 16px;
      border-top: 1px solid var(--color-border);
    }

    .spin-sm {
      width: 14px; height: 14px;
      border: 2px solid rgba(255,255,255,.35);
      border-top-color: #fff;
      border-radius: 50%;
      animation: sp-spin .5s linear infinite;
      display: inline-block;
    }

    @keyframes sp-spin { to { transform: rotate(360deg); } }
  `]
})
export class StudentFormComponent implements OnInit {
  private route      = inject(ActivatedRoute);
  private router     = inject(Router);
  private fb         = inject(FormBuilder);
  private studentSvc = inject(StudentService);
  private toast      = inject(ToastService);

  isEdit         = signal(false);
  saving         = signal(false);
  photoPreview   = signal<string | null>(null);
  photoFile      = signal<File | null>(null);
  uploadProgress = signal(0);
  studentId      = signal<string | null>(null);

  readonly bloodGroups = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];

  form = this.fb.group({
    fullName:         ['', Validators.required],
    email:            ['', [Validators.required, Validators.email]],
    password:         ['', [Validators.minLength(8)]],
    phoneNumber:      [''],
    rollNumber:       ['', Validators.required],
    classId:          [null as number | null, Validators.required],
    academicYearId:   [null as number | null, Validators.required],
    status:           ['Active'],
    gender:           ['Male'],
    dateOfBirth:      [''],
    bloodGroup:       [''],
    address:          [''],
    emergencyContact: ['']
  });

  get f() { return this.form.controls; }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'new') {
      this.isEdit.set(true);
      this.studentId.set(id);
      // Password is optional on edit
      this.form.get('password')!.clearValidators();
      this.form.get('password')!.updateValueAndValidity();
      this.loadStudent(id);
    } else {
      // Password required on create
      this.form.get('password')!.setValidators([Validators.required, Validators.minLength(8)]);
      this.form.get('password')!.updateValueAndValidity();
    }
  }

  private loadStudent(id: string): void {
    this.studentSvc.getStudent(id).subscribe({
      next: (s) => {
        this.form.patchValue({
          fullName:         s.fullName,
          email:            s.email,
          phoneNumber:      s.phoneNumber ?? '',
          rollNumber:       s.rollNumber,
          classId:          s.classId,
          academicYearId:   s.academicYearId,
          status:           s.status,
          gender:           s.gender,
          dateOfBirth:      s.dateOfBirth ?? '',
          bloodGroup:       s.bloodGroup ?? '',
          address:          s.address ?? '',
          emergencyContact: s.emergencyContact ?? ''
        });
        if (s.profilePhotoUrl) this.photoPreview.set(s.profilePhotoUrl);
      },
      error: () => this.toast.error('Failed to load student')
    });
  }

  onPhotoSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;

    // Preview locally (no upload until form submit)
    const reader = new FileReader();
    reader.onload = (e) => this.photoPreview.set(e.target?.result as string);
    reader.readAsDataURL(file);
    this.photoFile.set(file);
  }

  clearPhoto(): void {
    this.photoPreview.set(null);
    this.photoFile.set(null);
    this.uploadProgress.set(0);
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    const v     = this.form.value;
    const photo = this.photoFile();

    if (this.isEdit()) {
      const update: any = {
        FullName:         v.fullName ?? undefined,
        PhoneNumber:      v.phoneNumber ?? undefined,
        ClassId:          v.classId ?? undefined,
        Gender:           v.gender ?? undefined,
        BloodGroup:       v.bloodGroup ?? undefined,
        Address:          v.address ?? undefined,
        EmergencyContact: v.emergencyContact ?? undefined,
        Status:           v.status ?? undefined
      };
      if (v.dateOfBirth) {
        update.DateOfBirth = typeof v.dateOfBirth === 'string' ? v.dateOfBirth : undefined;
      }

      // updateStudentWithPhoto handles FormData if photo supplied
      const action = photo
        ? this.studentSvc.updateStudentWithPhoto(this.studentId()!, update, photo)
        : this.studentSvc.updateStudent(this.studentId()!, update);

      action.subscribe({
        next: () => {
          this.saving.set(false);
          this.toast.success('Student updated');
          this.router.navigate(['/students', this.studentId()]);
        },
        error: (err) => {
          this.saving.set(false);
          this.toast.error('Update failed', err?.error?.detail);
        }
      });

    } else {
      const create: any = {
        Email:            v.email!,
        Password:         v.password!,
        FullName:         v.fullName!,
        PhoneNumber:      v.phoneNumber ?? undefined,
        RollNumber:       v.rollNumber!,
        ClassId:          v.classId!,
        AcademicYearId:   v.academicYearId!,
        Gender:           v.gender ?? 'Male',
        BloodGroup:       v.bloodGroup ?? undefined,
        Address:          v.address ?? undefined,
        EmergencyContact: v.emergencyContact ?? undefined
      };
      if (v.dateOfBirth) {
        create.DateOfBirth = typeof v.dateOfBirth === 'string' ? v.dateOfBirth : undefined;
      }

      this.studentSvc.createStudent(create).subscribe({
        next: (res: any) => {
          this.saving.set(false);
          this.toast.success('Student enrolled successfully');
          this.router.navigate(['/students', res.studentId]);
        },
        error: (err) => {
          this.saving.set(false);
          const code = err?.error?.code;
          if (code === 'ROLL_EXISTS')  this.toast.error('Roll number already exists in this class');
          else if (code === 'EMAIL_EXISTS') this.toast.error('Email address is already registered');
          else this.toast.error('Enrollment failed', err?.error?.detail ?? 'Please try again.');
        }
      });
    }
  }
}