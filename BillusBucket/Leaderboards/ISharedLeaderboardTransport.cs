using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BillusBucket
{
    public sealed class SharedScoreSubmissionPayload
    {
        public string RunId { get; }
        public string AnonymousPlayerId { get; }
        public string DisplayName { get; }
        public BigInteger Score { get; }
        public float RunDurationSeconds { get; }
        public string SourcePlatform { get; }

        public SharedScoreSubmissionPayload(
            string runId,
            string anonymousPlayerId,
            string displayName,
            BigInteger score,
            float runDurationSeconds,
            string sourcePlatform)
        {
            RunId = runId ?? string.Empty;
            AnonymousPlayerId = anonymousPlayerId ?? string.Empty;
            DisplayName = LeaderboardStore.NormalizeName(displayName);
            Score = score < BigInteger.Zero ? BigInteger.Zero : score;
            RunDurationSeconds = float.IsFinite(runDurationSeconds) && runDurationSeconds >= 0f
                ? runDurationSeconds
                : 0f;
            SourcePlatform = string.IsNullOrWhiteSpace(sourcePlatform) ? "web" : sourcePlatform;
        }
    }

    public sealed class SharedLeaderboardRemoteResponse
    {
        public bool Success { get; }
        public int? PlayerWeeklyRank { get; }
        public IReadOnlyList<LeaderboardRankEntry> Top10 { get; }
        public string ErrorMessage { get; }

        public SharedLeaderboardRemoteResponse(
            bool success,
            int? playerWeeklyRank = null,
            IReadOnlyList<LeaderboardRankEntry> top10 = null,
            string errorMessage = "")
        {
            Success = success;
            PlayerWeeklyRank = playerWeeklyRank.HasValue && playerWeeklyRank.Value > 0
                ? playerWeeklyRank
                : null;
            Top10 = top10 ?? Array.Empty<LeaderboardRankEntry>();
            ErrorMessage = errorMessage ?? string.Empty;
        }
    }

    /// <summary>
    /// Network transport abstraction for the optional shared weekly leaderboard backend.
    /// Allows unit testing Poki/Shared adapters with zero real network sockets.
    /// </summary>
    public interface ISharedLeaderboardTransport
    {
        Task<SharedLeaderboardRemoteResponse> FetchWeeklyTop10Async(
            string anonymousPlayerId,
            string sourcePlatform);

        Task<SharedLeaderboardRemoteResponse> SubmitScoreAsync(
            SharedScoreSubmissionPayload payload);
    }

    /// <summary>
    /// Trimmer-safe HTTP JSON client for the Supabase Edge Function weekly leaderboard API.
    /// Uses only public anon headers (never private service_role keys) and a short 4-second timeout.
    /// </summary>
    public sealed class HttpSharedLeaderboardTransport : ISharedLeaderboardTransport
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);

        private readonly HttpClient httpClient;
        private readonly string endpointUrl;
        private readonly string publicAnonKey;

        public HttpSharedLeaderboardTransport(
            HttpClient httpClient,
            string endpointUrl,
            string publicAnonKey = "")
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.endpointUrl = (endpointUrl ?? string.Empty).Trim().TrimEnd('/');
            this.publicAnonKey = (publicAnonKey ?? string.Empty).Trim();
        }

        public async Task<SharedLeaderboardRemoteResponse> FetchWeeklyTop10Async(
            string anonymousPlayerId,
            string sourcePlatform)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl))
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "ONLINE BOARD NOT CONFIGURED");
            }

            try
            {
                string separator = endpointUrl.Contains('?') ? "&" : "?";
                string requestUrl = endpointUrl +
                    separator + "limit=10" +
                    "&playerId=" + Uri.EscapeDataString(anonymousPlayerId ?? string.Empty) +
                    "&platform=" + Uri.EscapeDataString(sourcePlatform ?? "web");

                using CancellationTokenSource cts = new CancellationTokenSource(RequestTimeout);
                using HttpRequestMessage request = CreateRequest(HttpMethod.Get, requestUrl);
                using HttpResponseMessage response = await httpClient.SendAsync(request, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    return new SharedLeaderboardRemoteResponse(false, errorMessage: "WEEKLY BOARD UNAVAILABLE");
                }

                string json = await response.Content.ReadAsStringAsync(cts.Token);
                return ParseResponseJson(json);
            }
            catch
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "OFFLINE - LOCAL BEST ACTIVE");
            }
        }

        public async Task<SharedLeaderboardRemoteResponse> SubmitScoreAsync(
            SharedScoreSubmissionPayload payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(endpointUrl))
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "ONLINE BOARD NOT CONFIGURED");
            }

            try
            {
                byte[] jsonBytes = SerializePayload(payload);
                using CancellationTokenSource cts = new CancellationTokenSource(RequestTimeout);
                using HttpRequestMessage request = CreateRequest(HttpMethod.Post, endpointUrl);
                request.Content = new ByteArrayContent(jsonBytes);
                request.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
                    {
                        CharSet = "utf-8"
                    };

                using HttpResponseMessage response = await httpClient.SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return new SharedLeaderboardRemoteResponse(false, errorMessage: "SCORE SAVED LOCALLY");
                }

                string json = await response.Content.ReadAsStringAsync(cts.Token);
                return ParseResponseJson(json);
            }
            catch
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "OFFLINE - SCORE SAVED LOCALLY");
            }
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, url);
            if (!string.IsNullOrWhiteSpace(publicAnonKey))
            {
                request.Headers.TryAddWithoutValidation("apikey", publicAnonKey);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + publicAnonKey);
            }

            return request;
        }

        private static byte[] SerializePayload(SharedScoreSubmissionPayload payload)
        {
            using MemoryStream buffer = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("runId", payload.RunId);
                writer.WriteString("playerId", payload.AnonymousPlayerId);
                writer.WriteString("displayName", payload.DisplayName);
                writer.WriteString("score", payload.Score.ToString(CultureInfo.InvariantCulture));
                writer.WriteNumber("durationSeconds", Math.Round(payload.RunDurationSeconds, 2));
                writer.WriteString("platform", payload.SourcePlatform);
                writer.WriteEndObject();
            }

            return buffer.ToArray();
        }

        private static SharedLeaderboardRemoteResponse ParseResponseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "EMPTY SERVER RESPONSE");
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "INVALID SERVER RESPONSE");
            }

            int? playerRank = null;
            if (root.TryGetProperty("playerRank", out JsonElement rankProp) &&
                rankProp.ValueKind == JsonValueKind.Number &&
                rankProp.TryGetInt32(out int parsedRank) &&
                parsedRank > 0)
            {
                playerRank = parsedRank;
            }

            List<LeaderboardRankEntry> entries = new List<LeaderboardRankEntry>();
            if (root.TryGetProperty("entries", out JsonElement entriesProp) &&
                entriesProp.ValueKind == JsonValueKind.Array)
            {
                int fallbackRank = 1;
                foreach (JsonElement item in entriesProp.EnumerateArray())
                {
                    if (entries.Count >= 10 || item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    int rank = fallbackRank;
                    if (item.TryGetProperty("rank", out JsonElement rProp) &&
                        rProp.ValueKind == JsonValueKind.Number &&
                        rProp.TryGetInt32(out int explicitRank) &&
                        explicitRank > 0)
                    {
                        rank = explicitRank;
                    }

                    string name = "PLAYER";
                    if (item.TryGetProperty("displayName", out JsonElement nameProp) &&
                        nameProp.ValueKind == JsonValueKind.String)
                    {
                        name = nameProp.GetString() ?? "PLAYER";
                    }

                    BigInteger score = BigInteger.Zero;
                    if (item.TryGetProperty("score", out JsonElement scoreProp))
                    {
                        string raw = scoreProp.ValueKind == JsonValueKind.String
                            ? (scoreProp.GetString() ?? "0")
                            : scoreProp.GetRawText();
                        BigInteger.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out score);
                    }

                    bool isCurrent = false;
                    if (item.TryGetProperty("isCurrentPlayer", out JsonElement currentProp) &&
                        (currentProp.ValueKind == JsonValueKind.True || currentProp.ValueKind == JsonValueKind.False))
                    {
                        isCurrent = currentProp.GetBoolean();
                    }

                    if (score > BigInteger.Zero)
                    {
                        entries.Add(new LeaderboardRankEntry(rank, name, score, isCurrent));
                        fallbackRank = rank + 1;
                    }
                }
            }

            return new SharedLeaderboardRemoteResponse(
                success: true,
                playerWeeklyRank: playerRank,
                top10: entries);
        }
    }
}
