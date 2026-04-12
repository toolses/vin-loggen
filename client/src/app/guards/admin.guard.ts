import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { AdminService } from '../services/admin.service';

export const adminGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const admin = inject(AdminService);
  const router = inject(Router);

  // Wait for the initial session restore to complete
  if (auth.loading()) {
    await new Promise<void>(resolve => {
      const check = () => {
        if (!auth.loading()) {
          resolve();
        } else {
          setTimeout(check, 50);
        }
      };
      check();
    });
  }

  if (!auth.isLoggedIn()) {
    return router.createUrlTree(['/login']);
  }

  await admin.checkAdminStatus();

  if (!admin.isAdmin()) {
    return router.createUrlTree(['/']);
  }

  return true;
};
