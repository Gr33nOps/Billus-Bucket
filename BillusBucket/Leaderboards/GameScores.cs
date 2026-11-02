using System;
using System.Diagnostics;
using System.Threading.Tasks;

// Import only BigInteger so it does not conflict with MonoGame.Vector2.
using BigInteger = System.Numerics.BigInteger;

namespace BillusBucket
{
    /// <summary>
    /// Connects finished runs and personal bests to the existing score services.
    /// You can skip this file while learning the main gameplay.
    /// </summary>
    public sealed partial class BillusBucketGame
    {
        private ILeaderboardService leaderboardService;

        // Prevent duplicate submissions and wait for an active save before refreshing.
        private string currentRunId = string.Empty;
        private bool hasSubmittedCurrentRun;
        private Task activeRunSubmissionTask = Task.CompletedTask;

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
                if (leaderboardService.HasCustomPlayerName)
                {
                    hasAutoPromptedForName = true;
                }
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
                await activeRunSubmissionTask;
                LeaderboardSnapshot snapshot = await leaderboardService.RefreshRankingsAsync();
                bestKnownCash = BigInteger.Max(bestKnownCash, snapshot.PersonalBest);
                if (leaderboardService.HasCustomPlayerName)
                {
                    hasAutoPromptedForName = true;
                }
            }
            catch (Exception error)
            {
                Debug.WriteLine("Leaderboard refresh skipped: " + error.Message);
            }
        }

        private async Task SubmitCompletedRunScoreAsync(string runId, BigInteger finalScore, float runDurationSeconds)
        {
            if (leaderboardService == null)
            {
                return;
            }

            try
            {
                ScoreSubmissionResult result = await leaderboardService.SubmitRunScoreAsync(runId, finalScore, runDurationSeconds);
                bestKnownCash = BigInteger.Max(bestKnownCash, result.PersonalBest);
                if (result.SubmittedOnline && leaderboardService.HasCustomPlayerName)
                {
                    hasBackedUpCustomProfileThisSession = true;
                }
            }
            catch (Exception error)
            {
                Debug.WriteLine("Leaderboard submit skipped: " + error.Message);
            }
        }

        private async void SavePlayerNameInBackground(string validName)
        {
            if (leaderboardService == null)
            {
                return;
            }

            isProfileBusy = true;
            editorStatusMessage = "SAVING NAME...";
            editorStatusIsError = false;
            try
            {
                // Ensure any in-flight run score submission completes first so the weekly row renames cleanly.
                if (activeRunSubmissionTask != null)
                {
                    await activeRunSubmissionTask;
                }

                ProfileSyncResult result = await leaderboardService.UpdatePlayerNameAsync(validName);
                isProfileBusy = false;
                if (!result.Accepted)
                {
                    editorStatusMessage = string.IsNullOrWhiteSpace(result.Message) ? "NAME NOT ALLOWED" : result.Message;
                    editorStatusIsError = true;
                    return;
                }

                hasAutoPromptedForName = true;
                if (result.SyncedOnline)
                {
                    hasBackedUpCustomProfileThisSession = true;
                }

                bestKnownCash = BigInteger.Max(bestKnownCash, result.PersonalBest);
                openedFromPostRunAutoPrompt = false;
                leaderboardSubView = LeaderboardSubView.Rankings;
                rankingsToastMessage = string.IsNullOrWhiteSpace(result.Message) ? "SAVED AS " + result.DisplayName : result.Message;
                rankingsToastTimer = ToastDurationSeconds;
            }
            catch (Exception error)
            {
                isProfileBusy = false;
                editorStatusMessage = "SAVED ON DEVICE";
                editorStatusIsError = false;
                leaderboardSubView = LeaderboardSubView.Rankings;
                Debug.WriteLine("Profile save skipped: " + error.Message);
            }
        }

        private async void EnsureCustomProfileSyncedInBackground()
        {
            if (leaderboardService == null || !leaderboardService.HasCustomPlayerName)
            {
                return;
            }

            try
            {
                ProfileSyncResult result = await leaderboardService.UpdatePlayerNameAsync(leaderboardService.PlayerDisplayName);
                if (result.SyncedOnline)
                {
                    hasBackedUpCustomProfileThisSession = true;
                    bestKnownCash = BigInteger.Max(bestKnownCash, result.PersonalBest);
                }
            }
            catch
            {
            }
        }

        private async void RestoreProfileInBackground(string normalizedCode)
        {
            if (leaderboardService == null)
            {
                return;
            }

            isProfileBusy = true;
            editorStatusMessage = "CONNECTING PROFILE...";
            editorStatusIsError = false;
            try
            {
                ProfileSyncResult result = await leaderboardService.RestoreProfileByCodeAsync(normalizedCode);
                isProfileBusy = false;
                if (!result.Accepted)
                {
                    editorStatusMessage = string.IsNullOrWhiteSpace(result.Message) ? "SAVE CODE NOT FOUND" : result.Message;
                    editorStatusIsError = true;
                    return;
                }

                hasAutoPromptedForName = true;
                hasBackedUpCustomProfileThisSession = true;
                bestKnownCash = result.PersonalBest;
                bool wasGameOver = currentRun.IsOver;
                currentRun = new BillusBucketRun(bestKnownCash);
                if (wasGameOver)
                {
                    currentRun.HitBomb();
                }

                lastRunCash = BigInteger.Zero;
                leaderboardSubView = LeaderboardSubView.Rankings;
                rankingsToastMessage = string.IsNullOrWhiteSpace(result.Message) ? "CONNECTED AS " + result.DisplayName : result.Message;
                rankingsToastTimer = ToastDurationSeconds;
            }
            catch (Exception error)
            {
                isProfileBusy = false;
                editorStatusMessage = "OFFLINE - CHECK INTERNET";
                editorStatusIsError = true;
                Debug.WriteLine("Profile restore skipped: " + error.Message);
            }
        }
    }
}
