const { test } = require("node:test");
const assert = require("node:assert/strict");
const vm = require("node:vm");
const fs = require("node:fs");
function setup(view = null) {
  let queued;
  let copiedClipboardText = null;
  const error = { style: { display: "none" } };
  const overlay = { style: { display: "block" } };
  const canvas = { width: 0, height: 0, style: {}, addEventListener() {}, focus() {} };
  const holder = {
    style: {},
    clientWidth: view?.width,
    clientHeight: view?.height,
  };
  const context = {
    console: { error() {} },
    Map,
    URLSearchParams,
    getComputedStyle() {
      return {
        paddingLeft: String(view?.left || 0),
        paddingRight: String(view?.right || 0),
        paddingTop: "0",
        paddingBottom: String(view?.bottom || 0),
      };
    },
    window: {
      innerWidth: view?.width,
      innerHeight: view?.height,
      navigator: {
        clipboard: {
          writeText(value) {
            copiedClipboardText = value;
            return Promise.resolve();
          },
        },
      },
      matchMedia() {
        return { matches: Boolean(view?.mobile) };
      },
      visualViewport: view
        ? {
          width: view.width,
          height: view.visibleHeight,
          offsetLeft: 0,
          offsetTop: 0,
          addEventListener() {},
        }
        : undefined,
      addEventListener() {},
      requestAnimationFrame(f) {
        queued = f;
      },
      location: { hostname: "localhost" },
    },
    document: {
      documentElement: { style: { setProperty() {} } },
      addEventListener() {},
      getElementById(id) {
        if (view && id === "theCanvas") return canvas;
        if (view && id === "canvasHolder") return holder;
        if (view && id === "viewportProbe") {
          return {
            getBoundingClientRect() {
              return {
                width: view.width,
                height: view.smallHeight || view.height,
              };
            },
          };
        }
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
    canvas,
    getCopiedText: () => copiedClipboardText,
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

test("mobile viewport excludes browser bars and notch insets", () => {
  const s = setup({
    width: 844,
    height: 390,
    visibleHeight: 300,
    left: 44,
    right: 44,
    bottom: 21,
  });
  s.api.syncResponsiveCanvasLayout();
  const bounds = s.api.getGameViewport();
  assert.deepEqual(JSON.parse(JSON.stringify(bounds)), {
    x: 44,
    y: 0,
    width: 756,
    height: 279,
  });
});

test("short phone viewport never forces a taller canvas", () => {
  const s = setup({ width: 320, height: 140, visibleHeight: 140 });
  s.api.syncResponsiveCanvasLayout();
  assert.equal(s.canvas.height, 140);
});

test("mobile uses smaller viewport measurement and reserves a bottom margin", () => {
  const s = setup({
    width: 844,
    height: 390,
    visibleHeight: 390,
    smallHeight: 280,
    mobile: true,
  });
  s.api.syncResponsiveCanvasLayout();
  const bounds = s.api.getGameViewport();
  assert(bounds.y >= 8);
  assert(bounds.y + bounds.height <= 268);
});

test("copyTextToClipboard copies Save Code cleanly and rejects empty input", () => {
  const s = setup();
  assert.equal(s.api.copyTextToClipboard(""), false);
  assert.equal(s.api.copyTextToClipboard("  K7P4-M9X2-W3R8  "), true);
  assert.equal(s.getCopiedText(), "K7P4-M9X2-W3R8");
});

test("consumePastedText returns single-use pasted text for Ctrl+V or paste button", () => {
  const s = setup();
  assert.equal(s.api.consumePastedText(), "");
  s.api.recordPastedText("  K7P4-M9X2-W3R8  ");
  assert.equal(s.api.consumePastedText(), "K7P4-M9X2-W3R8");
  assert.equal(s.api.consumePastedText(), "");
});
