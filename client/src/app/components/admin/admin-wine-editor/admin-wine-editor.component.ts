import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AdminWineService, type AdminWineDetail, type AdminWineUpdateRequest } from '../../../services/admin-wine.service';
import { NotificationService } from '../../../services/notification.service';

@Component({
  selector: 'app-admin-wine-editor',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './admin-wine-editor.component.html',
})
export class AdminWineEditorComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly wineService = inject(AdminWineService);
  private readonly notify = inject(NotificationService);

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly wine = signal<AdminWineDetail | null>(null);

  // Form model
  protected readonly name = signal('');
  protected readonly producer = signal('');
  protected readonly vintage = signal<number | null>(null);
  protected readonly type = signal('Rød');
  protected readonly country = signal('');
  protected readonly region = signal('');
  protected readonly grapesText = signal('');
  protected readonly alcoholContent = signal<number | null>(null);
  protected readonly foodPairingsText = signal('');
  protected readonly description = signal('');
  protected readonly technicalNotes = signal('');

  protected readonly wineTypes = [
    'Rød',
    'Hvit',
    'Rosé',
    'Musserende',
    'Oransje',
    'Dessert',
  ];

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.router.navigate(['/admin/wines']);
      return;
    }

    const wine = await this.wineService.getWine(id);
    if (!wine) {
      this.notify.show('Fant ikke vinen', 'error');
      this.router.navigate(['/admin/wines']);
      return;
    }

    this.wine.set(wine);
    this.name.set(wine.name);
    this.producer.set(wine.producer);
    this.vintage.set(wine.vintage);
    this.type.set(wine.type);
    this.country.set(wine.country ?? '');
    this.region.set(wine.region ?? '');
    this.grapesText.set(wine.grapes?.join(', ') ?? '');
    this.alcoholContent.set(wine.alcoholContent);
    this.foodPairingsText.set(wine.foodPairings?.join(', ') ?? '');
    this.description.set(wine.description ?? '');
    this.technicalNotes.set(wine.technicalNotes ?? '');
    this.loading.set(false);
  }

  async save(): Promise<void> {
    const w = this.wine();
    if (!w) return;

    this.saving.set(true);

    const parseList = (text: string): string[] | null => {
      const items = text
        .split(',')
        .map(s => s.trim())
        .filter(Boolean);
      return items.length > 0 ? items : null;
    };

    const request: AdminWineUpdateRequest = {
      name: this.name(),
      producer: this.producer(),
      vintage: this.vintage(),
      type: this.type(),
      country: this.country() || null,
      region: this.region() || null,
      grapes: parseList(this.grapesText()),
      alcoholContent: this.alcoholContent(),
      foodPairings: parseList(this.foodPairingsText()),
      description: this.description() || null,
      technicalNotes: this.technicalNotes() || null,
    };

    const result = await this.wineService.updateWine(w.id, request);
    this.saving.set(false);

    if (result) {
      this.notify.show('Vin oppdatert', 'success');
      this.router.navigate(['/admin/wines']);
    } else {
      this.notify.show('Kunne ikke oppdatere vinen', 'error');
    }
  }

  cancel(): void {
    this.router.navigate(['/admin/wines']);
  }
}
