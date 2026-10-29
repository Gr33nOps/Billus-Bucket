using System;
using System.Net.Http;

namespace BillusBucket
{
    /// <summary>
    /// Selects the policy-compliant ILeaderboardService for the active publishing platform.
    /// </summary>
    public static class LeaderboardServiceFactory
    {
        public static ILeaderboardService Create(
            PlatformRuntimeConfig config,
            ILocalScoreStorage localStorage,
            ICrazyGamesSdkClient crazyGamesSdkClient = null,
            HttpClient httpClient = null)
        {
            PlatformRuntimeConfig safeConfig = config ?? new PlatformRuntimeConfig();
            ILocalScoreStorage safeStorage = localStorage ?? new InMemoryScoreStorage(reportPersistentAvailable: false);
            string platform = NormalizePlatform(safeConfig.ActivePlatform);

            ISharedLeaderboardTransport sharedTransport = CreateSharedTransportIfConfigured(
                safeConfig,
                httpClient);

            if (platform == "poki")
            {
                // Poki CSP rule: only attach external transport if explicitly approved AND configured.
                ISharedLeaderboardTransport pokiTransport =
                    safeConfig.PokiApprovedExternalLeaderboardEnabled ? sharedTransport : null;

                return new PokiLeaderboardService(
                    safeStorage,
                    isApprovedByPokiCsp: safeConfig.PokiApprovedExternalLeaderboardEnabled && pokiTransport != null,
                    approvedExternalTransport: pokiTransport);
            }

            if (platform == "crazygames")
            {
                return new CrazyGamesLeaderboardService(
                    safeStorage,
                    crazyGamesSdkClient ?? new NullCrazyGamesSdkClient(),
                    leaderboardAccessGranted: safeConfig.CrazyGamesLeaderboardEnabled,
                    optionalSharedTransport: sharedTransport);
            }

            if (sharedTransport != null)
            {
                return new SharedOnlineLeaderboardService(
                    safeStorage,
                    sharedTransport,
                    sourcePlatform: platform,
                    providerName: "WEEKLY ONLINE");
            }

            return new LocalLeaderboardService(safeStorage);
        }

        public static string NormalizePlatform(string rawPlatform)
        {
            string value = (rawPlatform ?? string.Empty).Trim().ToLowerInvariant();
            if (value == "crazygames" || value == "crazy")
            {
                return "crazygames";
            }

            if (value == "poki")
            {
                return "poki";
            }

            if (value == "web" || value == "standalone")
            {
                return "web";
            }

            if (value == "desktop")
            {
                return "desktop";
            }

            return "local";
        }

        private static ISharedLeaderboardTransport CreateSharedTransportIfConfigured(
            PlatformRuntimeConfig config,
            HttpClient httpClient)
        {
            if (!config.SharedLeaderboardEnabled ||
                string.IsNullOrWhiteSpace(config.SharedLeaderboardEndpointUrl) ||
                httpClient == null)
            {
                return null;
            }

            return new HttpSharedLeaderboardTransport(
                httpClient,
                config.SharedLeaderboardEndpointUrl,
                config.SharedLeaderboardPublicKey);
        }
    }
}
