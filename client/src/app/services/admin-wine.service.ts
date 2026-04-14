import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

export interface AdminWineListItem {
  id: string;
  name: string;
  producer: string;
  vintage: number | null;
  type: string;
  country: string | null;
  region: string | null;
  logCount: number;
  createdAt: string;
}

export interface AdminWineDetail {
  id: string;
  name: string;
  producer: string;
  vintage: number | null;
  type: string;
  country: string | null;
  region: string | null;
  grapes: string[] | null;
  alcoholContent: number | null;
  externalSourceId: string | null;
  foodPairings: string[] | null;
  description: string | null;
  technicalNotes: string | null;
  logCount: number;
  createdAt: string;
}

export interface AdminWineUpdateRequest {
  name: string;
  producer: string;
  vintage: number | null;
  type: string;
  country: string | null;
  region: string | null;
  grapes: string[] | null;
  alcoholContent: number | null;
  foodPairings: string[] | null;
  description: string | null;
  technicalNotes: string | null;
}

export interface PaginatedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface AdminWineSearchParams {
  search?: string;
  type?: string;
  country?: string;
  page?: number;
  pageSize?: number;
}

export interface ResetDataResult {
  deletedWineLogs: number;
  deletedExternalIds: number;
  deletedWines: number;
  resetProfiles: number;
}

@Injectable({ providedIn: 'root' })
export class AdminWineService {
  private readonly http = inject(HttpClient);

  private readonly _wines = signal<AdminWineListItem[]>([]);
  private readonly _totalCount = signal(0);
  private readonly _page = signal(1);
  private readonly _pageSize = signal(25);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);

  readonly wines = this._wines.asReadonly();
  readonly totalCount = this._totalCount.asReadonly();
  readonly page = this._page.asReadonly();
  readonly pageSize = this._pageSize.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();

  async loadWines(params: AdminWineSearchParams = {}): Promise<void> {
    this._loading.set(true);
    this._error.set(null);
    try {
      let httpParams = new HttpParams();
      if (params.search) httpParams = httpParams.set('search', params.search);
      if (params.type) httpParams = httpParams.set('type', params.type);
      if (params.country) httpParams = httpParams.set('country', params.country);
      if (params.page) httpParams = httpParams.set('page', params.page.toString());
      if (params.pageSize) httpParams = httpParams.set('pageSize', params.pageSize.toString());

      const result = await firstValueFrom(
        this.http.get<PaginatedResult<AdminWineListItem>>(
          `${environment.apiBaseUrl}/admin/wines`,
          { params: httpParams },
        ),
      );
      this._wines.set(result.items);
      this._totalCount.set(result.totalCount);
      this._page.set(result.page);
      this._pageSize.set(result.pageSize);
    } catch (err: any) {
      this._error.set(err?.message ?? 'Failed to load wines');
    } finally {
      this._loading.set(false);
    }
  }

  async getWine(id: string): Promise<AdminWineDetail | null> {
    try {
      return await firstValueFrom(
        this.http.get<AdminWineDetail>(`${environment.apiBaseUrl}/admin/wines/${id}`),
      );
    } catch {
      return null;
    }
  }

  async updateWine(id: string, data: AdminWineUpdateRequest): Promise<AdminWineDetail | null> {
    try {
      return await firstValueFrom(
        this.http.put<AdminWineDetail>(`${environment.apiBaseUrl}/admin/wines/${id}`, data),
      );
    } catch {
      return null;
    }
  }

  async resetAllData(): Promise<ResetDataResult | null> {
    try {
      return await firstValueFrom(
        this.http.delete<ResetDataResult>(`${environment.apiBaseUrl}/admin/reset`),
      );
    } catch {
      return null;
    }
  }
}
