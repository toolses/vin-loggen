import { Injectable, computed, signal } from '@angular/core';
import { createClient, SupabaseClient } from '@supabase/supabase-js';
import { environment } from '../../environments/environment';

export interface Wine {
  id: string;
  name: string;
  producer: string;
  vintage: number | null;
  type: string;
  country: string | null;
  region: string | null;
  rating: number | null;
  notes: string | null;
  image_url: string | null;
  created_at: string;
}

export type NewWine = Omit<Wine, 'id' | 'created_at'>;

@Injectable({ providedIn: 'root' })
export class WineService {
  private readonly supabase: SupabaseClient;

  private readonly _wines = signal<Wine[]>([]);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);

  readonly wines = this._wines.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  readonly wineCount = computed(() => this._wines().length);

  constructor() {
    this.supabase = createClient(environment.supabaseUrl, environment.supabaseAnonKey);
  }

  async loadWines(): Promise<void> {
    this._loading.set(true);
    this._error.set(null);
    try {
      const { data, error } = await this.supabase
        .from('wines')
        .select('*')
        .order('created_at', { ascending: false });
      if (error) throw error;
      this._wines.set(data ?? []);
    } catch (err) {
      this._error.set(err instanceof Error ? err.message : 'Ukjent feil oppstod');
    } finally {
      this._loading.set(false);
    }
  }

  async addWine(wine: NewWine): Promise<void> {
    this._error.set(null);
    const { error } = await this.supabase.from('wines').insert(wine);
    if (error) {
      this._error.set(error.message);
      return;
    }
    await this.loadWines();
  }

  async uploadLabelImage(file: File): Promise<string | null> {
    const ext = file.name.split('.').pop() ?? 'jpg';
    const path = `labels/${Date.now()}.${ext}`;

    const { data, error } = await this.supabase.storage
      .from('wine-labels')
      .upload(path, file, { contentType: file.type, upsert: false });

    if (error) {
      this._error.set(error.message);
      return null;
    }

    const { data: urlData } = this.supabase.storage
      .from('wine-labels')
      .getPublicUrl(data.path);

    return urlData.publicUrl;
  }
}
