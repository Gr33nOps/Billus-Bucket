const { test } = require("node:test");
const assert = require("node:assert/strict");
const vm = require("node:vm");
const fs = require("node:fs");
function setup() {
  let queued;
  const error = { style: { display: "none" } };
  const overlay = { style: { display: "block" } };
  const context = {
    console: { error() {} },
    Map,
    URLSearchParams,
    window: {
      addEventListener() {},
      requestAnimationFrame(f) {
        queued = f;
      },
      location: { hostname: "localhost" },
    },
    document: {
      addEventListener() {},
      getElementById(id) {
        return id === "blazor-error-ui"
          ? error
          : id === "loadingOverlay"
          ? overlay
          : null;
      },
    },
  };
  vm.createContext(context);
  vm.runInContext(
    fs.readFileSync("BillusBucket.Web/wwwroot/js/platform-bridge.js", "utf8"),
    context,
  );
  return {
    api: context.window.billuPlatform,
    error,
    overlay,
    tick() {
      const f = queued;
      queued = null;
      f();
      return queued;
    },
  };
}
test("fatal game exception stops rendering and displays reload UI", () => {
  const s = setup();
  s.api.startRenderLoop({
    invokeMethod() {
      throw Error("broken asset");
    },
  });
  assert.equal(s.tick(), null);
  assert.equal(s.error.style.display, "block");
  assert.equal(s.overlay.style.display, "none");
});
