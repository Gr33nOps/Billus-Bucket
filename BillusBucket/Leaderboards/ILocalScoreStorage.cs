using System;
using System.Globalization;
using System.Numerics;

namespace BillusBucket
{
    /// <summary>
    /// Abstraction for saving the player's personal-best score and anonymous player identity locally.
    /// Desktop uses LeaderboardStore on disk; Browser uses safe localStorage with in-memory fallback.
    /// </summary>
    public interface ILocalScoreStorage
    {
        bool IsPersistentStorageAvailable { get; }
        BigInteger LoadPersonalBest();
        void SavePersonalBest(BigInteger bestScore, bool flushImmediately = true);
        string GetOrCreateAnonymousPlayerId();
        string GetOrCreateSafeDisplayName();
        void Flush();
    }

    /// <summary>
    /// Pure in-memory storage used for automated tests and when browser storage is completely blocked.
    /// </summary>
    public sealed class InMemoryScoreStorage : ILocalScoreStorage
    {
        private BigInteger personalBest;
        private string playerId;
        private string displayName;

        public bool IsPersistentStorageAvailable { get; }

        public InMemoryScoreStorage(BigInteger initialBest = default, bool reportPersistentAvailable = true)
        {
            personalBest = initialBest < BigInteger.Zero ? BigInteger.Zero : initialBest;
            IsPersistentStorageAvailable = reportPersistentAvailable;
        }

        public BigInteger LoadPersonalBest()
        {
            return personalBest;
        }

        public void SavePersonalBest(BigInteger bestScore, bool flushImmediately = true)
        {
            if (bestScore > personalBest)
            {
                personalBest = bestScore;
            }
        }

        public string GetOrCreateAnonymousPlayerId()
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                playerId = Guid.NewGuid().ToString("N");
            }

            return playerId;
        }

        public string GetOrCreateSafeDisplayName()
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = AnonymousPlayerIdentity.CreateSafeDisplayName(GetOrCreateAnonymousPlayerId());
            }

            return displayName;
        }

        public void Flush()
        {
        }
    }

    /// <summary>
    /// Desktop adapter wrapping the existing atomic-write LeaderboardStore JSON file.
    /// </summary>
    public sealed class DesktopScoreStorage : ILocalScoreStorage
    {
        private const string PlayerSlotName = "PLAYER";
        private const string IdentityFileName = "player-id.txt";

        private readonly LeaderboardStore store;
        private readonly string identityFilePath;
        private string cachedPlayerId;
        private string cachedDisplayName;

        public bool IsPersistentStorageAvailable => store.LastSaveError == null;

        public DesktopScoreStorage(LeaderboardStore store, string identityDirectory = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (!string.IsNullOrWhiteSpace(identityDirectory))
            {
                identityFilePath = System.IO.Path.Combine(identityDirectory, IdentityFileName);
            }
        }

        public BigInteger LoadPersonalBest()
        {
            return store.BestFor(PlayerSlotName);
        }

        public void SavePersonalBest(BigInteger bestScore, bool flushImmediately = true)
        {
            store.Record(PlayerSlotName, bestScore, saveImmediately: flushImmediately);
        }

        public string GetOrCreateAnonymousPlayerId()
        {
            if (!string.IsNullOrWhiteSpace(cachedPlayerId))
            {
                return cachedPlayerId;
            }

            if (!string.IsNullOrWhiteSpace(identityFilePath))
            {
                try
                {
                    if (System.IO.File.Exists(identityFilePath))
                    {
                        string existing = System.IO.File.ReadAllText(identityFilePath).Trim();
                        if (existing.Length >= 16)
                        {
                            cachedPlayerId = existing;
                            return cachedPlayerId;
                        }
                    }

                    string created = Guid.NewGuid().ToString("N");
                    string directory = System.IO.Path.GetDirectoryName(identityFilePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        System.IO.Directory.CreateDirectory(directory);
                    }

                    System.IO.File.WriteAllText(identityFilePath, created);
                    cachedPlayerId = created;
                    return cachedPlayerId;
                }
                catch
                {
                    // Fall back to a session-only anonymous ID if the disk path is read-only.
                }
            }

            cachedPlayerId = Guid.NewGuid().ToString("N");
            return cachedPlayerId;
        }

        public string GetOrCreateSafeDisplayName()
        {
            if (string.IsNullOrWhiteSpace(cachedDisplayName))
            {
                cachedDisplayName = AnonymousPlayerIdentity.CreateSafeDisplayName(GetOrCreateAnonymousPlayerId());
            }

            return cachedDisplayName;
        }

        public void Flush()
        {
            store.Flush();
        }
    }

    /// <summary>
    /// Generates safe, non-personal, portal-friendly display tags (e.g. BILLU_4821)
    /// without requiring accounts, logins, or unmoderated text input.
    /// </summary>
    public static class AnonymousPlayerIdentity
    {
        public static string CreateSafeDisplayName(string anonymousId)
        {
            string source = string.IsNullOrWhiteSpace(anonymousId) ? "0000" : anonymousId;
            uint hash = 2166136261u;
            for (int i = 0; i < source.Length; i++)
            {
                hash ^= source[i];
                hash *= 16777619u;
            }

            int suffix = (int)(hash % 9000u) + 1000;
            return "BILLU_" + suffix.ToString(CultureInfo.InvariantCulture);
        }
    }
}
