window.railMap = (() => {
  let map;
  const markers = new Map();
  const palette = t => t.delaySeconds == null ? '#7a8c9e' : t.delaySeconds > 900 ? '#bc6262' : t.delaySeconds > 300 ? '#c69c57' : '#4c907a';
  function init() {
    if (map) return;
    map = L.map('rail-map', { zoomControl: false, minZoom: 5, maxZoom: 16 }).setView([40.1, -3.6], 6);
    L.control.zoom({ position: 'bottomright' }).addTo(map);
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>', maxZoom: 19 }).addTo(map);
    new ResizeObserver(() => map.invalidateSize()).observe(document.getElementById('rail-map'));
  }
  function update(trains, mode) {
    if (!map) return;
    const ids = new Set(trains.map(t => t.id));
    for (const [id, marker] of markers) if (!ids.has(id)) { map.removeLayer(marker); markers.delete(id); }
    for (const t of trains) {
      let marker = markers.get(t.id);
      if (!marker) { marker = L.circleMarker([t.latitude, t.longitude], { radius: 5, weight: 1.5, color: 'white', fillOpacity: .95 }).addTo(map); markers.set(t.id, marker); }
      marker.setLatLng([t.latitude, t.longitude]); marker.setStyle({ fillColor: palette(t) });
      const popup = document.createElement('div'); popup.className = 'train-popup';
      const title = document.createElement('strong'); title.textContent = `${t.line} · Tren ${t.id}`; popup.append(title);
      const stop = document.createElement('p'); stop.textContent = t.nextStop; popup.append(stop);
      const delay = document.createElement('span'); delay.textContent = `${t.delaySeconds == null ? 'Sin retraso informado' : t.delaySeconds <= 0 ? 'En hora' : '+' + (t.delaySeconds / 60).toFixed(1) + ' min'} · ${mode === 'Live' ? 'Directo' : 'Grabación'}`; popup.append(delay);
      marker.bindPopup(popup);
    }
  }
  function focus(id) { const m = markers.get(id); if (m) { map.flyTo(m.getLatLng(), 11, { duration: .7 }); m.openPopup(); } }
  return { init, update, focus };
})();
