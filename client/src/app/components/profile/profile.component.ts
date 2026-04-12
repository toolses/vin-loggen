import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { ProfileService } from '../../services/profile.service';
import { WineService } from '../../services/wine.service';
import { LocationSearchComponent, LocationSelection } from '../location-search/location-search.component';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [LocationSearchComponent],
  templateUrl: './profile.component.html',
})
export class ProfileComponent implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly profileService = inject(ProfileService);
  protected readonly wineService = inject(WineService);
  private readonly router = inject(Router);

  protected readonly regenerating = signal(false);
  protected readonly editingHomeAddress = signal(false);

  protected readonly countryDistribution = computed(() => {
    const wines = this.wineService.wines();
    if (wines.length === 0) return [];
    const counts = new Map<string, number>();
    for (const w of wines) {
      if (w.country) counts.set(w.country, (counts.get(w.country) ?? 0) + 1);
    }
    return [...counts.entries()]
      .sort((a, b) => b[1] - a[1])
      .slice(0, 6)
      .map(([country, count]) => ({
        country,
        count,
        pct: Math.round((count / wines.length) * 100),
      }));
  });

  protected readonly typeDistribution = computed(() => {
    const wines = this.wineService.wines();
    if (wines.length === 0) return [];
    const counts = new Map<string, number>();
    for (const w of wines) {
      if (w.type) counts.set(w.type, (counts.get(w.type) ?? 0) + 1);
    }
    return [...counts.entries()]
      .sort((a, b) => b[1] - a[1])
      .map(([type, count]) => ({
        type,
        count,
        pct: Math.round((count / wines.length) * 100),
      }));
  });

  protected readonly memberSince = computed(() => {
    const user = this.auth.user();
    if (!user?.created_at) return null;
    return new Date(user.created_at).toLocaleDateString('nb-NO', {
      year: 'numeric',
      month: 'long',
    });
  });

  ngOnInit(): void {
    this.wineService.loadWines();
    this.profileService.loadProfile();
    this.profileService.loadHomeAddress();
    this.profileService.loadProQuota();
  }

  protected async regenerateInsight(): Promise<void> {
    this.regenerating.set(true);
    await this.profileService.regenerateProfile();
    await this.profileService.loadProQuota();
    this.regenerating.set(false);
  }

  protected async onHomeAddressSelected(location: LocationSelection): Promise<void> {
    await this.profileService.saveHomeAddress(location.name, location.lat, location.lng);
    this.editingHomeAddress.set(false);
  }

  protected async removeHomeAddress(): Promise<void> {
    await this.profileService.clearHomeAddress();
  }

  protected async signOut(): Promise<void> {
    await this.auth.signOut();
    this.router.navigate(['/login']);
  }

  protected getTypeColor(type: string): string {
    const colors: Record<string, string> = {
      'Rød': 'bg-red-900/40 text-red-300 border-red-700/30',
      'Hvit': 'bg-yellow-900/30 text-yellow-200 border-yellow-700/30',
      'Rosé': 'bg-pink-900/30 text-pink-300 border-pink-700/30',
      'Musserende': 'bg-amber-900/30 text-amber-200 border-amber-700/30',
      'Oransje': 'bg-orange-900/30 text-orange-300 border-orange-700/30',
      'Dessert': 'bg-purple-900/30 text-purple-300 border-purple-700/30',
    };
    return colors[type] ?? 'bg-white/5 text-cream-dark border-white/10';
  }
}
