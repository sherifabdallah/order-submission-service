import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DateTimePipe, MoneyPipe, OrderRefPipe } from '../../../shared/ui/format';
import { RecentOrdersStore } from '../data-access/recent-orders-store';

@Component({
  selector: 'app-recent-orders',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DateTimePipe, MoneyPipe, OrderRefPipe],
  template: `
    <section class="card recent" aria-labelledby="recent-title">
      <h2 id="recent-title" class="card-title">Recent orders</h2>
      @if (store.orders().length) {
        <ul>
          @for (order of store.orders(); track order.id) {
            <li>
              <a [routerLink]="['/orders', order.id]">
                <span class="ref">{{ order.id | orderRef }}</span>
                <span class="amount num">{{ order.total | money }}</span>
                <span class="meta hint">{{ order.customerReference }} · {{ order.itemCount }} {{ order.itemCount === 1 ? 'item' : 'items' }}</span>
                <span class="meta hint when">{{ order.placedAt | dateTime }}</span>
              </a>
            </li>
          }
        </ul>
      } @else {
        <p class="hint">Orders you place from this browser appear here.</p>
      }
    </section>
  `,
  styles: `
    :host { display: block; }
    .recent { padding: 20px; display: grid; gap: 10px; }
    ul { margin: 0 -8px; padding: 0; list-style: none; display: grid; }
    a {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: 2px 12px;
      padding: 10px 8px;
      border-radius: var(--radius-sm);
      color: var(--ink);
      text-decoration: none;
    }
    a:hover { background: var(--sheet-inset); }
    li + li a { border-top: 1px solid var(--rule); border-radius: 0; }
    .ref { font-weight: 650; color: var(--accent); }
    .amount { font-weight: 600; text-align: right; }
    .meta { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .when { text-align: right; }
  `,
})
export class RecentOrders {
  protected readonly store = inject(RecentOrdersStore);
}
