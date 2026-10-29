using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using BigInteger = System.Numerics.BigInteger;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BilluKiBalti
{
    internal readonly struct ArtReviewFrame
    {
        public BilluKiBaltiRun Run { get; }
        public BigInteger LastRunCash { get; }
        public float ExplosionTimer { get; }
        public float AmbientTime { get; }

        public ArtReviewFrame(
            BilluKiBaltiRun run,
            BigInteger lastRunCash,
            float explosionTimer,
            float ambientTime)
        {
            Run = run;
            LastRunCash = lastRunCash;
            ExplosionTimer = explosionTimer;
            AmbientTime = ambientTime;
        }
    }

    /// <summary>
    /// Optional diagnostic helpers triggered by --art-review or --live-review command-line flags.
    /// Keeps screenshot export code separate from the main game loop.
    /// </summary>
    internal sealed class ReviewTools
    {
        private static readonly string[] ArtReviewStateNames =
        {
            "ready",
            "playing",
            "walk-right",
            "walk-left",
            "paused",
            "explosion",
            "burned",
            "retry"
        };

        private static readonly Vector2[] SampleCashPositions =
        {
            new Vector2(57, 54),
            new Vector2(105, 78),
            new Vector2(213, 56),
            new Vector2(266, 96)
        };

        private const double LiveCaptureIntervalSeconds = 0.25;

        private readonly string artReviewDirectory;
        private readonly string liveReviewDirectory;
        private int artReviewIndex;
        private double nextLiveCaptureTime;

        public bool IsArtReviewMode => artReviewDirectory != null;
        public bool IsLiveReviewMode => liveReviewDirectory != null;

        public ReviewTools(string artReviewDirectory, string liveReviewDirectory)
        {
            this.artReviewDirectory = string.IsNullOrWhiteSpace(artReviewDirectory) ? null : artReviewDirectory;
            this.liveReviewDirectory = string.IsNullOrWhiteSpace(liveReviewDirectory) ? null : liveReviewDirectory;
        }

        public string GetLeaderboardPath(string defaultPath)
        {
            if (IsLiveReviewMode)
            {
                return Path.Combine(liveReviewDirectory, "test-scores.json");
            }

            return defaultPath;
        }

        /// <summary>
        /// Configures deterministic game state for each of the 8 art-review screenshots.
        /// </summary>
        public ArtReviewFrame PrepareArtReviewState(
            RoundFlow flow,
            BilluAnimator billu,
            List<Drop> drops)
        {
            flow.Reset();
            billu.Reset();
            drops.Clear();

            BilluKiBaltiRun run = new BilluKiBaltiRun();
            BigInteger lastRunCash = BigInteger.Zero;
            float explosionTimer = 0f;
            const float ambientTime = 0.6f;

            if (artReviewIndex == 0)
            {
                return new ArtReviewFrame(run, lastRunCash, explosionTimer, ambientTime);
            }

            flow.ActivateMainAction();
            if (artReviewIndex == 7)
            {
                return new ArtReviewFrame(run, lastRunCash, explosionTimer, ambientTime);
            }

            for (int i = 0; i < 12; i++)
            {
                run.CatchCash();
            }

            foreach (Vector2 point in SampleCashPositions)
            {
                int seed = drops.Count;
                int noteIndex = drops.Count % NoteCatalog.Count;
                DropMotion motion = new DropMotion(DropKind.Cash, point.X, point.Y, speed: 0f, seed: seed);
                drops.Add(new Drop(motion, noteIndex));
            }

            drops.Add(new Drop(new DropMotion(DropKind.Bomb, 140f, 48f, speed: 0f)));
            drops.Add(new Drop(new DropMotion(DropKind.Bomb, 238f, 76f, speed: 0f)));

            if (artReviewIndex == 2)
            {
                billu.Update(0.17f, 100f);
            }
            else if (artReviewIndex == 3)
            {
                billu.Update(0.29f, -100f);
            }
            else if (artReviewIndex == 4)
            {
                flow.TogglePause();
            }
            else if (artReviewIndex == 5 || artReviewIndex == 6)
            {
                lastRunCash = run.HitBomb();
                flow.HitBomb();
                billu.Burn();
                drops.Clear();

                if (artReviewIndex == 5)
                {
                    explosionTimer = 0.55f;
                }
            }

            return new ArtReviewFrame(run, lastRunCash, explosionTimer, ambientTime);
        }

        /// <summary>
        /// Saves the current art-review frame and returns true once all 8 states have been exported.
        /// </summary>
        public bool SaveArtReviewFrameAndAdvance(RenderTarget2D canvas)
        {
            if (!IsArtReviewMode)
            {
                return false;
            }

#if !BLAZORGL
            Directory.CreateDirectory(artReviewDirectory);
            string outputPath = Path.Combine(artReviewDirectory, ArtReviewStateNames[artReviewIndex] + ".png");
            using (FileStream file = File.Create(outputPath))
            {
                canvas.SaveAsPng(file, PixelArt.FieldWidth, PixelArt.Height);
            }
#endif

            artReviewIndex++;
            return artReviewIndex >= ArtReviewStateNames.Length;
        }

        /// <summary>
        /// Periodically writes latest.png and state.json during interactive --live-review sessions.
        /// </summary>
        public void CaptureLiveReviewIfDue(
            GameTime gameTime,
            RenderTarget2D canvas,
            RoundPhase phase,
            bool isWindowActive,
            float elapsed,
            BigInteger currentCash,
            float bucketX,
            bool musicMuted,
            bool sfxMuted,
            IReadOnlyList<Drop> drops)
        {
            if (!IsLiveReviewMode || gameTime.TotalGameTime.TotalSeconds < nextLiveCaptureTime)
            {
                return;
            }

            nextLiveCaptureTime = gameTime.TotalGameTime.TotalSeconds + LiveCaptureIntervalSeconds;

            try
            {
#if !BLAZORGL
                Directory.CreateDirectory(liveReviewDirectory);

                string imagePath = Path.Combine(liveReviewDirectory, "latest.png");
                string tempImagePath = imagePath + ".tmp";
                using (FileStream file = File.Create(tempImagePath))
                {
                    canvas.SaveAsPng(file, PixelArt.FieldWidth, PixelArt.Height);
                }
                File.Move(tempImagePath, imagePath, overwrite: true);
#endif

                var snapshot = new
                {
                    Phase = phase.ToString(),
                    Active = isWindowActive,
                    Elapsed = elapsed,
                    Score = currentCash.ToString(),
                    BucketX = bucketX,
                    MusicMuted = musicMuted,
                    SfxMuted = sfxMuted,
                    Drops = drops.Select(d => new
                    {
                        Kind = d.Motion.Kind.ToString(),
                        d.Motion.X,
                        d.Motion.Y
                    })
                };

                string statePath = Path.Combine(liveReviewDirectory, "state.json");
                string tempStatePath = statePath + ".tmp";
                File.WriteAllText(tempStatePath, JsonSerializer.Serialize(snapshot));
                File.Move(tempStatePath, statePath, overwrite: true);
            }
            catch (IOException error)
            {
                Debug.WriteLine("Live review capture skipped: " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                Debug.WriteLine("Live review capture skipped: " + error.Message);
            }
        }
    }
}
