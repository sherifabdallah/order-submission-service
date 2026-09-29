import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'New order · Order Desk',
    loadComponent: () => import('./features/orders/pages/new-order-page').then((m) => m.NewOrderPage),
  },
  {
    path: 'orders/:id',
    title: 'Order · Order Desk',
    loadComponent: () => import('./features/orders/pages/order-page').then((m) => m.OrderPage),
  },
  { path: '**', redirectTo: '' },
];
