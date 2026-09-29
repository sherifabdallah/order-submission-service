import { DestroyRef, Injectable, inject, signal } from '@angular/core';

/** A shared "now" signal for countdowns, ticking four times a second. */
@Injectable({ providedIn: 'root' })
export class Clock {
  private readonly _now = signal(Date.now());
  readonly now = this._now.asReadonly();

  constructor() {
    const handle = setInterval(() => this._now.set(Date.now()), 250);
    inject(DestroyRef).onDestroy(() => clearInterval(handle));
  }
}
