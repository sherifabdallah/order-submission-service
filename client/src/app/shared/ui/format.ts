import { Pipe, PipeTransform } from '@angular/core';

const money = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const clockTime = new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const dateTime = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });

export const formatMoney = (value: number): string => money.format(value);

/** A short, readable order number: the last 8 hex digits of the id, e.g. "#7233D41F". */
export const orderReference = (id: string): string => `#${id.replace(/-/g, '').slice(-8).toUpperCase()}`;

/** Amounts are in the store's single currency (see README assumptions), shown with two decimals. */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    return value == null || Number.isNaN(value) ? '—' : formatMoney(value);
  }
}

/** 24-hour time with seconds, the resolution delivery attempts happen at. */
@Pipe({ name: 'clockTime' })
export class ClockTimePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return value ? clockTime.format(new Date(value)) : '—';
  }
}

/** "27 Sep 2026, 08:36" */
@Pipe({ name: 'dateTime' })
export class DateTimePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return value ? dateTime.format(new Date(value)) : '—';
  }
}

@Pipe({ name: 'orderRef' })
export class OrderRefPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return value ? orderReference(value) : '';
  }
}

/** Time until a timestamp, e.g. "in 3.4s", "in 12s" or "now". */
export function countdown(target: string | null, now: number): string {
  if (!target) {
    return '';
  }
  const seconds = (Date.parse(target) - now) / 1000;
  if (seconds <= 0.05) {
    return 'now';
  }
  return seconds < 10 ? `in ${seconds.toFixed(1)}s` : `in ${Math.round(seconds)}s`;
}
