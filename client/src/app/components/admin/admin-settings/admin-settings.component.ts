import { Component, inject, OnInit, signal } from '@angular/core';
import { AdminSettingsService } from '../../../services/admin-settings.service';
import { NotificationService } from '../../../services/notification.service';

@Component({
  selector: 'app-admin-settings',
  standalone: true,
  templateUrl: './admin-settings.component.html',
})
export class AdminSettingsComponent implements OnInit {
  protected readonly settingsService = inject(AdminSettingsService);
  private readonly notificationService = inject(NotificationService);

  protected readonly saving = signal(false);

  async ngOnInit(): Promise<void> {
    await this.settingsService.loadSettings();
  }

  async onExpertModeChange(event: Event): Promise<void> {
    const value = (event.target as HTMLSelectElement).value;
    this.saving.set(true);
    const ok = await this.settingsService.updateSetting('expert_mode', value);
    this.saving.set(false);
    if (ok) {
      this.notificationService.show('Ekspertmodus oppdatert', 'success');
    } else {
      this.notificationService.show('Kunne ikke oppdatere innstilling', 'error');
    }
  }
}
