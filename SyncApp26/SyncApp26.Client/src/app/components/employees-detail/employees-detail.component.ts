import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Observable, combineLatest, BehaviorSubject } from 'rxjs';
import { map } from 'rxjs/operators';
import { UserSyncService } from '../../services/user-sync.service';
import { User, UserRole, Department, ImportConflictHistory } from '../../models/csv-sync.model';
import { PaginationComponent } from '../pagination/pagination.component';

@Component({
  selector: 'app-employees-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './employees-detail.component.html',
  styleUrls: ['./employees-detail.component.css']
})
export class EmployeesDetailComponent implements OnInit {
  users$!: Observable<User[]>;
  paginatedUsers$!: Observable<User[]>;
  departments$!: Observable<Department[]>;
  selectedUser: User | null = null;
  importConflicts: ImportConflictHistory[] = [];
  conflictsLoading = false;
  conflictsError = '';
  
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
    private router: Router,
    private route: ActivatedRoute
  ) {}

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
  }

  closeDetails(): void {
    this.selectedUser = null;
    this.importConflicts = [];
    this.conflictsError = '';
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

  getConflictGroupsByImport(): Array<{ importHistoryId: string; importDate?: string; importFileName?: string; conflicts: ImportConflictHistory[] }> {
    const groups = new Map<string, { importHistoryId: string; importDate?: string; importFileName?: string; conflicts: ImportConflictHistory[] }>();
    const sorted = this.importConflicts
      .slice()
      .sort((a, b) => {
        const aTime = a.importDate ? new Date(a.importDate).getTime() : 0;
        const bTime = b.importDate ? new Date(b.importDate).getTime() : 0;
        return bTime - aTime;
      });

    sorted.forEach(conflict => {
      const key = conflict.importHistoryId;
      if (!groups.has(key)) {
        groups.set(key, {
          importHistoryId: conflict.importHistoryId,
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

  getConflictStatusColor(status: string): string {
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
}
