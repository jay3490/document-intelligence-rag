import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
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

  it('reports an unreachable API', () => {
    expect(describeError(new HttpErrorResponse({ status: 0 }))).toBe('The API could not be reached.');
  });
});
