// TypeScript mirrors of the API's JSON contracts (src/Crawler.Api/Jobs/JobContracts.cs).

export type JobStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Canceled';

export const isActive = (status?: JobStatus) => status === 'Pending' || status === 'Running';

export interface JobSummary {
  jobId: string;
  url: string;
  status: JobStatus;
  maxDepth: number;
  maxPages: number;
  pagesCrawled: number;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  failureReason: string | null;
}

export interface JobHistory {
  items: JobSummary[];
  nextCursor: string | null;
}

export interface PageNode {
  pageId: number;
  url: string;
  depth: number;
  httpStatus: number | null;
  error: string | null;
  outgoingLinkCount: number;
  internalLinkCount: number;
  domainLinkRatio: number;
  children: PageNode[];
}

export interface JobTree {
  jobId: string;
  status: JobStatus;
  pageCount: number;
  root: PageNode | null;
}

export interface PageLinks {
  pageId: number;
  url: string;
  links: { url: string; isInternal: boolean }[];
}
