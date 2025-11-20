import { DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DocumentApiService, describeError } from '../core/document-api.service';
import { Citation } from '../core/models';
import { AnswerSegment, splitAnswer } from './answer-segments';

export interface Exchange {
  id: number;
  question: string;
  state: 'pending' | 'answered' | 'failed';
  segments: AnswerSegment[];
  citations: Citation[];
  error?: string;
}

@Component({
  selector: 'app-chat-panel',
  imports: [FormsModule, DecimalPipe],
  templateUrl: './chat-panel.html',
  styleUrl: './chat-panel.css',
})
export class ChatPanel {
  private readonly api = inject(DocumentApiService);
  private nextId = 1;

  protected readonly exchanges = signal<Exchange[]>([]);
  protected readonly highlighted = signal<string | null>(null);
  protected question = '';

  protected get busy(): boolean {
    return this.exchanges().some((exchange) => exchange.state === 'pending');
  }

  protected ask(): void {
    const question = this.question.trim();
    if (!question || this.busy) {
      return;
    }

    const id = this.nextId++;
    this.question = '';
    this.exchanges.update((list) => [
      ...list,
      { id, question, state: 'pending', segments: [], citations: [] },
    ]);

    this.api.ask(question).subscribe({
      next: (result) =>
        this.update(id, {
          state: 'answered',
          segments: splitAnswer(result.answer),
          citations: result.citations,
        }),
      error: (err) => this.update(id, { state: 'failed', error: describeError(err) }),
    });
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.ask();
    }
  }

  /** Key used to link a citation marker in an answer to its entry in the source list. */
  protected citationKey(exchange: Exchange, number: number): string {
    return `${exchange.id}:${number}`;
  }

  protected sourceTitle(citation: Citation): string {
    return citation.section ? `${citation.documentName} › ${citation.section}` : citation.documentName;
  }

  private update(id: number, changes: Partial<Exchange>): void {
    this.exchanges.update((list) =>
      list.map((exchange) => (exchange.id === id ? { ...exchange, ...changes } : exchange)),
    );
  }
}
