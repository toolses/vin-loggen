import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { WineService, Wine } from '../../services/wine.service';
import { SharePreviewComponent } from '../share-preview/share-preview.component';

@Component({
  selector: 'app-wine-detail',
  standalone: true,
  imports: [RouterLink, DatePipe, SharePreviewComponent],
  templateUrl: './wine-detail.component.html',
})
export class WineDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly wineService = inject(WineService);

  protected readonly wine = signal<Wine | null>(null);
  protected readonly loading = signal(true);
  protected readonly showDeleteConfirm = signal(false);
  protected readonly deleting = signal(false);

  // Share preview state
  protected readonly showSharePreview = signal(false);

  protected readonly stars = computed(() => {
    const r = this.wine()?.rating;
    if (r == null) return '';
    const full = Math.floor(r);
    return '★'.repeat(full) + '☆'.repeat(6 - full);
  });

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.router.navigate(['/cellar']);
      return;
    }

    // Try local cache first
    let wine = this.wineService.getWine(id);
    if (!wine) {
      wine = await this.wineService.fetchWine(id) ?? undefined;
    }

    if (wine) {
      this.wine.set(wine);
    } else {
      this.router.navigate(['/cellar']);
    }
    this.loading.set(false);
  }

  protected async confirmDelete(): Promise<void> {
    const wine = this.wine();
    if (!wine) return;

    this.deleting.set(true);
    const success = await this.wineService.deleteWine(wine.id);
    if (success) {
      if (navigator.vibrate) navigator.vibrate(100);
      this.router.navigate(['/cellar']);
    }
    this.deleting.set(false);
    this.showDeleteConfirm.set(false);
  }

  protected goBack(): void {
    this.router.navigate(['/cellar']);
  }
}
