import { Injectable, signal } from '@angular/core';

/** Open/closed state of the Test tools panel, shared by the header button and the panel. */
@Injectable({ providedIn: 'root' })
export class TestTools {
  private readonly _isOpen = signal(false);
  private returnFocusTo: HTMLElement | null = null;

  readonly isOpen = this._isOpen.asReadonly();

  open(): void {
    if (!this._isOpen()) {
      this.returnFocusTo = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      this._isOpen.set(true);
    }
  }

  close(): void {
    if (this._isOpen()) {
      this._isOpen.set(false);
      this.returnFocusTo?.focus();
      this.returnFocusTo = null;
    }
  }

  toggle(): void {
    if (this._isOpen()) {
      this.close();
    } else {
      this.open();
    }
  }
}
