import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { RouterLink } from '@angular/router';
import { environment } from '../../../../environments/environment';

// ── Search ───────────────────────────────────────────────────────────────

interface WineApiSearchHit {
  id: string | null;
  name: string | null;
  winery: string | null;
  vintage: number | null;
  type: string | null;
  region: string | null;
  country: string | null;
  averageRating: number | null;
  ratingsCount: number | null;
  confidence: number | null;
}

interface WineApiSearchResult {
  correlationId: string;
  hitCount: number;
  hits: WineApiSearchHit[];
}

// ── Details ──────────────────────────────────────────────────────────────

interface WineApiDetail {
  id: string | null;
  name: string | null;
  winery: string | null;
  vintage: number | null;
  type: string | null;
  region: string | null;
  country: string | null;
  description: string | null;
  foodPairings: string[] | null;
  technicalNotes: string | null;
  alcoholContent: number | null;
  grapes: string[] | null;
  averageRating: number | null;
  ratingsCount: number | null;
}

interface WineApiDetailResult {
  correlationId: string;
  found: boolean;
  detail: WineApiDetail | null;
}

// ── Identify ─────────────────────────────────────────────────────────────

interface WineApiIdentifyHit {
  id: string | null;
  name: string | null;
  vintage: number | null;
  type: string | null;
  region: string | null;
  country: string | null;
  averageRating: number | null;
  ratingsCount: number | null;
}

interface WineApiIdentifyResult {
  correlationId: string;
  found: boolean;
  result: {
    wine: WineApiIdentifyHit | null;
    suggestions: WineApiIdentifyHit[] | null;
    confidence: number | null;
  } | null;
}

@Component({
  selector: 'app-admin-api-test',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './admin-api-test.component.html',
})
export class AdminApiTestComponent {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/admin/api-test`;

  // ── Search state ─────────────────────────────────────────────────────────
  protected readonly producer = signal('');
  protected readonly wineName = signal('');
  protected readonly vintage = signal('');
  protected readonly searchLoading = signal(false);
  protected readonly searchResult = signal<WineApiSearchResult | null>(null);
  protected readonly searchError = signal<string | null>(null);
  protected readonly searchRawJson = signal<string | null>(null);

  // ── Detail state ─────────────────────────────────────────────────────────
  protected readonly detailLoadingId = signal<string | null>(null);
  protected readonly detailResults = signal<Record<string, WineApiDetail>>({});
  protected readonly expandedDetailId = signal<string | null>(null);

  // ── Identify state ───────────────────────────────────────────────────────
  protected readonly identifyQuery = signal('');
  protected readonly identifyLoading = signal(false);
  protected readonly identifyResult = signal<WineApiIdentifyResult | null>(null);
  protected readonly identifyError = signal<string | null>(null);
  protected readonly identifyRawJson = signal<string | null>(null);

  // ── Search ───────────────────────────────────────────────────────────────

  async search(): Promise<void> {
    const p = this.producer().trim();
    const n = this.wineName().trim();
    if (!p && !n) return;

    this.searchLoading.set(true);
    this.searchError.set(null);
    this.searchResult.set(null);
    this.searchRawJson.set(null);
    this.expandedDetailId.set(null);

    try {
      let params = new HttpParams().set('producer', p).set('name', n);
      const v = this.vintage().trim();
      if (v) params = params.set('vintage', v);

      const res = await firstValueFrom(
        this.http.get<WineApiSearchResult>(`${this.base}/wineapi/search`, { params }),
      );
      this.searchResult.set(res);
      this.searchRawJson.set(JSON.stringify(res, null, 2));
    } catch (err: unknown) {
      const httpErr = err as { error?: { detail?: string }; status?: number };
      this.searchError.set(httpErr?.error?.detail ?? 'Søket feilet');
    } finally {
      this.searchLoading.set(false);
    }
  }

  // ── Details ──────────────────────────────────────────────────────────────

  async fetchDetails(wineId: string): Promise<void> {
    if (this.expandedDetailId() === wineId) {
      this.expandedDetailId.set(null);
      return;
    }

    // Already cached?
    if (this.detailResults()[wineId]) {
      this.expandedDetailId.set(wineId);
      return;
    }

    this.detailLoadingId.set(wineId);
    this.expandedDetailId.set(wineId);

    try {
      const res = await firstValueFrom(
        this.http.get<WineApiDetailResult>(`${this.base}/wineapi/details/${encodeURIComponent(wineId)}`),
      );
      if (res.detail) {
        this.detailResults.update(map => ({ ...map, [wineId]: res.detail! }));
      }
    } catch {
      // silently ignore detail fetch failures
    } finally {
      this.detailLoadingId.set(null);
    }
  }

  getDetail(wineId: string): WineApiDetail | undefined {
    return this.detailResults()[wineId];
  }

  // ── Identify by text ─────────────────────────────────────────────────────

  async identifyByText(): Promise<void> {
    const q = this.identifyQuery().trim();
    if (!q) return;

    this.identifyLoading.set(true);
    this.identifyError.set(null);
    this.identifyResult.set(null);
    this.identifyRawJson.set(null);

    try {
      const res = await firstValueFrom(
        this.http.post<WineApiIdentifyResult>(`${this.base}/wineapi/identify-text`, { query: q }),
      );
      this.identifyResult.set(res);
      this.identifyRawJson.set(JSON.stringify(res, null, 2));
    } catch (err: unknown) {
      const httpErr = err as { error?: { detail?: string }; status?: number };
      this.identifyError.set(httpErr?.error?.detail ?? 'Identifisering feilet');
    } finally {
      this.identifyLoading.set(false);
    }
  }

  // ── Helpers ──────────────────────────────────────────────────────────────

  formatRating(rating: number | null): string {
    return rating != null ? rating.toFixed(1) : '–';
  }

  formatConfidence(confidence: number | null): string {
    if (confidence == null) return '–';
    return `${(confidence * 100).toFixed(0)}%`;
  }
}
