import { Component, inject, output, signal } from '@angular/core';
import { WineService } from '../../services/wine.service';

@Component({
  selector: 'app-camera-capture',
  standalone: true,
  templateUrl: './camera-capture.component.html',
})
export class CameraCaptureComponent {
  private readonly wineService = inject(WineService);

  protected readonly previewUrl = signal<string | null>(null);
  protected readonly uploading = signal(false);
  protected readonly uploadError = signal<string | null>(null);

  /** Emits the public Supabase Storage URL once upload succeeds. */
  readonly imageCaptured = output<string>();

  protected async onFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.uploadError.set(null);

    // Show local preview immediately
    const reader = new FileReader();
    reader.onload = (e) => this.previewUrl.set(e.target?.result as string);
    reader.readAsDataURL(file);

    this.uploading.set(true);
    try {
      const url = await this.wineService.uploadLabelImage(file);
      if (url) {
        this.imageCaptured.emit(url);
      }
    } catch (err) {
      this.uploadError.set(err instanceof Error ? err.message : 'Opplasting feilet');
    } finally {
      this.uploading.set(false);
      // Reset so the same file can be re-selected
      input.value = '';
    }
  }

  protected clearPreview(): void {
    this.previewUrl.set(null);
    this.uploadError.set(null);
  }
}
