import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DateTimePipe, OrderRefPipe } from '../../../shared/ui/format';
import { Icon } from '../../../shared/ui/icon';
import { OrderSubmissionStore } from '../data-access/order-submission-store';
import { OrderTracker } from '../data-access/order-tracker';
import { ConfirmationStatus } from '../ui/confirmation-status';
import { OrderSummary } from '../ui/order-summary';

/** Confirmation page after placing an order, and the addressable view of any order. */
@Component({
  selector: 'app-order-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [OrderTracker],
  imports: [RouterLink, DateTimePipe, OrderRefPipe, Icon, OrderSummary, ConfirmationStatus],
  template: `
    <div class="order-page">
      @if (tracker.order(); as order) {
        <section class="card hero" [class.replayed]="arrival() === 'replayed'">
          <span class="hero-icon" [class.info]="arrival() === 'replayed'">
            <app-icon [name]="arrival() === 'replayed' ? 'info' : 'check'" [size]="22" />
          </span>
          <div class="hero-text">
            <h1>
              @switch (arrival()) {
                @case ('placed') { Order placed }
                @case ('replayed') { This order was already placed }
                @default { Order {{ order.id | orderRef }} }
              }
            </h1>
            <p class="hint">
              @if (arrival() === 'replayed') {
                We recognised the repeated request, so no duplicate was created. This is the original order.
              } @else if (arrival() === 'placed') {
                Order <strong>{{ order.id | orderRef }}</strong> · placed {{ order.placedAt | dateTime }}
              } @else {
                Placed {{ order.placedAt | dateTime }} for customer {{ order.customerReference }}
              }
            </p>
          </div>
          <div class="hero-actions">
            <button type="button" class="btn btn-secondary btn-sm" (click)="copyId(order.id)">
              <app-icon [name]="copied() ? 'check' : 'copy'" [size]="14" /> {{ copied() ? 'Copied' : 'Copy order ID' }}
            </button>
            <a routerLink="/" class="btn btn-primary btn-sm"><app-icon name="plus" [size]="14" /> Place another order</a>
          </div>
        </section>

        <div class="columns">
          <app-order-summary [order]="order" />
          <app-confirmation-status
            [notification]="order.notification"
            [recipient]="order.customerReference"
            [busy]="tracker.retryingDelivery()"
            (retry)="tracker.retryDelivery()"
          />
        </div>

        @if (tracker.error(); as error) {
          <p class="field-error" role="alert">{{ error.message }}</p>
        }
      } @else if (tracker.error(); as error) {
        <section class="card message" role="alert">
          <h1>{{ error.kind === 'not-found' ? 'Order not found' : 'The order could not be loaded' }}</h1>
          <p class="hint">{{ error.kind === 'not-found' ? 'Check the link, or place a new order.' : error.message }}</p>
          <a routerLink="/" class="btn btn-primary btn-sm">Place a new order</a>
        </section>
      } @else {
        <section class="card message" aria-busy="true">
          <p class="hint loading"><span class="spinner" aria-hidden="true"></span> Loading order…</p>
        </section>
      }
    </div>
  `,
  styles: `
    .order-page { display: grid; grid-template-columns: minmax(0, 1fr); gap: 20px; max-width: 1040px; margin-inline: auto; }
    .hero {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr) auto;
      align-items: center;
      gap: 16px 20px;
      padding: 24px;
    }
    .hero-icon {
      display: grid; place-items: center; width: 48px; height: 48px; border-radius: 50%;
      background: var(--ok-soft); color: var(--ok);
    }
    .hero-icon.info { background: var(--accent-soft); color: var(--accent); }
    .hero-text { display: grid; gap: 4px; }
    .hero-text h1 { font-size: 1.5rem; font-weight: 700; letter-spacing: -0.015em; }
    .hero-text strong { color: var(--ink); }
    .hero-actions { display: flex; flex-wrap: wrap; gap: 8px; justify-content: flex-end; }
    .columns { display: grid; grid-template-columns: minmax(0, 3fr) minmax(0, 2fr); gap: 20px; align-items: start; }
    .message { padding: 28px; display: grid; gap: 10px; justify-items: start; }
    .message h1 { font-size: 1.25rem; }
    .loading { display: flex; align-items: center; gap: 10px; }
    @media (max-width: 860px) {
      .columns { grid-template-columns: minmax(0, 1fr); }
      .columns app-confirmation-status { order: -1; }
      .hero { grid-template-columns: auto minmax(0, 1fr); }
      .hero-actions { grid-column: 1 / -1; justify-content: flex-start; }
    }
    @media (max-width: 520px) {
      .hero { padding: 20px 16px; }
      .hero-actions .btn { flex: 1 1 auto; }
    }
  `,
})
export class OrderPage {
  protected readonly tracker = inject(OrderTracker);
  private readonly store = inject(OrderSubmissionStore);

  /** Bound from the :id route parameter. */
  readonly id = input.required<string>();

  protected readonly copied = signal(false);

  /** Whether this order was just placed (or replayed) from this browser, which changes the heading. */
  protected readonly arrival = computed<'placed' | 'replayed' | null>(() => {
    const completed = this.store.completed();
    if (completed?.order.id !== this.id()) {
      return null;
    }
    return completed.replayed ? 'replayed' : 'placed';
  });

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => {
        const completed = this.store.completed();
        this.tracker.track(id, completed?.order.id === id ? completed.order : undefined);
      });
    });
  }

  protected copyId(id: string): void {
    navigator.clipboard?.writeText(id).then(
      () => {
        this.copied.set(true);
        setTimeout(() => this.copied.set(false), 1500);
      },
      () => undefined,
    );
  }
}
