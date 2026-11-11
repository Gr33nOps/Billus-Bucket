using System;
using Microsoft.Xna.Framework;

// Import only BigInteger so it does not conflict with MonoGame.Vector2.
using BigInteger = System.Numerics.BigInteger;

namespace BillusBucket
{
    /// <summary>
    /// Draws the score, menus, buttons, panels, and text with the shared game palette.
    /// </summary>
    public sealed partial class BillusBucketGame
    {
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
        private static readonly Color InputBoxFillColor = new Color(49, 41, 56);
        private static readonly Color KeyFillColor = new Color(52, 83, 111);
        private static readonly Color KeyHoverFillColor = new Color(60, 134, 167);
        private static readonly Color KeyHighlightColor = new Color(119, 183, 200);
        private static readonly Color KeyHoverHighlightColor = new Color(155, 200, 207);
        private static readonly Color KeyDeleteFillColor = new Color(114, 86, 97);
        private static readonly Color KeyDisabledFillColor = new Color(64, 55, 68);

        private void DrawHud()
        {
            DrawHudText("SCORE", new Vector2(4f, 8f), WarmTitleColor);
            BigInteger visibleScore = CurrentPhase == RoundPhase.GameOver ? lastRunCash : currentRun.Cash;
            DrawHudAmount(visibleScore, 19, ScoreValueColor);
            DrawHudText("BEST", new Vector2(4f, 34f), WarmTitleColor);
            BigInteger bestScore = GetCurrentBestScore();
            DrawHudAmount(bestScore, 45, BestValueColor);
            bool pauseAvailable = CurrentPhase == RoundPhase.Playing || CurrentPhase == RoundPhase.Paused;
            spriteBatch.Draw(
                uiAudio,
                new Vector2(PauseButton.X, PauseButton.Y),
                new Rectangle(CurrentPhase == RoundPhase.Paused ? 100 : 80, 0, 20, 16),
                pauseAvailable ? Color.White : new Color(130, 125, 145)
            );
            Rectangle musicIconRect = new Rectangle(musicMuted ? 20 : 0, 0, 20, 16);
            spriteBatch.Draw(uiAudio, new Vector2(MusicButton.X, MusicButton.Y), musicIconRect, Color.White);
            Rectangle sfxIconRect = new Rectangle(sfxMuted ? 60 : 40, 0, 20, 16);
            spriteBatch.Draw(uiAudio, new Vector2(SfxButton.X, SfxButton.Y), sfxIconRect, Color.White);
            bool canUseLeaderboardIcon = roundFlow.IsLeaderboardOpen || CurrentPhase == RoundPhase.Ready || (CurrentPhase == RoundPhase.GameOver && explosionCountdown <= 0f);
            int lbFrame = !canUseLeaderboardIcon ? 2 : (roundFlow.IsLeaderboardOpen || isLeaderboardButtonHovered) ? 1 : 0;
            Rectangle lbIconRect = new Rectangle(lbFrame * 20, 0, 20, 16);
            spriteBatch.Draw(
                uiLeaderboard,
                new Vector2(LeaderboardIconButton.X, LeaderboardIconButton.Y),
                lbIconRect,
                Color.White
            );
        }

        private BigInteger GetCurrentBestScore()
        {
            BigInteger best = BigInteger.Max(currentRun.BestCash, bestKnownCash);
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
            DrawHudText(label, new Vector2(left, y), color);
        }

        private void DrawHudText(string text, Vector2 position, Color color)
        {
            DrawText(text, position, color, 1);
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
            spriteBatch.Draw(uiSign, new Vector2(88f, 28f), Color.White);
            DrawPanelHeading(WarmTitleColor, "BILLU'S", "BUCKET");
            DrawButton("PLAY", roundFlow.MainButton, isMainButtonHovered);
        }

        private void DrawPausedScreen()
        {
            spriteBatch.Draw(uiSign, new Vector2(88f, 28f), Color.White);
            DrawPanelHeading(WarmTitleColor, "PAUSED");
            DrawButton("RESUME", roundFlow.MainButton, isMainButtonHovered);
        }

        private void DrawGameOverScreen()
        {
            // A small support panel above the game-over sign, using the existing crying sprite.
            DrawTimberPanel(new Rectangle(88, 2, 144, 34));
            int faceFrame = (int)(sceneSeconds * 2f) % 7 == 0 ? 1 : 0;
            spriteBatch.Draw(burnedSprite, new Vector2(96, 10),
                new Rectangle(faceFrame * 24 + 3, 0, 18, 18), Color.White);
            DrawText("BUY BILLU CHAI!", new Vector2(118, 8), WarmTitleColor, 1);
            DrawButton("BUY CHAI", roundFlow.SupportButton, isSupportButtonHovered);

            spriteBatch.Draw(uiSign, new Vector2(88f, 40f), Color.White);
            DrawCenteredCompactHeading("GAME OVER", 73, GameOverTitleColor);
            DrawButton("RETRY", roundFlow.MainButton, isMainButtonHovered);
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
        /// Draws ui-button.png with crisp 3-slice horizontal caps so wider labels ("COPY CODE", "ENTER CODE")
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
                spriteBatch.Draw(
                    uiButton,
                    new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    new Rectangle(frameX, 0, sourceWidth, sourceHeight),
                    Color.White
                );
            }
            else
            {
                int middleDestWidth = Math.Max(1, bounds.Width - capWidth * 2);
                int middleSourceWidth = sourceWidth - capWidth * 2;
                spriteBatch.Draw(
                    uiButton,
                    new Rectangle(bounds.X, bounds.Y, capWidth, bounds.Height),
                    new Rectangle(frameX, 0, capWidth, sourceHeight),
                    Color.White
                );
                spriteBatch.Draw(
                    uiButton,
                    new Rectangle(bounds.X + capWidth, bounds.Y, middleDestWidth, bounds.Height),
                    new Rectangle(frameX + capWidth, 0, middleSourceWidth, sourceHeight),
                    Color.White
                );
                spriteBatch.Draw(
                    uiButton,
                    new Rectangle(bounds.X + bounds.Width - capWidth, bounds.Y, capWidth, bounds.Height),
                    new Rectangle(frameX + sourceWidth - capWidth, 0, capWidth, sourceHeight),
                    Color.White
                );
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
            PixelFont.Draw(spriteBatch, pixel, text, new Vector2(x, y + 1f), shadow, headingScale);
            PixelFont.Draw(spriteBatch, pixel, text, new Vector2(x, y), color, headingScale);
        }

        private void DrawText(string text, Vector2 position, Color color, int scale)
        {
            // Draw a 1-pixel dark plum drop shadow so letters stay readable against the dusk sky.
            Color shadow = TextShadowColor * (color.A / 255f);
            PixelFont.Draw(spriteBatch, pixel, text, position + new Vector2(0f, 1f), shadow, scale);
            PixelFont.Draw(spriteBatch, pixel, text, position, color, scale);
        }

        private void DrawRect(int x, int y, int width, int height, Color color)
        {
            if (width > 0 && height > 0)
            {
                spriteBatch.Draw(pixel, new Rectangle(x, y, width, height), color);
            }
        }
    }
}
