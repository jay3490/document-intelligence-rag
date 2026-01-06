import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { APP_CONFIG } from './app-config';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('AuthService and authInterceptor', () => {
  let auth: AuthService;
  let http: HttpTestingController;
  let client: HttpClient;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: '' } },
      ],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  function signIn(expiresAt = new Date(Date.now() + 60_000).toISOString()): void {
    auth.signIn('acme', 'alice').subscribe();
    const request = http.expectOne('/api/auth/dev-token');
    expect(request.request.body).toEqual({ tenant: 'acme', user: 'alice' });
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({ accessToken: 'token-1', expiresAt, tenantId: 'acme', subject: 'alice' });
  }

  it('signs in with a development token and remembers the session', () => {
    signIn();

    expect(auth.isSignedIn()).toBeTrue();
    expect(auth.tenant()).toBe('acme');
    expect(sessionStorage.getItem('di.session')).toContain('token-1');
  });

  it('sends the bearer token with API requests', () => {
    signIn();

    client.get('/api/documents').subscribe();

    expect(http.expectOne('/api/documents').request.headers.get('Authorization')).toBe('Bearer token-1');
  });

  it('signs out when the API rejects the token', () => {
    signIn();

    client.get('/api/documents').subscribe({ error: () => undefined });
    http.expectOne('/api/documents').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(auth.isSignedIn()).toBeFalse();
    expect(auth.signedOutReason()).toContain('no longer valid');
    expect(sessionStorage.getItem('di.session')).toBeNull();
  });

  it('drops an expired session instead of sending its token', () => {
    signIn(new Date(Date.now() - 1000).toISOString());

    client.get('/api/documents').subscribe();

    expect(http.expectOne('/api/documents').request.headers.has('Authorization')).toBeFalse();
    expect(auth.isSignedIn()).toBeFalse();
    expect(auth.signedOutReason()).toContain('expired');
  });
});
