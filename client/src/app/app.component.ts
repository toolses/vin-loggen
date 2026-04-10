import { Component, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { WineService } from './services/wine.service';
import { CameraCaptureComponent } from './components/camera-capture/camera-capture.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, CameraCaptureComponent],
  templateUrl: './app.component.html',
})
export class AppComponent implements OnInit {
  protected readonly wineService = inject(WineService);

  ngOnInit(): void {
    this.wineService.loadWines();
  }

  protected onImageCaptured(imageUrl: string): void {
    console.log('Label uploaded:', imageUrl);
    // TODO: Call /api/process-label with imageUrl, then open pre-filled wine form
  }
}
