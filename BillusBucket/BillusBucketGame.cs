using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
// Alias BigInteger so System.Numerics.Vector2 does not collide with MonoGame's Vector2.
using BigInteger = System.Numerics.BigInteger;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace BillusBucket
{
    /// <summary>
    /// Main MonoGame class for Billu's Bucket.
    /// Loads assets, reads player input, updates gameplay, coordinates platform/leaderboard services,
    /// and draws the crisp 320x180 rooftop scene scaled to any 16:9 viewport.
    /// </summary>
    public sealed class BillusBucketGame : Game
    {
        // Gameplay tuning and layout constants
        private const float MaxFrameDeltaSeconds = 0.05f;
        private const float InitialSpawnDelaySeconds = 0.25f;
        private const float ExplosionDurationSeconds = 0.85f;
        private const float ScreenFlashDuration = 0.22f;
        private const float ScreenFlashFadeSpeed = 0.9f;

        private const int PlayerCollisionWidth = 24;
        private const int CatchBoxHalfWidth = 13;
        private const int CatchBoxWidth = 26;
        private const int CatchBoxHeight = 4;
        private const int CatchBoxOffsetFromGround = 43;

        private const int CashHitboxHalfWidth = 5;
        private const int CashHitboxHalfHeight = 3;
        private const int BombHitboxHalfWidth = 6;
        private const int BombHitboxHalfHeight = 6;

        private const int SpawnSideMargin = 16;
        private const int WindSideMargin = 9;
        private const float DropSpawnY = -12f;
        private const float DropDespawnMargin = 20f;

        private const int NoteFrameWidth = 12;
        private const int NoteFrameHeight = 8;

        // Top-right HUD icon button hitboxes (in 320x180 canvas coordinates)
        private static readonly ButtonBounds PauseButton = new ButtonBounds(292, 8, 20, 16);
        private static readonly ButtonBounds MusicButton = new ButtonBounds(292, 29, 20, 16);
        private static readonly ButtonBounds SfxButton = new ButtonBounds(292, 50, 20, 16);
        private static readonly ButtonBounds LeaderboardIconButton = RoundFlow.TopRightLeaderboardButton;

        // UI palette colors (matched to tools/draw_assets.lua & ui-sign.png)
        private static readonly Color HudLabelColor = new Color(192, 180, 199);
        private static readonly Color ScoreValueColor = new Color(185, 220, 131);
        private static readonly Color BestValueColor = new Color(227, 182, 98);
        private static readonly Color WarmTitleColor = new Color(240, 211, 152);
        private static readonly Color GameOverTitleColor = new Color(246, 163, 69);
        private static readonly Color ButtonTextColor = new Color(241, 227, 191);
        private static readonly Color TextShadowColor = new Color(35, 36, 56);
        private static readonly Color ExplosionSparkColor = new Color(255, 117, 54);
        private static readonly Color SmokePuffColor = new Color(126, 113, 143);

        private static readonly Color PanelInkColor = new Color(35, 36, 56);
        private static readonly Color PanelTimberColor = new Color(114, 86, 97);
        private static readonly Color PanelFaceColor = new Color(81, 67, 82);
        private static readonly Color PanelTopTrimColor = new Color(173, 128, 128);
        private static readonly Color PanelTopGlintColor = new Color(193, 148, 135);
        private static readonly Color PanelBottomTrimColor = new Color(154, 115, 118);

        // Platform & asset abstractions (shared across DesktopGL and Browser WebAssembly)
        private readonly IAssetStreamProvider assetProvider;
        private readonly PlatformLifecycleGuard platformBridge;
        private ILeaderboardService leaderboardService;

        // MonoGame graphics & audio resources
        private readonly GraphicsDeviceManager graphics;
        private readonly ReviewTools reviewTools;
        private SpriteBatch batch;
        private RenderTarget2D canvas;
        private Texture2D pixel;
        private Texture2D background;
        private Texture2D cashSprite;
        private Texture2D bombSprite;
        private Texture2D burnedBucketSprite;
        private Texture2D billuSheet;
        private Texture2D burnedSprite;
        private Texture2D explosionSheet;
        private Texture2D uiSign;
        private Texture2D uiButton;
        private Texture2D uiAudio;
        private Texture2D uiLeaderboard;
        private Color[] notePopupColors;
        private GameAudio audio;

        // Gameplay systems & collections
        private readonly RoundFlow flow = new RoundFlow();
        private readonly BilluAnimator billu = new BilluAnimator();
        private readonly Random random = new Random();
        private readonly List<Drop> drops = new List<Drop>();
        private readonly List<Particle> particles = new List<Particle>();
        private readonly List<Popup> popups = new List<Popup>();

        private BillusBucketRun run;
        private BucketControl bucket;
        private DropDirector director = new DropDirector();

        // Input tracking across frames
        private KeyboardState previousKeys;
        private MouseState previousMouse;
        private bool previousTouch;
        private bool ignorePointerUntilRelease;
        private bool isMainButtonHovered;
        private bool isLeaderboardButtonHovered;
        private bool isBackButtonHovered;

        // Mute flags are stored here so the UI buttons still toggle when no audio hardware is present.
        private bool musicMuted;
        private bool sfxMuted;

        // Timers, per-run submission tracking, and summary values
        private string currentRunId = string.Empty;
        private bool hasSubmittedCurrentRun;
        private float elapsed;
        private float ambientTime;
        private float spawnTimer;
        private float explosionTimer;
        private float flash;
        private BigInteger lastRunCash;
        private BigInteger bestKnownCash;

        private RoundPhase CurrentPhase => flow.Phase;

        public BillusBucketGame(
            string reviewDirectory = null,
            string liveReviewDirectory = null,
            IAssetStreamProvider assetProvider = null,
            ILeaderboardService leaderboardService = null,
            IGamePlatformBridge platformBridge = null)
        {
            reviewTools = new ReviewTools(reviewDirectory, liveReviewDirectory);
            this.assetProvider = assetProvider ?? new DesktopAssetStreamProvider();
            this.leaderboardService = leaderboardService;
            this.platformBridge = new PlatformLifecycleGuard(platformBridge ?? new NullPlatformBridge());

            graphics = new GraphicsDeviceManager(this);
            IsMouseVisible = true;
            Window.Title = "BILLU'S BUCKET";
#if !BLAZORGL
            Window.AllowUserResizing = true;
#endif
        }

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

            batch = new SpriteBatch(GraphicsDevice);
            canvas = new RenderTarget2D(GraphicsDevice, PixelArt.FieldWidth, PixelArt.Height);

            pixel = new Texture2D(GraphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });

            background = LoadArt("rooftop.png");
            cashSprite = LoadArt("rupee-notes.png");
            notePopupColors = SampleNoteColors(cashSprite);

            bombSprite = LoadArt("bomb-sheet.png");
            burnedBucketSprite = LoadArt("bucket-burned.png");
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
            run = new BillusBucketRun(bestKnownCash);

            if (!reviewTools.IsArtReviewMode)
            {
                try
                {
                    audio = new GameAudio(assetProvider);
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

            bucket = CreateCenteredBucket();
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
            cashSprite?.Dispose();
            bombSprite?.Dispose();
            burnedBucketSprite?.Dispose();
            billuSheet?.Dispose();
            burnedSprite?.Dispose();
            explosionSheet?.Dispose();
            uiSign?.Dispose();
            uiButton?.Dispose();
            uiAudio?.Dispose();
            uiLeaderboard?.Dispose();
            batch?.Dispose();
            base.UnloadContent();
        }

        // =====================================================================
        // Update Loop & Input
        // =====================================================================

        protected override void Update(GameTime gameTime)
        {
            if (reviewTools.IsArtReviewMode)
            {
                ArtReviewFrame frame = reviewTools.PrepareArtReviewState(flow, billu, drops);
                run = frame.Run;
                lastRunCash = frame.LastRunCash;
                explosionTimer = frame.ExplosionTimer;
                ambientTime = frame.AmbientTime;
                ClearHoverStates();
                return;
            }

#if BLAZORGL
            SyncBrowserBackBufferToWindow();
#endif
            SyncTouchPanelDisplaySize();

            float dt = Math.Min(MaxFrameDeltaSeconds, (float)gameTime.ElapsedGameTime.TotalSeconds);
            KeyboardState keys = Keyboard.GetState();
            MouseState mouse = Mouse.GetState();
            TouchCollection touch = TouchPanel.GetState();

            bool touchDown = touch.Count > 0;
            bool clicked = mouse.LeftButton == ButtonState.Pressed &&
                           previousMouse.LeftButton == ButtonState.Released;
            bool tapped = touchDown && !previousTouch;

            if (clicked || tapped || keys.GetPressedKeys().Length > 0)
            {
                audio?.NotifyUserInteraction();
            }

            bool anyPointerHeld = mouse.LeftButton == ButtonState.Pressed || touchDown;
            if (!anyPointerHeld)
            {
                ignorePointerUntilRelease = false;
            }

            Rectangle canvasViewport = GetCanvasDestinationRect();

            // Ignore clicks that start outside the rendered 16:9 canvas area.
            if (clicked && !IsInsideCanvas(mouse.X, mouse.Y, canvasViewport))
            {
                ignorePointerUntilRelease = true;
                clicked = false;
            }

            if (tapped && !IsInsideCanvas(touch[0].Position.X, touch[0].Position.Y, canvasViewport))
            {
                ignorePointerUntilRelease = true;
                tapped = false;
            }

            // Automatically pause an active run if the player switches to another window or tab.
            if (!IsActive || platformBridge.IsExternalAudioMuted)
            {
                bool wasPlaying = CurrentPhase == RoundPhase.Playing;
                flow.OnFocusLost();
                if (wasPlaying)
                {
                    leaderboardService?.FlushLocalState();
                }

                platformBridge.NotifyGameplayStop();
                ClearHoverStates();
                audio?.Update(dt, CurrentPhase, isWindowActive: false, platformBridge.IsExternalAudioMuted);
                SaveInputState(keys, mouse, touchDown);
                base.Update(gameTime);
                return;
            }

            bool pauseRequested = false;
            // Handle the HUD before allowing a pointer to control Billu.
            if (clicked || tapped)
            {
                Point tapPoint = tapped
                    ? ScreenToCanvas(touch[0].Position.X, touch[0].Position.Y, canvasViewport)
                    : ScreenToCanvas(mouse.X, mouse.Y, canvasViewport);

                if (PauseButton.Contains(tapPoint.X, tapPoint.Y))
                {
                    pauseRequested = CurrentPhase == RoundPhase.Playing || CurrentPhase == RoundPhase.Paused;
                    ignorePointerUntilRelease = true;
                    clicked = false;
                    tapped = false;
                }
                else if (MusicButton.Contains(tapPoint.X, tapPoint.Y))
                {
                    musicMuted = !musicMuted;
                    audio?.SetMuted(musicMuted, sfxMuted);
                    ignorePointerUntilRelease = true;
                    clicked = false;
                    tapped = false;
                }
                else if (SfxButton.Contains(tapPoint.X, tapPoint.Y))
                {
                    sfxMuted = !sfxMuted;
                    audio?.SetMuted(musicMuted, sfxMuted);
                    ignorePointerUntilRelease = true;
                    clicked = false;
                    tapped = false;
                }
            }

            if (WasKeyJustPressed(keys, Keys.M))
            {
                bool bothCurrentlyMuted = musicMuted && sfxMuted;
                bool shouldMuteBoth = !bothCurrentlyMuted;
                musicMuted = shouldMuteBoth;
                sfxMuted = shouldMuteBoth;
                audio?.SetMuted(musicMuted, sfxMuted);
            }

            RoundPhase phaseBefore = CurrentPhase;

            if (CurrentPhase != RoundPhase.Paused)
            {
                UpdateVisualEffects(dt);
            }

            if (flow.IsLeaderboardOpen)
            {
                UpdateLeaderboardScreenInput(keys, mouse, touch, clicked, tapped, canvasViewport);
            }
            else if (CurrentPhase == RoundPhase.Ready)
            {
                if (CheckLeaderboardOpenInput(keys, mouse, touch, clicked, tapped, canvasViewport))
                {
                    OpenLeaderboardScreen();
                }
                else if (CheckMainMenuInput(keys, mouse, touch, clicked, tapped, canvasViewport) == RoundAction.StartRun)
                {
                    StartRun();
                }
            }
            else if (CurrentPhase == RoundPhase.Playing)
            {
                if (pauseRequested || WasKeyJustPressed(keys, Keys.Escape) || WasKeyJustPressed(keys, Keys.P))
                {
                    flow.TogglePause();
                }
                else
                {
                    UpdateRun(dt, keys, mouse, touch, canvasViewport);
                }
            }
            else if (CurrentPhase == RoundPhase.Paused)
            {
                if (pauseRequested || WasKeyJustPressed(keys, Keys.Escape) || WasKeyJustPressed(keys, Keys.P))
                {
                    flow.TogglePause();
                }
                else
                {
                    CheckMainMenuInput(keys, mouse, touch, clicked, tapped, canvasViewport);
                }
            }
            else if (CurrentPhase == RoundPhase.GameOver)
            {
                if (explosionTimer <= 0f)
                {
                    if (CheckLeaderboardOpenInput(keys, mouse, touch, clicked, tapped, canvasViewport))
                    {
                        OpenLeaderboardScreen();
                    }
                    else if (CheckMainMenuInput(keys, mouse, touch, clicked, tapped, canvasViewport) == RoundAction.StartRun)
                    {
                        StartRun();
                    }
                }
            }

            UpdateButtonHoverStates(mouse, canvasViewport);

            // Save scores and play UI feedback sounds whenever the phase changes.
            if (phaseBefore != CurrentPhase)
            {
                if (CurrentPhase == RoundPhase.Paused || CurrentPhase == RoundPhase.GameOver)
                {
                    leaderboardService?.FlushLocalState();
                }

                if (CurrentPhase == RoundPhase.Paused)
                {
                    audio?.Play(GameAudio.SoundPause);
                }
                else if (phaseBefore == RoundPhase.Paused)
                {
                    audio?.Play(GameAudio.SoundResume);
                }
            }

            SyncPlatformGameplayLifecycle();
            audio?.Update(dt, CurrentPhase, IsActive, platformBridge.IsExternalAudioMuted);
            SaveInputState(keys, mouse, touchDown);
            base.Update(gameTime);
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

        private void ClearHoverStates()
        {
            isMainButtonHovered = false;
            isLeaderboardButtonHovered = false;
            isBackButtonHovered = false;
        }

        private void UpdateButtonHoverStates(MouseState mouse, Rectangle canvasViewport)
        {
            if (!IsInsideCanvas(mouse.X, mouse.Y, canvasViewport))
            {
                ClearHoverStates();
                return;
            }

            Point mouseCanvas = ScreenToCanvas(mouse.X, mouse.Y, canvasViewport);
            isMainButtonHovered = flow.MainButton.Contains(mouseCanvas.X, mouseCanvas.Y);
            bool canUseLeaderboardIcon =
                flow.IsLeaderboardOpen ||
                CurrentPhase == RoundPhase.Ready ||
                (CurrentPhase == RoundPhase.GameOver && explosionTimer <= 0f);
            isLeaderboardButtonHovered =
                canUseLeaderboardIcon &&
                LeaderboardIconButton.Contains(mouseCanvas.X, mouseCanvas.Y);
            isBackButtonHovered = flow.BackButton.Contains(mouseCanvas.X, mouseCanvas.Y);
        }

        private void SaveInputState(KeyboardState keys, MouseState mouse, bool touchDown)
        {
            previousKeys = keys;
            previousMouse = mouse;
            previousTouch = touchDown;
        }

        private bool WasKeyJustPressed(KeyboardState keys, Keys key)
        {
            return keys.IsKeyDown(key) && !previousKeys.IsKeyDown(key);
        }

        private RoundAction CheckMainMenuInput(
            KeyboardState keys,
            MouseState mouse,
            TouchCollection touch,
            bool clicked,
            bool tapped,
            Rectangle canvasViewport)
        {
            if (WasKeyJustPressed(keys, Keys.Enter) || WasKeyJustPressed(keys, Keys.Space))
            {
                return flow.ActivateMainAction();
            }

            if (clicked)
            {
                Point canvasPoint = ScreenToCanvas(mouse.X, mouse.Y, canvasViewport);
                RoundAction action = flow.TryMainActionAt(canvasPoint.X, canvasPoint.Y);
                if (action != RoundAction.None)
                {
                    ignorePointerUntilRelease = true;
                }
                return action;
            }

            if (tapped)
            {
                Point canvasPoint = ScreenToCanvas(touch[0].Position.X, touch[0].Position.Y, canvasViewport);
                RoundAction action = flow.TryMainActionAt(canvasPoint.X, canvasPoint.Y);
                if (action != RoundAction.None)
                {
                    ignorePointerUntilRelease = true;
                }
                return action;
            }

            return RoundAction.None;
        }

        private bool CheckLeaderboardOpenInput(
            KeyboardState keys,
            MouseState mouse,
            TouchCollection touch,
            bool clicked,
            bool tapped,
            Rectangle canvasViewport)
        {
            if (WasKeyJustPressed(keys, Keys.L))
            {
                return true;
            }

            if (clicked)
            {
                Point canvasPoint = ScreenToCanvas(mouse.X, mouse.Y, canvasViewport);
                if (flow.LeaderboardButton.Contains(canvasPoint.X, canvasPoint.Y))
                {
                    ignorePointerUntilRelease = true;
                    return true;
                }
            }

            if (tapped)
            {
                Point canvasPoint = ScreenToCanvas(touch[0].Position.X, touch[0].Position.Y, canvasViewport);
                if (flow.LeaderboardButton.Contains(canvasPoint.X, canvasPoint.Y))
                {
                    ignorePointerUntilRelease = true;
                    return true;
                }
            }

            return false;
        }

        private void UpdateLeaderboardScreenInput(
            KeyboardState keys,
            MouseState mouse,
            TouchCollection touch,
            bool clicked,
            bool tapped,
            Rectangle canvasViewport)
        {
            bool closeByKey = WasKeyJustPressed(keys, Keys.Escape) ||
                              WasKeyJustPressed(keys, Keys.Back) ||
                              WasKeyJustPressed(keys, Keys.Enter) ||
                              WasKeyJustPressed(keys, Keys.Space) ||
                              WasKeyJustPressed(keys, Keys.L);

            if (closeByKey)
            {
                if (flow.CloseLeaderboard())
                {
                    audio?.Play(GameAudio.SoundToggle);
                }
                return;
            }

            if (clicked)
            {
                Point canvasPoint = ScreenToCanvas(mouse.X, mouse.Y, canvasViewport);
                if (flow.TryCloseLeaderboardAt(canvasPoint.X, canvasPoint.Y))
                {
                    ignorePointerUntilRelease = true;
                    audio?.Play(GameAudio.SoundToggle);
                }
            }
            else if (tapped)
            {
                Point canvasPoint = ScreenToCanvas(touch[0].Position.X, touch[0].Position.Y, canvasViewport);
                if (flow.TryCloseLeaderboardAt(canvasPoint.X, canvasPoint.Y))
                {
                    ignorePointerUntilRelease = true;
                    audio?.Play(GameAudio.SoundToggle);
                }
            }
        }

        private void OpenLeaderboardScreen()
        {
            if (flow.OpenLeaderboard())
            {
                audio?.Play(GameAudio.SoundToggle);
                RefreshLeaderboardInBackground();
            }
        }

        // =====================================================================
        // Active Run Gameplay
        // =====================================================================

        private void StartRun()
        {
            bestKnownCash = BigInteger.Max(bestKnownCash, leaderboardService.GetPersonalBest());
            run = new BillusBucketRun(bestKnownCash);
            bucket = CreateCenteredBucket();

            currentRunId = Guid.NewGuid().ToString("N");
            hasSubmittedCurrentRun = false;

            drops.Clear();
            particles.Clear();
            popups.Clear();

            audio?.Start();

            elapsed = 0f;
            spawnTimer = InitialSpawnDelaySeconds;
            director = new DropDirector();
            explosionTimer = 0f;
            flash = 0f;
            lastRunCash = BigInteger.Zero;
            billu.Reset();
        }

        private void UpdateRun(
            float dt,
            KeyboardState keys,
            MouseState mouse,
            TouchCollection touch,
            Rectangle canvasViewport)
        {
            int keyboardDirection = 0;
            if (keys.IsKeyDown(Keys.Left) || keys.IsKeyDown(Keys.A))
            {
                keyboardDirection -= 1;
            }
            if (keys.IsKeyDown(Keys.Right) || keys.IsKeyDown(Keys.D))
            {
                keyboardDirection += 1;
            }

            float? pointerTargetX = GetActivePointerTargetX(mouse, touch, canvasViewport);

            bucket.Update(dt, keyboardDirection, pointerTargetX);
            billu.Update(dt, bucket.VelocityX);

            elapsed += dt;
            spawnTimer -= dt;
            while (spawnTimer <= 0f)
            {
                SpawnDrop();
            }

            Rectangle bucketRimBox = new Rectangle(
                (int)bucket.X - CatchBoxHalfWidth,
                PixelArt.GroundTop - CatchBoxOffsetFromGround,
                CatchBoxWidth,
                CatchBoxHeight);

            for (int i = drops.Count - 1; i >= 0; i--)
            {
                Drop drop = drops[i];
                drop.Motion.Update(dt);

                bool isCash = drop.Motion.Kind == DropKind.Cash;
                int halfWidth = isCash ? CashHitboxHalfWidth : BombHitboxHalfWidth;
                int halfHeight = isCash ? CashHitboxHalfHeight : BombHitboxHalfHeight;

                Rectangle dropBox = new Rectangle(
                    (int)drop.Motion.X - halfWidth,
                    (int)drop.Motion.Y - halfHeight,
                    halfWidth * 2,
                    halfHeight * 2);

                if (dropBox.Intersects(bucketRimBox))
                {
                    drops.RemoveAt(i);
                    if (isCash)
                    {
                        HandleCashCaught(drop);
                    }
                    else
                    {
                        HandleBombHit();
                        break;
                    }
                }
                else if (drop.Motion.Y > PixelArt.Height + DropDespawnMargin)
                {
                    drops.RemoveAt(i);
                }
            }
        }

        private float? GetActivePointerTargetX(MouseState mouse, TouchCollection touch, Rectangle canvasViewport)
        {
            if (ignorePointerUntilRelease)
            {
                return null;
            }

            float? pointerX = null;

            if (mouse.LeftButton == ButtonState.Pressed && IsInsideCanvas(mouse.X, mouse.Y, canvasViewport))
            {
                Point canvasPoint = ScreenToCanvas(mouse.X, mouse.Y, canvasViewport);
                if (!IsTopRightHudButton(canvasPoint))
                {
                    pointerX = ScreenToCanvasFloatX(mouse.X, canvasViewport);
                }
            }

            if (touch.Count > 0 && IsInsideCanvas(touch[0].Position.X, touch[0].Position.Y, canvasViewport))
            {
                Point canvasPoint = ScreenToCanvas(touch[0].Position.X, touch[0].Position.Y, canvasViewport);
                if (!IsTopRightHudButton(canvasPoint))
                {
                    pointerX = ScreenToCanvasFloatX(touch[0].Position.X, canvasViewport);
                }
            }

            return pointerX;
        }

        private static bool IsTopRightHudButton(Point canvasPoint)
        {
            return PauseButton.Contains(canvasPoint.X, canvasPoint.Y) || MusicButton.Contains(canvasPoint.X, canvasPoint.Y) ||
                   SfxButton.Contains(canvasPoint.X, canvasPoint.Y) ||
                   LeaderboardIconButton.Contains(canvasPoint.X, canvasPoint.Y);
        }

        private void HandleCashCaught(Drop drop)
        {
            int caughtValue = drop.Value;
            run.CatchCash(caughtValue);
            bestKnownCash = BigInteger.Max(bestKnownCash, run.BestCash);

            float smallVariation = (float)random.NextDouble() * 0.024f - 0.012f;
            audio?.Play(GameAudio.SoundCatch, GameAudio.PitchForNote(caughtValue) + smallVariation);
            leaderboardService?.RecordLivePersonalBest(run.Cash);

            string popupLabel = "+RS " + CashDisplay.Compact(caughtValue);
            Vector2 popupStart = new Vector2(bucket.X - 10f, PixelArt.GroundTop - 55f);
            Color popupColor = notePopupColors[drop.NoteIndex];
            popups.Add(new Popup(popupLabel, popupStart, popupColor));
        }

        private void HandleBombHit()
        {
            // Capture the score achieved immediately before the bomb wipes the bucket's cash.
            lastRunCash = run.HitBomb();
            bestKnownCash = BigInteger.Max(bestKnownCash, run.BestCash);

            flow.HitBomb();
            billu.Burn();
            explosionTimer = ExplosionDurationSeconds;
            audio?.HitBomb();
            flash = ScreenFlashDuration;
            SpawnParticleBurst(new Vector2(bucket.X, PixelArt.GroundTop - 36f), ExplosionSparkColor, count: 14);
            drops.Clear();

            // Submit the pre-explosion run score at most once per completed run.
            if (!hasSubmittedCurrentRun)
            {
                hasSubmittedCurrentRun = true;
                string completedRunId = string.IsNullOrWhiteSpace(currentRunId)
                    ? Guid.NewGuid().ToString("N")
                    : currentRunId;
                SubmitCompletedRunScoreInBackground(completedRunId, lastRunCash, elapsed);
            }
        }

        private async void InitializeLeaderboardInBackground()
        {
            if (leaderboardService == null)
            {
                return;
            }

            try
            {
                await leaderboardService.InitializeAsync();
                bestKnownCash = BigInteger.Max(bestKnownCash, leaderboardService.GetPersonalBest());
            }
            catch (Exception error)
            {
                Debug.WriteLine("Leaderboard init skipped: " + error.Message);
            }
        }

        private async void RefreshLeaderboardInBackground()
        {
            if (leaderboardService == null)
            {
                return;
            }

            try
            {
                LeaderboardSnapshot snapshot = await leaderboardService.RefreshRankingsAsync();
                bestKnownCash = BigInteger.Max(bestKnownCash, snapshot.PersonalBest);
            }
            catch (Exception error)
            {
                Debug.WriteLine("Leaderboard refresh skipped: " + error.Message);
            }
        }

        private async void SubmitCompletedRunScoreInBackground(
            string runId,
            BigInteger finalScore,
            float runDurationSeconds)
        {
            if (leaderboardService == null)
            {
                return;
            }

            try
            {
                ScoreSubmissionResult result = await leaderboardService.SubmitRunScoreAsync(
                    runId,
                    finalScore,
                    runDurationSeconds);
                bestKnownCash = BigInteger.Max(bestKnownCash, result.PersonalBest);
            }
            catch (Exception error)
            {
                Debug.WriteLine("Leaderboard submit skipped: " + error.Message);
            }
        }

        private void SpawnDrop()
        {
            DropPlan plan = director.Next(
                elapsed,
                PixelArt.PlayLeft + SpawnSideMargin,
                PixelArt.PlayRight - SpawnSideMargin,
                bucket.X);

            spawnTimer += Math.Max(0.05f, plan.Delay);

            DropMotion motion = new DropMotion(
                plan.Kind,
                plan.X,
                DropSpawnY,
                plan.Speed,
                left: PixelArt.PlayLeft + WindSideMargin,
                right: PixelArt.PlayRight - WindSideMargin);

            int noteIndex = plan.Kind == DropKind.Cash
                ? NoteCatalog.IndexForRoll(random.Next(NoteCatalog.RollCount))
                : 0;

            drops.Add(new Drop(motion, noteIndex));
        }

        private void SpawnParticleBurst(Vector2 center, Color color, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = MathHelper.TwoPi * i / count + (float)random.NextDouble() * 0.18f;
                float speed = 18f + (float)random.NextDouble() * 45f;
                Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                float lifetime = 0.35f + (float)random.NextDouble() * 0.5f;
                particles.Add(new Particle(center, velocity, color, lifetime));
            }
        }

        private void UpdateVisualEffects(float dt)
        {
            ambientTime += dt;

            if (CurrentPhase == RoundPhase.Ready || CurrentPhase == RoundPhase.GameOver)
            {
                billu.Update(dt, horizontalSpeed: 0f);
            }

            flash = Math.Max(0f, flash - dt * ScreenFlashFadeSpeed);
            explosionTimer = Math.Max(0f, explosionTimer - dt);

            for (int i = particles.Count - 1; i >= 0; i--)
            {
                particles[i].Update(dt);
                if (particles[i].Life <= 0f)
                {
                    particles.RemoveAt(i);
                }
            }

            for (int i = popups.Count - 1; i >= 0; i--)
            {
                popups[i].Update(dt);
                if (popups[i].Life <= 0f)
                {
                    popups.RemoveAt(i);
                }
            }
        }

        // =====================================================================
        // Rendering
        // =====================================================================

        protected override void Draw(GameTime gameTime)
        {
            // 1. Draw the pixel-art scene at native 320x180 resolution onto the offscreen canvas.
            GraphicsDevice.SetRenderTarget(canvas);
            GraphicsDevice.Clear(Color.Black);
            batch.Begin(samplerState: SamplerState.PointClamp);
            DrawScene();
            batch.End();

            // 2. Switch back to the window/browser backbuffer and run optional diagnostic captures.
            GraphicsDevice.SetRenderTarget(null);
            reviewTools.CaptureLiveReviewIfDue(
                gameTime,
                canvas,
                CurrentPhase,
                IsActive,
                elapsed,
                run.Cash,
                bucket.X,
                musicMuted,
                sfxMuted,
                drops);

            bool shouldExitAfterReview = reviewTools.SaveArtReviewFrameAndAdvance(canvas);

            // 3. Scale the 320x180 canvas crisply to a centered 16:9 viewport with nearest-neighbor filtering.
            Rectangle destination = GetCanvasDestinationRect();
            GraphicsDevice.Clear(Color.Black);
            batch.Begin(samplerState: SamplerState.PointClamp);
            batch.Draw(canvas, destination, Color.White);
            batch.End();

            base.Draw(gameTime);

            if (shouldExitAfterReview)
            {
                Exit();
            }
        }

        private void DrawScene()
        {
            batch.Draw(background, Vector2.Zero, Color.White);

            // Falling rupee notes and bombs
            foreach (Drop drop in drops)
            {
                DropMotion motion = drop.Motion;
                int frame = motion.AnimationFrame;
                Vector2 position = new Vector2((int)motion.X, (int)motion.Y);

                if (motion.Kind == DropKind.Cash)
                {
                    int spriteColumn = drop.NoteIndex * NoteCatalog.FramesPerNote + frame;
                    Rectangle sourceRect = new Rectangle(spriteColumn * NoteFrameWidth, 0, NoteFrameWidth, NoteFrameHeight);
                    batch.Draw(
                        cashSprite,
                        position,
                        sourceRect,
                        Color.White,
                        motion.Rotation,
                        new Vector2(6f, 4f),
                        1f,
                        SpriteEffects.None,
                        0f);
                }
                else
                {
                    Rectangle sourceRect = new Rectangle(frame * 16, 0, 16, 16);
                    batch.Draw(
                        bombSprite,
                        position,
                        sourceRect,
                        Color.White,
                        0f,
                        new Vector2(4f, 8f),
                        1f,
                        SpriteEffects.None,
                        0f);
                }
            }

            // Billu and his bucket
            Vector2 characterRoot = new Vector2((int)bucket.X, PixelArt.GroundTop);
            if (CurrentPhase != RoundPhase.GameOver)
            {
                int frame = billu.Action == BilluAction.Straight ? 0 : 4 + billu.Frame;
                SpriteEffects facing = billu.Action == BilluAction.WalkLeft
                    ? SpriteEffects.FlipHorizontally
                    : SpriteEffects.None;

                batch.Draw(
                    billuSheet,
                    characterRoot,
                    new Rectangle(frame * 32, 0, 32, 48),
                    Color.White,
                    0f,
                    new Vector2(16f, 44f),
                    1f,
                    facing,
                    0f);
            }
            else
            {
                int blinkFrame = (int)(ambientTime * 2f) % 7 == 0 ? 1 : 0;
                batch.Draw(
                    burnedSprite,
                    characterRoot,
                    new Rectangle(blinkFrame * 24, 0, 24, 32),
                    Color.White,
                    0f,
                    new Vector2(12f, 32f),
                    1f,
                    SpriteEffects.None,
                    0f);

                // After the blast, the dropped bucket falls from overhead onto the roof.
                float fallProgress = MathHelper.Clamp((ExplosionDurationSeconds - explosionTimer) / 0.48f, 0f, 1f);
                int bucketBottom = (int)MathHelper.Lerp(
                    PixelArt.GroundTop - 28,
                    PixelArt.GroundTop,
                    fallProgress * fallProgress);

                batch.Draw(
                    burnedBucketSprite,
                    new Vector2((int)bucket.X, bucketBottom),
                    null,
                    Color.White,
                    0f,
                    new Vector2(14f, 16f),
                    1f,
                    SpriteEffects.None,
                    0f);

                // Small rising smoke wisps
                for (int i = 0; i < 3; i++)
                {
                    int rise = (int)((ambientTime * 8f + i * 5f) % 16f);
                    int size = 2 + i;
                    float smokeOpacity = 0.5f - rise * 0.025f;
                    DrawRect(
                        (int)bucket.X - 4 + i * 3,
                        PixelArt.GroundTop - 7 - rise,
                        size,
                        size,
                        SmokePuffColor * smokeOpacity);
                }
            }

            // Sparks, score popups, explosion sprite, and warm screen flash
            foreach (Particle particle in particles)
            {
                DrawRect((int)particle.Position.X, (int)particle.Position.Y, 1, 1, particle.Color * particle.Opacity);
            }

            foreach (Popup popup in popups)
            {
                DrawText(popup.Text, popup.Position, popup.Color * popup.Opacity, scale: 1);
            }

            if (CurrentPhase == RoundPhase.GameOver && explosionTimer > 0f)
            {
                float progress = (ExplosionDurationSeconds - explosionTimer) / ExplosionDurationSeconds;
                int explosionFrame = Math.Min(7, (int)(progress * 8f));
                batch.Draw(
                    explosionSheet,
                    new Vector2((int)bucket.X, PixelArt.GroundTop - 31),
                    new Rectangle(explosionFrame * 48, 0, 48, 48),
                    Color.White,
                    0f,
                    new Vector2(24f, 24f),
                    1f,
                    SpriteEffects.None,
                    0f);
            }

            if (flash > 0f)
            {
                DrawRect(0, 0, PixelArt.FieldWidth, PixelArt.Height, WarmTitleColor * flash);
            }

            DrawHud();

            if (flow.IsLeaderboardOpen)
            {
                DrawLeaderboardScreen();
            }
            else if (CurrentPhase == RoundPhase.Ready)
            {
                DrawReadyScreen();
            }
            else if (CurrentPhase == RoundPhase.Paused)
            {
                DrawPausedScreen();
            }
            else if (CurrentPhase == RoundPhase.GameOver && explosionTimer <= 0f)
            {
                DrawGameOverScreen();
            }
        }

        private void DrawHud()
        {
            DrawText("SCORE", new Vector2(4f, 8f), HudLabelColor, scale: 1);
            BigInteger visibleScore = CurrentPhase == RoundPhase.GameOver ? lastRunCash : run.Cash;
            DrawHudAmount(visibleScore, 19, ScoreValueColor);

            DrawText("BEST", new Vector2(4f, 34f), HudLabelColor, scale: 1);
            BigInteger bestScore = GetCurrentBestScore();
            DrawHudAmount(bestScore, 45, BestValueColor);

            bool pauseAvailable = CurrentPhase == RoundPhase.Playing || CurrentPhase == RoundPhase.Paused;
            batch.Draw(uiAudio, new Vector2(PauseButton.X, PauseButton.Y),
                new Rectangle(CurrentPhase == RoundPhase.Paused ? 100 : 80, 0, 20, 16),
                pauseAvailable ? Color.White : new Color(130, 125, 145));
            Rectangle musicIconRect = new Rectangle(musicMuted ? 20 : 0, 0, 20, 16);
            batch.Draw(uiAudio, new Vector2(MusicButton.X, MusicButton.Y), musicIconRect, Color.White);

            Rectangle sfxIconRect = new Rectangle(sfxMuted ? 60 : 40, 0, 20, 16);
            batch.Draw(uiAudio, new Vector2(SfxButton.X, SfxButton.Y), sfxIconRect, Color.White);

            bool canUseLeaderboardIcon =
                flow.IsLeaderboardOpen ||
                CurrentPhase == RoundPhase.Ready ||
                (CurrentPhase == RoundPhase.GameOver && explosionTimer <= 0f);
            int lbFrame = !canUseLeaderboardIcon
                ? 2
                : (flow.IsLeaderboardOpen || isLeaderboardButtonHovered) ? 1 : 0;
            Rectangle lbIconRect = new Rectangle(lbFrame * 20, 0, 20, 16);
            batch.Draw(uiLeaderboard, new Vector2(LeaderboardIconButton.X, LeaderboardIconButton.Y), lbIconRect, Color.White);
        }

        private BigInteger GetCurrentBestScore()
        {
            BigInteger best = BigInteger.Max(run.BestCash, bestKnownCash);
            if (leaderboardService != null)
            {
                best = BigInteger.Max(best, leaderboardService.GetPersonalBest());
            }

            return best;
        }

        private void DrawHudAmount(BigInteger amount, int y, Color color)
        {
            const int left = 4;
            const int rightPadding = 3;
            int availableWidth = PixelArt.PlayLeft - rightPadding - left;
            string label = FitCompactRupeeAmount(amount, availableWidth);
            DrawText(label, new Vector2(left, y), color, scale: 1);
        }

        private static string FitCompactRupeeAmount(BigInteger amount, int availableWidth)
        {
            string compact = CashDisplay.Compact(amount);
            string label = "RS " + compact;
            // Tighten the currency gap first; never stretch or shrink the pixel font.
            if (PixelFont.Width(label, 1) > availableWidth)
            {
                label = "RS" + compact;
            }

            // Large crore amounts may need fewer fractional digits to fit narrow columns.
            while (PixelFont.Width(label, 1) > availableWidth && compact.Contains('.'))
            {
                int dot = compact.IndexOf('.');
                int end = dot + 1;
                while (end < compact.Length && char.IsDigit(compact[end]))
                {
                    end++;
                }

                compact = compact.Remove(end - 1, 1);
                if (end - 1 == dot + 1)
                {
                    compact = compact.Remove(dot, 1);
                }

                label = "RS" + compact;
            }

            return label;
        }

        private void DrawReadyScreen()
        {
            batch.Draw(uiSign, new Vector2(88f, 28f), Color.White);
            DrawPanelHeading(WarmTitleColor, "BILLU'S", "BUCKET");
            DrawButton("PLAY", flow.MainButton, isMainButtonHovered);
        }

        private void DrawPausedScreen()
        {
            batch.Draw(uiSign, new Vector2(88f, 28f), Color.White);
            DrawPanelHeading(WarmTitleColor, "PAUSED");
            DrawButton("RESUME", flow.MainButton, isMainButtonHovered);
        }

        private void DrawGameOverScreen()
        {
            batch.Draw(uiSign, new Vector2(88f, 28f), Color.White);
            DrawPanelHeading(GameOverTitleColor, "GAME OVER");
            DrawButton("RETRY", flow.MainButton, isMainButtonHovered);
        }

        private void DrawLeaderboardScreen()
        {
            Rectangle panel = new Rectangle(50, 8, 220, 146);
            DrawTimberPanel(panel);

            LeaderboardSnapshot snapshot = leaderboardService?.CurrentSnapshot
                ?? new LeaderboardSnapshot(GetCurrentBestScore(), statusMessage: "PERSONAL BEST SAVED ON DEVICE");
            LeaderboardCapabilities capabilities = leaderboardService?.Capabilities;

            BigInteger bestScore = BigInteger.Max(GetCurrentBestScore(), snapshot.PersonalBest);

            DrawCenteredText("WEEKLY TOP 10", y: 16f, WarmTitleColor, scale: 1);

            string personalLine = "YOUR BEST: " + FitCompactRupeeAmount(bestScore, 80);
            if (snapshot.CurrentPlayerWeeklyRank.HasValue)
            {
                personalLine += "  RANK #" + snapshot.CurrentPlayerWeeklyRank.Value;
            }
            DrawCenteredText(personalLine, y: 26f, BestValueColor, scale: 1);

            DrawRect(panel.X + 10, 35, panel.Width - 20, 1, PanelTimberColor);

            if (snapshot.IsLoading && snapshot.WeeklyTop10.Count == 0)
            {
                DrawCenteredText("LOADING WEEKLY SCORES...", y: 78f, HudLabelColor, scale: 1);
            }
            else if (snapshot.WeeklyTop10.Count > 0)
            {
                int rowCount = Math.Min(10, snapshot.WeeklyTop10.Count);
                for (int i = 0; i < rowCount; i++)
                {
                    LeaderboardRankEntry entry = snapshot.WeeklyTop10[i];
                    int rowY = 39 + i * 10;
                    if (rowCount > 9)
                    {
                        rowY = 39 + i * 9;
                    }

                    Color rowColor = entry.IsCurrentPlayer
                        ? BestValueColor
                        : entry.Rank == 1
                            ? WarmTitleColor
                            : entry.Rank <= 3
                                ? ScoreValueColor
                                : ButtonTextColor;

                    DrawText(entry.Rank + ".", new Vector2(60f, rowY), rowColor, scale: 1);
                    DrawText(entry.DisplayName, new Vector2(82f, rowY), rowColor, scale: 1);

                    string scoreText = FitCompactRupeeAmount(entry.Score, 84);
                    DrawRightAlignedText(scoreText, rightX: 260, y: rowY, rowColor, scale: 1);
                }
            }
            else
            {
                string primaryStatus = string.IsNullOrWhiteSpace(snapshot.StatusMessage)
                    ? "PERSONAL BEST SAVED ON DEVICE"
                    : snapshot.StatusMessage;

                string secondaryStatus;
                if (capabilities != null && capabilities.UsesNativePortalOverlay)
                {
                    secondaryStatus = "SCORES SUBMIT AFTER EACH RUN";
                }
                else if (capabilities != null && capabilities.SupportsWeeklyTop10)
                {
                    secondaryStatus = snapshot.IsOnlineAvailable
                        ? "FINISH A RUN TO SET RECORD"
                        : "PLAY NORMALLY WHILE OFFLINE";
                }
                else
                {
                    secondaryStatus = "ONLINE WEEKLY BOARD INACTIVE";
                }

                DrawCenteredText(primaryStatus, y: 70f, ButtonTextColor, scale: 1);
                DrawCenteredText(secondaryStatus, y: 86f, HudLabelColor, scale: 1);
            }

            DrawButton("BACK", flow.BackButton, isBackButtonHovered);
        }

        private void DrawRightAlignedText(string text, int rightX, int y, Color color, int scale)
        {
            int width = PixelFont.Width(text, scale);
            DrawText(text, new Vector2(rightX - width, y), color, scale);
        }

        /// <summary>
        /// Draws a weathered rooftop timber panel matching ui-sign.png's exact palette and chamfered corners.
        /// </summary>
        private void DrawTimberPanel(Rectangle bounds)
        {
            // Chamfered 1px dark ink silhouette
            DrawRect(bounds.X + 2, bounds.Y, bounds.Width - 4, bounds.Height, PanelInkColor);
            DrawRect(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2, PanelInkColor);
            DrawRect(bounds.X, bounds.Y + 2, bounds.Width, bounds.Height - 4, PanelInkColor);

            // Outer weathered timber frame
            DrawRect(bounds.X + 2, bounds.Y + 1, bounds.Width - 4, bounds.Height - 2, PanelTimberColor);
            DrawRect(bounds.X + 1, bounds.Y + 2, bounds.Width - 2, bounds.Height - 4, PanelTimberColor);

            // Inner dark slate face & top/bottom trim highlights
            int innerX = bounds.X + 5;
            int innerWidth = bounds.Width - 10;
            DrawRect(innerX, bounds.Y + 4, innerWidth, 2, PanelTopTrimColor);
            DrawRect(innerX + 6, bounds.Y + 5, Math.Min(14, Math.Max(0, innerWidth - 12)), 1, PanelTopGlintColor);
            DrawRect(innerX, bounds.Y + 7, innerWidth, Math.Max(1, bounds.Height - 13), PanelFaceColor);
            DrawRect(innerX + 2, bounds.Bottom - 5, Math.Max(0, innerWidth - 4), 1, PanelBottomTrimColor);
        }

        private void DrawPanelHeading(Color color, params string[] lines)
        {
            // Visible panel face above the overlapping button: y=49 through 84.
            const int scale = 2;
            const int lineGap = 5;
            int lineHeight = PixelFont.Height * scale;
            int height = lines.Length * lineHeight + (lines.Length - 1) * lineGap + 1;
            int top = 49 + (int)Math.Round((36 - height) / 2f, MidpointRounding.AwayFromZero);
            for (int i = 0; i < lines.Length; i++)
            {
                DrawCenteredCompactHeading(lines[i], top + i * (lineHeight + lineGap), color);
            }
        }

        /// <summary>
        /// Draws ui-button.png with crisp 3-slice horizontal caps so wider labels ("PLAY AGAIN", "LEADERBOARD")
        /// never distort the pixel-art bevels.
        /// </summary>
        private void DrawButton(string label, ButtonBounds bounds, bool isHovered)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            int frameX = isHovered ? 68 : 0;
            const int capWidth = 8;
            const int sourceWidth = 68;
            const int sourceHeight = 18;

            if (bounds.Width == sourceWidth)
            {
                batch.Draw(
                    uiButton,
                    new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    new Rectangle(frameX, 0, sourceWidth, sourceHeight),
                    Color.White);
            }
            else
            {
                int middleDestWidth = Math.Max(1, bounds.Width - capWidth * 2);
                int middleSourceWidth = sourceWidth - capWidth * 2;

                batch.Draw(
                    uiButton,
                    new Rectangle(bounds.X, bounds.Y, capWidth, bounds.Height),
                    new Rectangle(frameX, 0, capWidth, sourceHeight),
                    Color.White);
                batch.Draw(
                    uiButton,
                    new Rectangle(bounds.X + capWidth, bounds.Y, middleDestWidth, bounds.Height),
                    new Rectangle(frameX + capWidth, 0, middleSourceWidth, sourceHeight),
                    Color.White);
                batch.Draw(
                    uiButton,
                    new Rectangle(bounds.X + bounds.Width - capWidth, bounds.Y, capWidth, bounds.Height),
                    new Rectangle(frameX + sourceWidth - capWidth, 0, capWidth, sourceHeight),
                    Color.White);
            }

            // Center the 6px-tall text (+1px shadow = 7px) inside the inner button face.
            int innerWidth = bounds.Width - 6;
            int textWidth = PixelFont.Width(label, scale: 1);
            int labelX = bounds.X + 3 + (int)Math.Round((innerWidth - textWidth) / 2f, MidpointRounding.AwayFromZero);
            int labelY = bounds.Y + 3 + (11 - (PixelFont.Height + 1)) / 2;
            DrawText(label, new Vector2(labelX, labelY), ButtonTextColor, scale: 1);
        }

        private void DrawCenteredText(string text, float y, Color color, int scale)
        {
            float x = (int)Math.Round((PixelArt.FieldWidth - PixelFont.Width(text, scale)) / 2f, MidpointRounding.AwayFromZero);
            DrawText(text, new Vector2(x, y), color, scale);
        }

        private void DrawCenteredCompactHeading(string text, float y, Color color)
        {
            const int headingScale = 2;
            float x = (int)Math.Round((PixelArt.FieldWidth - PixelFont.Width(text, headingScale)) / 2f, MidpointRounding.AwayFromZero);
            Color shadow = TextShadowColor * (color.A / 255f);

            PixelFont.Draw(batch, pixel, text, new Vector2(x, y + 1f), shadow, headingScale);
            PixelFont.Draw(batch, pixel, text, new Vector2(x, y), color, headingScale);
        }

        private void DrawText(string text, Vector2 position, Color color, int scale)
        {
            // Draw a 1-pixel dark plum drop shadow so letters stay readable against the dusk sky.
            Color shadow = TextShadowColor * (color.A / 255f);
            PixelFont.Draw(batch, pixel, text, position + new Vector2(0f, 1f), shadow, scale);
            PixelFont.Draw(batch, pixel, text, position, color, scale);
        }

        private void DrawRect(int x, int y, int width, int height, Color color)
        {
            if (width > 0 && height > 0)
            {
                batch.Draw(pixel, new Rectangle(x, y, width, height), color);
            }
        }

        // =====================================================================
        // Helpers & Responsive 16:9 Coordinate Mapping
        // =====================================================================

        private Texture2D LoadArt(string fileName)
        {
            using Stream stream = assetProvider.OpenRead(fileName);
            return Texture2D.FromStream(GraphicsDevice, stream);
        }

        /// <summary>
        /// Builds a 3-frame 20x16 pixel-art trophy button sprite sheet matching ui-audio.png's exact
        /// chamfered frame and palette (0 = normal, 1 = hovered/open, 2 = inactive during gameplay).
        /// </summary>
        private Texture2D CreateLeaderboardIconSheet()
        {
            const int iconWidth = 20;
            const int iconHeight = 16;
            const int frameCount = 3;
            const int sheetWidth = iconWidth * frameCount;

            string[] trophyRows =
            {
                "001111111100",
                "111111111111",
                "101111111101",
                "011111111110",
                "000111111000",
                "000011110000",
                "000001100000",
                "000001100000",
                "000111111000",
                "001111111100"
            };

            Color ink = new Color(35, 36, 56);
            Color[] fills =
            {
                new Color(52, 83, 111),
                new Color(60, 134, 167),
                new Color(80, 76, 96)
            };
            Color[] highlights =
            {
                new Color(119, 183, 200),
                new Color(155, 200, 207),
                new Color(148, 144, 157)
            };
            Color[] glyphs =
            {
                new Color(241, 227, 191),
                new Color(240, 211, 152),
                new Color(148, 144, 157)
            };

            Color[] pixels = new Color[sheetWidth * iconHeight];

            for (int frame = 0; frame < frameCount; frame++)
            {
                int offsetX = frame * iconWidth;
                for (int y = 0; y < iconHeight; y++)
                {
                    int dy = Math.Min(y, iconHeight - 1 - y);
                    for (int x = 0; x < iconWidth; x++)
                    {
                        int dx = Math.Min(x, iconWidth - 1 - x);
                        Color pixelColor;
                        if (dx + dy < 2)
                        {
                            pixelColor = Color.Transparent;
                        }
                        else if (dx == 0 || dy == 0 || dx + dy == 2)
                        {
                            pixelColor = ink;
                        }
                        else if (y == 1 && x >= 3 && x <= 16)
                        {
                            pixelColor = highlights[frame];
                        }
                        else
                        {
                            pixelColor = fills[frame];
                        }

                        pixels[y * sheetWidth + offsetX + x] = pixelColor;
                    }
                }

                for (int row = 0; row < trophyRows.Length; row++)
                {
                    string bits = trophyRows[row];
                    for (int col = 0; col < bits.Length; col++)
                    {
                        if (bits[col] == '1')
                        {
                            int px = offsetX + 4 + col;
                            int py = 3 + row;
                            pixels[py * sheetWidth + px] = glyphs[frame];
                        }
                    }
                }
            }

            Texture2D sheet = new Texture2D(GraphicsDevice, sheetWidth, iconHeight);
            sheet.SetData(pixels);
            return sheet;
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

        private static BucketControl CreateCenteredBucket()
        {
            return new BucketControl(
                startX: PixelArt.FieldWidth / 2f,
                playfieldWidth: PixelArt.PlayWidth,
                bucketWidth: PlayerCollisionWidth,
                playfieldLeft: PixelArt.PlayLeft);
        }

        private void SyncTouchPanelDisplaySize()
        {
            int viewportWidth = GraphicsDevice?.Viewport.Width ?? PixelArt.WindowWidth;
            int viewportHeight = GraphicsDevice?.Viewport.Height ?? PixelArt.WindowHeight;
            if (viewportWidth > 0 && viewportHeight > 0)
            {
                if (TouchPanel.DisplayWidth != viewportWidth)
                {
                    TouchPanel.DisplayWidth = viewportWidth;
                }
                if (TouchPanel.DisplayHeight != viewportHeight)
                {
                    TouchPanel.DisplayHeight = viewportHeight;
                }
            }
        }

#if BLAZORGL
        private void SyncBrowserBackBufferToWindow()
        {
            int clientWidth = Window?.ClientBounds.Width ?? 0;
            int clientHeight = Window?.ClientBounds.Height ?? 0;
            if (clientWidth <= 0 || clientHeight <= 0)
            {
                return;
            }

            int currentWidth = GraphicsDevice?.PresentationParameters.BackBufferWidth ?? 0;
            int currentHeight = GraphicsDevice?.PresentationParameters.BackBufferHeight ?? 0;
            if (currentWidth != clientWidth || currentHeight != clientHeight)
            {
                graphics.PreferredBackBufferWidth = clientWidth;
                graphics.PreferredBackBufferHeight = clientHeight;
                graphics.ApplyChanges();
            }
        }
#endif

        private Rectangle GetCanvasDestinationRect()
        {
            int viewportWidth = GraphicsDevice?.Viewport.Width ?? PixelArt.WindowWidth;
            int viewportHeight = GraphicsDevice?.Viewport.Height ?? PixelArt.WindowHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0)
            {
                return new Rectangle(0, 0, PixelArt.WindowWidth, PixelArt.WindowHeight);
            }

            int destWidth;
            int destHeight;
            if ((long)viewportWidth * PixelArt.Height <= (long)viewportHeight * PixelArt.FieldWidth)
            {
                destWidth = viewportWidth;
                destHeight = Math.Max(1, (viewportWidth * PixelArt.Height) / PixelArt.FieldWidth);
            }
            else
            {
                destHeight = viewportHeight;
                destWidth = Math.Max(1, (viewportHeight * PixelArt.FieldWidth) / PixelArt.Height);
            }

            int offsetX = (viewportWidth - destWidth) / 2;
            int offsetY = (viewportHeight - destHeight) / 2;
            return new Rectangle(offsetX, offsetY, destWidth, destHeight);
        }

        private static bool IsInsideCanvas(float screenX, float screenY, Rectangle canvasDestination)
        {
            return screenX >= canvasDestination.X &&
                   screenX < canvasDestination.X + canvasDestination.Width &&
                   screenY >= canvasDestination.Y &&
                   screenY < canvasDestination.Y + canvasDestination.Height;
        }

        private static Point ScreenToCanvas(float screenX, float screenY, Rectangle canvasDestination)
        {
            float normalizedX = (screenX - canvasDestination.X) / Math.Max(1, canvasDestination.Width);
            float normalizedY = (screenY - canvasDestination.Y) / Math.Max(1, canvasDestination.Height);
            int canvasX = (int)MathF.Floor(normalizedX * PixelArt.FieldWidth);
            int canvasY = (int)MathF.Floor(normalizedY * PixelArt.Height);
            return new Point(canvasX, canvasY);
        }

        private static float ScreenToCanvasFloatX(float screenX, Rectangle canvasDestination)
        {
            float normalizedX = (screenX - canvasDestination.X) / Math.Max(1, canvasDestination.Width);
            return normalizedX * PixelArt.FieldWidth;
        }
    }
}
