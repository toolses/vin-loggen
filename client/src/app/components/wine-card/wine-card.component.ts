import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Wine } from '../../services/wine.service';

@Component({
  selector: 'app-wine-card',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './wine-card.component.html',
  host: { class: 'block' },
})
export class WineCardComponent {
  readonly wine = input.required<Wine>();

  readonly mapClicked = output<Wine>();
  readonly shareClicked = output<Wine>();

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

  protected onMapClick(event: Event): void {
    event.stopPropagation();
    event.preventDefault();
    this.mapClicked.emit(this.wine());
  }

  protected onShareClick(event: Event): void {
    event.stopPropagation();
    event.preventDefault();
    this.shareClicked.emit(this.wine());
  }

  protected vinmonopoletSearchUrl(): string {
    const w = this.wine();
    const q = [w.producer, w.name].filter(Boolean).join(' ')
      .split(/\s+/).map(p => encodeURIComponent(p)).join('+');
    return `https://www.vinmonopolet.no/search?q=${q}:relevance`;
  }
}
