import { Component, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Observable, combineLatest, BehaviorSubject } from 'rxjs';
import { map, take } from 'rxjs/operators';
import { UserSyncService } from '../../services/user-sync.service';
import { AuthenticationService } from '../../services/authentication.service';
import { User, UserRole, Department, UserChangeHistory } from '../../models/csv-sync.model';
import { PaginationComponent } from '../pagination/pagination.component';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-employees-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './employees-detail.component.html',
  styleUrls: ['./employees-detail.component.css']
})
export class EmployeesDetailComponent implements OnInit {
  private readonly emptyGuid = '00000000-0000-0000-0000-000000000000';

  users$!: Observable<User[]>;
  paginatedUsers$!: Observable<User[]>;
  departments$!: Observable<Department[]>;
  selectedUser: User | null = null;
  importConflicts: UserChangeHistory[] = [];
  conflictsLoading = false;
  conflictsError = '';

  successMessage: string = '';

  userDocuments: any[] = [];
  documentsLoading = false;
  documentsError = '';

  private currentPage$ = new BehaviorSubject<number>(1);
  pageSize = 10;
  totalItems = 0;

  get currentPage(): number { return this.currentPage$.value; }
  set currentPage(value: number) { this.currentPage$.next(value); }

  private searchQuery$ = new BehaviorSubject<string>('');
  private selectedDepartment$ = new BehaviorSubject<string>('all');

  get searchQuery(): string { return this.searchQuery$.value; }
  set searchQuery(value: string) { this.searchQuery$.next(value); }

  get selectedDepartment(): string { return this.selectedDepartment$.value; }
  set selectedDepartment(value: string) { this.selectedDepartment$.next(value); }

  UserRole = UserRole;

  constructor(
    private userSyncService: UserSyncService,
    private authService: AuthenticationService,
    private router: Router,
    private route: ActivatedRoute,
    private http: HttpClient
  ) { }

  logout(): void {
    this.authService.logout();
  }

  ngOnInit(): void {
    this.users$ = this.userSyncService.users$;
    this.departments$ = this.userSyncService.getDepartments();

    // Check if specific user ID in route params
    this.route.params.subscribe(params => {
      if (params['id']) {
        this.users$.subscribe(users => {
          this.selectedUser = users.find(u => u.id === params['id']) || null;
          if (this.selectedUser) {
            this.loadUserConflicts(this.selectedUser.id);
          }
        });
      }
    });

    this.paginatedUsers$ = combineLatest([
      this.users$,
      this.searchQuery$,
      this.selectedDepartment$,
      this.currentPage$
    ]).pipe(
      map(([users, searchQuery, selectedDepartment, currentPage]) => {
        // Filter users
        let filtered = users.filter(user => {
          const fullName = `${user.firstName} ${user.lastName}`.toLowerCase();
          const matchesSearch = !searchQuery ||
            fullName.includes(searchQuery.toLowerCase()) ||
            user.email.toLowerCase().includes(searchQuery.toLowerCase()) ||
            user.departmentName.toLowerCase().includes(searchQuery.toLowerCase());
          const matchesDepartment = selectedDepartment === 'all' ||
            user.departmentName === selectedDepartment;
          return matchesSearch && matchesDepartment;
        });

        this.totalItems = filtered.length;

        // Paginate
        const startIndex = (currentPage - 1) * this.pageSize;
        return filtered.slice(startIndex, startIndex + this.pageSize);
      })
    );
  }

  selectUser(user: User): void {
    this.selectedUser = user;
    this.loadUserConflicts(user.id);
    this.loadUserDocuments(user.id);
  }

  closeDetails(): void {
    this.selectedUser = null;
    this.importConflicts = [];
    this.conflictsError = '';
    this.userDocuments = [];
    this.documentsError = '';
    this.router.navigate(['/employees']);
  }

  onPageChange(page: number): void {
    this.currentPage = page;
  }

  onSearchChange(): void {
    this.currentPage = 1;
  }

  onFilterChange(): void {
    this.currentPage = 1;
  }

  formatDate(date: Date | string | undefined): string {
    if (!date) return 'N/A';
    const d = new Date(date);
    const day = String(d.getDate()).padStart(2, '0');
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const year = String(d.getFullYear()).slice(-2);
    return `${day}/${month}/${year}`;
  }

  loadUserConflicts(userId: string): void {
    this.conflictsLoading = true;
    this.conflictsError = '';
    this.userSyncService.getImportConflictsByUserId(userId).subscribe({
      next: conflicts => {
        this.importConflicts = conflicts;
        this.conflictsLoading = false;
      },
      error: () => {
        this.conflictsLoading = false;
        this.conflictsError = 'Failed to load conflict history.';
        this.importConflicts = [];
      }
    });
  }

  loadUserDocuments(userId: string): void {
    this.documentsLoading = true;
    this.documentsError = '';
    this.http.get<any[]>(`${environment.apiUrl}/Document/user/${userId}`).subscribe({
      next: docs => {
        this.userDocuments = docs;
        this.documentsLoading = false;
      },
      error: () => {
        this.documentsLoading = false;
        this.documentsError = 'Failed to load documents.';
        this.userDocuments = [];
      }
    });
  }

  getConflictGroupsByImport(): Array<{ importHistoryId: string; importDate?: string; importFileName?: string; conflicts: UserChangeHistory[] }> {
    const groups = new Map<string, { importHistoryId: string; importDate?: string; importFileName?: string; conflicts: UserChangeHistory[] }>();
    const sorted = this.importConflicts
      .filter(conflict => this.isImportHistoryEntry(conflict))
      .slice()
      .sort((a, b) => {
        const aTime = this.getEntryTimestamp(a);
        const bTime = this.getEntryTimestamp(b);
        return bTime - aTime;
      });

    sorted.forEach(conflict => {
      const key = conflict.importHistoryId ?? '';
      if (!groups.has(key)) {
        groups.set(key, {
          importHistoryId: key,
          importDate: conflict.importDate,
          importFileName: conflict.importFileName,
          conflicts: []
        });
      }
      groups.get(key)?.conflicts.push(conflict);
    });

    return Array.from(groups.values()).sort((a, b) => {
      const aTime = a.importDate ? new Date(a.importDate).getTime() : 0;
      const bTime = b.importDate ? new Date(b.importDate).getTime() : 0;
      return bTime - aTime;
    });
  }

  getManualChanges(): UserChangeHistory[] {
    return this.importConflicts
      .filter(conflict => this.isManualEntry(conflict))
      .slice()
      .sort((a, b) => this.getEntryTimestamp(b) - this.getEntryTimestamp(a));
  }

  private isImportHistoryEntry(entry: UserChangeHistory): boolean {
    const importHistoryId = (entry.importHistoryId || '').trim();
    return !!entry.status && !!importHistoryId && importHistoryId !== this.emptyGuid;
  }

  private isManualEntry(entry: UserChangeHistory): boolean {
    const importHistoryId = (entry.importHistoryId || '').trim();
    return !entry.status && (!importHistoryId || importHistoryId === this.emptyGuid);
  }

  private getEntryTimestamp(entry: UserChangeHistory): number {
    const sourceDate = entry.createdAt || entry.importDate;
    return sourceDate ? new Date(sourceDate).getTime() : 0;
  }

  formatDateTime(date: Date | string | undefined): string {
    if (!date) return 'N/A';
    return new Date(date).toLocaleString('ro-RO');
  }

  getConflictStatusColor(status?: string | null): string {
    return status === 'accepted'
      ? 'bg-green-500/10 text-green-700 border-green-500/20'
      : 'bg-red-500/10 text-red-700 border-red-500/20';
  }

  formatConflictField(field: string): string {
    if (!field) return 'Unknown Field';
    const normalizedField = field.trim().toLowerCase();
    switch (normalizedField) {
      case 'firstname':
        return 'First Name';
      case 'lastname':
        return 'Last Name';
      case 'departmentname':
        return 'Department';
      case 'assignedtoname':
        return 'Line Manager';
      default:
        return field.replace(/([A-Z])/g, ' $1').replace(/^./, str => str.toUpperCase());
    }
  }

  getRoleBadgeColor(role: UserRole): string {
    return role === UserRole.LineManager
      ? 'bg-purple-500/10 text-purple-700 border-purple-500/20'
      : 'bg-blue-500/10 text-blue-700 border-blue-500/20';
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

  navigateToDashboard(): void {
    this.router.navigate(['/dashboard']);
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

  viewSSMSUForm(user: User, event?: Event): void {
    if (event) {
      event.stopPropagation();
    }
    this.router.navigate(['/employees', user.id, 'ssm-su']);
  }

  // Edit and Delete Modal State and Logic
  isEditModalOpen = false;
  isDeleteModalOpen = false;

  editForm = {
    firstName: '',
    lastName: '',
    email: '',
    departmentId: '',
    function: '',
    role: UserRole.Employee,
    assignedToId: ''
  };

  allUsers: User[] = [];
  availableFunctions: string[] = [];

  openEditModal(user: User, event: Event): void {
    event.stopPropagation();
    this.selectedUser = user;
    this.editForm = {
      firstName: user.firstName,
      lastName: user.lastName,
      email: user.email,
      departmentId: user.departmentId,
      function: user.function || '',
      role: user.role || UserRole.Employee,
      assignedToId: user.assignedToId || ''
    };

    // Store all users for the Line Manager dropdown
    this.users$.pipe(take(1)).subscribe(users => {
      this.allUsers = users;
      this.loadFunctionsForDepartment(this.editForm.departmentId, this.editForm.function);
    });
  }

  closeEditModal(): void {
    this.isEditModalOpen = false;
    this.availableFunctions = [];
  }

  onDepartmentChange(departmentId: string): void {
    this.editForm.assignedToId = '';
    this.loadFunctionsForDepartment(departmentId);
  }

  private loadFunctionsForDepartment(departmentId: string, preferredFunction?: string): void {
    this.userSyncService.getFunctionsByDepartmentId(departmentId).subscribe(functions => {
      this.availableFunctions = functions;

      const preferred = preferredFunction?.trim() || '';
      const preferredMatch = preferred
        ? this.availableFunctions.find(fn => fn.trim().toLowerCase() === preferred.toLowerCase())
        : undefined;

      if (preferredMatch) {
        this.editForm.function = preferredMatch;
      } else {
        this.editForm.function = '';
      }

      this.isEditModalOpen = true;
    });
  }

  getAvailableLineManagers(): User[] {
    // Exclude the current user from being their own line manager
    // Only include line managers from the selected department in the edit form
    return this.allUsers.filter(u =>
      u.role === UserRole.LineManager &&
      u.departmentId === this.editForm.departmentId &&
      (!this.selectedUser || u.id !== this.selectedUser.id)
    );
  }

  saveUser(): void {
    if (!this.selectedUser) return;

    const payload = {
      firstName: this.editForm.firstName,
      lastName: this.editForm.lastName,
      email: this.editForm.email,
      departmentId: this.editForm.departmentId,
      function: this.editForm.function || null,
      roleName: this.editForm.role === UserRole.LineManager ? 'Line Manager' : 'Basic User',
      assignedToId: this.editForm.role === UserRole.LineManager ? null : (this.editForm.assignedToId || null)
    };

    if (this.editForm.role === UserRole.Employee && !payload.assignedToId) {
      alert('Please select a Line Manager for the Employee.');
      return;
    }

    this.userSyncService.updateUser(this.selectedUser.id, payload).subscribe({
      next: () => {
        this.closeEditModal();
        // Since loadUsers is called in service, users$ will emit updated list
        // Update the selectedUser with the new details locally to rapidly refresh UI
        if (this.selectedUser) {
          this.selectedUser.firstName = payload.firstName;
          this.selectedUser.lastName = payload.lastName;
          this.selectedUser.email = payload.email;
          this.selectedUser.departmentId = payload.departmentId;
          this.selectedUser.function = payload.function || 'unknown';
          // The re-evaluation of Role/DepartmentName will happen when users$ emits automatically in selectUser trigger
          this.users$.pipe(take(1)).subscribe(users => {
            const updated = users.find(u => u.id === this.selectedUser?.id);
            if (updated) this.selectedUser = updated;
          });
        }
      },
      error: (err) => {
        console.error('Error updating user:', err);
        alert(err.error?.message || 'Error updating user');
      }
    });
  }

  openDeleteModal(user: User, event: Event): void {
    event.stopPropagation();
    this.selectedUser = user;
    this.isDeleteModalOpen = true;
  }

  closeDeleteModal(): void {
    this.isDeleteModalOpen = false;
  }

  confirmDelete(): void {
    if (!this.selectedUser) return;

    this.userSyncService.deleteUser(this.selectedUser.id).subscribe({
      next: () => {
        this.closeDeleteModal();
        this.closeDetails(); // Close the detail panel since the user is gone
      },
      error: (err) => {
        console.error('Error deleting user:', err);
        alert(err.error?.message || 'Error deleting user');
      }
    });
  }
}
