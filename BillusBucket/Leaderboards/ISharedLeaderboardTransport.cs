using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BillusBucket
{
    public sealed class SharedScoreSubmissionPayload
    {
        public string RunId { get; }
        public string AnonymousPlayerId { get; }
        public string ConnectCode { get; }
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
            string sourcePlatform,
            string connectCode = "")
        {
            RunId = runId ?? string.Empty;
            AnonymousPlayerId = anonymousPlayerId ?? string.Empty;
            ConnectCode = connectCode ?? string.Empty;
            DisplayName = LeaderboardStore.NormalizeName(displayName);
            Score = score < BigInteger.Zero ? BigInteger.Zero : score;
            RunDurationSeconds = float.IsFinite(runDurationSeconds) && runDurationSeconds >= 0f
                ? runDurationSeconds
                : 0f;
            SourcePlatform = string.IsNullOrWhiteSpace(sourcePlatform) ? "web" : sourcePlatform;
        }
    }

    public sealed class SharedProfileSavePayload
    {
        public string AnonymousPlayerId { get; }
        public string ConnectCode { get; }
        public string DisplayName { get; }
        public BigInteger PersonalBest { get; }
        public string SourcePlatform { get; }

        public SharedProfileSavePayload(
            string anonymousPlayerId,
            string connectCode,
            string displayName,
            BigInteger personalBest,
            string sourcePlatform)
        {
            AnonymousPlayerId = anonymousPlayerId ?? string.Empty;
            ConnectCode = connectCode ?? string.Empty;
            DisplayName = LeaderboardStore.NormalizeName(displayName);
            PersonalBest = personalBest < BigInteger.Zero ? BigInteger.Zero : personalBest;
            SourcePlatform = string.IsNullOrWhiteSpace(sourcePlatform) ? "web" : sourcePlatform;
        }
    }

    public sealed class SharedLeaderboardRemoteResponse
    {
        public bool Success { get; }
        public bool IsPermanentFailure { get; }
        public int? PlayerWeeklyRank { get; }
        public IReadOnlyList<LeaderboardRankEntry> Top10 { get; }
        public string ProfileDisplayName { get; }
        public BigInteger? ProfilePersonalBest { get; }
        public string ErrorMessage { get; }

        public SharedLeaderboardRemoteResponse(
            bool success,
            int? playerWeeklyRank = null,
            IReadOnlyList<LeaderboardRankEntry> top10 = null,
            string errorMessage = "",
            string profileDisplayName = null,
            BigInteger? profilePersonalBest = null,
            bool isPermanentFailure = false)
        {
            Success = success;
            IsPermanentFailure = isPermanentFailure;
            PlayerWeeklyRank = playerWeeklyRank.HasValue && playerWeeklyRank.Value > 0
                ? playerWeeklyRank
                : null;
            Top10 = top10 ?? Array.Empty<LeaderboardRankEntry>();
            ProfileDisplayName = profileDisplayName;
            ProfilePersonalBest = profilePersonalBest.HasValue && profilePersonalBest.Value >= BigInteger.Zero
                ? profilePersonalBest
                : null;
            ErrorMessage = errorMessage ?? string.Empty;
        }
    }

    /// <summary>
    /// Network transport abstraction for the optional shared weekly leaderboard & cloud profile backend.
    /// Allows unit testing Poki/Shared adapters with zero real network sockets.
    /// </summary>
    public interface ISharedLeaderboardTransport
    {
        Task<SharedLeaderboardRemoteResponse> FetchWeeklyTop10Async(
            string anonymousPlayerId,
            string sourcePlatform);

        Task<SharedLeaderboardRemoteResponse> SubmitScoreAsync(
            SharedScoreSubmissionPayload payload);

        Task<SharedLeaderboardRemoteResponse> SaveProfileAsync(
            SharedProfileSavePayload payload) =>
            Task.FromResult(new SharedLeaderboardRemoteResponse(false, errorMessage: "PROFILE SYNC NOT SUPPORTED"));

        Task<SharedLeaderboardRemoteResponse> RestoreProfileAsync(
            string connectCode,
            string sourcePlatform) =>
            Task.FromResult(new SharedLeaderboardRemoteResponse(false, errorMessage: "RESTORE NOT SUPPORTED"));
    }

    /// <summary>
    /// Trimmer-safe HTTP JSON client for the Cloudflare Worker + D1 (or Supabase) weekly leaderboard & profile API.
    /// Uses no private credentials and a short 4-second timeout so gameplay never stalls.
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

                string json = await response.Content.ReadAsStringAsync(cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return new SharedLeaderboardRemoteResponse(
                        false,
                        errorMessage: FriendlyHttpError(response.StatusCode, json, "WEEKLY BOARD UNAVAILABLE"));
                }

                return ParseResponseJson(json);
            }
            catch (JsonException)
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "INVALID SERVER RESPONSE");
            }
            catch (OperationCanceledException)
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "CONNECTION TIMED OUT");
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
                byte[] jsonBytes = SerializeSubmitPayload(payload);
                return await SendPostJsonAsync(jsonBytes, "SCORE NOT ACCEPTED", "OFFLINE - SCORE SAVED LOCALLY");
            }
            catch
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "OFFLINE - SCORE SAVED LOCALLY");
            }
        }

        public async Task<SharedLeaderboardRemoteResponse> SaveProfileAsync(
            SharedProfileSavePayload payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(endpointUrl))
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "SAVED ON DEVICE");
            }

            try
            {
                byte[] jsonBytes = SerializeSaveProfilePayload(payload);
                return await SendPostJsonAsync(jsonBytes, "NAME NOT ACCEPTED", "OFFLINE - SAVED ON DEVICE");
            }
            catch
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "OFFLINE - SAVED ON DEVICE");
            }
        }

        public async Task<SharedLeaderboardRemoteResponse> RestoreProfileAsync(
            string connectCode,
            string sourcePlatform)
        {
            if (string.IsNullOrWhiteSpace(connectCode) || string.IsNullOrWhiteSpace(endpointUrl))
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "CLOUD SYNC NOT CONFIGURED");
            }

            try
            {
                byte[] jsonBytes = SerializeRestorePayload(connectCode, sourcePlatform);
                return await SendPostJsonAsync(jsonBytes, "SAVE CODE NOT FOUND", "OFFLINE - CHECK INTERNET");
            }
            catch
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "OFFLINE - CHECK INTERNET");
            }
        }

        private async Task<SharedLeaderboardRemoteResponse> SendPostJsonAsync(
            byte[] jsonBytes,
            string defaultHttpError,
            string offlineError)
        {
            try
            {
                using CancellationTokenSource cts = new CancellationTokenSource(RequestTimeout);
                using HttpRequestMessage request = CreateRequest(HttpMethod.Post, endpointUrl);
                request.Content = new ByteArrayContent(jsonBytes);
                request.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
                    {
                        CharSet = "utf-8"
                    };

                using HttpResponseMessage response = await httpClient.SendAsync(request, cts.Token);
                string json = await response.Content.ReadAsStringAsync(cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return new SharedLeaderboardRemoteResponse(
                        false,
                        errorMessage: FriendlyHttpError(response.StatusCode, json, defaultHttpError),
                        isPermanentFailure: (int)response.StatusCode >= 400 && (int)response.StatusCode < 500 &&
                            (int)response.StatusCode != 429 && (int)response.StatusCode != 408);
                }

                return ParseResponseJson(json);
            }
            catch (JsonException)
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "INVALID SERVER RESPONSE");
            }
            catch (OperationCanceledException)
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: "CONNECTION TIMED OUT");
            }
            catch
            {
                return new SharedLeaderboardRemoteResponse(false, errorMessage: offlineError);
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

        private static byte[] SerializeSubmitPayload(SharedScoreSubmissionPayload payload)
        {
            using MemoryStream buffer = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("action", "submit_score");
                writer.WriteString("runId", payload.RunId);
                writer.WriteString("playerId", payload.AnonymousPlayerId);
                if (!string.IsNullOrWhiteSpace(payload.ConnectCode))
                {
                    writer.WriteString("connectCode", payload.ConnectCode);
                }
                writer.WriteString("displayName", payload.DisplayName);
                writer.WriteString("score", payload.Score.ToString(CultureInfo.InvariantCulture));
                writer.WriteNumber("durationSeconds", Math.Round(payload.RunDurationSeconds, 2));
                writer.WriteString("platform", payload.SourcePlatform);
                writer.WriteEndObject();
            }

            return buffer.ToArray();
        }

        private static byte[] SerializeSaveProfilePayload(SharedProfileSavePayload payload)
        {
            using MemoryStream buffer = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("action", "save_profile");
                writer.WriteString("playerId", payload.AnonymousPlayerId);
                if (!string.IsNullOrWhiteSpace(payload.ConnectCode))
                {
                    writer.WriteString("connectCode", payload.ConnectCode);
                }
                writer.WriteString("displayName", payload.DisplayName);
                writer.WriteString("personalBest", payload.PersonalBest.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("platform", payload.SourcePlatform);
                writer.WriteEndObject();
            }

            return buffer.ToArray();
        }

        private static byte[] SerializeRestorePayload(string connectCode, string sourcePlatform)
        {
            using MemoryStream buffer = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("action", "restore_profile");
                writer.WriteString("connectCode", connectCode);
                writer.WriteString("platform", string.IsNullOrWhiteSpace(sourcePlatform) ? "web" : sourcePlatform);
                writer.WriteEndObject();
            }

            return buffer.ToArray();
        }

        private static string FriendlyHttpError(System.Net.HttpStatusCode status, string json, string fallback)
        {
            if ((int)status == 429) return "TOO MANY REQUESTS - TRY LATER";
            if ((int)status >= 500) return "SERVER BUSY - TRY AGAIN";
            return TryExtractServerError(json, fallback);
        }

        private static string TryExtractServerError(string json, string fallback)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("error", out JsonElement errProp) &&
                    errProp.ValueKind == JsonValueKind.String)
                {
                    string msg = (errProp.GetString() ?? string.Empty).Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        return msg.Length > 34 ? msg.Substring(0, 34) : msg;
                    }
                }
            }
            catch
            {
            }

            return fallback;
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

            if (!root.TryGetProperty("entries", out JsonElement validatedEntries) ||
                validatedEntries.ValueKind != JsonValueKind.Array)
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

            string profileDisplayName = null;
            if (root.TryGetProperty("displayName", out JsonElement profileNameProp) &&
                profileNameProp.ValueKind == JsonValueKind.String)
            {
                string rawProfileName = profileNameProp.GetString();
                if (!string.IsNullOrWhiteSpace(rawProfileName))
                {
                    profileDisplayName = LeaderboardStore.NormalizeName(rawProfileName);
                }
            }

            BigInteger? profileBest = null;
            if (root.TryGetProperty("personalBest", out JsonElement profileBestProp))
            {
                string rawBest = profileBestProp.ValueKind == JsonValueKind.String
                    ? (profileBestProp.GetString() ?? string.Empty)
                    : profileBestProp.GetRawText();
                if (BigInteger.TryParse(rawBest, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger parsedBest) &&
                    parsedBest >= BigInteger.Zero)
                {
                    profileBest = parsedBest;
                }
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
                top10: entries,
                profileDisplayName: profileDisplayName,
                profilePersonalBest: profileBest);
        }
    }
}
