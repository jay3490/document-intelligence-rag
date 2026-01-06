import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { APP_CONFIG } from '../core/app-config';
import { IngestionJob } from '../core/models';
import { IngestionStatus, activePollMs, idlePollMs } from './ingestion-status';

function job(overrides: Partial<IngestionJob>): IngestionJob {
  return {
    id: 'j1',
    tenantId: 'acme',
    documentId: 'd1',
    fileName: 'handbook.md',
    sizeBytes: 1200,
    contentHash: 'ab',
    status: 'Completed',
    chunkCount: 4,
    attempts: 1,
    error: null,
    duplicateOfDocumentId: null,
    correlationId: 'c1',
    createdAt: '2024-05-01T09:00:00Z',
    startedAt: '2024-05-01T09:00:01Z',
    completedAt: '2024-05-01T09:00:03Z',
    ...overrides,
  };
}

describe('IngestionStatus', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [IngestionStatus],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: '' } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('polls quickly while jobs are active and slowly once they finish', fakeAsync(() => {
    const fixture = TestBed.createComponent(IngestionStatus);
    fixture.detectChanges();

    http.expectOne('/api/ingestions').flush([job({ status: 'Processing', completedAt: null })]);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.badge')?.textContent).toBe('Processing');

    tick(activePollMs);
    http.expectOne('/api/ingestions').flush([job({})]);
    fixture.detectChanges();
    expect(element.querySelector('.badge')?.textContent).toBe('Completed');

    tick(activePollMs);
    http.expectNone('/api/ingestions');
    tick(idlePollMs - activePollMs);
    http.expectOne('/api/ingestions').flush([job({})]);

    fixture.destroy();
    discardPeriodicTasks();
  }));

  it('keeps the last list and shows an error when a refresh fails', fakeAsync(() => {
    const fixture = TestBed.createComponent(IngestionStatus);
    fixture.detectChanges();
    http.expectOne('/api/ingestions').flush([job({ status: 'Failed', error: 'qdrant down' })]);

    fixture.componentInstance['refresh']();
    http.expectOne('/api/ingestions').flush(null, { status: 0, statusText: 'Unknown Error' });
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[role=alert]')?.textContent).toContain('The API could not be reached.');
    expect(element.querySelector('td .error')?.textContent).toContain('qdrant down');

    fixture.destroy();
    discardPeriodicTasks();
  }));

  it('filters to failed jobs', fakeAsync(() => {
    const fixture = TestBed.createComponent(IngestionStatus);
    fixture.detectChanges();
    http.expectOne('/api/ingestions').flush([job({ id: 'a' }), job({ id: 'b', status: 'Failed', fileName: 'broken.md' })]);

    fixture.componentInstance['filter'].set('failed');
    fixture.detectChanges();

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('broken.md');

    fixture.destroy();
    discardPeriodicTasks();
  }));
});
