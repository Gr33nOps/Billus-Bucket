using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BillusBucket
{
    public static class GameAssetDownload
    {
        public static async Task<byte[]> FetchAsync(HttpClient client, string uri, bool optional = false,
            CancellationToken cancellationToken = default, TimeSpan? retryDelay = null)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsByteArrayAsync(timeout.Token);
                }
                catch (HttpRequestException error) when (error.StatusCode.HasValue &&
                    (int)error.StatusCode.Value < 500 && (int)error.StatusCode.Value != 429)
                {
                    if (optional) return null;
                    throw;
                }
                catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (attempt == 2)
                    {
                        if (optional) return null;
                        throw;
                    }
                }
                await Task.Delay(retryDelay ?? TimeSpan.FromMilliseconds(150 * (attempt + 1)), cancellationToken);
            }
            throw new InvalidOperationException("Asset download did not complete.");
        }
    }
}
