(() => {
  if (window.__devduoShellObserver) return; window.__devduoShellObserver = true;
  let pending = false, previousY = scrollY;
  function rgb(value) {
    if (!value || !CSS.supports('color', value)) return null;
    const canvas = document.createElement('canvas'); canvas.width = canvas.height = 1;
    const c = canvas.getContext('2d', { willReadFrequently: true }); c.clearRect(0, 0, 1, 1); c.fillStyle = value; c.fillRect(0, 0, 1, 1);
    const p = c.getImageData(0, 0, 1, 1).data; return p[3] ? Array.from(p).slice(0, 3) : null;
  }
  function report() {
    pending = false; const theme = [...document.querySelectorAll('meta[name="theme-color"]')].find(m => !m.media || matchMedia(m.media).matches);
    let element = document.elementFromPoint(innerWidth / 2, Math.max(0, innerHeight - 12)), background = null;
    while (element && !background) { background = rgb(getComputedStyle(element).backgroundColor); element = element.parentElement; }
    const data = { type: 'devduo-shell-observation', scrollY, deltaY: scrollY - previousY, viewportWidth: innerWidth, viewportHeight: innerHeight, themeColor: rgb(theme?.content), rootBackgroundColor: rgb(getComputedStyle(document.documentElement).backgroundColor) || rgb(getComputedStyle(document.body || document.documentElement).backgroundColor) || [239, 243, 248], domBackgroundColor: background || rgb(getComputedStyle(document.body || document.documentElement).backgroundColor) || [239, 243, 248], title: document.title, host: location.hostname || 'Local color fixture', timestamp: new Date().toISOString() }; previousY = scrollY;
    window.devduoShellObservation = data;
    if (window.webkit?.messageHandlers?.devduoShell) window.webkit.messageHandlers.devduoShell.postMessage(data);
    else if (window.chrome?.webview) window.chrome.webview.postMessage(data);
  }
  function schedule() { if (!pending) { pending = true; requestAnimationFrame(report); } }
  window.addEventListener('scroll', schedule, { passive: true }); window.addEventListener('resize', schedule); window.addEventListener('load', schedule);
  const watch = () => {
    if (document.head) new MutationObserver(schedule).observe(document.head, { subtree: true, attributes: true, childList: true, characterData: true });
    if (document.body) new MutationObserver(schedule).observe(document.body, { attributes: true, attributeFilter: ['style', 'class'] }); schedule();
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', watch, { once: true }); else watch();
  window.__devduoReportContainerObservation = report;
})();
