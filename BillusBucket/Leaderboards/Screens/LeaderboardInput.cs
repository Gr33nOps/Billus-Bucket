using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

// Import only BigInteger so it does not conflict with MonoGame.Vector2.
using BigInteger = System.Numerics.BigInteger;

namespace BillusBucket
{
    /// <summary>
    /// Leaderboard screens, name entry, and Save Code input.
    /// These are optional reading after the gameplay files.
    /// </summary>
    public sealed partial class BillusBucketGame
    {
        private const float ToastDurationSeconds = 3.6f;
        private enum LeaderboardSubView
        {
            Rankings,
            EditName,
            SyncOverview,
            SyncEnterCode
        }

        private readonly struct KeypadKey
        {
            public string Label { get; }
            public char Character { get; }
            public bool IsBackspace { get; }
            public ButtonBounds Bounds { get; }

            public KeypadKey(string label, char character, bool isBackspace, ButtonBounds bounds)
            {
                Label = label;
                Character = character;
                IsBackspace = isBackspace;
                Bounds = bounds;
            }
        }

        // Leaderboard timber panel: face spans y = 12..169, so every button below sits fully inside it.
        private static readonly Rectangle LeaderboardPanel = new Rectangle(50, 5, 220, 171);

        // Leaderboard, Custom Name, and Cloud Sync button hitboxes (all inside the panel face x = 55..265)
        private static readonly ButtonBounds LeaderboardNameButton = new ButtonBounds(63, 147, 60, 18);
        private static readonly ButtonBounds LeaderboardSyncButton = new ButtonBounds(197, 147, 60, 18);
        private static readonly ButtonBounds NameCancelButton = new ButtonBounds(86, 147, 68, 18);
        private static readonly ButtonBounds NameSaveButton = new ButtonBounds(166, 147, 68, 18);
        private static readonly ButtonBounds CodeCancelButton = new ButtonBounds(63, 147, 60, 18);
        private static readonly ButtonBounds CodePasteButton = new ButtonBounds(130, 147, 60, 18);
        private static readonly ButtonBounds CodeConnectButton = new ButtonBounds(197, 147, 60, 18);
        private static readonly ButtonBounds SyncCopyButton = new ButtonBounds(58, 108, 96, 18);
        private static readonly ButtonBounds SyncEnterCodeButton = new ButtonBounds(166, 108, 96, 18);
        private static readonly KeypadKey[] OnScreenKeypad = BuildOnScreenKeypad();
        private static readonly Keys[] LetterKeys =
        {
            Keys.A,
            Keys.B,
            Keys.C,
            Keys.D,
            Keys.E,
            Keys.F,
            Keys.G,
            Keys.H,
            Keys.I,
            Keys.J,
            Keys.K,
            Keys.L,
            Keys.M,
            Keys.N,
            Keys.O,
            Keys.P,
            Keys.Q,
            Keys.R,
            Keys.S,
            Keys.T,
            Keys.U,
            Keys.V,
            Keys.W,
            Keys.X,
            Keys.Y,
            Keys.Z
        };
        private static readonly Keys[] TopDigitKeys =
        {
            Keys.D0,
            Keys.D1,
            Keys.D2,
            Keys.D3,
            Keys.D4,
            Keys.D5,
            Keys.D6,
            Keys.D7,
            Keys.D8,
            Keys.D9
        };
        private static readonly Keys[] NumPadDigitKeys =
        {
            Keys.NumPad0,
            Keys.NumPad1,
            Keys.NumPad2,
            Keys.NumPad3,
            Keys.NumPad4,
            Keys.NumPad5,
            Keys.NumPad6,
            Keys.NumPad7,
            Keys.NumPad8,
            Keys.NumPad9
        };
        private bool isLeaderboardButtonHovered;
        private bool isBackButtonHovered;
        private bool isLeaderboardNameHovered;
        private bool isLeaderboardSyncHovered;
        private bool isModalCancelHovered;
        private bool isModalConfirmHovered;
        private bool isModalPasteHovered;
        private bool isSyncCopyHovered;
        private bool isSyncEnterCodeHovered;
        private int hoveredKeypadIndex = -1;

        // Player custom name & cross-device Cloud Sync UI state
        private LeaderboardSubView leaderboardSubView = LeaderboardSubView.Rankings;
        private bool hasAutoPromptedForName;
        private bool openedFromPostRunAutoPrompt;
        private bool hasBackedUpCustomProfileThisSession;
        private bool isProfileBusy;
        private string editorBuffer = string.Empty;
        private string editorStatusMessage = string.Empty;
        private bool editorStatusIsError;
        private string syncStatusMessage = string.Empty;
        private string rankingsToastMessage = string.Empty;
        private float rankingsToastTimer;
        private bool IsTypingInKeypadModal => roundFlow.IsLeaderboardOpen && (leaderboardSubView == LeaderboardSubView.EditName || leaderboardSubView == LeaderboardSubView.SyncEnterCode);

        private void OpenLeaderboardScreen(bool autoOpenNameEditor = false, bool fromPostRunPrompt = false)
        {
            if (!roundFlow.OpenLeaderboard())
            {
                return;
            }

            bool shouldPromptForName = autoOpenNameEditor || (!hasAutoPromptedForName && leaderboardService != null && !leaderboardService.HasCustomPlayerName && !reviewTools.IsLiveReviewMode);
            if (shouldPromptForName)
            {
                hasAutoPromptedForName = true;
                BeginNameEditor(fromPostRunPrompt);
            }
            else
            {
                leaderboardSubView = LeaderboardSubView.Rankings;
                openedFromPostRunAutoPrompt = false;
            }

            audio?.Play(GameAudio.SoundToggle);
            RefreshLeaderboardInBackground();
        }

        private void CloseLeaderboardScreen()
        {
            if (roundFlow.CloseLeaderboard())
            {
                leaderboardSubView = LeaderboardSubView.Rankings;
                openedFromPostRunAutoPrompt = false;
                audio?.Play(GameAudio.SoundToggle);
            }
        }

        private void BeginNameEditor(bool fromPostRunPrompt)
        {
            leaderboardSubView = LeaderboardSubView.EditName;
            openedFromPostRunAutoPrompt = fromPostRunPrompt;
            isProfileBusy = false;
            editorStatusMessage = string.Empty;
            editorStatusIsError = false;
            DiscardStalePastedText();
            if (leaderboardService != null && leaderboardService.HasCustomPlayerName)
            {
                editorBuffer = leaderboardService.PlayerDisplayName ?? string.Empty;
            }
            else
            {
                editorBuffer = string.Empty;
            }
        }

        private void OpenSyncOverview()
        {
            leaderboardSubView = LeaderboardSubView.SyncOverview;
            openedFromPostRunAutoPrompt = false;
            isProfileBusy = false;
            syncStatusMessage = string.Empty;
            if (!hasBackedUpCustomProfileThisSession && leaderboardService != null && leaderboardService.HasCustomPlayerName)
            {
                EnsureCustomProfileSyncedInBackground();
            }
        }

        private void BeginSyncEnterCode()
        {
            leaderboardSubView = LeaderboardSubView.SyncEnterCode;
            isProfileBusy = false;
            editorBuffer = string.Empty;
            editorStatusMessage = string.Empty;
            editorStatusIsError = false;
            DiscardStalePastedText();
        }

        private void UpdateLeaderboardScreenInput(
            KeyboardState keys,
            MouseState mouse,
            TouchCollection touch,
            bool clicked,
            bool tapped,
            Rectangle canvasViewport)
        {
            Point? pointerPoint = null;
            if (clicked)
            {
                pointerPoint = ScreenToCanvas(mouse.X, mouse.Y, canvasViewport);
            }
            else if (tapped)
            {
                pointerPoint = ScreenToCanvas(touch[0].Position.X, touch[0].Position.Y, canvasViewport);
            }

            // Clicking the top-right Trophy icon closes the leaderboard modal from any sub-view.
            if (pointerPoint.HasValue && !isProfileBusy && LeaderboardIconButton.Contains(pointerPoint.Value.X, pointerPoint.Value.Y))
            {
                ignorePointerUntilRelease = true;
                CloseLeaderboardScreen();
                return;
            }

            if (leaderboardSubView == LeaderboardSubView.Rankings)
            {
                bool closeByKey = WasKeyJustPressed(keys, Keys.Escape) || WasKeyJustPressed(keys, Keys.Back) || WasKeyJustPressed(keys, Keys.Enter) || WasKeyJustPressed(keys, Keys.Space) || WasKeyJustPressed(keys, Keys.L);
                if (closeByKey)
                {
                    CloseLeaderboardScreen();
                    return;
                }

                if (pointerPoint.HasValue)
                {
                    Point pt = pointerPoint.Value;
                    if (LeaderboardNameButton.Contains(pt.X, pt.Y))
                    {
                        ignorePointerUntilRelease = true;
                        audio?.Play(GameAudio.SoundToggle);
                        BeginNameEditor(fromPostRunPrompt: false);
                    }
                    else if (LeaderboardSyncButton.Contains(pt.X, pt.Y))
                    {
                        ignorePointerUntilRelease = true;
                        audio?.Play(GameAudio.SoundToggle);
                        OpenSyncOverview();
                    }
                    else if (new Rectangle(61, 134, 198, 11).Contains(pt.X, pt.Y) && leaderboardService?.CurrentSnapshot.IsLoading == false)
                    {
                        ignorePointerUntilRelease = true;
                        rankingsToastTimer = 0f;
                        audio?.Play(GameAudio.SoundToggle);
                        RefreshLeaderboardInBackground();
                    }
                    else if (roundFlow.BackButton.Contains(pt.X, pt.Y))
                    {
                        ignorePointerUntilRelease = true;
                        CloseLeaderboardScreen();
                    }
                }
            }
            else if (leaderboardSubView == LeaderboardSubView.SyncOverview)
            {
                if (WasKeyJustPressed(keys, Keys.Escape) || WasKeyJustPressed(keys, Keys.Back))
                {
                    leaderboardSubView = LeaderboardSubView.Rankings;
                    audio?.Play(GameAudio.SoundToggle);
                    return;
                }

                if (pointerPoint.HasValue)
                {
                    Point pt = pointerPoint.Value;
                    if (SyncCopyButton.Contains(pt.X, pt.Y))
                    {
                        ignorePointerUntilRelease = true;
                        HandleCopyConnectCode();
                    }
                    else if (SyncEnterCodeButton.Contains(pt.X, pt.Y))
                    {
                        ignorePointerUntilRelease = true;
                        audio?.Play(GameAudio.SoundToggle);
                        BeginSyncEnterCode();
                    }
                    else if (roundFlow.BackButton.Contains(pt.X, pt.Y))
                    {
                        ignorePointerUntilRelease = true;
                        leaderboardSubView = LeaderboardSubView.Rankings;
                        audio?.Play(GameAudio.SoundToggle);
                    }
                }
            }
            else if (leaderboardSubView == LeaderboardSubView.EditName || leaderboardSubView == LeaderboardSubView.SyncEnterCode)
            {
                UpdateKeypadModalInput(keys, pointerPoint);
            }
        }

        private async void HandleCopyConnectCode()
        {
            string code = leaderboardService?.PlayerConnectCode ?? string.Empty;
            bool copied = await platformBridge.CopyTextToClipboardAsync(code);
            syncStatusMessage = copied ? "CODE COPIED TO CLIPBOARD!" : "WRITE DOWN SAVE CODE ABOVE";
            audio?.Play(GameAudio.SoundToggle);
            if (!hasBackedUpCustomProfileThisSession && leaderboardService != null && leaderboardService.HasCustomPlayerName)
            {
                EnsureCustomProfileSyncedInBackground();
            }
        }

        private void UpdateKeypadModalInput(KeyboardState keys, Point? pointerPoint)
        {
            if (isProfileBusy)
            {
                return;
            }

            if (WasKeyJustPressed(keys, Keys.Escape))
            {
                CancelKeypadModal();
                return;
            }

            if (WasKeyJustPressed(keys, Keys.Enter))
            {
                ConfirmKeypadModal();
                return;
            }

            bool isNameMode = leaderboardSubView == LeaderboardSubView.EditName;
            int maxLength = isNameMode ? PlayerIdentityValidator.MaxNameLength : PlayerIdentityValidator.ConnectCodeLength;
            if (WasKeyJustPressed(keys, Keys.Back) || WasKeyJustPressed(keys, Keys.Delete))
            {
                BackspaceEditorBuffer();
            }

            HandlePhysicalKeyboardTextEntry(keys, isNameMode, maxLength);
            HandleClipboardPaste(keys);
            if (!pointerPoint.HasValue)
            {
                return;
            }

            Point pt = pointerPoint.Value;
            if (GetModalCancelButton().Contains(pt.X, pt.Y))
            {
                ignorePointerUntilRelease = true;
                CancelKeypadModal();
                return;
            }

            if (GetModalConfirmButton().Contains(pt.X, pt.Y))
            {
                ignorePointerUntilRelease = true;
                ConfirmKeypadModal();
                return;
            }

            if (!isNameMode && CodePasteButton.Contains(pt.X, pt.Y))
            {
                ignorePointerUntilRelease = true;
                RequestPasteFromClipboard();
                return;
            }

            for (int i = 0; i < OnScreenKeypad.Length; i++)
            {
                KeypadKey key = OnScreenKeypad[i];
                if (!key.Bounds.Contains(pt.X, pt.Y))
                {
                    continue;
                }

                ignorePointerUntilRelease = true;
                if (key.IsBackspace)
                {
                    BackspaceEditorBuffer();
                }
                else if (isNameMode || (key.Character != '_' && key.Character != '-'))
                {
                    AppendEditorCharacter(key.Character, maxLength);
                }

                break;
            }
        }

        private void HandlePhysicalKeyboardTextEntry(KeyboardState keys, bool allowPunctuation, int maxLength)
        {
            bool shiftHeld = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);

            // Ctrl+V / Cmd+V is a paste shortcut, so never type the letter itself.
            if (IsShortcutModifierHeld(keys))
            {
                return;
            }

            for (int i = 0; i < LetterKeys.Length; i++)
            {
                if (WasKeyJustPressed(keys, LetterKeys[i]))
                {
                    AppendEditorCharacter((char)('A' + i), maxLength);
                }
            }

            if (!shiftHeld)
            {
                for (int i = 0; i < TopDigitKeys.Length; i++)
                {
                    if (WasKeyJustPressed(keys, TopDigitKeys[i]))
                    {
                        AppendEditorCharacter((char)('0' + i), maxLength);
                    }
                }
            }

            for (int i = 0; i < NumPadDigitKeys.Length; i++)
            {
                if (WasKeyJustPressed(keys, NumPadDigitKeys[i]))
                {
                    AppendEditorCharacter((char)('0' + i), maxLength);
                }
            }

            if (!allowPunctuation)
            {
                return;
            }

            if (WasKeyJustPressed(keys, Keys.OemMinus))
            {
                AppendEditorCharacter(shiftHeld ? '_' : '-', maxLength);
            }
            else if (WasKeyJustPressed(keys, Keys.Subtract))
            {
                AppendEditorCharacter('-', maxLength);
            }
            else if (WasKeyJustPressed(keys, Keys.Space) && editorBuffer.Length > 0 && editorBuffer[editorBuffer.Length - 1] != '_')
            {
                AppendEditorCharacter('_', maxLength);
            }
        }

        private static bool IsShortcutModifierHeld(KeyboardState keys)
        {
            return keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl) || keys.IsKeyDown(Keys.LeftWindows) || keys.IsKeyDown(Keys.RightWindows);
        }

        private ButtonBounds GetModalCancelButton()
        {
            return leaderboardSubView == LeaderboardSubView.SyncEnterCode ? CodeCancelButton : NameCancelButton;
        }

        private ButtonBounds GetModalConfirmButton()
        {
            return leaderboardSubView == LeaderboardSubView.SyncEnterCode ? CodeConnectButton : NameSaveButton;
        }

        /// <summary>
        /// Reads text pasted with Ctrl+V (browser paste event, or SDL clipboard on desktop)
        /// and puts it into the name or Save Code box.
        /// </summary>
        private void HandleClipboardPaste(KeyboardState keys)
        {
            if (IsShortcutModifierHeld(keys) && WasKeyJustPressed(keys, Keys.V))
            {
#if !BLAZORGL
                if (DesktopClipboard.TryGetText(out string desktopText))
                {
                    ApplyPastedText(desktopText);
                    return;
                }
#else
                platformBridge.RequestClipboardPaste();
#endif
            }

            if (platformBridge.TryConsumePastedText(out string pastedText))
            {
                ApplyPastedText(pastedText);
            }
        }

        private void RequestPasteFromClipboard()
        {
            audio?.Play(GameAudio.SoundToggle);
#if !BLAZORGL
            if (DesktopClipboard.TryGetText(out string desktopText))
            {
                ApplyPastedText(desktopText);
                return;
            }

#endif

            // The browser answers asynchronously; HandleClipboardPaste picks the text up on a later frame.
            if (!platformBridge.RequestClipboardPaste())
            {
                editorStatusMessage = "PRESS CTRL+V TO PASTE";
                editorStatusIsError = true;
            }
        }

        private void DiscardStalePastedText()
        {
            platformBridge.TryConsumePastedText(out _);
        }

        private void ApplyPastedText(string rawText)
        {
            if (isProfileBusy || string.IsNullOrWhiteSpace(rawText))
            {
                return;
            }

            string cleaned = leaderboardSubView == LeaderboardSubView.SyncEnterCode ? PlayerIdentityValidator.StripConnectCodeCharacters(rawText) : CleanPastedName(rawText);
            if (cleaned.Length == 0)
            {
                editorStatusMessage = "NOTHING USEFUL TO PASTE";
                editorStatusIsError = true;
                return;
            }

            editorBuffer = cleaned;
            editorStatusMessage = "PASTED!";
            editorStatusIsError = false;
            audio?.Play(GameAudio.SoundToggle);
        }

        private static string CleanPastedName(string rawText)
        {
            System.Text.StringBuilder name = new System.Text.StringBuilder(PlayerIdentityValidator.MaxNameLength);
            foreach (char rawChar in rawText.Trim())
            {
                if (name.Length >= PlayerIdentityValidator.MaxNameLength)
                {
                    break;
                }

                char c = char.ToUpperInvariant(rawChar);
                bool isAllowed = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (isAllowed)
                {
                    name.Append(c);
                }
                else if (char.IsWhiteSpace(c) && name.Length > 0 && name[name.Length - 1] != '_')
                {
                    name.Append('_');
                }
            }

            return name.ToString();
        }

        private void AppendEditorCharacter(char c, int maxLength)
        {
            if (editorBuffer.Length >= maxLength)
            {
                return;
            }

            editorBuffer += char.ToUpperInvariant(c);
            editorStatusMessage = string.Empty;
            editorStatusIsError = false;
            audio?.Play(GameAudio.SoundToggle);
        }

        private void BackspaceEditorBuffer()
        {
            if (editorBuffer.Length == 0)
            {
                return;
            }

            editorBuffer = editorBuffer.Substring(0, editorBuffer.Length - 1);
            editorStatusMessage = string.Empty;
            editorStatusIsError = false;
            audio?.Play(GameAudio.SoundToggle);
        }

        private void CancelKeypadModal()
        {
            audio?.Play(GameAudio.SoundToggle);
            if (leaderboardSubView == LeaderboardSubView.SyncEnterCode)
            {
                leaderboardSubView = LeaderboardSubView.SyncOverview;
                return;
            }

            if (openedFromPostRunAutoPrompt)
            {
                CloseLeaderboardScreen();
            }
            else
            {
                leaderboardSubView = LeaderboardSubView.Rankings;
            }
        }

        private void ConfirmKeypadModal()
        {
            if (leaderboardSubView == LeaderboardSubView.EditName)
            {
                if (!PlayerIdentityValidator.TryValidateCustomName(editorBuffer, out string validName, out string validationError))
                {
                    editorStatusMessage = validationError;
                    editorStatusIsError = true;
                    return;
                }

                audio?.Play(GameAudio.SoundToggle);
                SavePlayerNameInBackground(validName);
            }
            else if (leaderboardSubView == LeaderboardSubView.SyncEnterCode)
            {
                if (!PlayerIdentityValidator.TryNormalizeConnectCode(editorBuffer, out string normalizedCode))
                {
                    editorStatusMessage = "ENTER 12-CHAR SAVE CODE";
                    editorStatusIsError = true;
                    return;
                }

                audio?.Play(GameAudio.SoundToggle);
                RestoreProfileInBackground(normalizedCode);
            }
        }

        private static KeypadKey[] BuildOnScreenKeypad()
        {
            const int startX = 61;
            const int startY = 57;
            const int keyWidth = 18;
            const int keyHeight = 15;
            const int stepX = 20;
            const int stepY = 17;
            string[] rows =
            {
                "1234567890",
                "QWERTYUIOP",
                "ASDFGHJKL_",
                "ZXCVBNM-"
            };
            List<KeypadKey> keys = new List<KeypadKey>(39);
            for (int row = 0; row < rows.Length; row++)
            {
                string chars = rows[row];
                int y = startY + row * stepY;
                for (int col = 0; col < chars.Length; col++)
                {
                    char c = chars[col];
                    ButtonBounds bounds = new ButtonBounds(startX + col * stepX, y, keyWidth, keyHeight);
                    keys.Add(new KeypadKey(c.ToString(), c, isBackspace: false, bounds));
                }
            }

            // Double-wide DEL key in the bottom-right corner (columns 8..9 of row 3)
            ButtonBounds delBounds = new ButtonBounds(startX + 8 * stepX, startY + 3 * stepY, keyWidth * 2 + 2, keyHeight);
            keys.Add(new KeypadKey("DEL", '\0', isBackspace: true, delBounds));
            return keys.ToArray();
        }
    }
}
