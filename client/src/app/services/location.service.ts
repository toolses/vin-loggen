import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

export interface PlaceSuggestion {
  place_id: string;
  name: string;
  address: string;
}

export interface Place {
  name: string;
  address: string;
  lat: number;
  lng: number;
}

/** Backend DTO for autocomplete results */
interface AutocompleteSuggestionDto {
  placeId: string;
  mainText: string;
  secondaryText: string;
}

/** Backend DTO for place details */
interface PlaceDetailsDto {
  placeId: string;
  name: string;
  address: string | null;
  lat: number;
  lng: number;
  types: string[] | null;
}

@Injectable({ providedIn: 'root' })
export class LocationService {
  private readonly http = inject(HttpClient);
  private readonly mapboxToken = environment.mapboxToken;
  private sessionToken = crypto.randomUUID();

  readonly permissionState = signal<'prompt' | 'granted' | 'denied'>('prompt');

  constructor() {
    this.checkPermission();
  }

  private async checkPermission(): Promise<void> {
    try {
      const status = await navigator.permissions.query({ name: 'geolocation' });
      this.permissionState.set(status.state as 'prompt' | 'granted' | 'denied');
      status.addEventListener('change', () => {
        this.permissionState.set(status.state as 'prompt' | 'granted' | 'denied');
      });
    } catch {
      // Permissions API not supported — remain 'prompt'
    }
  }

  getCurrentPosition(): Promise<{ lat: number; lng: number }> {
    return new Promise((resolve, reject) => {
      if (!navigator.geolocation) {
        reject(new Error('Geolocation not supported'));
        return;
      }

      navigator.geolocation.getCurrentPosition(
        (pos) => {
          this.permissionState.set('granted');
          resolve({ lat: pos.coords.latitude, lng: pos.coords.longitude });
        },
        (err) => {
          if (err.code === err.PERMISSION_DENIED) {
            this.permissionState.set('denied');
          }
          reject(err);
        },
        { enableHighAccuracy: true, timeout: 10000, maximumAge: 60000 },
      );
    });
  }

  /** Reverse geocode via Mapbox (kept for "current position" quick action). */
  async reverseGeocode(lat: number, lng: number): Promise<{ name: string; address: string } | null> {
    if (!this.mapboxToken) return null;

    try {
      const url = `https://api.mapbox.com/search/geocode/v6/reverse`
        + `?longitude=${lng}&latitude=${lat}`
        + `&access_token=${this.mapboxToken}&types=address&limit=1&language=no`;

      const res = await fetch(url);
      const data = await res.json();
      const feature = data.features?.[0];
      if (!feature) return null;

      const props = feature.properties ?? {};
      return {
        name: props.name ?? props.full_address ?? '',
        address: props.full_address ?? '',
      };
    } catch {
      return null;
    }
  }

  /** Search places via Google Places Autocomplete (proxied through backend). */
  async searchPlaces(query: string, proximity?: { lat: number; lng: number }): Promise<PlaceSuggestion[]> {
    if (!query.trim()) return [];

    try {
      const params: Record<string, string> = {
        query,
        sessionToken: this.sessionToken,
      };
      if (proximity) {
        params['lat'] = proximity.lat.toString();
        params['lng'] = proximity.lng.toString();
      }

      const results = await firstValueFrom(
        this.http.get<AutocompleteSuggestionDto[]>(
          `${environment.apiBaseUrl}/locations/autocomplete`, { params }),
      );

      return (results ?? []).map((s) => ({
        place_id: s.placeId,
        name: s.mainText,
        address: s.secondaryText,
      }));
    } catch {
      return [];
    }
  }

  /** Retrieve full place details via Google Places (proxied through backend). */
  async retrievePlace(placeId: string): Promise<Place | null> {
    if (!placeId) return null;

    try {
      const result = await firstValueFrom(
        this.http.get<PlaceDetailsDto>(
          `${environment.apiBaseUrl}/locations/details`,
          { params: { placeId, sessionToken: this.sessionToken } }),
      );

      // Rotate session token after a complete autocomplete → details cycle
      this.sessionToken = crypto.randomUUID();

      if (!result) return null;

      return {
        name: result.name,
        address: result.address ?? '',
        lat: result.lat,
        lng: result.lng,
      };
    } catch {
      return null;
    }
  }
}
