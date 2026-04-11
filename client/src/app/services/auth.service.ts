import { Injectable, computed, inject, signal } from '@angular/core';
import { SupabaseService } from './supabase.service';
import type { User, Session } from '@supabase/supabase-js';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly supabase = inject(SupabaseService).client;

  private readonly _user = signal<User | null>(null);
  private readonly _session = signal<Session | null>(null);
  private readonly _loading = signal(true);

  readonly user = this._user.asReadonly();
  readonly session = this._session.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly isLoggedIn = computed(() => !!this._user());
  readonly avatarUrl = computed(() => this._user()?.user_metadata?.['avatar_url'] ?? null);
  readonly displayName = computed(() =>
    this._user()?.user_metadata?.['full_name'] ?? this._user()?.email ?? null,
  );

  constructor() {
    this.restoreSession();
    this.supabase.auth.onAuthStateChange((_event, session) => {
      this._session.set(session);
      this._user.set(session?.user ?? null);
      this._loading.set(false);
    });
  }

  async signUp(email: string, password: string, name: string): Promise<{ error: string | null }> {
    const { error } = await this.supabase.auth.signUp({
      email,
      password,
      options: { data: { full_name: name } },
    });
    return { error: error?.message ?? null };
  }

  async signInWithEmail(email: string, password: string): Promise<{ error: string | null }> {
    const { error } = await this.supabase.auth.signInWithPassword({ email, password });
    return { error: error?.message ?? null };
  }

  async signOut(): Promise<void> {
    await this.supabase.auth.signOut();
    this._user.set(null);
    this._session.set(null);
  }

  private async restoreSession(): Promise<void> {
    const { data } = await this.supabase.auth.getSession();
    this._session.set(data.session);
    this._user.set(data.session?.user ?? null);
    this._loading.set(false);
  }
}
