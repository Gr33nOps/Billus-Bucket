const { test } = require("node:test");
const assert = require("node:assert/strict");
const vm = require("node:vm");
const fs = require("node:fs");
function setup(view = null) {
  let queued;
  const windowEvents = {}, documentEvents = {};
  const persisted = new Map();
  let rejectWrites = false;
  let mockedFetch = async () => { throw Error("Unexpected network request"); };
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
    AbortController,
    setTimeout,
    clearTimeout,
    fetch: (...args) => mockedFetch(...args),
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
            if (view?.clipboardDenied) return Promise.reject(new Error("Permission denied"));
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
      addEventListener(name, handler) { (windowEvents[name] ||= []).push(handler); },
      localStorage: {
        getItem(key) { return persisted.get(key) ?? null; },
        setItem(key,value) { if (rejectWrites) throw Error("Storage full"); persisted.set(key,value); },
        removeItem(key) { persisted.delete(key); }
      },
      requestAnimationFrame(f) {
        queued = f;
      },
      location: { hostname: "localhost" },
    },
    document: {
      documentElement: { style: { setProperty() {} } },
      addEventListener(name, handler) { (documentEvents[name] ||= []).push(handler); },
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
    setRejectWrites(value) { rejectWrites = value; },
    setHidden(value) { context.document.hidden = value; for (const f of documentEvents.visibilitychange || []) f(); },
    emitWindow(name) { for (const f of windowEvents[name] || []) f({}); },
    setBlazor(value) { context.window.Blazor = value; },
    setFetch(value) { mockedFetch = value; },
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

test("copyTextToClipboard copies Save Code cleanly and rejects empty input", async () => {
  const s = setup();
  assert.equal(await s.api.copyTextToClipboardAsync(""), false);
  assert.equal(await s.api.copyTextToClipboardAsync("  K7P4-M9X2-W3R8  "), true);
  assert.equal(s.getCopiedText(), "K7P4-M9X2-W3R8");
});

test("consumePastedText returns single-use pasted text for Ctrl+V or paste button", () => {
  const s = setup();
  assert.equal(s.api.consumePastedText(), "");
  s.api.recordPastedText("  K7P4-M9X2-W3R8  ");
  assert.equal(s.api.consumePastedText(), "K7P4-M9X2-W3R8");
  assert.equal(s.api.consumePastedText(), "");
});

test("clipboard denial never reports a copied Save Code", async () => {
  const s = setup({clipboardDenied:true});
  assert.equal(await s.api.copyTextToClipboardAsync("K7P4-M9X2-W3R8"), false);
});

test("failed storage writes remain authoritative for the current session", () => {
  const s = setup();
  s.api.storageSet("score", "100");
  s.setRejectWrites(true);
  assert.equal(s.api.storageSet("score", "500"), false);
  assert.equal(s.api.storageGet("score"), "500");
});

test("a background interruption is observed even when no frames ran while hidden", () => {
  const s = setup();
  let mutedDuringFrame;
  s.api.startRenderLoop({invokeMethod() { mutedDuringFrame = s.api.isExternalAudioMuted(); }});
  s.setHidden(true);
  s.setHidden(false);
  s.tick();
  assert.equal(mutedDuringFrame, true);
  s.tick();
  assert.equal(mutedDuringFrame, false);
  s.emitWindow("blur");
  assert.equal(s.api.isExternalAudioMuted(), true);
  s.emitWindow("focus");
  s.tick();
  assert.equal(mutedDuringFrame, true);
});

test("Blazor startup failure displays reload UI instead of an endless loader", async () => {
  const s = setup();
  s.setBlazor({start: async () => {throw Error("Runtime download failed");}});
  await s.api.startBrowserGame();
  assert.equal(s.error.style.display, "block");
  assert.equal(s.overlay.style.display, "none");
});

test("page hide flushes and suspends the game before animation frames stop", () => {
  const s = setup();
  const calls=[];
  s.api.startRenderLoop({invokeMethod(name) {calls.push(name);}});
  s.emitWindow("pagehide");
  assert(calls.includes("SuspendBrowserGame"));
});

test("runtime resource downloads retry temporary failures and preserve integrity", async () => {
  const s = setup();
  let requests=0;
  s.setFetch(async (uri, options) => {
    assert.equal(uri,"/runtime.wasm");
    assert.equal(options.integrity,"sha256-test");
    requests++;
    return new Response(requests===1 ? "temporary" : "runtime", {status:requests===1 ? 503 : 200});
  });
  s.setBlazor({start: async options => {
    assert.equal(options.loadBootResource("dotnetjs", "runtime.js", "/runtime.js", ""), null);
    const response=await options.loadBootResource("dotnetwasm", "runtime.wasm", "/runtime.wasm", "sha256-test");
    assert.equal(await response.text(),"runtime");
  }});
  await s.api.startBrowserGame();
  assert.equal(requests,2);
  assert.equal(s.error.style.display,"none");
});

test("missing runtime resource shows reload UI without retrying a permanent 404", async () => {
  const s=setup();let requests=0;
  s.setFetch(async()=>{requests++;return new Response("missing",{status:404})});
  s.setBlazor({start: options=>options.loadBootResource("assembly","missing.wasm","/missing.wasm","")});
  await s.api.startBrowserGame();
  assert.equal(requests,1);
  assert.equal(s.error.style.display,"block");
  assert.equal(s.overlay.style.display,"none");
});
