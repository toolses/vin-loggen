import { Component, inject, OnInit, signal } from '@angular/core';
import { AdminTraceService, type TraceSummary, type TraceEntry } from '../../../services/admin-trace.service';

@Component({
  selector: 'app-admin-traces',
  standalone: true,
  imports: [],
  templateUrl: './admin-traces.component.html',
})
export class AdminTracesComponent implements OnInit {
  protected readonly traceService = inject(AdminTraceService);

  protected readonly selectedTrace = signal<TraceSummary | null>(null);
  protected readonly expandedEntryId = signal<string | null>(null);
  protected readonly days = signal(7);
  protected readonly lightboxUrl = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.traceService.loadTraces(this.days());
  }

  async selectTrace(trace: TraceSummary): Promise<void> {
    if (this.selectedTrace()?.correlationId === trace.correlationId) {
      this.selectedTrace.set(null);
      return;
    }
    this.selectedTrace.set(trace);
    this.expandedEntryId.set(null);
    await this.traceService.loadTraceDetail(trace.correlationId);
  }

  toggleEntry(entryId: string): void {
    this.expandedEntryId.set(this.expandedEntryId() === entryId ? null : entryId);
  }

  async refresh(): Promise<void> {
    this.selectedTrace.set(null);
    await this.traceService.loadTraces(this.days());
  }

  async changeDays(newDays: number): Promise<void> {
    this.days.set(newDays);
    this.selectedTrace.set(null);
    await this.traceService.loadTraces(newDays);
  }

  formatTime(dateStr: string): string {
    return new Date(dateStr).toLocaleTimeString('nb-NO', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  }

  formatDateTime(dateStr: string): string {
    return new Date(dateStr).toLocaleString('nb-NO', {
      day: 'numeric',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  }

  formatDuration(ms: number): string {
    if (ms < 1000) return `${ms}ms`;
    return `${(ms / 1000).toFixed(1)}s`;
  }

  providerLabel(provider: string): string {
    const labels: Record<string, string> = {
      gemini: 'Gemini',
      deepseek: 'DeepSeek',
      groq: 'Groq',
      wineapi: 'WineAPI',
    };
    return labels[provider] ?? provider;
  }

  providerBadgeClass(provider: string): string {
    const classes: Record<string, string> = {
      deepseek: 'bg-blue-500/20 text-blue-300',
      gemini: 'bg-amber-500/20 text-amber-300',
      groq: 'bg-orange-500/20 text-orange-300',
      wineapi: 'bg-emerald-500/20 text-emerald-300',
    };
    return classes[provider] ?? 'bg-white/10 text-cream/60';
  }

  providerIcon(provider: string): string {
    const icons: Record<string, string> = {
      deepseek: 'DS',
      gemini: 'GEM',
      groq: 'GRQ',
      wineapi: 'API',
    };
    return icons[provider] ?? '?';
  }

  statusClass(code: number): string {
    if (!code) return 'text-cream/40';
    if (code >= 400) return 'text-red-400';
    return 'text-green-400';
  }

  shortCorrelationId(id: string): string {
    return id.substring(0, 8);
  }

  endpointLabel(endpoint: string): string {
    const labels: Record<string, string> = {
      ExpertChat: 'Expert',
      AnalyzeLabel: 'Skann',
      AnalyzeLabels: 'Skann (2x)',
      LabelScan: 'Skann',
      GetFoodPairings: 'Matkombo',
    };
    return labels[endpoint] ?? endpoint;
  }

  formatBody(body: string | null): string {
    if (!body) return '';
    try {
      return JSON.stringify(JSON.parse(body), null, 2);
    } catch {
      return body;
    }
  }

  isLabelScan(entry: TraceEntry): boolean {
    return ['LabelScan', 'AnalyzeLabel', 'AnalyzeLabels'].includes(entry.endpoint);
  }

  /** Extract Supabase image URLs from the requestBody JSON of a LabelScan entry */
  parseLabelImages(requestBody: string): { front?: string; back?: string } | null {
    try {
      const parsed = JSON.parse(requestBody);
      if (parsed.frontImageUrl) {
        return {
          front: parsed.frontImageUrl,
          back: parsed.backImageUrl ?? undefined,
        };
      }
    } catch { /* not JSON or no URLs */ }
    return null;
  }

  /** Convert a full-size Supabase URL to its thumbnail counterpart */
  toThumbnailUrl(fullUrl: string): string {
    return fullUrl.replace('/full.', '/thumb.');
  }

  openLightbox(url: string): void {
    this.lightboxUrl.set(url);
  }

  closeLightbox(): void {
    this.lightboxUrl.set(null);
  }
}
