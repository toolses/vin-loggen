import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/dashboard/dashboard.component').then(m => m.DashboardComponent),
  },
  {
    path: 'scan',
    loadComponent: () =>
      import('./components/scanner/scanner.component').then(m => m.ScannerComponent),
  },
  {
    path: 'edit',
    loadComponent: () =>
      import('./components/wine-editor/wine-editor.component').then(m => m.WineEditorComponent),
  },
  {
    path: 'cellar',
    loadComponent: () =>
      import('./components/wine-list/wine-list.component').then(m => m.WineListComponent),
  },
  { path: '**', redirectTo: '' },
];
