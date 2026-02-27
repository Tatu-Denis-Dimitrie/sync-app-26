import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { forkJoin } from 'rxjs';
import { UserSyncService } from '../../services/user-sync.service';
import { AuthenticationService } from '../../services/authentication.service';
import { ImportConflictHistory, ImportHistoryItem, User } from '../../models/csv-sync.model';

@Component({
  selector: 'app-import-history',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './import-history.component.html',
  styleUrl: './import-history.component.css'
})
export class ImportHistoryComponent implements OnInit {
  histories: ImportHistoryItem[] = [];
  conflictsByHistory = new Map<string, ImportConflictHistory[]>();
  expandedHistory = new Set<string>();
  usersById = new Map<string, User>();
  loading = false;
  error = '';
  searchQuery = '';
  sortOrder: 'asc' | 'desc' = 'desc';

  constructor(
    private userSyncService: UserSyncService,
    private authService: AuthenticationService,
    private router: Router
  ) {}

  logout(): void {
    this.authService.logout();
  }

  ngOnInit(): void {
    this.loadUsers();
    this.loadImportHistory();
  }

  loadUsers(): void {
    this.userSyncService.getUsers().subscribe({
      next: users => {
        this.usersById = new Map(users.map(user => [user.id, user]));
      }
    });
  }

  loadImportHistory(): void {
    this.loading = true;
    this.error = '';
    this.userSyncService.getImportHistories().subscribe({
      next: histories => {
        this.histories = histories;
        this.loadConflictsForHistories(this.histories);
      },
      error: () => {
        this.loading = false;
        this.error = 'Failed to load import history.';
      }
    });
  }

  loadConflictsForHistories(histories: ImportHistoryItem[]): void {
    if (histories.length === 0) {
      this.loading = false;
      return;
    }

    const requests = histories.map(history =>
      this.userSyncService.getImportConflictsByImportHistoryId(history.id)
    );

    forkJoin(requests).subscribe({
      next: results => {
        results.forEach((conflicts, index) => {
          this.conflictsByHistory.set(histories[index].id, conflicts);
        });
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.error = 'Failed to load import conflicts.';
      }
    });
  }

  toggleHistory(historyId: string): void {
    if (this.expandedHistory.has(historyId)) {
      this.expandedHistory.delete(historyId);
    } else {
      this.expandedHistory.add(historyId);
    }
  }

  isExpanded(historyId: string): boolean {
    return this.expandedHistory.has(historyId);
  }

  getConflicts(historyId: string): ImportConflictHistory[] {
    return this.conflictsByHistory.get(historyId) ?? [];
  }

  getFilteredHistories(): ImportHistoryItem[] {
    const query = this.searchQuery.trim().toLowerCase();
    const filtered = query
      ? this.histories.filter(history =>
          (history.fileName || '').toLowerCase().includes(query))
      : this.histories.slice();

    return filtered.sort((a, b) => {
      const aTime = a.importDate ? new Date(a.importDate).getTime() : 0;
      const bTime = b.importDate ? new Date(b.importDate).getTime() : 0;
      return this.sortOrder === 'asc' ? aTime - bTime : bTime - aTime;
    });
  }

  toggleSortOrder(): void {
    this.sortOrder = this.sortOrder === 'asc' ? 'desc' : 'asc';
  }

  getConflictGroups(historyId: string): Array<{ userId: string; userName: string; conflicts: ImportConflictHistory[] }>
  {
    const conflicts = this.getConflicts(historyId);
    const groups = new Map<string, ImportConflictHistory[]>();

    conflicts.forEach(conflict => {
      if (!groups.has(conflict.userId)) {
        groups.set(conflict.userId, []);
      }
      groups.get(conflict.userId)?.push(conflict);
    });

    return Array.from(groups.entries()).map(([userId, userConflicts]) => {
      const user = this.usersById.get(userId);
      const userName = user ? `${user.firstName} ${user.lastName}` : 'Unknown User';
      return { userId, userName, conflicts: userConflicts };
    });
  }

  formatDate(date: string): string {
    if (!date) return 'N/A';
    return new Date(date).toLocaleDateString('ro-RO');
  }

  getStatusColor(status: string): string {
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
      case 'linemanager':
        return 'Line Manager';
      default:
        return field.replace(/([A-Z])/g, ' $1').replace(/^./, str => str.toUpperCase());
    }
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
