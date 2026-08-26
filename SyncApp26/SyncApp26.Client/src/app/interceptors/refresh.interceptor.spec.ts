import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { refreshInterceptor } from './refresh.interceptor';

describe('refreshInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([refreshInterceptor])),
        provideHttpClientTesting()
      ]
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('on 401, calls /refresh then retries the original request', () => {
    let result: unknown;
    http.get('/api/department').subscribe(res => (result = res));

    httpMock.expectOne('/api/department').flush('unauthorized', { status: 401, statusText: 'Unauthorized' });

    const refreshReq = httpMock.expectOne(r => r.url.endsWith('/authentication/refresh'));
    expect(refreshReq.request.method).toBe('POST');
    refreshReq.flush({});

    httpMock.expectOne('/api/department').flush({ ok: true });

    expect(result).toEqual({ ok: true });
  });

  it('does not attempt a refresh for exempt auth paths', () => {
    let errored = false;
    http.post('/api/authentication/login', {}).subscribe({ error: () => (errored = true) });

    httpMock.expectOne(r => r.url.endsWith('/authentication/login'))
      .flush('bad creds', { status: 401, statusText: 'Unauthorized' });

    expect(errored).toBeTrue();
    httpMock.expectNone(r => r.url.endsWith('/authentication/refresh'));
  });

  it('propagates the ORIGINAL 401 (not the refresh call error) if the refresh itself fails', () => {
    let observedStatus: number | undefined;
    http.get('/api/department').subscribe({ error: err => (observedStatus = err.status) });

    httpMock.expectOne('/api/department').flush('unauthorized', { status: 401, statusText: 'Unauthorized' });

    httpMock.expectOne(r => r.url.endsWith('/authentication/refresh'))
      .flush('refresh failed', { status: 401, statusText: 'Unauthorized' });

    expect(observedStatus).toBe(401);
    httpMock.expectNone('/api/department');
  });

  it('single-flight: two concurrent 401s share one /refresh call', () => {
    let result1: unknown;
    let result2: unknown;
    http.get('/api/department').subscribe(res => (result1 = res));
    http.get('/api/users').subscribe(res => (result2 = res));

    httpMock.expectOne('/api/department').flush('unauthorized', { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne('/api/users').flush('unauthorized', { status: 401, statusText: 'Unauthorized' });

    // Only one refresh request should have been made for both failures.
    httpMock.expectOne(r => r.url.endsWith('/authentication/refresh')).flush({});

    httpMock.expectOne('/api/department').flush({ a: 1 });
    httpMock.expectOne('/api/users').flush({ b: 2 });

    expect(result1).toEqual({ a: 1 });
    expect(result2).toEqual({ b: 2 });
  });
});
