import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { EMPTY, Observable, Subscription, expand, of, retry, switchMap, throwError, timer } from 'rxjs';
import { ApiError, isTransient, toApiError } from '../../../core/http/api-error';
import { Order, isTerminal } from './order.models';
import { OrdersApi } from './orders-api';

const MIN_POLL_MS = 1_000;
const MAX_POLL_MS = 10_000;

/**
 * Follows one order until its notification reaches a final state. Polling is adaptive: while a
 * retry is scheduled it waits until just after the attempt is due rather than polling every
 * second, and it stops completely once the notification is Delivered or Failed.
 */
@Injectable()
export class OrderTracker {
  private readonly api = inject(OrdersApi);
  private subscription?: Subscription;

  readonly order = signal<Order | null>(null);
  readonly error = signal<ApiError | null>(null);
  readonly retryingDelivery = signal(false);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.subscription?.unsubscribe());
  }

  track(orderId: string, initial?: Order): void {
    this.subscription?.unsubscribe();
    this.error.set(null);
    this.order.set(initial ?? null);

    const first$: Observable<Order> = initial ? of(initial) : this.fetch(orderId);
    this.subscription = first$
      .pipe(expand((order) => (isTerminal(order) ? EMPTY : timer(pollDelay(order)).pipe(switchMap(() => this.fetch(orderId))))))
      .subscribe({
        next: (order) => this.order.set(order),
        error: (error: unknown) => this.error.set(toApiError(error)),
      });
  }

  /** Asks the server to attempt delivery now, then resumes tracking. */
  retryDelivery(): void {
    const order = this.order();
    if (!order || this.retryingDelivery()) {
      return;
    }

    this.retryingDelivery.set(true);
    this.api.retryDelivery(order.id).subscribe({
      next: (updated) => {
        this.retryingDelivery.set(false);
        this.track(updated.id, updated);
      },
      error: (error: unknown) => {
        this.retryingDelivery.set(false);
        this.error.set(toApiError(error));
        this.track(order.id);
      },
    });
  }

  private fetch(orderId: string): Observable<Order> {
    return this.api.get(orderId).pipe(
      retry({ count: 5, delay: (error) => (isTransient(error) ? timer(2_000) : throwError(() => error)) }),
    );
  }
}

function pollDelay(order: Order): number {
  const next = order.notification.nextAttemptAt;
  const untilDue = next ? Date.parse(next) - Date.now() : 0;
  return Math.min(Math.max(untilDue + 400, MIN_POLL_MS), MAX_POLL_MS);
}
