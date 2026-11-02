using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using System.IO;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;

namespace BillusBucket
{
    /// <summary>
    /// Creates, loads, and disposes the graphics and audio resources.
    /// MonoGame calls Initialize and LoadContent once before the game loop.
    /// </summary>
    public sealed partial class BillusBucketGame
    {
        // Platform & asset abstractions (shared across DesktopGL and Browser WebAssembly)
        private readonly IAssetStreamProvider assetProvider;
        private readonly PlatformLifecycleGuard platformBridge;

        // MonoGame graphics & audio resources
        private readonly GraphicsDeviceManager graphics;
        private readonly ReviewTools reviewTools;
        private SpriteBatch spriteBatch;
        private RenderTarget2D canvas;
        private Texture2D pixel;
        private Texture2D background;
        private Texture2D skyClouds;
        private RasterizerState skyRasterizer;
        private static readonly Rectangle[] CloudShapes =
        {
            new Rectangle(157, 50, 44, 9),
            new Rectangle(68, 89, 41, 7),
            new Rectangle(224, 80, 25, 6)
        };
        private Texture2D cashSprite;
        private Texture2D paperSilhouette;
        private Texture2D bombSprite;
        private Texture2D burnedBucketSprite;
        private Texture2D bucketSprite;
        private Texture2D billuSheet;
        private Texture2D burnedSprite;
        private Texture2D explosionSheet;
        private Texture2D uiSign;
        private Texture2D uiButton;
        private Texture2D uiAudio;
        private Texture2D uiLeaderboard;
        private Color[] notePopupColors;
        private GameAudio audio;

        protected override void Initialize()
        {
#if BLAZORGL
            SyncBrowserBackBufferToWindow();
#else
            graphics.PreferredBackBufferWidth = PixelArt.WindowWidth;
            graphics.PreferredBackBufferHeight = PixelArt.WindowHeight;
            graphics.ApplyChanges();
#endif
            SyncTouchPanelDisplaySize();
            base.Initialize();
        }

        protected override void LoadContent()
        {
            platformBridge.NotifyLoadingStarted();
            spriteBatch = new SpriteBatch(GraphicsDevice);
            canvas = new RenderTarget2D(GraphicsDevice, PixelArt.FieldWidth, PixelArt.Height);
            pixel = new Texture2D(GraphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
            background = LoadArt("rooftop-scenery.png");
            skyClouds = LoadArt("sky-clouds.png");
            skyRasterizer = new RasterizerState
            {
                ScissorTestEnable = true,
                CullMode = CullMode.None
            };
            cashSprite = LoadArt("rupee-notes.png");
            notePopupColors = SampleNoteColors(cashSprite);
            Color[] paperMask = new Color[cashSprite.Width * cashSprite.Height];
            cashSprite.GetData(paperMask);
            for (int i = 0; i < paperMask.Length; i++)
            {
                paperMask[i] = new Color(paperMask[i].A, paperMask[i].A, paperMask[i].A, paperMask[i].A);
            }

            paperSilhouette = new Texture2D(GraphicsDevice, cashSprite.Width, cashSprite.Height);
            paperSilhouette.SetData(paperMask);
            bombSprite = LoadArt("bomb-sheet.png");
            burnedBucketSprite = LoadArt("bucket-burned.png");
            bucketSprite = LoadArt("bucket.png");
            billuSheet = LoadArt("billu-carry.png");
            burnedSprite = LoadArt("billu-burned.png");
            explosionSheet = LoadArt("explosion-sheet.png");
            uiSign = LoadArt("ui-sign.png");
            uiButton = LoadArt("ui-button.png");
            uiAudio = LoadArt("ui-audio.png");
            uiLeaderboard = CreateLeaderboardIconSheet();
            if (leaderboardService == null)
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string defaultSavePath = Path.Combine(localAppData, "BillusBucket", "leaderboard.json");
                string legacySavePath = Path.Combine(localAppData, "BilluKiBalti", "leaderboard.json");
                if (!reviewTools.IsLiveReviewMode && !File.Exists(defaultSavePath) && File.Exists(legacySavePath))
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(defaultSavePath));
                        File.Copy(legacySavePath, defaultSavePath);
                    }
                    catch
                    {
                    // Ignore legacy copy errors; LeaderboardStore falls back cleanly.
                    }
                }

                string savePath = reviewTools.GetLeaderboardPath(defaultSavePath);
                string saveDirectory = Path.GetDirectoryName(savePath);
                LeaderboardStore desktopStore = new LeaderboardStore(savePath);
                DesktopScoreStorage desktopStorage = new DesktopScoreStorage(desktopStore, saveDirectory);
                leaderboardService = new LocalLeaderboardService(desktopStorage);
            }

            bestKnownCash = leaderboardService.GetPersonalBest();
            hasAutoPromptedForName = leaderboardService.HasCustomPlayerName;
            currentRun = new BillusBucketRun(bestKnownCash);
            if (!reviewTools.IsArtReviewMode)
            {
                try
                {
                    if (AudioPreferences.TryParse(platformBridge.LoadAudioPreferences(), out var preferences))
                    {
                        musicMuted = preferences.MusicMuted;
                        sfxMuted = preferences.SfxMuted;
                    }

                    audio = new GameAudio(assetProvider);
                    audio.SetMuted(musicMuted, sfxMuted, playFeedback: false);
                }
                catch (NoAudioHardwareException error)
                {
                    Debug.WriteLine("Audio unavailable: " + error.Message);
                }
                catch (Exception error)
                {
                    Debug.WriteLine("Audio initialization skipped: " + error.Message);
                }

                InitializeLeaderboardInBackground();
            }

            bucketControl = CreateCenteredBucket();
            platformBridge.NotifyLoadingFinished();
        }

#if !BLAZORGL

        protected override void OnExiting(object sender, ExitingEventArgs args)
        {
            platformBridge.NotifyGameplayStop();
            leaderboardService?.FlushLocalState();
            base.OnExiting(sender, args);
        }

#endif

        protected override void UnloadContent()
        {
            platformBridge.NotifyGameplayStop();
            leaderboardService?.FlushLocalState();
            audio?.Dispose();
            canvas?.Dispose();
            pixel?.Dispose();
            background?.Dispose();
            skyClouds?.Dispose();
            skyRasterizer?.Dispose();
            cashSprite?.Dispose();
            paperSilhouette?.Dispose();
            bombSprite?.Dispose();
            burnedBucketSprite?.Dispose();
            bucketSprite?.Dispose();
            billuSheet?.Dispose();
            burnedSprite?.Dispose();
            explosionSheet?.Dispose();
            uiSign?.Dispose();
            uiButton?.Dispose();
            uiAudio?.Dispose();
            uiLeaderboard?.Dispose();
            spriteBatch?.Dispose();
            base.UnloadContent();
        }

        private void SyncPlatformGameplayLifecycle()
        {
            if (CurrentPhase == RoundPhase.Playing && IsActive && !platformBridge.IsExternalAudioMuted)
            {
                platformBridge.NotifyGameplayStart();
            }
            else
            {
                platformBridge.NotifyGameplayStop();
            }
        }

        private Texture2D LoadArt(string fileName)
        {
            using Stream stream = assetProvider.OpenRead(fileName);
            return Texture2D.FromStream(GraphicsDevice, stream);
        }

        private static Color[] SampleNoteColors(Texture2D noteSheet)
        {
            // Sample the main paper color from pixel (1, 2) of each note's first animation frame
            // so "+RS" popup colors automatically match rupee-notes.png.
            Color[] sheetPixels = new Color[noteSheet.Width * noteSheet.Height];
            noteSheet.GetData(sheetPixels);
            Color[] colors = new Color[NoteCatalog.Count];
            int pixelsPerDenomination = NoteCatalog.FramesPerNote * NoteFrameWidth;
            const int sampleY = 2;
            const int sampleOffsetX = 1;
            for (int index = 0; index < colors.Length; index++)
            {
                int sampleX = index * pixelsPerDenomination + sampleOffsetX;
                int pixelIndex = sampleY * noteSheet.Width + sampleX;
                colors[index] = pixelIndex < sheetPixels.Length ? sheetPixels[pixelIndex] : Color.White;
            }

            return colors;
        }
    }
}
