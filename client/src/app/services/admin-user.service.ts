import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

export interface AdminUser {
  id: string;
  email: string | null;
  displayName: string | null;
  subscriptionTier: string;
  proScansToday: number;
  isAdmin: boolean;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class AdminUserService {
  private readonly http = inject(HttpClient);

  private readonly _users = signal<AdminUser[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _saving = signal<string | null>(null);
  private readonly _toast = signal<{ message: string; type: 'success' | 'error' } | null>(null);

  readonly users = this._users.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly saving = this._saving.asReadonly();
  readonly toast = this._toast.asReadonly();

  async loadUsers(search?: string): Promise<void> {
    this._loading.set(true);
    this._error.set(null);
    try {
      let params = new HttpParams();
      if (search) params = params.set('search', search);

      const result = await firstValueFrom(
        this.http.get<AdminUser[]>(`${environment.apiBaseUrl}/admin/users`, { params }),
      );
      this._users.set(result);
    } catch (err: any) {
      this._error.set(err?.message ?? 'Kunne ikke laste brukere');
    } finally {
      this._loading.set(false);
    }
  }

  async updateTier(userId: string, tier: string): Promise<boolean> {
    this._saving.set(userId);
    try {
      const updated = await firstValueFrom(
        this.http.patch<AdminUser>(
          `${environment.apiBaseUrl}/admin/users/${userId}/tier`,
          { subscriptionTier: tier },
        ),
      );

      this._users.update(users =>
        users.map(u => (u.id === userId ? updated : u)),
      );
      this.showToast(`Bruker oppdatert til ${tier}`, 'success');
      return true;
    } catch {
      this.showToast('Kunne ikke oppdatere bruker', 'error');
      return false;
    } finally {
      this._saving.set(null);
    }
  }

  private showToast(message: string, type: 'success' | 'error'): void {
    this._toast.set({ message, type });
    setTimeout(() => this._toast.set(null), 3000);
  }
}
