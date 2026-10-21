(() => {
  const query = new URLSearchParams(location.search);
  const labels = { device: query.get('device') || 'unknown', pose: query.get('pose') || 'unknown', browser: query.get('browser') || 'unknown', state: query.get('state') || 'initial', displayMode: query.get('displayMode') || 'unknown', notes: query.get('notes') || '' };
  const select = document.getElementById('state'); select.value = labels.state;
  document.getElementById('labels').textContent = JSON.stringify(labels);
  const number = n => Number(n.toFixed(3));
  function measure(reason) {
    const s = screen, v = visualViewport, safe = getComputedStyle(document.getElementById('safe-probe'));
    const components = {}; document.querySelectorAll('[data-measure]').forEach(e => { const r = e.getBoundingClientRect(); components[e.id] = { x: number(r.x), y: number(r.y), width: number(r.width), height: number(r.height), documentX: number(r.x + scrollX), documentY: number(r.y + scrollY) }; });
    return { schemaVersion: 1, clientTimestamp: new Date().toISOString(), reason, observedState: scrollY <= 0 ? 'initial' : 'scrolled', labels: { ...labels, state: select.value || labels.state }, screen: { width: s.width, height: s.height, availWidth: s.availWidth, availHeight: s.availHeight, colorDepth: s.colorDepth, pixelDepth: s.pixelDepth, orientation: s.orientation ? { type: s.orientation.type, angle: s.orientation.angle } : null }, viewport: { innerWidth, innerHeight, outerWidth, outerHeight, clientWidth: document.documentElement.clientWidth, clientHeight: document.documentElement.clientHeight, devicePixelRatio, scrollX, scrollY, documentWidth: document.documentElement.scrollWidth, documentHeight: document.documentElement.scrollHeight, visual: v ? { width: v.width, height: v.height, offsetLeft: v.offsetLeft, offsetTop: v.offsetTop, pageLeft: v.pageLeft, pageTop: v.pageTop, scale: v.scale } : null }, safeAreaCssPx: { top: parseFloat(safe.paddingTop), right: parseFloat(safe.paddingRight), bottom: parseFloat(safe.paddingBottom), left: parseFloat(safe.paddingLeft) }, components, environment: { userAgent: navigator.userAgent, platform: navigator.platform, language: navigator.language, url: location.href, displayMode: matchMedia('(display-mode: standalone)').matches ? 'standalone' : 'browser', colorScheme: matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light', reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches, timezone: Intl.DateTimeFormat().resolvedOptions().timeZone }, unknown: { physicalWidthMm: null, physicalHeightMm: null, hardwareCornerRadiusMm: null, liquidGlassMaterialParameters: null }, units: { screen: 'CSS px reported by browser', viewport: 'CSS px', safeAreaCssPx: 'CSS px of page safe-area environment', components: 'CSS px, bounding client rect in layout viewport; document coordinates also supplied' } };
  }
  let last;
  async function capture(reason) {
    const record = measure(reason); last = record; window.devduoLastMeasurement = record;
    document.getElementById('readout').textContent = JSON.stringify({ screen: record.screen, viewport: record.viewport, safeAreaCssPx: record.safeAreaCssPx }, null, 2);
    try { const r = await fetch('/api/measurements', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(record) }); if (!r.ok) throw Error(r.status); const saved = await r.json(); document.getElementById('status').textContent = `Saved ${reason} · ${saved.id}`; window.devduoLastRecordId = saved.id; } catch (e) { document.getElementById('status').textContent = 'Save failed: ' + e.message; }
    return record;
  }
  window.devduoCapture = capture;
  let timer; const schedule = reason => { clearTimeout(timer); timer = setTimeout(() => capture(reason), 350) };
  window.addEventListener('resize', () => schedule('resize')); window.addEventListener('orientationchange', () => schedule('orientationchange')); window.addEventListener('scroll', () => schedule('scroll'), { passive: true });
  if (visualViewport) { visualViewport.addEventListener('resize', () => schedule('visualViewport.resize')); visualViewport.addEventListener('scroll', () => schedule('visualViewport.scroll')); }
  select.addEventListener('change', () => capture('state-change')); document.getElementById('capture').addEventListener('click', () => capture('manual'));
  window.addEventListener('load', () => setTimeout(() => capture('initial-load'), 350));
})();
