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
    /// The active game: start a run, move Billu, spawn objects, resolve catches, and end a run.
    /// The smaller classes in Gameplay/ do the movement, scoring, and animation calculations.
    /// </summary>
    public sealed partial class BillusBucketGame
    {
        private const float InitialSpawnDelaySeconds = 0.25f;
        private const float ExplosionDurationSeconds = 0.85f;
        private const float ScreenFlashDuration = 0.22f;
        private const float ScreenFlashFadeSpeed = 0.9f;

        // Positions and hitboxes are measured on the 320 by 180 game canvas.
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

        // These objects remember the state of one part of the game between frames.
        private readonly RoundFlow roundFlow = new RoundFlow();
        private readonly BilluAnimator billuAnimator = new BilluAnimator();
        private readonly BucketFeedback bucketFeedback = new BucketFeedback();
        private readonly Random random = new Random();

        // A List can grow and shrink: spawning adds a drop, catching removes it.
        private readonly List<Drop> drops = new List<Drop>();
        private readonly List<Particle> particles = new List<Particle>();
        private readonly List<Popup> popups = new List<Popup>();
        private BillusBucketRun currentRun;
        private BucketControl bucketControl;
        private DropDirector dropDirector = new DropDirector();
        private float runSeconds; // Time played in this run; controls difficulty and day/night.
        private float sceneSeconds; // Time for cloud movement and other background animation.
        private float spawnCountdown;
        private float explosionCountdown;
        private float screenFlashStrength;
        private BigInteger lastRunCash;
        private BigInteger bestKnownCash;

        // Called by the Play and Retry buttons. Reset the run without losing the best score.
        private void StartRun()
        {
            bestKnownCash = BigInteger.Max(bestKnownCash, leaderboardService.GetPersonalBest());
            currentRun = new BillusBucketRun(bestKnownCash);
            bucketControl = CreateCenteredBucket();
            currentRunId = Guid.NewGuid().ToString("N");
            hasSubmittedCurrentRun = false;
            drops.Clear();
            particles.Clear();
            popups.Clear();
            audio?.Start();
            runSeconds = 0f;
            spawnCountdown = InitialSpawnDelaySeconds;
            dropDirector = new DropDirector();
            explosionCountdown = 0f;
            screenFlashStrength = 0f;
            lastRunCash = BigInteger.Zero;
            billuAnimator.Reset();
            bucketFeedback.Reset();
        }

        // Each pair stores a caught object and when it touched the rim within this frame.
        // Time goes from 0 (frame start) to 1 (frame end).
        private readonly List<(Drop Drop, float Time)> frameCatches = new List<(Drop, float)>();

        private void UpdateRun(
            float deltaSeconds,
            KeyboardState keys,
            MouseState mouse,
            TouchCollection touch,
            Rectangle canvasViewport)
        {
            // 1. Read movement input. Left is -1, right is +1, and both keys cancel out.
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

            // 2. Move the bucket, then update the character and caught-note animations.
            float previousBucketX = bucketControl.X;
            bucketControl.Update(deltaSeconds, keyboardDirection, pointerTargetX);
            billuAnimator.Update(deltaSeconds, bucketControl.VelocityX);
            bucketFeedback.Update(deltaSeconds);

            // 3. Count down to the next spawn. SpawnDrop adds the next delay to the timer.
            runSeconds += deltaSeconds;
            spawnCountdown -= deltaSeconds;
            while (spawnCountdown <= 0f)
            {
                SpawnDrop();
            }

            // 4. Move every falling object and check its whole path across this frame.
            // Checking only its final position could miss a fast object crossing the rim.
            float rimCenterY = PixelArt.GroundTop - CatchBoxOffsetFromGround + CatchBoxHeight * 0.5f;
            frameCatches.Clear();

            // Work backwards so removing a missed drop does not skip the next item.
            for (int i = drops.Count - 1; i >= 0; i--)
            {
                Drop drop = drops[i];
                float oldX = drop.Motion.X, oldY = drop.Motion.Y;
                drop.Motion.Update(deltaSeconds);
                bool isCash = drop.Motion.Kind == DropKind.Cash;
                float halfWidth = CatchBoxHalfWidth + (isCash ? CashHitboxHalfWidth : BombHitboxHalfWidth);
                float halfHeight = CatchBoxHeight * 0.5f + (isCash ? CashHitboxHalfHeight : BombHitboxHalfHeight);
                if (DropCollision.TryFindCatch(
                    oldX,
                    oldY,
                    drop.Motion.X,
                    drop.Motion.Y,
                    previousBucketX,
                    bucketControl.X,
                    rimCenterY,
                    halfWidth,
                    halfHeight,
                    out float hitTime
                ))
                {
                    frameCatches.Add((drop, hitTime));
                }
                else if (drop.Motion.Y > PixelArt.Height + DropDespawnMargin)
                {
                    drops.RemoveAt(i);
                }
            }

            // 5. Handle catches in the order they happened. On an exact tie, the bomb wins.
            // A note caught before the bomb still counts toward the final score.
            frameCatches.Sort((first, second) =>
            {
                int byTime = first.Time.CompareTo(second.Time);
                return byTime != 0 ? byTime : second.Drop.Motion.Kind.CompareTo(first.Drop.Motion.Kind);
            });
            foreach (var caught in frameCatches)
            {
                drops.Remove(caught.Drop);
                if (caught.Drop.Motion.Kind == DropKind.Cash)
                {
                    HandleCashCaught(caught.Drop);
                }
                else
                {
                    HandleBombHit();
                    break;
                }
            }
        }

        private void HandleCashCaught(Drop drop)
        {
            // The score, the visible pile, the sound, and the popup all respond to one catch.
            int caughtValue = drop.Value;
            currentRun.CatchCash(caughtValue);
            bucketFeedback.Collect(drop.NoteIndex, drop.Motion.X - bucketControl.X);
            bestKnownCash = BigInteger.Max(bestKnownCash, currentRun.BestCash);
            float smallVariation = (float)random.NextDouble() * 0.024f - 0.012f;
            audio?.Play(GameAudio.SoundCatch, GameAudio.PitchForNote(caughtValue) + smallVariation);
            leaderboardService?.RecordLivePersonalBest(currentRun.Cash);
            string popupLabel = "+RS " + CashDisplay.Compact(caughtValue);
            Vector2 popupStart = new Vector2(bucketControl.X - 10f, PixelArt.GroundTop - 55f);
            Color popupColor = notePopupColors[drop.NoteIndex];
            popups.Add(new Popup(popupLabel, popupStart, popupColor));
        }

        private void HandleBombHit()
        {
            // Capture the score achieved immediately before the bomb wipes the bucket's cash.
            lastRunCash = currentRun.HitBomb();
            bestKnownCash = BigInteger.Max(bestKnownCash, currentRun.BestCash);
            roundFlow.HitBomb();
            billuAnimator.Burn();
            bucketFeedback.Reset();
            explosionCountdown = ExplosionDurationSeconds;
            audio?.HitBomb();
            screenFlashStrength = ScreenFlashDuration;
            SpawnParticleBurst(new Vector2(bucketControl.X, PixelArt.GroundTop - 36f), ExplosionSparkColor, count: 14);
            drops.Clear();

            // Submit the pre-explosion run score at most once per completed run.
            if (!hasSubmittedCurrentRun)
            {
                hasSubmittedCurrentRun = true;
                string completedRunId = string.IsNullOrWhiteSpace(currentRunId) ? Guid.NewGuid().ToString("N") : currentRunId;
                activeRunSubmissionTask = SubmitCompletedRunScoreAsync(completedRunId, lastRunCash, runSeconds);
            }
        }

        private void SpawnDrop()
        {
            // The director chooses what, where, how fast, and when the next spawn happens.
            DropPlan plan = dropDirector.Next(
                runSeconds,
                PixelArt.PlayLeft + SpawnSideMargin,
                PixelArt.PlayRight - SpawnSideMargin,
                bucketControl.X
            );
            spawnCountdown += Math.Max(0.05f, plan.Delay);

            // Motion handles how this particular object travels after it has spawned.
            DropMotion motion = new DropMotion(
                plan.Kind,
                plan.X,
                DropSpawnY,
                plan.Speed,
                left: PixelArt.PlayLeft + WindSideMargin,
                right: PixelArt.PlayRight - WindSideMargin
            );

            // The index identifies a row in NoteCatalog and a design in the note sprite sheet.
            int noteIndex = plan.Kind == DropKind.Cash ? NoteCatalog.IndexForRoll(random.Next(NoteCatalog.RollCount)) : 0;
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

        private void UpdateVisualEffects(float deltaSeconds)
        {
            sceneSeconds += deltaSeconds;
            if (CurrentPhase == RoundPhase.Ready || CurrentPhase == RoundPhase.GameOver)
            {
                billuAnimator.Update(deltaSeconds, horizontalSpeed: 0f);
            }

            screenFlashStrength = Math.Max(0f, screenFlashStrength - deltaSeconds * ScreenFlashFadeSpeed);
            explosionCountdown = Math.Max(0f, explosionCountdown - deltaSeconds);
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                particles[i].Update(deltaSeconds);
                if (particles[i].Life <= 0f)
                {
                    particles.RemoveAt(i);
                }
            }

            for (int i = popups.Count - 1; i >= 0; i--)
            {
                popups[i].Update(deltaSeconds);
                if (popups[i].Life <= 0f)
                {
                    popups.RemoveAt(i);
                }
            }
        }

        private static BucketControl CreateCenteredBucket()
        {
            return new BucketControl(
                startX: PixelArt.FieldWidth / 2f,
                playfieldWidth: PixelArt.PlayWidth,
                bucketWidth: PlayerCollisionWidth,
                playfieldLeft: PixelArt.PlayLeft
            );
        }
    }
}
