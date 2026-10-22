import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { runInNewContext } from "node:vm";
import { repoRoot } from "../lib/paths.mts";

test("footer color extension requires a visible opaque full-width fixed edge and clears dynamically", () => {
  const colors: Record<string, number[]> = {
    teal: [24, 185, 168, 255],
    purple: [121, 26, 194, 255],
    clear: [0, 0, 0, 0],
    translucent: [121, 26, 194, 128],
  };
  const root = {
    backgroundColor: "teal",
    scrollHeight: 2400,
    scrollWidth: 382,
    clientWidth: 382,
    clientHeight: 580,
  };
  const style = {
    position: "fixed",
    visibility: "visible",
    opacity: "1",
    backgroundImage: "none",
    backgroundColor: "purple",
  };
  const rect = { left: 0, right: 382, bottom: 580, height: 28 };
  const footer = { parentElement: null, getBoundingClientRect: () => rect };
  const frame = { innerWidth: 382, innerHeight: 580, scrollY: 1820 };
  let visible = true;
  let color = "clear";
  let report: (() => void) | undefined;
  const document = {
    readyState: "complete",
    documentElement: root,
    scrollingElement: root,
    body: root,
    head: {},
    title: "Probe",
    querySelectorAll: () => [],
    elementFromPoint: () => root,
    elementsFromPoint: () => (visible ? [footer] : []),
    createElement: () => ({
      getContext: () => ({
        clearRect: () => {},
        fillRect: () => {},
        set fillStyle(value: string) {
          color = value;
        },
        getImageData: () => ({ data: colors[color] }),
      }),
    }),
  };
  const window: Record<string, unknown> = { addEventListener: () => {} };
  window.top = window;
  const context = {
    ...frame,
    scrollX: 0,
    devicePixelRatio: 2,
    visualViewport: { width: 382, height: 580, scale: 1 },
    crypto: { randomUUID: () => "test-document" },
    performance: { now: () => 0 },
    document,
    window,
    location: { hostname: "localhost" },
    matchMedia: () => ({ matches: false, addEventListener() {} }),
    CSS: { supports: (_property: string, value: string) => value in colors },
    getComputedStyle: (element: unknown) => (element === footer ? style : root),
    requestAnimationFrame: (callback: () => void) => {
      report = callback;
    },
    MutationObserver: class {
      observe() {}
    },
  };
  runInNewContext(
    readFileSync(
      join(repoRoot, "src/DevDuo/WebAssets/Scripts/browser-observer.js"),
      "utf8",
    ),
    context,
  );
  function observedColor() {
    assert.ok(report);
    report();
    const observation = window.devduoShellObservation as {
      fixedBottomColor: number[] | null;
    };
    return observation.fixedBottomColor === null
      ? null
      : Array.from(observation.fixedBottomColor);
  }
  assert.deepEqual(observedColor(), colors.purple.slice(0, 3));
  const initial = window.devduoShellObservation as {
    documentHeight: number;
    documentWidth: number;
    underPageBackgroundFallback: number[];
    documentId: string;
  };
  assert.equal(initial.documentHeight, 2400);
  assert.equal(initial.documentWidth, 382);
  assert.deepEqual(
    Array.from(initial.underPageBackgroundFallback),
    [24, 185, 168],
  );
  assert.equal(initial.documentId, "test-document");
  let jsonMessage = "";
  window.webkit = {
    messageHandlers: {
      devduoShellJSON: {
        postMessage: (value: string) => {
          jsonMessage = value;
        },
      },
    },
  };
  observedColor();
  assert.equal(JSON.parse(jsonMessage).themeColor, null);
  visible = false;
  observedColor();
  assert.equal(
    JSON.parse(jsonMessage).fixedBottomColor,
    null,
    "The Windows JSON transport preserves null fields omitted by WebKit's C dictionary conversion",
  );
  visible = true;
  delete window.webkit;
  root.scrollHeight = 3200;
  observedColor();
  assert.equal(
    (window.devduoShellObservation as { documentHeight: number })
      .documentHeight,
    3200,
  );
  document.scrollingElement = {
    ...root,
    scrollHeight: 1800,
    scrollWidth: 1600,
  };
  observedColor();
  assert.equal(
    (window.devduoShellObservation as { documentHeight: number })
      .documentHeight,
    1800,
    "Quirks pages report the actual body scrolling element",
  );
  assert.equal(
    (window.devduoShellObservation as { documentWidth: number }).documentWidth,
    1600,
    "Horizontal continuation uses the actual document extent",
  );
  rect.right = 150;
  assert.equal(
    observedColor(),
    null,
    "A narrow marker must not become a full-width color extension",
  );
  rect.right = 382;
  visible = false;
  assert.equal(
    observedColor(),
    null,
    "Removing the footer clears its old color",
  );
  visible = true;
  for (const change of [
    { position: "absolute" },
    { visibility: "hidden" },
    { opacity: "0.5" },
    { backgroundImage: "linear-gradient(red, blue)" },
    { backgroundColor: "clear" },
    { backgroundColor: "translucent" },
  ]) {
    const previous = { ...style };
    Object.assign(style, change);
    assert.equal(observedColor(), null, JSON.stringify(change));
    Object.assign(style, previous);
  }
  rect.bottom = 560;
  assert.equal(
    observedColor(),
    null,
    "A floating panel away from the viewport edge is not a footer",
  );
  rect.bottom = 580;
  assert.deepEqual(observedColor(), colors.purple.slice(0, 3));
  assert.deepEqual(
    {
      innerWidth: context.innerWidth,
      innerHeight: context.innerHeight,
      scrollY: context.scrollY,
    },
    frame,
  );
});
