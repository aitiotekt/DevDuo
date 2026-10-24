import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { runInNewContext } from "node:vm";
import { repoRoot } from "../lib/paths.mts";

const source = readFileSync(
  join(repoRoot, "src/DevDuo/WebAssets/Scripts/touch-input.js"),
  "utf8",
);

type Listener = (event: InputEvent) => void;
class InputEvent {
  defaultPrevented = false;
  stopped = false;
  isTrusted = false;
  pointerId = 1;
  pointerType = "mouse";
  button = 0;
  detail = 1;
  clientX = 20;
  clientY = 100;
  screenX = 20;
  screenY = 100;
  target!: Element;
  type: string;
  constructor(type: string, options: object = {}) {
    this.type = type;
    Object.assign(this, options);
  }
  preventDefault() {
    this.defaultPrevented = true;
  }
  stopImmediatePropagation() {
    this.stopped = true;
  }
  composedPath() {
    return [this.target];
  }
}
class Events {
  listeners = new Map<string, Listener[]>();
  addEventListener(name: string, listener: Listener) {
    const listeners = this.listeners.get(name) ?? [];
    listeners.push(listener);
    this.listeners.set(name, listeners);
  }
  emit(event: InputEvent) {
    for (const listener of this.listeners.get(event.type) ?? []) {
      listener(event);
      if (event.stopped) break;
    }
  }
}
class Element extends Events {
  parentElement: Element | null = null;
  isConnected = true;
  scrollTop = 0;
  scrollLeft = 0;
  scrollLimit = 1500;
  clicks = 0;
  focused = false;
  textContent = "";
  style = {
    touchAction: "auto",
    overflowX: "visible",
    overflowY: "visible",
    overscrollBehaviorX: "auto",
    overscrollBehaviorY: "auto",
  };
  private window: Events;
  tag: string;
  constructor(window: Events, tag = "div") {
    super();
    this.window = window;
    this.tag = tag;
  }
  dispatchEvent(event: InputEvent) {
    event.target = this;
    this.window.emit(event);
    if (!event.stopped) this.emit(event);
    return !event.defaultPrevented;
  }
  setPointerCapture() {}
  releasePointerCapture() {}
  appendChild(node: Element) {
    node.isConnected = true;
  }
  remove() {
    this.isConnected = false;
  }
  closest() {
    return this.tag === "button" ? this : null;
  }
  contains(target: Element): boolean {
    return (
      target === this ||
      (target.parentElement !== null && this.contains(target.parentElement))
    );
  }
  focus() {
    this.focused = true;
  }
  click() {
    this.clicks++;
  }
  scrollBy({ left, top }: { left: number; top: number }) {
    this.scrollLeft = Math.max(0, this.scrollLeft + left);
    this.scrollTop = Math.max(
      0,
      Math.min(this.scrollLimit, this.scrollTop + top),
    );
  }
}
function fixture() {
  const window = Object.assign(new Events(), {
    top: null as unknown,
    __devduoInput: null as unknown,
  });
  window.top = window;
  const root = new Element(window, "html");
  const button = new Element(window, "button");
  button.parentElement = root;
  const navigator = { maxTouchPoints: 0 };
  const document = Object.assign(new Events(), {
    documentElement: root,
    scrollingElement: root,
    createElement: () => new Element(window),
  });
  const timers = new Map<number, () => void>();
  let timer = 0;
  const context = {
    window,
    document,
    navigator,
    frames: [],
    parent: window,
    Element,
    Event: InputEvent,
    PointerEvent: InputEvent,
    getComputedStyle: (element: Element) => element.style,
    scrollX: 0,
    scrollY: 0,
    setTimeout: (callback: () => void) => {
      timers.set(++timer, callback);
      return timer;
    },
    clearTimeout: (id: number) => timers.delete(id),
  };
  runInNewContext(`(${source})("touch")`, context);
  const input = window.__devduoInput as {
    configure(mode: string): void;
    state: { mode: string; active: boolean; panSequence: number };
  };
  function native(
    type: string,
    y: number,
    target = button,
    extra: object = {},
  ) {
    const event = new InputEvent(type, {
      isTrusted: true,
      target,
      clientY: y,
      screenY: y,
      ...extra,
    });
    window.emit(event);
    return event;
  }
  return {
    window,
    input,
    root,
    button,
    navigator,
    native,
    timers,
    flush: () => {
      for (const callback of [...timers.values()]) callback();
      timers.clear();
    },
  };
}

test("touch pans the page, emits a cancelled touch pointer and blocks wheel; switching keeps scroll", () => {
  const f = fixture();
  const events: InputEvent[] = [];
  for (const type of [
    "pointerdown",
    "pointercancel",
    "touchstart",
    "touchmove",
    "touchend",
  ])
    f.button.addEventListener(type, (event) => events.push(event));
  f.native("pointerdown", 100);
  f.native("pointermove", 20);
  f.native("pointerup", 20);
  assert.equal(f.root.scrollTop, 80);
  assert.equal(f.input.state.active, false);
  assert.deepEqual(
    events.map((event) => event.type),
    ["pointerdown", "touchstart", "touchmove", "pointercancel", "touchend"],
  );
  assert.equal(events[0]?.pointerType, "touch");
  assert.ok(events.every((event) => !event.isTrusted));
  assert.equal(f.native("wheel", 20).defaultPrevented, true);
  f.native("click", 20);
  f.flush();
  assert.equal(
    f.button.clicks,
    0,
    "panning must not activate the element under the finger",
  );
  f.input.configure("mouse");
  assert.equal(f.navigator.maxTouchPoints, 0);
  assert.equal(f.native("wheel", 20).defaultPrevented, false);
  assert.equal(f.native("pointerdown", 100).stopped, false);
  assert.equal(f.root.scrollTop, 80);
});

test("tap has one compatibility activation or one fallback, while cancelled start still gets terminal events", () => {
  const f = fixture();
  assert.equal(f.navigator.maxTouchPoints, 1);
  f.native("pointerdown", 100);
  f.native("pointerup", 100);
  assert.equal(
    f.native("click", 100).stopped,
    false,
    "the engine's real compatibility click is allowed",
  );
  f.flush();
  assert.equal(
    f.button.clicks,
    0,
    "a compatibility click cancels the synthetic fallback",
  );
  f.native("pointerdown", 100);
  f.native("pointerup", 100);
  f.flush();
  assert.equal(f.button.clicks, 1);
  assert.equal(f.button.focused, true);
  const terminal: string[] = [];
  f.button.addEventListener("touchstart", (event) => event.preventDefault());
  f.button.addEventListener("pointerup", (event) => terminal.push(event.type));
  f.button.addEventListener("touchend", (event) => terminal.push(event.type));
  f.native("pointerdown", 100);
  f.native("pointermove", 20);
  f.native("pointerup", 20);
  f.flush();
  assert.equal(f.root.scrollTop, 0);
  assert.deepEqual(terminal, ["pointerup", "touchend"]);
  assert.equal(f.button.clicks, 1);
});

test("nested scrolling consumes motion, overscroll containment stops chaining and touch-action none preserves custom dragging", () => {
  const f = fixture();
  const nested = new Element(f.window);
  nested.parentElement = f.root;
  nested.style.overflowY = "auto";
  nested.scrollLimit = 50;
  f.button.parentElement = nested;
  f.native("pointerdown", 100);
  f.native("pointermove", 20);
  f.native("pointerup", 20);
  assert.equal(nested.scrollTop, 50);
  assert.equal(f.root.scrollTop, 30);
  nested.style.overscrollBehaviorY = "contain";
  f.native("pointerdown", 100);
  f.native("pointermove", 20);
  f.native("pointerup", 20);
  assert.equal(f.root.scrollTop, 30);
  f.button.style.touchAction = "none";
  let moves = 0,
    cancelled = 0;
  f.button.addEventListener("pointermove", () => moves++);
  f.button.addEventListener("touchcancel", () => cancelled++);
  f.native("pointerdown", 100);
  f.native("pointermove", 20);
  assert.equal(moves, 1);
  assert.equal(f.root.scrollTop, 30);
  f.input.configure("mouse");
  assert.equal(cancelled, 1);
  assert.equal(f.input.state.active, false);
  assert.equal(f.input.state.mode, "mouse");
});

test("root overflow lock prevents drag scrolling without resizing or replacing the document", () => {
  const f = fixture();
  f.root.style.overflowY = "hidden";
  f.native("pointerdown", 100);
  f.native("pointermove", 20);
  f.native("pointerup", 20);
  assert.equal(f.root.scrollTop, 0);
  f.root.style.overflowY = "visible";
  f.native("pointerdown", 100);
  f.native("pointermove", 20);
  f.native("pointerup", 20);
  assert.equal(f.root.scrollTop, 80);
});
