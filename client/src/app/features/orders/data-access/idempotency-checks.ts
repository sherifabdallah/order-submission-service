import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';
import { newIdempotencyKey } from '../../../core/browser/storage';
import { toApiError } from '../../../core/http/api-error';
import { PlaceOrderRequest } from './order.models';
import { OrdersApi } from './orders-api';

export interface CheckResult {
  id: number;
  title: string;
  passed: boolean;
  summary: string;
  detail: string;
}

interface Outcome {
  status: number;
  orderId?: string;
  replayed?: boolean;
  message?: string;
}

/**
 * Lets a reviewer exercise the API's idempotency guarantees from the UI and see the raw outcome:
 * a replayed request, a reused key with a changed payload, and a burst of concurrent duplicates.
 */
@Injectable({ providedIn: 'root' })
export class IdempotencyChecks {
  private readonly api = inject(OrdersApi);
  private sequence = 0;

  readonly results = signal<CheckResult[]>([]);
  readonly running = signal<string | null>(null);

  resendIdentical(key: string, request: PlaceOrderRequest, originalOrderId: string): void {
    this.run('identical', this.send(request, key), (outcome) => ({
      title: 'Same key, same order',
      passed: outcome.status === 200 && outcome.replayed === true && outcome.orderId === originalOrderId,
      summary: `${statusText(outcome.status)}${outcome.replayed ? ' · Idempotent-Replayed' : ''}`,
      detail: outcome.orderId === originalOrderId ? `Returned the original order ${short(outcome.orderId)}. Nothing new was created.` : describe(outcome),
    }));
  }

  resendChanged(key: string, request: PlaceOrderRequest): void {
    const changed: PlaceOrderRequest = {
      ...request,
      items: request.items.map((item, index) => (index === 0 ? { ...item, quantity: item.quantity + 1 } : item)),
    };
    this.run('changed', this.send(changed, key), (outcome) => ({
      title: 'Same key, changed quantity',
      passed: outcome.status === 409,
      summary: statusText(outcome.status),
      detail: outcome.message ?? describe(outcome),
    }));
  }

  /** Places one new order by firing several identical requests with one fresh key at the same time. */
  burst(request: PlaceOrderRequest, count = 5): void {
    const key = newIdempotencyKey();
    const requests = Array.from({ length: count }, () => this.send(request, key));
    this.run('burst', forkJoin(requests), (outcomes) => {
      const ids = new Set(outcomes.map((outcome) => outcome.orderId).filter(Boolean));
      const created = outcomes.filter((outcome) => outcome.status === 201).length;
      const replayed = outcomes.filter((outcome) => outcome.status === 200).length;
      return {
        title: `${count} concurrent requests, one new key`,
        passed: ids.size === 1 && created === 1 && created + replayed === count,
        summary: `${created} × 201 Created · ${replayed} × 200 Replayed`,
        detail: ids.size === 1 ? `All ${count} responses point to order ${short([...ids][0])}.` : `Responses referenced ${ids.size} different orders.`,
      };
    });
  }

  private send(request: PlaceOrderRequest, key: string) {
    return this.api.place(request, key).pipe(
      map((response): Outcome => ({ status: response.status, orderId: response.order.id, replayed: response.replayed })),
      catchError((error: unknown) =>
        of<Outcome>({ status: error instanceof HttpErrorResponse ? error.status : -1, message: toApiError(error).message }),
      ),
    );
  }

  private run<T>(name: string, source: Observable<T>, evaluate: (value: T) => Omit<CheckResult, 'id'>): void {
    if (this.running()) {
      return;
    }
    this.running.set(name);
    source.subscribe((value) => {
      this.running.set(null);
      this.results.update((results) => [{ id: ++this.sequence, ...evaluate(value) }, ...results].slice(0, 4));
    });
  }
}

const statusNames: Record<number, string> = { 200: 'OK', 201: 'Created', 400: 'Bad Request', 409: 'Conflict', 429: 'Too Many Requests' };
const statusText = (status: number) => (status > 0 ? `${status} ${statusNames[status] ?? ''}`.trim() : 'No response');
const short = (id?: string) => (id ? `…${id.slice(-8)}` : '');
const describe = (outcome: Outcome) => outcome.message ?? `Order ${short(outcome.orderId)}`;
