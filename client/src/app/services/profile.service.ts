import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { firstValueFrom } from 'rxjs';

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

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);

  private readonly _profile = signal<TasteProfile | null>(null);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);

  readonly profile = this._profile.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();

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
}
