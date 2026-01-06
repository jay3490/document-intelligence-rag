import { Component, inject, signal } from '@angular/core';
import { ChatPanel } from './chat/chat-panel';
import { AuthService } from './core/auth.service';
import { DocumentsPanel } from './documents/documents-panel';
import { IngestionStatus } from './ingestions/ingestion-status';
import { LoginPanel } from './login/login-panel';

export type View = 'workspace' | 'ingestions';

@Component({
  selector: 'app-root',
  imports: [ChatPanel, DocumentsPanel, IngestionStatus, LoginPanel],
  template: `
    <header class="masthead">
      <div>
        <h1>Document Intelligence</h1>
        <p>Upload text or Markdown, then ask questions answered from those documents with citations.</p>
      </div>
      @if (auth.current(); as session) {
        <div class="session">
          <span>
            <strong>{{ session.user }}</strong> in tenant <strong class="tenant">{{ session.tenant }}</strong>
          </span>
          <button type="button" class="link" (click)="auth.signOut()">Sign out</button>
        </div>
      }
    </header>

    @if (auth.isSignedIn()) {
      <nav class="tabs" aria-label="Views">
        <button type="button" [class.active]="view() === 'workspace'" (click)="view.set('workspace')">Documents and questions</button>
        <button type="button" [class.active]="view() === 'ingestions'" (click)="view.set('ingestions')">Ingestion status</button>
      </nav>

      @if (view() === 'workspace') {
        <main class="layout">
          <app-documents-panel />
          <app-chat-panel />
        </main>
      } @else {
        <main class="single">
          <app-ingestion-status />
        </main>
      }
    } @else {
      <main class="single">
        <app-login-panel />
      </main>
    }
  `,
  styles: `
    .masthead {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      align-items: flex-start;
      gap: 1rem;
      padding: 1.5rem 1.5rem 0;
    }
    .masthead h1 {
      margin: 0;
      font-size: 1.5rem;
    }
    .masthead p {
      margin: 0.25rem 0 0;
      color: var(--muted);
    }
    .session {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      font-size: 0.9rem;
    }
    .tenant {
      color: var(--accent);
    }
    .tabs {
      display: flex;
      gap: 0.5rem;
      padding: 1rem 1.5rem 0;
    }
    .tabs button {
      background: none;
      color: var(--muted);
      border: 1px solid var(--border);
    }
    .tabs button.active {
      background: var(--accent-soft);
      color: var(--accent);
      border-color: var(--accent);
    }
    .layout {
      display: grid;
      grid-template-columns: minmax(280px, 1fr) 2fr;
      gap: 1.5rem;
      padding: 1.5rem;
      align-items: start;
    }
    .single {
      padding: 1.5rem;
    }
    @media (max-width: 860px) {
      .layout {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly view = signal<View>('workspace');
}
