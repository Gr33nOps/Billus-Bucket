using System;
using System.Threading.Tasks;

namespace BillusBucket
{
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

        Task InitializeAsync();
        void NotifyLoadingStarted();
        void NotifyLoadingFinished();
        void NotifyGameplayStart();
        void NotifyGameplayStop();
    }

    /// <summary>
    /// Default no-op platform bridge used on desktop and standalone builds.
    /// </summary>
    public sealed class NullPlatformBridge : IGamePlatformBridge
    {
        public string PlatformId { get; }
        public bool IsExternalAudioMuted => false;

        public NullPlatformBridge(string platformId = "desktop")
        {
            PlatformId = string.IsNullOrWhiteSpace(platformId) ? "desktop" : platformId;
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
        public bool IsExternalAudioMuted => inner.IsExternalAudioMuted;
        public bool IsGameplayActive => gameplayActive;
        public bool IsLoadingFinished => loadingFinished;

        public PlatformLifecycleGuard(IGamePlatformBridge inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

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
    }
}
