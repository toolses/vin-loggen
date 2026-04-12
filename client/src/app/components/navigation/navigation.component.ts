import { Component, effect, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { AdminService } from '../../services/admin.service';

@Component({
  selector: 'app-navigation',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './navigation.component.html',
})
export class NavigationComponent {
  protected readonly auth = inject(AuthService);
  protected readonly admin = inject(AdminService);

  constructor() {
    effect(() => {
      if (this.auth.isLoggedIn()) {
        this.admin.checkAdminStatus();
      }
    });
  }
}
