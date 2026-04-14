import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { firstValueFrom } from 'rxjs';
import { NotificationService } from './notification.service';
import { ProfileService } from './profile.service';
import { AuthService } from './auth.service';

// ── Wine reference with optional feedback ────────────────────────────────

export interface ExpertWineRef {
  id: string;
  name: string;
  producer: string;
  vintage: number | null;
  type: string;
  country: string | null;
  region?: string | null;
  foodPairings?: string[] | null;
  description?: string | null;
  technicalNotes?: string | null;
  whyRecommended?: string | null;
  source?: 'catalog' | 'wineapi' | 'ai' | null;
  suggestionId?: string | null;
  feedback?: number | null; // 1 | -1 | null
}

// ── Type-based suggestion (new mode) ─────────────────────────────────

export interface ExpertTypeSuggestionRef {
  category: string;
  subType: string | null;
  country: string | null;
  region: string | null;
  grapes: string[] | null;
  characteristics: string | null;
  foodPairings: string[] | null;
  whyRecommended: string | null;
  vinmonopoletUrl: string | null;
  googleSearchUrl: string | null;
  catalogMatches: ExpertWineRef[] | null;
  source: string;
  suggestionId?: string | null;
  feedback?: number | null;
}

// ── API response ─────────────────────────────────────────────────────────

export interface ExpertAskResponse {
  answer: string;
  referencedWines: ExpertWineRef[] | null;
  proScansToday: number;
  dailyProLimit: number;
  scansRemaining: number;
  modelUsed: string | null;
  sessionId: string | null;
  wineSuggestionIds: string[] | null;
  typeSuggestions: ExpertTypeSuggestionRef[] | null;
}

// ── Chat message ─────────────────────────────────────────────────────────

export interface ExpertMessage {
  role: 'user' | 'assistant';
  content: string;
  wines?: ExpertWineRef[];
  typeSuggestions?: ExpertTypeSuggestionRef[];
  modelUsed?: string | null;
}

// ── Session DTOs ─────────────────────────────────────────────────────────

export interface ExpertSessionSummary {
  id: string;
  title: string | null;
  createdAt: string;
  updatedAt: string;
  messageCount: number;
}

export interface ExpertSessionDetail {
  id: string;
  title: string | null;
  createdAt: string;
  messages: ExpertMessageDto[];
}

export interface ExpertMessageDto {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  modelUsed: string | null;
  createdAt: string;
  wines: ExpertWineSuggestionDto[] | null;
}

export interface ExpertWineSuggestionDto {
  id: string;
  wineId: string | null;
  wineDataJson: string;
  feedback: number | null;
}

// ── Service ──────────────────────────────────────────────────────────────

@Injectable({ providedIn: 'root' })
export class ExpertService {
  private readonly http = inject(HttpClient);
  private readonly notifications = inject(NotificationService);
  private readonly profileService = inject(ProfileService);
  private readonly auth = inject(AuthService);

  private readonly _messages = signal<ExpertMessage[]>([]);
  private readonly _loading = signal(false);
  private readonly _statusText = signal<string | null>(null);
  private readonly _currentSessionId = signal<string | null>(null);
  private readonly _sessions = signal<ExpertSessionSummary[]>([]);
  private readonly _sessionsLoading = signal(false);
  private readonly _viewingHistory = signal(false);

  readonly messages = this._messages.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly statusText = this._statusText.asReadonly();
  readonly currentSessionId = this._currentSessionId.asReadonly();
  readonly sessions = this._sessions.asReadonly();
  readonly sessionsLoading = this._sessionsLoading.asReadonly();
  readonly viewingHistory = this._viewingHistory.asReadonly();

  // ── Ask (creates/continues a session) ────────────────────────────────

  async ask(question: string): Promise<ExpertAskResponse | null> {
    // Add user message immediately
    this._messages.update(msgs => [...msgs, { role: 'user', content: question }]);
    this._loading.set(true);
    this._statusText.set(null);
    this._viewingHistory.set(false);

    try {
      const body: { question: string; sessionId?: string } = { question };
      const sid = this._currentSessionId();
      if (sid) body.sessionId = sid;

      const result = await this.fetchSseAsk(body);

      // Track the session
      if (result.sessionId) {
        this._currentSessionId.set(result.sessionId);
      }

      // Build wine refs with suggestion IDs for feedback
      const wines = result.referencedWines?.map((w, i) => ({
        ...w,
        suggestionId: result.wineSuggestionIds?.[i] ?? null,
        feedback: null as number | null,
      })) ?? undefined;

      // Build type suggestions with suggestion IDs for feedback
      const typeSuggestions = result.typeSuggestions?.map((ts, i) => {
        const baseIndex = (result.referencedWines?.length ?? 0) + i;
        return {
          ...ts,
          suggestionId: result.wineSuggestionIds?.[baseIndex] ?? null,
          feedback: null as number | null,
        };
      }) ?? undefined;

      // Add assistant response
      this._messages.update(msgs => [
        ...msgs,
        {
          role: 'assistant' as const,
          content: result.answer,
          wines,
          typeSuggestions,
          modelUsed: result.modelUsed,
        },
      ]);

      // Sync quota
      this.profileService.syncQuotaFromScan(
        result.proScansToday,
        result.dailyProLimit,
        this.profileService.isPro(),
      );

      return result;
    } catch (err: unknown) {
      const sseErr = err as { errorCode?: string; detail?: string };
      if (sseErr?.errorCode) {
        this.notifications.showApiError(sseErr.errorCode);
      } else {
        const detail = sseErr?.detail
          ?? (err instanceof Error ? err.message : 'Kunne ikke kontakte eksperten');
        this.notifications.error(detail);
      }

      // Remove the user message on failure
      this._messages.update(msgs => msgs.slice(0, -1));
      return null;
    } finally {
      this._loading.set(false);
      this._statusText.set(null);
    }
  }

  // ── SSE fetch helper ───────────────────────────────────────────────

  private async fetchSseAsk(body: { question: string; sessionId?: string }): Promise<ExpertAskResponse> {
    const token = this.auth.session()?.access_token;
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (token) headers['Authorization'] = `Bearer ${token}`;

    const response = await fetch(`${environment.apiBaseUrl}/expert/ask-stream`, {
      method: 'POST',
      headers,
      body: JSON.stringify(body),
    });

    if (!response.ok || !response.body) {
      throw { detail: 'Kunne ikke kontakte eksperten' };
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    let result: ExpertAskResponse | null = null;

    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      // Parse complete SSE messages from buffer
      const parts = buffer.split('\n\n');
      buffer = parts.pop() ?? '';

      for (const part of parts) {
        const lines = part.split('\n');
        let eventName = '';
        let data = '';

        for (const line of lines) {
          if (line.startsWith('event: ')) eventName = line.slice(7);
          else if (line.startsWith('data: ')) data = line.slice(6);
        }

        if (eventName === 'status' && data) {
          const parsed = JSON.parse(data) as { message: string };
          this._statusText.set(parsed.message);
        } else if (eventName === 'result' && data) {
          result = JSON.parse(data) as ExpertAskResponse;
        } else if (eventName === 'error' && data) {
          throw JSON.parse(data);
        }
      }
    }

    if (!result) throw { detail: 'Ingen respons mottatt fra serveren' };
    return result;
  }

  // ── Session history ──────────────────────────────────────────────────

  async loadSessions(limit = 20, offset = 0): Promise<void> {
    this._sessionsLoading.set(true);
    try {
      const params = new HttpParams().set('limit', limit).set('offset', offset);
      const sessions = await firstValueFrom(
        this.http.get<ExpertSessionSummary[]>(`${environment.apiBaseUrl}/expert/sessions`, { params }),
      );
      this._sessions.set(sessions);
    } catch {
      this.notifications.error('Kunne ikke laste samtalehistorikk');
    } finally {
      this._sessionsLoading.set(false);
    }
  }

  async loadSession(sessionId: string): Promise<void> {
    this._loading.set(true);
    try {
      const detail = await firstValueFrom(
        this.http.get<ExpertSessionDetail>(`${environment.apiBaseUrl}/expert/sessions/${sessionId}`),
      );

      // Convert to ExpertMessage array
      const messages: ExpertMessage[] = detail.messages.map(m => {
        const wineList: ExpertWineRef[] = [];
        const typeList: ExpertTypeSuggestionRef[] = [];

        for (const s of m.wines ?? []) {
          const data = JSON.parse(s.wineDataJson);
          if (data.source === 'ai-type') {
            typeList.push({ ...data, suggestionId: s.id, feedback: s.feedback });
          } else {
            wineList.push({ ...data, suggestionId: s.id, feedback: s.feedback } as ExpertWineRef);
          }
        }

        return {
          role: m.role,
          content: m.content,
          wines: wineList.length ? wineList : undefined,
          typeSuggestions: typeList.length ? typeList : undefined,
          modelUsed: m.modelUsed,
        };
      });

      this._messages.set(messages);
      this._currentSessionId.set(sessionId);
      this._viewingHistory.set(true);
    } catch {
      this.notifications.error('Kunne ikke laste samtalen');
    } finally {
      this._loading.set(false);
    }
  }

  async deleteSession(sessionId: string): Promise<void> {
    try {
      await firstValueFrom(
        this.http.delete(`${environment.apiBaseUrl}/expert/sessions/${sessionId}`),
      );
      this._sessions.update(s => s.filter(x => x.id !== sessionId));

      // If deleting the current session, reset
      if (this._currentSessionId() === sessionId) {
        this.startNewConversation();
      }
    } catch {
      this.notifications.error('Kunne ikke slette samtalen');
    }
  }

  // ── Feedback ─────────────────────────────────────────────────────────

  async submitFeedback(suggestionId: string, feedback: 1 | -1): Promise<void> {
    try {
      await firstValueFrom(
        this.http.patch(
          `${environment.apiBaseUrl}/expert/suggestions/${suggestionId}/feedback`,
          { feedback },
        ),
      );

      // Update the wine ref or type suggestion in messages
      this._messages.update(msgs =>
        msgs.map(m => ({
          ...m,
          wines: m.wines?.map(w =>
            w.suggestionId === suggestionId ? { ...w, feedback } : w,
          ),
          typeSuggestions: m.typeSuggestions?.map(ts =>
            ts.suggestionId === suggestionId ? { ...ts, feedback } : ts,
          ),
        })),
      );
    } catch {
      this.notifications.error('Kunne ikke lagre tilbakemelding');
    }
  }

  // ── Navigation helpers ───────────────────────────────────────────────

  startNewConversation(): void {
    this._messages.set([]);
    this._currentSessionId.set(null);
    this._viewingHistory.set(false);
  }

  clearMessages(): void {
    this._messages.set([]);
    this._currentSessionId.set(null);
  }
}
