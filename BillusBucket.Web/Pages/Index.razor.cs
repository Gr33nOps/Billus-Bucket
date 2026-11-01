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
            // The pixel art is tiny. A per-load image revision avoids stale character
            // sprites during the GitHub Pages cache window without reloading large WAVs.
            string imageRevision = Guid.NewGuid().ToString("N");

            for (int i = 0; i < assetPaths.Length; i++)
            {
                string revision = assetPaths[i].EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? assetVersion + "-" + imageRevision
                    : assetVersion;
                downloads[i] = GameAssetDownload.FetchAsync(Http,
                    "Assets/" + assetPaths[i] + "?v=" + Uri.EscapeDataString(revision),
                    optional: assetPaths[i].EndsWith(".wav", StringComparison.OrdinalIgnoreCase),
                    cancellationToken: preloadCancellation.Token);
            }

            await Task.WhenAll(downloads);

            for (int i = 0; i < assetPaths.Length; i++)
            {
                if (downloads[i].Result != null) provider.Add(assetPaths[i], downloads[i].Result);
            }

            return provider;
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
