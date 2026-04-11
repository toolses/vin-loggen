import { Injectable, inject, signal } from '@angular/core';
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

  readonly profile = this._profile.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly homeAddress = this._homeAddress.asReadonly();

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
