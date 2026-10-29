using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace BillusBucket
{
    /// <summary>
    /// Default/offline leaderboard adapter.
    /// Tracks personal best locally, tolerates restricted storage, and never fabricates global rankings.
    /// </summary>
    public sealed class LocalLeaderboardService : ILeaderboardService
    {
        private const int MaxRememberedRuns = 256;

        private readonly ILocalScoreStorage storage;
        private readonly HashSet<string> submittedRuns = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> submittedRunOrder = new Queue<string>();
        private BigInteger personalBest;

        public LeaderboardCapabilities Capabilities { get; } = new LeaderboardCapabilities(
            providerName: "LOCAL",
            supportsPersonalBest: true,
            supportsScoreSubmission: false,
            supportsWeeklyTop10: false,
            supportsPlayerRank: false);

        public LeaderboardSnapshot CurrentSnapshot { get; private set; }

        public LocalLeaderboardService(ILocalScoreStorage storage)
        {
            this.storage = storage ?? new InMemoryScoreStorage(reportPersistentAvailable: false);
            personalBest = SafeLoadPersonalBest();
            CurrentSnapshot = BuildSnapshot();
        }

        public Task InitializeAsync()
        {
            personalBest = BigInteger.Max(personalBest, SafeLoadPersonalBest());
            CurrentSnapshot = BuildSnapshot();
            return Task.CompletedTask;
        }

        public BigInteger GetPersonalBest()
        {
            return personalBest;
        }

        public void RecordLivePersonalBest(BigInteger currentScore)
        {
            if (currentScore <= personalBest)
            {
                return;
            }

            personalBest = currentScore;
            SafeSavePersonalBest(personalBest, flushImmediately: false);
            CurrentSnapshot = BuildSnapshot();
        }

        public Task<ScoreSubmissionResult> SubmitRunScoreAsync(
            string runId,
            BigInteger finalScore,
            float runDurationSeconds = 0f)
        {
            if (string.IsNullOrWhiteSpace(runId))
            {
                return Task.FromResult(new ScoreSubmissionResult(
                    accepted: false,
                    wasDuplicateRun: false,
                    isNewPersonalBest: false,
                    personalBest: personalBest,
                    message: "INVALID RUN ID"));
            }

            if (!RememberRunId(runId))
            {
                return Task.FromResult(new ScoreSubmissionResult(
                    accepted: false,
                    wasDuplicateRun: true,
                    isNewPersonalBest: false,
                    personalBest: personalBest,
                    message: "RUN ALREADY RECORDED"));
            }

            bool isNewBest = false;
            if (finalScore > personalBest)
            {
                personalBest = finalScore;
                isNewBest = true;
            }

            if (finalScore > BigInteger.Zero)
            {
                SafeSavePersonalBest(personalBest, flushImmediately: true);
            }
            else
            {
                FlushLocalState();
            }

            CurrentSnapshot = BuildSnapshot();

            return Task.FromResult(new ScoreSubmissionResult(
                accepted: finalScore > BigInteger.Zero,
                wasDuplicateRun: false,
                isNewPersonalBest: isNewBest,
                personalBest: personalBest,
                weeklyRank: null,
                submittedOnline: false,
                message: CurrentSnapshot.StatusMessage));
        }

        public Task<LeaderboardSnapshot> RefreshRankingsAsync()
        {
            personalBest = BigInteger.Max(personalBest, SafeLoadPersonalBest());
            CurrentSnapshot = BuildSnapshot();
            return Task.FromResult(CurrentSnapshot);
        }

        public void FlushLocalState()
        {
            try
            {
                storage.Flush();
            }
            catch
            {
                // Restricted storage must never interrupt gameplay.
            }
        }

        private bool RememberRunId(string runId)
        {
            if (!submittedRuns.Add(runId))
            {
                return false;
            }

            submittedRunOrder.Enqueue(runId);
            while (submittedRunOrder.Count > MaxRememberedRuns)
            {
                string oldest = submittedRunOrder.Dequeue();
                submittedRuns.Remove(oldest);
            }

            return true;
        }

        private BigInteger SafeLoadPersonalBest()
        {
            try
            {
                BigInteger loaded = storage.LoadPersonalBest();
                return loaded < BigInteger.Zero ? BigInteger.Zero : loaded;
            }
            catch
            {
                return personalBest;
            }
        }

        private void SafeSavePersonalBest(BigInteger score, bool flushImmediately)
        {
            try
            {
                storage.SavePersonalBest(score, flushImmediately);
            }
            catch
            {
                // Keep score in memory for the active session when storage is blocked.
            }
        }

        private LeaderboardSnapshot BuildSnapshot()
        {
            bool persistent = false;
            try
            {
                persistent = storage.IsPersistentStorageAvailable;
            }
            catch
            {
                persistent = false;
            }

            string status = persistent
                ? "PERSONAL BEST SAVED ON DEVICE"
                : "SESSION ONLY - STORAGE RESTRICTED";

            return new LeaderboardSnapshot(
                personalBest: personalBest,
                currentPlayerWeeklyRank: null,
                weeklyTop10: Array.Empty<LeaderboardRankEntry>(),
                isLoading: false,
                isOnlineAvailable: false,
                statusMessage: status);
        }
    }
}
