import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { AdminCorrectionService, type AdminCorrectionDetail } from '../../../services/admin-correction.service';
import { NotificationService } from '../../../services/notification.service';

@Component({
  selector: 'app-admin-correction-detail',
  standalone: true,
  imports: [RouterLink, DatePipe],
  templateUrl: './admin-correction-detail.component.html',
})
export class AdminCorrectionDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(AdminCorrectionService);
  private readonly notifications = inject(NotificationService);

  protected readonly correction = signal<AdminCorrectionDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly deleting = signal(false);
  protected readonly showDeleteConfirm = signal(false);

  // Parsed JSONB diffs
  protected readonly originalFields = signal<Record<string, unknown>>({});
  protected readonly correctedFields = signal<Record<string, unknown>>({});

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.router.navigate(['/admin/corrections']);
      return;
    }
    const data = await this.service.getCorrection(id);
    if (!data) {
      this.router.navigate(['/admin/corrections']);
      return;
    }
    this.correction.set(data);
    try {
      this.originalFields.set(JSON.parse(data.originalData));
      this.correctedFields.set(JSON.parse(data.correctedData));
    } catch { /* keep defaults */ }
    this.loading.set(false);
  }

  protected get fieldNames(): string[] {
    const keys = new Set([
      ...Object.keys(this.originalFields()),
      ...Object.keys(this.correctedFields()),
    ]);
    return Array.from(keys);
  }

  protected fieldLabel(key: string): string {
    const labels: Record<string, string> = {
      name: 'Navn',
      producer: 'Produsent',
      vintage: 'Årgang',
      type: 'Type',
      country: 'Land',
      region: 'Region',
      grapes: 'Druer',
      alcoholContent: 'Alkohol',
      comment: 'Kommentar',
      fieldName: 'Felt',
    };
    return labels[key] ?? key;
  }

  protected formatValue(val: unknown): string {
    if (val === null || val === undefined) return '—';
    if (Array.isArray(val)) return val.join(', ');
    return String(val);
  }

  protected sourceLabel(source: string): string {
    const labels: Record<string, string> = {
      gemini: 'Gemini AI',
      wineapi: 'WineAPI',
      manual: 'Manuell rapport',
    };
    return labels[source] ?? source;
  }

  protected async deleteCorrection(): Promise<void> {
    const c = this.correction();
    if (!c) return;
    this.deleting.set(true);
    const ok = await this.service.deleteCorrection(c.id);
    this.deleting.set(false);
    if (ok) {
      this.notifications.success('Korreksjon slettet.');
      this.router.navigate(['/admin/corrections']);
    } else {
      this.notifications.error('Kunne ikke slette korreksjonen.');
    }
  }
}
