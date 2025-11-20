import { Component } from '@angular/core';
import { ChatPanel } from './chat/chat-panel';
import { DocumentsPanel } from './documents/documents-panel';

@Component({
  selector: 'app-root',
  imports: [ChatPanel, DocumentsPanel],
  template: `
    <header class="masthead">
      <h1>Document Intelligence</h1>
      <p>Upload text or Markdown, then ask questions answered from those documents with citations.</p>
    </header>
    <main class="layout">
      <app-documents-panel />
      <app-chat-panel />
    </main>
  `,
  styles: `
    .masthead {
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
    .layout {
      display: grid;
      grid-template-columns: minmax(280px, 1fr) 2fr;
      gap: 1.5rem;
      padding: 1.5rem;
      align-items: start;
    }
    @media (max-width: 860px) {
      .layout {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class App {}
