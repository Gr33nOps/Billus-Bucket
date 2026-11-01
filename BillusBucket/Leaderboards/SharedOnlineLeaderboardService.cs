using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace BillusBucket
{
    /// <summary>
    /// Shared weekly online leaderboard & cloud profile adapter backed by Cloudflare Workers + D1 (or Supabase).
    /// Always saves personal best and custom name locally first, syncs to the cloud,
    /// supports cross-device Save Codes, and degrades cleanly to local mode whenever offline.
    /// </summary>
    public sealed class SharedOnlineLeaderboardService : ILeaderboardService
    {
        private const int MaxRememberedRuns = 256;

        private readonly ILocalScoreStorage storage;
        private readonly ISharedLeaderboardTransport transport;
        private readonly string sourcePlatform;
        private readonly HashSet<string> submittedRuns = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> submittedRunOrder = new Queue<string>();

        private long rankingsRevision;
        private BigInteger personalBest;
        private int? currentWeeklyRank;
        private IReadOnlyList<LeaderboardRankEntry> weeklyTop10 = Array.Empty<LeaderboardRankEntry>();
        private bool isOnlineAvailable;
        private string fallbackSessionName;
        private string fallbackSessionCode;
        private bool fallbackHasCustomName;

        public LeaderboardCapabilities Capabilities { get; }

        public LeaderboardSnapshot CurrentSnapshot { get; private set; }

        public string PlayerDisplayName => SafeGetDisplayName();
        public bool HasCustomPlayerName => SafeHasCustomName();
        public string PlayerConnectCode => SafeGetConnectCode();

        public SharedOnlineLeaderboardService(
            ILocalScoreStorage storage,
            ISharedLeaderboardTransport transport,
            string sourcePlatform = "web",
            string providerName = "WEEKLY ONLINE")
        {
            this.storage = storage ?? new InMemoryScoreStorage(reportPersistentAvailable: false);
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.sourcePlatform = string.IsNullOrWhiteSpace(sourcePlatform) ? "web" : sourcePlatform;

            Capabilities = new LeaderboardCapabilities(
                providerName: providerName,
                supportsPersonalBest: true,
                supportsScoreSubmission: true,
                supportsWeeklyTop10: true,
                supportsPlayerRank: true);

            personalBest = SafeLoadPersonalBest();
            CurrentSnapshot = new LeaderboardSnapshot(
                personalBest: personalBest,
                statusMessage: "WEEKLY TOP 10 READY");
        }

        public async Task InitializeAsync()
        {
            personalBest = BigInteger.Max(personalBest, SafeLoadPersonalBest());
            await RefreshRankingsAsync();
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
            CurrentSnapshot = new LeaderboardSnapshot(
                personalBest: personalBest,
                currentPlayerWeeklyRank: currentWeeklyRank,
                weeklyTop10: weeklyTop10,
                isLoading: CurrentSnapshot.IsLoading,
                isOnlineAvailable: isOnlineAvailable,
                statusMessage: CurrentSnapshot.StatusMessage);
        }

        public async Task<ScoreSubmissionResult> SubmitRunScoreAsync(
            string runId,
            BigInteger finalScore,
            float runDurationSeconds = 0f)
        {
            if (string.IsNullOrWhiteSpace(runId))
            {
                return new ScoreSubmissionResult(
                    accepted: false,
                    wasDuplicateRun: false,
                    isNewPersonalBest: false,
                    personalBest: personalBest,
                    weeklyRank: currentWeeklyRank,
                    submittedOnline: false,
                    message: "INVALID RUN ID");
            }

            if (!RememberRunId(runId))
            {
                return new ScoreSubmissionResult(
                    accepted: false,
                    wasDuplicateRun: true,
                    isNewPersonalBest: false,
                    personalBest: personalBest,
                    weeklyRank: currentWeeklyRank,
                    submittedOnline: false,
                    message: "RUN ALREADY SUBMITTED");
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
                return new ScoreSubmissionResult(
                    accepted: false,
                    wasDuplicateRun: false,
                    isNewPersonalBest: false,
                    personalBest: personalBest,
                    weeklyRank: currentWeeklyRank,
                    submittedOnline: false,
                    message: "ZERO SCORE NOT RANKED");
            }

            SharedScoreSubmissionPayload payload = new SharedScoreSubmissionPayload(
                runId: runId,
                anonymousPlayerId: SafeGetPlayerId(),
                displayName: SafeGetDisplayName(),
                score: finalScore,
                runDurationSeconds: runDurationSeconds,
                sourcePlatform: sourcePlatform,
                connectCode: SafeGetConnectCode());

            SharedLeaderboardRemoteResponse remote = await transport.SubmitScoreAsync(payload);
            if (remote.Success)
            {
                ApplyRemoteProfileSyncIfPresent(remote);
                isOnlineAvailable = true;
                currentWeeklyRank = remote.PlayerWeeklyRank ?? currentWeeklyRank;
                if (remote.Top10.Count > 0)
                {
                    weeklyTop10 = remote.Top10;
                }

                string message = weeklyTop10.Count > 0
                    ? "WEEKLY TOP 10"
                    : "NO WEEKLY SCORES YET";

                CurrentSnapshot = new LeaderboardSnapshot(
                    personalBest: personalBest,
                    currentPlayerWeeklyRank: currentWeeklyRank,
                    weeklyTop10: weeklyTop10,
                    isLoading: false,
                    isOnlineAvailable: true,
                    statusMessage: message);

                return new ScoreSubmissionResult(
                    accepted: true,
                    wasDuplicateRun: false,
                    isNewPersonalBest: isNewBest,
                    personalBest: personalBest,
                    weeklyRank: currentWeeklyRank,
                    submittedOnline: true,
                    message: message);
            }

            isOnlineAvailable = false;
            string fallbackStatus = string.IsNullOrWhiteSpace(remote.ErrorMessage)
                ? "OFFLINE - SCORE SAVED LOCALLY"
                : remote.ErrorMessage;

            CurrentSnapshot = new LeaderboardSnapshot(
                personalBest: personalBest,
                currentPlayerWeeklyRank: currentWeeklyRank,
                weeklyTop10: weeklyTop10,
                isLoading: false,
                isOnlineAvailable: false,
                statusMessage: fallbackStatus);

            return new ScoreSubmissionResult(
                accepted: true,
                wasDuplicateRun: false,
                isNewPersonalBest: isNewBest,
                personalBest: personalBest,
                weeklyRank: currentWeeklyRank,
                submittedOnline: false,
                message: fallbackStatus);
        }

        public async Task<ProfileSyncResult> UpdatePlayerNameAsync(string newDisplayName)
        {
            if (!PlayerIdentityValidator.TryValidateCustomName(newDisplayName, out string validName, out string error))
            {
                return new ProfileSyncResult(
                    accepted: false,
                    syncedOnline: false,
                    displayName: PlayerDisplayName,
                    personalBest: personalBest,
                    weeklyRank: currentWeeklyRank,
                    message: error);
            }

            rankingsRevision++; // Invalidate refreshes started before this name change.
            fallbackSessionName = validName;
            fallbackHasCustomName = true;
            try
            {
                storage.SaveCustomDisplayName(validName);
            }
            catch
            {
            }

            SharedProfileSavePayload payload = new SharedProfileSavePayload(
                anonymousPlayerId: SafeGetPlayerId(),
                connectCode: SafeGetConnectCode(),
                displayName: validName,
                personalBest: personalBest,
                sourcePlatform: sourcePlatform);

            SharedLeaderboardRemoteResponse remote = await transport.SaveProfileAsync(payload);
            if (remote.Success)
            {
                rankingsRevision++;
                ApplyRemoteProfileSyncIfPresent(remote);
                isOnlineAvailable = true;
                currentWeeklyRank = remote.PlayerWeeklyRank ?? currentWeeklyRank;
                weeklyTop10 = remote.Top10;

                string status = weeklyTop10.Count > 0 ? "WEEKLY TOP 10" : "NO WEEKLY SCORES YET";
                CurrentSnapshot = new LeaderboardSnapshot(
                    personalBest: personalBest,
                    currentPlayerWeeklyRank: currentWeeklyRank,
                    weeklyTop10: weeklyTop10,
                    isLoading: false,
                    isOnlineAvailable: true,
                    statusMessage: status);

                return new ProfileSyncResult(
                    accepted: true,
                    syncedOnline: true,
                    displayName: validName,
                    personalBest: personalBest,
                    weeklyRank: currentWeeklyRank,
                    message: "SAVED TO CLOUD AS " + validName);
            }

            rankingsRevision++;
            isOnlineAvailable = false;
            CurrentSnapshot = new LeaderboardSnapshot(
                personalBest: personalBest,
                currentPlayerWeeklyRank: currentWeeklyRank,
                weeklyTop10: weeklyTop10,
                isLoading: false,
                isOnlineAvailable: false,
                statusMessage: "OFFLINE - NAME SAVED ON DEVICE");

            return new ProfileSyncResult(
                accepted: true,
                syncedOnline: false,
                displayName: validName,
                personalBest: personalBest,
                weeklyRank: currentWeeklyRank,
                message: string.IsNullOrWhiteSpace(remote.ErrorMessage) ? "SAVED ON DEVICE" : remote.ErrorMessage);
        }

        public async Task<ProfileSyncResult> RestoreProfileByCodeAsync(string connectCode)
        {
            if (!PlayerIdentityValidator.TryNormalizeConnectCode(connectCode, out string formattedCode))
            {
                return new ProfileSyncResult(
                    accepted: false,
                    syncedOnline: false,
                    displayName: PlayerDisplayName,
                    personalBest: personalBest,
                    weeklyRank: currentWeeklyRank,
                    message: "ENTER 12-CHAR SAVE CODE");
            }

            SharedLeaderboardRemoteResponse remote = await transport.RestoreProfileAsync(formattedCode, sourcePlatform);
            if (!remote.Success)
            {
                string err = string.IsNullOrWhiteSpace(remote.ErrorMessage)
                    ? "SAVE CODE NOT FOUND"
                    : remote.ErrorMessage;
                return new ProfileSyncResult(
                    accepted: false,
                    syncedOnline: false,
                    displayName: PlayerDisplayName,
                    personalBest: personalBest,
                    weeklyRank: currentWeeklyRank,
                    message: err);
            }

            string restoredName = !string.IsNullOrWhiteSpace(remote.ProfileDisplayName)
                ? remote.ProfileDisplayName
                : PlayerDisplayName;
            BigInteger restoredBest = remote.ProfilePersonalBest.HasValue
                ? BigInteger.Max(personalBest, remote.ProfilePersonalBest.Value)
                : personalBest;

            rankingsRevision++; // The active identity has changed.
            fallbackSessionCode = formattedCode;
            fallbackSessionName = restoredName;
            fallbackHasCustomName = !AnonymousPlayerIdentity.IsAutoGeneratedName(restoredName);
            personalBest = restoredBest;

            try
            {
                storage.ApplyRestoredProfile(formattedCode, restoredName, restoredBest);
            }
            catch
            {
            }

            isOnlineAvailable = true;
            currentWeeklyRank = remote.PlayerWeeklyRank;
            weeklyTop10 = remote.Top10;

            string status = weeklyTop10.Count > 0 ? "WEEKLY TOP 10" : "NO WEEKLY SCORES YET";
            CurrentSnapshot = new LeaderboardSnapshot(
                personalBest: personalBest,
                currentPlayerWeeklyRank: currentWeeklyRank,
                weeklyTop10: weeklyTop10,
                isLoading: false,
                isOnlineAvailable: true,
                statusMessage: status);

            return new ProfileSyncResult(
                accepted: true,
                syncedOnline: true,
                displayName: restoredName,
                personalBest: personalBest,
                weeklyRank: currentWeeklyRank,
                message: "CONNECTED AS " + restoredName);
        }

        public async Task<LeaderboardSnapshot> RefreshRankingsAsync()
        {
            long requestRevision = ++rankingsRevision;
            personalBest = BigInteger.Max(personalBest, SafeLoadPersonalBest());
            CurrentSnapshot = new LeaderboardSnapshot(
                personalBest: personalBest,
                currentPlayerWeeklyRank: currentWeeklyRank,
                weeklyTop10: weeklyTop10,
                isLoading: true,
                isOnlineAvailable: isOnlineAvailable,
                statusMessage: "LOADING WEEKLY TOP 10...");

            SharedLeaderboardRemoteResponse remote = await transport.FetchWeeklyTop10Async(
                SafeGetPlayerId(),
                sourcePlatform);

            if (requestRevision != rankingsRevision)
            {
                return CurrentSnapshot;
            }

            if (remote.Success)
            {
                ApplyRemoteProfileSyncIfPresent(remote);
                isOnlineAvailable = true;
                currentWeeklyRank = remote.PlayerWeeklyRank;
                weeklyTop10 = remote.Top10;

                string status = weeklyTop10.Count > 0
                    ? "WEEKLY TOP 10"
                    : "NO WEEKLY SCORES YET";

                CurrentSnapshot = new LeaderboardSnapshot(
                    personalBest: personalBest,
                    currentPlayerWeeklyRank: currentWeeklyRank,
                    weeklyTop10: weeklyTop10,
                    isLoading: false,
                    isOnlineAvailable: true,
                    statusMessage: status);
            }
            else
            {
                isOnlineAvailable = false;
                string status = string.IsNullOrWhiteSpace(remote.ErrorMessage)
                    ? "OFFLINE - LOCAL BEST ACTIVE"
                    : remote.ErrorMessage;

                CurrentSnapshot = new LeaderboardSnapshot(
                    personalBest: personalBest,
                    currentPlayerWeeklyRank: null,
                    weeklyTop10: weeklyTop10,
                    isLoading: false,
                    isOnlineAvailable: false,
                    statusMessage: status);
            }

            return CurrentSnapshot;
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

        private void ApplyRemoteProfileSyncIfPresent(SharedLeaderboardRemoteResponse remote)
        {
            if (remote.ProfilePersonalBest.HasValue && remote.ProfilePersonalBest.Value > personalBest)
            {
                personalBest = remote.ProfilePersonalBest.Value;
                SafeSavePersonalBest(personalBest, flushImmediately: true);
            }

            if (!string.IsNullOrWhiteSpace(remote.ProfileDisplayName) &&
                !SafeHasCustomName() &&
                !AnonymousPlayerIdentity.IsAutoGeneratedName(remote.ProfileDisplayName))
            {
                fallbackSessionName = remote.ProfileDisplayName;
                fallbackHasCustomName = true;
                try
                {
                    storage.SaveCustomDisplayName(remote.ProfileDisplayName);
                }
                catch
                {
                }
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

        private string SafeGetPlayerId()
        {
            try
            {
                return storage.GetOrCreateAnonymousPlayerId();
            }
            catch
            {
                return SafeGetConnectCode();
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
    }
}
