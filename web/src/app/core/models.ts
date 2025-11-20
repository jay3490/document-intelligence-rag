export type IngestionStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed';

export interface IngestionJob {
  id: string;
  documentId: string;
  fileName: string;
  sizeBytes: number;
  status: IngestionStatus;
  chunkCount: number | null;
  error: string | null;
  createdAt: string;
  completedAt: string | null;
}

export interface IndexedDocument {
  id: string;
  name: string;
  chunkCount: number;
  tokenCount: number;
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
