import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { signal } from '@angular/core';
import { ProfileService } from './profile.service';
import { SupabaseService } from './supabase.service';
import { AuthService } from './auth.service';

// ── Minimal stubs ─────────────────────────────────────────────────────────────

const mockSupabase = {
  client: {
    from: () => ({
      select: () => ({
        eq: () => ({
          single: () => Promise.resolve({ data: null, error: null }),
        }),
      }),
    }),
  },
};

const mockAuth = {
  user: signal<{ id: string } | null>(null),
};

// ── Suite ─────────────────────────────────────────────────────────────────────

describe('ProfileService – pro quota', () => {
  let service: ProfileService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ProfileService,
        provideHttpClient(),
        { provide: SupabaseService, useValue: mockSupabase },
        { provide: AuthService,    useValue: mockAuth  },
      ],
    });
    service = TestBed.inject(ProfileService);
  });

  it('proScansRemaining defaults to dailyProLimit (no scans yet)', () => {
    expect(service.proScansRemaining()).toBe(10);
    expect(service.proScansToday()).toBe(0);
    expect(service.isPro()).toBe(false);
  });

  it('syncQuotaFromScan updates all quota signals correctly', () => {
    service.syncQuotaFromScan(7, 10, false);
    expect(service.proScansToday()).toBe(7);
    expect(service.dailyProLimit()).toBe(10);
    expect(service.proScansRemaining()).toBe(3);
    expect(service.isPro()).toBe(false);
  });

  it('proScansRemaining never goes below 0 when scans exceed limit', () => {
    service.syncQuotaFromScan(15, 10, false);
    expect(service.proScansRemaining()).toBe(0);
  });

  it('isPro reflects the tier from syncQuotaFromScan', () => {
    service.syncQuotaFromScan(0, 10, true);
    expect(service.isPro()).toBe(true);
  });

  it('dailyProLimit updates when server returns a different cap', () => {
    service.syncQuotaFromScan(3, 5, false);
    expect(service.dailyProLimit()).toBe(5);
    expect(service.proScansRemaining()).toBe(2);
  });

  it('proScansRemaining reacts to sequential syncQuotaFromScan calls', () => {
    service.syncQuotaFromScan(2, 10, false);
    expect(service.proScansRemaining()).toBe(8);

    service.syncQuotaFromScan(9, 10, false);
    expect(service.proScansRemaining()).toBe(1);

    service.syncQuotaFromScan(10, 10, false);
    expect(service.proScansRemaining()).toBe(0);
  });
});
