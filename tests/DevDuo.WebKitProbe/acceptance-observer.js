(() => {
  const scroller = document.scrollingElement || document.documentElement;
  const rect = element => {
    const r = element.getBoundingClientRect();
    return [r.x, r.y, r.width, r.height].map(value => Math.round(value * 1000) / 1000);
  };
  const units = Object.fromEntries([...document.querySelectorAll('[data-unit]')]
    .map(element => [element.dataset.unit, rect(element)[3]]));
  const positioned = [...document.querySelectorAll('*')].flatMap(element => {
    const position = getComputedStyle(element).position;
    return position === 'fixed' || position === 'sticky'
      ? [{ tag: element.tagName, position, rect: rect(element) }] : [];
  });
  return {
    documentId: window.devduoShellObservation?.documentId,
    mode: document.compatMode, url: location.href,
    inner: [innerWidth, innerHeight], client: [scroller.clientWidth, scroller.clientHeight],
    visual: [visualViewport.width, visualViewport.height, visualViewport.scale],
    document: [scroller.scrollWidth, scroller.scrollHeight],
    range: [Math.max(0, scroller.scrollWidth - scroller.clientWidth), Math.max(0, scroller.scrollHeight - scroller.clientHeight)],
    scroll: [scrollX, scrollY], positioned, units,
    dpr: devicePixelRatio, screen: [screen.width, screen.height], userAgent: navigator.userAgent,
  };
})()
