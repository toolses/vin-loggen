import {
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
  afterNextRender,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { WineService, Wine } from '../../services/wine.service';
import { ShareService } from '../../services/share.service';
import { WineShareCardComponent } from '../wine-share-card/wine-share-card.component';

@Component({
  selector: 'app-wine-list',
  standalone: true,
  imports: [FormsModule, RouterLink, WineShareCardComponent],
  templateUrl: './wine-list.component.html',
})
export class WineListComponent implements OnInit, OnDestroy {
  private readonly wineService = inject(WineService);
  private readonly shareService = inject(ShareService);

  protected readonly sentinelRef = viewChild<ElementRef<HTMLDivElement>>('sentinel');
  protected readonly shareCardRef = viewChild<ElementRef<HTMLDivElement>>('shareCard');

  protected readonly search = signal('');
  protected readonly typeFilter = signal<string | null>(null);
  protected readonly displayCount = signal(20);
  protected readonly loading = this.wineService.loading;
  protected readonly error = this.wineService.error;
  protected readonly sharingWine = signal<Wine | null>(null);
  protected readonly isSharing = signal(false);

  protected readonly wineTypes = ['Rød', 'Hvit', 'Rosé', 'Musserende', 'Oransje', 'Dessert'];

  private observer: IntersectionObserver | null = null;

  protected readonly filteredWines = computed(() => {
    let wines = this.wineService.wines();
    const query = this.search().toLowerCase().trim();
    const type = this.typeFilter();

    if (query) {
      wines = wines.filter(
        w =>
          w.name.toLowerCase().includes(query) ||
          w.producer.toLowerCase().includes(query) ||
          (w.country?.toLowerCase().includes(query) ?? false)
      );
    }

    if (type) {
      wines = wines.filter(w => w.type === type);
    }

    return wines;
  });

  protected readonly displayedWines = computed(() =>
    this.filteredWines().slice(0, this.displayCount())
  );

  protected readonly hasMore = computed(() =>
    this.displayCount() < this.filteredWines().length
  );

  protected readonly uniqueCountries = computed(() => {
    const countries = this.wineService.wines()
      .map(w => w.country)
      .filter((c): c is string => !!c);
    return [...new Set(countries)].sort();
  });

  constructor() {
    afterNextRender(() => this.setupIntersectionObserver());
  }

  ngOnInit(): void {
    this.wineService.loadWines();
  }

  private setupIntersectionObserver(): void {
    const el = this.sentinelRef()?.nativeElement;
    if (!el) return;

    this.observer = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting && this.hasMore()) {
          this.displayCount.update(c => c + 20);
        }
      },
      { rootMargin: '200px' }
    );
    this.observer.observe(el);
  }

  protected toggleTypeFilter(type: string): void {
    this.typeFilter.update(current => current === type ? null : type);
    this.displayCount.set(20);
  }

  protected clearFilters(): void {
    this.search.set('');
    this.typeFilter.set(null);
    this.displayCount.set(20);
  }

  protected getTypeColor(type: string): string {
    switch (type) {
      case 'Rød': return 'bg-red-900/40 text-red-300 border-red-500/20';
      case 'Hvit': return 'bg-yellow-900/30 text-yellow-300 border-yellow-500/20';
      case 'Rosé': return 'bg-pink-900/30 text-pink-300 border-pink-500/20';
      case 'Musserende': return 'bg-amber-900/30 text-amber-300 border-amber-500/20';
      case 'Oransje': return 'bg-orange-900/30 text-orange-300 border-orange-500/20';
      case 'Dessert': return 'bg-purple-900/30 text-purple-300 border-purple-500/20';
      default: return 'bg-white/5 text-cream-dark border-white/10';
    }
  }

  protected async shareWine(wine: Wine): Promise<void> {
    this.sharingWine.set(wine);
    this.isSharing.set(true);

    // Wait for Angular to render the share card
    await new Promise(resolve => setTimeout(resolve, 100));

    const el = this.shareCardRef()?.nativeElement;
    if (!el) {
      this.isSharing.set(false);
      this.sharingWine.set(null);
      return;
    }

    try {
      const blob = await this.shareService.generateShareImage(el);
      await this.shareService.shareWine(wine, blob);
    } catch {
      // User cancelled share or error
    } finally {
      this.isSharing.set(false);
      this.sharingWine.set(null);
    }
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }
}
