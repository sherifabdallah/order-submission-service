import { Injectable, signal } from '@angular/core';
import { safeStorage } from '../../../core/browser/storage';
import { Order } from './order.models';

export interface RecentOrder {
  id: string;
  customerReference: string;
  total: number;
  itemCount: number;
  placedAt: string;
}

const STORAGE_KEY = 'order-desk.recent-orders';
const CAPACITY = 6;

/** Orders placed from this browser, so they can be reopened after a reload. */
@Injectable({ providedIn: 'root' })
export class RecentOrdersStore {
  private readonly _orders = signal<RecentOrder[]>(safeStorage.read<RecentOrder[]>(STORAGE_KEY) ?? []);
  readonly orders = this._orders.asReadonly();

  remember(order: Order): void {
    const entry: RecentOrder = {
      id: order.id,
      customerReference: order.customerReference,
      total: order.total,
      itemCount: order.items.length,
      placedAt: order.placedAt,
    };
    this._orders.update((orders) => [entry, ...orders.filter((existing) => existing.id !== order.id)].slice(0, CAPACITY));
    safeStorage.write(STORAGE_KEY, this._orders());
  }
}
