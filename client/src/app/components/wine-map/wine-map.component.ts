import {
  Component,
  ElementRef,
  OnDestroy,
  afterNextRender,
  effect,
  inject,
  input,
  viewChild,
} from '@angular/core';
import mapboxgl from 'mapbox-gl';
import { environment } from '../../../environments/environment';
import type { Wine } from '../../services/wine.service';

@Component({
  selector: 'app-wine-map',
  standalone: true,
  templateUrl: './wine-map.component.html',
})
export class WineMapComponent implements OnDestroy {
  private readonly mapContainerRef = viewChild<ElementRef<HTMLDivElement>>('mapContainer');

  readonly wines = input.required<Wine[]>();

  private map: mapboxgl.Map | null = null;
  private popup: mapboxgl.Popup | null = null;

  constructor() {
    afterNextRender(() => this.initMap());

    // Update GeoJSON source whenever wines input changes
    effect(() => {
      const wines = this.wines();
      this.updateSource(wines);
    });
  }

  private initMap(): void {
    const container = this.mapContainerRef()?.nativeElement;
    if (!container || !environment.mapboxToken) return;

    mapboxgl.accessToken = environment.mapboxToken;

    this.map = new mapboxgl.Map({
      container,
      style: 'mapbox://styles/mapbox/dark-v11',
      center: [10.75, 59.91], // Oslo default
      zoom: 4,
      attributionControl: false,
    });

    this.map.addControl(new mapboxgl.NavigationControl(), 'top-right');

    this.map.on('load', () => {
      this.addSources();
      this.addLayers();
      this.addInteractions();
      this.updateSource(this.wines());
    });
  }

  private addSources(): void {
    this.map!.addSource('wines', {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] },
      cluster: true,
      clusterMaxZoom: 14,
      clusterRadius: 50,
    });
  }

  private addLayers(): void {
    // Cluster circles
    this.map!.addLayer({
      id: 'clusters',
      type: 'circle',
      source: 'wines',
      filter: ['has', 'point_count'],
      paint: {
        'circle-color': '#800020',
        'circle-radius': ['step', ['get', 'point_count'], 20, 10, 30, 50, 40],
        'circle-stroke-width': 2,
        'circle-stroke-color': '#D4AF37',
        'circle-opacity': 0.85,
      },
    });

    // Cluster count labels
    this.map!.addLayer({
      id: 'cluster-count',
      type: 'symbol',
      source: 'wines',
      filter: ['has', 'point_count'],
      layout: {
        'text-field': '{point_count_abbreviated}',
        'text-font': ['DIN Offc Pro Medium', 'Arial Unicode MS Bold'],
        'text-size': 14,
      },
      paint: {
        'text-color': '#F5F5F5',
      },
    });

    // Individual wine pins
    this.map!.addLayer({
      id: 'wine-points',
      type: 'circle',
      source: 'wines',
      filter: ['!', ['has', 'point_count']],
      paint: {
        'circle-color': [
          'match', ['get', 'wineType'],
          'Rød', '#991b1b',
          'Hvit', '#a16207',
          'Rosé', '#9d174d',
          'Musserende', '#92400e',
          'Oransje', '#c2410c',
          'Dessert', '#7e22ce',
          '#800020', // default
        ],
        'circle-radius': 8,
        'circle-stroke-width': 2,
        'circle-stroke-color': '#D4AF37',
      },
    });
  }

  private addInteractions(): void {
    const map = this.map!;

    // Click cluster → zoom in
    map.on('click', 'clusters', (e) => {
      const features = map.queryRenderedFeatures(e.point, { layers: ['clusters'] });
      const clusterId = features[0]?.properties?.['cluster_id'];
      if (clusterId == null) return;

      (map.getSource('wines') as mapboxgl.GeoJSONSource).getClusterExpansionZoom(clusterId, (err, zoom) => {
        if (err || zoom == null) return;
        const coords = (features[0].geometry as GeoJSON.Point).coordinates as [number, number];
        map.easeTo({ center: coords, zoom });
      });
    });

    // Click wine pin → show popup
    map.on('click', 'wine-points', (e) => {
      const feature = e.features?.[0];
      if (!feature) return;
      const coords = (feature.geometry as GeoJSON.Point).coordinates.slice() as [number, number];
      const props = feature.properties!;

      this.popup?.remove();

      const imageHtml = props['imageUrl']
        ? `<img src="${props['imageUrl']}" class="w-full h-24 object-cover rounded-t-lg" />`
        : `<div class="w-full h-24 bg-burgundy/20 rounded-t-lg flex items-center justify-center text-3xl">🍷</div>`;

      const ratingHtml = props['rating']
        ? `<span class="text-gold font-bold">${props['rating']}</span><span class="text-cream-dark text-[10px]"> / 6</span>`
        : '';

      this.popup = new mapboxgl.Popup({ offset: 15, maxWidth: '220px', closeButton: true })
        .setLngLat(coords)
        .setHTML(`
          <div style="font-family: 'Inter', system-ui, sans-serif;">
            ${imageHtml}
            <div class="p-3">
              <p class="font-semibold text-sm text-cream leading-tight">${props['name']}</p>
              <p class="text-xs text-cream-dark mt-0.5">${props['producer']}</p>
              <div class="flex items-center justify-between mt-2">
                <span class="text-[10px] text-cream-dark">${props['locationName'] ?? ''}</span>
                ${ratingHtml}
              </div>
            </div>
          </div>
        `)
        .addTo(map);
    });

    // Cursor styles
    map.on('mouseenter', 'clusters', () => { map.getCanvas().style.cursor = 'pointer'; });
    map.on('mouseleave', 'clusters', () => { map.getCanvas().style.cursor = ''; });
    map.on('mouseenter', 'wine-points', () => { map.getCanvas().style.cursor = 'pointer'; });
    map.on('mouseleave', 'wine-points', () => { map.getCanvas().style.cursor = ''; });
  }

  private updateSource(wines: Wine[]): void {
    if (!this.map?.isStyleLoaded()) return;

    const source = this.map.getSource('wines') as mapboxgl.GeoJSONSource | undefined;
    if (!source) return;

    const features: GeoJSON.Feature[] = wines
      .filter(w => w.location_lat != null && w.location_lng != null)
      .map(w => ({
        type: 'Feature' as const,
        geometry: {
          type: 'Point' as const,
          coordinates: [w.location_lng!, w.location_lat!],
        },
        properties: {
          id: w.id,
          name: w.name,
          producer: w.producer,
          wineType: w.type,
          rating: w.rating,
          imageUrl: w.image_url,
          locationName: w.location_name,
        },
      }));

    source.setData({ type: 'FeatureCollection', features });

    // Fit bounds if there are wines with locations
    if (features.length > 0) {
      const bounds = new mapboxgl.LngLatBounds();
      features.forEach(f => {
        const coords = (f.geometry as GeoJSON.Point).coordinates as [number, number];
        bounds.extend(coords);
      });
      this.map.fitBounds(bounds, { padding: 60, maxZoom: 14 });
    }
  }

  ngOnDestroy(): void {
    this.popup?.remove();
    this.map?.remove();
  }
}
