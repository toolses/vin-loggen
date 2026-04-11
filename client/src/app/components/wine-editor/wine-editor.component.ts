import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { WineService, NewWine } from '../../services/wine.service';
import { StarRatingComponent } from '../star-rating/star-rating.component';

@Component({
  selector: 'app-wine-editor',
  standalone: true,
  imports: [FormsModule, StarRatingComponent],
  templateUrl: './wine-editor.component.html',
})
export class WineEditorComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly wineService = inject(WineService);

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

  protected readonly imageUrl = signal<string | null>(null);
  protected readonly wineTypes = ['Rød', 'Hvit', 'Rosé', 'Musserende', 'Oransje', 'Dessert'];

  // Edit mode state
  protected readonly editMode = signal(false);
  private editId = '';

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id) {
      // Edit mode: load existing wine
      this.editMode.set(true);
      this.editId = id;
      this.loadExistingWine(id);
    } else {
      // Create mode: pre-fill from scan result and default tasted_at to today
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
      }
    }
  }

  private async loadExistingWine(id: string): Promise<void> {
    let wine = this.wineService.getWine(id);
    if (!wine) {
      wine = await this.wineService.fetchWine(id) ?? undefined;
    }
    if (!wine) {
      this.router.navigate(['/cellar']);
      return;
    }

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
  }

  protected async save(): Promise<void> {
    if (!this.name() || !this.producer()) return;

    this.saving.set(true);

    const wine: NewWine = {
      name: this.name(),
      producer: this.producer(),
      vintage: this.vintage(),
      type: this.type(),
      country: this.country() || null,
      region: this.region() || null,
      rating: this.rating() > 0 ? this.rating() : null,
      notes: this.notes() || null,
      image_url: this.imageUrl() || null,
      tasted_at: this.tastedAt() || null,
    };

    let success: boolean;
    if (this.editMode()) {
      success = await this.wineService.updateWine(this.editId, wine);
    } else {
      success = await this.wineService.addWine(wine);
    }

    if (success) {
      if (navigator.vibrate) {
        navigator.vibrate([100, 50, 100]);
      }
      if (!this.editMode()) {
        this.wineService.clearScanResult();
      }
      this.router.navigate(this.editMode() ? ['/wines', this.editId] : ['/cellar']);
    }

    this.saving.set(false);
  }

  protected goBack(): void {
    if (this.editMode()) {
      this.router.navigate(['/wines', this.editId]);
    } else {
      this.router.navigate(['/']);
    }
  }
}
