import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { firstValueFrom } from 'rxjs';
import { SupabaseService } from './supabase.service';
import { AuthService } from './auth.service';

export interface TasteProfile {
  preferredTypes: string[];
  topCountries: string[];
  topRegions: string[];
  averageRating: number;
  vintageRange: string | null;
  flavorDescriptors: string[];
  recommendation: string;
  personalityTitle: string;
}

export interface HomeAddress {
  name: string;
  lat: number;
  lng: number;
}

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);
  private readonly supabase = inject(SupabaseService);
  private readonly auth = inject(AuthService);

  private readonly _profile = signal<TasteProfile | null>(null);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _homeAddress = signal<HomeAddress | null>(null);

  // Pro quota
  private readonly _proScansToday  = signal(0);
  private readonly _dailyProLimit  = signal(10);
  private readonly _isPro          = signal(false);

  readonly profile = this._profile.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly homeAddress = this._homeAddress.asReadonly();

  readonly proScansToday   = this._proScansToday.asReadonly();
  readonly dailyProLimit   = this._dailyProLimit.asReadonly();
  readonly isPro           = this._isPro.asReadonly();
  readonly proScansRemaining = computed(() =>
    Math.max(0, this._dailyProLimit() - this._proScansToday()));

  async loadProfile(): Promise<void> {
    this._loading.set(true);
    this._error.set(null);
    try {
      const result = await firstValueFrom(
        this.http.get<TasteProfile>(`${environment.apiBaseUrl}/profile/taste`),
      );
      this._profile.set(result);
    } catch (err: any) {
      // 404 means not enough data — not an error to display
      if (err?.status === 404) {
        this._profile.set(null);
      } else {
        this._error.set(err?.message ?? 'Kunne ikke hente smaksprofil');
      }
    } finally {
      this._loading.set(false);
    }
  }

  async regenerateProfile(): Promise<void> {
    this._loading.set(true);
    this._error.set(null);
    try {
      const result = await firstValueFrom(
        this.http.post<TasteProfile>(`${environment.apiBaseUrl}/profile/taste/regenerate`, {}),
      );
      this._profile.set(result);
    } catch (err: any) {
      if (err?.status === 404) {
        this._profile.set(null);
      } else {
        this._error.set(err?.message ?? 'Kunne ikke regenerere smaksprofil');
      }
    } finally {
      this._loading.set(false);
    }
  }

  /** Loads the user's current Pro quota state from user_profiles. */
  async loadProQuota(): Promise<void> {
    const userId = this.auth.user()?.id;
    if (!userId) return;

    try {
      const { data, error } = await this.supabase.client
        .from('user_profiles')
        .select('subscription_tier, pro_scans_today, last_pro_scan_date')
        .eq('id', userId)
        .single();

      if (error) {
        console.warn('Could not load pro quota (migration may not be applied yet):', error.message);
        return;
      }

      if (data) {
        const today = new Date().toISOString().slice(0, 10);
        const isNewDay = data.last_pro_scan_date !== today;
        this._proScansToday.set(isNewDay ? 0 : (data.pro_scans_today ?? 0));
        this._isPro.set(data.subscription_tier === 'pro');
      }
    } catch {
      console.warn('Could not load pro quota');
    }
  }

  /** Syncs quota state from the most recent scan result (avoids a round-trip). */
  syncQuotaFromScan(proScansToday: number, dailyProLimit: number, isPro: boolean): void {
    this._proScansToday.set(proScansToday);
    this._dailyProLimit.set(dailyProLimit);
    this._isPro.set(isPro);
  }

  async loadHomeAddress(): Promise<void> {
    const userId = this.auth.user()?.id;
    if (!userId) return;

    try {
      const { data, error } = await this.supabase.client
        .from('user_profiles')
        .select('home_address_name, home_address_lat, home_address_lng')
        .eq('id', userId)
        .single();

      if (error) {
        console.warn('Could not load home address (migration may not be applied yet):', error.message);
        return;
      }

      if (data?.home_address_name && data.home_address_lat != null && data.home_address_lng != null) {
        this._homeAddress.set({
          name: data.home_address_name,
          lat: data.home_address_lat,
          lng: data.home_address_lng,
        });
      } else {
        this._homeAddress.set(null);
      }
    } catch {
      console.warn('Could not load home address');
    }
  }

  async saveHomeAddress(name: string, lat: number, lng: number): Promise<void> {
    const userId = this.auth.user()?.id;
    if (!userId) return;

    await this.supabase.client
      .from('user_profiles')
      .upsert({
        id: userId,
        home_address_name: name,
        home_address_lat: lat,
        home_address_lng: lng,
      });

    this._homeAddress.set({ name, lat, lng });
  }

  async clearHomeAddress(): Promise<void> {
    const userId = this.auth.user()?.id;
    if (!userId) return;

    await this.supabase.client
      .from('user_profiles')
      .update({
        home_address_name: null,
        home_address_lat: null,
        home_address_lng: null,
      })
      .eq('id', userId);

    this._homeAddress.set(null);
  }
}
