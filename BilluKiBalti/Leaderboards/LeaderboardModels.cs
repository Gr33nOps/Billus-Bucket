using System;
using System.Collections.Generic;
using System.Numerics;

namespace BilluKiBalti
{
    /// <summary>
    /// Describes which leaderboard features are genuinely active on the current platform.
    /// Prevents the UI from claiming global rankings exist when running locally or unapproved.
    /// </summary>
    public sealed class LeaderboardCapabilities
    {
        public string ProviderName { get; }
        public bool SupportsPersonalBest { get; }
        public bool SupportsScoreSubmission { get; }
        public bool SupportsWeeklyTop10 { get; }
        public bool SupportsPlayerRank { get; }
        public bool UsesNativePortalOverlay { get; }

        public LeaderboardCapabilities(
            string providerName,
            bool supportsPersonalBest,
            bool supportsScoreSubmission,
            bool supportsWeeklyTop10,
            bool supportsPlayerRank,
            bool usesNativePortalOverlay = false)
        {
            ProviderName = string.IsNullOrWhiteSpace(providerName) ? "Local" : providerName;
            SupportsPersonalBest = supportsPersonalBest;
            SupportsScoreSubmission = supportsScoreSubmission;
            SupportsWeeklyTop10 = supportsWeeklyTop10;
            SupportsPlayerRank = supportsPlayerRank;
            UsesNativePortalOverlay = usesNativePortalOverlay;
        }
    }

    /// <summary>
    /// One ranked player row on the weekly top-10 leaderboard.
    /// </summary>
    public sealed class LeaderboardRankEntry
    {
        public int Rank { get; }
        public string DisplayName { get; }
        public BigInteger Score { get; }
        public bool IsCurrentPlayer { get; }

        public LeaderboardRankEntry(int rank, string displayName, BigInteger score, bool isCurrentPlayer = false)
        {
            Rank = Math.Max(1, rank);
            DisplayName = LeaderboardStore.NormalizeName(displayName);
            Score = score < BigInteger.Zero ? BigInteger.Zero : score;
            IsCurrentPlayer = isCurrentPlayer;
        }
    }

    /// <summary>
    /// Immutable view of the current leaderboard state rendered by the UI.
    /// </summary>
    public sealed class LeaderboardSnapshot
    {
        public BigInteger PersonalBest { get; }
        public int? CurrentPlayerWeeklyRank { get; }
        public IReadOnlyList<LeaderboardRankEntry> WeeklyTop10 { get; }
        public bool IsLoading { get; }
        public bool IsOnlineAvailable { get; }
        public string StatusMessage { get; }

        public LeaderboardSnapshot(
            BigInteger personalBest,
            int? currentPlayerWeeklyRank = null,
            IReadOnlyList<LeaderboardRankEntry> weeklyTop10 = null,
            bool isLoading = false,
            bool isOnlineAvailable = false,
            string statusMessage = "")
        {
            PersonalBest = personalBest < BigInteger.Zero ? BigInteger.Zero : personalBest;
            CurrentPlayerWeeklyRank = currentPlayerWeeklyRank.HasValue && currentPlayerWeeklyRank.Value > 0
                ? currentPlayerWeeklyRank
                : null;
            WeeklyTop10 = weeklyTop10 ?? Array.Empty<LeaderboardRankEntry>();
            IsLoading = isLoading;
            IsOnlineAvailable = isOnlineAvailable;
            StatusMessage = statusMessage ?? string.Empty;
        }
    }

    /// <summary>
    /// Outcome of submitting a completed run's score.
    /// </summary>
    public sealed class ScoreSubmissionResult
    {
        public bool Accepted { get; }
        public bool WasDuplicateRun { get; }
        public bool IsNewPersonalBest { get; }
        public BigInteger PersonalBest { get; }
        public int? WeeklyRank { get; }
        public bool SubmittedOnline { get; }
        public string Message { get; }

        public ScoreSubmissionResult(
            bool accepted,
            bool wasDuplicateRun,
            bool isNewPersonalBest,
            BigInteger personalBest,
            int? weeklyRank = null,
            bool submittedOnline = false,
            string message = "")
        {
            Accepted = accepted;
            WasDuplicateRun = wasDuplicateRun;
            IsNewPersonalBest = isNewPersonalBest;
            PersonalBest = personalBest < BigInteger.Zero ? BigInteger.Zero : personalBest;
            WeeklyRank = weeklyRank.HasValue && weeklyRank.Value > 0 ? weeklyRank : null;
            SubmittedOnline = submittedOnline;
            Message = message ?? string.Empty;
        }
    }
}
