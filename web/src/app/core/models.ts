export type IngestionStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed' | 'Duplicate';

export interface IngestionJob {
  id: string;
  tenantId: string;
  documentId: string;
  fileName: string;
  sizeBytes: number;
  contentHash: string;
  status: IngestionStatus;
  chunkCount: number | null;
  attempts: number;
  error: string | null;
  duplicateOfDocumentId: string | null;
  correlationId: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

export interface IndexedDocument {
  id: string;
  name: string;
  chunkCount: number;
  tokenCount: number;
  source: string | null;
  tags: string[];
  date: string | null;
}

/** Metadata sent with an upload; all fields are optional. */
export interface UploadMetadata {
  tags?: string[];
  source?: string;
  /** ISO date (yyyy-mm-dd) or date and time. */
  date?: string;
}

/** Narrows the sources a question is answered from. */
export interface QueryFilters {
  tags?: string[];
  source?: string;
  from?: string;
  to?: string;
}

export interface Citation {
  number: number;
  chunkId: string;
  documentId: string;
  documentName: string;
  section: string | null;
  chunkIndex: number;
  text: string;
  score: number;
}

export interface AnswerResult {
  answer: string;
  citations: Citation[];
}

export interface DevToken {
  accessToken: string;
  expiresAt: string;
  tenantId: string;
  subject: string;
}

export function isActive(job: IngestionJob): boolean {
  return job.status === 'Queued' || job.status === 'Processing';
}
