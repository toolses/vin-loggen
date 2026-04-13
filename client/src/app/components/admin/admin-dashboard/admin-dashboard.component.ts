import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminUsageService, type DailyUsageRow } from '../../../services/admin-usage.service';
import { AdminWineService } from '../../../services/admin-wine.service';
import { AdminCorrectionService } from '../../../services/admin-correction.service';
import { NotificationService } from '../../../services/notification.service';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './admin-dashboard.component.html',
})
export class AdminDashboardComponent implements OnInit {
  protected readonly usageService = inject(AdminUsageService);
  protected readonly wineService = inject(AdminWineService);
  protected readonly correctionService = inject(AdminCorrectionService);
  private readonly notificationService = inject(NotificationService);

  protected readonly totalWines = signal(0);
  protected readonly totalCorrections = signal(0);
  protected readonly resetting = signal(false);
  protected readonly showResetConfirm = signal(false);

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
      this.correctionService.loadCorrections({ page: 1, pageSize: 1 }),
    ]);
    this.totalWines.set(this.wineService.totalCount());
    this.totalCorrections.set(this.correctionService.totalCount());
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
      deepseek: 'DeepSeek',
      wineapi: 'WineAPI',
    };
    return labels[provider] ?? provider;
  }

  endpointLabel(endpoint: string): string {
    const labels: Record<string, string> = {
      ExpertChat: 'Expert',
      AnalyzeLabel: 'Skann',
      GetFoodPairings: 'Matkombo',
    };
    return labels[endpoint] ?? endpoint;
  }

  providerIcon(provider: string): string {
    const icons: Record<string, string> = {
      deepseek: 'DS',
      gemini: 'GEM',
      wineapi: 'API',
    };
    return icons[provider] ?? '?';
  }

  async resetData(): Promise<void> {
    this.resetting.set(true);
    const result = await this.wineService.resetAllData();
    this.resetting.set(false);
    this.showResetConfirm.set(false);
    if (result) {
      this.notificationService.show(`Slettet ${result.deletedWines} viner, ${result.deletedWineLogs} loggføringer og ${result.deletedExternalIds} eksterne ID-er.`, 'success');
      this.totalWines.set(0);
      await Promise.all([
        this.usageService.loadTodayUsage(),
        this.usageService.loadDailyUsage(30),
      ]);
    } else {
      this.notificationService.show('Kunne ikke slette data. Prøv igjen.', 'error');
    }
  }
}
