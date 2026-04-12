import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { firstValueFrom } from 'rxjs';
import { SupabaseService } from './supabase.service';
import { NotificationService } from './notification.service';

// ── Master wine data (from the wines table) ───────────────────────────────────
// Combined with the user's latest log via the wine_entries view.
export interface Wine {
  id: string;                      // wine master id  (used in routes /wines/:id)
  log_id: string;                  // latest wine_log id (used for edit / delete)
  name: string;
  producer: string;
  vintage: number | null;
  type: string;
  country: string | null;
  region: string | null;
  grapes: string[] | null;
  alcohol_content: number | null;
  external_source_id: string | null;
  food_pairings: string[] | null;
  description: string | null;
  technical_notes: string | null;
  // From the latest wine_log
  rating: number | null;
  notes: string | null;
  image_url: string | null;
  thumbnail_url: string | null;
  tasted_at: string | null;
  location_name: string | null;
  location_lat: number | null;
  location_lng: number | null;
  location_type: string | null;
  created_at: string;              // log's created_at
  log_count: number;               // how many times this user has logged this wine
}

// ── Single tasting event ──────────────────────────────────────────────────────
export interface WineLog {
  id: string;
  wine_id: string;
  user_id: string;
  rating: number | null;
  notes: string | null;
  image_url: string | null;
  thumbnail_url: string | null;
  tasted_at: string | null;
  location_name: string | null;
  location_lat: number | null;
  location_lng: number | null;
  location_type: string | null;
  created_at: string;
}

// ── Shape used when creating a new tasting entry ──────────────────────────────
export interface NewWine {
  // Master data (upserted into wines)
  name: string;
  producer: string;
  vintage: number | null;
  type: string;
  country: string | null;
  region: string | null;
  grapes: string[] | null;
  alcohol_content: number | null;
  food_pairings: string[] | null;
  description: string | null;
  technical_notes: string | null;
  // Log data (inserted into wine_logs)
  rating: number | null;
  notes: string | null;
  image_url: string | null;
  thumbnail_url: string | null;
  tasted_at: string | null;
  location_name: string | null;
  location_lat: number | null;
  location_lng: number | null;
  location_type: string | null;
}

/** Matches WineAnalysisResponse returned by POST /api/wine/analyze */
export interface WineAnalysisResult {
  wineName: string | null;
  producer: string | null;
  vintage: number | null;
  country: string | null;
  region: string | null;
  grapes: string[] | null;
  type: string | null;
  alcoholContent: number | null;
  // Deduplication fields
  alreadyTasted: boolean;
  existingWineId: string | null;
  lastRating: number | null;
  lastTastedAt: string | null;
  // Pro enrichment (populated when quota was available)
  foodPairings: string[] | null;
  description: string | null;
  technicalNotes: string | null;
  externalSourceId: string | null;
  // Name suggestions from catalogue match
  suggestedName: string | null;
  suggestedProducer: string | null;
  // Quota metadata (always returned for UI)
  proLimitReached: boolean;
  proScansToday: number;
  dailyProLimit: number;
  isPro: boolean;
}

/** Lightweight wine record returned by the search endpoint */
export interface WineSearchResult {
  id: string;
  name: string;
  producer: string;
  vintage: number | null;
  type: string;
  country: string | null;
  region: string | null;
}

@Injectable({ providedIn: 'root' })
export class WineService {
  private readonly http = inject(HttpClient);
  private readonly supabase = inject(SupabaseService).client;
  private readonly notifications = inject(NotificationService);

  private readonly _wines = signal<Wine[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _processing = signal(false);
  private readonly _lastScanResult = signal<WineAnalysisResult | null>(null);
  private readonly _lastScanImageUrl = signal<string | null>(null);
  private readonly _lastScanThumbnailUrl = signal<string | null>(null);
  private readonly _lastScanLocation = signal<{ lat: number; lng: number } | null>(null);

  readonly wines = this._wines.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly wineCount = computed(() => this._wines().length);
  readonly processing = this._processing.asReadonly();
  readonly lastScanResult = this._lastScanResult.asReadonly();
  readonly lastScanImageUrl = this._lastScanImageUrl.asReadonly();
  readonly lastScanThumbnailUrl = this._lastScanThumbnailUrl.asReadonly();
  readonly lastScanLocation = this._lastScanLocation.asReadonly();

  // ── Read ────────────────────────────────────────────────────────────────────

  async loadWines(): Promise<void> {
    this._loading.set(true);
    this._error.set(null);
    try {
      // wine_entries view: latest log per wine for the current user (RLS-filtered)
      const { data, error } = await (this.supabase as any)
        .from('wine_entries')
        .select('*')
        .order('created_at', { ascending: false });
      if (error) throw error;
      this._wines.set((data ?? []) as Wine[]);
    } catch (err) {
      this._error.set(err instanceof Error ? err.message : 'Ukjent feil oppstod');
    } finally {
      this._loading.set(false);
    }
  }

  getWine(id: string): Wine | undefined {
    return this._wines().find(w => w.id === id);
  }

  async fetchWine(id: string): Promise<Wine | null> {
    const { data, error } = await (this.supabase as any)
      .from('wine_entries')
      .select('*')
      .eq('id', id)
      .single();
    if (error || !data) return null;
    return data as Wine;
  }

  /** Returns all tasting logs for a wine, newest first. */
  async fetchWineLogs(wineId: string): Promise<WineLog[]> {
    const { data, error } = await this.supabase
      .from('wine_logs' as any)
      .select('*')
      .eq('wine_id', wineId)
      .order('created_at', { ascending: false });
    if (error) {
      console.error('fetchWineLogs error:', error);
      return [];
    }
    return (data ?? []) as WineLog[];
  }

  /** Returns a single tasting log by its id. */
  async fetchWineLog(logId: string): Promise<WineLog | null> {
    const { data, error } = await this.supabase
      .from('wine_logs' as any)
      .select('*')
      .eq('id', logId)
      .single();
    if (error || !data) return null;
    return data as WineLog;
  }

  // ── Search ──────────────────────────────────────────────────────────────────

  /** Searches the global wine catalogue by name or producer. */
  async searchWines(query: string): Promise<WineSearchResult[]> {
    try {
      const res = await firstValueFrom(
        this.http.get<WineSearchResult[]>('/api/wines/search', {
          params: { q: query },
        }),
      );
      return res ?? [];
    } catch {
      return [];
    }
  }

  // ── Write ───────────────────────────────────────────────────────────────────

  /**
   * Saves a new tasting entry.
   *
   * When `existingWineId` is provided (wine recognised by the AI and already
   * in the catalogue), only a new wine_log row is inserted.
   *
   * Otherwise the master wine data is upserted first (ON CONFLICT on the
   * producer+name+vintage unique constraint), and then the log is inserted.
   */
  async addWine(wine: NewWine, existingWineId?: string): Promise<boolean> {
    this._error.set(null);
    const { data: { user } } = await this.supabase.auth.getUser();
    if (!user) {
      this._error.set('Ikke innlogget');
      return false;
    }

    let wineId = existingWineId ?? null;

    if (!wineId) {
      // Upsert master wine data – ON CONFLICT returns the existing row's id
      const { data: wineData, error: wineError } = await (this.supabase as any)
        .from('wines')
        .upsert(
          {
            name:             wine.name,
            producer:         wine.producer,
            vintage:          wine.vintage,
            type:             wine.type,
            country:          wine.country,
            region:           wine.region,
            grapes:           wine.grapes,
            alcohol_content:  wine.alcohol_content,
            food_pairings:    wine.food_pairings,
            description:      wine.description,
            technical_notes:  wine.technical_notes,
          },
          { onConflict: 'producer,name,vintage', ignoreDuplicates: false }
        )
        .select('id')
        .single();

      if (wineError || !wineData) {
        this._error.set(wineError?.message ?? 'Kunne ikke lagre vin');
        return false;
      }
      wineId = (wineData as { id: string }).id;
    } else if (wine.food_pairings || wine.description || wine.technical_notes) {
      // Update enrichment on existing wine (re-drinking with new scan data)
      await (this.supabase as any)
        .from('wines')
        .update({
          food_pairings:    wine.food_pairings,
          description:      wine.description,
          technical_notes:  wine.technical_notes,
        })
        .eq('id', wineId);
    }

    // Insert tasting log
    const { error: logError } = await this.supabase
      .from('wine_logs' as any)
      .insert({
        wine_id:       wineId,
        user_id:       user.id,
        rating:        wine.rating,
        notes:         wine.notes,
        image_url:     wine.image_url,
        thumbnail_url: wine.thumbnail_url,
        tasted_at:     wine.tasted_at,
        location_name: wine.location_name,
        location_lat:  wine.location_lat,
        location_lng:  wine.location_lng,
        location_type: wine.location_type,
      });

    if (logError) {
      this._error.set(logError.message);
      return false;
    }

    await this.loadWines();
    return true;
  }

  /** Updates a specific tasting log and its master wine record. */
  async updateWine(logId: string, wineId: string, wine: Partial<NewWine>): Promise<boolean> {
    this._error.set(null);

    // Update master wine data
    const { error: wineError } = await this.supabase
      .from('wines' as any)
      .update({
        name:             wine.name,
        producer:         wine.producer,
        vintage:          wine.vintage,
        type:             wine.type,
        country:          wine.country,
        region:           wine.region,
        grapes:           wine.grapes,
        alcohol_content:  wine.alcohol_content,
      })
      .eq('id', wineId);

    if (wineError) {
      this._error.set(wineError.message);
      return false;
    }

    // Update tasting log data
    const { error: logError } = await this.supabase
      .from('wine_logs' as any)
      .update({
        rating:        wine.rating,
        notes:         wine.notes,
        image_url:     wine.image_url,
        thumbnail_url: wine.thumbnail_url,
        tasted_at:     wine.tasted_at,
        location_name: wine.location_name,
        location_lat:  wine.location_lat,
        location_lng:  wine.location_lng,
        location_type: wine.location_type,
      })
      .eq('id', logId);

    if (logError) {
      this._error.set(logError.message);
      return false;
    }
    await this.loadWines();
    return true;
  }

  /** Deletes a specific tasting log (not the master wine record). */
  async deleteWine(logId: string): Promise<boolean> {
    this._error.set(null);
    const { error } = await this.supabase
      .from('wine_logs' as any)
      .delete()
      .eq('id', logId);

    if (error) {
      this._error.set(error.message);
      return false;
    }
    await this.loadWines();
    return true;
  }

  // ── Storage ─────────────────────────────────────────────────────────────────

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
   * Uploads both the full-size and thumbnail images to Supabase Storage.
   * Supports front and optional back label images.
   * Path structure: {userId}/{wineId}/front/full.webp + thumb.webp
   *                 {userId}/{wineId}/back/full.webp + thumb.webp
   */
  async uploadLabelImages(
    frontFull: Blob,
    frontThumb: Blob,
    backFull?: Blob | null,
    backThumb?: Blob | null,
  ): Promise<{ imageUrl: string; thumbnailUrl: string; backImageUrl?: string; backThumbnailUrl?: string } | null> {
    const { data: { user } } = await this.supabase.auth.getUser();
    const ts = Date.now();
    const prefix = user ? `${user.id}/scans/${ts}` : `labels/${ts}`;
    const ext = frontFull.type === 'image/webp' ? 'webp' : 'jpg';

    // Upload front images
    const [frontFullResult, frontThumbResult] = await Promise.all([
      this.supabase.storage
        .from('wine-labels')
        .upload(`${prefix}/front/full.${ext}`, frontFull, { contentType: frontFull.type, upsert: false }),
      this.supabase.storage
        .from('wine-labels')
        .upload(`${prefix}/front/thumb.${ext}`, frontThumb, { contentType: frontThumb.type, upsert: false }),
    ]);

    if (frontFullResult.error || frontThumbResult.error) {
      const msg = frontFullResult.error?.message ?? frontThumbResult.error?.message ?? 'Bildeopplasting feilet';
      console.error('Storage upload error (front):', msg);
      this._error.set(msg);
      return null;
    }

    const { data: frontFullUrl } = this.supabase.storage
      .from('wine-labels')
      .getPublicUrl(frontFullResult.data.path);

    const { data: frontThumbUrl } = this.supabase.storage
      .from('wine-labels')
      .getPublicUrl(frontThumbResult.data.path);

    const result: { imageUrl: string; thumbnailUrl: string; backImageUrl?: string; backThumbnailUrl?: string } = {
      imageUrl: frontFullUrl.publicUrl,
      thumbnailUrl: frontThumbUrl.publicUrl,
    };

    // Upload back images if provided
    if (backFull && backThumb) {
      const backExt = backFull.type === 'image/webp' ? 'webp' : 'jpg';
      const [backFullResult, backThumbResult] = await Promise.all([
        this.supabase.storage
          .from('wine-labels')
          .upload(`${prefix}/back/full.${backExt}`, backFull, { contentType: backFull.type, upsert: false }),
        this.supabase.storage
          .from('wine-labels')
          .upload(`${prefix}/back/thumb.${backExt}`, backThumb, { contentType: backThumb.type, upsert: false }),
      ]);

      if (!backFullResult.error && !backThumbResult.error) {
        const { data: backFullUrl } = this.supabase.storage
          .from('wine-labels')
          .getPublicUrl(backFullResult.data.path);
        const { data: backThumbUrl } = this.supabase.storage
          .from('wine-labels')
          .getPublicUrl(backThumbResult.data.path);
        result.backImageUrl = backFullUrl.publicUrl;
        result.backThumbnailUrl = backThumbUrl.publicUrl;
      } else {
        console.warn('Storage upload error (back), continuing with front only');
      }
    }

    return result;
  }

  // ── AI Analysis ─────────────────────────────────────────────────────────────

  /**
   * Sends the raw image blob(s) as multipart/form-data to POST /api/wine/analyze.
   * The backend calls Gemini and checks the catalogue for de-duplication.
   * Sets the `processing` signal while the request is in flight.
   */
  async analyzeLabel(frontImage: Blob, backImage?: Blob | null): Promise<WineAnalysisResult | null> {
    this._processing.set(true);
    this._error.set(null);
    try {
      const formData = new FormData();
      formData.append('image', frontImage, 'label.jpg');
      if (backImage) {
        formData.append('backImage', backImage, 'back-label.jpg');
      }

      const result = await firstValueFrom(
        this.http.post<WineAnalysisResult>(`${environment.apiBaseUrl}/wine/analyze`, formData)
      );
      this._lastScanResult.set(result);
      return result;
    } catch (err: unknown) {
      const httpErr = err as { error?: { extensions?: { errorCode?: string }; detail?: string }; status?: number };
      const errorCode = httpErr?.error?.extensions?.errorCode;

      if (errorCode) {
        this.notifications.showApiError(errorCode);
      } else {
        const detail = httpErr?.error?.detail
          ?? (err instanceof Error ? err.message : 'Kunne ikke analysere etiketten');
        this.notifications.error(detail);
      }
      return null;
    } finally {
      this._processing.set(false);
    }
  }

  /** Called by ScannerComponent after the Supabase upload completes. */
  setScanImageUrl(url: string): void {
    this._lastScanImageUrl.set(url);
  }

  setScanThumbnailUrl(url: string): void {
    this._lastScanThumbnailUrl.set(url);
  }

  setScanLocation(lat: number, lng: number): void {
    this._lastScanLocation.set({ lat, lng });
  }

  clearScanResult(): void {
    this._lastScanResult.set(null);
    this._lastScanImageUrl.set(null);
    this._lastScanThumbnailUrl.set(null);
    this._lastScanLocation.set(null);
  }
}
