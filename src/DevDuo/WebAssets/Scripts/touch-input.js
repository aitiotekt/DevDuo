(initialMode, nativeTouch = false) => {
  if (window.__devduoInput) {
    window.__devduoInput.configure(initialMode);
    return;
  }
  if (nativeTouch) {
    // Native contacts, default gestures and compatibility clicks belong to
    // WebKit. This branch only reports the configured mode to the observer.
    let mode = initialMode;
    function configure(value) {
      if (value !== "touch" && value !== "mouse") return;
      mode = value;
      for (let index = 0; index < frames.length; index++)
        frames[index].postMessage({ type: "devduo-interaction-mode", mode }, "*");
      window.__devduoReportContainerObservation?.();
    }
    window.addEventListener("message", event => {
      if (window !== window.top && event.source === parent && event.data?.type === "devduo-interaction-mode") configure(event.data.mode);
    });
    Object.defineProperty(window, "__devduoInput", { value: {
      configure,
      get state() { return { mode, source: "WebKit native single-contact input", trustedTouchEvents: true, multiTouch: false }; },
    } });
    configure(initialMode);
    return;
  }
  let mode = initialMode;
  let contact = null;
  let pendingTap = null;
  let panSequence = 0;
  let direction = 0;
  let moveFrame = 0, pendingMove = null;
  let receivedMoves = 0, processedMoves = 0, scrollWrites = 0;
  let wheelSuppression = false;
  const nativeMaxTouchPoints = navigator.maxTouchPoints;
  const touchDescriptor = Object.getOwnPropertyDescriptor(window, "ontouchstart");

  // These are DOM emulation events, never trusted engine touch injection. Keep
  // native matchMedia/CSS pointer capabilities unchanged rather than spoofing them.
  Object.defineProperty(navigator, "maxTouchPoints", {
    configurable: true,
    get: () => mode === "touch" ? 1 : nativeMaxTouchPoints,
  });

  function suppress(event) {
    event.preventDefault();
    event.stopImmediatePropagation();
  }
  function pointer(type, gesture, event) {
    return gesture.target.dispatchEvent(new PointerEvent(type, {
      bubbles: true, cancelable: true, composed: true,
      pointerId: gesture.id, pointerType: "touch", isPrimary: true,
      clientX: event.clientX, clientY: event.clientY,
      screenX: event.screenX, screenY: event.screenY,
      button: 0, buttons: type === "pointerup" || type === "pointercancel" ? 0 : 1,
      width: 16, height: 16, pressure: type === "pointerdown" || type === "pointermove" ? 0.5 : 0,
    }));
  }
  function touch(type, gesture, event) {
    const values = {
      identifier: gesture.id, target: gesture.target,
      clientX: event.clientX, clientY: event.clientY,
      pageX: event.clientX + scrollX, pageY: event.clientY + scrollY,
      screenX: event.screenX, screenY: event.screenY,
      radiusX: 8, radiusY: 8, rotationAngle: 0, force: type === "touchend" || type === "touchcancel" ? 0 : 0.5,
    };
    const point = typeof Touch === "function" ? new Touch(values) : values;
    const active = type === "touchend" || type === "touchcancel" ? [] : [point];
    const options = { bubbles: true, cancelable: true, composed: true, touches: active, targetTouches: active, changedTouches: [point] };
    let result;
    if (typeof TouchEvent === "function") result = new TouchEvent(type, options);
    else {
      // Desktop ports can omit TouchEvent/Touch constructors. PointerEvent is
      // still a real DOM interface; the legacy touch payload remains diagnostic.
      result = new Event(type, options);
      for (const key of ["touches", "targetTouches", "changedTouches"])
        Object.defineProperty(result, key, { value: options[key] });
    }
    return gesture.target.dispatchEvent(result);
  }
  function discardMove() {
    if (moveFrame) cancelAnimationFrame(moveFrame);
    moveFrame = 0; pendingMove = null;
  }
  function cancel() {
    discardMove();
    if (!contact) return;
    const gesture = contact;
    contact = null;
    if (!gesture.panning) pointer("pointercancel", gesture, gesture.last);
    touch("touchcancel", gesture, gesture.last);
    try { gesture.target.releasePointerCapture(gesture.id); } catch { /* Native contact may already be gone. */ }
  }
  function stopTap() {
    if (pendingTap) clearTimeout(pendingTap.timer);
    pendingTap = null;
  }
  function allowedPan(target) {
    let x = true, y = true;
    for (let node = target; node instanceof Element; node = node.parentElement) {
      const action = getComputedStyle(node).touchAction || "auto";
      if (action === "none") return { x: false, y: false };
      if (action !== "auto" && action !== "manipulation") {
        x &&= /pan-x|pan-left|pan-right/.test(action);
        y &&= /pan-y|pan-up|pan-down/.test(action);
      }
    }
    return { x, y };
  }
  function scrollState(node, root) {
    const style = getComputedStyle(node);
    const rootNode = node === root;
    const bodyStyle = rootNode && document.body ? getComputedStyle(document.body) : style;
    const overflowX = rootNode && style.overflowX === "visible" ? bodyStyle.overflowX : style.overflowX;
    const overflowY = rootNode && style.overflowY === "visible" ? bodyStyle.overflowY : style.overflowY;
    return { node, rootNode,
      x: rootNode ? !/hidden|clip/.test(overflowX) : /auto|scroll|overlay/.test(overflowX),
      y: rootNode ? !/hidden|clip/.test(overflowY) : /auto|scroll|overlay/.test(overflowY),
      stopX: /contain|none/.test(style.overscrollBehaviorX), stopY: /contain|none/.test(style.overscrollBehaviorY) };
  }
  function scrollChain(gesture, dx, dy) {
    let remainingX = dx, remainingY = dy;
    const root = document.scrollingElement;
    let states;
    if (!gesture.chain) {
      states = [];
      for (let node = gesture.target; node instanceof Element; node = node.parentElement) {
        const state = scrollState(node, root);
        if (state.x || state.y || state.rootNode) states.push(state);
      }
      gesture.chain = states.map(state => state.node);
    } else {
      // Recheck only scroll candidates, before writing any scroll offset. This
      // preserves live root/modal locks without restyling every deep ancestor.
      states = gesture.chain.filter(node => node.isConnected).map(node => scrollState(node, root));
    }
    for (const { node, x, y, rootNode, stopX, stopY } of states) {
      if (!node.isConnected) continue;
      const beforeX = node.scrollLeft, beforeY = node.scrollTop;
      scrollWrites++;
      node.scrollBy({ left: x ? remainingX : 0, top: y ? remainingY : 0, behavior: "instant" });
      remainingX -= node.scrollLeft - beforeX;
      remainingY -= node.scrollTop - beforeY;
      if (stopX) remainingX = 0;
      if (stopY) remainingY = 0;
      if (rootNode || (!remainingX && !remainingY)) break;
    }
  }
  function down(event) {
    if (mode !== "touch" || !event.isTrusted || event.pointerType !== "mouse" || event.button !== 0) return;
    cancel(); stopTap();
    const target = event.composedPath()[0];
    contact = {
      id: event.pointerId, target, startX: event.clientX, startY: event.clientY,
      last: event, moved: false, panning: false, pan: allowedPan(target), preventScroll: false, preventClick: false,
    };
    const gesture = contact;
    gesture.preventClick = !pointer("pointerdown", gesture, event);
    gesture.preventScroll = !touch("touchstart", gesture, event);
    gesture.preventClick ||= gesture.preventScroll;
    try { target.setPointerCapture(event.pointerId); } catch { /* Some native controls cannot capture. */ }
    suppress(event);
    window.__devduoReportContainerObservation?.();
  }
  function move(event) {
    if (mode !== "touch" || !event.isTrusted || event.pointerType !== "mouse") return;
    suppress(event);
    if (!contact) return;
    receivedMoves++;
    contact.moved ||= Math.hypot(event.clientX - contact.startX, event.clientY - contact.startY) >= 6;
    pendingMove = event;
    if (!moveFrame) moveFrame = requestAnimationFrame(flushMove);
  }
  function flushMove() {
    const event = pendingMove;
    discardMove();
    if (!event || !contact) return;
    if (!contact.target.isConnected) { cancel(); return; }
    processedMoves++;
    const gesture = contact;
    const dx = gesture.last.clientX - event.clientX, dy = gesture.last.clientY - event.clientY;
    gesture.moved ||= Math.hypot(event.clientX - gesture.startX, event.clientY - gesture.startY) >= 6;
    if (!gesture.panning) pointer("pointermove", gesture, event);
    if (!touch("touchmove", gesture, event)) gesture.preventScroll = true;
    if (gesture.moved && !gesture.preventScroll && ((gesture.pan.x && dx) || (gesture.pan.y && dy))) {
      if (!gesture.panning) {
        gesture.panning = true;
        pointer("pointercancel", gesture, event);
      }
      direction = dy > 0 ? 1 : dy < 0 ? -1 : direction;
      panSequence++;
      scrollChain(gesture, gesture.pan.x ? dx : 0, gesture.pan.y ? dy : 0);
    }
    gesture.last = event;
  }
  function up(event) {
    if (mode !== "touch" || !event.isTrusted || event.pointerType !== "mouse" || event.button !== 0) return;
    suppress(event);
    flushMove();
    if (!contact) return;
    const gesture = contact;
    contact = null;
    if (!gesture.panning) { const accepted = pointer("pointerup", gesture, event); gesture.preventClick ||= !accepted; }
    const accepted = touch("touchend", gesture, event);
    gesture.preventClick ||= !accepted;
    if (!gesture.moved && !gesture.preventClick && gesture.target.isConnected) {
      const focusable = gesture.target.closest("input,textarea,select,button,a[href],[tabindex],[contenteditable=true]");
      focusable?.focus({ preventScroll: true });
      // Prefer the engine's compatibility click for native activation. Some
      // desktop ports suppress it after cancelling pointerdown, so retain one
      // delayed fallback; the real click cancels it before page handlers run.
      const tap = { target: gesture.target, timer: 0 };
      pendingTap = tap;
      tap.timer = setTimeout(() => {
        if (pendingTap !== tap) return;
        pendingTap = null;
        if (tap.target.isConnected) tap.target.click();
      }, 150);
    }
    window.__devduoReportContainerObservation?.();
  }
  function nativeClick(event) {
    if (mode !== "touch" || !event.isTrusted || event.detail === 0) return;
    if (pendingTap && (event.composedPath().includes(pendingTap.target) || pendingTap.target.contains(event.target))) {
      stopTap();
      return;
    }
    suppress(event);
  }
  function configure(value) {
    if (value !== "touch" && value !== "mouse") return;
    if (value !== mode) { cancel(); stopTap(); }
    mode = value;
    // A dormant non-passive wheel handler still makes WebKit consult the page
    // main thread. Keep that blocking region only while touch emulation needs it.
    if (wheelSuppression !== (mode === "touch")) {
      wheelSuppression = mode === "touch";
      if (wheelSuppression) window.addEventListener("wheel", suppress, { capture: true, passive: false });
      else window.removeEventListener("wheel", suppress, true);
    }
    if (mode === "touch" && !touchDescriptor) Object.defineProperty(window, "ontouchstart", { configurable: true, writable: true, value: null });
    else if (mode === "mouse" && !touchDescriptor) delete window.ontouchstart;
    for (let index = 0; index < frames.length; index++)
      frames[index].postMessage({ type: "devduo-interaction-mode", mode }, "*");
    window.__devduoReportContainerObservation?.();
  }
  window.addEventListener("message", event => {
    if (window !== window.top && event.source === parent && event.data?.type === "devduo-interaction-mode") configure(event.data.mode);
  });
  // Capture before arbitrary page handlers, but let all emulated events through.
  window.addEventListener("pointerdown", down, true);
  window.addEventListener("pointermove", move, true);
  window.addEventListener("pointerup", up, true);
  window.addEventListener("pointercancel", event => { if (event.isTrusted) cancel(); }, true);
  window.addEventListener("lostpointercapture", event => { if (event.isTrusted) cancel(); }, true);
  for (const name of ["mousedown", "mousemove", "mouseup", "dblclick", "dragstart", "contextmenu"])
    window.addEventListener(name, event => { if (mode === "touch" && event.isTrusted) suppress(event); }, true);
  window.addEventListener("click", nativeClick, true);
  window.addEventListener("blur", () => { cancel(); stopTap(); });
  window.addEventListener("pagehide", () => { cancel(); stopTap(); });
  Object.defineProperty(window, "__devduoInput", { value: {
    configure,
    get state() { return { mode, active: contact !== null, panSequence, direction, receivedMoves, processedMoves, scrollWrites, source: "DOM single-touch emulation", trustedTouchEvents: false, multiTouch: false }; },
  } });
  configure(initialMode);
}
