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

    function syncResponsiveCanvasLayout() {
        const canvas = document.getElementById("theCanvas");
        const holder = document.getElementById("canvasHolder");
        if (!canvas || !holder) {
            return;
        }

        const viewW = Math.max(320, Math.floor(holder.clientWidth || window.innerWidth || 1280));
        const viewH = Math.max(180, Math.floor(holder.clientHeight || window.innerHeight || 720));

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

    function tickAnimationFrame() {
        if (dotNetInstance) {
            try {
                dotNetInstance.invokeMethod("TickDotNet");
            } catch (_err) {
            }
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
        return externalAudioMuted || document.hidden === true;
    }

    window.addEventListener("resize", syncResponsiveCanvasLayout);
    window.addEventListener("orientationchange", syncResponsiveCanvasLayout);

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
        startRenderLoop: startRenderLoop,
        isExternalAudioMuted: isExternalAudioMuted,
        syncResponsiveCanvasLayout: syncResponsiveCanvasLayout
    };
})();
