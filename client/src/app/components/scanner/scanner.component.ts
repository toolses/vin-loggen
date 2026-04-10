import {
  Component,
  ElementRef,
  OnDestroy,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
import { WineService } from '../../services/wine.service';
import { ImageProcessingService } from '../../services/image-processing.service';

@Component({
  selector: 'app-scanner',
  standalone: true,
  templateUrl: './scanner.component.html',
})
export class ScannerComponent implements OnDestroy {
  private readonly router = inject(Router);
  private readonly wineService = inject(WineService);
  private readonly imageProcessing = inject(ImageProcessingService);

  protected readonly videoRef = viewChild<ElementRef<HTMLVideoElement>>('videoEl');
  protected readonly canvasRef = viewChild<ElementRef<HTMLCanvasElement>>('canvasEl');
  protected readonly fileInputRef = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  protected readonly cameraActive = signal(false);
  protected readonly cameraError = signal<string | null>(null);
  protected readonly capturing = signal(false);
  protected readonly processing = this.wineService.processing;
  protected readonly previewUrl = signal<string | null>(null);

  private stream: MediaStream | null = null;
  private previewObjectUrl: string | null = null;

  async startCamera(): Promise<void> {
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'environment', width: { ideal: 1920 }, height: { ideal: 1080 } },
      });
      const video = this.videoRef()?.nativeElement;
      if (video) {
        video.srcObject = this.stream;
        await video.play();
        this.cameraActive.set(true);
        this.cameraError.set(null);
      }
    } catch {
      this.cameraError.set('Kunne ikke åpne kamera. Sjekk tillatelser.');
      this.cameraActive.set(false);
    }
  }

  async capturePhoto(): Promise<void> {
    const video = this.videoRef()?.nativeElement;
    const canvas = this.canvasRef()?.nativeElement;
    if (!video || !canvas) return;

    this.capturing.set(true);

    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    canvas.getContext('2d')?.drawImage(video, 0, 0);

    canvas.toBlob(async (blob) => {
      if (!blob) {
        this.capturing.set(false);
        return;
      }
      await this.processFile(blob);
    }, 'image/jpeg');
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    input.value = '';
    this.capturing.set(true);
    this.processFile(file);
  }

  protected triggerFileInput(): void {
    this.fileInputRef()?.nativeElement.click();
  }

  private async processFile(source: File | Blob): Promise<void> {
    try {
      // 1. Resize to max 1080p, JPEG 0.8 quality
      const resized = await this.imageProcessing.resizeImage(source);

      // 2. Show local preview immediately
      this.revokePreview();
      this.previewObjectUrl = URL.createObjectURL(resized);
      this.previewUrl.set(this.previewObjectUrl);
      this.stopCamera();

      // 3. Build a named File for the Supabase upload
      const uploadFile = new File([resized], `scan-${Date.now()}.jpg`, { type: 'image/jpeg' });

      // 4. Upload to Supabase (for image_url) and send to AI endpoint in parallel
      const [imageUrl] = await Promise.all([
        this.wineService.uploadLabelImage(uploadFile),
        this.wineService.analyzeLabel(resized),
      ]);

      if (imageUrl) {
        this.wineService.setScanImageUrl(imageUrl);
      }

      if (navigator.vibrate) {
        navigator.vibrate(200);
      }

      this.router.navigate(['/edit']);
    } catch (err) {
      console.error('Scanner: processFile error', err);
    } finally {
      this.capturing.set(false);
    }
  }

  private stopCamera(): void {
    this.stream?.getTracks().forEach(t => t.stop());
    this.stream = null;
    this.cameraActive.set(false);
  }

  private revokePreview(): void {
    if (this.previewObjectUrl) {
      URL.revokeObjectURL(this.previewObjectUrl);
      this.previewObjectUrl = null;
    }
  }

  protected goBack(): void {
    this.router.navigate(['/']);
  }

  ngOnDestroy(): void {
    this.stopCamera();
    this.revokePreview();
  }
}
