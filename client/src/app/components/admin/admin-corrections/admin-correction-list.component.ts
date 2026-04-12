import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import {
  AdminCorrectionService,
  type AdminCorrectionSearchParams,
} from '../../../services/admin-correction.service';

@Component({
  selector: 'app-admin-correction-list',
  standalone: true,
  imports: [RouterLink, FormsModule, DatePipe],
  templateUrl: './admin-correction-list.component.html',
})
export class AdminCorrectionListComponent implements OnInit {
  protected readonly service = inject(AdminCorrectionService);

  protected readonly search = signal('');
  protected readonly sourceFilter = signal('');

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(page = 1): Promise<void> {
    const params: AdminCorrectionSearchParams = { page, pageSize: 25 };
    if (this.search().trim()) params.search = this.search().trim();
    if (this.sourceFilter()) params.source = this.sourceFilter();
    await this.service.loadCorrections(params);
  }

  protected async onSearch(): Promise<void> {
    await this.load(1);
  }

  protected get totalPages(): number {
    return Math.ceil(this.service.totalCount() / this.service.pageSize());
  }

  protected sourceLabel(source: string): string {
    const labels: Record<string, string> = {
      gemini: 'Gemini',
      wineapi: 'WineAPI',
      manual: 'Manuell',
    };
    return labels[source] ?? source;
  }

  protected sourceBadgeClass(source: string): string {
    switch (source) {
      case 'gemini': return 'bg-blue-500/20 text-blue-300 border-blue-500/30';
      case 'wineapi': return 'bg-green-500/20 text-green-300 border-green-500/30';
      case 'manual': return 'bg-gold/20 text-gold border-gold/30';
      default: return 'bg-white/10 text-cream/60 border-white/20';
    }
  }
}
