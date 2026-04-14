import { AfterViewInit, Component, computed, effect, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { ProfileService } from '../../services/profile.service';
import { ExpertService, ExpertMessage, ExpertWineRef, ExpertTypeSuggestionRef } from '../../services/expert.service';
import { MarkdownPipe } from '../../pipes/markdown.pipe';

@Component({
  selector: 'app-expert',
  standalone: true,
  imports: [RouterLink, NgTemplateOutlet, MarkdownPipe, DatePipe],
  templateUrl: './expert.component.html',
})
export class ExpertComponent implements OnInit, AfterViewInit {
  protected readonly profile = inject(ProfileService);
  protected readonly expert = inject(ExpertService);
  private readonly router = inject(Router);

  protected readonly chatInput = signal('');
  protected readonly chatScroll = viewChild<ElementRef<HTMLDivElement>>('chatScroll');
  private readonly chatTextarea = viewChild<ElementRef<HTMLTextAreaElement>>('chatTextarea');
  protected readonly showHistory = signal(false);

  protected readonly isViewingPastSession = computed(() => this.expert.viewingHistory());

  constructor() {
    effect(() => {
      if (this.expert.loading()) {
        this.expert.statusText(); // track status changes
        this.scrollToBottom();
      }
    });
  }

  ngOnInit(): void {
    this.profile.loadProQuota();
  }

  ngAfterViewInit(): void {
    if (this.expert.messages().length > 0) {
      this.scrollToBottom();
    }
  }

  protected async sendMessage(): Promise<void> {
    const question = this.chatInput().trim();
    if (!question || this.expert.loading()) return;

    this.chatInput.set('');
    this.resetTextareaHeight();
    const askPromise = this.expert.ask(question);
    this.scrollToBottom();
    await askPromise;
    this.scrollLastMessageIntoView();
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
    this.scrollToBottom();
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
    if (wine.feedback === feedback) return;
    this.expert.submitFeedback(wine.suggestionId, feedback);
  }

  protected onTypeFeedback(ts: ExpertTypeSuggestionRef, feedback: 1 | -1): void {
    if (!ts.suggestionId) return;
    if (ts.feedback === feedback) return;
    this.expert.submitFeedback(ts.suggestionId, feedback);
  }

  protected exploreType(ts: ExpertTypeSuggestionRef): void {
    const parts = [ts.subType, ts.region, ts.country, ts.category].filter(Boolean);
    const question = `Fortell meg mer om ${parts.join(', ')}`;
    this.chatInput.set(question);
    this.sendMessage();
  }

  protected getCategoryBadgeClass(category: string): string {
    switch (category?.toLowerCase()) {
      case 'rødvin': case 'rød':         return 'bg-burgundy/20 text-burgundy border-burgundy/30';
      case 'hvitvin': case 'hvit':       return 'bg-gold/20 text-gold border-gold/30';
      case 'rosévin': case 'rosé':       return 'bg-rose-400/20 text-rose-300 border-rose-400/30';
      case 'musserende':                 return 'bg-sky-400/20 text-sky-300 border-sky-400/30';
      case 'oransje':                    return 'bg-orange-400/20 text-orange-300 border-orange-400/30';
      case 'dessert': case 'dessertvin': case 'sterkvin': return 'bg-amber-500/20 text-amber-300 border-amber-500/30';
      default:                           return 'bg-white/10 text-cream/50 border-white/10';
    }
  }

  protected getCategoryIcon(category: string): string {
    switch (category?.toLowerCase()) {
      case 'rødvin': return '🍷';
      case 'hvitvin': return '🥂';
      case 'rosévin': case 'rosé': return '🌸';
      case 'musserende': return '🫧';
      case 'oransje': return '🍊';
      case 'dessertvin': case 'sterkvin': return '🍯';
      default: return '🍷';
    }
  }

  protected googleSearchUrl(wine: ExpertWineRef): string {
    const terms = [wine.name, wine.producer, 'vin'].filter(Boolean).join(' ');
    return `https://www.google.com/search?q=${encodeURIComponent(terms)}`;
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

  protected vinmonopoletSearchUrl(wine: ExpertWineRef): string {
    const q = [wine.name, wine.vintage].filter(Boolean).join(' ')
      .split(/\s+/).map(w => encodeURIComponent(w)).join('+');
    return `https://www.vinmonopolet.no/search?q=${q}:relevance`;
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

  private scrollLastMessageIntoView(): void {
    setTimeout(() => {
      const container = this.chatScroll()?.nativeElement;
      if (!container) return;
      const last = container.lastElementChild as HTMLElement | null;
      if (last) last.scrollIntoView({ block: 'start' });
    }, 50);
  }

  private resetTextareaHeight(): void {
    const el = this.chatTextarea()?.nativeElement;
    if (el) el.style.height = 'auto';
  }
}
