import { Injectable, signal } from '@angular/core';
import { environment } from '../../environments/environment';

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
      const url = `https://api.mapbox.com/geocoding/v5/mapbox.places/${lng},${lat}.json`
        + `?access_token=${this.token}&types=poi,address&limit=1&language=no`;

      const res = await fetch(url);
      const data = await res.json();
      const feature = data.features?.[0];
      if (!feature) return null;

      return {
        name: feature.text ?? feature.place_name,
        address: feature.place_name ?? '',
      };
    } catch {
      return null;
    }
  }

  async searchPlaces(query: string, proximity?: { lat: number; lng: number }): Promise<Place[]> {
    if (!this.token || !query.trim()) return [];

    try {
      let url = `https://api.mapbox.com/geocoding/v5/mapbox.places/${encodeURIComponent(query)}.json`
        + `?access_token=${this.token}&limit=5&language=no`;

      if (proximity) {
        url += `&proximity=${proximity.lng},${proximity.lat}`;
      }

      const res = await fetch(url);
      const data = await res.json();

      return (data.features ?? []).map((f: Record<string, unknown>) => ({
        name: f['text'] as string ?? '',
        address: f['place_name'] as string ?? '',
        lat: (f['center'] as number[])[1],
        lng: (f['center'] as number[])[0],
        category: ((f['properties'] as Record<string, unknown>)?.['category'] as string) ?? undefined,
      }));
    } catch {
      return [];
    }
  }
}
