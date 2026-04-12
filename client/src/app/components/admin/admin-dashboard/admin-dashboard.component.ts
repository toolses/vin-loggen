import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminUsageService, type DailyUsageRow } from '../../../services/admin-usage.service';
import { AdminWineService } from '../../../services/admin-wine.service';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './admin-dashboard.component.html',
})
export class AdminDashboardComponent implements OnInit {
  protected readonly usageService = inject(AdminUsageService);
  protected readonly wineService = inject(AdminWineService);

  protected readonly totalWines = signal(0);

  protected readonly dailyGrouped = computed(() => {
    const rows = this.usageService.dailyUsage();
    const map = new Map<string, DailyUsageRow[]>();
    for (const row of rows) {
      const existing = map.get(row.date) ?? [];
      existing.push(row);
      map.set(row.date, existing);
    }
    return Array.from(map.entries()).map(([date, providers]) => ({ date, providers }));
  });

  async ngOnInit(): Promise<void> {
    await Promise.all([
      this.usageService.loadTodayUsage(),
      this.usageService.loadDailyUsage(30),
      this.wineService.loadWines({ page: 1, pageSize: 1 }),
    ]);
    this.totalWines.set(this.wineService.totalCount());
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString('nb-NO', {
      day: 'numeric',
      month: 'short',
    });
  }

  providerLabel(provider: string): string {
    const labels: Record<string, string> = {
      gemini: 'Gemini',
      wineapi: 'WineAPI',
    };
    return labels[provider] ?? provider;
  }
}
