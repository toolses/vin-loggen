import { Routes } from '@angular/router';
import { authGuard } from './guards/auth.guard';
import { adminGuard } from './guards/admin.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./components/login/login.component').then(m => m.LoginComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
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
    path: 'expert',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./components/expert/expert.component').then(m => m.ExpertComponent),
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
  {
    path: 'admin',
    canActivate: [adminGuard],
    loadComponent: () =>
      import('./components/admin/admin-layout/admin-layout.component').then(
        m => m.AdminLayoutComponent,
      ),
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./components/admin/admin-dashboard/admin-dashboard.component').then(
            m => m.AdminDashboardComponent,
          ),
      },
      {
        path: 'wines',
        loadComponent: () =>
          import('./components/admin/admin-wine-list/admin-wine-list.component').then(
            m => m.AdminWineListComponent,
          ),
      },
      {
        path: 'wines/:id',
        loadComponent: () =>
          import('./components/admin/admin-wine-editor/admin-wine-editor.component').then(
            m => m.AdminWineEditorComponent,
          ),
      },
      {
        path: 'users',
        loadComponent: () =>
          import('./components/admin/admin-user-list/admin-user-list.component').then(
            m => m.AdminUserListComponent,
          ),
      },
      {
        path: 'corrections',
        loadComponent: () =>
          import('./components/admin/admin-corrections/admin-correction-list.component').then(
            m => m.AdminCorrectionListComponent,
          ),
      },
      {
        path: 'corrections/:id',
        loadComponent: () =>
          import('./components/admin/admin-corrections/admin-correction-detail.component').then(
            m => m.AdminCorrectionDetailComponent,
          ),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
