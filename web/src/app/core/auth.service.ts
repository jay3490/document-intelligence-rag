import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { APP_CONFIG } from './app-config';
import { DevToken } from './models';

const storageKey = 'di.session';

export interface Session {
  token: string;
  tenant: string;
  user: string;
  expiresAt: string;
}

/**
 * Holds the signed-in session. Tokens come from the API's development token endpoint, which
 * only exists when the API runs in DevToken mode; the session is kept in sessionStorage so a
 * reload stays signed in but closing the tab does not.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(APP_CONFIG).apiBaseUrl;

  private readonly session = signal<Session | null>(readStoredSession());

  /** Set when the API rejected the session, so the sign-in form can say why. */
  readonly signedOutReason = signal<string | null>(null);

  readonly current = this.session.asReadonly();
  readonly isSignedIn = computed(() => this.session() !== null);
  readonly tenant = computed(() => this.session()?.tenant ?? null);

  get token(): string | null {
    const session = this.session();
    if (session && new Date(session.expiresAt).getTime() <= Date.now()) {
      this.signOut('Your session expired. Sign in again.');
      return null;
    }
    return session?.token ?? null;
  }

  signIn(tenant: string, user: string): Observable<DevToken> {
    return this.http
      .post<DevToken>(`${this.baseUrl}/api/auth/dev-token`, { tenant, user })
      .pipe(
        tap((token) => {
          const session: Session = {
            token: token.accessToken,
            tenant: token.tenantId,
            user: token.subject,
            expiresAt: token.expiresAt,
          };
          this.signedOutReason.set(null);
          this.session.set(session);
          writeStoredSession(session);
        }),
      );
  }

  signOut(reason: string | null = null): void {
    this.signedOutReason.set(reason);
    this.session.set(null);
    writeStoredSession(null);
  }
}

function readStoredSession(): Session | null {
  try {
    const raw = sessionStorage.getItem(storageKey);
    const session = raw ? (JSON.parse(raw) as Session) : null;
    return session && new Date(session.expiresAt).getTime() > Date.now() ? session : null;
  } catch {
    return null;
  }
}

function writeStoredSession(session: Session | null): void {
  try {
    if (session) {
      sessionStorage.setItem(storageKey, JSON.stringify(session));
    } else {
      sessionStorage.removeItem(storageKey);
    }
  } catch {
    // Storage can be unavailable (private mode, blocked site data); the session then lasts
    // only as long as the page.
  }
}
