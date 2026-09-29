import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type IconName =
  | 'plus'
  | 'minus'
  | 'trash'
  | 'check'
  | 'cross'
  | 'clock'
  | 'retry'
  | 'copy'
  | 'arrow-left'
  | 'alert'
  | 'info'
  | 'tools'
  | 'send';

/** Small stroke icons drawn inline so they inherit colour and need no icon font. */
@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true', style: 'display:inline-flex' },
  template: `
    <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">
      @switch (name()) {
        @case ('plus') { <path d="M8 3v10M3 8h10" /> }
        @case ('minus') { <path d="M3 8h10" /> }
        @case ('trash') { <path d="M3 4.5h10M6.5 4.5V3h3v1.5M4.5 4.5l.6 8.5h5.8l.6-8.5" /> }
        @case ('check') { <path d="M3.5 8.5l3 3 6-7" /> }
        @case ('cross') { <path d="M4.5 4.5l7 7M11.5 4.5l-7 7" /> }
        @case ('clock') { <circle cx="8" cy="8" r="5.5" /><path d="M8 5v3.2l2 1.3" /> }
        @case ('retry') { <path d="M12.5 8a4.5 4.5 0 1 1-1.3-3.2M12.5 3v2.5H10" /> }
        @case ('copy') { <rect x="5.5" y="5.5" width="7" height="7" rx="1" /><path d="M3.5 10.5v-6a1 1 0 0 1 1-1h6" /> }
        @case ('arrow-left') { <path d="M13 8H3.5M7.5 4l-4 4 4 4" /> }
        @case ('alert') { <path d="M8 2.5l6 10.5H2L8 2.5z" /><path d="M8 6.5v3M8 11.3v.2" /> }
        @case ('info') { <circle cx="8" cy="8" r="5.5" /><path d="M8 7.3v3.7M8 5v.2" /> }
        @case ('tools') { <path d="M6 2.5h4M7 2.5v4l-3.5 6a1 1 0 0 0 .9 1.5h7.2a1 1 0 0 0 .9-1.5L9 6.5v-4M5 10h6" /> }
        @case ('send') { <path d="M2.5 8L13.5 3l-3 10.5-2.5-4.5-5.5-1z" /><path d="M8 9l5.5-6" /> }
      }
    </svg>
  `,
})
export class Icon {
  readonly name = input.required<IconName>();
  readonly size = input(16);
}
