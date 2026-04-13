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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { WineService, Wine } from '../../services/wine.service';
import { SharePreviewComponent } from '../share-preview/share-preview.component';
import { WineMapComponent } from '../wine-map/wine-map.component';
import { WineCardComponent } from '../wine-card/wine-card.component';

@Component({
  selector: 'app-wine-list',
  standalone: true,
  imports: [FormsModule, RouterLink, SharePreviewComponent, WineMapComponent, WineCardComponent],
  templateUrl: './wine-list.component.html',
})
export class WineListComponent implements OnInit, OnDestroy {
  private readonly wineService = inject(WineService);
  private readonly route = inject(ActivatedRoute);

  protected readonly sentinelRef = viewChild<ElementRef<HTMLDivElement>>('sentinel');

  protected readonly search = signal('');
  protected readonly typeFilter = signal<string | null>(null);
  protected readonly displayCount = signal(20);
  protected readonly viewMode = signal<'list' | 'map'>('list');
  protected readonly flyToTarget = signal<{ lng: number; lat: number } | null>(null);
  protected readonly loading = this.wineService.loading;
  protected readonly error = this.wineService.error;
  protected readonly sharingWine = signal<Wine | null>(null);

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

    const params = this.route.snapshot.queryParams;
    if (params['view'] === 'map') {
      this.viewMode.set('map');
      const lat = parseFloat(params['lat']);
      const lng = parseFloat(params['lng']);
      if (!isNaN(lat) && !isNaN(lng)) {
        this.flyToTarget.set({ lat, lng });
      }
    }
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

  protected openSharePreview(wine: Wine): void {
    this.sharingWine.set(wine);
  }

  protected closeSharePreview(): void {
    this.sharingWine.set(null);
  }

  protected showOnMap(wine: Wine): void {
    if (wine.location_lat == null || wine.location_lng == null) return;
    this.flyToTarget.set({ lng: wine.location_lng, lat: wine.location_lat });
    this.viewMode.set('map');
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }
}
