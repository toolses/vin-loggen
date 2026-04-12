import { Injectable, signal } from '@angular/core';
import { environment } from '../../environments/environment';

export interface PlaceSuggestion {
  mapbox_id: string;
  name: string;
  address: string;
  category?: string;
}

export interface Place {
  name: string;
  address: string;
  lat: number;
  lng: number;
  category?: string;
}

@Injectable({ providedIn: 'root' })
export class LocationService {
  private readonly token = environment.mapboxToken;
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

  async reverseGeocode(lat: number, lng: number): Promise<{ name: string; address: string } | null> {
    if (!this.token) return null;

    try {
      const url = `https://api.mapbox.com/search/geocode/v6/reverse`
        + `?longitude=${lng}&latitude=${lat}`
        + `&access_token=${this.token}&types=address&limit=1&language=no`;

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

  async searchPlaces(query: string, proximity?: { lat: number; lng: number }): Promise<PlaceSuggestion[]> {
    if (!this.token || !query.trim()) return [];

    try {
      let url = `https://api.mapbox.com/search/searchbox/v1/suggest`
        + `?q=${encodeURIComponent(query)}`
        + `&access_token=${this.token}&session_token=${this.sessionToken}`
        + `&limit=5&language=no&types=poi,address,place,neighborhood,street`;

      if (proximity) {
        url += `&proximity=${proximity.lng},${proximity.lat}`;
      }

      const res = await fetch(url);
      const data = await res.json();

      return (data.suggestions ?? []).map((s: Record<string, unknown>) => ({
        mapbox_id: (s['mapbox_id'] as string) ?? '',
        name: (s['name'] as string) ?? '',
        address: (s['full_address'] as string) ?? (s['place_formatted'] as string) ?? '',
        category: ((s['poi_category'] as string[]) ?? [])[0] ?? undefined,
      }));
    } catch {
      return [];
    }
  }

  async retrievePlace(mapboxId: string): Promise<Place | null> {
    if (!this.token || !mapboxId) return null;

    try {
      const url = `https://api.mapbox.com/search/searchbox/v1/retrieve/${mapboxId}`
        + `?access_token=${this.token}&session_token=${this.sessionToken}`;

      const res = await fetch(url);
      const data = await res.json();
      const feature = data.features?.[0];
      if (!feature) return null;

      const props = feature.properties ?? {};
      const coords = feature.geometry?.coordinates ?? [0, 0];

      // Rotate session token after a complete suggest → retrieve cycle
      this.sessionToken = crypto.randomUUID();

      return {
        name: props.name ?? '',
        address: props.full_address ?? '',
        lat: coords[1],
        lng: coords[0],
        category: (props.poi_category ?? [])[0] ?? undefined,
      };
    } catch {
      return null;
    }
  }
}
