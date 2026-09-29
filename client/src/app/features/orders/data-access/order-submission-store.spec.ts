import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { Order, PlaceOrderRequest, PlaceOrderResponse } from './order.models';
import { OrderSubmissionStore } from './order-submission-store';
import { OrdersApi } from './orders-api';

const request: PlaceOrderRequest = {
  customerReference: 'ACME-4471',
  items: [{ productCode: 'CHR-ERGO-BLK', quantity: 2, unitPrice: 189 }],
};

const order: Order = {
  id: '01a0e15d-0000-7000-8000-000000000001',
  customerReference: 'ACME-4471',
  total: 378,
  placedAt: '2026-09-27T10:00:00Z',
  items: [{ lineNumber: 1, productCode: 'CHR-ERGO-BLK', quantity: 2, unitPrice: 189, lineTotal: 378 }],
  notification: { status: 'Pending', attemptCount: 0, nextAttemptAt: null, deliveredAt: null, lastError: null, attempts: [] },
};

const serverDown = () => new HttpErrorResponse({ status: 500, statusText: 'Internal Server Error' });

/** Records every call and answers with whatever the test queues next. */
class FakeOrdersApi {
  readonly keys: string[] = [];
  readonly responses: Array<() => Observable<PlaceOrderResponse>> = [];

  place(_: PlaceOrderRequest, key: string): Observable<PlaceOrderResponse> {
    this.keys.push(key);
    return (this.responses.shift() ?? (() => of({ order, replayed: false, status: 201 })))();
  }
}

describe('OrderSubmissionStore', () => {
  let api: FakeOrdersApi;
  let store: OrderSubmissionStore;

  beforeEach(() => {
    localStorage.clear();
    api = new FakeOrdersApi();
    TestBed.configureTestingModule({ providers: [{ provide: OrdersApi, useValue: api }] });
    store = TestBed.inject(OrderSubmissionStore);
  });

  it('retries an unconfirmed submission with the same Idempotency-Key', () => {
    api.responses.push(() => throwError(serverDown));

    store.submit(request);
    expect(store.phase()).toBe('failed');
    expect(store.keyPlan(request)).toEqual({ mode: 'reuse', key: api.keys[0] });

    store.retry();

    expect(api.keys).toHaveLength(2);
    expect(api.keys[1]).toBe(api.keys[0]);
    expect(store.phase()).toBe('succeeded');
  });

  it('treats pressing submit again with an unchanged order as a retry', () => {
    api.responses.push(() => throwError(serverDown));

    store.submit(request);
    store.submit({ ...request, customerReference: '  ACME-4471 ' });

    expect(api.keys[1]).toBe(api.keys[0]);
  });

  it('issues a new key when the order was edited after a failed attempt', () => {
    api.responses.push(() => throwError(serverDown));
    store.submit(request);

    const edited = { ...request, items: [{ ...request.items[0], quantity: 3 }] };
    expect(store.keyPlan(edited)).toEqual({ mode: 'changed', previousKey: api.keys[0] });
    store.submit(edited);

    expect(api.keys[1]).not.toBe(api.keys[0]);
  });

  it('gives the next order a new key once the previous one succeeded', () => {
    store.submit(request);
    expect(store.completed()?.key).toBe(api.keys[0]);

    store.startNewOrder();
    store.submit(request);

    expect(api.keys[1]).not.toBe(api.keys[0]);
  });

  it('releases the key when the server definitively rejects the request', () => {
    api.responses.push(() => throwError(() => new HttpErrorResponse({ status: 400, error: { errors: {} } })));

    store.submit(request);

    expect(store.pending()).toBeNull();
    expect(store.error()?.kind).toBe('validation');
  });

  it('ignores a second submit while the first request is in flight', () => {
    const inFlight = new Subject<PlaceOrderResponse>();
    api.responses.push(() => inFlight);

    store.submit(request);
    store.submit(request);

    expect(api.keys).toHaveLength(1);
    inFlight.next({ order, replayed: false, status: 201 });
    inFlight.complete();
    expect(store.phase()).toBe('succeeded');
  });

  it('survives a page reload: the unconfirmed submission is restored with its key', () => {
    api.responses.push(() => throwError(serverDown));
    store.submit(request);
    const key = api.keys[0];

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [{ provide: OrdersApi, useValue: api }] });
    const reloaded = TestBed.inject(OrderSubmissionStore);

    expect(reloaded.interrupted()).toBe(true);
    expect(reloaded.keyPlan(request)).toEqual({ mode: 'reuse', key });
  });

  it('reports a replayed response from the server', () => {
    api.responses.push(() => of({ order, replayed: true, status: 200 }));

    store.submit(request);

    expect(store.completed()?.replayed).toBe(true);
  });
});
