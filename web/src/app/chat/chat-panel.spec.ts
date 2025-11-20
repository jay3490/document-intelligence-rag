import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { APP_CONFIG } from '../core/app-config';
import { ChatPanel } from './chat-panel';

describe('ChatPanel', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ChatPanel],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: '' } },
      ],
    }).compileComponents();
  });

  it('renders the answer with citation markers and its sources', async () => {
    const fixture = TestBed.createComponent(ChatPanel);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    const textarea = element.querySelector('textarea')!;
    textarea.value = 'How much leave?';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();

    element.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    http.expectOne('/api/query').flush({
      answer: 'You get 25 days [1].',
      citations: [
        {
          number: 1,
          chunkId: 'c1',
          documentId: 'd1',
          documentName: 'handbook.md',
          section: 'Leave',
          chunkIndex: 0,
          text: 'Employees receive 25 days.',
          score: 0.032,
        },
      ],
    });
    fixture.detectChanges();

    expect(element.querySelector('.question')?.textContent).toContain('How much leave?');
    expect(element.querySelector('.marker')?.textContent?.trim()).toBe('1');
    expect(element.querySelector('.sources strong')?.textContent).toBe('handbook.md › Leave');
    expect(element.querySelector('blockquote')?.textContent).toContain('Employees receive 25 days.');
    http.verify();
  });
});
