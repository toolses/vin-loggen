import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ImageProcessingService {
  private readonly MAX_DIMENSION = 1080;
  private readonly JPEG_QUALITY = 0.8;

  /**
   * Resizes the source image so neither dimension exceeds 1080px,
   * then re-encodes as JPEG at 0.8 quality to reduce upload size.
   */
  async resizeImage(source: File | Blob): Promise<Blob> {
    const bitmap = await createImageBitmap(source);
    const { width, height } = this.scaleDimensions(bitmap.width, bitmap.height);

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;

    const ctx = canvas.getContext('2d');
    if (!ctx) {
      bitmap.close();
      throw new Error('Could not get 2D canvas context');
    }

    ctx.drawImage(bitmap, 0, 0, width, height);
    bitmap.close();

    return new Promise<Blob>((resolve, reject) => {
      canvas.toBlob(
        blob => (blob ? resolve(blob) : reject(new Error('Canvas toBlob returned null'))),
        'image/jpeg',
        this.JPEG_QUALITY,
      );
    });
  }

  private scaleDimensions(w: number, h: number): { width: number; height: number } {
    if (w <= this.MAX_DIMENSION && h <= this.MAX_DIMENSION) {
      return { width: w, height: h };
    }
    const ratio = Math.min(this.MAX_DIMENSION / w, this.MAX_DIMENSION / h);
    return { width: Math.round(w * ratio), height: Math.round(h * ratio) };
  }
}
