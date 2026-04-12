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
}
