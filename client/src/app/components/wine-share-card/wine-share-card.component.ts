import { Component, input } from '@angular/core';
import { SlicePipe } from '@angular/common';
import type { Wine } from '../../services/wine.service';

@Component({
  selector: 'app-wine-share-card',
  standalone: true,
  imports: [SlicePipe],
  templateUrl: './wine-share-card.component.html',
})
export class WineShareCardComponent {
  readonly wine = input.required<Wine>();

  protected renderStars(rating: number | null): string {
    if (rating == null) return '';
    const full = Math.floor(rating);
    return '★'.repeat(full) + '☆'.repeat(6 - full);
  }
}
