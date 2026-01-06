import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../core/auth.service';
import { describeError } from '../core/document-api.service';

const tenantPattern = /^[a-z0-9][a-z0-9_-]{0,63}$/;

/**
 * Signs in with a development token for a tenant. Everything the user then uploads, lists or
 * asks is confined to that tenant by the API.
 */
@Component({
  selector: 'app-login-panel',
  imports: [FormsModule],
  template: `
    <section class="panel login">
      <h2>Sign in</h2>
      <p class="muted">
        Choose a tenant to work in. Documents, ingestion jobs and answers are kept separate per tenant.
      </p>

      @if (auth.signedOutReason(); as reason) {
        <p class="notice" role="status">{{ reason }}</p>
      }

      <form (ngSubmit)="submit()">
        <label>
          Tenant
          <input name="tenant" [(ngModel)]="tenant" autocomplete="organization" placeholder="acme" required />
        </label>
        <label>
          Your name
          <input name="user" [(ngModel)]="user" autocomplete="username" placeholder="developer" maxlength="64" />
        </label>

        @if (error(); as message) {
          <p class="error" role="alert">{{ message }}</p>
        }

        <button type="submit" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
      </form>
      <p class="muted small">
        This form uses the API's development tokens. Deployments that sign in through an identity
        provider do not offer it.
      </p>
    </section>
  `,
  styles: `
    .login {
      max-width: 420px;
      margin: 3rem auto;
    }
    form {
      display: grid;
      gap: 0.75rem;
    }
    label {
      display: grid;
      gap: 0.25rem;
      font-size: 0.85rem;
      color: var(--muted);
    }
    .notice {
      padding: 0.5rem 0.75rem;
      border-radius: var(--radius);
      background: var(--accent-soft);
      font-size: 0.85rem;
    }
    .small {
      margin-top: 1rem;
      font-size: 0.75rem;
    }
  `,
})
export class LoginPanel {
  protected readonly auth = inject(AuthService);

  protected tenant = '';
  protected user = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    const tenant = this.tenant.trim().toLowerCase();
    if (!tenantPattern.test(tenant)) {
      this.error.set('Use 1 to 64 lower-case letters, digits, "-" or "_", starting with a letter or digit.');
      return;
    }

    this.error.set(null);
    this.busy.set(true);
    this.auth.signIn(tenant, this.user.trim() || 'developer').subscribe({
      next: () => this.busy.set(false),
      error: (err) => {
        this.busy.set(false);
        this.error.set(
          err?.status === 404
            ? 'This API does not issue development tokens. Sign in through your identity provider.'
            : describeError(err),
        );
      },
    });
  }
}
