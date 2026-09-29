export type NotificationStatus = 'Pending' | 'Delivered' | 'Failed';

export interface OrderLine {
  lineNumber: number;
  productCode: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface DeliveryAttempt {
  number: number;
  attemptedAt: string;
  succeeded: boolean;
  error: string | null;
}

export interface NotificationState {
  status: NotificationStatus;
  attemptCount: number;
  nextAttemptAt: string | null;
  deliveredAt: string | null;
  lastError: string | null;
  attempts: DeliveryAttempt[];
}

export interface Order {
  id: string;
  customerReference: string;
  total: number;
  placedAt: string;
  items: OrderLine[];
  notification: NotificationState;
}

export interface PlaceOrderItem {
  productCode: string;
  quantity: number;
  unitPrice: number;
}

export interface PlaceOrderRequest {
  customerReference: string;
  items: PlaceOrderItem[];
}

export interface PlaceOrderResponse {
  order: Order;
  /** True when the server recognised the Idempotency-Key and returned the original order. */
  replayed: boolean;
  status: number;
}

/** Mirrors the server's rules so most mistakes are caught before a round trip. */
export const OrderRules = {
  customerReferenceMaxLength: 64,
  productCodeMaxLength: 50,
  maxLines: 100,
  maxQuantity: 10_000,
  maxUnitPrice: 1_000_000,
} as const;

export const isTerminal = (order: Order): boolean => order.notification.status !== 'Pending';

/**
 * Canonical form of a request, used to decide whether a retry is "the same submission".
 * Mirrors the server's fingerprint: whitespace and trailing zeros do not matter, line order does.
 */
export function fingerprint(request: PlaceOrderRequest): string {
  return JSON.stringify([
    request.customerReference.trim(),
    request.items.map((item) => [item.productCode.trim(), Number(item.quantity), Number(item.unitPrice)]),
  ]);
}
