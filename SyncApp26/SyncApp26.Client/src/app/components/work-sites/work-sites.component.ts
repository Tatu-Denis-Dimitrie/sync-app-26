import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { WorkSiteService } from '../../services/work-site.service';
import { UserSyncService } from '../../services/user-sync.service';
import { WorkSite, User } from '../../models/csv-sync.model';
import { TranslationService } from '../../services/translation.service';
import { TranslatePipe } from '../../shared/pipes/translate.pipe';

interface WorkSiteStats {
  employeeCount: number;
  ssmSigned: number;
  suSigned: number;
  bothSigned: number;
  unsigned: number;
  /** Share of the site's roster that has signed, 0-100, for the fill tracks. */
  ssmPct: number;
  suPct: number;
  /** Share fully covered on both documents — the headline number each row leads with. */
  bothPct: number;
}

const EMPTY_STATS: WorkSiteStats = {
  employeeCount: 0, ssmSigned: 0, suSigned: 0, bothSigned: 0, unsigned: 0, ssmPct: 0, suPct: 0, bothPct: 0
};

@Component({
  selector: 'app-work-sites',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe],
  templateUrl: './work-sites.component.html',
  styleUrls: ['./work-sites.component.css']
})
export class WorkSitesComponent implements OnInit {
  workSites: WorkSite[] = [];
  deletedWorkSites: WorkSite[] = [];
  errorMessage: string | null = null;
  searchQuery = '';
  showDeleted = false;

  private allUsers: User[] = [];

  isAddModalOpen = false;
  newWorkSiteName = '';

  isEditModalOpen = false;
  workSiteToEdit: WorkSite | null = null;
  editWorkSiteName = '';

  isDeleteModalOpen = false;
  workSiteToDelete: WorkSite | null = null;

  isRestoreModalOpen = false;
  workSiteToRestore: WorkSite | null = null;

  isEmployeesModalOpen = false;
  workSiteForEmployees: WorkSite | null = null;

  constructor(private workSiteService: WorkSiteService, private userSyncService: UserSyncService, private router: Router, private translationService: TranslationService) {}

  tOrg(key: string): string {
    return this.translationService.translate('Organization', key);
  }

  siteWord(count: number): string {
    return this.tOrg(count === 1 ? 'workSites.headerSite' : 'workSites.headerSites');
  }

  employeeWord(count: number): string {
    return this.tOrg(count === 1 ? 'workSites.employee' : 'workSites.employees');
  }

  renameAria(name: string): string {
    return this.translationService.translate('Organization', 'workSites.renameAria', name);
  }

  deleteAria(name: string): string {
    return this.translationService.translate('Organization', 'workSites.deleteAria', name);
  }

  noMatchLabel(query: string): string {
    return this.translationService.translate('Organization', 'workSites.noMatch', query);
  }

  employeesAtLabel(name: string): string {
    return this.translationService.translate('Organization', 'workSites.employeesAt', name);
  }

  ngOnInit(): void {
    this.loadWorkSites();
    this.userSyncService.users$.subscribe(users => this.allUsers = users);
  }

  loadWorkSites(): void {
    this.workSiteService.getAll().subscribe({
      next: workSites => this.workSites = workSites,
      error: () => this.errorMessage = this.tOrg('workSites.errorLoading')
    });
    this.workSiteService.getDeleted().subscribe({
      next: workSites => this.deletedWorkSites = workSites,
      error: () => {}
    });
  }

  get filteredWorkSites(): WorkSite[] {
    const query = this.searchQuery.trim().toLowerCase();
    const sites = query
      ? this.workSites.filter(w => w.name.toLowerCase().includes(query))
      : this.workSites;
    return [...sites].sort((a, b) => a.name.localeCompare(b.name));
  }

  get activeCount(): number {
    return this.workSites.filter(w => w.isActive).length;
  }

  /** Every assigned employee across all sites — the denominator the header statement reads against. */
  get aggregateStats(): WorkSiteStats {
    return this.summarize(this.allUsers.filter(u => !!u.workSiteId));
  }

  getStats(workSiteId: string): WorkSiteStats {
    return this.summarize(this.allUsers.filter(u => u.workSiteId === workSiteId));
  }

  getEmployees(workSiteId: string): User[] {
    return this.allUsers
      .filter(u => u.workSiteId === workSiteId)
      .sort((a, b) => a.lastName.localeCompare(b.lastName) || a.firstName.localeCompare(b.firstName));
  }

  private summarize(employees: User[]): WorkSiteStats {
    if (employees.length === 0) return EMPTY_STATS;

    const ssmSigned = employees.filter(u => !!u.hasSignedSsm).length;
    const suSigned = employees.filter(u => !!u.hasSignedSu).length;
    const bothSigned = employees.filter(u => !!u.hasSignedSsm && !!u.hasSignedSu).length;

    return {
      employeeCount: employees.length,
      ssmSigned,
      suSigned,
      bothSigned,
      unsigned: employees.filter(u => !u.hasSignedSsm && !u.hasSignedSu).length,
      ssmPct: Math.round((ssmSigned / employees.length) * 100),
      suPct: Math.round((suSigned / employees.length) * 100),
      bothPct: Math.round((bothSigned / employees.length) * 100)
    };
  }

  /** Traffic-light tone for a coverage figure, so a lagging site is legible at a glance. */
  coverageTone(pct: number): string {
    if (pct >= 80) return 'text-emerald-600';
    if (pct >= 40) return 'text-amber-600';
    return 'text-red-600';
  }

  toggleActive(workSite: WorkSite): void {
    this.workSiteService.update(workSite.id, workSite.name, !workSite.isActive).subscribe({
      next: () => this.loadWorkSites(),
      error: () => this.errorMessage = this.tOrg('workSites.errorUpdating')
    });
  }

  // ───────────────────────── Add ─────────────────────────

  openAddModal(): void {
    this.newWorkSiteName = '';
    this.isAddModalOpen = true;
  }

  closeAddModal(): void {
    this.isAddModalOpen = false;
  }

  saveNewWorkSite(): void {
    if (!this.newWorkSiteName.trim()) return;
    this.workSiteService.add(this.newWorkSiteName.trim()).subscribe({
      next: () => {
        this.closeAddModal();
        this.loadWorkSites();
      },
      error: () => this.errorMessage = this.tOrg('workSites.errorCreating')
    });
  }

  // ───────────────────────── Edit ─────────────────────────

  openEditModal(workSite: WorkSite): void {
    this.workSiteToEdit = workSite;
    this.editWorkSiteName = workSite.name;
    this.isEditModalOpen = true;
  }

  closeEditModal(): void {
    this.isEditModalOpen = false;
    this.workSiteToEdit = null;
  }

  saveWorkSite(): void {
    if (!this.workSiteToEdit || !this.editWorkSiteName.trim()) return;
    this.workSiteService.update(this.workSiteToEdit.id, this.editWorkSiteName.trim(), this.workSiteToEdit.isActive).subscribe({
      next: () => {
        this.closeEditModal();
        this.loadWorkSites();
      },
      error: () => this.errorMessage = this.tOrg('workSites.errorUpdating')
    });
  }

  // ───────────────────────── Delete ─────────────────────────

  openDeleteModal(workSite: WorkSite): void {
    this.workSiteToDelete = workSite;
    this.isDeleteModalOpen = true;
  }

  closeDeleteModal(): void {
    this.isDeleteModalOpen = false;
    this.workSiteToDelete = null;
  }

  confirmDelete(): void {
    if (!this.workSiteToDelete) return;
    this.workSiteService.delete(this.workSiteToDelete.id).subscribe({
      next: () => {
        this.closeDeleteModal();
        this.loadWorkSites();
      },
      error: () => this.errorMessage = this.tOrg('workSites.errorDeleting')
    });
  }

  // ───────────────────────── Restore ─────────────────────────

  openRestoreModal(workSite: WorkSite): void {
    this.workSiteToRestore = workSite;
    this.isRestoreModalOpen = true;
  }

  closeRestoreModal(): void {
    this.isRestoreModalOpen = false;
    this.workSiteToRestore = null;
  }

  confirmRestore(): void {
    if (!this.workSiteToRestore) return;
    this.workSiteService.restore(this.workSiteToRestore.id).subscribe({
      next: () => {
        this.closeRestoreModal();
        this.loadWorkSites();
      },
      error: () => this.errorMessage = this.tOrg('workSites.errorRestoring')
    });
  }

  // ───────────────────────── Employees ─────────────────────────

  openEmployeesModal(workSite: WorkSite): void {
    this.workSiteForEmployees = workSite;
    this.isEmployeesModalOpen = true;
  }

  closeEmployeesModal(): void {
    this.isEmployeesModalOpen = false;
    this.workSiteForEmployees = null;
  }

  viewEmployee(employee: User): void {
    this.closeEmployeesModal();
    this.router.navigate(['/employees', employee.id]);
  }
}
