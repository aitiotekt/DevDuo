import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import test from "node:test";
import { runInNewContext } from "node:vm";
import { repoRoot } from "../lib/paths.mts";

test("owned calibration labels update without navigating or accepting another origin", () => {
  const elements = new Map<
    string,
    { value: string; textContent: string; addEventListener: () => void }
  >();
  const element = (id: string) => {
    const value = elements.get(id) ?? {
      value: "",
      textContent: "",
      addEventListener() {},
    };
    elements.set(id, value);
    return value;
  };
  const location = {
    origin: "http://127.0.0.1:8765",
    pathname: "/",
    search: "?pose=closed&state=initial",
  };
  const history = {
    state: { preserved: true },
    replacements: [] as string[],
    replaceState(_state: unknown, _title: string, target: URL) {
      this.replacements.push(target.href);
    },
  };
  const window: Record<string, unknown> = { addEventListener() {} };
  runInNewContext(readFileSync(join(repoRoot, "web/calibration.js"), "utf8"), {
    URL,
    URLSearchParams,
    location,
    history,
    window,
    visualViewport: null,
    document: { getElementById: element },
    setTimeout: () => 1,
    clearTimeout() {},
  });
  const update = window.devduoUpdateCalibrationLabels as (
    target: string,
  ) => void;
  update(
    "http://127.0.0.1:8765/?pose=open&browser=Windows-WebKit&state=initial&displayMode=safari-shell-approx&notes=body-clockwise-90",
  );
  assert.equal(JSON.parse(element("labels").textContent).pose, "open");
  assert.equal(
    JSON.parse(element("labels").textContent).notes,
    "body-clockwise-90",
  );
  assert.equal(history.replacements.length, 1);
  assert.equal(history.state.preserved, true);
  update("https://example.com/?pose=other");
  update("http://127.0.0.1:8765/unrelated?pose=other");
  assert.equal(history.replacements.length, 1);
  assert.equal(JSON.parse(element("labels").textContent).pose, "open");
});
