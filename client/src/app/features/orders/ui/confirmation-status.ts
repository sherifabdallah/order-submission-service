import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { Clock } from '../../../core/browser/clock';
import { ClockTimePipe, countdown } from '../../../shared/ui/format';
import { Icon, IconName } from '../../../shared/ui/icon';
import { NotificationState } from '../data-access/order.models';

type Tone = 'info' | 'ok' | 'warn' | 'bad';

interface StatusView {
  tone: Tone;
  icon: IconName;
  badge: string;
  title: string;
  busy: boolean;
}

/**
 * The order confirmation message to the customer, in plain language. The raw attempt history
 * (including the provider's error messages) is available on demand.
 */
@Component({
  selector: 'app-confirmation-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ClockTimePipe, Icon],
  template: `
    <section class="card status" [class]="'card status ' + view().tone" aria-labelledby="confirmation-title" aria-live="polite">
      <header class="head">
        <h2 id="confirmation-title" class="card-title">Order confirmation</h2>
        <span class="badge">
          @if (view().busy) { <span class="spinner" aria-hidden="true"></span> } @else { <app-icon [name]="view().icon" [size]="14" /> }
          {{ view().badge }}
        </span>
      </header>

      <div class="message">
        <p class="title">{{ view().title }}</p>
        @switch (notification().status) {
          @case ('Delivered') {
            <p class="hint">Sent to customer {{ recipient() }} at {{ notification().deliveredAt | clockTime }}{{ attemptsNote() }}.</p>
          }
          @case ('Failed') {
            <p class="hint">
              {{ notification().attemptCount }} attempts failed. The order itself is saved; only the confirmation message is missing.
            </p>
          }
          @default {
            @if (notification().attemptCount === 0) {
              <p class="hint">Sending the confirmation to customer {{ recipient() }}.</p>
            } @else {
              <p class="hint">
                The messaging service did not respond.
                @if (nextTry() === 'now') { Trying again now. } @else { Next automatic try {{ nextTry() }}. }
                Your order is saved; only the confirmation is delayed.
              </p>
            }
          }
        }
      </div>

      @if (canRetry()) {
        <button type="button" class="btn btn-secondary btn-sm retry" (click)="retry.emit()" [disabled]="busy()">
          @if (busy()) { <span class="spinner" aria-hidden="true"></span> } @else { <app-icon name="send" [size]="14" /> }
          {{ notification().status === 'Failed' ? 'Send it again' : 'Retry now' }}
        </button>
      }

      @if (notification().attempts.length) {
        <details class="history">
          <summary>Delivery history ({{ notification().attempts.length }} {{ notification().attempts.length === 1 ? 'attempt' : 'attempts' }})</summary>
          <ol>
            @for (attempt of notification().attempts; track attempt.number) {
              <li [class.ok]="attempt.succeeded">
                <span class="time num">{{ attempt.attemptedAt | clockTime }}</span>
                <span>
                  Attempt {{ attempt.number }}: {{ attempt.succeeded ? 'sent' : 'failed' }}
                  @if (attempt.error) { <span class="error">{{ attempt.error }}</span> }
                </span>
              </li>
            }
          </ol>
        </details>
      }
    </section>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    .status { padding: 24px; display: grid; grid-template-columns: minmax(0, 1fr); gap: 14px; border-top: 3px solid var(--tone); }
    .status.info { --tone: var(--accent); --tone-soft: var(--accent-soft); }
    .status.ok { --tone: var(--ok); --tone-soft: var(--ok-soft); }
    .status.warn { --tone: var(--wait); --tone-soft: var(--wait-soft); }
    .status.bad { --tone: var(--bad); --tone-soft: var(--bad-soft); }
    .head { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
    .badge {
      display: inline-flex; align-items: center; gap: 6px; padding: 3px 10px;
      border-radius: 999px; background: var(--tone-soft); color: var(--tone);
      font-size: 0.8125rem; font-weight: 600; white-space: nowrap;
    }
    .badge .spinner { width: 12px; height: 12px; }
    .message { display: grid; gap: 4px; }
    .title { font-size: 1.0625rem; font-weight: 650; }
    .retry { justify-self: start; }
    .history { font-size: 0.875rem; }
    .history summary { cursor: pointer; color: var(--ink-2); width: fit-content; }
    .history ol { margin: 10px 0 0; padding: 0; list-style: none; display: grid; gap: 8px; }
    .history li { display: grid; grid-template-columns: 4.5rem minmax(0, 1fr); gap: 8px; color: var(--ink); }
    .history .time { color: var(--ink-2); }
    .history .error { display: block; color: var(--ink-2); font-size: 0.8125rem; overflow-wrap: anywhere; }
    .history li.ok { color: var(--ok); }
    @media (max-width: 520px) { .status { padding: 20px 16px; } }
  `,
})
export class ConfirmationStatus {
  private readonly clock = inject(Clock);

  readonly notification = input.required<NotificationState>();
  readonly recipient = input.required<string>();
  readonly busy = input(false);
  readonly retry = output<void>();

  protected readonly attemptsNote = computed(() => {
    const attempts = this.notification().attemptCount;
    return attempts > 1 ? ` after ${attempts} attempts` : '';
  });

  protected readonly nextTry = computed(() => countdown(this.notification().nextAttemptAt, this.clock.now()));

  protected readonly canRetry = computed(() => {
    const notification = this.notification();
    return notification.status === 'Failed' || (notification.status === 'Pending' && notification.attemptCount > 0 && this.nextTry() !== 'now');
  });

  protected readonly view = computed<StatusView>(() => {
    const notification = this.notification();
    switch (notification.status) {
      case 'Delivered':
        return { tone: 'ok', icon: 'check', badge: 'Sent', title: 'Confirmation sent', busy: false };
      case 'Failed':
        return { tone: 'bad', icon: 'cross', badge: 'Not sent', title: 'The confirmation could not be sent', busy: false };
      default:
        return notification.attemptCount === 0
          ? { tone: 'info', icon: 'send', badge: 'Sending', title: 'Sending confirmation…', busy: true }
          : { tone: 'warn', icon: 'clock', badge: 'Retrying', title: 'Confirmation delayed', busy: false };
    }
  });
}
