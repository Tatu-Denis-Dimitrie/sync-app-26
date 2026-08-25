import { TestBed } from '@angular/core/testing';
import { DOCUMENT } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AuthenticationService, Roles } from './authentication.service';

describe('AuthenticationService', () => {
  let service: AuthenticationService;
  let httpMock: HttpTestingController;
  let mockDocument: { location: { href: string } };

  beforeEach(() => {
    mockDocument = { location: { href: '' } };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DOCUMENT, useValue: mockDocument }
      ]
    });
    service = TestBed.inject(AuthenticationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should create', () => {
    expect(service).toBeTruthy();
  });

  describe('hydrate', () => {
    it('populates the session from an authenticated /me response', () => {
      let completed = false;
      service.hydrate().subscribe(() => (completed = true));

      httpMock.expectOne(r => r.url.endsWith('/authentication/me')).flush({
        authenticated: true,
        user: { id: '1', email: 'a@b.com', firstName: 'A', lastName: 'B', roles: [Roles.Admin] }
      });

      expect(completed).toBeTrue();
      expect(service.isLoggedIn()).toBeTrue();
      expect(service.isAdmin()).toBeTrue();
      expect(service.getCurrentUser()?.email).toBe('a@b.com');
    });

    it('leaves the session empty for an unauthenticated /me response', () => {
      service.hydrate().subscribe();

      httpMock.expectOne(r => r.url.endsWith('/authentication/me')).flush({ authenticated: false });

      expect(service.isLoggedIn()).toBeFalse();
      expect(service.getCurrentUser()).toBeNull();
    });

    it('resolves without throwing when /me itself fails - the app-initializer contract', () => {
      let completed = false;
      let errored = false;
      service.hydrate().subscribe({ next: () => (completed = true), error: () => (errored = true) });

      httpMock.expectOne(r => r.url.endsWith('/authentication/me'))
        .flush('boom', { status: 500, statusText: 'Server Error' });

      expect(errored).toBeFalse();
      expect(completed).toBeTrue();
      expect(service.isLoggedIn()).toBeFalse();
    });

    it('populates the impersonator block when /me reports an active impersonation', () => {
      service.hydrate().subscribe();

      httpMock.expectOne(r => r.url.endsWith('/authentication/me')).flush({
        authenticated: true,
        user: { id: 'target', email: 'target@test.com', firstName: 'T', lastName: 'U', roles: [Roles.BasicUser] },
        impersonating: true,
        impersonator: { id: 'admin', email: 'admin@test.com', firstName: 'Ad', lastName: 'Min', roles: [Roles.Admin] }
      });

      expect(service.isImpersonating()).toBeTrue();
      expect(service.impersonator()?.email).toBe('admin@test.com');
    });
  });

  describe('login', () => {
    it('populates the session from the login response', () => {
      service.login({ email: 'a@b.com', password: 'pw' }).subscribe();

      httpMock.expectOne(r => r.url.endsWith('/authentication/login')).flush({
        message: 'Login successful.',
        user: { id: '1', email: 'a@b.com', firstName: 'A', lastName: 'B', roles: [Roles.LineManager] }
      });

      expect(service.isLoggedIn()).toBeTrue();
      expect(service.isLineManager()).toBeTrue();
    });
  });

  describe('logout', () => {
    it('posts to /logout, clears the session, and redirects to /login', () => {
      service.login({ email: 'a@b.com', password: 'pw' }).subscribe();
      httpMock.expectOne(r => r.url.endsWith('/authentication/login')).flush({
        message: 'ok',
        user: { id: '1', email: 'a@b.com', firstName: 'A', lastName: 'B', roles: [Roles.Admin] }
      });

      service.logout();
      httpMock.expectOne(r => r.url.endsWith('/authentication/logout')).flush({});

      expect(service.isLoggedIn()).toBeFalse();
      expect(mockDocument.location.href).toBe('/login');
    });

    it('still redirects to /login even if the logout call itself fails', () => {
      service.logout();
      httpMock.expectOne(r => r.url.endsWith('/authentication/logout'))
        .flush('boom', { status: 500, statusText: 'Server Error' });

      expect(mockDocument.location.href).toBe('/login');
    });
  });

  it('never touches localStorage for session state', () => {
    service.login({ email: 'a@b.com', password: 'pw' }).subscribe();
    httpMock.expectOne(r => r.url.endsWith('/authentication/login')).flush({
      message: 'ok',
      user: { id: '1', email: 'a@b.com', firstName: 'A', lastName: 'B', roles: [Roles.Admin] }
    });

    expect(localStorage.getItem('authToken')).toBeNull();
    expect(localStorage.getItem('currentUser')).toBeNull();
    expect(localStorage.getItem('impersonationOriginalToken')).toBeNull();
    expect(localStorage.getItem('impersonationOriginalUser')).toBeNull();
  });
});
