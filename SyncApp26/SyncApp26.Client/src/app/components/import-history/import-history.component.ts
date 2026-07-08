import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { UserSyncService } from '../../services/user-sync.service';
import { AuthenticationService } from '../../services/authentication.service';
import { UserChangeHistory, User } from '../../models/csv-sync.model';
import { formatDateTime as formatDateTimeUtil } from '../../shared/utils/date-format.util';

interface ImportHistoryGroup {
  importHistoryId: string;
  importDate?: string;
  importFileName?: string;
  conflicts: UserChangeHistory[];
}

interface ManualDayGroup {
  dayKey: string;
  dayDate?: string;
  changes: UserChangeHistory[];
}

interface DataChangeRequestDayGroup {
  dayKey: string;
  dayDate?: string;
  changes: UserChangeHistory[];
}

@Component({
  selector: 'app-import-history',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './import-history.component.html',
  styleUrl: './import-history.component.css'
})
export class ImportHistoryComponent implements OnInit {
  private readonly emptyGuid = '00000000-0000-0000-0000-000000000000';

  allChanges: UserChangeHistory[] = [];
  expandedImportHistory = new Set<string>();
  expandedManualDays = new Set<string>();
  expandedDataChangeDays = new Set<string>();
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
    this.loadUserChangeHistory();
  }

  loadUsers(): void {
    this.userSyncService.getUsers().subscribe({
      next: users => {
        this.usersById = new Map(users.map(user => [user.id, user]));
      }
    });
  }

  loadUserChangeHistory(): void {
    this.loading = true;
    this.error = '';
    this.userSyncService.getAllUserChangeHistories().subscribe({
      next: changes => {
        this.allChanges = changes ?? [];
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.error = 'Failed to load user change history.';
      }
    });
  }

  private hasRealImportHistoryId(change: UserChangeHistory): boolean {
    const importHistoryId = (change.importHistoryId || '').trim();
    return !!importHistoryId && importHistoryId !== this.emptyGuid;
  }

  private getChangeDate(change: UserChangeHistory): string | undefined {
    return change.createdAt || change.importDate;
  }

  private getChangeTime(change: UserChangeHistory): number {
    const date = this.getChangeDate(change);
    return date ? new Date(date).getTime() : 0;
  }

  private getDayKey(date?: string): string {
    if (!date) {
      return 'unknown';
    }

    const parsed = new Date(date);
    if (isNaN(parsed.getTime())) {
      return 'unknown';
    }

    const year = parsed.getFullYear();
    const month = String(parsed.getMonth() + 1).padStart(2, '0');
    const day = String(parsed.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  isImportConflict(change: UserChangeHistory): boolean {
    return this.hasRealImportHistoryId(change) && !!change.status;
  }

  isManualChange(change: UserChangeHistory): boolean {
    return !this.hasRealImportHistoryId(change) && !change.status;
  }

  isDataChangeRequest(change: UserChangeHistory): boolean {
    return !this.hasRealImportHistoryId(change) && !!change.status;
  }

  toggleImportGroup(historyId: string): void {
    if (this.expandedImportHistory.has(historyId)) {
      this.expandedImportHistory.delete(historyId);
    } else {
      this.expandedImportHistory.add(historyId);
    }
  }

  isImportGroupExpanded(historyId: string): boolean {
    return this.expandedImportHistory.has(historyId);
  }

  toggleManualDay(dayKey: string): void {
    if (this.expandedManualDays.has(dayKey)) {
      this.expandedManualDays.delete(dayKey);
    } else {
      this.expandedManualDays.add(dayKey);
    }
  }

  isManualDayExpanded(dayKey: string): boolean {
    return this.expandedManualDays.has(dayKey);
  }

  toggleDataChangeDay(dayKey: string): void {
    if (this.expandedDataChangeDays.has(dayKey)) {
      this.expandedDataChangeDays.delete(dayKey);
    } else {
      this.expandedDataChangeDays.add(dayKey);
    }
  }

  isDataChangeDayExpanded(dayKey: string): boolean {
    return this.expandedDataChangeDays.has(dayKey);
  }

  getUserName(userId: string): string {
    const user = this.usersById.get(userId);
    return user ? `${user.firstName} ${user.lastName}` : 'Unknown User';
  }

  getFilteredImportGroups(): ImportHistoryGroup[] {
    const query = this.searchQuery.trim().toLowerCase();
    const groups = new Map<string, ImportHistoryGroup>();

    this.allChanges
      .filter(change => this.isImportConflict(change))
      .forEach(change => {
        const key = change.importHistoryId!;
        if (!groups.has(key)) {
          groups.set(key, {
            importHistoryId: key,
            importDate: change.importDate,
            importFileName: change.importFileName,
            conflicts: []
          });
        }

        const group = groups.get(key)!;
        group.conflicts.push(change);

        if (!group.importDate && change.importDate) {
          group.importDate = change.importDate;
        }
        if (!group.importFileName && change.importFileName) {
          group.importFileName = change.importFileName;
        }
      });

    let result = Array.from(groups.values());

    if (query) {
      result = result.filter(group =>
        (group.importFileName || '').toLowerCase().includes(query)
      );
    }

    return result.sort((a, b) => {
      const aTime = a.importDate ? new Date(a.importDate).getTime() : 0;
      const bTime = b.importDate ? new Date(b.importDate).getTime() : 0;
      return this.sortOrder === 'asc' ? aTime - bTime : bTime - aTime;
    });
  }

  getFilteredManualDayGroups(): ManualDayGroup[] {
    const query = this.searchQuery.trim().toLowerCase();
    const groups = new Map<string, ManualDayGroup>();

    this.allChanges
      .filter(change => this.isManualChange(change))
      .forEach(change => {
        const date = this.getChangeDate(change);
        const dayKey = this.getDayKey(date);

        if (!groups.has(dayKey)) {
          groups.set(dayKey, {
            dayKey,
            dayDate: date,
            changes: []
          });
        }

        const group = groups.get(dayKey)!;
        group.changes.push(change);

        if (date && (!group.dayDate || this.getChangeTime(change) > new Date(group.dayDate).getTime())) {
          group.dayDate = date;
        }
      });

    let result = Array.from(groups.values()).map(group => ({
      ...group,
      changes: group.changes.slice().sort((a, b) => this.getChangeTime(b) - this.getChangeTime(a))
    }));

    if (query) {
      result = result.filter(group => {
        const dateLabel = this.formatDate(group.dayDate).toLowerCase();
        const hasMatchingDate = dateLabel.includes(query);
        const hasMatchingChange = group.changes.some(change =>
          this.getUserName(change.userId).toLowerCase().includes(query) ||
          this.formatConflictField(change.fieldName).toLowerCase().includes(query)
        );
        return hasMatchingDate || hasMatchingChange;
      });
    }

    return result.sort((a, b) => {
      const aTime = a.dayDate ? new Date(a.dayDate).getTime() : 0;
      const bTime = b.dayDate ? new Date(b.dayDate).getTime() : 0;
      return this.sortOrder === 'asc' ? aTime - bTime : bTime - aTime;
    });
  }

  getFilteredDataChangeRequestGroups(): DataChangeRequestDayGroup[] {
    const query = this.searchQuery.trim().toLowerCase();
    const groups = new Map<string, DataChangeRequestDayGroup>();

    this.allChanges
      .filter(change => this.isDataChangeRequest(change))
      .forEach(change => {
        const date = this.getChangeDate(change);
        const dayKey = this.getDayKey(date);

        if (!groups.has(dayKey)) {
          groups.set(dayKey, {
            dayKey,
            dayDate: date,
            changes: []
          });
        }

        const group = groups.get(dayKey)!;
        group.changes.push(change);

        if (date && (!group.dayDate || this.getChangeTime(change) > new Date(group.dayDate).getTime())) {
          group.dayDate = date;
        }
      });

    let result = Array.from(groups.values()).map(group => ({
      ...group,
      changes: group.changes.slice().sort((a, b) => this.getChangeTime(b) - this.getChangeTime(a))
    }));

    if (query) {
      result = result.filter(group => {
        const dateLabel = this.formatDate(group.dayDate).toLowerCase();
        const hasMatchingDate = dateLabel.includes(query);
        const hasMatchingChange = group.changes.some(change =>
          this.getUserName(change.userId).toLowerCase().includes(query) ||
          this.formatConflictField(change.fieldName).toLowerCase().includes(query)
        );
        return hasMatchingDate || hasMatchingChange;
      });
    }

    return result.sort((a, b) => {
      const aTime = a.dayDate ? new Date(a.dayDate).getTime() : 0;
      const bTime = b.dayDate ? new Date(b.dayDate).getTime() : 0;
      return this.sortOrder === 'asc' ? aTime - bTime : bTime - aTime;
    });
  }

  toggleSortOrder(): void {
    this.sortOrder = this.sortOrder === 'asc' ? 'desc' : 'asc';
  }

  getConflictGroups(conflicts: UserChangeHistory[]): Array<{ userId: string; userName: string; conflicts: UserChangeHistory[] }> {
    const groups = new Map<string, UserChangeHistory[]>();

    conflicts.forEach(conflict => {
      if (!groups.has(conflict.userId)) {
        groups.set(conflict.userId, []);
      }
      groups.get(conflict.userId)?.push(conflict);
    });

    return Array.from(groups.entries()).map(([userId, userConflicts]) => {
      const userName = this.getUserName(userId);
      return { userId, userName, conflicts: userConflicts };
    });
  }

  formatDate(date?: string): string {
    if (!date) return 'N/A';
    return new Date(date).toLocaleDateString('ro-RO');
  }

  formatDateTime(date?: string): string {
    return formatDateTimeUtil(date);
  }

  getStatusColor(status?: string | null): string {
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

  navigateToSignature(): void {
    this.router.navigate(['/admin-signature']);
  }

  
  navigateToDataRequests(): void {
    this.router.navigate(['/data-requests']);
  }
navigateToDocuments(): void {
    this.router.navigate(['/documents']);
  }
}
