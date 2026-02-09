import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Observable, combineLatest, BehaviorSubject } from 'rxjs';
import { map } from 'rxjs/operators';
import { UserSyncService } from '../../services/user-sync.service';
import { User, UserRole, Department } from '../../models/csv-sync.model';
import { PaginationComponent } from '../pagination/pagination.component';

@Component({
  selector: 'app-users-list',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './users-list.component.html',
  styleUrls: ['./users-list.component.css']
})
export class UsersListComponent implements OnInit {
  users$!: Observable<User[]>;
  paginatedUsers$!: Observable<User[]>;
  departments$!: Observable<Department[]>;
  
  private currentPage$ = new BehaviorSubject<number>(1);
  pageSize = 15;
  totalItems = 0;
  
  get currentPage(): number { return this.currentPage$.value; }
  set currentPage(value: number) { this.currentPage$.next(value); }
  
  private searchQuery$ = new BehaviorSubject<string>('');
  private selectedDepartment$ = new BehaviorSubject<string>('all');
  private selectedRole$ = new BehaviorSubject<UserRole | 'all'>('all');
  
  get searchQuery(): string { return this.searchQuery$.value; }
  set searchQuery(value: string) { this.searchQuery$.next(value); }
  
  get selectedDepartment(): string { return this.selectedDepartment$.value; }
  set selectedDepartment(value: string) { this.selectedDepartment$.next(value); }
  
  get selectedRole(): UserRole | 'all' { return this.selectedRole$.value; }
  set selectedRole(value: UserRole | 'all') { this.selectedRole$.next(value); }
  
  UserRole = UserRole;

  constructor(
    private userSyncService: UserSyncService,
    private router: Router,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    this.users$ = this.userSyncService.users$;
    this.departments$ = this.userSyncService.getDepartments();
    
    // Check for department filter from query params
    this.route.queryParams.subscribe(params => {
      if (params['department']) {
        this.selectedDepartment = params['department'];
      }
    });
    
    this.paginatedUsers$ = combineLatest([
      this.users$,
      this.searchQuery$,
      this.selectedDepartment$,
      this.selectedRole$,
      this.currentPage$
    ]).pipe(
      map(([users, searchQuery, selectedDepartment, selectedRole, currentPage]) => {
        // Filter users
        let filtered = users.filter(user => {
          const fullName = `${user.firstName} ${user.lastName}`.toLowerCase();
          const matchesSearch = !searchQuery || 
            fullName.includes(searchQuery.toLowerCase()) ||
            user.email.toLowerCase().includes(searchQuery.toLowerCase());
          const matchesDepartment = selectedDepartment === 'all' || 
            user.departmentName === selectedDepartment;
          const matchesRole = selectedRole === 'all' || 
            user.role === selectedRole;
          return matchesSearch && matchesDepartment && matchesRole;
        });
        
        this.totalItems = filtered.length;
        
        // Paginate
        const startIndex = (currentPage - 1) * this.pageSize;
        return filtered.slice(startIndex, startIndex + this.pageSize);
      })
    );
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
}
