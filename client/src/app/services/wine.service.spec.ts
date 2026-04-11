import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { WineService, WineAnalysisResult } from './wine.service';
import { SupabaseService } from './supabase.service';

// ── Minimal Supabase stub ─────────────────────────────────────────────────────

const mockSupabase = {
  client: {
    auth: { getUser: () => Promise.resolve({ data: { user: { id: 'user-1' } } }) },
    from:    () => ({ select: () => ({ order: () => Promise.resolve({ data: [], error: null }) }) }),
    storage: { from: () => ({ upload: () => Promise.resolve({ data: null, error: null }), getPublicUrl: () => ({ data: { publicUrl: '' } }) }) },
  },
};

// ── A minimal valid analysis result ─────────────────────────────────────────

const baseResult: WineAnalysisResult = {
  wineName:        'Barolo Riserva',
  producer:        'Marchesi di Barolo',
  vintage:         2018,
  country:         'Italia',
  region:          'Piemonte',
  grapes:          ['Nebbiolo'],
  type:            'Rød',
  alcoholContent:  14.5,
  alreadyTasted:   false,
  existingWineId:  null,
  lastRating:      null,
  lastTastedAt:    null,
  foodPairings:    null,
  description:     null,
  technicalNotes:  null,
  externalSourceId: null,
  proLimitReached: false,
  proScansToday:   0,
  dailyProLimit:   10,
  isPro:           false,
};

// ── Suite ─────────────────────────────────────────────────────────────────────

describe('WineService – analyzeLabel', () => {
  let service: WineService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        WineService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SupabaseService, useValue: mockSupabase },
      ],
    });
    service    = TestBed.inject(WineService);
    httpMock   = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('returns the parsed result and stores it in lastScanResult', async () => {
    const blob = new Blob(['fake-image'], { type: 'image/jpeg' });
    const promise = service.analyzeLabel(blob);

    const req = httpMock.expectOne(r => r.url.includes('/wine/analyze'));
    expect(req.request.method).toBe('POST');
    req.flush(baseResult);

    const result = await promise;
    expect(result).toBeTruthy();
    expect(result!.wineName).toBe('Barolo Riserva');
    expect(service.lastScanResult()?.wineName).toBe('Barolo Riserva');
  });

  it('sets error signal and returns null on HTTP error', async () => {
    const blob    = new Blob(['bad'], { type: 'image/jpeg' });
    const promise = service.analyzeLabel(blob);

    const req = httpMock.expectOne(r => r.url.includes('/wine/analyze'));
    req.flush({ detail: 'AI analysis failed' }, { status: 502, statusText: 'Bad Gateway' });

    const result = await promise;
    expect(result).toBeNull();
    expect(service.error()).toBe('AI analysis failed');
  });

  it('clears lastScanResult, imageUrl, and location via clearScanResult', async () => {
    const blob = new Blob(['img'], { type: 'image/jpeg' });
    const promise = service.analyzeLabel(blob);
    httpMock.expectOne(r => r.url.includes('/wine/analyze')).flush(baseResult);
    await promise;

    service.setScanImageUrl('https://cdn.example.com/label.jpg');
    service.setScanLocation(59.91, 10.75);

    service.clearScanResult();

    expect(service.lastScanResult()).toBeNull();
    expect(service.lastScanImageUrl()).toBeNull();
    expect(service.lastScanLocation()).toBeNull();
  });

  it('processing signal is true during the request and false afterwards', async () => {
    const blob = new Blob(['img'], { type: 'image/jpeg' });
    const promise = service.analyzeLabel(blob);

    expect(service.processing()).toBe(true);

    httpMock.expectOne(r => r.url.includes('/wine/analyze')).flush(baseResult);
    await promise;

    expect(service.processing()).toBe(false);
  });
});

// ── Suite: WineAnalysisResult interface completeness ─────────────────────────

describe('WineAnalysisResult interface', () => {
  it('includes all required quota and enrichment fields', () => {
    const result: WineAnalysisResult = baseResult;
    // Verify every expected field is present and has the right type
    expect(typeof result.proLimitReached).toBe('boolean');
    expect(typeof result.proScansToday).toBe('number');
    expect(typeof result.dailyProLimit).toBe('number');
    expect(typeof result.isPro).toBe('boolean');
    expect(result.foodPairings).toBeNull();
    expect(result.technicalNotes).toBeNull();
    expect(result.description).toBeNull();
  });
});
