import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { NotificationServiceSimulationStore } from './features/simulation/notification-service-simulation';
import { TestTools } from './features/test-tools/test-tools';
import { MESSAGING_MODES, TestToolsPanel } from './features/test-tools/test-tools-panel';
import { Icon } from './shared/ui/icon';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, Icon, TestToolsPanel],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  protected readonly tools = inject(TestTools);
  protected readonly simulation = inject(NotificationServiceSimulationStore);

  /** Shown in the header whenever messaging is deliberately broken, so failures are never a surprise. */
  protected readonly degradedMode = computed(() => {
    const mode = this.simulation.current()?.mode;
    return mode && mode !== 'Healthy' ? MESSAGING_MODES.find((option) => option.value === mode) ?? null : null;
  });

  ngOnInit(): void {
    this.simulation.load();
  }
}
