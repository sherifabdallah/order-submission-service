import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { toApiError } from '../../core/http/api-error';

export type NotificationServiceMode = 'Healthy' | 'Flaky' | 'Outage';

export interface NotificationServiceSimulation {
  mode: NotificationServiceMode;
  failureRate: number;
  latencyMs: number;
  allowsRuntimeChanges: boolean;
}

const ENDPOINT = '/api/simulation/notification-service';

/** Reads and switches the behaviour of the fake notification service on the server. */
@Injectable({ providedIn: 'root' })
export class NotificationServiceSimulationStore {
  private readonly http = inject(HttpClient);

  readonly current = signal<NotificationServiceSimulation | null>(null);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  load(): void {
    this.http.get<NotificationServiceSimulation>(ENDPOINT).subscribe({
      next: (simulation) => this.current.set(simulation),
      error: (error: unknown) => this.error.set(toApiError(error).message),
    });
  }

  setMode(mode: NotificationServiceMode): void {
    if (this.current()?.mode === mode || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.http.put<NotificationServiceSimulation>(ENDPOINT, { mode }).subscribe({
      next: (simulation) => {
        this.current.set(simulation);
        this.saving.set(false);
      },
      error: (error: unknown) => {
        this.error.set(toApiError(error).message);
        this.saving.set(false);
      },
    });
  }
}
