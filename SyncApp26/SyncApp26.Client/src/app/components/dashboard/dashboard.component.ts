import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, Subject, combineLatest, BehaviorSubject } from 'rxjs';
import { map, take, takeUntil } from 'rxjs/operators';
import { UserSyncService } from '../../services/user-sync.service';
import { DepartmentsSyncService } from '../../services/departments-sync.service';
import { AuthenticationService } from '../../services/authentication.service';
import { CSVDepartmentComparisonDTO } from '../../models/csv-department-sync.model';
import { User, UserComparison, UserRole, Department } from '../../models/csv-sync.model';
import { PaginationComponent } from '../pagination/pagination.component';
import { ComparisonViewComponent } from '../comparison-view/comparison-view.component';
import { UploadProgress, SyncProgressUpdate } from '../../services/user-sync.signalr.service';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent, ComparisonViewComponent, RouterModule],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  users$!: Observable<User[]>;
  paginatedUsers$!: Observable<User[]>;
  stats$!: Observable<any>;
  departments$!: Observable<Department[]>;
  currentComparison$!: Observable<UserComparison[] | null>;

  private currentPage$ = new BehaviorSubject<number>(1);
  pageSize = 10;
  totalItems = 0;

  get currentPage(): number { return this.currentPage$.value; }
  set currentPage(value: number) { this.currentPage$.next(value); }

  private searchQuery$ = new BehaviorSubject<string>('');
  private selectedDepartment$ = new BehaviorSubject<string>('all');
  private selectedFunction$ = new BehaviorSubject<string>('all');
  private selectedRole$ = new BehaviorSubject<UserRole | 'all'>('all');
  private sortField$ = new BehaviorSubject<'createdAt' | 'updatedAt'>('updatedAt');
  private sortDirection$ = new BehaviorSubject<'asc' | 'desc'>('desc');

  get searchQuery(): string { return this.searchQuery$.value; }
  set searchQuery(value: string) { this.searchQuery$.next(value); }

  get selectedDepartment(): string { return this.selectedDepartment$.value; }
  set selectedDepartment(value: string) { this.selectedDepartment$.next(value); }

  get selectedFunction(): string { return this.selectedFunction$.value; }
  set selectedFunction(value: string) { this.selectedFunction$.next(value); }

  get selectedRole(): UserRole | 'all' { return this.selectedRole$.value; }
  set selectedRole(value: UserRole | 'all') { this.selectedRole$.next(value); }

  get sortField(): 'createdAt' | 'updatedAt' { return this.sortField$.value; }
  set sortField(value: 'createdAt' | 'updatedAt') { this.sortField$.next(value); }

  get sortDirection(): 'asc' | 'desc' { return this.sortDirection$.value; }
  set sortDirection(value: 'asc' | 'desc') { this.sortDirection$.next(value); }

  isUploading = false;
  isSyncing = false;
  showComparison = false;
  showDepartmentComparison = false;
  showErrorModal = false;
  errorModalTitle = '';
  errorModalErrors: string[] = [];
  errorModalWarnings: string[] = [];
  errorModalStats = { totalRows: 0, validRows: 0, invalidRows: 0 };
  canProceedWithValidRows = false;
  currentUploadFile: File | null = null;

  currentComparisons: UserComparison[] = [];
  totalSyncItems = 0;
  currentDepartmentComparisons: CSVDepartmentComparisonDTO[] = [];
  departmentSearchQuery: string = '';

  uploadProgress: UploadProgress | null = null;
  syncProgress: SyncProgressUpdate | null = null;

  uploadStartTime: number = 0;
  syncStartTime: number = 0;
  successMessage: string = '';
  serverTimingInfo: { validationTimeMs: number; comparisonTimeMs: number; totalTimeMs: number } | null = null;
  availableDepartmentFunctions: string[] = [];

  UserRole = UserRole;
  fileName = '';

  constructor(
    private userSyncService: UserSyncService,
    private departmentsSyncService: DepartmentsSyncService,
    private authService: AuthenticationService,
    private router: Router
  ) { }

  logout(): void {
    this.authService.logout();
  }

  ngOnInit(): void {
    this.users$ = this.userSyncService.users$;
    this.stats$ = this.userSyncService.getUserStats();
    this.departments$ = this.userSyncService.getDepartments();
    this.currentComparison$ = this.userSyncService.currentComparison$;

    // Subscribe to progress events
    this.userSyncService.uploadProgress$
      .pipe(takeUntil(this.destroy$))
      .subscribe(progress => {
        this.uploadProgress = progress;
      });

    this.userSyncService.syncProgress$
      .pipe(takeUntil(this.destroy$))
      .subscribe(progress => {
        this.syncProgress = progress;
      });

    // Subscribe to comparison changes
    this.currentComparison$
      .pipe(takeUntil(this.destroy$))
      .subscribe(comparisons => {
        this.showComparison = comparisons !== null && comparisons.length > 0;
        this.currentComparisons = comparisons || [];
      });

    this.paginatedUsers$ = combineLatest([
      this.users$,
      this.stats$,
      this.searchQuery$,
      this.selectedDepartment$,
      this.selectedFunction$,
      this.selectedRole$,
      this.currentPage$,
      this.sortField$,
      this.sortDirection$
    ]).pipe(
      map(([users, stats, searchQuery, selectedDepartment, selectedFunction, selectedRole, currentPage, sortField, sortDirection]) => {
        // Filter users
        let filtered = users.filter(user => {
          const fullName = `${user.firstName} ${user.lastName}`.toLowerCase();
          const matchesSearch = !searchQuery ||
            fullName.includes(searchQuery.toLowerCase()) ||
            user.email.toLowerCase().includes(searchQuery.toLowerCase());
          const matchesDepartment = selectedDepartment === 'all' ||
            user.departmentName === selectedDepartment;
          const matchesFunction = selectedFunction === 'all' ||
            (user.function || '').trim().toLowerCase() === selectedFunction.trim().toLowerCase();
          const matchesRole = selectedRole === 'all' ||
            user.role === selectedRole;
          return matchesSearch && matchesDepartment && matchesFunction && matchesRole;
        });

        // Sort users
        filtered = filtered.sort((a, b) => {
          const aValue = a[sortField] ? new Date(a[sortField]!).getTime() : 0;
          const bValue = b[sortField] ? new Date(b[sortField]!).getTime() : 0;
          return sortDirection === 'asc' ? aValue - bValue : bValue - aValue;
        });

        this.totalItems = filtered.length;

        // Paginate
        const startIndex = (currentPage - 1) * this.pageSize;
        return filtered.slice(startIndex, startIndex + this.pageSize);
      })
    );
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  formatDuration(ms: number): string {
    const seconds = Math.floor(ms / 1000);
    const minutes = Math.floor(seconds / 60);
    const remainingSeconds = seconds % 60;
    if (minutes > 0) {
      return `${minutes}m ${remainingSeconds}s`;
    }
    return `${seconds}.${Math.floor((ms % 1000) / 100)}s`;
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];
      this.fileName = file.name;
      this.uploadFile(file);
    }
  }

  onDepartmentFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];
      this.uploadDepartmentFile(file);
    }
  }

  uploadFile(file: File): void {
    this.isUploading = true;
    this.successMessage = '';
    this.uploadStartTime = Date.now();
    this.serverTimingInfo = null;
    this.currentUploadFile = file; // Save for potential retry

    this.userSyncService.uploadAndCompare(file)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (comparisons) => {
          const duration = Date.now() - this.uploadStartTime;
          console.log('CSV uploaded and compared:', comparisons);
          this.isUploading = false;
          this.showComparison = true;
          this.fileName = file.name;

          // Get server timing info
          this.userSyncService.timingInfo$.pipe(takeUntil(this.destroy$)).subscribe(timing => {
            this.serverTimingInfo = timing;
            if (timing) {
              this.successMessage = `Analysis completed in ${this.formatDuration(duration)} (Server: ${this.formatDuration(timing.totalTimeMs)}, Network: ${this.formatDuration(duration - timing.totalTimeMs)})`;
            } else {
              this.successMessage = `Analysis completed in ${this.formatDuration(duration)}`;
            }
          });

          // Check for warnings
          this.userSyncService.warnings$.pipe(takeUntil(this.destroy$)).subscribe(warnings => {
            if (warnings && warnings.length > 0) {
              this.errorModalTitle = 'CSV Validation Warnings';
              this.errorModalErrors = [];
              this.errorModalWarnings = warnings;
              this.errorModalStats = { totalRows: 0, validRows: 0, invalidRows: 0 };
              this.showErrorModal = true;
            }
          });

          setTimeout(() => this.successMessage = '', 5000);
        },
        error: (error) => {
          console.error('Upload failed:', error);
          this.isUploading = false;

          // Show validation errors in custom modal
          if (error.status === 400 && error.error) {
            const errorData = error.error;

            if (errorData.errors && errorData.errors.length > 0) {
              // Check if we can proceed with valid rows only
              this.canProceedWithValidRows = errorData.canProceedWithValidRows === true;

              this.errorModalTitle = this.canProceedWithValidRows
                ? 'CSV Has Invalid Rows - Proceed with Valid Rows?'
                : 'CSV Validation Failed';
              this.errorModalErrors = [];
              this.errorModalWarnings = [];

              // Format errors
              const maxErrors = 20;
              const errorsToShow = errorData.errors.slice(0, maxErrors);

              errorsToShow.forEach((err: any) => {
                // Handle both string errors and object errors
                if (typeof err === 'string') {
                  this.errorModalErrors.push(err);
                } else if (err.row === 0 || !err.row) {
                  this.errorModalErrors.push(err.message || err);
                } else {
                  this.errorModalErrors.push(`Row ${err.row}: ${err.field} - ${err.message}`);
                }
              });

              if (errorData.errors.length > maxErrors) {
                this.errorModalErrors.push(`...and ${errorData.errors.length - maxErrors} more errors`);
              }

              // Format warnings
              if (errorData.warnings && errorData.warnings.length > 0) {
                errorData.warnings.slice(0, 10).forEach((warn: any) => {
                  if (typeof warn === 'string') {
                    this.errorModalWarnings.push(warn);
                  } else {
                    this.errorModalWarnings.push(`Row ${warn.row}: ${warn.field} - ${warn.message}`);
                  }
                });
              }

              // Stats
              if (errorData.totalRows !== undefined) {
                this.errorModalStats = {
                  totalRows: errorData.totalRows,
                  validRows: errorData.validRows,
                  invalidRows: errorData.invalidRows
                };
              }

              this.showErrorModal = true;
            } else if (errorData.error) {
              this.errorModalTitle = 'Upload Error';
              this.errorModalErrors = [errorData.error];
              this.errorModalWarnings = [];
              this.errorModalStats = { totalRows: 0, validRows: 0, invalidRows: 0 };
              this.showErrorModal = true;
            }
          } else {
            this.errorModalTitle = 'Upload Failed';
            this.errorModalErrors = [error.message || 'Unknown error occurred'];
            this.errorModalWarnings = [];
            this.errorModalStats = { totalRows: 0, validRows: 0, invalidRows: 0 };
            this.showErrorModal = true;
          }
        }
      });
  }

  uploadDepartmentFile(file: File): void {
    this.isUploading = true;
    this.departmentsSyncService.uploadAndCompareDepartments(file)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (comparisons) => {
          console.log('CSV uploaded and compared:', comparisons);
          this.currentDepartmentComparisons = this.normalizeDepartmentComparisons(comparisons);
          this.isUploading = false;
          this.showDepartmentComparison = true;
        },
        error: (error) => {
          console.error('Upload failed:', error);
          this.isUploading = false;

          if (error.status === 400 && error.error) {
            const errorData = error.error;

            if (errorData.errors && errorData.errors.length > 0) {
              this.errorModalTitle = 'CSV Validation Failed';
              this.errorModalErrors = errorData.errors.slice(0, 20);
              this.errorModalWarnings = errorData.warnings || [];
              this.showErrorModal = true;
            } else if (errorData.error) {
              this.errorModalTitle = 'Upload Error';
              this.errorModalErrors = [errorData.error];
              this.errorModalWarnings = [];
              this.showErrorModal = true;
            }
          } else {
            this.errorModalTitle = 'Upload Failed';
            this.errorModalErrors = [error.message || 'Unknown error occurred'];
            this.errorModalWarnings = [];
            this.showErrorModal = true;
          }
        }
      });
  }

  closeErrorModal(): void {
    this.showErrorModal = false;
    this.canProceedWithValidRows = false;
    this.errorModalTitle = '';
    this.errorModalErrors = [];
    this.errorModalWarnings = [];
    this.errorModalStats = { totalRows: 0, validRows: 0, invalidRows: 0 };
  }

  proceedWithValidRows(): void {
    if (!this.currentUploadFile) return;

    this.showErrorModal = false;
    this.canProceedWithValidRows = false;
    this.isUploading = true;
    this.uploadStartTime = Date.now();

    // Re-upload with skipInvalidRows flag
    this.userSyncService.uploadAndCompare(this.currentUploadFile, true)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (comparisons) => {
          const duration = Date.now() - this.uploadStartTime;
          console.log('CSV uploaded with valid rows only:', comparisons);
          this.isUploading = false;
          this.showComparison = true;
          this.fileName = this.currentUploadFile?.name || this.fileName;

          // Get server timing info
          this.userSyncService.timingInfo$.pipe(takeUntil(this.destroy$)).subscribe(timing => {
            this.serverTimingInfo = timing;
            if (timing) {
              this.successMessage = `Analysis completed in ${this.formatDuration(duration)} (Server: ${this.formatDuration(timing.totalTimeMs)}, Network: ${this.formatDuration(duration - timing.totalTimeMs)})`;
            } else {
              this.successMessage = `Analysis completed in ${this.formatDuration(duration)}`;
            }
          });

          // Show info about skipped rows if any
          this.userSyncService.errors$.pipe(takeUntil(this.destroy$)).subscribe(errors => {
            if (errors && errors.length > 0) {
              // Show notification about skipped rows
              const skippedCount = errors.length;
              this.successMessage += ` (${skippedCount} invalid rows skipped)`;
            }
          });

          setTimeout(() => this.successMessage = '', 5000);
        },
        error: (error) => {
          console.error('Upload with valid rows failed:', error);
          this.isUploading = false;
          this.errorModalTitle = 'Upload Failed';
          this.errorModalErrors = [error.message || 'Unknown error occurred'];
          this.errorModalWarnings = [];
          this.errorModalStats = { totalRows: 0, validRows: 0, invalidRows: 0 };
          this.canProceedWithValidRows = false;
          this.showErrorModal = true;
        }
      });
  }

  syncSelectedUsers(): void {
    this.totalSyncItems = this.getSelectedSyncCount();
    this.isSyncing = true;
    this.successMessage = '';
    this.syncStartTime = Date.now();
    this.serverTimingInfo = null;

    this.userSyncService.syncUsers(this.currentComparisons, this.fileName)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          const duration = Date.now() - this.syncStartTime;
          console.log('Sync successful:', result);
          this.isSyncing = false;
          this.showComparison = false;

          if (result.processingTimeMs) {
            this.successMessage = `Sync completed in ${this.formatDuration(duration)} (Server: ${this.formatDuration(result.processingTimeMs)}, Network: ${this.formatDuration(duration - result.processingTimeMs)})`;
          } else {
            this.successMessage = `Sync completed in ${this.formatDuration(duration)}`;
          }

          setTimeout(() => this.successMessage = '', 5000);

          this.userSyncService.refreshUsers();
        },
        error: (error) => {
          console.error('Sync failed:', error);
          this.isSyncing = false;
        }
      });
  }

  syncSelectedDepartments(): void {
    if (!this.currentDepartmentComparisons || this.currentDepartmentComparisons.length === 0) {
      return;
    }

    this.isSyncing = true;
    const selected = this.currentDepartmentComparisons.filter((c: CSVDepartmentComparisonDTO) => c.selected && c.status === 'new');
    this.departmentsSyncService.syncDepartments(selected)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          console.log('Department sync successful:', result);
          this.isSyncing = false;
          this.showDepartmentComparison = false;
        },
        error: (error) => {
          console.error('Department sync failed:', error);
          this.isSyncing = false;
        }
      });
  }

  cancelComparison(): void {
    this.userSyncService.clearComparison();
    this.showComparison = false;
  }

  cancelDepartmentComparison(): void {
    this.departmentsSyncService.clearComparison();
    this.showDepartmentComparison = false;
  }

  toggleAllDepartmentSelections(checked: boolean): void {
    this.currentDepartmentComparisons = this.currentDepartmentComparisons.map((c: CSVDepartmentComparisonDTO) => ({
      ...c,
      selected: c.status === 'new' ? checked : false
    }));
  }

  toggleDepartmentSelection(comparison: any): void {
    if (comparison.status !== 'new') {
      return;
    }
    comparison.selected = !comparison.selected;
  }

  private normalizeDepartmentComparisons(comparisons: CSVDepartmentComparisonDTO[]): CSVDepartmentComparisonDTO[] {
    return (comparisons || []).map(comparison => {
      const normalizedStatus = (comparison.status || '').toLowerCase() as CSVDepartmentComparisonDTO['status'];
      return {
        ...comparison,
        status: normalizedStatus,
        selected: normalizedStatus === 'new'
      };
    });
  }

  getSelectedDepartmentSyncCount(): number {
    return this.currentDepartmentComparisons.filter((c: any) => c.selected).length;
  }

  getDepartmentChangesCount(): number {
    return this.currentDepartmentComparisons.filter((c: any) => c.status === 'new').length;
  }

  hasDepartmentChanges(): boolean {
    return this.getDepartmentChangesCount() > 0;
  }

  onPageChange(page: number): void {
    this.currentPage = page;
  }

  onSearchChange(): void {
    this.currentPage = 1;
  }

  onDepartmentFilterChange(): void {
    this.currentPage = 1;

    if (this.selectedDepartment === 'all') {
      this.availableDepartmentFunctions = [];
      this.selectedFunction = 'all';
      return;
    }

    this.selectedFunction = 'all';
    this.departments$.pipe(take(1)).subscribe(departments => {
      const selectedDept = departments.find(d => d.name === this.selectedDepartment);

      if (!selectedDept) {
        this.availableDepartmentFunctions = [];
        return;
      }

      this.userSyncService.getFunctionsByDepartmentId(selectedDept.id)
        .pipe(take(1))
        .subscribe(functions => {
          this.availableDepartmentFunctions = functions;
        });
    });
  }

  onFunctionFilterChange(): void {
    this.currentPage = 1;
  }

  onRoleFilterChange(): void {
    this.currentPage = 1;
  }

  toggleSort(field: 'createdAt' | 'updatedAt'): void {
    if (this.sortField === field) {
      // Toggle direction if same field
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      // Set new field with default descending direction
      this.sortField = field;
      this.sortDirection = 'desc';
    }
  }

  getSortIcon(field: 'createdAt' | 'updatedAt'): string {
    if (this.sortField !== field) {
      return '↕️'; // Not sorted
    }
    return this.sortDirection === 'asc' ? '↑' : '↓';
  }

  getRoleBadgeColor(role: UserRole): string {
    return role === UserRole.LineManager
      ? 'bg-purple-500/10 text-purple-700 border-purple-500/20'
      : 'bg-blue-500/10 text-blue-700 border-blue-500/20';
  }

  getRoleIcon(role: UserRole): string {
    return role === UserRole.LineManager ? '👔' : '👤';
  }

  formatDate(date: Date | string | undefined): string {
    if (!date) return 'N/A';
    const d = new Date(date);
    const day = String(d.getDate()).padStart(2, '0');
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const year = String(d.getFullYear()).slice(-2);
    return `${day}/${month}/${year}`;
  }

  onComparisonSelectionChange(comparisons: UserComparison[]): void {
    this.currentComparisons = comparisons;
  }

  onFieldConflictResolved(event: { comparisonId: string, field: string, value: 'db' | 'csv' }): void {
    console.log('Conflict resolved:', event);
  }

  getSelectedSyncCount(): number {
    return this.currentComparisons.filter(c => c.selected).length;
  }

  navigateToDepartments(): void {
    this.router.navigate(['/departments']);
  }

  navigateToUsers(): void {
    this.router.navigate(['/users']);
  }

  navigateToEmployees(): void {
    this.router.navigate(['/employees']);
  }

  navigateToImportHistory(): void {
    this.router.navigate(['/import-history']);
  }

  navigateToSignature(): void {
    this.router.navigate(['/admin-signature']);
  }

  navigateToDocuments(): void {
    this.router.navigate(['/documents']);
  }

  getFilteredDepartmentComparisons(): CSVDepartmentComparisonDTO[] {
    // Filter to show only new departments
    let filtered = this.currentDepartmentComparisons.filter((comp: CSVDepartmentComparisonDTO) => comp.status === 'new');

    // Apply search query if provided
    if (!this.departmentSearchQuery.trim()) {
      return filtered;
    }

    const query = this.departmentSearchQuery.toLowerCase();
    return filtered.filter((comp: CSVDepartmentComparisonDTO) => {
      const csvName = comp.csvDepartment?.name?.toLowerCase() || '';
      return csvName.includes(query);
    });
  }

  getRelativeTime(date: Date | string | undefined): string {
    if (!date) return '';
    const now = new Date().getTime();
    const then = new Date(date).getTime();
    const diff = now - then;

    const seconds = Math.floor(diff / 1000);
    const minutes = Math.floor(seconds / 60);
    const hours = Math.floor(minutes / 60);
    const days = Math.floor(hours / 24);

    if (days > 0) return `${days}d ago`;
    if (hours > 0) return `${hours}h ago`;
    if (minutes > 0) return `${minutes}m ago`;
    return 'just now';
  }
}
