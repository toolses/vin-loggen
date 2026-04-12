import { Injectable } from '@angular/core';
import type { Wine } from './wine.service';

@Injectable({ providedIn: 'root' })
export class ShareService {

  async generateShareImage(element: HTMLElement): Promise<Blob> {
    const { toPng } = await import('html-to-image');
    const dataUrl = await toPng(element, {
      pixelRatio: 2,
      backgroundColor: '#121212',
    });
    const res = await fetch(dataUrl);
    return res.blob();
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
