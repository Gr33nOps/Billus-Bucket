using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace BillusBucket.Web
{
    public sealed class BrowserBootstrapState
    {
        public string ActivePlatform { get; set; } = "local";
        public bool PersistentStorageAvailable { get; set; }
        public bool CrazyGamesSdkInitialized { get; set; }
        public bool CrazyGamesLeaderboardEnabled { get; set; }
        public bool CrazyGamesNativeLeaderboardReady { get; set; }
        public bool PokiSdkInitialized { get; set; }
        public bool PokiApprovedExternalLeaderboardEnabled { get; set; }
        public bool SharedLeaderboardEnabled { get; set; }
        public string SharedLeaderboardEndpointUrl { get; set; } = string.Empty;
        public string SharedLeaderboardPublicKey { get; set; } = string.Empty;

        public PlatformRuntimeConfig ToRuntimeConfig()
        {
            return new PlatformRuntimeConfig
            {
                ActivePlatform = ActivePlatform ?? "local",
                CrazyGamesSdkAvailable = CrazyGamesSdkInitialized,
                CrazyGamesLeaderboardEnabled = CrazyGamesLeaderboardEnabled && CrazyGamesNativeLeaderboardReady,
                PokiApprovedExternalLeaderboardEnabled = PokiApprovedExternalLeaderboardEnabled,
                SharedLeaderboardEnabled = SharedLeaderboardEnabled,
                SharedLeaderboardEndpointUrl = SharedLeaderboardEndpointUrl ?? string.Empty,
                SharedLeaderboardPublicKey = SharedLeaderboardPublicKey ?? string.Empty
            };
        }
    }

    /// <summary>
    /// Bridges C# platform lifecycle callbacks (loadingStart/Stop, gameplayStart/Stop, external mute)
    /// to browser portal SDKs (CrazyGames SDK v3, Poki SDK v2, or standalone local mode).
    /// </summary>
    public sealed class BrowserJsPlatformBridge : IGamePlatformBridge
    {
        private readonly IJSRuntime jsRuntime;
        private readonly IJSInProcessRuntime jsInProcess;

        public BrowserBootstrapState State { get; private set; } = new BrowserBootstrapState();

        public string PlatformId => State.ActivePlatform;
        public GameViewportBounds ViewportBounds
        {
            get
            {
                try { return jsInProcess?.Invoke<GameViewportBounds>("billuPlatform.getGameViewport"); }
                catch { return null; }
            }
        }

        public bool IsExternalAudioMuted
        {
            get
            {
                if (jsInProcess == null)
                {
                    return false;
                }

                try
                {
                    return jsInProcess.Invoke<bool>("billuPlatform.isExternalAudioMuted");
                }
                catch
                {
                    return false;
                }
            }
        }

        public BrowserJsPlatformBridge(IJSRuntime jsRuntime)
        {
            this.jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
            jsInProcess = jsRuntime as IJSInProcessRuntime;
        }

        public async Task InitializeAsync()
        {
            try
            {
                BrowserBootstrapState initialized =
                    await jsRuntime.InvokeAsync<BrowserBootstrapState>("billuPlatform.initializePlatform");
                if (initialized != null)
                {
                    State = initialized;
                }
            }
            catch
            {
                State = new BrowserBootstrapState();
            }
        }

        public void NotifyLoadingStarted()
        {
            InvokeFireAndForget("billuPlatform.notifyLoadingStarted");
        }

        public void NotifyLoadingFinished()
        {
            InvokeFireAndForget("billuPlatform.notifyLoadingFinished");
        }

        public void NotifyGameplayStart()
        {
            InvokeFireAndForget("billuPlatform.notifyGameplayStart");
        }

        public void NotifyGameplayStop()
        {
            InvokeFireAndForget("billuPlatform.notifyGameplayStop");
        }

        public bool TryCopyTextToClipboard(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                if (jsInProcess != null)
                {
                    return jsInProcess.Invoke<bool>("billuPlatform.copyTextToClipboard", text);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> CopyTextToClipboardAsync(string text)
        {
            try
            {
                return await jsRuntime.InvokeAsync<bool>("billuPlatform.copyTextToClipboardAsync", text);
            }
            catch
            {
                return false;
            }
        }

        public bool RequestClipboardPaste()
        {
            try
            {
                if (jsInProcess != null)
                {
                    return jsInProcess.Invoke<bool>("billuPlatform.requestClipboardPaste");
                }

                _ = jsRuntime.InvokeVoidAsync("billuPlatform.requestClipboardPaste");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryConsumePastedText(out string text)
        {
            text = null;
            try
            {
                if (jsInProcess == null)
                {
                    return false;
                }

                string candidate = jsInProcess.Invoke<string>("billuPlatform.consumePastedText");
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return false;
                }

                text = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void InvokeFireAndForget(string identifier)
        {
            try
            {
                if (jsInProcess != null)
                {
                    jsInProcess.InvokeVoid(identifier);
                }
                else
                {
                    _ = jsRuntime.InvokeVoidAsync(identifier);
                }
            }
            catch
            {
                // Portal SDK errors or adblockers must never disrupt gameplay.
            }
        }
    }

    /// <summary>
    /// Safe browser localStorage implementation of ILocalScoreStorage.
    /// Gracefully degrades to session memory when third-party storage is blocked in iframes or private windows.
    /// </summary>
    public sealed class BrowserLocalScoreStorage : ILocalScoreStorage
    {
        private const string BestScoreKey = "billus_bucket.personal_best";
        // Retain the former storage key only to migrate existing players' personal best.
        private const string LegacyBestScoreKey = "billu_ki_balti.personal_best";
        private const string PlayerIdKey = "billus_bucket.player_id";
        private const string ConnectCodeKey = "billus_bucket.connect_code";
        private const string CustomNameKey = "billus_bucket.custom_name";
        private const string DisplayNameKey = "billus_bucket.display_name";

        private readonly IJSInProcessRuntime jsInProcess;
        private BigInteger cachedPersonalBest;
        private bool hasPendingFlush;
        private string cachedPlayerId;
        private string cachedConnectCode;
        private string cachedDisplayName;
        private bool customNameChecked;
        private bool hasCustomDisplayName;
        private bool persistentAvailable;

        public bool IsPersistentStorageAvailable => persistentAvailable;

        public bool HasCustomDisplayName
        {
            get
            {
                EnsureCustomNameLoaded();
                return hasCustomDisplayName;
            }
        }

        public BrowserLocalScoreStorage(IJSRuntime jsRuntime, bool initialPersistentAvailable)
        {
            jsInProcess = jsRuntime as IJSInProcessRuntime;
            persistentAvailable = initialPersistentAvailable && jsInProcess != null;
            cachedPersonalBest = ReadStoredPersonalBest();
        }

        public BigInteger LoadPersonalBest()
        {
            BigInteger stored = ReadStoredPersonalBest();
            if (stored > cachedPersonalBest)
            {
                cachedPersonalBest = stored;
            }

            return cachedPersonalBest;
        }

        public void SavePersonalBest(BigInteger bestScore, bool flushImmediately = true)
        {
            if (bestScore <= cachedPersonalBest)
            {
                return;
            }

            cachedPersonalBest = bestScore;
            hasPendingFlush = true;

            if (flushImmediately)
            {
                Flush();
            }
        }

        public string GetOrCreateAnonymousPlayerId()
        {
            if (!string.IsNullOrWhiteSpace(cachedPlayerId))
            {
                return cachedPlayerId;
            }

            string existing = TryStorageGet(PlayerIdKey);
            if (!string.IsNullOrWhiteSpace(existing) && existing.Replace("-", string.Empty).Length >= 12)
            {
                cachedPlayerId = existing;
                return cachedPlayerId;
            }

            cachedPlayerId = PlayerIdentityValidator.CreateConnectCode();
            cachedConnectCode = cachedPlayerId;
            TryStorageSet(PlayerIdKey, cachedPlayerId);
            TryStorageSet(ConnectCodeKey, cachedConnectCode);
            return cachedPlayerId;
        }

        public string GetOrCreateConnectCode()
        {
            if (!string.IsNullOrWhiteSpace(cachedConnectCode))
            {
                return cachedConnectCode;
            }

            string existingCode = TryStorageGet(ConnectCodeKey);
            if (PlayerIdentityValidator.TryNormalizeConnectCode(existingCode, out string normalizedExisting))
            {
                cachedConnectCode = normalizedExisting;
                return cachedConnectCode;
            }

            string playerId = GetOrCreateAnonymousPlayerId();
            if (PlayerIdentityValidator.TryNormalizeConnectCode(playerId, out string normalizedFromPlayerId))
            {
                cachedConnectCode = normalizedFromPlayerId;
            }
            else
            {
                cachedConnectCode = PlayerIdentityValidator.CreateConnectCode();
            }

            TryStorageSet(ConnectCodeKey, cachedConnectCode);
            return cachedConnectCode;
        }

        public string GetOrCreateSafeDisplayName()
        {
            EnsureCustomNameLoaded();
            if (!string.IsNullOrWhiteSpace(cachedDisplayName))
            {
                return cachedDisplayName;
            }

            string existing = TryStorageGet(DisplayNameKey);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                cachedDisplayName = LeaderboardStore.NormalizeName(existing);
                return cachedDisplayName;
            }

            cachedDisplayName = AnonymousPlayerIdentity.CreateSafeDisplayName(GetOrCreateAnonymousPlayerId());
            TryStorageSet(DisplayNameKey, cachedDisplayName);
            return cachedDisplayName;
        }

        public void SaveCustomDisplayName(string customName)
        {
            if (!PlayerIdentityValidator.TryValidateCustomName(customName, out string validName, out _))
            {
                return;
            }

            customNameChecked = true;
            hasCustomDisplayName = true;
            cachedDisplayName = validName;
            TryStorageSet(CustomNameKey, validName);
            TryStorageSet(DisplayNameKey, validName);
        }

        public void ApplyRestoredProfile(string formattedConnectCode, string customDisplayName, BigInteger restoredPersonalBest)
        {
            if (PlayerIdentityValidator.TryNormalizeConnectCode(formattedConnectCode, out string normalizedCode))
            {
                cachedPlayerId = normalizedCode;
                cachedConnectCode = normalizedCode;
                TryStorageSet(PlayerIdKey, normalizedCode);
                TryStorageSet(ConnectCodeKey, normalizedCode);
            }

            if (PlayerIdentityValidator.TryValidateCustomName(customDisplayName, out string validName, out _))
            {
                customNameChecked = true;
                cachedDisplayName = validName;
                hasCustomDisplayName = !AnonymousPlayerIdentity.IsAutoGeneratedName(validName);
                if (hasCustomDisplayName)
                {
                    TryStorageSet(CustomNameKey, validName);
                }
                TryStorageSet(DisplayNameKey, validName);
            }

            if (restoredPersonalBest > cachedPersonalBest)
            {
                SavePersonalBest(restoredPersonalBest, flushImmediately: true);
            }
        }

        public void Flush()
        {
            if (!hasPendingFlush || cachedPersonalBest <= BigInteger.Zero)
            {
                return;
            }

            if (TryStorageSet(BestScoreKey, cachedPersonalBest.ToString(CultureInfo.InvariantCulture)))
            {
                hasPendingFlush = false;
            }
        }

        private void EnsureCustomNameLoaded()
        {
            if (customNameChecked)
            {
                return;
            }

            customNameChecked = true;
            string savedCustom = TryStorageGet(CustomNameKey);
            if (PlayerIdentityValidator.TryValidateCustomName(savedCustom, out string validCustom, out _))
            {
                cachedDisplayName = validCustom;
                hasCustomDisplayName = true;
            }
        }

        private BigInteger ReadStoredPersonalBest()
        {
            string raw = TryStorageGet(BestScoreKey);
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = TryStorageGet(LegacyBestScoreKey);
            }
            if (string.IsNullOrWhiteSpace(raw))
            {
                return cachedPersonalBest;
            }

            if (BigInteger.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger parsed) &&
                parsed > BigInteger.Zero)
            {
                return BigInteger.Max(cachedPersonalBest, parsed);
            }

            return cachedPersonalBest;
        }

        private string TryStorageGet(string key)
        {
            if (jsInProcess == null)
            {
                return null;
            }

            try
            {
                return jsInProcess.Invoke<string>("billuPlatform.storageGet", key);
            }
            catch
            {
                persistentAvailable = false;
                return null;
            }
        }

        private bool TryStorageSet(string key, string value)
        {
            if (jsInProcess == null)
            {
                persistentAvailable = false;
                return false;
            }

            try
            {
                bool savedPersisted = jsInProcess.Invoke<bool>("billuPlatform.storageSet", key, value);
                persistentAvailable = savedPersisted;
                return savedPersisted;
            }
            catch
            {
                persistentAvailable = false;
                return false;
            }
        }
    }

    /// <summary>
    /// JS interop client for CrazyGames SDK v3 AES-GCM encrypted score submission.
    /// </summary>
    public sealed class BrowserCrazyGamesSdkClient : ICrazyGamesSdkClient
    {
        private readonly IJSRuntime jsRuntime;
        private readonly BrowserJsPlatformBridge bridge;

        public bool IsSdkInitialized => bridge.State.CrazyGamesSdkInitialized;
        public bool IsNativeLeaderboardReady => bridge.State.CrazyGamesNativeLeaderboardReady;

        public BrowserCrazyGamesSdkClient(IJSRuntime jsRuntime, BrowserJsPlatformBridge bridge)
        {
            this.jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        public async Task<bool> SubmitEncryptedScoreAsync(long score)
        {
            if (!IsNativeLeaderboardReady || score <= 0)
            {
                return false;
            }

            try
            {
                return await jsRuntime.InvokeAsync<bool>("billuPlatform.submitCrazyGamesScore", score);
            }
            catch
            {
                return false;
            }
        }
    }
}
