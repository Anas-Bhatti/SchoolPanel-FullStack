// ============================================================
// core/models/index.ts
// TypeScript interfaces matching every backend DTO exactly.
// ============================================================

// ─── Auth ─────────────────────────────────────────────────────

export interface LoginRequest {
  email: string;
  password: string;
  deviceInfo?: string;
}

export interface LoginResponse {
  accessToken: string | null;
  refreshToken: string | null;
  accessTokenExpiry: string;
  refreshTokenExpiry: string;
  requiresTwoFactor: boolean;
  pendingToken: string | null;
  user: AuthUser | null;
}

export interface AuthUser {
  userId: string;
  email: string;
  fullName: string;
  roles: string[];
  permissions: Permission[];
}

export interface Permission {
  module: string;
  canView: boolean;
  canCreate: boolean;
  canEdit: boolean;
  canDelete: boolean;
  canExport: boolean;
}

export interface TwoFactorVerifyRequest {
  pendingToken: string;
  code: string;
}

export interface TwoFactorSetupResponse {
  secretKey: string;
  qrCodeUri: string;
  manualEntryKey: string;
  recoveryCodes: string[];
}

export interface GoogleLoginRequest {
  idToken: string;
  deviceInfo?: string;
}

export interface RefreshTokenRequest {
  refreshToken: string;
  deviceInfo?: string;
}

// ─── Pagination ───────────────────────────────────────────────

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface PaginationQuery {
  page?: number;
  pageSize?: number;
  search?: string;
  sort?: string;
  dir?: 'asc' | 'desc';
}

// ─── Students ─────────────────────────────────────────────────

export interface StudentFilters extends PaginationQuery {
  classId?: number;
  section?: string;
  status?: string;
  gender?: string;
  academicYearId?: number;
}

export interface StudentListItem {
  studentId: string;
  rollNumber: string;
  fullName: string;
  email: string;
  phoneNumber: string | null;
  className: string;
  section: string;
  gender: string;
  status: 'Active' | 'Inactive' | 'Graduated' | 'Transferred';
  academicYear: string;
  enrollmentDate: string;
  profilePhotoUrl: string | null;
}

export interface StudentDetail extends StudentListItem {
  classId: number;
  bloodGroup: string | null;
  dateOfBirth: string | null;
  address: string | null;
  emergencyContact: string | null;
  academicYearId: number;
  parentName: string | null;
  parentPhone: string | null;
  attendance: AttendanceSummary;
  fees: FeesSummary;
}

export interface CreateStudentRequest {
  email: string;
  password: string;
  fullName: string;
  phoneNumber?: string;
  rollNumber: string;
  classId: number;
  academicYearId: number;
  parentId?: string;
  dateOfBirth?: string;
  gender: string;
  bloodGroup?: string;
  address?: string;
  emergencyContact?: string;
}

export interface UpdateStudentRequest {
  fullName?: string;
  phoneNumber?: string;
  classId?: number;
  dateOfBirth?: string;
  gender?: string;
  bloodGroup?: string;
  address?: string;
  emergencyContact?: string;
  status?: string;
}

export interface BulkImportResult {
  totalRows: number;
  succeeded: number;
  failed: number;
  errors: BulkRowError[];
}

export interface BulkRowError {
  row: number;
  rollNumber: string;
  reason: string;
}

// ─── Attendance ───────────────────────────────────────────────

export interface AttendanceSummary {
  totalDays: number;
  presentDays: number;
  absentDays: number;
  leaveDays: number;
  attendancePct: number;
}

export interface AttendanceEntry {
  studentId: string;
  status: 'P' | 'A' | 'L' | 'H';
  remarks?: string;
}

export interface MarkAttendanceRequest {
  classId: number;
  attendanceDate: string;
  entries: AttendanceEntry[];
}

export interface AttendanceRow {
  studentId: string;
  rollNumber: string;
  fullName: string;
  attendanceDate: string;
  status: string;
  remarks: string | null;
  markedAt: string | null;
  markedBy: string | null;
}

export interface TodayAttendance {
  date: string;
  totalStudents: number;
  totalTeachers: number;
  studentsPresent: number;
  studentsAbsent: number;
  studentsLeave: number;
  studentPresentPct: number;
  classBreakdown: ClassAttendanceSnapshot[];
}

export interface ClassAttendanceSnapshot {
  className: string;
  section: string;
  total: number;
  present: number;
  absent: number;
  leave: number;
}

// ─── Fees ─────────────────────────────────────────────────────

export interface FeesSummary {
  totalDue: number;
  totalPaid: number;
  balanceDue: number;
}

export interface FeesDue {
  studentId: string;
  studentName: string;
  rollNumber: string;
  className: string;
  lineItems: FeeLineItem[];
  grandTotalDue: number;
  grandTotalPaid: number;
  grandBalance: number;
}

export interface FeeLineItem {
  feeTypeId: number;
  feeTypeName: string;
  frequency: string;
  amountDue: number;
  amountPaid: number;
  discount: number;
  fine: number;
  balance: number;
  dueDate: string | null;
  isOverdue: boolean;
}

export interface RecordPaymentRequest {
  studentId: string;
  feeTypeId: number;
  academicYearId: number;
  amountDue: number;
  amountPaid: number;
  discount?: number;
  fine?: number;
  paymentMethod: string;
  referenceNumber?: string;
  remarks?: string;
}

export interface FeeCollectionSummary {
  monthly: MonthlyFee[];
  byClass: ClassFee[];
  totalDue: number;
  totalCollected: number;
  totalPending: number;
}

export interface MonthlyFee {
  year: number;
  month: number;
  monthName: string;
  studentsWhoPaid: number;
  totalDue: number;
  totalCollected: number;
  totalPending: number;
  totalDiscount: number;
  totalFine: number;
}

export interface ClassFee {
  className: string;
  section: string;
  totalStudents: number;
  totalDue: number;
  totalCollected: number;
  totalPending: number;
}

// ─── Dashboard ────────────────────────────────────────────────

export interface DashboardStats {
  summary: DashboardSummary;
  feeChart: FeeChartPoint[];
  attendanceSnapshot: ClassAttendanceSnapshot[];
}

export interface DashboardSummary {
  totalStudents: number;
  totalTeachers: number;
  totalClasses: number;
  feeCollectedThisMonth: number;
  totalFeePending: number;
  totalMaleStudents: number;
  totalFemaleStudents: number;
}

export interface FeeChartPoint {
  payYear: number;
  payMonth: number;
  monthLabel: string;
  collected: number;
  pending: number;
}

// ─── Settings ─────────────────────────────────────────────────

export interface Setting {
  settingKey: string;
  settingValue: string | null;
  category: string;
  description: string | null;
}

export interface UpdateSettingRequest {
  key: string;
  value: string | null;
  category?: string;
}

// ─── Notification ─────────────────────────────────────────────

export interface Notification {
  notificationId: number;
  title: string;
  message: string;
  notificationType: 'Info' | 'Success' | 'Warning' | 'Danger';
  module: string | null;
  isRead: boolean;
  readAt: string | null;
  createdAt: string;
}

// ─── Roles ────────────────────────────────────────────────────

export interface Role {
  roleId: number;
  roleName: string;
  description: string | null;
  isSystemRole: boolean;
  createdAt: string;
}

// ─── API response wrappers ────────────────────────────────────

export interface ApiResult<T> {
  success: boolean;
  data: T | null;
  error?: ApiError;
}

export interface ApiError {
  status: number;
  code: string;
  message: string;
  detail?: string;
}

export interface ApiOk<T> {
  data: T;
  message?: string;
}

// ─── Shared ───────────────────────────────────────────────────

export interface SelectOption {
  value: string | number;
  label: string;
}

export interface FileUploadResult {
  success: boolean;
  url: string | null;
  blobName: string | null;
  error: string | null;
}

export interface ToastMessage {
  id: string;
  type: 'success' | 'error' | 'warning' | 'info';
  title: string;
  message?: string;
  duration?: number;
}