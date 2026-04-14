import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { WineService, NewWine, WineLog } from '../../services/wine.service';
import type { WineSearchResult, WineSavePayload } from '../../services/wine.service';
import { ProfileService } from '../../services/profile.service';
import { LocationService } from '../../services/location.service';
import { NotificationService } from '../../services/notification.service';
import { AdminService } from '../../services/admin.service';
import { StarRatingComponent } from '../star-rating/star-rating.component';
import {
  LocationSearchComponent,
  LocationSelection,
} from '../location-search/location-search.component';

@Component({
  selector: 'app-wine-editor',
  standalone: true,
  imports: [FormsModule, DatePipe, StarRatingComponent, LocationSearchComponent],
  templateUrl: './wine-editor.component.html',
})
export class WineEditorComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly wineService = inject(WineService);
  private readonly profileService = inject(ProfileService);
  private readonly locationService = inject(LocationService);
  private readonly notifications = inject(NotificationService);
  protected readonly admin = inject(AdminService);

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
  protected readonly locationInitialQuery = signal<string | null>(null);
  protected readonly homeAddress = this.profileService.homeAddress;

  protected readonly imageUrl = signal<string | null>(null);
  protected readonly thumbnailUrl = signal<string | null>(null);
  protected readonly wineTypes = ['Rød', 'Hvit', 'Rosé', 'Musserende', 'Oransje', 'Dessert'];

  // Edit mode
  protected readonly editMode = signal(false);
  private editLogId = '';    // wine_log id used for updateWine
  private editWineId = '';   // master wine id used for updateWine

  // Re-drinking / deduplication
  protected readonly alreadyTasted = signal(false);
  protected readonly previousRating = signal<number | null>(null);
  protected readonly previousTastedAt = signal<string | null>(null);
  protected existingWineId: string | null = null;
  /** Parsed grapes array for master-data upsert */
  private grapes: string[] | null = null;
  private alcoholNum: number | null = null;

  // Pro enrichment (populated from scan result when quota was available)
  protected readonly proLimitReached  = signal(false);
  protected readonly foodPairings     = signal<string[] | null>(null);
  protected readonly description      = signal<string | null>(null);
  protected readonly technicalNotes   = signal<string | null>(null);

  // Catalogue name suggestion
  protected readonly suggestedName      = signal<string | null>(null);
  protected readonly suggestedProducer  = signal<string | null>(null);
  protected readonly showSuggestion     = signal(false);
  protected readonly nameFromCatalogue  = signal(false);

  // Candidate selection metadata (set when user picked from selection step)
  private candidateSelected = false;
  private candidateIsLocal  = false;
  private candidateFields: Record<string, boolean> = {};

  // Wine search step (manual entry without scan result)
  protected readonly searchStep     = signal(false);
  protected readonly searchQuery    = signal('');
  protected readonly searchResults  = signal<WineSearchResult[]>([]);
  protected readonly searching      = signal(false);
  private searchTimeout: ReturnType<typeof setTimeout> | null = null;

  // Original AI data snapshot for correction tracking (create mode only)
  private originalData: WineSavePayload['originalData'] = null;
  private originalSource: string | null = null;

  // Tracks which fields the user has edited vs the AI/API original
  protected readonly editedFields = computed<Set<string>>(() => {
    const od = this.originalData;
    if (!od || this.editMode()) return new Set();
    const fields = new Set<string>();
    const ci = (a?: string | null, b?: string | null) =>
      (a ?? '').trim().toLowerCase() !== (b ?? '').trim().toLowerCase();
    if (ci(od.name, this.name())) fields.add('name');
    if (ci(od.producer, this.producer())) fields.add('producer');
    if (od.vintage !== this.vintage()) fields.add('vintage');
    if (ci(od.type, this.type())) fields.add('type');
    if (ci(od.country, this.country())) fields.add('country');
    if (ci(od.region, this.region())) fields.add('region');
    // Compare grape variety text
    const origGrapes = (od.grapes ?? []).join(', ').trim().toLowerCase();
    const currGrapes = this.grapeVariety().trim().toLowerCase();
    if (origGrapes !== currGrapes) fields.add('grapeVariety');
    // Compare alcohol
    const origAlc = od.alcoholContent != null ? `${od.alcoholContent}%` : '';
    const currAlc = this.alcoholContent().trim();
    if (origAlc.toLowerCase() !== currAlc.toLowerCase()) fields.add('alcoholContent');
    return fields;
  });

  // Report incorrect info UI
  protected readonly showReportModal = signal(false);
  protected readonly reportComment = signal('');
  protected readonly reportSubmitting = signal(false);

  // Edit scope: 'wine' = wine fields only, 'tasting' = tasting fields only, null = all
  protected readonly editScope = signal<'wine' | 'tasting' | null>(null);

  ngOnInit(): void {
    this.profileService.loadHomeAddress();
    const id = this.route.snapshot.paramMap.get('id');
    const logId = this.route.snapshot.queryParamMap.get('logId');
    const mode = this.route.snapshot.queryParamMap.get('mode');
    if (mode === 'wine' || mode === 'tasting') {
      this.editScope.set(mode);
    }

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
      this.thumbnailUrl.set(this.wineService.lastScanThumbnailUrl());
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

        // Snapshot original AI data for correction tracking
        this.originalData = {
          name: scan.wineName ?? null,
          producer: scan.producer ?? null,
          vintage: scan.vintage ?? null,
          type: scan.type ?? null,
          country: scan.country ?? null,
          region: scan.region ?? null,
          grapes: scan.grapes ?? null,
          alcoholContent: scan.alcoholContent ?? null,
          source: scan.externalSourceId ? 'wineapi' : 'gemini',
        };

        // Pro enrichment
        this.proLimitReached.set(scan.proLimitReached ?? false);
        this.foodPairings.set(scan.foodPairings ?? null);
        this.description.set(scan.description ?? null);
        this.technicalNotes.set(scan.technicalNotes ?? null);

        // Candidate selection metadata
        this.candidateSelected = scan.candidateSelected ?? false;
        this.candidateIsLocal  = scan.candidateIsLocal ?? false;
        this.candidateFields   = scan.candidateFields ?? {};

        // Catalogue name (auto-applied by backend, or interactive suggestion fallback)
        // Skip suggestion banner if user already manually selected a candidate
        this.nameFromCatalogue.set(scan.nameFromCatalogue ?? false);
        if (this.candidateSelected) {
          this.showSuggestion.set(false);
        } else if (scan.nameFromCatalogue) {
          this.showSuggestion.set(true);
        } else {
          const sn = scan.suggestedName?.trim();
          const sp = scan.suggestedProducer?.trim();
          const namesDiffer  = sn && sn.toLowerCase() !== (scan.wineName ?? '').trim().toLowerCase();
          const prodsDiffer  = sp && sp.toLowerCase() !== (scan.producer ?? '').trim().toLowerCase();
          if (namesDiffer || prodsDiffer) {
            this.suggestedName.set(sn ?? null);
            this.suggestedProducer.set(sp ?? null);
            this.showSuggestion.set(true);
          }
        }

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
      } else {
        // No scan result → show wine search step for manual entry
        this.searchStep.set(true);
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

    // Store the log_id and wine_id for the update call
    this.editLogId = wine.log_id;
    this.editWineId = wine.id;

    this.name.set(wine.name);
    this.producer.set(wine.producer);
    this.vintage.set(wine.vintage);
    this.type.set(wine.type);
    this.country.set(wine.country ?? '');
    this.region.set(wine.region ?? '');
    this.notes.set(wine.notes ?? '');
    this.rating.set(wine.rating ?? 0);
    this.imageUrl.set(wine.image_url);
    this.tastedAt.set(wine.tasted_at?.slice(0, 10) ?? '');
    if (wine.grapes?.length)   this.grapeVariety.set(wine.grapes.join(', '));
    if (wine.alcohol_content != null) this.alcoholContent.set(`${wine.alcohol_content}%`);
    this.grapes = wine.grapes ?? null;
    this.alcoholNum = wine.alcohol_content ?? null;

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
    this.editWineId = wineId;

    // Master data from wine
    this.name.set(wine.name);
    this.producer.set(wine.producer);
    this.vintage.set(wine.vintage);
    this.type.set(wine.type);
    this.country.set(wine.country ?? '');
    this.region.set(wine.region ?? '');
    if (wine.grapes?.length)   this.grapeVariety.set(wine.grapes.join(', '));
    if (wine.alcohol_content != null) this.alcoholContent.set(`${wine.alcohol_content}%`);
    this.grapes = wine.grapes ?? null;
    this.alcoholNum = wine.alcohol_content ?? null;

    // Log-specific data
    this.notes.set(log.notes ?? '');
    this.rating.set(log.rating ?? 0);
    this.imageUrl.set(log.image_url);
    this.tastedAt.set(log.tasted_at?.slice(0, 10) ?? '');

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

    // Parse grapes and alcohol from display signals so edits are captured
    const gv = this.grapeVariety().trim();
    const parsedGrapes = gv ? gv.split(',').map(g => g.trim()).filter(Boolean) : null;
    const ac = this.alcoholContent().replace('%', '').trim();
    const parsedAlcohol = ac ? parseFloat(ac) : null;

    const wine: NewWine = {
      name:             this.name(),
      producer:         this.producer(),
      vintage:          this.vintage(),
      type:             this.type(),
      country:          this.country() || null,
      region:           this.region() || null,
      grapes:           parsedGrapes,
      alcohol_content:  parsedAlcohol !== null && !isNaN(parsedAlcohol) ? parsedAlcohol : null,
      food_pairings:    this.foodPairings(),
      description:      this.description(),
      technical_notes:  this.technicalNotes(),
      rating:           this.rating() > 0 ? this.rating() : null,
      notes:            this.notes() || null,
      image_url:        this.imageUrl() || null,
      thumbnail_url:    this.thumbnailUrl() || null,
      tasted_at:        this.tastedAt() || null,
      location_name:    this.locationName(),
      location_lat:     this.locationLat(),
      location_lng:     this.locationLng(),
      location_type:    this.locationType(),
    };

    let success: boolean;
    if (this.editMode()) {
      success = await this.wineService.updateWine(this.editLogId, this.editWineId, wine);
    } else {
      // Create mode: use backend smart save endpoint
      const payload: WineSavePayload = {
        name:             wine.name,
        producer:         wine.producer,
        vintage:          wine.vintage,
        type:             wine.type,
        country:          wine.country,
        region:           wine.region,
        grapes:           parsedGrapes,
        alcoholContent:   parsedAlcohol !== null && !isNaN(parsedAlcohol) ? parsedAlcohol : null,
        externalSourceId: null,
        foodPairings:     this.foodPairings(),
        description:      this.description(),
        technicalNotes:   this.technicalNotes(),
        originalData:     this.originalData,
        existingWineId:   this.existingWineId,
        rating:           wine.rating,
        notes:            wine.notes,
        imageUrl:         wine.image_url,
        thumbnailUrl:     wine.thumbnail_url,
        tastedAt:         wine.tasted_at,
        locationName:     wine.location_name,
        locationLat:      wine.location_lat,
        locationLng:      wine.location_lng,
        locationType:     wine.location_type,
      };
      const result = await this.wineService.saveWine(payload);
      success = result !== null;
      if (result) {
        this.existingWineId = result.wineId;
      }
    }

    if (success) {
      this.notifications.success(this.editMode() ? 'Vin oppdatert!' : 'Vin lagret!');
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

  // ── Catalogue name suggestion ─────────────────────────────────────────────

  protected fieldSource(field: string): string | null {
    if (this.editMode() || !this.originalData) return null;
    if (field === 'description' || field === 'foodPairings' || field === 'technicalNotes')
      return 'AI';
    if (this.candidateSelected) {
      const candidateLabel = this.candidateIsLocal ? 'Katalog' : 'WineAPI';
      return this.candidateFields[field] ? candidateLabel : 'OCR';
    }
    if (field === 'name' || field === 'producer')
      return this.nameFromCatalogue() ? 'Katalog' : 'OCR';
    return 'OCR';
  }

  protected acceptSuggestion(): void {
    const sn = this.suggestedName();
    const sp = this.suggestedProducer();
    if (sn) this.name.set(sn);
    if (sp) this.producer.set(sp);
    this.showSuggestion.set(false);
  }

  protected dismissSuggestion(): void {
    this.showSuggestion.set(false);
  }

  // ── Wine search (manual entry) ────────────────────────────────────────────

  protected onSearchInput(query: string): void {
    this.searchQuery.set(query);
    if (this.searchTimeout) clearTimeout(this.searchTimeout);
    if (query.trim().length < 2) {
      this.searchResults.set([]);
      return;
    }
    this.searchTimeout = setTimeout(() => this.runSearch(query.trim()), 300);
  }

  private async runSearch(query: string): Promise<void> {
    this.searching.set(true);
    const results = await this.wineService.searchWines(query);
    this.searchResults.set(results);
    this.searching.set(false);
  }

  protected selectSearchResult(wine: WineSearchResult): void {
    this.name.set(wine.name);
    this.producer.set(wine.producer);
    this.vintage.set(wine.vintage);
    this.type.set(wine.type);
    if (wine.country) this.country.set(wine.country);
    if (wine.region)  this.region.set(wine.region);
    if (wine.grapes?.length)  this.grapeVariety.set(wine.grapes.join(', '));
    if (wine.alcoholContent != null) this.alcoholContent.set(`${wine.alcoholContent}%`);
    this.grapes = wine.grapes ?? null;
    this.alcoholNum = wine.alcoholContent ?? null;
    this.existingWineId = wine.id;
    this.searchStep.set(false);
  }

  protected skipSearch(): void {
    this.searchStep.set(false);
  }

  protected onLocationSelected(loc: LocationSelection): void {
    this.locationName.set(loc.name);
    this.locationLat.set(loc.lat);
    this.locationLng.set(loc.lng);
    this.locationType.set(loc.type);
    this.locationSet.set(true);
    this.locationInitialQuery.set(null);
  }

  protected clearLocation(): void {
    this.locationInitialQuery.set(this.locationName());
    // Keep name/lat/lng — they're used to re-persist if user only changes type
    this.locationSet.set(false);
  }

  protected onLocationTypeChanged(type: string): void {
    this.locationType.set(type);
    // If old coordinates are still in memory, re-confirm the location with the new type
    if (this.locationName()) {
      this.locationSet.set(true);
    }
  }

  // ── Report incorrect info ──────────────────────────────────────────────

  protected async submitReport(): Promise<void> {
    const comment = this.reportComment().trim();
    if (!comment || !this.existingWineId) return;
    this.reportSubmitting.set(true);
    const ok = await this.wineService.reportWine(this.existingWineId, comment);
    this.reportSubmitting.set(false);
    if (ok) {
      this.notifications.success('Takk for tilbakemeldingen!');
      this.showReportModal.set(false);
      this.reportComment.set('');
    } else {
      this.notifications.error('Kunne ikke sende rapporten. Prøv igjen.');
    }
  }
}
