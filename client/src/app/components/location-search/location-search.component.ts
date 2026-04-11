import {
  Component,
  OnDestroy,
  OnInit,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LocationService, Place } from '../../services/location.service';

export interface LocationSelection {
  name: string;
  lat: number;
  lng: number;
  type: string;
}

@Component({
  selector: 'app-location-search',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './location-search.component.html',
})
export class LocationSearchComponent implements OnInit, OnDestroy {
  private readonly locationService = inject(LocationService);

  /** If true, show the "Hjemme" quick-pick button */
  readonly homeAddress = input<{ name: string; lat: number; lng: number } | null>(null);

  /** Hide the location type chip selector (e.g. when picking a home address) */
  readonly hideTypeSelector = input(false);

  /** Pre-select a location type (e.g. when re-editing a saved location) */
  readonly initialType = input<string | null>(null);

  readonly locationSelected = output<LocationSelection>();
  readonly typeChanged = output<string>();

  protected readonly query = signal('');
  protected readonly results = signal<Place[]>([]);
  protected readonly searching = signal(false);
  protected readonly locating = signal(false);
  protected readonly locationType = signal('restaurant');
  protected readonly permission = this.locationService.permissionState;

  protected readonly locationTypes = [
    { value: 'restaurant', label: 'Restaurant', icon: '🍷' },
    { value: 'butikk', label: 'Butikk', icon: '🛒' },
    { value: 'hjemme', label: 'Hjemme', icon: '🏠' },
    { value: 'annet', label: 'Annet', icon: '📍' },
  ];

  private debounceTimer: ReturnType<typeof setTimeout> | null = null;
  private userProximity: { lat: number; lng: number } | null = null;

  ngOnInit(): void {
    const type = this.initialType();
    if (type) this.locationType.set(type);
  }

  protected onQueryChange(value: string): void {
    this.query.set(value);
    if (this.debounceTimer) clearTimeout(this.debounceTimer);

    if (!value.trim()) {
      this.results.set([]);
      return;
    }

    this.debounceTimer = setTimeout(() => this.search(value), 300);
  }

  private async search(query: string): Promise<void> {
    this.searching.set(true);
    try {
      const places = await this.locationService.searchPlaces(query, this.userProximity ?? undefined);
      this.results.set(places);
    } finally {
      this.searching.set(false);
    }
  }

  protected selectPlace(place: Place): void {
    this.query.set(place.name);
    this.results.set([]);
    this.locationSelected.emit({
      name: place.name,
      lat: place.lat,
      lng: place.lng,
      type: this.locationType(),
    });
  }

  protected async useCurrentLocation(): Promise<void> {
    this.locating.set(true);
    try {
      const pos = await this.locationService.getCurrentPosition();
      this.userProximity = pos;
      const geo = await this.locationService.reverseGeocode(pos.lat, pos.lng);
      const name = geo?.name ?? `${pos.lat.toFixed(4)}, ${pos.lng.toFixed(4)}`;
      this.query.set(name);
      this.results.set([]);
      this.locationSelected.emit({
        name,
        lat: pos.lat,
        lng: pos.lng,
        type: this.locationType(),
      });
    } catch {
      // Permission denied or error — handled gracefully
    } finally {
      this.locating.set(false);
    }
  }

  protected useHomeAddress(): void {
    const home = this.homeAddress();
    if (!home) return;
    this.query.set(home.name);
    this.results.set([]);
    this.locationType.set('hjemme');
    this.locationSelected.emit({
      name: home.name,
      lat: home.lat,
      lng: home.lng,
      type: 'hjemme',
    });
  }

  protected onTypeChange(type: string): void {
    this.locationType.set(type);
    this.typeChanged.emit(type);
  }

  ngOnDestroy(): void {
    if (this.debounceTimer) clearTimeout(this.debounceTimer);
  }
}
