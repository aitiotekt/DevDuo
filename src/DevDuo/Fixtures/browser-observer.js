(() => {
  if (window.__devduoShellObserver) return; window.__devduoShellObserver = true;
  let pending = false, previousY = scrollY;
  function rgb(value, requireOpaque = false) {
    if (!value || !CSS.supports('color', value)) return null;
    const canvas = document.createElement('canvas'); canvas.width = canvas.height = 1;
    const c = canvas.getContext('2d', { willReadFrequently: true }); c.clearRect(0, 0, 1, 1); c.fillStyle = value; c.fillRect(0, 0, 1, 1);
    const p = c.getImageData(0, 0, 1, 1).data;
    return p[3] && (!requireOpaque || p[3] === 255) ? Array.from(p).slice(0, 3) : null;
  }
  function fixedBottomColor() {
    // Read paint order and geometry without resizing the page or changing its scroll range.
    // This approximates solid fixed-footer color extension; it does not sample arbitrary artwork.
    let sharedFooter = null;
    for (const x of [1, innerWidth / 2, innerWidth - 1]) {
      let footer = null;
      for (const hit of document.elementsFromPoint(x, innerHeight - 0.5)) {
        for (let element = hit; element; element = element.parentElement) {
          const style = getComputedStyle(element);
          if (style.position !== 'fixed') continue;
          const rect = element.getBoundingClientRect();
          if (rect.height <= 0 || rect.left > 1 || rect.right < innerWidth - 1
              || Math.abs(rect.bottom - innerHeight) > 1 || style.visibility !== 'visible'
              || Number(style.opacity) !== 1 || style.backgroundImage !== 'none'
              || !rgb(style.backgroundColor, true)) continue;
          footer = element;
          break;
        }
        if (footer) break;
      }
      if (!footer || (sharedFooter && sharedFooter !== footer)) return null;
      sharedFooter = footer;
    }
    return sharedFooter ? rgb(getComputedStyle(sharedFooter).backgroundColor, true) : null;
  }
  function report() {
    pending = false; const theme = [...document.querySelectorAll('meta[name="theme-color"]')].find(m => !m.media || matchMedia(m.media).matches);
    let element = document.elementFromPoint(innerWidth / 2, Math.max(0, innerHeight - 12)), background = null;
    while (element && !background) { background = rgb(getComputedStyle(element).backgroundColor); element = element.parentElement; }
    const data = { type: 'devduo-shell-observation', scrollY, deltaY: scrollY - previousY, viewportWidth: innerWidth, viewportHeight: innerHeight, themeColor: rgb(theme?.content), rootBackgroundColor: rgb(getComputedStyle(document.documentElement).backgroundColor) || rgb(getComputedStyle(document.body || document.documentElement).backgroundColor) || [239, 243, 248], fixedBottomColor: fixedBottomColor(), domBackgroundColor: background || rgb(getComputedStyle(document.body || document.documentElement).backgroundColor) || [239, 243, 248], title: document.title, host: location.hostname || 'Local color fixture', timestamp: new Date().toISOString() }; previousY = scrollY;
    window.devduoShellObservation = data;
    if (window.webkit?.messageHandlers?.devduoShell) window.webkit.messageHandlers.devduoShell.postMessage(data);
    else if (window.chrome?.webview) window.chrome.webview.postMessage(data);
  }
  function schedule() { if (!pending) { pending = true; requestAnimationFrame(report); } }
  window.addEventListener('scroll', schedule, { passive: true }); window.addEventListener('resize', schedule); window.addEventListener('load', schedule);
  const watch = () => {
    if (document.head) new MutationObserver(schedule).observe(document.head, { subtree: true, attributes: true, childList: true, characterData: true });
    if (document.body) new MutationObserver(schedule).observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['style', 'class', 'hidden'] }); schedule();
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', watch, { once: true }); else watch();
  window.__devduoReportContainerObservation = report;
})();
