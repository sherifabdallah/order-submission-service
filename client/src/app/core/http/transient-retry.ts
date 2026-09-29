import { MonoTypeOperatorFunction, retry, throwError, timer } from 'rxjs';
import { isTransient } from './api-error';

export interface TransientRetryOptions {
  /** Extra attempts after the first one. */
  retries: number;
  baseDelayMs: number;
  onRetry?: (attempt: number, error: unknown) => void;
}

/**
 * Retries transient failures (network, 408, 429, 502-504) with exponential back-off and jitter.
 * The same HttpRequest is re-sent, so headers such as Idempotency-Key are reused as-is.
 */
export function retryTransient<T>({ retries, baseDelayMs, onRetry }: TransientRetryOptions): MonoTypeOperatorFunction<T> {
  return retry({
    count: retries,
    delay: (error, retryCount) => {
      if (!isTransient(error)) {
        return throwError(() => error);
      }
      onRetry?.(retryCount + 1, error);
      const backoff = baseDelayMs * 2 ** (retryCount - 1);
      return timer(backoff * (0.8 + Math.random() * 0.4));
    },
  });
}
