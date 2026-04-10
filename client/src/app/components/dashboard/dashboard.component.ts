import { Component, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { WineService } from '../../services/wine.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent implements OnInit {
  protected readonly wineService = inject(WineService);

  ngOnInit(): void {
    this.wineService.loadWines();
  }

  protected recentWines() {
    return this.wineService.wines().slice(0, 8);
  }
}
