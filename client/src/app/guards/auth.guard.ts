import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
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

  if (auth.isLoggedIn()) {
    return true;
  }

  return router.createUrlTree(['/login']);
};
