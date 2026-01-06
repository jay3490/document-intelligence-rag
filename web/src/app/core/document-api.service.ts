import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { APP_CONFIG } from './app-config';
import {
  AnswerResult,
  IndexedDocument,
  IngestionJob,
  QueryFilters,
  UploadMetadata,
} from './models';

@Injectable({ providedIn: 'root' })
export class DocumentApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(APP_CONFIG).apiBaseUrl;

  upload(file: File, metadata: UploadMetadata = {}): Observable<IngestionJob> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (metadata.tags?.length) {
      form.append('tags', metadata.tags.join(','));
    }
    if (metadata.source) {
      form.append('source', metadata.source);
    }
    if (metadata.date) {
      form.append('date', metadata.date);
    }
    return this.http.post<IngestionJob>(this.url('/api/documents'), form);
  }

  listJobs(): Observable<IngestionJob[]> {
    return this.http.get<IngestionJob[]>(this.url('/api/ingestions'));
  }

  getJob(id: string): Observable<IngestionJob> {
    return this.http.get<IngestionJob>(this.url(`/api/ingestions/${encodeURIComponent(id)}`));
  }

  listDocuments(): Observable<IndexedDocument[]> {
    return this.http.get<IndexedDocument[]>(this.url('/api/documents'));
  }

  deleteDocument(id: string): Observable<void> {
    return this.http.delete<void>(this.url(`/api/documents/${encodeURIComponent(id)}`));
  }

  ask(question: string, topK?: number, filters?: QueryFilters): Observable<AnswerResult> {
    const body: { question: string; topK?: number; filters?: QueryFilters } = { question };
    if (topK) {
      body.topK = topK;
    }
    const cleaned = cleanFilters(filters);
    if (cleaned) {
      body.filters = cleaned;
    }
    return this.http.post<AnswerResult>(this.url('/api/query'), body);
  }

  private url(path: string): string {
    return `${this.baseUrl}${path}`;
  }
}

/** Drops empty filter fields; returns undefined when nothing is left. */
export function cleanFilters(filters?: QueryFilters): QueryFilters | undefined {
  if (!filters) {
    return undefined;
  }
  const cleaned: QueryFilters = {};
  const tags = (filters.tags ?? []).map((tag) => tag.trim()).filter((tag) => tag.length > 0);
  if (tags.length > 0) {
    cleaned.tags = tags;
  }
  if (filters.source?.trim()) {
    cleaned.source = filters.source.trim();
  }
  if (filters.from) {
    cleaned.from = filters.from;
  }
  if (filters.to) {
    cleaned.to = filters.to;
  }
  return Object.keys(cleaned).length > 0 ? cleaned : undefined;
}

/** Splits a comma-separated tag list typed by the user. */
export function parseTags(text: string): string[] {
  return text
    .split(',')
    .map((tag) => tag.trim())
    .filter((tag) => tag.length > 0);
}

/** Turns an HTTP failure, usually an RFC 9457 problem response, into one readable line. */
export function describeError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Something went wrong.';
  }
  if (error.status === 0) {
    return 'The API could not be reached.';
  }
  if (error.status === 401) {
    return 'You are not signed in, or your session has expired.';
  }

  const problem = error.error as { title?: string; detail?: string; errors?: Record<string, string[]> } | null;
  const validation = problem?.errors ? Object.values(problem.errors).flat() : [];
  if (validation.length > 0) {
    return validation.join(' ');
  }
  if (error.status === 429) {
    const retryAfter = error.headers?.get('Retry-After');
    return retryAfter
      ? `Too many requests. Try again in ${retryAfter} seconds.`
      : 'Too many requests. Try again shortly.';
  }
  return problem?.detail ?? problem?.title ?? `Request failed with status ${error.status}.`;
}
