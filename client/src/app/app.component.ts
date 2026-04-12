import { Component, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { NavigationComponent } from './components/navigation/navigation.component';
import { ToastContainerComponent } from './components/toast-container/toast-container.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, NavigationComponent, ToastContainerComponent],
  templateUrl: './app.component.html',
})
export class AppComponent {
  private readonly router = inject(Router);
  protected readonly isAdminRoute = signal(false);

  constructor() {
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe(e => {
        this.isAdminRoute.set(e.urlAfterRedirects.startsWith('/admin'));
      });
  }
}
