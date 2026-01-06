import { HttpErrorResponse, HttpHeaders, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { APP_CONFIG } from './app-config';
import { DocumentApiService, describeError } from './document-api.service';

describe('DocumentApiService', () => {
  let service: DocumentApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: 'http://api.test' } },
      ],
    });
    service = TestBed.inject(DocumentApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uploads the file as multipart form data under "file"', () => {
    const file = new File(['# Notes'], 'notes.md', { type: 'text/markdown' });

    service.upload(file).subscribe();

    const request = http.expectOne('http://api.test/api/documents');
    expect(request.request.method).toBe('POST');
    expect((request.request.body as FormData).get('file')).toBeInstanceOf(File);
    request.flush({});
  });

  it('posts questions to the query endpoint', () => {
    let answer = '';
    service.ask('How many days?', 3).subscribe((result) => (answer = result.answer));

    const request = http.expectOne('http://api.test/api/query');
    expect(request.request.body).toEqual({ question: 'How many days?', topK: 3 });
    request.flush({ answer: '25 days [1].', citations: [] });
    expect(answer).toBe('25 days [1].');
  });

  it('sends upload metadata as form fields', () => {
    const file = new File(['text'], 'a.md');

    service.upload(file, { tags: ['hr', 'policy'], source: 'wiki', date: '2024-03-01' }).subscribe();

    const body = http.expectOne('http://api.test/api/documents').request.body as FormData;
    expect(body.get('tags')).toBe('hr,policy');
    expect(body.get('source')).toBe('wiki');
    expect(body.get('date')).toBe('2024-03-01');
  });

  it('sends only the filters that are set', () => {
    service.ask('q', undefined, { tags: [' hr ', ''], source: '  ', from: '2024-01-01T00:00:00Z' }).subscribe();

    expect(http.expectOne('http://api.test/api/query').request.body).toEqual({
      question: 'q',
      filters: { tags: ['hr'], from: '2024-01-01T00:00:00Z' },
    });
  });

  it('omits empty filters entirely', () => {
    service.ask('q', undefined, { tags: [], source: '' }).subscribe();

    expect(http.expectOne('http://api.test/api/query').request.body).toEqual({ question: 'q' });
  });

  it('deletes documents by id', () => {
    service.deleteDocument('abc').subscribe();

    const request = http.expectOne('http://api.test/api/documents/abc');
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
  });
});

describe('describeError', () => {
  it('prefers validation messages from a problem response', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { title: 'One or more validation errors occurred.', errors: { file: ['The file contains no text.'] } },
    });
    expect(describeError(error)).toBe('The file contains no text.');
  });

  it('falls back to the problem detail, then the title', () => {
    expect(describeError(new HttpErrorResponse({ status: 502, error: { title: 'A dependency failed', detail: 'qdrant down' } }))).toBe('qdrant down');
    expect(describeError(new HttpErrorResponse({ status: 404, error: { title: 'Not found' } }))).toBe('Not found');
  });

  it('explains rate limiting with the retry delay', () => {
    const error = new HttpErrorResponse({
      status: 429,
      error: { title: 'Too many requests' },
      headers: new HttpHeaders({ 'Retry-After': '30' }),
    });
    expect(describeError(error)).toBe('Too many requests. Try again in 30 seconds.');
  });

  it('explains a missing or expired session', () => {
    expect(describeError(new HttpErrorResponse({ status: 401 }))).toContain('not signed in');
  });

  it('reports an unreachable API', () => {
    expect(describeError(new HttpErrorResponse({ status: 0 }))).toBe('The API could not be reached.');
  });
});
