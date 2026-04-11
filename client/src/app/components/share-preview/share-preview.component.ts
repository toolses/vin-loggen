import {
  Component,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { WineShareCardComponent } from '../wine-share-card/wine-share-card.component';
import { ShareService } from '../../services/share.service';
import type { Wine } from '../../services/wine.service';

@Component({
  selector: 'app-share-preview',
  standalone: true,
  imports: [WineShareCardComponent],
  templateUrl: './share-preview.component.html',
})
export class SharePreviewComponent {
  private readonly shareService = inject(ShareService);

  readonly wine = input.required<Wine>();
  readonly closed = output<void>();

  protected readonly shareCardRef = viewChild<ElementRef<HTMLDivElement>>('shareCard');
  protected readonly isSharing = signal(false);

  protected async share(): Promise<void> {
    const el = this.shareCardRef()?.nativeElement;
    if (!el) return;

    this.isSharing.set(true);
    try {
      const blob = await this.shareService.generateShareImage(el);
      await this.shareService.shareWine(this.wine(), blob);
    } catch {
      // User cancelled or error
    } finally {
      this.isSharing.set(false);
      this.closed.emit();
    }
  }

  protected close(): void {
    this.closed.emit();
  }
}
