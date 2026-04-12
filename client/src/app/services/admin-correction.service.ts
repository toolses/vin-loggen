import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import type { PaginatedResult } from './admin-wine.service';

export interface AdminCorrectionListItem {
  id: string;
  userId: string;
  userEmail: string | null;
  wineId: string | null;
  wineName: string | null;
  wineProducer: string | null;
  source: string;
  comment: string | null;
  createdAt: string;
  fieldCount: number;
}

export interface AdminCorrectionDetail {
  id: string;
  userId: string;
  userEmail: string | null;
  wineId: string | null;
  wineName: string | null;
  wineProducer: string | null;
  source: string;
  originalData: string;
  correctedData: string;
  comment: string | null;
  createdAt: string;
}

export interface AdminCorrectionSearchParams {
  source?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class AdminCorrectionService {
  private readonly http = inject(HttpClient);

  private readonly _corrections = signal<AdminCorrectionListItem[]>([]);
  private readonly _totalCount = signal(0);
  private readonly _page = signal(1);
  private readonly _pageSize = signal(25);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);

  readonly corrections = this._corrections.asReadonly();
  readonly totalCount = this._totalCount.asReadonly();
  readonly page = this._page.asReadonly();
  readonly pageSize = this._pageSize.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();

  async loadCorrections(params: AdminCorrectionSearchParams = {}): Promise<void> {
    this._loading.set(true);
    this._error.set(null);
    try {
      let httpParams = new HttpParams();
      if (params.source) httpParams = httpParams.set('source', params.source);
      if (params.search) httpParams = httpParams.set('search', params.search);
      if (params.page) httpParams = httpParams.set('page', params.page.toString());
      if (params.pageSize) httpParams = httpParams.set('pageSize', params.pageSize.toString());

      const result = await firstValueFrom(
        this.http.get<PaginatedResult<AdminCorrectionListItem>>(
          `${environment.apiBaseUrl}/admin/corrections`,
          { params: httpParams },
        ),
      );
      this._corrections.set(result.items);
      this._totalCount.set(result.totalCount);
      this._page.set(result.page);
      this._pageSize.set(result.pageSize);
    } catch (err: any) {
      this._error.set(err?.message ?? 'Failed to load corrections');
    } finally {
      this._loading.set(false);
    }
  }

  async getCorrection(id: string): Promise<AdminCorrectionDetail | null> {
    try {
      return await firstValueFrom(
        this.http.get<AdminCorrectionDetail>(
          `${environment.apiBaseUrl}/admin/corrections/${id}`,
        ),
      );
    } catch {
      return null;
    }
  }

  async deleteCorrection(id: string): Promise<boolean> {
    try {
      await firstValueFrom(
        this.http.delete(`${environment.apiBaseUrl}/admin/corrections/${id}`),
      );
      return true;
    } catch {
      return false;
    }
  }
}
