import { Routes } from '@angular/router';
import { AdminGuard } from './guards/admin.guard';
import { OfficerGuard } from './guards/officer.guard';
import { AuthGuard } from './guards/auth.guard';
import { LineManagerGuard } from './guards/line-manager.guard';

// Pages are lazy-loaded to keep the initial bundle under budget.
export const routes: Routes = [
  // Unconditional redirect, not a guard decision - '/loading' re-evaluates the real session via
  // AuthGuard and sends an already-logged-in visitor to their dashboard instead of bouncing them
  // through '/login' regardless of a perfectly valid session (this was the root cause of the
  // dashboard-flash-then-login-redirect bug: visiting the bare domain ignored auth state entirely).
  { path: '', redirectTo: '/loading', pathMatch: 'full' },

  // Public routes (no authentication required)
  {
    path: 'login',
    loadComponent: () => import('./components/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'loading',
    loadComponent: () => import('./components/loading-screen/loading-screen.component').then(m => m.LoadingScreenComponent),
    canActivate: [AuthGuard]
  },
  {
    path: 'register',
    loadComponent: () => import('./components/register/register.component').then(m => m.RegisterComponent)
  },
  {
    path: 'forgot-password',
    loadComponent: () => import('./components/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent)
  },
  {
    path: 'reset-password',
    loadComponent: () => import('./components/reset-password/reset-password.component').then(m => m.ResetPasswordComponent)
  },
  {
    path: 'confirm-email-change',
    loadComponent: () => import('./pages/confirm-email-change/confirm-email-change.component').then(m => m.ConfirmEmailChangeComponent)
  },
  {
    // Public, no guard - Google/Microsoft fetch this URL when reviewing the OAuth app.
    path: 'privacy',
    loadComponent: () => import('./pages/privacy-policy/privacy-policy.component').then(m => m.PrivacyPolicyComponent)
  },
  {
    path: 'sign/:token',
    loadComponent: () => import('./pages/document-signature/document-signature.component').then(m => m.DocumentSignatureComponent)
  },

  // Authenticated routes (login required)
  {
    path: 'basic-user',
    loadComponent: () => import('./components/basic-user/basic-user.component').then(m => m.BasicUserComponent),
    canActivate: [AuthGuard]
  },
  {
    path: 'line-manager',
    loadComponent: () => import('./components/line-manager/line-manager.component').then(m => m.LineManagerComponent),
    canActivate: [LineManagerGuard]
  },
  {
    path: 'access-restricted',
    loadComponent: () => import('./components/access-restricted/access-restricted.component').then(m => m.AccessRestrictedComponent),
    canActivate: [AuthGuard]
  },

  // Admin only routes
  {
    path: 'dashboard',
    loadComponent: () => import('./components/dashboard/dashboard.component').then(m => m.DashboardComponent),
    canActivate: [AdminGuard]
  },
  {
    path: 'departments',
    loadComponent: () => import('./components/departments/departments.component').then(m => m.DepartmentsComponent),
    canActivate: [AdminGuard]
  },
  {
    path: 'work-sites',
    loadComponent: () => import('./components/work-sites/work-sites.component').then(m => m.WorkSitesComponent),
    canActivate: [AdminGuard]
  },
  {
    path: 'users',
    loadComponent: () => import('./components/users-list/users-list.component').then(m => m.UsersListComponent),
    canActivate: [AdminGuard]
  },
  {
    path: 'employees',
    loadComponent: () => import('./components/employees-detail/employees-detail.component').then(m => m.EmployeesDetailComponent),
    canActivate: [LineManagerGuard]
  },
  {
    path: 'employees/:id',
    loadComponent: () => import('./components/employees-detail/employees-detail.component').then(m => m.EmployeesDetailComponent),
    canActivate: [LineManagerGuard]
  },
  {
    path: 'employees/:id/ssm-su',
    loadComponent: () => import('./components/ssm-su-form/ssm-su-form.component').then(m => m.SsmSuFormComponent),
    canActivate: [LineManagerGuard]
  },
  {
    path: 'import-history',
    loadComponent: () => import('./components/import-history/import-history.component').then(m => m.ImportHistoryComponent),
    canActivate: [AdminGuard]
  },
  {
    path: 'test-signature',
    loadComponent: () => import('./pages/test-signature/test-signature.component').then(m => m.TestSignatureComponent),
    canActivate: [AdminGuard]
  },
  {
    path: 'admin-signature',
    loadComponent: () => import('./pages/admin-signature/admin-signature.component').then(m => m.AdminSignatureComponent),
    canActivate: [OfficerGuard]
  },
  {
    path: 'documents',
    loadComponent: () => import('./pages/documents-view/documents-view.component').then(m => m.DocumentsViewComponent),
    canActivate: [LineManagerGuard]
  },
  {
    path: 'data-requests',
    loadComponent: () => import('./pages/data-change-requests/data-change-requests.component').then(m => m.DataChangeRequestsComponent),
    canActivate: [AdminGuard]
  },

  // Same reasoning as the '' route above - let '/loading' decide based on the real session
  // instead of unconditionally bouncing an already-logged-in visitor to '/login'.
  { path: '**', redirectTo: '/loading' }
];
