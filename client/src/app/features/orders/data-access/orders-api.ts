import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { Order, PlaceOrderRequest, PlaceOrderResponse } from './order.models';

export const IDEMPOTENCY_KEY_HEADER = 'Idempotency-Key';
export const IDEMPOTENT_REPLAYED_HEADER = 'Idempotent-Replayed';

@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private readonly http = inject(HttpClient);

  place(request: PlaceOrderRequest, idempotencyKey: string): Observable<PlaceOrderResponse> {
    return this.http
      .post<Order>('/api/orders', request, {
        headers: { [IDEMPOTENCY_KEY_HEADER]: idempotencyKey },
        observe: 'response',
      })
      .pipe(
        map((response) => ({
          order: response.body as Order,
          replayed: response.headers.get(IDEMPOTENT_REPLAYED_HEADER) === 'true',
          status: response.status,
        })),
      );
  }

  get(orderId: string): Observable<Order> {
    return this.http.get<Order>(`/api/orders/${encodeURIComponent(orderId)}`);
  }

  retryDelivery(orderId: string): Observable<Order> {
    return this.http.post<Order>(`/api/orders/${encodeURIComponent(orderId)}/notification/retry`, null);
  }
}
