import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { WineService, Wine } from '../../services/wine.service';
import { SharePreviewComponent } from '../share-preview/share-preview.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink, SharePreviewComponent],
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent implements OnInit {
  protected readonly wineService = inject(WineService);
  protected readonly sharingWine = signal<Wine | null>(null);

  ngOnInit(): void {
    this.wineService.loadWines();
  }

  protected recentWines() {
    return this.wineService.wines().slice(0, 8);
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

  protected openSharePreview(wine: Wine): void {
    this.sharingWine.set(wine);
  }

  protected closeSharePreview(): void {
    this.sharingWine.set(null);
  }
}
