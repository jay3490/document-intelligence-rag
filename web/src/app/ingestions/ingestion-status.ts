import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { DocumentApiService, describeError } from '../core/document-api.service';
import { IngestionJob, isActive } from '../core/models';

/** Poll quickly while something is queued or running, slowly otherwise. */
export const activePollMs = 2000;
export const idlePollMs = 15000;

type StatusFilter = 'all' | 'active' | 'failed';

/**
 * The tenant's ingestion jobs with their status, attempts, chunk counts and errors. Polls the
 * API and keeps showing the last good list (with a warning) when a refresh fails.
 */
@Component({
  selector: 'app-ingestion-status',
  imports: [DatePipe, DecimalPipe, FormsModule],
  templateUrl: './ingestion-status.html',
  styleUrl: './ingestion-status.css',
})
export class IngestionStatus implements OnInit {
  private readonly api = inject(DocumentApiService);
  private timer: ReturnType<typeof setTimeout> | undefined;
  private request: Subscription | undefined;

  protected readonly jobs = signal<IngestionJob[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly lastUpdated = signal<Date | null>(null);
  protected readonly filter = signal<StatusFilter>('all');

  protected readonly activeCount = computed(() => this.jobs().filter(isActive).length);
  protected readonly failedCount = computed(() => this.jobs().filter((job) => job.status === 'Failed').length);
  protected readonly visible = computed(() => {
    switch (this.filter()) {
      case 'active':
        return this.jobs().filter(isActive);
      case 'failed':
        return this.jobs().filter((job) => job.status === 'Failed');
      default:
        return this.jobs();
    }
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.timer);
      this.request?.unsubscribe();
    });
  }

  ngOnInit(): void {
    this.refresh();
  }

  protected refresh(): void {
    clearTimeout(this.timer);
    this.request?.unsubscribe();
    this.request = this.api.listJobs().subscribe({
      next: (jobs) => {
        this.jobs.set(jobs);
        this.error.set(null);
        this.loading.set(false);
        this.lastUpdated.set(new Date());
        this.schedule();
      },
      error: (err) => {
        this.error.set(describeError(err));
        this.loading.set(false);
        if (err?.status !== 401) {
          this.schedule();
        }
      },
    });
  }

  protected durationSeconds(job: IngestionJob): number | null {
    if (!job.startedAt || !job.completedAt) {
      return null;
    }
    return (new Date(job.completedAt).getTime() - new Date(job.startedAt).getTime()) / 1000;
  }

  private schedule(): void {
    this.timer = setTimeout(() => this.refresh(), this.activeCount() > 0 ? activePollMs : idlePollMs);
  }
}
