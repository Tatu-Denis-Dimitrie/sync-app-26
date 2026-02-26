import { Routes } from '@angular/router';
import { LoginComponent } from './components/login/login.component';
import { ForgotPasswordComponent } from './components/forgot-password/forgot-password.component';
import { ResetPasswordComponent } from './components/reset-password/reset-password.component';
import { AccessRestrictedComponent } from './components/access-restricted/access-restricted.component';
import { DashboardComponent } from './components/dashboard/dashboard.component';
import { DepartmentsComponent } from './components/departments/departments.component';
import { UsersListComponent } from './components/users-list/users-list.component';
import { EmployeesDetailComponent } from './components/employees-detail/employees-detail.component';
import { ImportHistoryComponent } from './components/import-history/import-history.component';
import { RegisterComponent } from './components/register/register.component';
import { DocumentSignatureComponent } from './pages/document-signature/document-signature.component';
import { TestSignatureComponent } from './pages/test-signature/test-signature.component';
import { AdminGuard } from './guards/admin.guard';
import { AuthGuard } from './guards/auth.guard';
import { SsmSuFormComponent } from './components/ssm-su-form/ssm-su-form.component';

export const routes: Routes = [
  { path: '', redirectTo: '/login', pathMatch: 'full' },
  
  // Public routes (no authentication required)
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { path: 'forgot-password', component: ForgotPasswordComponent },
  { path: 'reset-password', component: ResetPasswordComponent },
  { path: 'sign/:token', component: DocumentSignatureComponent },
  
  // Authenticated routes (login required)
  { path: 'access-restricted', component: AccessRestrictedComponent, canActivate: [AuthGuard] },
  
  // Admin only routes
  { path: 'dashboard', component: DashboardComponent, canActivate: [AdminGuard] },
  { path: 'departments', component: DepartmentsComponent, canActivate: [AdminGuard] },
  { path: 'users', component: UsersListComponent, canActivate: [AdminGuard] },
  { path: 'employees', component: EmployeesDetailComponent, canActivate: [AdminGuard] },
  { path: 'employees/:id', component: EmployeesDetailComponent, canActivate: [AdminGuard] },
  { path: 'employees/:id/ssm-su', component: SsmSuFormComponent, canActivate: [AdminGuard] },
  { path: 'import-history', component: ImportHistoryComponent, canActivate: [AdminGuard] },
  { path: 'test-signature', component: TestSignatureComponent, canActivate: [AdminGuard] },
  
  { path: '**', redirectTo: '/login' }
];
