using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace BillusBucket
{
    /// <summary>
    /// Keyboard, mouse, and touch helpers, including screen-to-game coordinates.
    /// All button positions and movement targets use the same 320 by 180 game canvas.
    /// </summary>
    public sealed partial class BillusBucketGame
    {
        // Top-right HUD icon button hitboxes (in 320x180 canvas coordinates)
        private static readonly ButtonBounds PauseButton = new ButtonBounds(292, 8, 20, 16);
        private static readonly ButtonBounds MusicButton = new ButtonBounds(292, 29, 20, 16);
        private static readonly ButtonBounds SfxButton = new ButtonBounds(292, 50, 20, 16);
        private static readonly ButtonBounds LeaderboardIconButton = RoundFlow.TopRightLeaderboardButton;

        // Input tracking across frames
        private KeyboardState previousKeys;
        private MouseState previousMouse;
        private bool previousTouch;
        private bool ignorePointerUntilRelease;
        private bool isMainButtonHovered;
        private bool isSupportButtonHovered;

        // Mute flags are stored here so the UI buttons still toggle when no audio hardware is present.
        private bool musicMuted;
        private bool sfxMuted;

        public void SuspendForBrowser()
        {
            roundFlow.OnFocusLost();
            leaderboardService?.FlushLocalState();
            platformBridge.NotifyGameplayStop();
            audio?.Update(0f, CurrentPhase, isWindowActive: false, isExternallyMuted: true);
        }

        private void ApplyAudioPreferences()
        {
            audio?.SetMuted(musicMuted, sfxMuted);
            platformBridge.SaveAudioPreferences(new AudioPreferences(musicMuted, sfxMuted).Serialize());
        }

        private void ClearHoverStates()
        {
            isMainButtonHovered = false;
            isSupportButtonHovered = false;
            isLeaderboardButtonHovered = false;
            isBackButtonHovered = false;
            isLeaderboardNameHovered = false;
            isLeaderboardSyncHovered = false;
            isModalCancelHovered = false;
            isModalConfirmHovered = false;
            isModalPasteHovered = false;
            isSyncCopyHovered = false;
            isSyncEnterCodeHovered = false;
            hoveredKeypadIndex = -1;
        }

        private void UpdateButtonHoverStates(MouseState mouse, Rectangle canvasViewport)
        {
            ClearHoverStates();
            if (!IsInsideCanvas(mouse.X, mouse.Y, canvasViewport))
            {
                return;
            }

            Point mouseCanvas = ScreenToCanvas(mouse.X, mouse.Y, canvasViewport);
            isMainButtonHovered = roundFlow.MainButton.Contains(mouseCanvas.X, mouseCanvas.Y);
            isSupportButtonHovered = explosionCountdown <= 0f &&
                roundFlow.SupportButton.Contains(mouseCanvas.X, mouseCanvas.Y);
            bool canUseLeaderboardIcon = roundFlow.IsLeaderboardOpen || CurrentPhase == RoundPhase.Ready || (CurrentPhase == RoundPhase.GameOver && explosionCountdown <= 0f);
            isLeaderboardButtonHovered = canUseLeaderboardIcon && LeaderboardIconButton.Contains(mouseCanvas.X, mouseCanvas.Y);
            if (!roundFlow.IsLeaderboardOpen)
            {
                return;
            }

            if (leaderboardSubView == LeaderboardSubView.Rankings)
            {
                isLeaderboardNameHovered = LeaderboardNameButton.Contains(mouseCanvas.X, mouseCanvas.Y);
                isBackButtonHovered = roundFlow.BackButton.Contains(mouseCanvas.X, mouseCanvas.Y);
                isLeaderboardSyncHovered = LeaderboardSyncButton.Contains(mouseCanvas.X, mouseCanvas.Y);
            }
            else if (leaderboardSubView == LeaderboardSubView.SyncOverview)
            {
                isSyncCopyHovered = SyncCopyButton.Contains(mouseCanvas.X, mouseCanvas.Y);
                isSyncEnterCodeHovered = SyncEnterCodeButton.Contains(mouseCanvas.X, mouseCanvas.Y);
                isBackButtonHovered = roundFlow.BackButton.Contains(mouseCanvas.X, mouseCanvas.Y);
            }
            else if (leaderboardSubView == LeaderboardSubView.EditName || leaderboardSubView == LeaderboardSubView.SyncEnterCode)
            {
                isModalCancelHovered = !isProfileBusy && GetModalCancelButton().Contains(mouseCanvas.X, mouseCanvas.Y);
                isModalConfirmHovered = !isProfileBusy && GetModalConfirmButton().Contains(mouseCanvas.X, mouseCanvas.Y);
                isModalPasteHovered = !isProfileBusy && leaderboardSubView == LeaderboardSubView.SyncEnterCode && CodePasteButton.Contains(mouseCanvas.X, mouseCanvas.Y);
                if (!isProfileBusy)
                {
                    bool allowPunctuation = leaderboardSubView == LeaderboardSubView.EditName;
                    for (int i = 0; i < OnScreenKeypad.Length; i++)
                    {
                        KeypadKey key = OnScreenKeypad[i];
                        if (!allowPunctuation && !key.IsBackspace && (key.Character == '_' || key.Character == '-'))
                        {
                            continue;
                        }

                        if (key.Bounds.Contains(mouseCanvas.X, mouseCanvas.Y))
                        {
                            hoveredKeypadIndex = i;
                            break;
                        }
                    }
                }
            }
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
                return roundFlow.ActivateMainAction();
            }

            if (clicked)
            {
                Point canvasPoint = ScreenToCanvas(mouse.X, mouse.Y, canvasViewport);
                if (roundFlow.SupportButton.Contains(canvasPoint.X, canvasPoint.Y))
                {
                    platformBridge.OpenSupportPage();
                    ignorePointerUntilRelease = true;
                    return RoundAction.None;
                }
                RoundAction action = roundFlow.TryMainActionAt(canvasPoint.X, canvasPoint.Y);
                if (action != RoundAction.None)
                {
                    ignorePointerUntilRelease = true;
                }

                return action;
            }

            if (tapped)
            {
                Point canvasPoint = ScreenToCanvas(touch[0].Position.X, touch[0].Position.Y, canvasViewport);
                if (roundFlow.SupportButton.Contains(canvasPoint.X, canvasPoint.Y))
                {
                    platformBridge.OpenSupportPage();
                    ignorePointerUntilRelease = true;
                    return RoundAction.None;
                }
                RoundAction action = roundFlow.TryMainActionAt(canvasPoint.X, canvasPoint.Y);
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
                if (roundFlow.LeaderboardButton.Contains(canvasPoint.X, canvasPoint.Y))
                {
                    ignorePointerUntilRelease = true;
                    return true;
                }
            }

            if (tapped)
            {
                Point canvasPoint = ScreenToCanvas(touch[0].Position.X, touch[0].Position.Y, canvasViewport);
                if (roundFlow.LeaderboardButton.Contains(canvasPoint.X, canvasPoint.Y))
                {
                    ignorePointerUntilRelease = true;
                    return true;
                }
            }

            return false;
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
            return PauseButton.Contains(canvasPoint.X, canvasPoint.Y) || MusicButton.Contains(canvasPoint.X, canvasPoint.Y) || SfxButton.Contains(canvasPoint.X, canvasPoint.Y) || LeaderboardIconButton.Contains(canvasPoint.X, canvasPoint.Y);
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

            int safeX = 0, safeY = 0;
            GameViewportBounds visible = platformBridge.ViewportBounds;
            if (visible != null && visible.Width > 0 && visible.Height > 0)
            {
                safeX = Math.Clamp(visible.X, 0, viewportWidth - 1);
                safeY = Math.Clamp(visible.Y, 0, viewportHeight - 1);
                viewportWidth = Math.Clamp(visible.Width, 1, viewportWidth - safeX);
                viewportHeight = Math.Clamp(visible.Height, 1, viewportHeight - safeY);
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
            return new Rectangle(safeX + offsetX, safeY + offsetY, destWidth, destHeight);
        }

        private static bool IsInsideCanvas(float screenX, float screenY, Rectangle canvasDestination)
        {
            return screenX >= canvasDestination.X && screenX < canvasDestination.X + canvasDestination.Width && screenY >= canvasDestination.Y && screenY < canvasDestination.Y + canvasDestination.Height;
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
