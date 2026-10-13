import { Routes } from '@angular/router';
import { Shell } from './layout/shell';

export const routes: Routes = [
  {
    path: '',
    component: Shell,
    children: [
      {
        path: '',
        title: 'Pulpit · Broker Ledger',
        loadComponent: () => import('./features/dashboard/dashboard-page').then((m) => m.DashboardPage),
      },
      {
        path: 'zlecenia',
        title: 'Zlecenia · Broker Ledger',
        loadComponent: () => import('./features/orders/orders-page').then((m) => m.OrdersPage),
      },
      {
        path: 'ksiega',
        title: 'Księga · Broker Ledger',
        loadComponent: () => import('./features/ledger/ledger-page').then((m) => m.LedgerPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
