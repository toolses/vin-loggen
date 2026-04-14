import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { RouterLink } from '@angular/router';
import { environment } from '../../../../environments/environment';

interface WineApiTestResult {
  correlationId: string;
  found: boolean;
  externalId: string | null;
  suggestedName: string | null;
  suggestedProducer: string | null;
  description: string | null;
  foodPairings: string[] | null;
  technicalNotes: string | null;
  alcoholContent: number | null;
  grapes: string[] | null;
}

@Component({
  selector: 'app-admin-api-test',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './admin-api-test.component.html',
})
export class AdminApiTestComponent {
  private readonly http = inject(HttpClient);

  protected readonly producer = signal('');
  protected readonly name = signal('');
  protected readonly vintage = signal('');
  protected readonly loading = signal(false);
  protected readonly result = signal<WineApiTestResult | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly rawJson = signal<string | null>(null);

  async search(): Promise<void> {
    const p = this.producer().trim();
    const n = this.name().trim();
    if (!p && !n) return;

    this.loading.set(true);
    this.error.set(null);
    this.result.set(null);
    this.rawJson.set(null);

    try {
      let params = new HttpParams()
        .set('producer', p)
        .set('name', n);
      const v = this.vintage().trim();
      if (v) params = params.set('vintage', v);

      const res = await firstValueFrom(
        this.http.get<WineApiTestResult>(`${environment.apiBaseUrl}/admin/api-test/wineapi/search`, { params }),
      );
      this.result.set(res);
      this.rawJson.set(JSON.stringify(res, null, 2));
    } catch (err: unknown) {
      const httpErr = err as { error?: { detail?: string }; status?: number };
      this.error.set(httpErr?.error?.detail ?? 'Søket feilet');
    } finally {
      this.loading.set(false);
    }
  }
}
