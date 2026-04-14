import { Component, effect, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { AdminService } from '../../services/admin.service';
import { ProfileService } from '../../services/profile.service';

@Component({
  selector: 'app-navigation',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './navigation.component.html',
})
export class NavigationComponent {
  protected readonly auth = inject(AuthService);
  protected readonly admin = inject(AdminService);
  protected readonly profile = inject(ProfileService);

  constructor() {
    effect(() => {
      if (this.auth.isLoggedIn()) {
        this.admin.checkAdminStatus();
        this.profile.loadProQuota();
      }
    });
  }
}
