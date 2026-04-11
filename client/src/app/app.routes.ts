import { Routes } from '@angular/router';
import { authGuard } from './guards/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./components/login/login.component').then(m => m.LoginComponent),
  },
  {
    path: '',
    loadComponent: () =>
      import('./components/dashboard/dashboard.component').then(m => m.DashboardComponent),
  },
  {
    path: 'scan',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/scanner/scanner.component').then(m => m.ScannerComponent),
  },
  {
    path: 'edit',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/wine-editor/wine-editor.component').then(m => m.WineEditorComponent),
  },
  {
    path: 'cellar',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/wine-list/wine-list.component').then(m => m.WineListComponent),
  },
  {
    path: 'wines/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/wine-detail/wine-detail.component').then(m => m.WineDetailComponent),
  },
  {
    path: 'wines/:id/edit',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/wine-editor/wine-editor.component').then(m => m.WineEditorComponent),
  },
  {
    path: 'profile',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/profile/profile.component').then(m => m.ProfileComponent),
  },
  { path: '**', redirectTo: '' },
];
