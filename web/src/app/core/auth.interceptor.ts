import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Adds the session's bearer token to API calls and signs out when the API answers 401, so
 * an expired or revoked token sends the user back to the sign-in form.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const isTokenRequest = request.url.endsWith('/api/auth/dev-token');
  const token = isTokenRequest ? null : auth.token;

  const authorized = token
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (!isTokenRequest && error instanceof HttpErrorResponse && error.status === 401) {
        auth.signOut('Your session is no longer valid. Sign in again.');
      }
      return throwError(() => error);
    }),
  );
};
