import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { WineService, NewWine } from '../../services/wine.service';
import { StarRatingComponent } from '../star-rating/star-rating.component';

@Component({
  selector: 'app-wine-editor',
  standalone: true,
  imports: [FormsModule, StarRatingComponent],
  templateUrl: './wine-editor.component.html',
})
export class WineEditorComponent implements OnInit {
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
  protected readonly saving = signal(false);

  protected readonly imageUrl = this.wineService.lastScanImageUrl;
  protected readonly wineTypes = ['Rød', 'Hvit', 'Rosé', 'Musserende', 'Oransje', 'Dessert'];

  ngOnInit(): void {
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
    };

    const success = await this.wineService.addWine(wine);

    if (success) {
      // Haptic feedback on successful save
      if (navigator.vibrate) {
        navigator.vibrate([100, 50, 100]);
      }
      this.wineService.clearScanResult();
      this.router.navigate(['/cellar']);
    }

    this.saving.set(false);
  }

  protected goBack(): void {
    this.router.navigate(['/']);
  }
}
