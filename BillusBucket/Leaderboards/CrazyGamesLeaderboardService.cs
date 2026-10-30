using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace BillusBucket
{
    /// <summary>
    /// JS interop contract for CrazyGames SDK v3 native weekly leaderboard submission.
    /// CrazyGames allows at most one native weekly leaderboard per invited game,
    /// displayed natively on the CrazyGames web page outside the game canvas.
    /// </summary>
    public interface ICrazyGamesSdkClient
    {
        bool IsSdkInitialized { get; }
        bool IsNativeLeaderboardReady { get; }

        Task<bool> SubmitEncryptedScoreAsync(long score);
    }

    public sealed class NullCrazyGamesSdkClient : ICrazyGamesSdkClient
    {
        public bool IsSdkInitialized => false;
        public bool IsNativeLeaderboardReady => false;

        public Task<bool> SubmitEncryptedScoreAsync(long score)
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// CrazyGames leaderboard adapter following official CrazyGames SDK v3 documentation.
    /// </summary>
    public sealed class CrazyGamesLeaderboardService : ILeaderboardService
    {
        private const long MaxJavaScriptSafeInteger = 9_007_199_254_740_991L;
        private const int MaxRememberedRuns = 256;

        private readonly LocalLeaderboardService localFallback;
        private readonly SharedOnlineLeaderboardService optionalSharedBoard;
        private readonly ICrazyGamesSdkClient sdkClient;
        private readonly bool leaderboardAccessGranted;
        private readonly HashSet<string> submittedRuns = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> submittedRunOrder = new Queue<string>();

        private bool IsNativeActive =>
            leaderboardAccessGranted &&
            sdkClient != null &&
            sdkClient.IsNativeLeaderboardReady;

        public LeaderboardCapabilities Capabilities => BuildCapabilities();

        public LeaderboardSnapshot CurrentSnapshot { get; private set; }

        public string PlayerDisplayName =>
            optionalSharedBoard != null ? optionalSharedBoard.PlayerDisplayName : localFallback.PlayerDisplayName;

        public bool HasCustomPlayerName =>
            optionalSharedBoard != null ? optionalSharedBoard.HasCustomPlayerName : localFallback.HasCustomPlayerName;

        public string PlayerConnectCode =>
            optionalSharedBoard != null ? optionalSharedBoard.PlayerConnectCode : localFallback.PlayerConnectCode;

        public CrazyGamesLeaderboardService(
            ILocalScoreStorage storage,
            ICrazyGamesSdkClient sdkClient,
            bool leaderboardAccessGranted,
            ISharedLeaderboardTransport optionalSharedTransport = null)
        {
            ILocalScoreStorage safeStorage = storage ?? new InMemoryScoreStorage(reportPersistentAvailable: false);
            localFallback = new LocalLeaderboardService(safeStorage);
            this.sdkClient = sdkClient ?? new NullCrazyGamesSdkClient();
            this.leaderboardAccessGranted = leaderboardAccessGranted;

            if (optionalSharedTransport != null)
            {
                optionalSharedBoard = new SharedOnlineLeaderboardService(
                    safeStorage,
                    optionalSharedTransport,
                    sourcePlatform: "crazygames",
                    providerName: "CRAZYGAMES");
            }

            CurrentSnapshot = BuildCurrentSnapshot();
        }

        public async Task InitializeAsync()
        {
            await localFallback.InitializeAsync();
            if (optionalSharedBoard != null)
            {
                await optionalSharedBoard.InitializeAsync();
            }

            CurrentSnapshot = BuildCurrentSnapshot();
        }

        public BigInteger GetPersonalBest()
        {
            BigInteger best = localFallback.GetPersonalBest();
            if (optionalSharedBoard != null)
            {
                best = BigInteger.Max(best, optionalSharedBoard.GetPersonalBest());
            }

            return best;
        }

        public void RecordLivePersonalBest(BigInteger currentScore)
        {
            localFallback.RecordLivePersonalBest(currentScore);
            optionalSharedBoard?.RecordLivePersonalBest(currentScore);
            CurrentSnapshot = BuildCurrentSnapshot();
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
                    personalBest: GetPersonalBest(),
                    message: "INVALID RUN ID");
            }

            if (!RememberRunId(runId))
            {
                return new ScoreSubmissionResult(
                    accepted: false,
                    wasDuplicateRun: true,
                    isNewPersonalBest: false,
                    personalBest: GetPersonalBest(),
                    weeklyRank: CurrentSnapshot.CurrentPlayerWeeklyRank,
                    submittedOnline: false,
                    message: "RUN ALREADY SUBMITTED");
            }

            ScoreSubmissionResult localResult = await localFallback.SubmitRunScoreAsync(
                runId,
                finalScore,
                runDurationSeconds);

            bool submittedNative = false;
            if (finalScore > BigInteger.Zero && IsNativeActive)
            {
                long clampedScore = ClampToJsSafeInteger(finalScore);
                try
                {
                    submittedNative = await sdkClient.SubmitEncryptedScoreAsync(clampedScore);
                }
                catch
                {
                    submittedNative = false;
                }
            }

            ScoreSubmissionResult sharedResult = null;
            if (optionalSharedBoard != null && finalScore > BigInteger.Zero)
            {
                sharedResult = await optionalSharedBoard.SubmitRunScoreAsync(
                    runId,
                    finalScore,
                    runDurationSeconds);
            }

            CurrentSnapshot = BuildCurrentSnapshot(submittedNative);

            return new ScoreSubmissionResult(
                accepted: localResult.Accepted,
                wasDuplicateRun: false,
                isNewPersonalBest: localResult.IsNewPersonalBest || (sharedResult?.IsNewPersonalBest ?? false),
                personalBest: GetPersonalBest(),
                weeklyRank: sharedResult?.WeeklyRank ?? CurrentSnapshot.CurrentPlayerWeeklyRank,
                submittedOnline: submittedNative || (sharedResult?.SubmittedOnline ?? false),
                message: CurrentSnapshot.StatusMessage);
        }

        public async Task<ProfileSyncResult> UpdatePlayerNameAsync(string newDisplayName)
        {
            ProfileSyncResult result = optionalSharedBoard != null
                ? await optionalSharedBoard.UpdatePlayerNameAsync(newDisplayName)
                : await localFallback.UpdatePlayerNameAsync(newDisplayName);
            CurrentSnapshot = BuildCurrentSnapshot();
            return result;
        }

        public async Task<ProfileSyncResult> RestoreProfileByCodeAsync(string connectCode)
        {
            ProfileSyncResult result = optionalSharedBoard != null
                ? await optionalSharedBoard.RestoreProfileByCodeAsync(connectCode)
                : await localFallback.RestoreProfileByCodeAsync(connectCode);
            CurrentSnapshot = BuildCurrentSnapshot();
            return result;
        }

        public async Task<LeaderboardSnapshot> RefreshRankingsAsync()
        {
            await localFallback.RefreshRankingsAsync();
            if (optionalSharedBoard != null)
            {
                await optionalSharedBoard.RefreshRankingsAsync();
            }

            CurrentSnapshot = BuildCurrentSnapshot();
            return CurrentSnapshot;
        }

        public void FlushLocalState()
        {
            localFallback.FlushLocalState();
            optionalSharedBoard?.FlushLocalState();
        }

        private LeaderboardCapabilities BuildCapabilities()
        {
            bool nativeActive = IsNativeActive;
            bool hasSharedTop10 = optionalSharedBoard != null;

            return new LeaderboardCapabilities(
                providerName: nativeActive || hasSharedTop10 ? "CRAZYGAMES" : "LOCAL",
                supportsPersonalBest: true,
                supportsScoreSubmission: nativeActive || hasSharedTop10,
                supportsWeeklyTop10: hasSharedTop10,
                supportsPlayerRank: hasSharedTop10,
                usesNativePortalOverlay: nativeActive);
        }

        private LeaderboardSnapshot BuildCurrentSnapshot(bool? lastNativeSubmitSucceeded = null)
        {
            if (optionalSharedBoard != null)
            {
                LeaderboardSnapshot shared = optionalSharedBoard.CurrentSnapshot;
                return new LeaderboardSnapshot(
                    personalBest: GetPersonalBest(),
                    currentPlayerWeeklyRank: shared.CurrentPlayerWeeklyRank,
                    weeklyTop10: shared.WeeklyTop10,
                    isLoading: shared.IsLoading,
                    isOnlineAvailable: shared.IsOnlineAvailable || IsNativeActive,
                    statusMessage: shared.StatusMessage);
            }

            if (IsNativeActive)
            {
                string nativeMessage = lastNativeSubmitSucceeded == false
                    ? "OFFLINE - SCORE SAVED LOCALLY"
                    : "RANKINGS ON CRAZYGAMES PAGE";

                return new LeaderboardSnapshot(
                    personalBest: localFallback.GetPersonalBest(),
                    currentPlayerWeeklyRank: null,
                    weeklyTop10: Array.Empty<LeaderboardRankEntry>(),
                    isLoading: false,
                    isOnlineAvailable: lastNativeSubmitSucceeded != false,
                    statusMessage: nativeMessage);
            }

            return localFallback.CurrentSnapshot;
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

        private static long ClampToJsSafeInteger(BigInteger value)
        {
            if (value <= BigInteger.Zero)
            {
                return 0L;
            }

            if (value > MaxJavaScriptSafeInteger)
            {
                return MaxJavaScriptSafeInteger;
            }

            return (long)value;
        }
    }
}
