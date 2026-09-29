import { HttpErrorResponse } from '@angular/common/http';

/** RFC 9457 problem details as returned by the API. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
  traceId?: string;
}

export type ApiErrorKind = 'validation' | 'conflict' | 'not-found' | 'rate-limited' | 'server' | 'network';

export interface ApiError {
  kind: ApiErrorKind;
  status: number;
  message: string;
  code?: string;
  fieldErrors: Record<string, string[]>;
}

/** Statuses where the request may never have been processed, so an automatic retry is sensible. */
const transientStatuses = new Set([0, 408, 429, 502, 503, 504]);

export function isTransient(error: unknown): boolean {
  return error instanceof HttpErrorResponse && transientStatuses.has(error.status);
}

export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return { kind: 'server', status: -1, message: 'Something unexpected went wrong in the browser.', fieldErrors: {} };
  }

  const problem: ProblemDetails = typeof error.error === 'object' && error.error !== null ? error.error : {};
  const base = { status: error.status, code: problem.code, fieldErrors: problem.errors ?? {} };

  switch (true) {
    case error.status === 0:
      return { ...base, kind: 'network', message: 'Could not reach the server. Check your connection.' };
    case error.status === 400:
      return { ...base, kind: 'validation', message: problem.detail ?? 'Some fields need attention.' };
    case error.status === 404:
      return { ...base, kind: 'not-found', message: problem.detail ?? 'Not found.' };
    case error.status === 409:
      return { ...base, kind: 'conflict', message: problem.detail ?? 'The request conflicts with an earlier one.' };
    case error.status === 429:
      return { ...base, kind: 'rate-limited', message: 'Too many requests from this device. Wait a few seconds and try again.' };
    default:
      return { ...base, kind: 'server', message: problem.detail ?? `The server returned ${error.status}.` };
  }
}
