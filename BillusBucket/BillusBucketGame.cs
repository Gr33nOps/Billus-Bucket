using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

// Import only BigInteger so it does not conflict with MonoGame.Vector2.
using BigInteger = System.Numerics.BigInteger;

namespace BillusBucket
{
    /// <summary>
    /// START HERE: MonoGame calls Update repeatedly while the game is running.
    /// This file decides which screen should handle this frame.
    /// Read Game/Playing.cs next to follow one active frame of gameplay.
    /// </summary>
    public sealed partial class BillusBucketGame : Game
    {
        // Limit a slow frame to 0.05 simulated seconds so a stall cannot jump the whole scene.
        private const float MaxFrameDeltaSeconds = 0.05f;
        private RoundPhase CurrentPhase => roundFlow.Phase;

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

        protected override void Update(GameTime gameTime)
        {
            if (reviewTools.IsArtReviewMode)
            {
                ArtReviewFrame frame = reviewTools.PrepareArtReviewState(roundFlow, billuAnimator, drops, bucketFeedback);
                currentRun = frame.Run;
                lastRunCash = frame.LastRunCash;
                explosionCountdown = frame.ExplosionTimer;
                sceneSeconds = frame.AmbientTime;
                runSeconds = frame.AmbientTime;
                ClearHoverStates();
                return;
            }

#if BLAZORGL
            SyncBrowserBackBufferToWindow();
#endif
            SyncTouchPanelDisplaySize();

            // A frame is one pass through Update. deltaSeconds is the time since the last pass.
            float deltaSeconds = Math.Min(MaxFrameDeltaSeconds, (float)gameTime.ElapsedGameTime.TotalSeconds);
            rankingsToastTimer = Math.Max(0f, rankingsToastTimer - deltaSeconds);

            // Read the devices once, then share that snapshot with this frame's helpers.
            KeyboardState keys = Keyboard.GetState();
            MouseState mouse = Mouse.GetState();
            TouchCollection touch = TouchPanel.GetState();
            bool touchDown = touch.Count > 0;
            bool clicked = mouse.LeftButton == ButtonState.Pressed && previousMouse.LeftButton == ButtonState.Released;
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
                roundFlow.OnFocusLost();
                if (wasPlaying)
                {
                    leaderboardService?.FlushLocalState();
                }

                platformBridge.NotifyGameplayStop();
                ClearHoverStates();
                audio?.Update(deltaSeconds, CurrentPhase, isWindowActive: false, platformBridge.IsExternalAudioMuted);
                SaveInputState(keys, mouse, touchDown);
                base.Update(gameTime);
                return;
            }

            bool pauseRequested = false;

            // Handle the HUD before allowing a pointer to control Billu.
            if (clicked || tapped)
            {
                Point tapPoint = tapped ? ScreenToCanvas(touch[0].Position.X, touch[0].Position.Y, canvasViewport) : ScreenToCanvas(mouse.X, mouse.Y, canvasViewport);
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
                    ApplyAudioPreferences();
                    ignorePointerUntilRelease = true;
                    clicked = false;
                    tapped = false;
                }
                else if (SfxButton.Contains(tapPoint.X, tapPoint.Y))
                {
                    sfxMuted = !sfxMuted;
                    ApplyAudioPreferences();
                    ignorePointerUntilRelease = true;
                    clicked = false;
                    tapped = false;
                }
            }

            // Do not mute audio when the player types the letter 'M' in their name or Save Code!
            if (!IsTypingInKeypadModal && WasKeyJustPressed(keys, Keys.M))
            {
                bool bothCurrentlyMuted = musicMuted && sfxMuted;
                bool shouldMuteBoth = !bothCurrentlyMuted;
                musicMuted = shouldMuteBoth;
                sfxMuted = shouldMuteBoth;
                ApplyAudioPreferences();
            }

            // Remember the old screen so pause/resume sounds play only when it changes.
            RoundPhase phaseBefore = CurrentPhase;
            if (CurrentPhase != RoundPhase.Paused)
            {
                UpdateVisualEffects(deltaSeconds);
            }

            // Only the active screen handles input. UpdateRun contains the actual gameplay.
            if (roundFlow.IsLeaderboardOpen)
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
                    roundFlow.TogglePause();
                }
                else
                {
                    UpdateRun(deltaSeconds, keys, mouse, touch, canvasViewport);
                }
            }
            else if (CurrentPhase == RoundPhase.Paused)
            {
                if (pauseRequested || WasKeyJustPressed(keys, Keys.Escape) || WasKeyJustPressed(keys, Keys.P))
                {
                    roundFlow.TogglePause();
                }
                else
                {
                    CheckMainMenuInput(keys, mouse, touch, clicked, tapped, canvasViewport);
                }
            }
            else if (CurrentPhase == RoundPhase.GameOver)
            {
                if (explosionCountdown <= 0f)
                {
                    // Prompt once after the player's first scored run finishes its explosion animation
                    // if they haven't chosen a custom player name yet.
                    if (!hasAutoPromptedForName && lastRunCash > BigInteger.Zero && leaderboardService != null && !leaderboardService.HasCustomPlayerName && !reviewTools.IsLiveReviewMode)
                    {
                        hasAutoPromptedForName = true;
                        OpenLeaderboardScreen(autoOpenNameEditor: true, fromPostRunPrompt: true);
                    }
                    else if (CheckLeaderboardOpenInput(keys, mouse, touch, clicked, tapped, canvasViewport))
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
            audio?.Update(deltaSeconds, CurrentPhase, IsActive, platformBridge.IsExternalAudioMuted);
            SaveInputState(keys, mouse, touchDown);
            base.Update(gameTime);
        }
    }
}
