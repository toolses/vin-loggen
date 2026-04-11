import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { SlicePipe } from '@angular/common';
import { WineService, NewWine, WineLog } from '../../services/wine.service';
import { ProfileService } from '../../services/profile.service';
import { LocationService } from '../../services/location.service';
import { StarRatingComponent } from '../star-rating/star-rating.component';
import {
  LocationSearchComponent,
  LocationSelection,
} from '../location-search/location-search.component';

@Component({
  selector: 'app-wine-editor',
  standalone: true,
  imports: [FormsModule, SlicePipe, StarRatingComponent, LocationSearchComponent],
  templateUrl: './wine-editor.component.html',
})
export class WineEditorComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly wineService = inject(WineService);
  private readonly profileService = inject(ProfileService);
  private readonly locationService = inject(LocationService);

  protected readonly name = signal('');
  protected readonly producer = signal('');
  protected readonly vintage = signal<number | null>(null);
  protected readonly type = signal('Rød');
  protected readonly country = signal('');
  protected readonly region = signal('');
  protected readonly grapeVariety = signal('');
  protected readonly alcoholContent = signal('');
  protected readonly notes = signal('');
  protected readonly rating = signal(0);
  protected readonly tastedAt = signal('');
  protected readonly saving = signal(false);

  // Location
  protected readonly locationName = signal<string | null>(null);
  protected readonly locationLat = signal<number | null>(null);
  protected readonly locationLng = signal<number | null>(null);
  protected readonly locationType = signal<string | null>(null);
  protected readonly locationSet = signal(false);

  protected readonly imageUrl = signal<string | null>(null);
  protected readonly wineTypes = ['Rød', 'Hvit', 'Rosé', 'Musserende', 'Oransje', 'Dessert'];

  // Edit mode
  protected readonly editMode = signal(false);
  private editLogId = '';   // wine_log id used for updateWine

  // Re-drinking / deduplication
  protected readonly alreadyTasted = signal(false);
  protected readonly previousRating = signal<number | null>(null);
  protected readonly previousTastedAt = signal<string | null>(null);
  private existingWineId: string | null = null;
  /** Parsed grapes array for master-data upsert */
  private grapes: string[] | null = null;
  private alcoholNum: number | null = null;

  // Pro enrichment (populated from scan result when quota was available)
  protected readonly proLimitReached  = signal(false);
  protected readonly foodPairings     = signal<string[] | null>(null);
  protected readonly description      = signal<string | null>(null);
  protected readonly technicalNotes   = signal<string | null>(null);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    const logId = this.route.snapshot.queryParamMap.get('logId');

    if (id) {
      // Edit mode: load existing wine
      this.editMode.set(true);
      if (logId) {
        // Editing a specific log entry (from tasting timeline)
        this.loadExistingLog(id, logId);
      } else {
        // Editing the latest log (default)
        this.loadExistingWine(id);
      }
    } else {
      // Create mode: pre-fill from scan result
      this.imageUrl.set(this.wineService.lastScanImageUrl());
      this.tastedAt.set(new Date().toISOString().slice(0, 10));
      const scan = this.wineService.lastScanResult();
      if (scan) {
        if (scan.wineName)        this.name.set(scan.wineName);
        if (scan.producer)        this.producer.set(scan.producer);
        if (scan.vintage)         this.vintage.set(scan.vintage);
        if (scan.type)            this.type.set(scan.type);
        if (scan.country)         this.country.set(scan.country);
        if (scan.region)          this.region.set(scan.region);
        if (scan.grapes?.length)  this.grapeVariety.set(scan.grapes.join(', '));
        if (scan.alcoholContent != null) this.alcoholContent.set(`${scan.alcoholContent}%`);

        this.grapes = scan.grapes?.length ? scan.grapes : null;
        this.alcoholNum = scan.alcoholContent ?? null;

        // Deduplication state from backend
        if (scan.alreadyTasted) {
          this.alreadyTasted.set(true);
          this.previousRating.set(scan.lastRating);
          this.previousTastedAt.set(scan.lastTastedAt);
        }
        if (scan.existingWineId) {
          this.existingWineId = scan.existingWineId;
        }

        // Pro enrichment
        this.proLimitReached.set(scan.proLimitReached ?? false);
        this.foodPairings.set(scan.foodPairings ?? null);
        this.description.set(scan.description ?? null);
        this.technicalNotes.set(scan.technicalNotes ?? null);

        // Sync quota state to ProfileService (avoids an extra DB round-trip)
        this.profileService.syncQuotaFromScan(
          scan.proScansToday ?? 0,
          scan.dailyProLimit ?? 10,
          scan.isPro ?? false,
        );

        // Haptic feedback when pro quota was reached
        if (scan.proLimitReached && navigator.vibrate) {
          navigator.vibrate([100, 80, 100, 80, 300]);
        }
      }

      // Pre-fill location from scan GPS
      const scanLoc = this.wineService.lastScanLocation();
      if (scanLoc) {
        this.locationLat.set(scanLoc.lat);
        this.locationLng.set(scanLoc.lng);
        this.locationService.reverseGeocode(scanLoc.lat, scanLoc.lng).then(geo => {
          if (geo) {
            this.locationName.set(geo.name);
            this.locationSet.set(true);
          }
        });
      }
    }
  }

  private async loadExistingWine(wineId: string): Promise<void> {
    let wine = this.wineService.getWine(wineId);
    if (!wine) {
      wine = await this.wineService.fetchWine(wineId) ?? undefined;
    }
    if (!wine) {
      this.router.navigate(['/cellar']);
      return;
    }

    // Store the log_id for the update call
    this.editLogId = wine.log_id;

    this.name.set(wine.name);
    this.producer.set(wine.producer);
    this.vintage.set(wine.vintage);
    this.type.set(wine.type);
    this.country.set(wine.country ?? '');
    this.region.set(wine.region ?? '');
    this.notes.set(wine.notes ?? '');
    this.rating.set(wine.rating ?? 0);
    this.imageUrl.set(wine.image_url);
    this.tastedAt.set(wine.tasted_at ?? '');
    if (wine.grapes?.length)   this.grapeVariety.set(wine.grapes.join(', '));
    if (wine.alcohol_content != null) this.alcoholContent.set(`${wine.alcohol_content}%`);

    if (wine.location_name) {
      this.locationName.set(wine.location_name);
      this.locationLat.set(wine.location_lat);
      this.locationLng.set(wine.location_lng);
      this.locationType.set(wine.location_type);
      this.locationSet.set(true);
    }
  }

  /** Load a specific log entry for editing (used from the tasting timeline). */
  private async loadExistingLog(wineId: string, logId: string): Promise<void> {
    // Load master wine data for the form header fields
    let wine = this.wineService.getWine(wineId);
    if (!wine) {
      wine = await this.wineService.fetchWine(wineId) ?? undefined;
    }
    if (!wine) {
      this.router.navigate(['/cellar']);
      return;
    }

    // Load the specific log entry
    const log = await this.wineService.fetchWineLog(logId);
    if (!log) {
      this.router.navigate(['/wines', wineId]);
      return;
    }

    this.editLogId = logId;

    // Master data from wine
    this.name.set(wine.name);
    this.producer.set(wine.producer);
    this.vintage.set(wine.vintage);
    this.type.set(wine.type);
    this.country.set(wine.country ?? '');
    this.region.set(wine.region ?? '');
    if (wine.grapes?.length)   this.grapeVariety.set(wine.grapes.join(', '));
    if (wine.alcohol_content != null) this.alcoholContent.set(`${wine.alcohol_content}%`);

    // Log-specific data
    this.notes.set(log.notes ?? '');
    this.rating.set(log.rating ?? 0);
    this.imageUrl.set(log.image_url);
    this.tastedAt.set(log.tasted_at ?? '');

    if (log.location_name) {
      this.locationName.set(log.location_name);
      this.locationLat.set(log.location_lat);
      this.locationLng.set(log.location_lng);
      this.locationType.set(log.location_type);
      this.locationSet.set(true);
    }
  }

  protected async save(): Promise<void> {
    if (!this.name() || !this.producer()) return;

    this.saving.set(true);

    const wine: NewWine = {
      name:             this.name(),
      producer:         this.producer(),
      vintage:          this.vintage(),
      type:             this.type(),
      country:          this.country() || null,
      region:           this.region() || null,
      grapes:           this.grapes,
      alcohol_content:  this.alcoholNum,
      food_pairings:    this.foodPairings(),
      description:      this.description(),
      technical_notes:  this.technicalNotes(),
      rating:           this.rating() > 0 ? this.rating() : null,
      notes:            this.notes() || null,
      image_url:        this.imageUrl() || null,
      tasted_at:        this.tastedAt() || null,
      location_name:    this.locationName(),
      location_lat:     this.locationLat(),
      location_lng:     this.locationLng(),
      location_type:    this.locationType(),
    };

    let success: boolean;
    if (this.editMode()) {
      success = await this.wineService.updateWine(this.editLogId, wine);
    } else {
      // Pass existingWineId to skip the wines upsert when re-drinking
      success = await this.wineService.addWine(
        wine,
        this.existingWineId ?? undefined
      );
    }

    if (success) {
      if (navigator.vibrate) navigator.vibrate([100, 50, 100]);
      if (!this.editMode()) this.wineService.clearScanResult();
      // Navigate to the wine detail using the master wine id (from route or existingWineId)
      const targetId = this.route.snapshot.paramMap.get('id') ?? this.existingWineId;
      this.router.navigate(targetId ? ['/wines', targetId] : ['/cellar']);
    }

    this.saving.set(false);
  }

  protected goBack(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.router.navigate(['/wines', id]);
    } else {
      this.router.navigate(['/']);
    }
  }

  protected onLocationSelected(loc: LocationSelection): void {
    this.locationName.set(loc.name);
    this.locationLat.set(loc.lat);
    this.locationLng.set(loc.lng);
    this.locationType.set(loc.type);
    this.locationSet.set(true);
  }

  protected clearLocation(): void {
    this.locationName.set(null);
    this.locationLat.set(null);
    this.locationLng.set(null);
    // Keep locationType so the location-search pre-selects the previous type
    this.locationSet.set(false);
  }
}
