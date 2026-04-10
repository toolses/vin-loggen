import { Component, model } from '@angular/core';

@Component({
  selector: 'app-star-rating',
  standalone: true,
  templateUrl: './star-rating.component.html',
})
export class StarRatingComponent {
  readonly rating = model<number>(0);

  protected readonly stars = [1, 2, 3, 4, 5, 6];

  protected setRating(value: number): void {
    this.rating.set(value);
    if (navigator.vibrate) {
      navigator.vibrate(50);
    }
  }
}
