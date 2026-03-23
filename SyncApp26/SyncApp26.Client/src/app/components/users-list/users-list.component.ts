import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Observable, combineLatest, BehaviorSubject } from 'rxjs';
import { map, take } from 'rxjs/operators';
import { UserSyncService } from '../../services/user-sync.service';
import { AuthenticationService } from '../../services/authentication.service';
import { NotificationService } from '../../services/notification.service';
import { User, UserRole, Department } from '../../models/csv-sync.model';
import { PaginationComponent } from '../pagination/pagination.component';

interface SignatureStats {
  total: number;
  ssmSigned: number;
  suSigned: number;
  bothSigned: number;
  unsigned: number;
}

interface DepartmentSignatureStats extends SignatureStats {
  departmentName: string;
}

interface LineManagerTeamStats {
  managerId: string;
  managerName: string;
  departmentName: string;
  totalSubordinates: number;
  missingSsm: number;
  missingSu: number;
  missingAny: number;
  fullySignedBoth: number;
}

type UserSignatureFilter = 'all' | 'ssm-signed' | 'su-signed' | 'both-signed' | 'unsigned';
type SortDirection = 'asc' | 'desc';
type DepartmentSortKey = keyof DepartmentSignatureStats;
type ManagerSortKey = keyof LineManagerTeamStats;

@Component({
  selector: 'app-users-list',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './users-list.component.html',
  styleUrls: ['./users-list.component.css']
})
export class UsersListComponent implements OnInit {
  users$!: Observable<User[]>;
  filteredUsers$!: Observable<User[]>;
  paginatedUsers$!: Observable<User[]>;
  departments$!: Observable<Department[]>;
  signatureStats$!: Observable<SignatureStats>;
  departmentSignatureStats$!: Observable<DepartmentSignatureStats[]>;
  sortedDepartmentSignatureStats$!: Observable<DepartmentSignatureStats[]>;
  lineManagerTeamStats$!: Observable<LineManagerTeamStats[]>;
  sortedLineManagerTeamStats$!: Observable<LineManagerTeamStats[]>;

  private currentPage$ = new BehaviorSubject<number>(1);
  pageSize = 15;
  totalItems = 0;

  get currentPage(): number { return this.currentPage$.value; }
  set currentPage(value: number) { this.currentPage$.next(value); }

  private searchQuery$ = new BehaviorSubject<string>('');
  private selectedDepartment$ = new BehaviorSubject<string>('all');
  private selectedRole$ = new BehaviorSubject<UserRole | 'all'>('all');
  private selectedSignature$ = new BehaviorSubject<UserSignatureFilter>('all');
  private departmentSortKey$ = new BehaviorSubject<DepartmentSortKey>('departmentName');
  private departmentSortDirection$ = new BehaviorSubject<SortDirection>('asc');
  private managerSortKey$ = new BehaviorSubject<ManagerSortKey>('missingAny');
  private managerSortDirection$ = new BehaviorSubject<SortDirection>('desc');

  get searchQuery(): string { return this.searchQuery$.value; }
  set searchQuery(value: string) { this.searchQuery$.next(value); }

  get selectedDepartment(): string { return this.selectedDepartment$.value; }
  set selectedDepartment(value: string) { this.selectedDepartment$.next(value); }

  get selectedRole(): UserRole | 'all' { return this.selectedRole$.value; }
  set selectedRole(value: UserRole | 'all') { this.selectedRole$.next(value); }

  get selectedSignature(): UserSignatureFilter { return this.selectedSignature$.value; }
  set selectedSignature(value: UserSignatureFilter) { this.selectedSignature$.next(value); }

  UserRole = UserRole;
  isPendingUsersModalOpen = false;
  pendingUsersModalTitle = '';
  pendingUsersForModal: User[] = [];

  constructor(
    private userSyncService: UserSyncService,
    private authService: AuthenticationService,
    private router: Router,
    private route: ActivatedRoute,
    private notificationService: NotificationService
  ) { }

  logout(): void {
    this.authService.logout();
  }

  ngOnInit(): void {
    this.users$ = this.userSyncService.users$;
    this.departments$ = this.userSyncService.getDepartments();

    this.users$.subscribe(users => {
      this.allUsers = users;
    });

    // Check for department filter from query params
    this.route.queryParams.subscribe(params => {
      if (params['department']) {
        this.selectedDepartment = params['department'];
      }

      const signatureParam = params['signature'] as UserSignatureFilter | undefined;
      if (signatureParam && ['all', 'ssm-signed', 'su-signed', 'both-signed', 'unsigned'].includes(signatureParam)) {
        this.selectedSignature = signatureParam;
      }
    });

    this.filteredUsers$ = combineLatest([
      this.users$,
      this.searchQuery$,
      this.selectedDepartment$,
      this.selectedRole$,
      this.selectedSignature$
    ]).pipe(
      map(([users, searchQuery, selectedDepartment, selectedRole, selectedSignature]) => {
        return users.filter(user => {
          const fullName = `${user.firstName} ${user.lastName}`.toLowerCase();
          const matchesSearch = !searchQuery ||
            fullName.includes(searchQuery.toLowerCase()) ||
            user.email.toLowerCase().includes(searchQuery.toLowerCase());
          const matchesDepartment = selectedDepartment === 'all' ||
            user.departmentName === selectedDepartment;
          const matchesRole = selectedRole === 'all' ||
            user.role === selectedRole;

          const matchesSignature =
            selectedSignature === 'all' ||
            (selectedSignature === 'ssm-signed' && !!user.hasSignedSsm) ||
            (selectedSignature === 'su-signed' && !!user.hasSignedSu) ||
            (selectedSignature === 'both-signed' && !!user.hasSignedSsm && !!user.hasSignedSu) ||
            (selectedSignature === 'unsigned' && !user.hasSignedSsm && !user.hasSignedSu);

          return matchesSearch && matchesDepartment && matchesRole && matchesSignature;
        });
      })
    );

    this.signatureStats$ = this.filteredUsers$.pipe(
      map(users => this.computeSignatureStats(users))
    );

    this.departmentSignatureStats$ = this.filteredUsers$.pipe(
      map(users => {
        const grouped = new Map<string, User[]>();

        users.forEach(user => {
          const key = user.departmentName || 'Unknown';
          if (!grouped.has(key)) {
            grouped.set(key, []);
          }
          grouped.get(key)!.push(user);
        });

        return Array.from(grouped.entries())
          .map(([departmentName, deptUsers]) => ({
            departmentName,
            ...this.computeSignatureStats(deptUsers)
          }))
          .sort((a, b) => a.departmentName.localeCompare(b.departmentName));
      })
    );

    this.sortedDepartmentSignatureStats$ = combineLatest([
      this.departmentSignatureStats$,
      this.departmentSortKey$,
      this.departmentSortDirection$
    ]).pipe(
      map(([rows, sortKey, sortDirection]) => this.sortRows(rows, sortKey, sortDirection))
    );

    this.lineManagerTeamStats$ = combineLatest([
      this.users$,
      this.selectedDepartment$
    ]).pipe(
      map(([users, selectedDepartment]) => {
        const scopedUsers = selectedDepartment === 'all'
          ? users
          : users.filter(u => u.departmentName === selectedDepartment);

        const lineManagers = scopedUsers.filter(u => u.role === UserRole.LineManager);

        return lineManagers
          .map(manager => {
            const subordinates = scopedUsers.filter(u => u.assignedToId === manager.id);
            const missingSsm = subordinates.filter(u => !u.hasSignedSsm).length;
            const missingSu = subordinates.filter(u => !u.hasSignedSu).length;
            const fullySignedBoth = subordinates.filter(u => !!u.hasSignedSsm && !!u.hasSignedSu).length;

            return {
              managerId: manager.id,
              managerName: `${manager.firstName} ${manager.lastName}`,
              departmentName: manager.departmentName,
              totalSubordinates: subordinates.length,
              missingSsm,
              missingSu,
              missingAny: subordinates.filter(u => !u.hasSignedSsm || !u.hasSignedSu).length,
              fullySignedBoth
            };
          })
          .filter(row => row.totalSubordinates > 0)
          .sort((a, b) => b.missingAny - a.missingAny || a.managerName.localeCompare(b.managerName));
      })
    );

    this.sortedLineManagerTeamStats$ = combineLatest([
      this.lineManagerTeamStats$,
      this.managerSortKey$,
      this.managerSortDirection$
    ]).pipe(
      map(([rows, sortKey, sortDirection]) => this.sortRows(rows, sortKey, sortDirection))
    );

    this.paginatedUsers$ = combineLatest([
      this.filteredUsers$,
      this.currentPage$
    ]).pipe(
      map(([filtered, currentPage]) => {

        this.totalItems = filtered.length;

        // Paginate
        const startIndex = (currentPage - 1) * this.pageSize;
        return filtered.slice(startIndex, startIndex + this.pageSize);
      })
    );
  }

  private computeSignatureStats(users: User[]): SignatureStats {
    return {
      total: users.length,
      ssmSigned: users.filter(u => !!u.hasSignedSsm).length,
      suSigned: users.filter(u => !!u.hasSignedSu).length,
      bothSigned: users.filter(u => !!u.hasSignedSsm && !!u.hasSignedSu).length,
      unsigned: users.filter(u => !u.hasSignedSsm && !u.hasSignedSu).length
    };
  }

  toggleDepartmentTableSort(key: DepartmentSortKey): void {
    const currentKey = this.departmentSortKey$.value;
    const currentDirection = this.departmentSortDirection$.value;

    if (currentKey === key) {
      this.departmentSortDirection$.next(currentDirection === 'asc' ? 'desc' : 'asc');
      return;
    }

    this.departmentSortKey$.next(key);
    this.departmentSortDirection$.next(key === 'departmentName' ? 'asc' : 'desc');
  }

  toggleManagerTableSort(key: ManagerSortKey): void {
    const currentKey = this.managerSortKey$.value;
    const currentDirection = this.managerSortDirection$.value;

    if (currentKey === key) {
      this.managerSortDirection$.next(currentDirection === 'asc' ? 'desc' : 'asc');
      return;
    }

    this.managerSortKey$.next(key);
    this.managerSortDirection$.next(key === 'managerName' || key === 'departmentName' ? 'asc' : 'desc');
  }

  getDepartmentSortIndicator(key: DepartmentSortKey): string {
    if (this.departmentSortKey$.value !== key) return '';
    return this.departmentSortDirection$.value === 'asc' ? ' ▲' : ' ▼';
  }

  getManagerSortIndicator(key: ManagerSortKey): string {
    if (this.managerSortKey$.value !== key) return '';
    return this.managerSortDirection$.value === 'asc' ? ' ▲' : ' ▼';
  }

  private sortRows<T>(rows: T[], sortKey: keyof T, sortDirection: SortDirection): T[] {
    const sorted = [...rows].sort((a, b) => {
      const aValue = a[sortKey];
      const bValue = b[sortKey];

      if (typeof aValue === 'number' && typeof bValue === 'number') {
        return aValue - bValue;
      }

      return String(aValue ?? '').localeCompare(String(bValue ?? ''));
    });

    return sortDirection === 'asc' ? sorted : sorted.reverse();
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

  viewUserDetails(userId: string): void {
    this.router.navigate(['/employees', userId]);
  }

  openPendingUsersForManager(row: LineManagerTeamStats, event?: Event): void {
    event?.stopPropagation();

    const scopedUsers = this.selectedDepartment === 'all'
      ? this.allUsers
      : this.allUsers.filter(u => u.departmentName === this.selectedDepartment);

    this.pendingUsersForModal = scopedUsers
      .filter(u => u.assignedToId === row.managerId)
      .filter(u => !u.hasSignedSsm || !u.hasSignedSu)
      .sort((a, b) => `${a.firstName} ${a.lastName}`.localeCompare(`${b.firstName} ${b.lastName}`));

    this.pendingUsersModalTitle = `Users with pending signatures for ${row.managerName}`;
    this.isPendingUsersModalOpen = true;
  }

  closePendingUsersModal(): void {
    this.isPendingUsersModalOpen = false;
    this.pendingUsersModalTitle = '';
    this.pendingUsersForModal = [];
  }

  getRoleBadgeColor(role: UserRole): string {
    return role === UserRole.LineManager
      ? 'bg-purple-500/10 text-purple-700 border-purple-500/20'
      : 'bg-blue-500/10 text-blue-700 border-blue-500/20';
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

  
  navigateToDataRequests(): void {
    this.router.navigate(['/data-requests']);
  }

  navigateToDocuments(): void {
    this.router.navigate(['/documents']);
  }

  // Edit and Delete Modal State and Logic
  isEditModalOpen = false;
  isDeleteModalOpen = false;
  selectedUser: User | null = null;

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
    this.selectedUser = null;
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
    this.selectedUser = null;
  }

  confirmDelete(): void {
    if (!this.selectedUser) return;

    this.userSyncService.deleteUser(this.selectedUser.id).subscribe({
      next: () => {
        this.closeDeleteModal();
      },
      error: (err) => {
        console.error('Error deleting user:', err);
        alert(err.error?.message || 'Error deleting user');
      }
    });
  }
}
