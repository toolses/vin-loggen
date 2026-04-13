import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

export interface ProviderUsageToday {
  provider: string;
  endpoint: string;
  totalCalls: number;
  avgResponseMs: number;
  errorCount: number;
}

export interface DailyUsageRow {
  date: string;
  provider: string;
  endpoint: string;
  totalCalls: number;
  avgResponseMs: number;
}

@Injectable({ providedIn: 'root' })
export class AdminUsageService {
  private readonly http = inject(HttpClient);

  private readonly _todayUsage = signal<ProviderUsageToday[]>([]);
  private readonly _dailyUsage = signal<DailyUsageRow[]>([]);
  private readonly _loading = signal(false);

  readonly todayUsage = this._todayUsage.asReadonly();
  readonly dailyUsage = this._dailyUsage.asReadonly();
  readonly loading = this._loading.asReadonly();

  async loadTodayUsage(): Promise<void> {
    this._loading.set(true);
    try {
      const result = await firstValueFrom(
        this.http.get<ProviderUsageToday[]>(`${environment.apiBaseUrl}/admin/usage/today`),
      );
      this._todayUsage.set(result);
    } catch {
      this._todayUsage.set([]);
    } finally {
      this._loading.set(false);
    }
  }

  async loadDailyUsage(days: number = 30): Promise<void> {
    try {
      const params = new HttpParams().set('days', days.toString());
      const result = await firstValueFrom(
        this.http.get<DailyUsageRow[]>(`${environment.apiBaseUrl}/admin/usage/daily`, { params }),
      );
      this._dailyUsage.set(result);
    } catch {
      this._dailyUsage.set([]);
    }
  }
}
