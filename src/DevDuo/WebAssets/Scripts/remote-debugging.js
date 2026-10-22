(function () {
  "use strict";
  if (window.top !== window) return;
  if (window.__devduoRemoteDebugging) {
    window.__devduoRemoteDebugging.connect();
    return;
  }
  let active = true;
  const documentId = `${Date.now()}-${Math.random()}`;
  function post(envelope) {
    if (active && typeof window.__devduoPostDeveloperMessage === "function") {
      window.__devduoPostDeveloperMessage(JSON.stringify(envelope));
    }
  }
  let chobitsu;
  let rootObserver;
  let currentClientId;
  function resetTransient() {
    if (!chobitsu) return;
    for (const [index, command] of [
      { method: "Overlay.setInspectMode", params: { mode: "none" } },
      { method: "Overlay.hideHighlight" },
      { method: "Page.stopScreencast" },
    ].entries()) {
      chobitsu.sendRawMessage(JSON.stringify({ id: -1 - index, ...command }));
    }
  }
  function initialize() {
    // The bundle itself installs listeners on documentElement while loading.
    // Waiting only before ready/DOM.enable is too late for document-start injection.
    if (chobitsu) return true;
    try {
      // Keep the UMD export private even on pages that use AMD or CommonJS globals.
      const module = { exports: {} };
      const exports = module.exports;
      const define = undefined;
      /* CHOBITSU */
      chobitsu = module.exports;
      chobitsu.setOnMessage((message) => post({ type: "cdp", documentId, message }));
      return true;
    } catch (error) {
      post({ type: "error", documentId, message: String(error) });
      return false;
    }
  }
  const bridge = {
    dispatch(message) {
      if (!active || !chobitsu) return;
      const command = JSON.parse(message);
      if (command.__devduoDocumentId !== undefined) {
        if (command.__devduoDocumentId !== documentId) return;
        if (command.__devduoCleanup) {
          if (command.__devduoClientId === currentClientId) {
            resetTransient();
            currentClientId = undefined;
          }
          return;
        }
        currentClientId = command.__devduoClientId;
        delete command.__devduoDocumentId;
        delete command.__devduoClientId;
        delete command.__devduoCleanup;
      }
      chobitsu.sendRawMessage(JSON.stringify(command));
    },
    disconnect() {
      // Keep instrumentation hooks, but release transient UI/input modes and work.
      active = false;
      if (rootObserver) rootObserver.disconnect();
      resetTransient();
      currentClientId = undefined;
    },
    connect() {
      active = true;
      if (!document.documentElement) {
        if (!rootObserver) rootObserver = new MutationObserver(() => {
          if (document.documentElement) {
            rootObserver.disconnect();
            if (active) bridge.connect();
          }
        });
        rootObserver.observe(document, { childList: true });
        return;
      }
      if (initialize()) post({ type: "ready", documentId, url: location.href, title: document.title });
    },
  };
  Object.defineProperty(window, "__devduoRemoteDebugging", { value: bridge });
  document.addEventListener("DOMContentLoaded", () => { if (active) bridge.connect(); }, { once: true });
  window.addEventListener("pageshow", (event) => {
    if (event.persisted && active) {
      resetTransient();
      currentClientId = undefined;
      bridge.connect();
    }
  });
  window.addEventListener("pagehide", () => {
    if (active) {
      resetTransient();
      currentClientId = undefined;
    }
  });
  bridge.connect();
})();
