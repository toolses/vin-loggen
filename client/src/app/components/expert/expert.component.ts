import { Component, computed, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { ProfileService } from '../../services/profile.service';
import { ExpertService, ExpertMessage, ExpertWineRef } from '../../services/expert.service';
import { MarkdownPipe } from '../../pipes/markdown.pipe';

@Component({
  selector: 'app-expert',
  standalone: true,
  imports: [RouterLink, NgTemplateOutlet, MarkdownPipe, DatePipe],
  templateUrl: './expert.component.html',
})
export class ExpertComponent implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly profile = inject(ProfileService);
  protected readonly expert = inject(ExpertService);
  private readonly router = inject(Router);

  protected readonly chatInput = signal('');
  protected readonly chatScroll = viewChild<ElementRef<HTMLDivElement>>('chatScroll');
  private readonly chatTextarea = viewChild<ElementRef<HTMLTextAreaElement>>('chatTextarea');
  protected readonly showHistory = signal(false);

  protected readonly quotaPercent = computed(() => {
    const limit = this.profile.dailyProLimit();
    if (limit === 0) return 0;
    return Math.round((this.profile.proScansRemaining() / limit) * 100);
  });

  protected readonly greeting = computed(() => {
    const name = this.auth.displayName();
    const firstName = name?.split(' ')[0] ?? null;
    return firstName ? `Hei, ${firstName}!` : 'Hei!';
  });

  protected readonly isViewingPastSession = computed(() => this.expert.viewingHistory());

  ngOnInit(): void {
    this.profile.loadProQuota();
  }

  protected async sendMessage(): Promise<void> {
    const question = this.chatInput().trim();
    if (!question || this.expert.loading()) return;

    this.chatInput.set('');
    this.resetTextareaHeight();
    await this.expert.ask(question);
    this.scrollToBottom();
  }

  protected onInput(event: Event): void {
    const textarea = event.target as HTMLTextAreaElement;
    this.chatInput.set(textarea.value);
    textarea.style.height = 'auto';
    textarea.style.height = textarea.scrollHeight + 'px';
  }

  protected onQuickAction(action: string): void {
    this.chatInput.set(action);
    this.sendMessage();
  }

  protected openScanner(): void {
    this.router.navigate(['/scan']);
  }

  protected onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.sendMessage();
    }
  }

  // ── History ────────────────────────────────────────────────────────

  protected toggleHistory(): void {
    const show = !this.showHistory();
    this.showHistory.set(show);
    if (show) this.expert.loadSessions();
  }

  protected openSession(sessionId: string): void {
    this.expert.loadSession(sessionId);
    this.showHistory.set(false);
  }

  protected deleteSession(sessionId: string, event: Event): void {
    event.stopPropagation();
    this.expert.deleteSession(sessionId);
  }

  protected startNew(): void {
    this.expert.startNewConversation();
    this.showHistory.set(false);
  }

  // ── Feedback ───────────────────────────────────────────────────────

  protected onFeedback(wine: ExpertWineRef, feedback: 1 | -1): void {
    if (!wine.suggestionId) return;

    // Toggle off if same feedback
    if (wine.feedback === feedback) return;

    this.expert.submitFeedback(wine.suggestionId, feedback);
  }

  // ── Helpers ────────────────────────────────────────────────────────

  protected getTypeColor(type: string): string {
    switch (type) {
      case 'Rød': return 'bg-red-900/40 text-red-300 border-red-500/20';
      case 'Hvit': return 'bg-yellow-900/30 text-yellow-300 border-yellow-500/20';
      case 'Rosé': return 'bg-pink-900/30 text-pink-300 border-pink-500/20';
      case 'Musserende': return 'bg-amber-900/30 text-amber-300 border-amber-500/20';
      case 'Oransje': return 'bg-orange-900/30 text-orange-300 border-orange-500/20';
      default: return 'bg-white/5 text-cream-dark border-white/10';
    }
  }

  protected isLinkable(wine: ExpertWineRef): boolean {
    return wine.source !== 'ai' && wine.id !== '00000000-0000-0000-0000-000000000000';
  }

  protected getSourceLabel(source: string | null | undefined): string {
    switch (source) {
      case 'wineapi': return 'WineAPI';
      case 'ai':      return 'AI-forslag';
      default:        return 'Katalog';
    }
  }

  protected getSourceStyle(source: string | null | undefined): string {
    switch (source) {
      case 'ai':      return 'bg-purple-900/30 text-purple-300 border-purple-500/20';
      case 'wineapi': return 'bg-amber-900/30 text-amber-300 border-amber-500/20';
      default:        return 'bg-white/10 text-cream-dark border-white/10';
    }
  }

  private scrollToBottom(): void {
    setTimeout(() => {
      const el = this.chatScroll()?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    }, 50);
  }

  private resetTextareaHeight(): void {
    const el = this.chatTextarea()?.nativeElement;
    if (el) el.style.height = 'auto';
  }
}
