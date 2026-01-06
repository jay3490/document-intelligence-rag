import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { DocumentApiService, describeError, parseTags } from '../core/document-api.service';
import { IndexedDocument, IngestionJob } from '../core/models';

const pollIntervalMs = 1000;
const acceptedExtensions = ['.md', '.markdown', '.txt', '.text'];

@Component({
  selector: 'app-documents-panel',
  imports: [DatePipe, DecimalPipe, FormsModule],
  templateUrl: './documents-panel.html',
  styleUrl: './documents-panel.css',
})
export class DocumentsPanel implements OnInit {
  private readonly api = inject(DocumentApiService);
  private pollTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly accept = acceptedExtensions.join(',');
  protected readonly jobs = signal<IngestionJob[]>([]);
  protected readonly documents = signal<IndexedDocument[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly uploading = signal(false);
  protected readonly dragging = signal(false);
  protected uploadTags = '';
  protected uploadSource = '';
  protected uploadDate = '';
  protected readonly pending = computed(() =>
    this.jobs().some((job) => job.status === 'Queued' || job.status === 'Processing'),
  );

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.pollTimer));
  }

  ngOnInit(): void {
    this.refresh();
  }

  protected onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.upload(Array.from(input.files ?? []));
    input.value = '';
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.upload(Array.from(event.dataTransfer?.files ?? []));
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected remove(document: IndexedDocument): void {
    this.api.deleteDocument(document.id).subscribe({
      next: () => this.refresh(),
      error: (err) => this.error.set(describeError(err)),
    });
  }

  private upload(files: File[]): void {
    const supported = files.filter((file) =>
      acceptedExtensions.some((ext) => file.name.toLowerCase().endsWith(ext)),
    );
    if (supported.length < files.length) {
      this.error.set(`Only ${acceptedExtensions.join(', ')} files can be ingested.`);
    } else {
      this.error.set(null);
    }
    if (supported.length === 0) {
      return;
    }

    this.uploading.set(true);
    const metadata = {
      tags: parseTags(this.uploadTags),
      source: this.uploadSource.trim() || undefined,
      date: this.uploadDate || undefined,
    };
    forkJoin(supported.map((file) => this.api.upload(file, metadata))).subscribe({
      next: () => {
        this.uploading.set(false);
        this.refresh();
      },
      error: (err) => {
        this.uploading.set(false);
        this.error.set(describeError(err));
        this.refresh();
      },
    });
  }

  /** Reloads jobs and documents, and keeps polling while any job is still running. */
  private refresh(): void {
    clearTimeout(this.pollTimer);
    forkJoin({ jobs: this.api.listJobs(), documents: this.api.listDocuments() }).subscribe({
      next: ({ jobs, documents }) => {
        this.jobs.set(jobs);
        this.documents.set(documents);
        if (this.pending()) {
          this.pollTimer = setTimeout(() => this.refresh(), pollIntervalMs);
        }
      },
      error: (err) => this.error.set(describeError(err)),
    });
  }
}
