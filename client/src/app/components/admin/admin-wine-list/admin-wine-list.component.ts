import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminWineService } from '../../../services/admin-wine.service';

@Component({
  selector: 'app-admin-wine-list',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './admin-wine-list.component.html',
})
export class AdminWineListComponent implements OnInit {
  protected readonly wineService = inject(AdminWineService);

  protected readonly search = signal('');
  protected readonly typeFilter = signal('');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);

  protected readonly totalPages = computed(() =>
    Math.ceil(this.wineService.totalCount() / this.pageSize()),
  );

  protected readonly wineTypes = [
    'Rød',
    'Hvit',
    'Rosé',
    'Musserende',
    'Oransje',
    'Dessert',
  ];

  private debounceTimeout: ReturnType<typeof setTimeout> | null = null;

  async ngOnInit(): Promise<void> {
    await this.loadWines();
  }

  onSearchInput(value: string): void {
    this.search.set(value);
    if (this.debounceTimeout) clearTimeout(this.debounceTimeout);
    this.debounceTimeout = setTimeout(() => {
      this.page.set(1);
      this.loadWines();
    }, 300);
  }

  onTypeChange(value: string): void {
    this.typeFilter.set(value);
    this.page.set(1);
    this.loadWines();
  }

  prevPage(): void {
    if (this.page() > 1) {
      this.page.update(p => p - 1);
      this.loadWines();
    }
  }

  nextPage(): void {
    if (this.page() < this.totalPages()) {
      this.page.update(p => p + 1);
      this.loadWines();
    }
  }

  private async loadWines(): Promise<void> {
    await this.wineService.loadWines({
      search: this.search() || undefined,
      type: this.typeFilter() || undefined,
      page: this.page(),
      pageSize: this.pageSize(),
    });
  }

  protected typeClass(type: string | null | undefined): string {
    switch (type) {
      case 'Rød':        return 'bg-burgundy/20 text-burgundy border-burgundy/30';
      case 'Hvit':       return 'bg-gold/20 text-gold border-gold/30';
      case 'Rosé':       return 'bg-rose-400/20 text-rose-300 border-rose-400/30';
      case 'Musserende': return 'bg-sky-400/20 text-sky-300 border-sky-400/30';
      case 'Oransje':    return 'bg-orange-400/20 text-orange-300 border-orange-400/30';
      case 'Dessert':    return 'bg-amber-500/20 text-amber-300 border-amber-500/30';
      default:           return 'bg-white/10 text-cream/50 border-white/10';
    }
  }
}
