using System;
using System.Threading.Tasks;
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

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);
            if (!firstRender)
            {
                return;
            }

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

            for (int i = 0; i < assetPaths.Length; i++)
            {
                downloads[i] = Http.GetByteArrayAsync("Assets/" + assetPaths[i]);
            }

            await Task.WhenAll(downloads);

            for (int i = 0; i < assetPaths.Length; i++)
            {
                provider.Add(assetPaths[i], downloads[i].Result);
            }

            return provider;
        }

        public void Dispose()
        {
            game?.Dispose();
            dotNetRef?.Dispose();
        }
    }
}
