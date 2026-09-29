import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MoneyPipe } from '../../../shared/ui/format';
import { findProduct } from '../data-access/sample-products';
import { Order } from '../data-access/order.models';

/** What was ordered, with the total as calculated by the server. */
@Component({
  selector: 'app-order-summary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MoneyPipe],
  template: `
    <section class="card summary" aria-labelledby="summary-title">
      <header class="head">
        <h2 id="summary-title" class="card-title">Order summary</h2>
        <p class="hint">Customer <strong>{{ order().customerReference }}</strong></p>
      </header>

      <table>
        <thead>
          <tr>
            <th scope="col">Product</th>
            <th scope="col" class="right">Qty</th>
            <th scope="col" class="right">Unit price</th>
            <th scope="col" class="right">Amount</th>
          </tr>
        </thead>
        <tbody>
          @for (item of order().items; track item.lineNumber) {
            <tr>
              <td>
                <span class="code">{{ item.productCode }}</span>
                @if (productName(item.productCode); as name) {
                  <span class="hint name">{{ name }}</span>
                }
                <span class="hint per-unit">{{ item.quantity }} × {{ item.unitPrice | money }}</span>
              </td>
              <td class="right num">{{ item.quantity }}</td>
              <td class="right num">{{ item.unitPrice | money }}</td>
              <td class="right num strong">{{ item.lineTotal | money }}</td>
            </tr>
          }
        </tbody>
      </table>

      <p class="total-row">
        <span>Total</span>
        <span class="num total">{{ order().total | money }}</span>
      </p>
    </section>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    .summary { padding: 24px; display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; }
    .head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 4px 16px; }
    .head strong { color: var(--ink); font-weight: 600; }
    table { width: 100%; border-collapse: collapse; font-size: 0.9375rem; }
    th, td { padding: 10px 0; text-align: left; vertical-align: top; }
    th + th, td + td { padding-left: 16px; }
    thead th { font-size: 0.8125rem; font-weight: 500; color: var(--ink-2); border-bottom: 1px solid var(--rule); padding-top: 0; }
    tbody tr + tr td { border-top: 1px solid var(--rule); }
    .right { text-align: right; }
    .code { display: block; font-weight: 600; overflow-wrap: anywhere; }
    .name { display: block; }
    .strong { font-weight: 600; }
    .per-unit { display: none; }
    .total-row { display: flex; align-items: baseline; justify-content: space-between; padding-top: 14px; border-top: 2px solid var(--ink); font-weight: 650; }
    .total { font-size: 1.25rem; font-weight: 700; }
    @media (max-width: 520px) {
      .summary { padding: 20px 16px; }
      thead th:nth-child(2), tbody td:nth-child(2), thead th:nth-child(3), tbody td:nth-child(3) { display: none; }
      .per-unit { display: block; }
    }
  `,
})
export class OrderSummary {
  readonly order = input.required<Order>();

  protected productName(code: string): string | null {
    return findProduct(code)?.name ?? null;
  }
}
