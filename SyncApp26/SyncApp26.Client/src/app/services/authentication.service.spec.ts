import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';

import { AuthenticationService } from './authentication.service';

const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

function makeToken(payload: Record<string, unknown>): string {
  const header = btoa(JSON.stringify({ alg: 'none', typ: 'JWT' }));
  const body = btoa(JSON.stringify(payload));
  return `${header}.${body}.signature`;
}

describe('AuthenticationService', () => {
  let service: AuthenticationService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(AuthenticationService);
  });

  afterEach(() => localStorage.clear());

  it('should create', () => {
    expect(service).toBeTruthy();
  });

  describe('hasRole', () => {
    it('reads a single role from a string claim', () => {
      localStorage.setItem('authToken', makeToken({ [ROLE_CLAIM]: 'Admin', exp: Math.floor(Date.now() / 1000) + 3600 }));

      expect(service.hasRole('Admin')).toBeTrue();
      expect(service.hasRole('LineManager')).toBeFalse();
    });

    it('reads multiple roles from an array claim', () => {
      localStorage.setItem('authToken', makeToken({ [ROLE_CLAIM]: ['Admin', 'LineManager'], exp: Math.floor(Date.now() / 1000) + 3600 }));

      expect(service.hasRole('Admin')).toBeTrue();
      expect(service.hasRole('LineManager')).toBeTrue();
      expect(service.hasRole('SsmOfficer')).toBeFalse();
    });

    it('ignores a tampered currentUser entry that does not match the signed token', () => {
      // The whole point of reading roles from the JWT: currentUser is plain localStorage JSON a
      // user can edit in devtools, but it must not grant anything the token itself doesn't carry.
      localStorage.setItem('authToken', makeToken({ [ROLE_CLAIM]: 'BasicUser', exp: Math.floor(Date.now() / 1000) + 3600 }));
      localStorage.setItem('currentUser', JSON.stringify({ id: '1', email: 'a@b.com', firstName: 'A', lastName: 'B', roles: ['Admin'] }));

      expect(service.hasRole('Admin')).toBeFalse();
    });

    it('returns false when no token is stored', () => {
      expect(service.hasRole('Admin')).toBeFalse();
    });
  });

  describe('isLoggedIn', () => {
    it('returns false when no token is stored', () => {
      expect(service.isLoggedIn()).toBeFalse();
    });

    it('returns true for a token with a future exp', () => {
      localStorage.setItem('authToken', makeToken({ exp: Math.floor(Date.now() / 1000) + 3600 }));

      expect(service.isLoggedIn()).toBeTrue();
    });

    it('returns false for an expired token', () => {
      localStorage.setItem('authToken', makeToken({ exp: Math.floor(Date.now() / 1000) - 60 }));

      expect(service.isLoggedIn()).toBeFalse();
    });

    it('returns false for a malformed token', () => {
      localStorage.setItem('authToken', 'not-a-jwt');

      expect(service.isLoggedIn()).toBeFalse();
    });
  });
});
