using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace BilluKiBalti
{
    public sealed class LeaderboardEntry
    {
        public string Name { get; }
        public BigInteger BestCash { get; internal set; }

        public LeaderboardEntry(string name, BigInteger bestCash)
        {
            Name = name;
            BestCash = bestCash;
        }
    }

    /// <summary>
    /// Loads and saves local high scores to a JSON file on disk.
    /// Score updates during gameplay stay in memory until Flush() is called on pause, game over, or exit.
    /// </summary>
    public sealed class LeaderboardStore
    {
        private const string DefaultPlayerName = "PLAYER";
        private const int MaxPlayerNameLength = 12;

        private readonly string filePath;
        private readonly List<LeaderboardEntry> entries;
        private bool hasUnsavedChanges;

        public string LastSaveError { get; private set; }

        public LeaderboardStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Leaderboard file path cannot be empty.", nameof(filePath));
            }

            this.filePath = filePath;

            try
            {
                entries = LoadEntries(filePath);
            }
            catch (JsonException)
            {
                entries = new List<LeaderboardEntry>();
            }
            catch (IOException error)
            {
                LastSaveError = error.Message;
                Debug.WriteLine("Score load failed: " + error.Message);
                entries = new List<LeaderboardEntry>();
            }
            catch (UnauthorizedAccessException error)
            {
                LastSaveError = error.Message;
                Debug.WriteLine("Score load failed: " + error.Message);
                entries = new List<LeaderboardEntry>();
            }
        }

        public static string NormalizeName(string playerName)
        {
            string trimmedUpper = (playerName ?? string.Empty).Trim().ToUpperInvariant();
            StringBuilder cleaned = new StringBuilder();

            foreach (char c in trimmedUpper)
            {
                if (cleaned.Length == MaxPlayerNameLength)
                {
                    break;
                }

                bool isLetter = c >= 'A' && c <= 'Z';
                bool isDigit = c >= '0' && c <= '9';
                if (isLetter || isDigit || c == '_')
                {
                    cleaned.Append(c);
                }
            }

            return cleaned.Length == 0 ? DefaultPlayerName : cleaned.ToString();
        }

        public bool Record(string playerName, BigInteger score, bool saveImmediately = true)
        {
            if (score <= BigInteger.Zero)
            {
                return false;
            }

            string normalizedName = NormalizeName(playerName);
            LeaderboardEntry existing = FindEntry(normalizedName);

            if (existing != null && existing.BestCash >= score)
            {
                return false;
            }

            if (existing == null)
            {
                entries.Add(new LeaderboardEntry(normalizedName, score));
            }
            else
            {
                existing.BestCash = score;
            }

            hasUnsavedChanges = true;
            if (saveImmediately)
            {
                Flush();
            }

            return true;
        }

        /// <summary>
        /// Writes any pending high-score changes to disk using a temporary file first,
        /// so an interrupted write never corrupts the existing save file.
        /// </summary>
        public void Flush()
        {
            if (!hasUnsavedChanges)
            {
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(filePath);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var serializableEntries = entries.Select(entry => new
                {
                    entry.Name,
                    BestCash = entry.BestCash.ToString(CultureInfo.InvariantCulture)
                });

                string json = JsonSerializer.Serialize(serializableEntries);
                string tempFilePath = filePath + ".tmp";
                File.WriteAllText(tempFilePath, json);
                File.Move(tempFilePath, filePath, overwrite: true);

                hasUnsavedChanges = false;
                LastSaveError = null;
            }
            catch (IOException error)
            {
                RememberSaveError(error);
            }
            catch (UnauthorizedAccessException error)
            {
                RememberSaveError(error);
            }
        }

        public BigInteger BestFor(string playerName)
        {
            string normalizedName = NormalizeName(playerName);
            LeaderboardEntry existing = FindEntry(normalizedName);
            return existing != null ? existing.BestCash : BigInteger.Zero;
        }

        public IReadOnlyList<LeaderboardEntry> TopScores(int count)
        {
            int safeCount = Math.Max(0, count);
            return entries
                .OrderByDescending(entry => entry.BestCash)
                .ThenBy(entry => entry.Name)
                .Take(safeCount)
                .Select(entry => new LeaderboardEntry(entry.Name, entry.BestCash))
                .ToList();
        }

        private LeaderboardEntry FindEntry(string normalizedName)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Name == normalizedName)
                {
                    return entries[i];
                }
            }

            return null;
        }

        private void RememberSaveError(Exception error)
        {
            // Keep hasUnsavedChanges = true so we can try saving again at the next pause/game-over/exit.
            LastSaveError = error.Message;
            Debug.WriteLine("Score save failed: " + error.Message);
        }

        private static List<LeaderboardEntry> LoadEntries(string path)
        {
            List<LeaderboardEntry> loaded = new List<LeaderboardEntry>();
            if (!File.Exists(path))
            {
                return loaded;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return loaded;
            }

            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!item.TryGetProperty("Name", out JsonElement nameElement) ||
                    !item.TryGetProperty("BestCash", out JsonElement scoreElement) ||
                    nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string rawScore = scoreElement.ValueKind == JsonValueKind.String
                    ? (scoreElement.GetString() ?? string.Empty)
                    : scoreElement.GetRawText();

                bool isValidScore = BigInteger.TryParse(
                    rawScore,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out BigInteger amount);

                if (!isValidScore || amount <= BigInteger.Zero)
                {
                    continue;
                }

                string normalizedName = NormalizeName(nameElement.GetString() ?? string.Empty);

                // Merge duplicates if two saved names normalize to the same uppercase name.
                LeaderboardEntry duplicate = null;
                for (int i = 0; i < loaded.Count; i++)
                {
                    if (loaded[i].Name == normalizedName)
                    {
                        duplicate = loaded[i];
                        break;
                    }
                }

                if (duplicate == null)
                {
                    loaded.Add(new LeaderboardEntry(normalizedName, amount));
                }
                else if (amount > duplicate.BestCash)
                {
                    duplicate.BestCash = amount;
                }
            }

            return loaded;
        }
    }
}
