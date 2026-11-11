using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// Import only BigInteger so it does not conflict with MonoGame.Vector2.
using BigInteger = System.Numerics.BigInteger;

namespace BillusBucket
{
    /// <summary>
    /// Draws the leaderboard, name editor, and Save Code screens.
    /// </summary>
    public sealed partial class BillusBucketGame
    {
        private void DrawLeaderboardScreen()
        {
            DrawTimberPanel(LeaderboardPanel);
            if (leaderboardSubView == LeaderboardSubView.EditName)
            {
                DrawNameEditorModal();
                return;
            }

            if (leaderboardSubView == LeaderboardSubView.SyncOverview)
            {
                DrawSyncOverviewModal();
                return;
            }

            if (leaderboardSubView == LeaderboardSubView.SyncEnterCode)
            {
                DrawSyncEnterCodeModal();
                return;
            }

            DrawLeaderboardRankingsView(LeaderboardPanel);
        }

        private void DrawLeaderboardRankingsView(Rectangle panel)
        {
            LeaderboardSnapshot snapshot = leaderboardService?.CurrentSnapshot ?? new LeaderboardSnapshot(GetCurrentBestScore(), statusMessage: "PERSONAL BEST SAVED ON DEVICE");
            LeaderboardCapabilities capabilities = leaderboardService?.Capabilities;
            BigInteger bestScore = BigInteger.Max(GetCurrentBestScore(), snapshot.PersonalBest);
            string playerName = leaderboardService?.PlayerDisplayName ?? "PLAYER";
            DrawCenteredText(
                "LEADERBOARD",
                y: 17f,
                WarmTitleColor,
                scale: 1
            );
            DrawText("YOU: " + playerName, new Vector2(62f, 28f), BestValueColor, scale: 1);
            if (snapshot.CurrentPlayerWeeklyRank.HasValue)
            {
                DrawRightAlignedText(
                    "RANK #" + snapshot.CurrentPlayerWeeklyRank.Value,
                    rightX: 258,
                    y: 28,
                    ScoreValueColor,
                    scale: 1
                );
            }
            else
            {
                DrawRightAlignedText("BEST " + FitCompactRupeeAmount(bestScore, 64), rightX: 258, y: 28, BestValueColor, scale: 1);
            }

            DrawRect(panel.X + 10, 38, panel.Width - 20, 1, PanelTimberColor);
            if (snapshot.IsLoading && snapshot.WeeklyTop10.Count == 0)
            {
                DrawCenteredText("LOADING SCORES...", y: 84f, HudLabelColor, scale: 1);
            }
            else if (snapshot.WeeklyTop10.Count > 0)
            {
                int rowCount = Math.Min(10, snapshot.WeeklyTop10.Count);
                for (int i = 0; i < rowCount; i++)
                {
                    LeaderboardRankEntry entry = snapshot.WeeklyTop10[i];
                    int rowY = 43 + i * 9;
                    Color rowColor = entry.IsCurrentPlayer ? BestValueColor : entry.Rank == 1 ? WarmTitleColor : entry.Rank <= 3 ? ScoreValueColor : ButtonTextColor;
                    DrawText(entry.Rank + ".", new Vector2(62f, rowY), rowColor, scale: 1);
                    DrawText(entry.DisplayName, new Vector2(84f, rowY), rowColor, scale: 1);
                    string scoreText = FitCompactRupeeAmount(entry.Score, 84);
                    DrawRightAlignedText(scoreText, rightX: 258, y: rowY, rowColor, scale: 1);
                }
            }
            else
            {
                string primaryStatus = string.IsNullOrWhiteSpace(snapshot.StatusMessage) ? "PERSONAL BEST SAVED ON DEVICE" : snapshot.StatusMessage;
                string secondaryStatus;
                if (capabilities != null && capabilities.UsesNativePortalOverlay)
                {
                    secondaryStatus = "SCORES SUBMIT AFTER EACH RUN";
                }
                else if (capabilities != null && capabilities.SupportsWeeklyTop10)
                {
                    secondaryStatus = snapshot.IsOnlineAvailable ? "FINISH A RUN TO SET RECORD" : "PLAY NORMALLY WHILE OFFLINE";
                }
                else
                {
                    secondaryStatus = "ONLINE BOARD INACTIVE";
                }

                DrawCenteredText(FitLeaderboardStatus(primaryStatus), y: 78f, ButtonTextColor, scale: 1);
                DrawCenteredText(secondaryStatus, y: 94f, HudLabelColor, scale: 1);
            }

            string connectionStatus = snapshot.IsLoading ? "CONNECTING..." : capabilities?.SupportsWeeklyTop10 != true ? "SAVED ON THIS DEVICE" : snapshot.IsOnlineAvailable ? "ONLINE - TAP TO REFRESH" : snapshot.StatusMessage.StartsWith("OFFLINE", StringComparison.Ordinal) ? "OFFLINE - CACHED - RETRY" : snapshot.StatusMessage.Contains("TOO MANY") ? "PLEASE WAIT - TAP TO RETRY" : "UNAVAILABLE - TAP TO RETRY";
            if (rankingsToastTimer > 0f && !string.IsNullOrWhiteSpace(rankingsToastMessage))
            {
                connectionStatus = rankingsToastMessage;
            }

            DrawRect(61, 134, 198, 1, PanelTimberColor);
            DrawCenteredText(
                FitLeaderboardStatus(connectionStatus),
                y: 137f,
                snapshot.IsOnlineAvailable ? ScoreValueColor : HudLabelColor,
                scale: 1
            );
            DrawButton("NAME", LeaderboardNameButton, isLeaderboardNameHovered);
            DrawButton("BACK", roundFlow.BackButton, isBackButtonHovered);
            DrawButton("SYNC", LeaderboardSyncButton, isLeaderboardSyncHovered);
        }

        private void DrawNameEditorModal()
        {
            bool hasCustom = leaderboardService != null && leaderboardService.HasCustomPlayerName;
            string heading = hasCustom ? "CHANGE YOUR NAME" : "CHOOSE YOUR NAME";
            DrawCenteredText(heading, y: 17f, WarmTitleColor, scale: 1);
            DrawInputBox(new Rectangle(76, 27, 168, 16), editorBuffer, showCursor: !isProfileBusy && editorBuffer.Length < PlayerIdentityValidator.MaxNameLength);
            DrawEditorStatusLine("2-12 LETTERS OR NUMBERS");
            DrawOnScreenKeypad(allowPunctuation: true);
            DrawCenteredText("SAVE YOUR NAME TO CONNECT", y: 131f, HudLabelColor, scale: 1);
            string cancelLabel = (openedFromPostRunAutoPrompt && !hasCustom) ? "LATER" : "CANCEL";
            DrawButton(cancelLabel, NameCancelButton, isModalCancelHovered);
            DrawButton(isProfileBusy ? "WAIT" : "SAVE", NameSaveButton, isModalConfirmHovered);
        }

        private void DrawSyncOverviewModal()
        {
            string playerName = leaderboardService?.PlayerDisplayName ?? "PLAYER";
            string connectCode = leaderboardService?.PlayerConnectCode ?? "----";
            bool hasCustomName = leaderboardService != null && leaderboardService.HasCustomPlayerName;
            DrawCenteredText("CLOUD SAVE + SYNC", y: 17f, WarmTitleColor, scale: 1);
            DrawCenteredText("PLAYER: " + playerName, y: 31f, BestValueColor, scale: 1);
            DrawCenteredText("YOUR 12-CHAR SAVE CODE:", y: 43f, HudLabelColor, scale: 1);
            Rectangle codeBox = new Rectangle(72, 53, 176, 20);
            DrawRect(codeBox.X, codeBox.Y, codeBox.Width, codeBox.Height, PanelInkColor);
            DrawRect(codeBox.X + 1, codeBox.Y + 1, codeBox.Width - 2, codeBox.Height - 2, InputBoxFillColor);
            DrawRect(codeBox.X + 1, codeBox.Y + 1, codeBox.Width - 2, 1, PanelTimberColor);
            DrawRect(codeBox.X + 1, codeBox.Bottom - 2, codeBox.Width - 2, 1, PanelTopTrimColor);
            DrawCenteredText(connectCode, y: 60f, ScoreValueColor, scale: 1);
            if (!string.IsNullOrWhiteSpace(syncStatusMessage))
            {
                DrawCenteredText(FitLeaderboardStatus(syncStatusMessage), y: 80f, WarmTitleColor, scale: 1);
            }
            else if (!hasCustomName)
            {
                DrawCenteredText("SET YOUR NAME FIRST TO SAVE", y: 80f, GameOverTitleColor, scale: 1);
            }
            else
            {
                DrawCenteredText(
                    hasBackedUpCustomProfileThisSession ? "CLOUD SAVED - KEEP CODE PRIVATE" : "BACKUP NOT CONFIRMED - RETRY",
                    y: 80f,
                    HudLabelColor,
                    scale: 1
                );
            }

            DrawCenteredText("RESTORES NAME, BEST + RANK", y: 92f, HudLabelColor, scale: 1);
            DrawButton("COPY CODE", SyncCopyButton, isSyncCopyHovered);
            DrawButton("ENTER CODE", SyncEnterCodeButton, isSyncEnterCodeHovered);
            DrawButton("BACK", roundFlow.BackButton, isBackButtonHovered);
        }

        private void DrawSyncEnterCodeModal()
        {
            DrawCenteredText("ENTER SAVE CODE", y: 17f, WarmTitleColor, scale: 1);
            string formattedCode = PlayerIdentityValidator.FormatPartialConnectCode(editorBuffer);
            DrawInputBox(new Rectangle(72, 27, 176, 16), formattedCode, showCursor: !isProfileBusy && editorBuffer.Length < PlayerIdentityValidator.ConnectCodeLength);
            DrawEditorStatusLine("TYPE 12 LETTERS OR NUMBERS");
            DrawOnScreenKeypad(allowPunctuation: false);
            DrawCenteredText("CTRL+V OR TAP PASTE", y: 131f, HudLabelColor, scale: 1);
            DrawButton("CANCEL", CodeCancelButton, isModalCancelHovered);
            DrawButton("PASTE", CodePasteButton, isModalPasteHovered);
            DrawButton(isProfileBusy ? "WAIT" : "CONNECT", CodeConnectButton, isModalConfirmHovered);
        }

        private static string FitLeaderboardStatus(string text)
        {
            string fitted = text ?? string.Empty;
            if (PixelFont.Width(fitted, 1) <= 196)
            {
                return fitted;
            }

            while (fitted.Length > 0 && PixelFont.Width(fitted + "...", 1) > 196)
            {
                fitted = fitted.Substring(0, fitted.Length - 1);
            }

            return fitted + "...";
        }

        private void DrawEditorStatusLine(string defaultHint)
        {
            bool hasStatus = !string.IsNullOrWhiteSpace(editorStatusMessage);
            string statusLine = hasStatus ? editorStatusMessage : defaultHint;
            Color statusColor = !hasStatus ? HudLabelColor : (editorStatusIsError ? GameOverTitleColor : ScoreValueColor);
            DrawCenteredText(FitLeaderboardStatus(statusLine), y: 47f, statusColor, scale: 1);
        }

        private void DrawInputBox(Rectangle box, string valueText, bool showCursor)
        {
            DrawRect(box.X, box.Y, box.Width, box.Height, PanelInkColor);
            DrawRect(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2, InputBoxFillColor);
            DrawRect(box.X + 1, box.Bottom - 2, box.Width - 2, 1, PanelTimberColor);
            string safeValue = valueText ?? string.Empty;
            bool blinkOn = ((int)(sceneSeconds * 3f) % 2) == 0;
            int valueWidth = PixelFont.Width(safeValue, scale: 1);
            int cursorWidth = showCursor ? (PixelFont.Width("_", scale: 1) + (safeValue.Length > 0 ? 1 : 0)) : 0;
            int totalWidth = valueWidth + cursorWidth;
            int textX = box.X + (box.Width - totalWidth) / 2;
            int textY = box.Y + 5;
            if (safeValue.Length > 0)
            {
                DrawText(safeValue, new Vector2(textX, textY), ButtonTextColor, scale: 1);
            }

            if (showCursor && blinkOn)
            {
                int cursorX = safeValue.Length > 0 ? (textX + valueWidth + 1) : textX;
                DrawText("_", new Vector2(cursorX, textY), WarmTitleColor, scale: 1);
            }
        }

        private void DrawOnScreenKeypad(bool allowPunctuation)
        {
            for (int i = 0; i < OnScreenKeypad.Length; i++)
            {
                KeypadKey key = OnScreenKeypad[i];
                bool enabled = !isProfileBusy && (allowPunctuation || key.IsBackspace || (key.Character != '_' && key.Character != '-'));
                bool hovered = enabled && hoveredKeypadIndex == i;
                ButtonBounds b = key.Bounds;
                DrawRect(b.X, b.Y, b.Width, b.Height, PanelInkColor);
                Color fill = !enabled ? KeyDisabledFillColor : hovered ? KeyHoverFillColor : (key.IsBackspace ? KeyDeleteFillColor : KeyFillColor);
                DrawRect(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2, fill);
                if (enabled)
                {
                    Color topGlint = hovered ? KeyHoverHighlightColor : (key.IsBackspace ? PanelTopTrimColor : KeyHighlightColor);
                    DrawRect(b.X + 2, b.Y + 1, b.Width - 4, 1, topGlint);
                }

                Color labelColor = enabled ? ButtonTextColor : (HudLabelColor * 0.55f);
                int labelWidth = PixelFont.Width(key.Label, scale: 1);
                int labelX = b.X + (b.Width - labelWidth) / 2;
                int labelY = b.Y + 4;
                DrawText(key.Label, new Vector2(labelX, labelY), labelColor, scale: 1);
            }
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
    }
}
