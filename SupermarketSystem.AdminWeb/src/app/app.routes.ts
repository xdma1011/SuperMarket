import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { redirectIfAuthenticatedGuard } from './core/guards/redirect-if-authenticated.guard';
import { requirePermissionGuard } from './core/guards/require-permission.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [redirectIfAuthenticatedGuard],
    loadComponent: () => import('./auth/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: '',
    loadComponent: () => import('./layout/shell/shell.component').then(m => m.ShellComponent),
    canActivate: [authGuard],
    children: [
      {
        path: '',
        loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent)
      },
      {
        path: 'sales',
        canActivate: [requirePermissionGuard('Sales.Create')],
        loadComponent: () => import('./features/sales/sales.component').then(m => m.SalesComponent)
      },
      {
        path: 'returns',
        canActivate: [requirePermissionGuard('Returns.Process')],
        loadComponent: () => import('./features/returns/returns.component').then(m => m.ReturnsComponent)
      },
      {
        path: 'purchases',
        canActivate: [requirePermissionGuard('Purchasing.Create')],
        loadComponent: () => import('./features/purchasing/purchasing.component').then(m => m.PurchasingComponent)
      },
      {
        path: 'catalog',
        canActivate: [requirePermissionGuard('Catalog.Manage')],
        loadComponent: () => import('./features/catalog/catalog.component').then(m => m.CatalogComponent)
      },
      {
        path: 'suppliers',
        canActivate: [requirePermissionGuard('Suppliers.Manage')],
        loadComponent: () => import('./features/suppliers/suppliers.component').then(m => m.SuppliersComponent)
      },
      {
        path: 'stocktakes',
        canActivate: [requirePermissionGuard('Stocktake.Manage')],
        loadComponent: () => import('./features/stocktakes/stocktakes.component').then(m => m.StocktakesComponent)
      },
      {
        path: 'stocktakes/:id',
        canActivate: [requirePermissionGuard('Stocktake.Manage')],
        loadComponent: () => import('./features/stocktakes/stocktake-detail.component').then(m => m.StocktakeDetailComponent)
      },
      {
        path: 'reports',
        canActivate: [requirePermissionGuard('Reports.View')],
        loadComponent: () => import('./features/reports/reports.component').then(m => m.ReportsComponent)
      },
      {
        path: 'backup',
        canActivate: [requirePermissionGuard('Backups.Manage')],
        loadComponent: () => import('./features/backup/backup.component').then(m => m.BackupComponent)
      },
      {
        path: 'notifications',
        canActivate: [requirePermissionGuard('Notifications.View')],
        loadComponent: () => import('./features/notifications/notifications.component').then(m => m.NotificationsComponent)
      },
      {
        path: 'reviews',
        canActivate: [requirePermissionGuard('Returns.Review')],
        loadComponent: () => import('./features/reviews/reviews.component').then(m => m.ReviewsComponent)
      },
      {
        path: 'current-stock',
        canActivate: [requirePermissionGuard('Reports.View')],
        loadComponent: () => import('./features/current-stock/current-stock.component').then(m => m.CurrentStockComponent)
      },
      {
        path: 'complimentary',
        canActivate: [requirePermissionGuard('Inventory.ComplimentaryIssue')],
        loadComponent: () => import('./features/complimentary/complimentary.component').then(m => m.ComplimentaryComponent)
      },
      {
        path: 'users',
        canActivate: [requirePermissionGuard('Users.Manage')],
        loadComponent: () => import('./features/users/users.component').then(m => m.UsersComponent)
      },
      {
        path: 'sessions',
        canActivate: [requirePermissionGuard('Sessions.Manage')],
        loadComponent: () => import('./features/sessions/sessions.component').then(m => m.SessionsComponent)
      }
    ]
  },
  { path: '**', redirectTo: '' }
];
