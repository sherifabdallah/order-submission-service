import { ChangeDetectionStrategy, Component, effect, inject, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { Icon } from '../../../shared/ui/icon';
import { TestTools } from '../../test-tools/test-tools';
import { OrderSubmissionStore } from '../data-access/order-submission-store';
import { OrderForm } from '../ui/order-form/order-form';
import { RecentOrders } from '../ui/recent-orders';

@Component({
  selector: 'app-new-order-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, OrderForm, RecentOrders],
  template: `
    <div class="layout">
      <app-order-form />
      <aside class="side">
        <app-recent-orders />
        <section class="card tip">
          <app-icon name="tools" [size]="18" />
          <div>
            <p class="tip-title">Trying out this app?</p>
            <p class="hint">
              Use <button type="button" class="link" (click)="tools.open()">Test tools</button> to take the messaging service down,
              watch confirmations retry, and resend requests to check that no duplicate orders are created.
            </p>
          </div>
        </section>
      </aside>
    </div>
  `,
  styles: `
    .layout {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 320px;
      gap: 24px;
      align-items: start;
    }
    .side { display: grid; gap: 16px; }
    .tip { padding: 16px 20px; display: grid; grid-template-columns: 20px minmax(0, 1fr); gap: 12px; color: var(--accent); }
    .tip-title { font-weight: 650; color: var(--ink); }
    .link {
      padding: 0; border: 0; background: none; color: var(--accent);
      font: inherit; font-weight: 600; text-decoration: underline; text-underline-offset: 3px; cursor: pointer;
    }
    @media (max-width: 1040px) {
      .layout { grid-template-columns: minmax(0, 1fr); }
    }
  `,
})
export class NewOrderPage {
  protected readonly tools = inject(TestTools);
  private readonly store = inject(OrderSubmissionStore);
  private readonly router = inject(Router);

  constructor() {
    // As soon as an order is confirmed, take the user to its confirmation page.
    effect(() => {
      if (this.store.phase() !== 'succeeded') {
        return;
      }
      const completed = this.store.completed();
      untracked(() => {
        this.store.startNewOrder();
        if (completed) {
          void this.router.navigate(['/orders', completed.order.id]);
        }
      });
    });
  }
}
