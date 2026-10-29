using System.Numerics;
using System.Threading.Tasks;

namespace BillusBucket
{
    /// <summary>
    /// Platform-independent leaderboard contract used by BillusBucketGame.
    /// Implementations exist for Local/Default, CrazyGames, Poki, and Shared Online backends.
    /// </summary>
    public interface ILeaderboardService
    {
        LeaderboardCapabilities Capabilities { get; }
        LeaderboardSnapshot CurrentSnapshot { get; }

        Task InitializeAsync();

        BigInteger GetPersonalBest();

        /// <summary>
        /// Updates local personal-best tracking immediately during a run (deferred flush)
        /// without submitting unfinished runs to any online leaderboard.
        /// </summary>
        void RecordLivePersonalBest(BigInteger currentScore);

        /// <summary>
        /// Submits the final pre-explosion run score at most once per unique runId.
        /// Never throws when offline or when a portal feature is unavailable.
        /// </summary>
        Task<ScoreSubmissionResult> SubmitRunScoreAsync(
            string runId,
            BigInteger finalScore,
            float runDurationSeconds = 0f);

        /// <summary>
        /// Refreshes the weekly top-10 rankings and player rank if supported by the active provider.
        /// </summary>
        Task<LeaderboardSnapshot> RefreshRankingsAsync();

        /// <summary>
        /// Flushes any pending local personal-best changes to persistent storage.
        /// </summary>
        void FlushLocalState();
    }
}
