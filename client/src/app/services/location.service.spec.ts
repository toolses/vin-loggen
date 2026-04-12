import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { LocationService } from './location.service';
import { vi } from 'vitest';

// ── Helpers ──────────────────────────────────────────────────────────────────

/** Build a fake Geocoding v6 reverse response (still Mapbox) */
function fakeReverseResponse(overrides: {
  name?: string;
  full_address?: string;
} = {}) {
  const {
    name = 'Storgata 15',
    full_address = 'Storgata 15, 0123 Oslo, Norway',
  } = overrides;

  return {
    type: 'FeatureCollection',
    features: [
      {
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [10.75, 59.91] },
        properties: { name, full_address },
      },
    ],
  };
}

function mockFetchJson(data: unknown) {
  vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
    json: () => Promise.resolve(data),
  } as Response);
}

// ── Suite: searchPlaces (Google Places via backend proxy) ────────────────────

describe('LocationService – searchPlaces', () => {
  let service: LocationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        LocationService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(LocationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
  });

  it('calls the backend autocomplete endpoint', async () => {
    const promise = service.searchPlaces('Test');

    const req = httpMock.expectOne(r => r.url.includes('/locations/autocomplete'));
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('query')).toBe('Test');
    expect(req.request.params.get('sessionToken')).toBeTruthy();
    req.flush([{ placeId: 'abc', mainText: 'Test Place', secondaryText: 'Oslo' }]);

    const results = await promise;
    expect(results).toHaveLength(1);
  });

  it('includes lat/lng when proximity is provided', async () => {
    const promise = service.searchPlaces('Storgata', { lat: 59.91, lng: 10.75 });

    const req = httpMock.expectOne(r => r.url.includes('/locations/autocomplete'));
    expect(req.request.params.get('lat')).toBe('59.91');
    expect(req.request.params.get('lng')).toBe('10.75');
    req.flush([]);

    const results = await promise;
    expect(results).toEqual([]);
  });

  it('maps backend response to PlaceSuggestion with place_id, name, address', async () => {
    const promise = service.searchPlaces('To Rom');

    const req = httpMock.expectOne(r => r.url.includes('/locations/autocomplete'));
    req.flush([
      { placeId: 'poi123', mainText: 'To Rom og Kjøkken', secondaryText: 'Trondheim, Norway' },
    ]);

    const results = await promise;
    expect(results).toHaveLength(1);
    expect(results[0].place_id).toBe('poi123');
    expect(results[0].name).toBe('To Rom og Kjøkken');
    expect(results[0].address).toBe('Trondheim, Norway');
  });

  it('returns empty array for empty query', async () => {
    const results = await service.searchPlaces('   ');
    expect(results).toEqual([]);
  });

  it('returns empty array on HTTP error', async () => {
    const promise = service.searchPlaces('test');

    const req = httpMock.expectOne(r => r.url.includes('/locations/autocomplete'));
    req.error(new ProgressEvent('error'));

    const results = await promise;
    expect(results).toEqual([]);
  });
});

// ── Suite: retrievePlace (Google Places via backend proxy) ───────────────────

describe('LocationService – retrievePlace', () => {
  let service: LocationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        LocationService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(LocationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
  });

  it('calls the backend details endpoint with placeId', async () => {
    const promise = service.retrievePlace('abc123');

    const req = httpMock.expectOne(r => r.url.includes('/locations/details'));
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('placeId')).toBe('abc123');
    expect(req.request.params.get('sessionToken')).toBeTruthy();
    req.flush({ placeId: 'abc123', name: 'Test', address: 'Oslo', lat: 59.91, lng: 10.75, types: [] });

    const place = await promise;
    expect(place).not.toBeNull();
  });

  it('returns Place with name, address, lat, lng', async () => {
    const promise = service.retrievePlace('poi123');

    const req = httpMock.expectOne(r => r.url.includes('/locations/details'));
    req.flush({
      placeId: 'poi123',
      name: 'To Rom og Kjøkken',
      address: 'Carl Johans gate 5, 7010 Trondheim, Norway',
      lat: 63.433,
      lng: 10.395,
      types: ['restaurant'],
    });

    const place = await promise;
    expect(place!.name).toBe('To Rom og Kjøkken');
    expect(place!.address).toBe('Carl Johans gate 5, 7010 Trondheim, Norway');
    expect(place!.lat).toBe(63.433);
    expect(place!.lng).toBe(10.395);
  });

  it('rotates session token after retrieve', async () => {
    const oldToken = (service as any).sessionToken;

    const promise = service.retrievePlace('abc123');

    const req = httpMock.expectOne(r => r.url.includes('/locations/details'));
    req.flush({ placeId: 'abc123', name: 'Test', address: null, lat: 59.91, lng: 10.75, types: null });

    await promise;
    expect((service as any).sessionToken).not.toBe(oldToken);
  });

  it('returns null when placeId is empty', async () => {
    const result = await service.retrievePlace('');
    expect(result).toBeNull();
  });

  it('returns null on HTTP error', async () => {
    const promise = service.retrievePlace('abc123');

    const req = httpMock.expectOne(r => r.url.includes('/locations/details'));
    req.error(new ProgressEvent('error'));

    const result = await promise;
    expect(result).toBeNull();
  });
});

// ── Suite: reverseGeocode (still Mapbox) ─────────────────────────────────────

describe('LocationService – reverseGeocode', () => {
  let service: LocationService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        LocationService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(LocationService);
    (service as any).mapboxToken = 'test-token';
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('calls the Geocoding v6 reverse endpoint', async () => {
    mockFetchJson(fakeReverseResponse({ name: 'Aker Brygge' }));

    await service.reverseGeocode(59.91, 10.75);

    const url = (fetch as any).mock.calls[0][0] as string;
    expect(url).toContain('search/geocode/v6/reverse');
    expect(url).toContain('longitude=10.75');
    expect(url).toContain('latitude=59.91');
  });

  it('returns name and address from v6 properties', async () => {
    mockFetchJson(fakeReverseResponse({
      name: 'Aker Brygge',
      full_address: 'Aker Brygge, 0252 Oslo, Norway',
    }));

    const result = await service.reverseGeocode(59.91, 10.75);

    expect(result).toEqual({
      name: 'Aker Brygge',
      address: 'Aker Brygge, 0252 Oslo, Norway',
    });
  });

  it('returns null when no features are found', async () => {
    mockFetchJson({ type: 'FeatureCollection', features: [] });
    const result = await service.reverseGeocode(0, 0);
    expect(result).toBeNull();
  });

  it('returns null when token is missing', async () => {
    (service as any).mapboxToken = '';
    const result = await service.reverseGeocode(59.91, 10.75);
    expect(result).toBeNull();
  });

  it('returns null on fetch error', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new Error('Network error'));
    const result = await service.reverseGeocode(59.91, 10.75);
    expect(result).toBeNull();
  });
});
