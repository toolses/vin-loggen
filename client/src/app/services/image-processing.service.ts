import { Injectable } from '@angular/core';

export interface ProcessedImages {
  full: Blob;       // 1080px max dimension, WebP 0.8 quality
  thumbnail: Blob;  // 200x200px center-cropped, WebP 0.6 quality
}

@Injectable({ providedIn: 'root' })
export class ImageProcessingService {
  private readonly MAX_DIMENSION = 1080;
  private readonly FULL_QUALITY = 0.8;
  private readonly THUMB_SIZE = 200;
  private readonly THUMB_QUALITY = 0.6;

  private webpSupported: boolean | null = null;

  /**
   * Generates two optimised versions of the source image:
   *  - full: max 1080px dimension, WebP 0.8 quality
   *  - thumbnail: 200x200px center-cropped, WebP 0.6 quality
   */
  async processImage(source: File | Blob): Promise<ProcessedImages> {
    const bitmap = await createImageBitmap(source);
    const useWebP = await this.supportsWebP();

    const [full, thumbnail] = await Promise.all([
      this.generateFull(bitmap, useWebP),
      this.generateThumbnail(bitmap, useWebP),
    ]);

    bitmap.close();
    return { full, thumbnail };
  }

  /**
   * @deprecated Use processImage() instead. Kept for backward compatibility.
   */
  async resizeImage(source: File | Blob): Promise<Blob> {
    const { full } = await this.processImage(source);
    return full;
  }

  private async generateFull(bitmap: ImageBitmap, useWebP: boolean): Promise<Blob> {
    const { width, height } = this.scaleDimensions(bitmap.width, bitmap.height);
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;

    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('Could not get 2D canvas context');

    ctx.drawImage(bitmap, 0, 0, width, height);

    return this.canvasToBlob(canvas, useWebP, this.FULL_QUALITY);
  }

  private async generateThumbnail(bitmap: ImageBitmap, useWebP: boolean): Promise<Blob> {
    // Center-crop to square, then scale to THUMB_SIZE
    const srcSize = Math.min(bitmap.width, bitmap.height);
    const sx = (bitmap.width - srcSize) / 2;
    const sy = (bitmap.height - srcSize) / 2;

    const canvas = document.createElement('canvas');
    canvas.width = this.THUMB_SIZE;
    canvas.height = this.THUMB_SIZE;

    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('Could not get 2D canvas context');

    ctx.drawImage(bitmap, sx, sy, srcSize, srcSize, 0, 0, this.THUMB_SIZE, this.THUMB_SIZE);

    return this.canvasToBlob(canvas, useWebP, this.THUMB_QUALITY);
  }

  private canvasToBlob(canvas: HTMLCanvasElement, useWebP: boolean, quality: number): Promise<Blob> {
    const mimeType = useWebP ? 'image/webp' : 'image/jpeg';
    return new Promise<Blob>((resolve, reject) => {
      canvas.toBlob(
        blob => (blob ? resolve(blob) : reject(new Error('Canvas toBlob returned null'))),
        mimeType,
        quality,
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

  private async supportsWebP(): Promise<boolean> {
    if (this.webpSupported !== null) return this.webpSupported;

    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 1;
    const blob = await new Promise<Blob | null>(r => canvas.toBlob(r, 'image/webp'));
    this.webpSupported = blob?.type === 'image/webp';
    return this.webpSupported;
  }
}
