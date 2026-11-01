using System;
using System.IO;
using System.Threading.Tasks;

namespace BillusBucket
{
    public readonly struct AudioPreferences
    {
        public bool MusicMuted { get; }
        public bool SfxMuted { get; }
        public AudioPreferences(bool musicMuted, bool sfxMuted) { MusicMuted = musicMuted; SfxMuted = sfxMuted; }
        public string Serialize() => (MusicMuted ? "1" : "0") + (SfxMuted ? "1" : "0");
        public static bool TryParse(string value, out AudioPreferences preferences)
        {
            preferences = default;
            if (value == null || value.Length != 2 || (value[0] != '0' && value[0] != '1') || (value[1] != '0' && value[1] != '1'))
                return false;
            preferences = new AudioPreferences(value[0] == '1', value[1] == '1');
            return true;
        }
    }

    public sealed class GameViewportBounds
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
    /// <summary>
    /// Configuration selecting the active publishing platform and leaderboard integration.
    /// Safe defaults ensure zero unauthorized external requests on strict CSP portals like Poki.
    /// </summary>
    public sealed class PlatformRuntimeConfig
    {
        public string ActivePlatform { get; set; } = "local";

        // CrazyGames native weekly leaderboard (invite-only; one leaderboard per game).
        public bool CrazyGamesLeaderboardEnabled { get; set; }
        public bool CrazyGamesSdkAvailable { get; set; }

        // Poki strict External Resources Policy flag (enable only after Poki CSP + Privacy Policy approval).
        public bool PokiApprovedExternalLeaderboardEnabled { get; set; }

        // Optional shared online weekly leaderboard (e.g., Supabase Edge Function).
        public bool SharedLeaderboardEnabled { get; set; }
        public string SharedLeaderboardEndpointUrl { get; set; } = string.Empty;
        public string SharedLeaderboardPublicKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Decouples core gameplay from browser portal SDK lifecycle events (CrazyGames, Poki, etc.).
    /// </summary>
    public interface IGamePlatformBridge
    {
        string PlatformId { get; }
        bool IsExternalAudioMuted { get; }
        GameViewportBounds ViewportBounds => null;

        string LoadAudioPreferences() => null;
        void SaveAudioPreferences(string value) { }
        Task InitializeAsync();
        void NotifyLoadingStarted();
        void NotifyLoadingFinished();
        void NotifyGameplayStart();
        void NotifyGameplayStop();
        bool TryCopyTextToClipboard(string text) => false;
        Task<bool> CopyTextToClipboardAsync(string text) => Task.FromResult(TryCopyTextToClipboard(text));
        bool RequestClipboardPaste() => false;
        bool TryConsumePastedText(out string text)
        {
            text = null;
            return false;
        }
    }

    /// <summary>
    /// Standalone desktop bridge: local audio preferences and no portal notifications.
    /// </summary>
    public sealed class NullPlatformBridge : IGamePlatformBridge
    {
        public string PlatformId { get; }
        public bool IsExternalAudioMuted => false;

        private readonly string preferencesPath;
        public NullPlatformBridge(string platformId = "desktop", string preferencesPath = null)
        {
            PlatformId = string.IsNullOrWhiteSpace(platformId) ? "desktop" : platformId;
            this.preferencesPath = preferencesPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BillusBucket", "audio.txt");
        }

        public string LoadAudioPreferences()
        {
            try { return File.Exists(preferencesPath) ? File.ReadAllText(preferencesPath) : null; }
            catch { return null; }
        }
        public void SaveAudioPreferences(string value)
        {
            if (!AudioPreferences.TryParse(value, out _)) return;
            try
            {
                string directory = Path.GetDirectoryName(preferencesPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(preferencesPath, value);
            }
            catch { }
        }

        public Task InitializeAsync() => Task.CompletedTask;
        public void NotifyLoadingStarted() { }
        public void NotifyLoadingFinished() { }
        public void NotifyGameplayStart() { }
        public void NotifyGameplayStop() { }
    }

    /// <summary>
    /// Wraps an IGamePlatformBridge to enforce strict portal SDK lifecycle rules:
    /// - loadingStart / loadingFinished fire at most once.
    /// - gameplayStart never fires before loadingFinished, and never fires twice consecutively.
    /// - gameplayStop only fires when gameplay is currently active.
    /// </summary>
    public sealed class PlatformLifecycleGuard : IGamePlatformBridge
    {
        private readonly IGamePlatformBridge inner;
        private bool loadingStarted;
        private bool loadingFinished;
        private bool gameplayActive;

        public string PlatformId => inner.PlatformId;
        public GameViewportBounds ViewportBounds => inner.ViewportBounds;
        public bool IsExternalAudioMuted => inner.IsExternalAudioMuted;
        public bool IsGameplayActive => gameplayActive;
        public bool IsLoadingFinished => loadingFinished;

        public PlatformLifecycleGuard(IGamePlatformBridge inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string LoadAudioPreferences() => inner.LoadAudioPreferences();
        public void SaveAudioPreferences(string value) => inner.SaveAudioPreferences(value);

        public Task InitializeAsync()
        {
            return inner.InitializeAsync();
        }

        public void NotifyLoadingStarted()
        {
            if (loadingStarted || loadingFinished)
            {
                return;
            }

            loadingStarted = true;
            inner.NotifyLoadingStarted();
        }

        public void NotifyLoadingFinished()
        {
            if (loadingFinished)
            {
                return;
            }

            if (!loadingStarted)
            {
                loadingStarted = true;
                inner.NotifyLoadingStarted();
            }

            loadingFinished = true;
            inner.NotifyLoadingFinished();
        }

        public void NotifyGameplayStart()
        {
            if (!loadingFinished)
            {
                NotifyLoadingFinished();
            }

            if (gameplayActive)
            {
                return;
            }

            gameplayActive = true;
            inner.NotifyGameplayStart();
        }

        public void NotifyGameplayStop()
        {
            if (!gameplayActive)
            {
                return;
            }

            gameplayActive = false;
            inner.NotifyGameplayStop();
        }

        public bool TryCopyTextToClipboard(string text)
        {
            return inner.TryCopyTextToClipboard(text);
        }

        public Task<bool> CopyTextToClipboardAsync(string text) => inner.CopyTextToClipboardAsync(text);

        public bool RequestClipboardPaste()
        {
            return inner.RequestClipboardPaste();
        }

        public bool TryConsumePastedText(out string text)
        {
            return inner.TryConsumePastedText(out text);
        }
    }
}
