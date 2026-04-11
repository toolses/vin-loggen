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
import { ProfileService } from '../../services/profile.service';
import { AuthService } from '../../services/auth.service';
import { LocationService } from '../../services/location.service';
import { ImageProcessingService, ProcessedImages } from '../../services/image-processing.service';
import { NotificationService } from '../../services/notification.service';

type ScanStep = 'idle' | 'front' | 'back' | 'processing';

@Component({
  selector: 'app-scanner',
  standalone: true,
  templateUrl: './scanner.component.html',
})
export class ScannerComponent implements OnDestroy {
  private readonly router = inject(Router);
  private readonly wineService = inject(WineService);
  private readonly locationService = inject(LocationService);
  private readonly imageProcessing = inject(ImageProcessingService);
  private readonly notifications = inject(NotificationService);
  protected readonly profileService = inject(ProfileService);
  protected readonly auth = inject(AuthService);

  protected readonly videoRef = viewChild<ElementRef<HTMLVideoElement>>('videoEl');
  protected readonly canvasRef = viewChild<ElementRef<HTMLCanvasElement>>('canvasEl');
  protected readonly fileInputRef = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  protected readonly cameraActive = signal(false);
  protected readonly cameraError = signal<string | null>(null);
  protected readonly capturing = signal(false);
  protected readonly processing = this.wineService.processing;

  /** Current step in the scanning flow */
  protected readonly step = signal<ScanStep>('idle');

  /** Thumbnail previews for captured images */
  protected readonly frontPreviewUrl = signal<string | null>(null);
  protected readonly backPreviewUrl = signal<string | null>(null);

  private stream: MediaStream | null = null;
  private frontPreviewObjectUrl: string | null = null;
  private backPreviewObjectUrl: string | null = null;

  /** Processed images awaiting submission */
  private frontImages: ProcessedImages | null = null;
  private backImages: ProcessedImages | null = null;

  /** Track which image the file input targets */
  private fileInputTarget: 'front' | 'back' = 'front';

  private locationPromise: Promise<{ lat: number; lng: number } | null>;

  constructor() {
    this.locationPromise = this.locationService.getCurrentPosition().catch(() => null);
    this.profileService.loadProQuota();
  }

  async startCamera(): Promise<void> {
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'environment', width: { ideal: 1920 }, height: { ideal: 1080 } },
      });
      this.cameraActive.set(true);
      this.cameraError.set(null);

      if (this.step() === 'idle') {
        this.step.set('front');
      }

      await new Promise(r => requestAnimationFrame(r));
      const video = this.videoRef()?.nativeElement;
      if (video) {
        video.srcObject = this.stream;
        await video.play();
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
      await this.handleCapture(blob);
    }, 'image/jpeg');
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    input.value = '';
    this.capturing.set(true);
    this.handleCapture(file);
  }

  protected triggerFileInput(target: 'front' | 'back' = 'front'): void {
    this.fileInputTarget = target;
    this.fileInputRef()?.nativeElement.click();
  }

  /** Called after capturing an image from camera or file picker */
  private async handleCapture(source: File | Blob): Promise<void> {
    try {
      const processed = await this.imageProcessing.processImage(source);
      const currentStep = this.step() === 'idle' ? 'front' : this.step();

      if (currentStep === 'front' || this.fileInputTarget === 'front') {
        this.frontImages = processed;
        this.revokePreview('front');
        this.frontPreviewObjectUrl = URL.createObjectURL(processed.thumbnail);
        this.frontPreviewUrl.set(this.frontPreviewObjectUrl);
        this.stopCamera();
        // Move to back-capture step
        this.step.set('back');
      } else {
        this.backImages = processed;
        this.revokePreview('back');
        this.backPreviewObjectUrl = URL.createObjectURL(processed.thumbnail);
        this.backPreviewUrl.set(this.backPreviewObjectUrl);
        this.stopCamera();
      }
    } catch (err) {
      console.error('Scanner: image processing error', err);
      this.notifications.error('Kunne ikke behandle bildet. Prøv igjen.');
    } finally {
      this.capturing.set(false);
    }
  }

  /** Skip back label capture and proceed with front only */
  protected skipBack(): void {
    this.submitImages();
  }

  /** User wants to capture the back label */
  protected captureBack(): void {
    this.step.set('back');
    this.startCamera();
  }

  /** Re-take the front image */
  protected retakeFront(): void {
    this.frontImages = null;
    this.revokePreview('front');
    this.frontPreviewUrl.set(null);
    this.step.set('front');
    this.startCamera();
  }

  /** Re-take the back image */
  protected retakeBack(): void {
    this.backImages = null;
    this.revokePreview('back');
    this.backPreviewUrl.set(null);
    this.step.set('back');
    this.startCamera();
  }

  /** Submit captured images (front required, back optional) */
  protected async submitImages(): Promise<void> {
    if (!this.frontImages) return;

    this.step.set('processing');

    try {
      // Upload to Supabase and send to AI in parallel
      const [uploadResult] = await Promise.all([
        this.wineService.uploadLabelImages(
          this.frontImages.full,
          this.frontImages.thumbnail,
          this.backImages?.full ?? null,
          this.backImages?.thumbnail ?? null,
        ),
        this.wineService.analyzeLabel(
          this.frontImages.full,
          this.backImages?.full ?? null,
        ),
      ]);

      if (uploadResult) {
        this.wineService.setScanImageUrl(uploadResult.imageUrl);
        this.wineService.setScanThumbnailUrl(uploadResult.thumbnailUrl);
      }

      const loc = await this.locationPromise;
      if (loc) {
        this.wineService.setScanLocation(loc.lat, loc.lng);
      }

      if (navigator.vibrate) {
        navigator.vibrate(200);
      }

      this.router.navigate(['/edit']);
    } catch (err) {
      console.error('Scanner: submitImages error', err);
      this.notifications.error('Noe gikk galt under analyse. Prøv igjen.');
      this.step.set('back');
    }
  }

  private stopCamera(): void {
    this.stream?.getTracks().forEach(t => t.stop());
    this.stream = null;
    this.cameraActive.set(false);
  }

  private revokePreview(which: 'front' | 'back'): void {
    if (which === 'front' && this.frontPreviewObjectUrl) {
      URL.revokeObjectURL(this.frontPreviewObjectUrl);
      this.frontPreviewObjectUrl = null;
    }
    if (which === 'back' && this.backPreviewObjectUrl) {
      URL.revokeObjectURL(this.backPreviewObjectUrl);
      this.backPreviewObjectUrl = null;
    }
  }

  protected goBack(): void {
    this.router.navigate(['/']);
  }

  ngOnDestroy(): void {
    this.stopCamera();
    this.revokePreview('front');
    this.revokePreview('back');
  }
}
