import { ChangeDetectionStrategy, Component, ElementRef, afterNextRender, computed, inject, viewChild } from '@angular/core';
import { IdempotencyChecks } from '../orders/data-access/idempotency-checks';
import { OrderSubmissionStore } from '../orders/data-access/order-submission-store';
import { NotificationServiceMode, NotificationServiceSimulationStore } from '../simulation/notification-service-simulation';
import { OrderRefPipe } from '../../shared/ui/format';
import { Icon } from '../../shared/ui/icon';
import { TestTools } from './test-tools';

interface ModeOption {
  value: NotificationServiceMode;
  label: string;
  description: string;
}

export const MESSAGING_MODES: readonly ModeOption[] = [
  { value: 'Healthy', label: 'Working', description: 'Every confirmation is sent.' },
  { value: 'Flaky', label: 'Unreliable', description: 'About half of the attempts fail and are retried.' },
  { value: 'Outage', label: 'Down', description: 'Every attempt fails until you switch it back.' },
];

/**
 * Side panel for people evaluating the app: simulate messaging failures and resend requests to see
 * the duplicate protection at work. Kept out of the ordering flow so customers never see it.
 */
@Component({
  selector: 'app-test-tools-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, OrderRefPipe],
  host: { '(document:keydown.escape)': 'tools.close()' },
  template: `
    <aside id="test-tools" class="panel" role="complementary" aria-labelledby="tools-title">
      <header class="panel-head">
        <div>
          <h2 id="tools-title">Test tools</h2>
          <p class="hint">For trying out failure handling. Changes apply to this server for everyone using it.</p>
        </div>
        <button #closeButton type="button" class="close" (click)="tools.close()" aria-label="Close test tools">
          <app-icon name="cross" [size]="18" />
        </button>
      </header>

      <section class="block" aria-labelledby="messaging-title">
        <h3 id="messaging-title">Messaging service</h3>
        <p class="hint">Simulates the service that sends order confirmations to customers.</p>
        <div class="modes" role="radiogroup" aria-labelledby="messaging-title">
          @for (mode of modes; track mode.value) {
            <label class="mode" [class.selected]="simulation.current()?.mode === mode.value">
              <input
                type="radio"
                name="messaging-mode"
                [value]="mode.value"
                [checked]="simulation.current()?.mode === mode.value"
                [disabled]="!canChangeMode()"
                (change)="simulation.setMode(mode.value)"
              />
              <span class="dot" [class]="'dot ' + mode.value.toLowerCase()" aria-hidden="true"></span>
              <span class="mode-text">
                <span class="mode-label">{{ mode.label }}</span>
                <span class="hint">{{ mode.description }}</span>
              </span>
            </label>
          }
        </div>
        @if (simulation.current()?.allowsRuntimeChanges === false) {
          <p class="hint">Runtime changes are turned off in this environment.</p>
        }
        @if (simulation.error(); as error) {
          <p class="field-error" role="alert">{{ error }}</p>
        }
      </section>

      <section class="block" aria-labelledby="checks-title">
        <h3 id="checks-title">Duplicate-request checks</h3>
        @if (store.completed(); as last) {
          <p class="hint">These call the real API using your last order, <strong>{{ last.order.id | orderRef }}</strong>.</p>
          <ul class="checks">
            <li>
              <button type="button" class="btn btn-secondary btn-sm" (click)="resendIdentical()" [disabled]="!!checks.running()">
                @if (checks.running() === 'identical') { <span class="spinner"></span> } Resend the same request
              </button>
              <span class="hint">Expected: the original order comes back and nothing new is created.</span>
            </li>
            <li>
              <button type="button" class="btn btn-secondary btn-sm" (click)="resendChanged()" [disabled]="!!checks.running()">
                @if (checks.running() === 'changed') { <span class="spinner"></span> } Reuse the key with a different quantity
              </button>
              <span class="hint">Expected: rejected, because the key belongs to a different order.</span>
            </li>
            <li>
              <button type="button" class="btn btn-secondary btn-sm" (click)="burst()" [disabled]="!!checks.running()">
                @if (checks.running() === 'burst') { <span class="spinner"></span> } Send 5 identical requests at once
              </button>
              <span class="hint">Expected: exactly one new order. This places a real order.</span>
            </li>
          </ul>

          @if (checks.results().length) {
            <ul class="results" aria-live="polite">
              @for (result of checks.results(); track result.id) {
                <li [class.pass]="result.passed">
                  <span class="mark"><app-icon [name]="result.passed ? 'check' : 'cross'" [size]="12" /></span>
                  <span class="result-text">
                    <span class="result-title">{{ result.title }}: {{ result.passed ? 'as expected' : 'unexpected result' }}</span>
                    <span class="hint">{{ result.summary }}. {{ result.detail }}</span>
                  </span>
                </li>
              }
            </ul>
          }

          <details class="tech">
            <summary>Last request details</summary>
            <dl>
              <dt>Idempotency-Key</dt>
              <dd class="data">{{ last.key }}</dd>
              <dt>Server response</dt>
              <dd>{{ last.replayed ? '200 OK, returned the existing order' : '201 Created, new order' }}</dd>
            </dl>
          </details>
        } @else {
          <p class="hint">Place an order first. These checks resend its request to show that repeats never create a second order.</p>
        }
      </section>

      <footer class="panel-foot">
        <a href="/scalar" target="_blank" rel="noopener">API reference</a>
      </footer>
    </aside>
  `,
  styles: `
    .panel {
      position: fixed;
      z-index: 50;
      top: 0;
      right: 0;
      bottom: 0;
      width: min(420px, 100vw);
      overflow-y: auto;
      padding: calc(20px + env(safe-area-inset-top, 0px)) 24px calc(24px + env(safe-area-inset-bottom, 0px));
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      align-content: start;
      gap: 24px;
      background: var(--sheet);
      border-left: 1px solid var(--rule);
      box-shadow: -12px 0 32px rgb(19 26 41 / 0.14);
      animation: slide-in 180ms ease-out;
    }
    @keyframes slide-in { from { transform: translateX(24px); opacity: 0; } }
    .panel-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; }
    .panel-head h2 { font-size: 1.25rem; font-weight: 700; margin-bottom: 4px; }
    .close {
      flex: none; display: grid; place-items: center; width: 40px; height: 40px;
      border: 0; border-radius: var(--radius-sm); background: transparent; color: var(--ink-2); cursor: pointer;
    }
    .close:hover { background: var(--sheet-inset); color: var(--ink); }
    .block { display: grid; gap: 10px; padding-top: 20px; border-top: 1px solid var(--rule); }
    h3 { font-size: 1rem; font-weight: 650; }
    .block strong { color: var(--ink); }
    .modes { display: grid; gap: 8px; }
    .mode {
      display: grid; grid-template-columns: auto auto minmax(0, 1fr); align-items: center; gap: 10px;
      padding: 10px 12px; border: 1px solid var(--rule-strong); border-radius: var(--radius-sm);
      cursor: pointer; font-weight: 400;
    }
    .mode.selected { border-color: var(--accent); background: var(--accent-soft); }
    .mode input { margin: 0; accent-color: var(--accent); }
    .dot { width: 10px; height: 10px; border-radius: 50%; }
    .dot.healthy { background: var(--ok); }
    .dot.flaky { background: var(--wait); }
    .dot.outage { background: var(--bad); }
    .mode-text { display: grid; }
    .mode-label { font-weight: 600; }
    .checks { margin: 0; padding: 0; list-style: none; display: grid; gap: 12px; }
    .checks li { display: grid; gap: 4px; justify-items: start; }
    .results { margin: 0; padding: 0; list-style: none; display: grid; gap: 8px; }
    .results li {
      display: grid; grid-template-columns: 22px minmax(0, 1fr); gap: 10px;
      padding: 10px 12px; border-radius: var(--radius-sm); background: var(--bad-soft);
    }
    .results li.pass { background: var(--ok-soft); }
    .mark { display: grid; place-items: center; width: 22px; height: 22px; border-radius: 50%; background: var(--sheet); color: var(--bad); }
    .pass .mark { color: var(--ok); }
    .result-text { display: grid; gap: 2px; min-width: 0; }
    .result-title { font-weight: 600; font-size: 0.875rem; }
    dl { margin: 8px 0 0; display: grid; gap: 4px; }
    dt { font-weight: 600; color: var(--ink); }
    dd { margin: 0 0 6px; overflow-wrap: anywhere; }
    .panel-foot { padding-top: 16px; border-top: 1px solid var(--rule); font-size: 0.875rem; }
    @media (max-width: 480px) { .panel { padding-inline: 16px; } }
  `,
})
export class TestToolsPanel {
  protected readonly tools = inject(TestTools);
  protected readonly simulation = inject(NotificationServiceSimulationStore);
  protected readonly checks = inject(IdempotencyChecks);
  protected readonly store = inject(OrderSubmissionStore);
  protected readonly modes = MESSAGING_MODES;

  private readonly closeButton = viewChild.required<ElementRef<HTMLButtonElement>>('closeButton');

  protected readonly canChangeMode = computed(() => {
    const current = this.simulation.current();
    return !!current && current.allowsRuntimeChanges && !this.simulation.saving();
  });

  constructor() {
    afterNextRender(() => this.closeButton().nativeElement.focus());
  }

  protected resendIdentical(): void {
    const last = this.store.completed();
    if (last) {
      this.checks.resendIdentical(last.key, last.request, last.order.id);
    }
  }

  protected resendChanged(): void {
    const last = this.store.completed();
    if (last) {
      this.checks.resendChanged(last.key, last.request);
    }
  }

  protected burst(): void {
    const last = this.store.completed();
    if (last) {
      this.checks.burst(last.request);
    }
  }
}
