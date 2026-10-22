import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { runInNewContext } from "node:vm";
import { repoRoot } from "../lib/paths.mts";

const bootstrap = readFileSync(
  join(repoRoot, "src/DevDuo/WebAssets/Scripts/remote-debugging.js"),
  "utf8",
);

test("native debugging bootstrap isolates exports, reconnects and mutes without removing hooks", () => {
  const envelopes: Record<string, unknown>[] = [];
  const commands: string[] = [];
  const pageModule = { exports: "page-owned" };
  let onMessage: (message: string) => void = () => {};
  const backend = {
    setOnMessage(callback: typeof onMessage) {
      onMessage = callback;
    },
    sendRawMessage(message: string) {
      commands.push(message);
    },
  };
  const window: Record<string, unknown> = {};
  window.top = window;
  window.addEventListener = () => {};
  window.__devduoPostDeveloperMessage = (raw: string) =>
    envelopes.push(JSON.parse(raw));
  const context = {
    window,
    module: pageModule,
    define: Object.assign(() => {}, { amd: true }),
    document: { documentElement: {}, title: "Example", addEventListener() {} },
    location: { href: "https://example.test/" },
    backend,
  };
  const script = bootstrap.replace(
    "/* CHOBITSU */",
    "module.exports = backend;",
  );
  runInNewContext(script, context);
  const bridge = window.__devduoRemoteDebugging as {
    dispatch(message: string): void;
    disconnect(): void;
  };
  assert.equal(envelopes[0]?.type, "ready");
  assert.equal(pageModule.exports, "page-owned");
  assert.equal(window.chobitsu, undefined);
  bridge.dispatch('{"id":1,"method":"Runtime.enable"}');
  assert.equal(commands.length, 1);
  onMessage('{"id":1,"result":{}}');
  assert.equal(envelopes[1]?.type, "cdp");
  const firstDocument = envelopes[0]?.documentId;
  bridge.disconnect();
  assert.deepEqual(
    commands.slice(1).map((command) => JSON.parse(command).method),
    ["Overlay.setInspectMode", "Overlay.hideHighlight", "Page.stopScreencast"],
  );
  bridge.dispatch("ignored");
  onMessage("ignored");
  assert.equal(commands.length, 4);
  assert.equal(envelopes.length, 2);
  runInNewContext(script, context);
  assert.equal(envelopes[2]?.type, "ready");
  assert.equal(envelopes[2]?.documentId, firstDocument);
});

test("document-start instrumentation waits for the HTML root before advertising DOM readiness", () => {
  const envelopes: Record<string, unknown>[] = [];
  let observed = false;
  let callback = () => {};
  const window: Record<string, unknown> = {};
  window.top = window;
  window.addEventListener = () => {};
  window.__devduoPostDeveloperMessage = (raw: string) =>
    envelopes.push(JSON.parse(raw));
  const document: {
    documentElement: object | null;
    title: string;
    addEventListener(): void;
  } = {
    documentElement: null,
    title: "",
    addEventListener() {},
  };
  class Observer {
    constructor(handler: () => void) {
      callback = handler;
    }
    observe() {
      observed = true;
    }
    disconnect() {
      observed = false;
    }
  }
  runInNewContext(
    bootstrap.replace(
      "/* CHOBITSU */",
      "if (!document.documentElement) throw Error('backend loaded before HTML root'); module.exports = { setOnMessage() {}, sendRawMessage() {} };",
    ),
    {
      window,
      document,
      location: { href: "https://example.test/" },
      MutationObserver: Observer,
    },
  );
  assert.equal(observed, true);
  assert.equal(envelopes.length, 0);
  document.documentElement = {};
  callback();
  assert.equal(observed, false);
  assert.equal(envelopes[0]?.type, "ready");
});

test("BFCache restoration reannounces the active document and never revives a disconnected bridge", () => {
  const envelopes: Record<string, unknown>[] = [];
  const listeners = new Map<string, (event: { persisted: boolean }) => void>();
  let initializations = 0;
  const window: Record<string, unknown> = {};
  window.top = window;
  window.addEventListener = (
    name: string,
    handler: (event: { persisted: boolean }) => void,
  ) => listeners.set(name, handler);
  window.__devduoPostDeveloperMessage = (raw: string) =>
    envelopes.push(JSON.parse(raw));
  const backend = { setOnMessage() {}, sendRawMessage() {} };
  const context = {
    window,
    backend,
    document: { documentElement: {}, title: "First", addEventListener() {} },
    location: { href: "https://example.test/first" },
    initializeBackend() {
      initializations++;
    },
  };
  const script = bootstrap.replace(
    "/* CHOBITSU */",
    "initializeBackend(); module.exports = backend;",
  );
  runInNewContext(script, context);
  const firstDocument = envelopes[0]?.documentId;
  listeners.get("pageshow")?.({ persisted: false });
  assert.equal(envelopes.length, 1);
  listeners.get("pageshow")?.({ persisted: true });
  assert.equal(envelopes.length, 2);
  assert.equal(envelopes[1]?.documentId, firstDocument);
  assert.equal(initializations, 1);
  (window.__devduoRemoteDebugging as { disconnect(): void }).disconnect();
  listeners.get("pageshow")?.({ persisted: true });
  assert.equal(envelopes.length, 2);
  // Native navigation completion refreshes the current transport setter and bootstrap.
  runInNewContext(script, context);
  assert.equal(envelopes.length, 3);
  assert.equal(envelopes[2]?.documentId, firstDocument);
  assert.equal(initializations, 1);
});

test("frontend cleanup resets transient modes only for its own client and document", () => {
  const envelopes: Record<string, unknown>[] = [];
  const commands: Record<string, unknown>[] = [];
  const window: Record<string, unknown> = {};
  window.top = window;
  window.addEventListener = () => {};
  window.__devduoPostDeveloperMessage = (raw: string) =>
    envelopes.push(JSON.parse(raw));
  const backend = {
    setOnMessage() {},
    sendRawMessage(raw: string) {
      commands.push(JSON.parse(raw));
    },
  };
  runInNewContext(
    bootstrap.replace("/* CHOBITSU */", "module.exports = backend;"),
    {
      window,
      backend,
      document: {
        documentElement: {},
        title: "Example",
        addEventListener() {},
      },
      location: { href: "https://example.test/" },
    },
  );
  const bridge = window.__devduoRemoteDebugging as {
    dispatch(raw: string): void;
  };
  const documentId = envelopes[0]?.documentId;
  const command = {
    id: 7,
    method: "Overlay.setInspectMode",
    params: { mode: "searchForNode" },
  };
  bridge.dispatch(
    JSON.stringify({
      ...command,
      __devduoClientId: "first",
      __devduoDocumentId: documentId,
    }),
  );
  assert.deepEqual(commands[0], command);
  bridge.dispatch(
    JSON.stringify({
      ...command,
      id: 8,
      __devduoClientId: "second",
      __devduoDocumentId: documentId,
    }),
  );
  bridge.dispatch(
    JSON.stringify({
      __devduoCleanup: true,
      __devduoClientId: "first",
      __devduoDocumentId: documentId,
    }),
  );
  bridge.dispatch(
    JSON.stringify({
      __devduoCleanup: true,
      __devduoClientId: "second",
      __devduoDocumentId: "old-document",
    }),
  );
  assert.equal(commands.length, 2);
  const cleanup = JSON.stringify({
    __devduoCleanup: true,
    __devduoClientId: "second",
    __devduoDocumentId: documentId,
  });
  bridge.dispatch(cleanup);
  assert.deepEqual(
    commands.slice(2).map((entry) => entry.method),
    ["Overlay.setInspectMode", "Overlay.hideHighlight", "Page.stopScreencast"],
  );
  assert.deepEqual(commands[2]?.params, { mode: "none" });
  bridge.dispatch(cleanup);
  assert.equal(commands.length, 5);
  bridge.dispatch(
    JSON.stringify({
      ...command,
      id: 9,
      __devduoClientId: "third",
      __devduoDocumentId: documentId,
    }),
  );
  assert.equal(commands.length, 6);
});
