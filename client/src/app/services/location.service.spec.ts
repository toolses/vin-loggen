import { TestBed } from '@angular/core/testing';
import { LocationService } from './location.service';
import { vi } from 'vitest';

// ── Helpers ──────────────────────────────────────────────────────────────────

/** Build a fake Search Box suggest response */
function fakeSuggestions(
  items: { mapbox_id?: string; name?: string; full_address?: string; place_formatted?: string; poi_category?: string[] }[] = [],
) {
  return {
    suggestions: items.map((item) => ({
      mapbox_id: item.mapbox_id ?? 'abc123',
      name: item.name ?? 'Test Place',
      full_address: item.full_address,
      place_formatted: item.place_formatted ?? 'Test, Norway',
      poi_category: item.poi_category,
    })),
  };
}

/** Build a fake Search Box retrieve response */
function fakeRetrieveResponse(overrides: {
  name?: string;
  full_address?: string;
  coordinates?: [number, number];
  poi_category?: string[];
} = {}) {
  const {
    name = 'Storgata 15',
    full_address = 'Storgata 15, 0123 Oslo, Norway',
    coordinates = [10.75, 59.91],
    poi_category,
  } = overrides;

  return {
    type: 'FeatureCollection',
    features: [
      {
        type: 'Feature',
        geometry: { type: 'Point', coordinates },
        properties: {
          name,
          full_address,
          ...(poi_category != null ? { poi_category } : {}),
        },
      },
    ],
  };
}

/** Build a fake Geocoding v6 reverse response */
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

// ── Suite: searchPlaces ──────────────────────────────────────────────────────

describe('LocationService – searchPlaces', () => {
  let service: LocationService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [LocationService] });
    service = TestBed.inject(LocationService);
    (service as any).token = 'test-token';
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('calls the Search Box suggest endpoint', async () => {
    mockFetchJson(fakeSuggestions([{ name: 'Test' }]));

    await service.searchPlaces('Test');

    const url = (fetch as any).mock.calls[0][0] as string;
    expect(url).toContain('search/searchbox/v1/suggest');
  });

  it('includes poi, address, place, neighborhood, and street types', async () => {
    mockFetchJson(fakeSuggestions([{ name: 'Test' }]));

    await service.searchPlaces('Test');

    const url = (fetch as any).mock.calls[0][0] as string;
    expect(url).toContain('types=poi,address,place,neighborhood,street');
  });

  it('includes proximity parameter when provided', async () => {
    mockFetchJson(fakeSuggestions([]));

    await service.searchPlaces('Storgata', { lat: 59.91, lng: 10.75 });

    const url = (fetch as any).mock.calls[0][0] as string;
    expect(url).toContain('proximity=10.75,59.91');
  });

  it('returns PlaceSuggestion with mapbox_id, name, and address', async () => {
    mockFetchJson(fakeSuggestions([
      {
        mapbox_id: 'poi123',
        name: 'To Rom og Kjøkken',
        full_address: 'Carl Johans gate 5, 7010 Trondheim, Norway',
        poi_category: ['restaurant'],
      },
    ]));

    const results = await service.searchPlaces('To Rom');

    expect(results).toHaveLength(1);
    expect(results[0].mapbox_id).toBe('poi123');
    expect(results[0].name).toBe('To Rom og Kjøkken');
    expect(results[0].address).toBe('Carl Johans gate 5, 7010 Trondheim, Norway');
    expect(results[0].category).toBe('restaurant');
  });

  it('falls back to place_formatted when full_address is missing', async () => {
    mockFetchJson(fakeSuggestions([
      { name: 'Oslo', place_formatted: 'Oslo, Norway' },
    ]));

    const results = await service.searchPlaces('Oslo');

    expect(results[0].address).toBe('Oslo, Norway');
  });

  it('returns empty array for empty query', async () => {
    const results = await service.searchPlaces('   ');
    expect(results).toEqual([]);
  });

  it('returns empty array when token is missing', async () => {
    (service as any).token = '';
    const results = await service.searchPlaces('test');
    expect(results).toEqual([]);
  });

  it('returns empty array on fetch error', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new Error('Network error'));

    const results = await service.searchPlaces('test');
    expect(results).toEqual([]);
  });
});

// ── Suite: retrievePlace ─────────────────────────────────────────────────────

describe('LocationService – retrievePlace', () => {
  let service: LocationService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [LocationService] });
    service = TestBed.inject(LocationService);
    (service as any).token = 'test-token';
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('calls the Search Box retrieve endpoint with mapbox_id', async () => {
    mockFetchJson(fakeRetrieveResponse());

    await service.retrievePlace('abc123');

    const url = (fetch as any).mock.calls[0][0] as string;
    expect(url).toContain('search/searchbox/v1/retrieve/abc123');
  });

  it('returns Place with name, address, lat, lng', async () => {
    mockFetchJson(fakeRetrieveResponse({
      name: 'To Rom og Kjøkken',
      full_address: 'Carl Johans gate 5, 7010 Trondheim, Norway',
      coordinates: [10.395, 63.433],
      poi_category: ['restaurant'],
    }));

    const place = await service.retrievePlace('poi123');

    expect(place).not.toBeNull();
    expect(place!.name).toBe('To Rom og Kjøkken');
    expect(place!.address).toBe('Carl Johans gate 5, 7010 Trondheim, Norway');
    expect(place!.lat).toBe(63.433);
    expect(place!.lng).toBe(10.395);
    expect(place!.category).toBe('restaurant');
  });

  it('rotates session token after retrieve', async () => {
    mockFetchJson(fakeRetrieveResponse());
    const oldToken = (service as any).sessionToken;

    await service.retrievePlace('abc123');

    expect((service as any).sessionToken).not.toBe(oldToken);
  });

  it('returns null when no features are found', async () => {
    mockFetchJson({ type: 'FeatureCollection', features: [] });
    const result = await service.retrievePlace('missing');
    expect(result).toBeNull();
  });

  it('returns null when token is missing', async () => {
    (service as any).token = '';
    const result = await service.retrievePlace('abc123');
    expect(result).toBeNull();
  });

  it('returns null when mapboxId is empty', async () => {
    const result = await service.retrievePlace('');
    expect(result).toBeNull();
  });

  it('returns null on fetch error', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new Error('Network error'));
    const result = await service.retrievePlace('abc123');
    expect(result).toBeNull();
  });
});

// ── Suite: reverseGeocode ────────────────────────────────────────────────────

describe('LocationService – reverseGeocode', () => {
  let service: LocationService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [LocationService] });
    service = TestBed.inject(LocationService);
    (service as any).token = 'test-token';
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
    (service as any).token = '';
    const result = await service.reverseGeocode(59.91, 10.75);
    expect(result).toBeNull();
  });

  it('returns null on fetch error', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new Error('Network error'));
    const result = await service.reverseGeocode(59.91, 10.75);
    expect(result).toBeNull();
  });
});
