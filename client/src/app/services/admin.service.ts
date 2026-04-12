import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  private readonly _isAdmin = signal(false);
  private readonly _checked = signal(false);
  private readonly _loading = signal(false);

  readonly isAdmin = this._isAdmin.asReadonly();
  readonly checked = this._checked.asReadonly();
  readonly loading = this._loading.asReadonly();

  async checkAdminStatus(): Promise<void> {
    if (this._checked() || !this.auth.isLoggedIn()) return;
    this._loading.set(true);
    try {
      const result = await firstValueFrom(
        this.http.get<{ isAdmin: boolean }>(`${environment.apiBaseUrl}/admin/me`),
      );
      this._isAdmin.set(result.isAdmin);
    } catch {
      this._isAdmin.set(false);
    } finally {
      this._checked.set(true);
      this._loading.set(false);
    }
  }

  reset(): void {
    this._isAdmin.set(false);
    this._checked.set(false);
  }
}
