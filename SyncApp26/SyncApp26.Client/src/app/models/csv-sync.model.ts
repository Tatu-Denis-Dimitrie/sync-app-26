export enum UserRole {
  Employee = 'employee',
  LineManager = 'line-manager'
}

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
  assignedToPersonalId?: string;
  assignedToName?: string;
  createdAt: Date;
  updatedAt?: Date;
  // Computed properties
  role?: UserRole;  // Calculated based on whether user has direct reports
}

export interface Department {
  id: string;
  name: string;
  lineManagerCount: number;
  employeeCount: number;
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

export interface ImportConflictHistory {
  id: string;
  importHistoryId: string;
  importDate?: string;
  importFileName?: string;
  userId: string;
  fieldName: string;
  oldValue: string;
  newValue: string;
  status: string; // 'accepted' | 'rejected'
}
