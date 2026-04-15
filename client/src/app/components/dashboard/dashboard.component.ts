import { Component, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { WineService, Wine } from '../../services/wine.service';
import { ProfileService } from '../../services/profile.service';
import { SharePreviewComponent } from '../share-preview/share-preview.component';
import { WineCardComponent } from '../wine-card/wine-card.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink, SharePreviewComponent, WineCardComponent],
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent implements OnInit {
  protected readonly wineService = inject(WineService);
  protected readonly profile = inject(ProfileService);
  private readonly router = inject(Router);
  protected readonly sharingWine = signal<Wine | null>(null);

  ngOnInit(): void {
    this.wineService.loadWines();
    this.profile.loadProQuota();
  }

  protected recentWines() {
    return this.wineService.wines().slice(0, 3);
  }

  protected openSharePreview(wine: Wine): void {
    this.sharingWine.set(wine);
  }

  protected closeSharePreview(): void {
    this.sharingWine.set(null);
  }

  protected showOnMap(wine: Wine): void {
    if (wine.location_lat == null || wine.location_lng == null) return;
    this.router.navigate(['/cellar'], {
      queryParams: { view: 'map', lat: wine.location_lat, lng: wine.location_lng },
    });
  }
}
