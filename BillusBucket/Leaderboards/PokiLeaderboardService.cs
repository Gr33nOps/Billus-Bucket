using System.Numerics;
using System.Threading.Tasks;

namespace BillusBucket
{
    /// <summary>
    /// Poki leaderboard adapter following Poki's External Resources Policy:
    /// https://developers.poki.com/guide/external-resources-policy
    /// - Poki has no native leaderboard SDK API and blocks unapproved external requests via CSP.
    /// - Makes ZERO external network calls unless Poki CSP + Privacy Policy approval is explicitly enabled.
    /// - Never prompts for accounts or logins, and remains playable offline with local personal-best scores.
    /// </summary>
    public sealed class PokiLeaderboardService : ILeaderboardService
    {
        private readonly ILeaderboardService activeDelegate;

        public bool IsExternalBackendEnabled { get; }

        public LeaderboardCapabilities Capabilities => activeDelegate.Capabilities;

        public LeaderboardSnapshot CurrentSnapshot => activeDelegate.CurrentSnapshot;

        public string PlayerDisplayName => activeDelegate.PlayerDisplayName;
        public bool HasCustomPlayerName => activeDelegate.HasCustomPlayerName;
        public string PlayerConnectCode => activeDelegate.PlayerConnectCode;

        public PokiLeaderboardService(
            ILocalScoreStorage storage,
            bool isApprovedByPokiCsp,
            ISharedLeaderboardTransport approvedExternalTransport = null)
        {
            ILocalScoreStorage safeStorage = storage ?? new InMemoryScoreStorage(reportPersistentAvailable: false);

            if (isApprovedByPokiCsp && approvedExternalTransport != null)
            {
                IsExternalBackendEnabled = true;
                activeDelegate = new SharedOnlineLeaderboardService(
                    safeStorage,
                    approvedExternalTransport,
                    sourcePlatform: "poki",
                    providerName: "POKI WEEKLY");
            }
            else
            {
                IsExternalBackendEnabled = false;
                activeDelegate = new LocalLeaderboardService(safeStorage);
            }
        }

        public Task InitializeAsync()
        {
            return activeDelegate.InitializeAsync();
        }

        public BigInteger GetPersonalBest()
        {
            return activeDelegate.GetPersonalBest();
        }

        public void RecordLivePersonalBest(BigInteger currentScore)
        {
            activeDelegate.RecordLivePersonalBest(currentScore);
        }

        public Task<ScoreSubmissionResult> SubmitRunScoreAsync(
            string runId,
            BigInteger finalScore,
            float runDurationSeconds = 0f)
        {
            return activeDelegate.SubmitRunScoreAsync(runId, finalScore, runDurationSeconds);
        }

        public Task<ProfileSyncResult> UpdatePlayerNameAsync(string newDisplayName)
        {
            return activeDelegate.UpdatePlayerNameAsync(newDisplayName);
        }

        public Task<ProfileSyncResult> RestoreProfileByCodeAsync(string connectCode)
        {
            return activeDelegate.RestoreProfileByCodeAsync(connectCode);
        }

        public Task<LeaderboardSnapshot> RefreshRankingsAsync()
        {
            return activeDelegate.RefreshRankingsAsync();
        }

        public void FlushLocalState()
        {
            activeDelegate.FlushLocalState();
        }
    }
}
