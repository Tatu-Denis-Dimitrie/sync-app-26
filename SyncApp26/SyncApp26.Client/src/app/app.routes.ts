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

export const routes: Routes = [
  { path: '', redirectTo: '/login', pathMatch: 'full' },
  { path: 'login', component: LoginComponent },
  { path: 'register', component: RegisterComponent },
  { path: 'forgot-password', component: ForgotPasswordComponent },
  { path: 'reset-password', component: ResetPasswordComponent },
  { path: 'access-restricted', component: AccessRestrictedComponent },  
  { path: 'dashboard', component: DashboardComponent },
  { path: 'departments', component: DepartmentsComponent },
  { path: 'users', component: UsersListComponent },
  { path: 'employees', component: EmployeesDetailComponent },
  { path: 'employees/:id', component: EmployeesDetailComponent },
  { path: 'import-history', component: ImportHistoryComponent },
  { path: 'sign/:token', component: DocumentSignatureComponent },
  { path: 'test-signature', component: TestSignatureComponent },
  { path: '**', redirectTo: '/login' }
];
