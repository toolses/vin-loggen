import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AdminSettingsService {
  private readonly http = inject(HttpClient);

  private readonly _settings = signal<Record<string, string>>({});
  private readonly _loading = signal(false);

  readonly settings = this._settings.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly expertMode = computed(() => this._settings()['expert_mode'] ?? 'type');

  async loadSettings(): Promise<void> {
    this._loading.set(true);
    try {
      const result = await firstValueFrom(
        this.http.get<Record<string, string>>(`${environment.apiBaseUrl}/admin/settings`),
      );
      this._settings.set(result);
    } catch {
      this._settings.set({});
    } finally {
      this._loading.set(false);
    }
  }

  async updateSetting(key: string, value: string): Promise<boolean> {
    try {
      await firstValueFrom(
        this.http.put(`${environment.apiBaseUrl}/admin/settings/${encodeURIComponent(key)}`, { value }),
      );
      this._settings.update(s => ({ ...s, [key]: value }));
      return true;
    } catch {
      return false;
    }
  }
}
