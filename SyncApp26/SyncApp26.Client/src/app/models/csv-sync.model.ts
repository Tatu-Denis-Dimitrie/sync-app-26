export enum UserRole {
  BasicUser = 'basicuser',
  LineManager = 'line-manager'
}

export enum BloodType {
  APositive = 'APositive',
  ANegative = 'ANegative',
  BPositive = 'BPositive',
  BNegative = 'BNegative',
  ABPositive = 'ABPositive',
  ABNegative = 'ABNegative',
  OPositive = 'OPositive',
  ONegative = 'ONegative'
}

export const BLOOD_TYPE_LABELS: Record<BloodType, string> = {
  [BloodType.APositive]: 'A+',
  [BloodType.ANegative]: 'A-',
  [BloodType.BPositive]: 'B+',
  [BloodType.BNegative]: 'B-',
  [BloodType.ABPositive]: 'AB+',
  [BloodType.ABNegative]: 'AB-',
  [BloodType.OPositive]: 'O+',
  [BloodType.ONegative]: 'O-'
};

export const BLOOD_TYPE_OPTIONS: { value: BloodType; label: string }[] =
  Object.values(BloodType).map(value => ({ value, label: BLOOD_TYPE_LABELS[value] }));

export enum SyncStatus {
  Pending = 'pending',
  InProgress = 'in-progress',
  Synced = 'synced',
  Failed = 'failed',
  Conflict = 'conflict'
}

export interface User {
  id: string;
  personalId: string;
  firstName: string;
  lastName: string;
  email: string;
  departmentId: string;
  departmentName: string;
  assignedToId?: string;
  assignedToPersonalId?: string;
  assignedToName?: string;
  function?: string;
  address?: string;
  badgeNumber?: string;
  bloodType?: BloodType;
  createdAt: Date;
  updatedAt?: Date;
  hasSignedSsm?: boolean;
  hasSignedSu?: boolean;
  hasUnsignedSsm?: boolean;
  hasUnsignedSu?: boolean;
  // Computed properties
  role?: UserRole;  // Calculated based on whether user has direct reports
}

export interface Department {
  id: string;
  name: string;
  isActive: boolean;
  lineManagerCount: number;
  employeeCount: number;
  deletedAt?: string | Date;
}

export interface UserComparison {
  id: string;
  status: 'new' | 'modified' | 'unchanged' | 'deleted';
  dbUser: User | null;
  csvUser: User | null;
  conflicts: FieldConflict[];
  selected: boolean;
}

export interface FieldConflict {
  field: keyof User;
  dbValue: any;
  csvValue: any;
  selectedValue?: 'db' | 'csv';
  selected: boolean; // Whether this field should be synced
  hasPendingRequest?: boolean; // True if a pending DataChangeRequest is also targeting this field
  pendingRequestValue?: string; // The value(s) the pending request(s) are asking for, when hasPendingRequest is true
}

export interface CsvImport {
  id: string;
  fileName: string;
  uploadedAt: Date;
  totalRecords: number;
  newRecords: number;
  modifiedRecords: number;
  unchangedRecords: number;
  conflicts: number;
  status: SyncStatus;
}

export interface SyncResult {
  success: boolean;
  recordsProcessed: number;
  recordsFailed: number;
  recordsSkipped: number;
  message?: string;
  errors?: string[];
  processingTimeMs?: number;
}

export interface ComparisonResponse {
  comparisons: UserComparison[];
  totalRows: number;
  validationTimeMs: number;
  comparisonTimeMs: number;
  totalTimeMs: number;
  fileName?: string;
}

export interface PaginationParams {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

export interface SyncProgress {
  fileName: string;
  progress: number;
  currentRecord: number;
  totalRecords: number;
  status: SyncStatus;
}

export interface SyncProgressUpdate {
  processed: number;
  failed: number;
  skipped: number;
  message?: string;
}

export interface ImportHistoryItem {
  id: string;
  importDate: string;
  fileName: string;
}

export interface UserChangeHistory {
  id: string;
  importHistoryId?: string;
  importDate?: string;
  importFileName?: string;
  userId: string;
  fieldName: string;
  oldValue: string;
  newValue: string;
  status?: string | null; // 'accepted' | 'rejected' | null (manual change)
  createdAt?: string;
}
