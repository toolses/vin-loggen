import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

export interface TraceSummary {
  correlationId: string;
  startedAt: string;
  endedAt: string;
  totalCalls: number;
  totalDurationMs: number;
  totalTokens: number;
  errorCount: number;
  providers: string[];
  endpoints: string[];
  models: string[] | null;
}

export interface TraceEntry {
  id: string;
  provider: string;
  endpoint: string;
  usedModel: string;
  statusCode: number;
  responseTimeMs: number;
  totalTokensUsed: number;
  requestBody: string;
  responseBody: string;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class AdminTraceService {
  private readonly http = inject(HttpClient);

  private readonly _traces = signal<TraceSummary[]>([]);
  private readonly _traceEntries = signal<TraceEntry[]>([]);
  private readonly _loading = signal(false);

  readonly traces = this._traces.asReadonly();
  readonly traceEntries = this._traceEntries.asReadonly();
  readonly loading = this._loading.asReadonly();

  async loadTraces(days: number = 7): Promise<void> {
    this._loading.set(true);
    try {
      const params = new HttpParams().set('days', days.toString());
      const result = await firstValueFrom(
        this.http.get<TraceSummary[]>(`${environment.apiBaseUrl}/admin/traces`, { params }),
      );
      this._traces.set(result);
    } catch {
      this._traces.set([]);
    } finally {
      this._loading.set(false);
    }
  }

  async loadTraceDetail(correlationId: string): Promise<void> {
    this._loading.set(true);
    try {
      const result = await firstValueFrom(
        this.http.get<TraceEntry[]>(`${environment.apiBaseUrl}/admin/traces/${correlationId}`),
      );
      this._traceEntries.set(result);
    } catch {
      this._traceEntries.set([]);
    } finally {
      this._loading.set(false);
    }
  }
}
