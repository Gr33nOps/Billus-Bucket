(function () {
    "use strict";

    const memoryStorage = new Map();
    let dotNetInstance = null;
    let renderLoopRunning = false;
    let externalAudioMuted = false;
    let crazyGamesReady = false;
    let crazyGamesEnvironment = "disabled";
    let pokiReady = false;

    function isLocalhost() {
        const host = (window.location && window.location.hostname) || "";
        return host === "localhost" || host === "127.0.0.1" || host === "::1";
    }

    function getQueryParam(name) {
        try {
            const params = new URLSearchParams(window.location.search || "");
            return params.get(name);
        } catch (_err) {
            return null;
        }
    }

    function isRestrictedStorageSimulated() {
        return isLocalhost() && getQueryParam("restrictedStorage") === "1";
    }

    function probePersistentStorage() {
        if (isRestrictedStorageSimulated()) {
            return false;
        }

        try {
            const storage = window.localStorage;
            if (!storage) {
                return false;
            }
            const probeKey = "__billu_storage_probe__";
            storage.setItem(probeKey, "1");
            storage.removeItem(probeKey);
            return true;
        } catch (_err) {
            return false;
        }
    }

    function storageGet(key) {
        if (!key) {
            return null;
        }

        if (!isRestrictedStorageSimulated()) {
            try {
                if (window.localStorage) {
                    const value = window.localStorage.getItem(key);
                    if (value !== null) {
                        memoryStorage.set(key, value);
                        return value;
                    }
                }
            } catch (_err) {
                // Fall back to session memory map below.
            }
        }

        return memoryStorage.has(key) ? memoryStorage.get(key) : null;
    }

    function storageSet(key, value) {
        if (!key) {
            return false;
        }

        const stringValue = String(value ?? "");
        memoryStorage.set(key, stringValue);

        if (isRestrictedStorageSimulated()) {
            return false;
        }

        try {
            if (!window.localStorage) {
                return false;
            }
            window.localStorage.setItem(key, stringValue);
            return true;
        } catch (_err) {
            return false;
        }
    }

    function copyTextToClipboard(text) {
        const value = String(text ?? "").trim();
        if (!value) {
            return false;
        }

        let copied = false;
        try {
            if (document.createElement && document.body && typeof document.execCommand === "function") {
                const area = document.createElement("textarea");
                area.value = value;
                area.setAttribute("readonly", "readonly");
                area.style.position = "fixed";
                area.style.left = "-9999px";
                area.style.top = "0";
                document.body.appendChild(area);
                area.focus();
                area.select();
                copied = Boolean(document.execCommand("copy"));
                document.body.removeChild(area);
                document.getElementById("theCanvas")?.focus?.();
            }
        } catch (_err) {
        }

        try {
            if (window.navigator?.clipboard?.writeText) {
                window.navigator.clipboard.writeText(value).catch(function () {});
                copied = true;
            }
        } catch (_err) {
        }

        return copied;
    }

    let pendingPastedText = null;

    function recordPastedText(text) {
        if (!text) {
            return;
        }
        const cleaned = String(text).trim();
        if (cleaned) {
            pendingPastedText = cleaned;
        }
    }

    if (typeof window !== "undefined" && typeof window.addEventListener === "function") {
        window.addEventListener("paste", function (e) {
            try {
                const text = e.clipboardData?.getData("text/plain");
                if (text) {
                    recordPastedText(text);
                }
            } catch (_err) {
            }
        });
    }

    function consumePastedText() {
        const text = pendingPastedText;
        pendingPastedText = null;
        return text || "";
    }

    function requestClipboardPaste() {
        try {
            if (typeof window !== "undefined" && window.navigator?.clipboard?.readText) {
                window.navigator.clipboard.readText().then(function (text) {
                    recordPastedText(text);
                }).catch(function () {});
                return true;
            }
        } catch (_err) {
        }
        return false;
    }

    function getEffectiveConfig() {
        const raw = window.BILLU_PLATFORM_CONFIG || {};
        let platform = String(raw.activePlatform || "local").trim().toLowerCase();

        // Allow convenient ?platform=local|crazygames|poki testing strictly on localhost.
        if (isLocalhost()) {
            const overridePlatform = getQueryParam("platform");
            if (overridePlatform) {
                platform = overridePlatform.trim().toLowerCase();
            }
        }

        return {
            activePlatform: platform,
            crazyGamesLeaderboardEnabled: Boolean(raw.crazyGamesLeaderboardEnabled),
            crazyGamesEncryptionKeyBase64: String(raw.crazyGamesEncryptionKeyBase64 || "").trim(),
            pokiApprovedExternalLeaderboardEnabled: Boolean(raw.pokiApprovedExternalLeaderboardEnabled),
            sharedLeaderboardEnabled: Boolean(raw.sharedLeaderboardEnabled),
            sharedLeaderboardEndpointUrl: String(raw.sharedLeaderboardEndpointUrl || "").trim(),
            sharedLeaderboardPublicKey: String(raw.sharedLeaderboardPublicKey || "").trim()
        };
    }

    async function initializePlatform() {
        const cfg = getEffectiveConfig();
        const persistentStorageAvailable = probePersistentStorage();

        crazyGamesReady = false;
        crazyGamesEnvironment = "disabled";
        pokiReady = false;

        if (cfg.activePlatform === "crazygames") {
            try {
                if (window.CrazyGames && window.CrazyGames.SDK) {
                    await window.CrazyGames.SDK.init();
                    crazyGamesEnvironment = String(window.CrazyGames.SDK.environment || "crazygames");
                    crazyGamesReady = crazyGamesEnvironment !== "disabled";
                }
            } catch (_err) {
                crazyGamesReady = false;
            }
        } else if (cfg.activePlatform === "poki") {
            try {
                if (window.PokiSDK && typeof window.PokiSDK.init === "function") {
                    await window.PokiSDK.init();
                    pokiReady = true;
                }
            } catch (_err) {
                // Poki documentation requires continuing normally when adblockers reject init().
                pokiReady = Boolean(window.PokiSDK);
            }
        }

        const hasEncryptionKey = cfg.crazyGamesEncryptionKeyBase64.length > 0;
        const hasWebCrypto = Boolean(window.crypto && window.crypto.subtle);
        const hasCrazySubmitApi = Boolean(
            crazyGamesReady &&
            window.CrazyGames &&
            window.CrazyGames.SDK &&
            window.CrazyGames.SDK.user &&
            typeof window.CrazyGames.SDK.user.submitScore === "function"
        );

        const crazyGamesNativeLeaderboardReady =
            cfg.crazyGamesLeaderboardEnabled &&
            hasEncryptionKey &&
            hasWebCrypto &&
            hasCrazySubmitApi;

        return {
            activePlatform: cfg.activePlatform,
            persistentStorageAvailable: persistentStorageAvailable,
            crazyGamesSdkInitialized: crazyGamesReady,
            crazyGamesLeaderboardEnabled: cfg.crazyGamesLeaderboardEnabled,
            crazyGamesNativeLeaderboardReady: crazyGamesNativeLeaderboardReady,
            pokiSdkInitialized: pokiReady,
            pokiApprovedExternalLeaderboardEnabled: cfg.pokiApprovedExternalLeaderboardEnabled,
            sharedLeaderboardEnabled: cfg.sharedLeaderboardEnabled,
            sharedLeaderboardEndpointUrl: cfg.sharedLeaderboardEndpointUrl,
            sharedLeaderboardPublicKey: cfg.sharedLeaderboardPublicKey
        };
    }

    function notifyLoadingStarted() {
        try {
            if (crazyGamesReady && window.CrazyGames?.SDK?.game?.loadingStart) {
                window.CrazyGames.SDK.game.loadingStart();
            }
        } catch (_err) {
        }
    }

    function notifyLoadingFinished() {
        const loadingOverlay = document.getElementById("loadingOverlay");
        if (loadingOverlay) {
            loadingOverlay.style.display = "none";
        }

        try {
            if (crazyGamesReady && window.CrazyGames?.SDK?.game?.loadingStop) {
                window.CrazyGames.SDK.game.loadingStop();
            } else if (pokiReady && window.PokiSDK?.gameLoadingFinished) {
                window.PokiSDK.gameLoadingFinished();
            }
        } catch (_err) {
        }
    }

    function notifyGameplayStart() {
        try {
            if (crazyGamesReady && window.CrazyGames?.SDK?.game?.gameplayStart) {
                window.CrazyGames.SDK.game.gameplayStart();
            } else if (pokiReady && window.PokiSDK?.gameplayStart) {
                window.PokiSDK.gameplayStart();
            }
        } catch (_err) {
        }
    }

    function notifyGameplayStop() {
        try {
            if (crazyGamesReady && window.CrazyGames?.SDK?.game?.gameplayStop) {
                window.CrazyGames.SDK.game.gameplayStop();
            } else if (pokiReady && window.PokiSDK?.gameplayStop) {
                window.PokiSDK.gameplayStop();
            }
        } catch (_err) {
        }
    }

    function base64ToBytes(base64) {
        const binary = window.atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    }

    function bytesToBase64(bytes) {
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return window.btoa(binary);
    }

    // Encrypts score per official CrazyGames SDK v3 Leaderboards documentation:
    // AES-GCM with a random 12-byte IV prepended to ciphertext, base64-encoded.
    async function encryptScoreAesGcm(scoreNumber, base64Key) {
        const iv = window.crypto.getRandomValues(new Uint8Array(12));
        const rawKey = base64ToBytes(base64Key);
        const cryptoKey = await window.crypto.subtle.importKey(
            "raw",
            rawKey,
            { name: "AES-GCM" },
            false,
            ["encrypt"]
        );

        const encodedScore = new TextEncoder().encode(String(scoreNumber));
        const encryptedBuffer = await window.crypto.subtle.encrypt(
            { name: "AES-GCM", iv: iv },
            cryptoKey,
            encodedScore
        );

        const encryptedBytes = new Uint8Array(encryptedBuffer);
        const combined = new Uint8Array(iv.length + encryptedBytes.length);
        combined.set(iv, 0);
        combined.set(encryptedBytes, iv.length);
        return bytesToBase64(combined);
    }

    async function submitCrazyGamesScore(scoreValue) {
        const cfg = getEffectiveConfig();
        const numericScore = Math.floor(Number(scoreValue));
        if (!Number.isFinite(numericScore) || numericScore <= 0) {
            return false;
        }

        if (!cfg.crazyGamesLeaderboardEnabled ||
            !cfg.crazyGamesEncryptionKeyBase64 ||
            !crazyGamesReady ||
            !window.CrazyGames?.SDK?.user?.submitScore) {
            return false;
        }

        try {
            const encryptedScore = await encryptScoreAesGcm(
                numericScore,
                cfg.crazyGamesEncryptionKeyBase64
            );
            window.CrazyGames.SDK.user.submitScore({
                encryptedScore: encryptedScore,
                score: numericScore
            });
            return true;
        } catch (_err) {
            return false;
        }
    }

    let gameViewport = null;
    let lastViewportCheck = 0;

    function syncResponsiveCanvasLayout() {
        const canvas = document.getElementById("theCanvas");
        const holder = document.getElementById("canvasHolder");
        if (!canvas || !holder) {
            return;
        }

        // Keep the backing canvas in client coordinates, but fit the game within
        // the visible rectangle after browser chrome and device cutouts are removed.
        const viewW = Math.max(1, Math.floor(window.innerWidth || holder.clientWidth || 1280));
        const viewH = Math.max(1, Math.floor(window.innerHeight || holder.clientHeight || 720));
        const visual = window.visualViewport;
        const measured = document.getElementById("viewportProbe")?.getBoundingClientRect();
        const left = Math.max(0, Math.floor(visual?.offsetLeft || 0));
        const top = Math.max(0, Math.floor(visual?.offsetTop || 0));
        const width = Math.max(1, Math.floor(Math.min(viewW - left, visual?.width || viewW)));
        const height = Math.max(1, Math.floor(Math.min(viewH - top, visual?.height || viewH,
            measured?.height > 0 ? measured.height : viewH)));
        const style = getComputedStyle(holder);
        const inset = key => Math.max(0, Math.ceil(parseFloat(style[key]) || 0));
        const mobile = Boolean(window.matchMedia?.("(pointer: coarse)").matches);
        const margin = mobile ? 8 : 0;
        const safeLeft = Math.min(width - 1, inset("paddingLeft") + margin);
        const safeTop = Math.min(height - 1, inset("paddingTop") + margin);
        gameViewport = {
            x: left + safeLeft,
            y: top + safeTop,
            width: Math.max(1, width - safeLeft - inset("paddingRight") - margin),
            height: Math.max(1, height - safeTop - inset("paddingBottom") - (mobile ? 12 : 0))
        };
        holder.style.width = viewW + "px";
        holder.style.height = viewH + "px";
        const root = document.documentElement.style;
        root.setProperty("--visible-x", left + "px");
        root.setProperty("--visible-y", top + "px");
        root.setProperty("--visible-width", width + "px");
        root.setProperty("--visible-height", height + "px");

        // Keep #theCanvas aligned with the full client viewport at (0, 0) so KNI's clientX/clientY
        // mouse & touch events match GraphicsDevice.Viewport 1:1 while C# letterboxes 320x180 to 16:9.
        if (canvas.width !== viewW) {
            canvas.width = viewW;
        }
        if (canvas.height !== viewH) {
            canvas.height = viewH;
        }

        canvas.style.position = "absolute";
        canvas.style.left = "0px";
        canvas.style.top = "0px";
        canvas.style.width = viewW + "px";
        canvas.style.height = viewH + "px";
    }

    function showFatalError(error) {
        renderLoopRunning = false;
        dotNetInstance = null;
        externalAudioMuted = true;
        console.error("Game stopped:", error);
        const overlay = document.getElementById("loadingOverlay");
        if (overlay) overlay.style.display = "none";
        const message = document.getElementById("blazor-error-ui");
        if (message) message.style.display = "block";
    }

    function tickAnimationFrame() {
        if (!renderLoopRunning) return;
        try {
            // Embedded mobile browsers can change their toolbar without a resize event.
            if (Date.now() - lastViewportCheck > 250) {
                syncResponsiveCanvasLayout();
                lastViewportCheck = Date.now();
            }
            if (dotNetInstance) dotNetInstance.invokeMethod("TickDotNet");
        } catch (error) {
            showFatalError(error);
            return;
        }
        window.requestAnimationFrame(tickAnimationFrame);
    }

    function startRenderLoop(instance) {
        dotNetInstance = instance;
        const canvas = document.getElementById("theCanvas");
        if (canvas) {
            canvas.addEventListener("contextmenu", function (e) {
                e.preventDefault();
            });
        }

        syncResponsiveCanvasLayout();

        if (!renderLoopRunning) {
            renderLoopRunning = true;
            window.requestAnimationFrame(tickAnimationFrame);
        }
    }

    function isExternalAudioMuted() {
        return externalAudioMuted || document.hidden === true ||
            Boolean(window.matchMedia?.("(orientation: portrait) and (pointer: coarse)").matches);
    }

    function getAssetVersion() {
        return document.querySelector('meta[name="billu-build"]')?.content || "dev";
    }

    window.addEventListener("resize", syncResponsiveCanvasLayout);
    window.addEventListener("orientationchange", syncResponsiveCanvasLayout);
    window.visualViewport?.addEventListener("resize", syncResponsiveCanvasLayout);
    window.visualViewport?.addEventListener("scroll", syncResponsiveCanvasLayout);

    document.addEventListener("visibilitychange", function () {
        externalAudioMuted = document.hidden === true;
    });

    window.addEventListener("keydown", function (event) {
        const blockedCodes = ["Space", "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"];
        if (blockedCodes.indexOf(event.code) !== -1 || [32, 37, 38, 39, 40].indexOf(event.keyCode) !== -1) {
            event.preventDefault();
        }
    }, { passive: false });

    window.addEventListener("wheel", function (event) {
        event.preventDefault();
    }, { passive: false });

    window.billuPlatform = {
        initializePlatform: initializePlatform,
        notifyLoadingStarted: notifyLoadingStarted,
        notifyLoadingFinished: notifyLoadingFinished,
        notifyGameplayStart: notifyGameplayStart,
        notifyGameplayStop: notifyGameplayStop,
        submitCrazyGamesScore: submitCrazyGamesScore,
        storageGet: storageGet,
        storageSet: storageSet,
        copyTextToClipboard: copyTextToClipboard,
        requestClipboardPaste: requestClipboardPaste,
        consumePastedText: consumePastedText,
        recordPastedText: recordPastedText,
        startRenderLoop: startRenderLoop,
        showFatalError: showFatalError,
        isExternalAudioMuted: isExternalAudioMuted,
        getAssetVersion: getAssetVersion,
        getGameViewport: () => gameViewport,
        syncResponsiveCanvasLayout: syncResponsiveCanvasLayout
    };
})();
