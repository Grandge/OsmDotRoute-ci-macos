import maplibregl, { type LngLatBoundsLike, type MapMouseEvent } from 'maplibre-gl';
import { useEffect, useImperativeHandle, useRef, forwardRef } from 'react';
import 'maplibre-gl/dist/maplibre-gl.css';

export interface MapViewHandle {
  fitBounds(bounds: LngLatBoundsLike, padding?: number): void;
  setRoadNetwork(geojson: GeoJSON.FeatureCollection | null): void;
  setMeshGrid(geojson: GeoJSON.FeatureCollection | null): void;
  setRestrictions(geojson: GeoJSON.FeatureCollection | null): void;
  setRoute(line: GeoJSON.LineString | null): void;
  setRouteEndpoints(points: { from?: [number, number]; to?: [number, number] }): void;
  setPolygonDraft(vertices: [number, number][]): void;
  setBboxRect(bounds: [number, number, number, number] | null): void;
  startBboxDraw(): void;
  cancelBboxDraw(): void;
  getMap(): maplibregl.Map | null;
}

interface Props {
  initialCenter?: [number, number];
  initialZoom?: number;
  onBoundsChange?: (sw: [number, number], ne: [number, number]) => void;
  onMapClick?: (lngLat: { lng: number; lat: number }, feature: maplibregl.MapGeoJSONFeature | null) => void;
  onBboxDrawn?: (bbox: [number, number, number, number]) => void;
}

const ROAD_SOURCE = 'road-network';
const ROAD_LAYER = 'road-network-line';
const MESH_SOURCE = 'mesh-grid';
const MESH_LAYER_FILL = 'mesh-grid-fill';
const MESH_LAYER_LINE = 'mesh-grid-line';
const REST_SOURCE = 'restrictions';
const REST_LAYER_FILL = 'restrictions-fill';
const REST_LAYER_LINE = 'restrictions-line';
const ROUTE_SOURCE = 'route';
const ROUTE_LAYER = 'route-line';
const ENDPT_SOURCE = 'route-endpoints';
const ENDPT_LAYER = 'route-endpoints-circle';
const DRAFT_SOURCE = 'polygon-draft';
const DRAFT_LAYER_LINE = 'polygon-draft-line';
const DRAFT_LAYER_PT = 'polygon-draft-points';
const BBOX_SOURCE = 'bbox-rect';
const BBOX_LAYER_FILL = 'bbox-rect-fill';
const BBOX_LAYER_LINE = 'bbox-rect-line';

function createCornerElement(): HTMLElement {
  const el = document.createElement('div');
  el.style.width = '12px';
  el.style.height = '12px';
  el.style.borderRadius = '50%';
  el.style.background = '#2563eb';
  el.style.border = '2px solid white';
  el.style.cursor = 'move';
  el.style.boxShadow = '0 1px 3px rgba(0,0,0,0.3)';
  return el;
}

export const MapView = forwardRef<MapViewHandle, Props>(function MapView(
  { initialCenter = [137.0, 35.15], initialZoom = 6, onBoundsChange, onMapClick, onBboxDrawn },
  ref,
) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<maplibregl.Map | null>(null);
  const onMapClickRef = useRef(onMapClick);
  onMapClickRef.current = onMapClick;
  const onBboxDrawnRef = useRef(onBboxDrawn);
  onBboxDrawnRef.current = onBboxDrawn;
  const bboxDrawRef = useRef<{ active: boolean; firstCorner: [number, number] | null }>({
    active: false,
    firstCorner: null,
  });
  // [NW, NE, SE, SW]
  const bboxMarkersRef = useRef<maplibregl.Marker[]>([]);
  const isDraggingMarkerRef = useRef(false);

  useEffect(() => {
    if (!containerRef.current || mapRef.current) return;
    const map = new maplibregl.Map({
      container: containerRef.current,
      style: {
        version: 8,
        sources: {
          osm: {
            type: 'raster',
            tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
            tileSize: 256,
            attribution: '&copy; OpenStreetMap contributors',
          },
        },
        layers: [{ id: 'osm', type: 'raster', source: 'osm' }],
      },
      center: initialCenter,
      zoom: initialZoom,
    });
    mapRef.current = map;

    const emitBounds = () => {
      const b = map.getBounds();
      onBoundsChange?.([b.getSouth(), b.getWest()], [b.getNorth(), b.getEast()]);
    };
    map.on('load', emitBounds);
    map.on('moveend', emitBounds);

    map.on('mousemove', (e: MapMouseEvent) => {
      const state = bboxDrawRef.current;
      if (!state.active || !state.firstCorner) return;
      const [lng1, lat1] = state.firstCorner;
      updateBboxSource(map, lng1, lat1, e.lngLat.lng, e.lngLat.lat);
    });

    map.on('click', (e: MapMouseEvent) => {
      if (isDraggingMarkerRef.current) return;

      const state = bboxDrawRef.current;
      if (state.active) {
        if (!state.firstCorner) {
          state.firstCorner = [e.lngLat.lng, e.lngLat.lat];
        } else {
          const [lng1, lat1] = state.firstCorner;
          const west = Math.min(lng1, e.lngLat.lng);
          const south = Math.min(lat1, e.lngLat.lat);
          const east = Math.max(lng1, e.lngLat.lng);
          const north = Math.max(lat1, e.lngLat.lat);
          updateBboxSource(map, west, south, east, north);
          syncBboxMarkers(map, west, south, east, north);
          state.active = false;
          state.firstCorner = null;
          map.getCanvas().style.cursor = '';
          onBboxDrawnRef.current?.([west, south, east, north]);
        }
        return;
      }

      const layerIds = [MESH_LAYER_FILL, REST_LAYER_FILL].filter((id) => map.getLayer(id));
      const features = layerIds.length > 0 ? map.queryRenderedFeatures(e.point, { layers: layerIds }) : [];
      onMapClickRef.current?.(e.lngLat, features[0] ?? null);
    });

    return () => {
      removeBboxMarkers();
      map.remove();
      mapRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function removeBboxMarkers() {
    bboxMarkersRef.current.forEach((m) => m.remove());
    bboxMarkersRef.current = [];
  }

  function syncBboxMarkers(map: maplibregl.Map, west: number, south: number, east: number, north: number) {
    const corners: [number, number][] = [
      [west, north],  // NW - index 0
      [east, north],  // NE - index 1
      [east, south],  // SE - index 2
      [west, south],  // SW - index 3
    ];

    if (bboxMarkersRef.current.length === 4) {
      bboxMarkersRef.current.forEach((m, i) => m.setLngLat(corners[i]));
      return;
    }

    removeBboxMarkers();
    const markers = corners.map((pos, idx) => {
      const marker = new maplibregl.Marker({ element: createCornerElement(), draggable: true })
        .setLngLat(pos)
        .addTo(map);

      marker.on('dragstart', () => {
        isDraggingMarkerRef.current = true;
      });

      marker.on('drag', () => {
        handleCornerDrag(idx);
      });

      marker.on('dragend', () => {
        handleCornerDrag(idx);
        setTimeout(() => { isDraggingMarkerRef.current = false; }, 50);
        const bbox = getBboxFromMarkers();
        if (bbox) onBboxDrawnRef.current?.(bbox);
      });

      return marker;
    });
    bboxMarkersRef.current = markers;
  }

  function handleCornerDrag(cornerIndex: number) {
    const markers = bboxMarkersRef.current;
    const m = mapRef.current;
    if (markers.length !== 4 || !m) return;

    const dragged = markers[cornerIndex].getLngLat();
    let west: number, south: number, east: number, north: number;

    switch (cornerIndex) {
      case 0: // NW
        west = dragged.lng; north = dragged.lat;
        east = markers[1].getLngLat().lng; south = markers[2].getLngLat().lat;
        break;
      case 1: // NE
        east = dragged.lng; north = dragged.lat;
        west = markers[0].getLngLat().lng; south = markers[3].getLngLat().lat;
        break;
      case 2: // SE
        east = dragged.lng; south = dragged.lat;
        west = markers[3].getLngLat().lng; north = markers[0].getLngLat().lat;
        break;
      case 3: // SW
        west = dragged.lng; south = dragged.lat;
        east = markers[2].getLngLat().lng; north = markers[1].getLngLat().lat;
        break;
      default: return;
    }

    const newPositions: [number, number][] = [
      [west, north], [east, north], [east, south], [west, south],
    ];
    for (let i = 0; i < 4; i++) {
      if (i !== cornerIndex) markers[i].setLngLat(newPositions[i]);
    }

    updateBboxSource(m, west, south, east, north);
  }

  function getBboxFromMarkers(): [number, number, number, number] | null {
    const markers = bboxMarkersRef.current;
    if (markers.length !== 4) return null;
    const nw = markers[0].getLngLat();
    const se = markers[2].getLngLat();
    const west = Math.min(nw.lng, se.lng);
    const south = Math.min(nw.lat, se.lat);
    const east = Math.max(nw.lng, se.lng);
    const north = Math.max(nw.lat, se.lat);
    return [west, south, east, north];
  }

  useImperativeHandle(ref, () => ({
    fitBounds(bounds, padding = 32) {
      mapRef.current?.fitBounds(bounds, { padding, animate: false });
    },
    setRoadNetwork(geojson) {
      runWhenStyleReady((m) => {
        if (!geojson) {
          if (m.getLayer(ROAD_LAYER)) m.setLayoutProperty(ROAD_LAYER, 'visibility', 'none');
          return;
        }
        const existing = m.getSource(ROAD_SOURCE) as maplibregl.GeoJSONSource | undefined;
        if (existing) {
          existing.setData(geojson);
          if (m.getLayer(ROAD_LAYER)) m.setLayoutProperty(ROAD_LAYER, 'visibility', 'visible');
          return;
        }
        m.addSource(ROAD_SOURCE, { type: 'geojson', data: geojson });
        m.addLayer({
          id: ROAD_LAYER,
          type: 'line',
          source: ROAD_SOURCE,
          paint: { 'line-color': '#1e6fff', 'line-width': 1.2, 'line-opacity': 0.55 },
        });
      });
    },
    setMeshGrid(geojson) {
      runWhenStyleReady((m) => updateGeoJsonSource(m, MESH_SOURCE, geojson, () => {
        m.addLayer({
          id: MESH_LAYER_FILL,
          type: 'fill',
          source: MESH_SOURCE,
          paint: { 'fill-color': '#9ca3af', 'fill-opacity': 0.06 },
        });
        m.addLayer({
          id: MESH_LAYER_LINE,
          type: 'line',
          source: MESH_SOURCE,
          paint: { 'line-color': '#4b5563', 'line-width': 0.6, 'line-opacity': 0.7 },
        });
      }, [MESH_LAYER_LINE, MESH_LAYER_FILL]));
    },
    setRestrictions(geojson) {
      runWhenStyleReady((m) => updateGeoJsonSource(m, REST_SOURCE, geojson, () => {
        m.addLayer({
          id: REST_LAYER_FILL,
          type: 'fill',
          source: REST_SOURCE,
          paint: {
            'fill-color': [
              'match', ['get', 'kind'],
              'block', '#dc2626',
              'difficulty', '#f59e0b',
              '#6b7280',
            ],
            'fill-opacity': 0.35,
          },
        });
        m.addLayer({
          id: REST_LAYER_LINE,
          type: 'line',
          source: REST_SOURCE,
          paint: {
            'line-color': [
              'match', ['get', 'kind'],
              'block', '#7f1d1d',
              'difficulty', '#92400e',
              '#374151',
            ],
            'line-width': 1.4,
          },
        });
      }, [REST_LAYER_LINE, REST_LAYER_FILL]));
    },
    setRoute(line) {
      const fc: GeoJSON.FeatureCollection | null = line
        ? { type: 'FeatureCollection', features: [{ type: 'Feature', properties: {}, geometry: line }] }
        : null;
      runWhenStyleReady((m) => updateGeoJsonSource(m, ROUTE_SOURCE, fc, () => {
        m.addLayer({
          id: ROUTE_LAYER,
          type: 'line',
          source: ROUTE_SOURCE,
          paint: { 'line-color': '#16a34a', 'line-width': 4, 'line-opacity': 0.9 },
        });
      }, [ROUTE_LAYER]));
    },
    setRouteEndpoints(points) {
      const feats: GeoJSON.Feature[] = [];
      if (points.from) feats.push(makePoint(points.from, 'from'));
      if (points.to) feats.push(makePoint(points.to, 'to'));
      const fc: GeoJSON.FeatureCollection = { type: 'FeatureCollection', features: feats };
      runWhenStyleReady((m) => updateGeoJsonSource(m, ENDPT_SOURCE, fc, () => {
        m.addLayer({
          id: ENDPT_LAYER,
          type: 'circle',
          source: ENDPT_SOURCE,
          paint: {
            'circle-radius': 8,
            'circle-color': ['match', ['get', 'role'], 'from', '#059669', 'to', '#dc2626', '#6b7280'],
            'circle-stroke-color': '#fff',
            'circle-stroke-width': 2,
          },
        });
      }, [ENDPT_LAYER]));
    },
    setPolygonDraft(vertices) {
      const lineFC: GeoJSON.FeatureCollection = {
        type: 'FeatureCollection',
        features: vertices.length >= 2
          ? [{ type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: vertices } }]
          : [],
      };
      const pointFC: GeoJSON.FeatureCollection = {
        type: 'FeatureCollection',
        features: vertices.map((v) => ({ type: 'Feature', properties: {}, geometry: { type: 'Point', coordinates: v } })),
      };
      runWhenStyleReady((m) => {
        updateGeoJsonSource(m, DRAFT_SOURCE, lineFC, () => {
          m.addLayer({
            id: DRAFT_LAYER_LINE,
            type: 'line',
            source: DRAFT_SOURCE,
            paint: { 'line-color': '#7c3aed', 'line-width': 2, 'line-dasharray': [2, 2] },
          });
        }, [DRAFT_LAYER_LINE]);
        updateGeoJsonSource(m, DRAFT_SOURCE + '-pt', pointFC, () => {
          m.addLayer({
            id: DRAFT_LAYER_PT,
            type: 'circle',
            source: DRAFT_SOURCE + '-pt',
            paint: { 'circle-radius': 5, 'circle-color': '#7c3aed', 'circle-stroke-color': '#fff', 'circle-stroke-width': 2 },
          });
        }, [DRAFT_LAYER_PT]);
      });
    },
    setBboxRect(bounds) {
      const m = mapRef.current;
      if (!m) return;
      if (!bounds) {
        runWhenStyleReady((m2) => updateGeoJsonSource(m2, BBOX_SOURCE, null, () => {}, [BBOX_LAYER_LINE, BBOX_LAYER_FILL]));
        removeBboxMarkers();
        return;
      }
      const [west, south, east, north] = bounds;
      runWhenStyleReady((m2) => {
        updateBboxSource(m2, west, south, east, north);
        syncBboxMarkers(m2, west, south, east, north);
      });
    },
    startBboxDraw() {
      removeBboxMarkers();
      const m = mapRef.current;
      if (m) {
        updateGeoJsonSource(m, BBOX_SOURCE, null, () => {}, [BBOX_LAYER_LINE, BBOX_LAYER_FILL]);
      }
      bboxDrawRef.current = { active: true, firstCorner: null };
      if (m) m.getCanvas().style.cursor = 'crosshair';
    },
    cancelBboxDraw() {
      bboxDrawRef.current = { active: false, firstCorner: null };
      const m = mapRef.current;
      if (m) m.getCanvas().style.cursor = '';
    },
    getMap() {
      return mapRef.current;
    },
  }));

  return <div ref={containerRef} style={{ width: '100%', height: '100%' }} />;

  function runWhenStyleReady(fn: (m: maplibregl.Map) => void) {
    const m = mapRef.current;
    if (!m) return;
    if (m.isStyleLoaded()) {
      fn(m);
      return;
    }
    // 'load' は初回のみ。isStyleLoaded() が false の場合は 'style.load' を待つ
    // （load 発火後も isStyleLoaded() が false を返すケースへの対策）
    m.once('style.load', () => {
      if (mapRef.current === m) fn(m);
    });
  }
});

function updateBboxSource(map: maplibregl.Map, west: number, south: number, east: number, north: number) {
  const ring = [[west, south], [east, south], [east, north], [west, north], [west, south]];
  const fc: GeoJSON.FeatureCollection = {
    type: 'FeatureCollection',
    features: [{ type: 'Feature', properties: {}, geometry: { type: 'Polygon', coordinates: [ring] } }],
  };
  updateGeoJsonSource(map, BBOX_SOURCE, fc, () => {
    map.addLayer({
      id: BBOX_LAYER_FILL,
      type: 'fill',
      source: BBOX_SOURCE,
      paint: { 'fill-color': '#3b82f6', 'fill-opacity': 0.1 },
    });
    map.addLayer({
      id: BBOX_LAYER_LINE,
      type: 'line',
      source: BBOX_SOURCE,
      paint: { 'line-color': '#2563eb', 'line-width': 2, 'line-dasharray': [4, 3] },
    });
  }, [BBOX_LAYER_LINE, BBOX_LAYER_FILL]);
}

function updateGeoJsonSource(
  map: maplibregl.Map,
  sourceId: string,
  data: GeoJSON.FeatureCollection | null,
  addLayers: () => void,
  layerIds: string[],
) {
  const existing = map.getSource(sourceId) as maplibregl.GeoJSONSource | undefined;
  const empty = data === null || (data.type === 'FeatureCollection' && data.features.length === 0);
  if (empty) {
    for (const id of layerIds) {
      if (map.getLayer(id)) map.removeLayer(id);
    }
    if (existing) map.removeSource(sourceId);
    return;
  }
  if (existing) {
    existing.setData(data);
    return;
  }
  map.addSource(sourceId, { type: 'geojson', data });
  addLayers();
}

function makePoint(latlng: [number, number], role: 'from' | 'to'): GeoJSON.Feature {
  return {
    type: 'Feature',
    properties: { role },
    geometry: { type: 'Point', coordinates: [latlng[1], latlng[0]] },
  };
}
