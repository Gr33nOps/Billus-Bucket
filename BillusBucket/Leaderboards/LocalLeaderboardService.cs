using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace BillusBucket
{
    /// <summary>
    /// Default/offline leaderboard adapter.
    /// Tracks personal best and custom player name locally, tolerates restricted storage,
    /// and never fabricates global rankings.
    /// </summary>
    public sealed class LocalLeaderboardService : ILeaderboardService
    {
        private const int MaxRememberedRuns = 256;

        private readonly ILocalScoreStorage storage;
        private readonly HashSet<string> submittedRuns = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> submittedRunOrder = new Queue<string>();
        private BigInteger personalBest;
        private string fallbackSessionName;
        private string fallbackSessionCode;
        private bool fallbackHasCustomName;

        public LeaderboardCapabilities Capabilities { get; } = new LeaderboardCapabilities(
            providerName: "LOCAL",
            supportsPersonalBest: true,
            supportsScoreSubmission: false,
            supportsWeeklyTop10: false,
            supportsPlayerRank: false);

        public LeaderboardSnapshot CurrentSnapshot { get; private set; }

        public string PlayerDisplayName => SafeGetDisplayName();
        public bool HasCustomPlayerName => SafeHasCustomName();
        public string PlayerConnectCode => SafeGetConnectCode();

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

        public Task<ProfileSyncResult> UpdatePlayerNameAsync(string newDisplayName)
        {
            if (!PlayerIdentityValidator.TryValidateCustomName(newDisplayName, out string validName, out string error))
            {
                return Task.FromResult(new ProfileSyncResult(
                    accepted: false,
                    syncedOnline: false,
                    displayName: PlayerDisplayName,
                    personalBest: personalBest,
                    message: error));
            }

            fallbackSessionName = validName;
            fallbackHasCustomName = true;
            try
            {
                storage.SaveCustomDisplayName(validName);
            }
            catch
            {
            }

            CurrentSnapshot = BuildSnapshot();
            return Task.FromResult(new ProfileSyncResult(
                accepted: true,
                syncedOnline: false,
                displayName: validName,
                personalBest: personalBest,
                message: "NAME SAVED ON DEVICE"));
        }

        public Task<ProfileSyncResult> RestoreProfileByCodeAsync(string connectCode)
        {
            if (!PlayerIdentityValidator.TryNormalizeConnectCode(connectCode, out _))
            {
                return Task.FromResult(new ProfileSyncResult(
                    accepted: false,
                    syncedOnline: false,
                    displayName: PlayerDisplayName,
                    personalBest: personalBest,
                    message: "ENTER 12-CHAR SAVE CODE"));
            }

            return Task.FromResult(new ProfileSyncResult(
                accepted: false,
                syncedOnline: false,
                displayName: PlayerDisplayName,
                personalBest: personalBest,
                message: "CLOUD SYNC OFFLINE"));
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
            }
        }

        private string SafeGetDisplayName()
        {
            try
            {
                return storage.GetOrCreateSafeDisplayName();
            }
            catch
            {
                if (string.IsNullOrWhiteSpace(fallbackSessionName))
                {
                    fallbackSessionName = "BILLU_1000";
                }
                return fallbackSessionName;
            }
        }

        private bool SafeHasCustomName()
        {
            try
            {
                return storage.HasCustomDisplayName;
            }
            catch
            {
                return fallbackHasCustomName;
            }
        }

        private string SafeGetConnectCode()
        {
            try
            {
                return storage.GetOrCreateConnectCode();
            }
            catch
            {
                if (string.IsNullOrWhiteSpace(fallbackSessionCode))
                {
                    fallbackSessionCode = PlayerIdentityValidator.CreateConnectCode();
                }
                return fallbackSessionCode;
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
