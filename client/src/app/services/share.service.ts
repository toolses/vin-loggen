import { Injectable } from '@angular/core';
import type { Wine } from './wine.service';

@Injectable({ providedIn: 'root' })
export class ShareService {

  async generateShareImage(element: HTMLElement): Promise<Blob> {
    // Convert cross-origin images to inline data URLs so html-to-image can render them
    await this.inlineExternalImages(element);

    const { toPng } = await import('html-to-image');
    const dataUrl = await toPng(element, {
      pixelRatio: 2,
      backgroundColor: '#121212',
      cacheBust: true,
    });
    const res = await fetch(dataUrl);
    return res.blob();
  }

  private async inlineExternalImages(root: HTMLElement): Promise<void> {
    const images = root.querySelectorAll<HTMLImageElement>('img[src]');
    await Promise.all(
      Array.from(images).map(async (img) => {
        const src = img.src;
        if (!src || src.startsWith('data:')) return;
        try {
          // Load via Image + canvas — more reliable on iOS Safari than fetch
          const dataUrl = await this.imageToDataUrl(src);
          img.src = dataUrl;
        } catch {
          // If canvas approach fails, try fetch as fallback
          try {
            const resp = await fetch(src, { mode: 'cors' });
            const blob = await resp.blob();
            img.src = await this.blobToDataUrl(blob);
          } catch {
            // Image will be missing in export
          }
        }
      }),
    );
  }

  /**
   * Load an image via HTMLImageElement and draw it onto a canvas to extract
   * a data URL. This avoids iOS Safari fetch/CORS issues with Supabase
   * storage URLs by using the browser's native image loading pipeline.
   */
  private imageToDataUrl(src: string): Promise<string> {
    return new Promise((resolve, reject) => {
      const img = new Image();
      img.crossOrigin = 'anonymous';
      img.onload = () => {
        try {
          const canvas = document.createElement('canvas');
          canvas.width = img.naturalWidth;
          canvas.height = img.naturalHeight;
          const ctx = canvas.getContext('2d');
          if (!ctx) { reject(new Error('No 2d context')); return; }
          ctx.drawImage(img, 0, 0);
          resolve(canvas.toDataURL('image/png'));
        } catch (e) {
          reject(e);
        }
      };
      img.onerror = () => reject(new Error('Image load failed'));
      img.src = src;
    });
  }

  private blobToDataUrl(blob: Blob): Promise<string> {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onloadend = () => resolve(reader.result as string);
      reader.onerror = reject;
      reader.readAsDataURL(blob);
    });
  }

  async shareWine(wine: Wine, imageBlob: Blob): Promise<void> {
    // Haptic feedback when image is ready
    if (navigator.vibrate) {
      navigator.vibrate(100);
    }

    const file = new File(
      [imageBlob],
      `vinsomm-${wine.name.replace(/\s+/g, '-').toLowerCase()}.png`,
      { type: 'image/png' },
    );

    if (navigator.canShare?.({ files: [file] })) {
      await navigator.share({
        title: `${wine.name} – VinSomm`,
        text: `Sjekk ut denne vinen: ${wine.name} fra ${wine.producer}`,
        files: [file],
      });
    } else {
      // Fallback: download the image
      const url = URL.createObjectURL(imageBlob);
      const a = document.createElement('a');
      a.href = url;
      a.download = file.name;
      a.click();
      URL.revokeObjectURL(url);
    }
  }
}
