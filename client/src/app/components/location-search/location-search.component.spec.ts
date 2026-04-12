import { Injector, runInInjectionContext, signal } from '@angular/core';
import { LocationSearchComponent, LocationSelection } from './location-search.component';
import { LocationService } from '../../services/location.service';

// ── Mocks ────────────────────────────────────────────────────────────────────

const createMockLocationService = () => ({
  permissionState: signal<'prompt' | 'granted' | 'denied'>('prompt'),
  searchPlaces: vi.fn().mockResolvedValue([]),
  retrievePlace: vi.fn().mockResolvedValue(null),
  getCurrentPosition: vi.fn(),
  reverseGeocode: vi.fn(),
});

/**
 * Create a LocationSearchComponent instance inside an injection context.
 * We bypass template compilation entirely since we only test class logic.
 */
function createComponent(locationService: ReturnType<typeof createMockLocationService>) {
  const injector = Injector.create({
    providers: [
      { provide: LocationService, useValue: locationService },
    ],
  });

  return runInInjectionContext(injector, () => {
    const comp = new (LocationSearchComponent as any)();
    return comp as LocationSearchComponent;
  });
}

// ── Suite ────────────────────────────────────────────────────────────────────

describe('LocationSearchComponent', () => {
  let component: LocationSearchComponent;
  let mockService: ReturnType<typeof createMockLocationService>;

  beforeEach(() => {
    mockService = createMockLocationService();
    component = createComponent(mockService);
  });

  afterEach(() => vi.restoreAllMocks());

  // ── Location types ──────────────────────────────────────────────────────

  it('does not include "butikk" in location types', () => {
    const values = (component as any).locationTypes.map((t: any) => t.value);
    expect(values).not.toContain('butikk');
  });

  it('includes "bar" in location types', () => {
    const values = (component as any).locationTypes.map((t: any) => t.value);
    expect(values).toContain('bar');
  });

  it('includes restaurant, bar, hjemme, and annet as location types', () => {
    const values = (component as any).locationTypes.map((t: any) => t.value);
    expect(values).toEqual(['restaurant', 'bar', 'hjemme', 'annet']);
  });

  it('defaults to "restaurant" location type', () => {
    expect((component as any).locationType()).toBe('restaurant');
  });

  // ── selectPlace ─────────────────────────────────────────────────────────

  it('calls retrievePlace and emits location when a suggestion is selected', async () => {
    mockService.retrievePlace.mockResolvedValue({
      name: 'Himkok',
      address: 'Storgata 27, Oslo',
      lat: 59.916,
      lng: 10.749,
      category: 'bar',
    });

    const emitted: LocationSelection[] = [];
    component.locationSelected.subscribe(e => emitted.push(e));

    (component as any).onTypeChange('bar');

    await (component as any).selectPlace({
      place_id: 'poi456',
      name: 'Himkok',
      address: 'Storgata 27, Oslo',
    });

    expect(mockService.retrievePlace).toHaveBeenCalledWith('poi456');
    expect(emitted).toHaveLength(1);
    expect(emitted[0].type).toBe('bar');
    expect(emitted[0].name).toBe('Himkok');
    expect(emitted[0].lat).toBe(59.916);
    expect(emitted[0].lng).toBe(10.749);
  });

  it('does not emit when retrievePlace returns null', async () => {
    mockService.retrievePlace.mockResolvedValue(null);

    const emitted: LocationSelection[] = [];
    component.locationSelected.subscribe(e => emitted.push(e));

    await (component as any).selectPlace({
      place_id: 'missing',
      name: 'Unknown',
      address: '',
    });

    expect(emitted).toHaveLength(0);
  });

  // ── onTypeChange ────────────────────────────────────────────────────────

  it('updates locationType and emits typeChanged on type change', () => {
    const emitted: string[] = [];
    component.typeChanged.subscribe(t => emitted.push(t));

    (component as any).onTypeChange('annet');

    expect((component as any).locationType()).toBe('annet');
    expect(emitted).toEqual(['annet']);
  });

  it('auto-fills home address when "hjemme" chip is clicked and home address is registered', () => {
    (component as any).homeAddress = signal({ name: 'Hjemmeveien 1', lat: 59.91, lng: 10.75 });

    const emitted: LocationSelection[] = [];
    component.locationSelected.subscribe(e => emitted.push(e));

    (component as any).onTypeChange('hjemme');

    expect(emitted).toHaveLength(1);
    expect(emitted[0].type).toBe('hjemme');
    expect(emitted[0].name).toBe('Hjemmeveien 1');
    expect(emitted[0].lat).toBe(59.91);
    expect(emitted[0].lng).toBe(10.75);
  });

  it('only sets type when "hjemme" chip is clicked without a registered home address', () => {
    const emitted: LocationSelection[] = [];
    component.locationSelected.subscribe(e => emitted.push(e));
    const types: string[] = [];
    component.typeChanged.subscribe(t => types.push(t));

    (component as any).onTypeChange('hjemme');

    expect(emitted).toHaveLength(0);
    expect(types).toEqual(['hjemme']);
    expect((component as any).locationType()).toBe('hjemme');
  });

  it('pre-fills query from initialQuery input when ngOnInit runs', () => {
    (component as any).initialQuery = signal('Himkok Bar');
    (component as any).ngOnInit();
    expect((component as any).query()).toBe('Himkok Bar');
  });

  it('leaves query empty when initialQuery is null', () => {
    (component as any).ngOnInit();
    expect((component as any).query()).toBe('');
  });

  // ── useHomeAddress ──────────────────────────────────────────────────────

  it('sets locationType to "hjemme" when home address is used', () => {
    // Override the input signal with a concrete value
    (component as any).homeAddress = signal({
      name: 'Hjemmeveien 1',
      lat: 59.91,
      lng: 10.75,
    });

    const emitted: LocationSelection[] = [];
    component.locationSelected.subscribe(e => emitted.push(e));

    (component as any).useHomeAddress();

    expect((component as any).locationType()).toBe('hjemme');
    expect(emitted).toHaveLength(1);
    expect(emitted[0].type).toBe('hjemme');
    expect(emitted[0].name).toBe('Hjemmeveien 1');
    expect(emitted[0].lat).toBe(59.91);
    expect(emitted[0].lng).toBe(10.75);
  });

  it('does nothing when useHomeAddress is called without a home address', () => {
    const emitted: LocationSelection[] = [];
    component.locationSelected.subscribe(e => emitted.push(e));

    (component as any).useHomeAddress();

    expect(emitted).toHaveLength(0);
    // locationType should remain as default
    expect((component as any).locationType()).toBe('restaurant');
  });

  it('sets query to home address name when home address is used', () => {
    (component as any).homeAddress = signal({
      name: 'Hjemmeveien 1',
      lat: 59.91,
      lng: 10.75,
    });

    (component as any).useHomeAddress();

    expect((component as any).query()).toBe('Hjemmeveien 1');
  });

  it('clears results when home address is used', () => {
    (component as any).results.set([{ place_id: 'x', name: 'Old result', address: '' }]);
    (component as any).homeAddress = signal({
      name: 'Test',
      lat: 0,
      lng: 0,
    });

    (component as any).useHomeAddress();

    expect((component as any).results()).toEqual([]);
  });
});
