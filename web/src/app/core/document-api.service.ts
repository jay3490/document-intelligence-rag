import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { APP_CONFIG } from './app-config';
import { AnswerResult, IndexedDocument, IngestionJob } from './models';

@Injectable({ providedIn: 'root' })
export class DocumentApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(APP_CONFIG).apiBaseUrl;

  upload(file: File): Observable<IngestionJob> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<IngestionJob>(this.url('/api/documents'), form);
  }

  listJobs(): Observable<IngestionJob[]> {
    return this.http.get<IngestionJob[]>(this.url('/api/ingestions'));
  }

  listDocuments(): Observable<IndexedDocument[]> {
    return this.http.get<IndexedDocument[]>(this.url('/api/documents'));
  }

  deleteDocument(id: string): Observable<void> {
    return this.http.delete<void>(this.url(`/api/documents/${encodeURIComponent(id)}`));
  }

  ask(question: string, topK?: number): Observable<AnswerResult> {
    return this.http.post<AnswerResult>(this.url('/api/query'), topK ? { question, topK } : { question });
  }

  private url(path: string): string {
    return `${this.baseUrl}${path}`;
  }
}

/** Turns an HTTP failure, usually an RFC 9457 problem response, into one readable line. */
export function describeError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Something went wrong.';
  }
  if (error.status === 0) {
    return 'The API could not be reached.';
  }

  const problem = error.error as { title?: string; detail?: string; errors?: Record<string, string[]> } | null;
  const validation = problem?.errors ? Object.values(problem.errors).flat() : [];
  if (validation.length > 0) {
    return validation.join(' ');
  }
  return problem?.detail ?? problem?.title ?? `Request failed with status ${error.status}.`;
}
