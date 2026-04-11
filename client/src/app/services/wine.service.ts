import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { firstValueFrom } from 'rxjs';
import { SupabaseService } from './supabase.service';

export interface Wine {
  id: string;
  name: string;
  producer: string;
  vintage: number | null;
  type: string;
  country: string | null;
  region: string | null;
  rating: number | null;
  notes: string | null;
  image_url: string | null;
  tasted_at: string | null;
  location_name: string | null;
  location_lat: number | null;
  location_lng: number | null;
  location_type: string | null;
  created_at: string;
}

export type NewWine = Omit<Wine, 'id' | 'created_at'>;

/** Matches the WineAnalysisResponse record returned by POST /api/wine/analyze */
export interface WineAnalysisResult {
  wineName: string | null;
  producer: string | null;
  vintage: number | null;
  country: string | null;
  region: string | null;
  grapes: string[] | null;
  type: string | null;
  alcoholContent: number | null;
}

@Injectable({ providedIn: 'root' })
export class WineService {
  private readonly http = inject(HttpClient);
  private readonly supabase = inject(SupabaseService).client;

  private readonly _wines = signal<Wine[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _processing = signal(false);
  private readonly _lastScanResult = signal<WineAnalysisResult | null>(null);
  private readonly _lastScanImageUrl = signal<string | null>(null);
  private readonly _lastScanLocation = signal<{ lat: number; lng: number } | null>(null);

  readonly wines = this._wines.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly wineCount = computed(() => this._wines().length);
  readonly processing = this._processing.asReadonly();
  readonly lastScanResult = this._lastScanResult.asReadonly();
  readonly lastScanImageUrl = this._lastScanImageUrl.asReadonly();
  readonly lastScanLocation = this._lastScanLocation.asReadonly();

  async loadWines(): Promise<void> {
    this._loading.set(true);
    this._error.set(null);
    try {
      const { data, error } = await this.supabase
        .from('wines')
        .select('*')
        .order('created_at', { ascending: false });
      if (error) throw error;
      this._wines.set(data ?? []);
    } catch (err) {
      this._error.set(err instanceof Error ? err.message : 'Ukjent feil oppstod');
    } finally {
      this._loading.set(false);
    }
  }

  async addWine(wine: NewWine): Promise<boolean> {
    this._error.set(null);
    const { data: { user } } = await this.supabase.auth.getUser();
    const { error } = await this.supabase.from('wines').insert({
      ...wine,
      user_id: user?.id,
    });
    if (error) {
      this._error.set(error.message);
      return false;
    }
    await this.loadWines();
    return true;
  }

  getWine(id: string): Wine | undefined {
    return this._wines().find(w => w.id === id);
  }

  async fetchWine(id: string): Promise<Wine | null> {
    const { data, error } = await this.supabase
      .from('wines')
      .select('*')
      .eq('id', id)
      .single();
    if (error || !data) return null;
    return data as Wine;
  }

  async updateWine(id: string, wine: Partial<NewWine>): Promise<boolean> {
    this._error.set(null);
    const { error } = await this.supabase
      .from('wines')
      .update(wine)
      .eq('id', id);
    if (error) {
      this._error.set(error.message);
      return false;
    }
    await this.loadWines();
    return true;
  }

  async deleteWine(id: string): Promise<boolean> {
    this._error.set(null);
    const { error } = await this.supabase
      .from('wines')
      .delete()
      .eq('id', id);
    if (error) {
      this._error.set(error.message);
      return false;
    }
    await this.loadWines();
    return true;
  }

  async uploadLabelImage(file: File): Promise<string | null> {
    const ext = file.name.split('.').pop() ?? 'jpg';
    const path = `labels/${Date.now()}.${ext}`;

    const { data, error } = await this.supabase.storage
      .from('wine-labels')
      .upload(path, file, { contentType: file.type, upsert: false });

    if (error) {
      console.error('Storage upload error:', JSON.stringify(error));
      this._error.set(error.message);
      return null;
    }

    const { data: urlData } = this.supabase.storage
      .from('wine-labels')
      .getPublicUrl(data.path);

    return urlData.publicUrl;
  }

  /**
   * Sends the raw image blob as multipart/form-data to POST /api/wine/analyze.
   * The backend calls Gemini 2.0 Flash and returns structured wine data.
   * Sets the `processing` signal while the request is in flight.
   */
  async analyzeLabel(file: Blob): Promise<WineAnalysisResult | null> {
    this._processing.set(true);
    this._error.set(null);
    try {
      const formData = new FormData();
      formData.append('image', file, 'label.jpg');

      const result = await firstValueFrom(
        this.http.post<WineAnalysisResult>(`${environment.apiBaseUrl}/wine/analyze`, formData)
      );
      this._lastScanResult.set(result);
      return result;
    } catch (err: unknown) {
      const detail = (err as { error?: { detail?: string } })?.error?.detail
        ?? (err instanceof Error ? err.message : 'Kunne ikke analysere etiketten');
      this._error.set(detail);
      return null;
    } finally {
      this._processing.set(false);
    }
  }

  /** Called by ScannerComponent after the Supabase upload completes. */
  setScanImageUrl(url: string): void {
    this._lastScanImageUrl.set(url);
  }

  setScanLocation(lat: number, lng: number): void {
    this._lastScanLocation.set({ lat, lng });
  }

  clearScanResult(): void {
    this._lastScanResult.set(null);
    this._lastScanImageUrl.set(null);
    this._lastScanLocation.set(null);
  }
}
