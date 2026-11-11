using System;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.JSInterop;

namespace BillusBucket.Web.Pages
{
    public partial class Index : IDisposable
    {
        private BillusBucketGame game;
        private InMemoryAssetStreamProvider preloadedAssets;
        private BrowserJsPlatformBridge platformBridge;
        private ILeaderboardService leaderboardService;
        private DotNetObjectReference<Index> dotNetRef;
        private bool bootCompleted;
        private bool disposed;
        private readonly CancellationTokenSource preloadCancellation = new CancellationTokenSource();

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);
            if (!firstRender)
            {
                return;
            }

            try
            {
                platformBridge = new BrowserJsPlatformBridge(JsRuntime);
                await platformBridge.InitializeAsync();
                platformBridge.NotifyLoadingStarted();

                preloadedAssets = await PreloadGameAssetsAsync();

                BrowserLocalScoreStorage localStorage = new BrowserLocalScoreStorage(
                    JsRuntime,
                    platformBridge.State.PersistentStorageAvailable);
                BrowserCrazyGamesSdkClient crazyGamesClient = new BrowserCrazyGamesSdkClient(
                    JsRuntime,
                    platformBridge);

                leaderboardService = LeaderboardServiceFactory.Create(
                    platformBridge.State.ToRuntimeConfig(),
                    localStorage,
                    crazyGamesClient,
                    Http);

                dotNetRef = DotNetObjectReference.Create(this);
                bootCompleted = true;

                await JsRuntime.InvokeVoidAsync("billuPlatform.startRenderLoop", dotNetRef);
                // Optional audio must never hold the ready screen behind the loader.
                _ = LoadAudioInBackgroundAsync();
            }
            catch (Exception error)
            {
                bootCompleted = false;
                if (disposed) return;
                await JsRuntime.InvokeVoidAsync("billuPlatform.showFatalError", error.Message);
            }
        }

        [JSInvokable]
        public void SuspendBrowserGame()
        {
            game?.SuspendForBrowser();
            leaderboardService?.FlushLocalState();
        }

        [JSInvokable]
        public void TickDotNet()
        {
            if (!bootCompleted)
            {
                return;
            }

            if (game == null)
            {
                game = new BillusBucketGame(
                    assetProvider: preloadedAssets,
                    leaderboardService: leaderboardService,
                    platformBridge: platformBridge);
                game.Run();
            }

            game.Tick();
        }

        private async Task<InMemoryAssetStreamProvider> PreloadGameAssetsAsync()
        {
            InMemoryAssetStreamProvider provider = new InMemoryAssetStreamProvider();
            string[] assetPaths = InMemoryAssetStreamProvider.RequiredAssets;
            Task<byte[]>[] downloads = new Task<byte[]>[assetPaths.Length];
            string assetVersion = await JsRuntime.InvokeAsync<string>("billuPlatform.getAssetVersion");
            for (int i = 0; i < assetPaths.Length; i++)
            {
                if (assetPaths[i].EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    downloads[i] = Task.FromResult<byte[]>(null);
                    continue;
                }

                // A stable build version refreshes updated art while allowing repeat visits to cache it.
                downloads[i] = GameAssetDownload.FetchAsync(Http,
                    "Assets/" + assetPaths[i] + "?v=" + Uri.EscapeDataString(assetVersion),
                    cancellationToken: preloadCancellation.Token);
            }

            await Task.WhenAll(downloads);

            for (int i = 0; i < assetPaths.Length; i++)
            {
                if (downloads[i].Result != null) provider.Add(assetPaths[i], downloads[i].Result);
            }

            return provider;
        }

        private async Task LoadAudioInBackgroundAsync()
        {
            try
            {
                string version = await JsRuntime.InvokeAsync<string>("billuPlatform.getAssetVersion");
                var downloads = new System.Collections.Generic.List<Task>();
                foreach (string path in InMemoryAssetStreamProvider.RequiredAssets)
                {
                    if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        downloads.Add(DownloadSoundAsync(path, version));
                    }
                }
                await Task.WhenAll(downloads);
            }
            catch (OperationCanceledException) { } // Closing the page cancels outstanding downloads.
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine("Background audio skipped: " + error.Message);
            }
        }

        private async Task DownloadSoundAsync(string path, string version)
        {
            byte[] bytes = await GameAssetDownload.FetchAsync(Http,
                "Assets/" + path + "?v=" + Uri.EscapeDataString(version),
                optional: true, cancellationToken: preloadCancellation.Token);
            if (!disposed && bytes != null)
            {
                preloadedAssets.Add(path, bytes);
            }
        }

        public void Dispose()
        {
            disposed = true;
            bootCompleted = false;
            preloadCancellation.Cancel();
            game?.Dispose();
            dotNetRef?.Dispose();
        }
    }
}
