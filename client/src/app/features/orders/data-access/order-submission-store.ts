import { Injectable, computed, inject, signal } from '@angular/core';
import { safeStorage, newIdempotencyKey } from '../../../core/browser/storage';
import { ApiError, toApiError } from '../../../core/http/api-error';
import { retryTransient } from '../../../core/http/transient-retry';
import { Order, PlaceOrderRequest, fingerprint } from './order.models';
import { OrdersApi } from './orders-api';
import { RecentOrdersStore } from './recent-orders-store';

/** A submission whose outcome is not known yet. It owns its Idempotency-Key until it resolves. */
export interface PendingSubmission {
  key: string;
  request: PlaceOrderRequest;
  fingerprint: string;
  createdAt: string;
}

export interface CompletedSubmission {
  key: string;
  request: PlaceOrderRequest;
  order: Order;
  replayed: boolean;
}

export type SubmissionPhase = 'idle' | 'sending' | 'succeeded' | 'failed';

/** What pressing "Place order" will do with the Idempotency-Key, given the current draft. */
export type KeyPlan =
  | { mode: 'new' }
  | { mode: 'reuse'; key: string }
  | { mode: 'changed'; previousKey: string };

const PENDING_STORAGE_KEY = 'order-desk.pending-submission';

/** Automatic retries for transient failures, on top of the first attempt. */
export const AUTO_RETRIES = 3;

/**
 * Owns the client side of idempotent submission:
 * - one key per logical submission, generated when it is first sent;
 * - every retry of that submission (automatic or manual, even after a page reload) reuses the key;
 * - an edited order is a new submission with a new key;
 * - the key is released once the server gives a definitive answer.
 */
@Injectable({ providedIn: 'root' })
export class OrderSubmissionStore {
  private readonly api = inject(OrdersApi);
  private readonly recentOrders = inject(RecentOrdersStore);

  private readonly _pending = signal<PendingSubmission | null>(safeStorage.read<PendingSubmission>(PENDING_STORAGE_KEY));
  private readonly _phase = signal<SubmissionPhase>('idle');
  private readonly _attempt = signal(0);
  private readonly _error = signal<ApiError | null>(null);
  private readonly _completed = signal<CompletedSubmission | null>(null);

  readonly pending = this._pending.asReadonly();
  readonly phase = this._phase.asReadonly();
  /** Transport attempt of the current send: 1 for the first request, 2+ for automatic retries. */
  readonly attempt = this._attempt.asReadonly();
  readonly error = this._error.asReadonly();
  readonly completed = this._completed.asReadonly();

  readonly sending = computed(() => this._phase() === 'sending');

  /** A submission restored from a previous visit whose outcome was never confirmed. */
  readonly interrupted = computed(() => this._pending() !== null && this._phase() === 'idle');

  keyPlan(draft: PlaceOrderRequest): KeyPlan {
    const pending = this._pending();
    if (!pending) {
      return { mode: 'new' };
    }
    return pending.fingerprint === fingerprint(draft)
      ? { mode: 'reuse', key: pending.key }
      : { mode: 'changed', previousKey: pending.key };
  }

  submit(request: PlaceOrderRequest): void {
    if (this.sending()) {
      return; // A double click must not start a second request.
    }

    const plan = this.keyPlan(request);
    const pending: PendingSubmission =
      plan.mode === 'reuse'
        ? this._pending()!
        : { key: newIdempotencyKey(), request, fingerprint: fingerprint(request), createdAt: new Date().toISOString() };

    this.setPending(pending);
    this.send(pending);
  }

  /** Re-sends the unresolved submission with its original key. */
  retry(): void {
    const pending = this._pending();
    if (pending && !this.sending()) {
      this.send(pending);
    }
  }

  discard(): void {
    this.setPending(null);
    this._error.set(null);
    this._phase.set('idle');
  }

  /**
   * Readies the store for the next order. The last completed submission is kept so the confirmation
   * page and the test tools can still refer to it; the next submit always gets a new key.
   */
  startNewOrder(): void {
    this._error.set(null);
    this._phase.set('idle');
  }

  private send(pending: PendingSubmission): void {
    this._phase.set('sending');
    this._attempt.set(1);
    this._error.set(null);

    this.api
      .place(pending.request, pending.key)
      .pipe(retryTransient({ retries: AUTO_RETRIES, baseDelayMs: 600, onRetry: (attempt) => this._attempt.set(attempt) }))
      .subscribe({
        next: ({ order, replayed }) => {
          this.setPending(null);
          this._completed.set({ key: pending.key, request: pending.request, order, replayed });
          this._phase.set('succeeded');
          this.recentOrders.remember(order);
        },
        error: (error: unknown) => {
          const apiError = toApiError(error);
          // 400/409 are definitive: nothing was stored, so the key is released. Anything else leaves
          // the outcome unknown, so the key is kept for a safe retry.
          if (apiError.kind === 'validation' || apiError.kind === 'conflict') {
            this.setPending(null);
          }
          this._error.set(apiError);
          this._phase.set('failed');
        },
      });
  }

  private setPending(pending: PendingSubmission | null): void {
    this._pending.set(pending);
    if (pending) {
      safeStorage.write(PENDING_STORAGE_KEY, pending);
    } else {
      safeStorage.remove(PENDING_STORAGE_KEY);
    }
  }
}
