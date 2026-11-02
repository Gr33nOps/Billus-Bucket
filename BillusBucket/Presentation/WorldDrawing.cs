using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BillusBucket
{
    /// <summary>
    /// Draws the sky, rooftop, falling objects, Billu, and the bucket.
    /// Drawing reads the game state; movement and catch decisions happen in Game/Playing.cs.
    /// </summary>
    public sealed partial class BillusBucketGame
    {
        private const int NoteFrameWidth = 12;
        private const int NoteFrameHeight = 8;

        protected override void Draw(GameTime gameTime)
        {
            // 1. Draw the pixel-art scene at native 320x180 resolution onto the offscreen canvas.
            GraphicsDevice.SetRenderTarget(canvas);
            GraphicsDevice.Clear(Color.Transparent);
            spriteBatch.Begin(samplerState: SamplerState.PointClamp);

            // Native-resolution diagnostic exports include the sky for a complete scene.
            if (reviewTools.IsArtReviewMode || reviewTools.IsLiveReviewMode)
            {
                DrawSky();
            }

            DrawScene();
            spriteBatch.End();

            // 2. Switch back to the window/browser backbuffer and run optional diagnostic captures.
            GraphicsDevice.SetRenderTarget(null);
            reviewTools.CaptureLiveReviewIfDue(
                gameTime,
                canvas,
                CurrentPhase,
                IsActive,
                runSeconds,
                currentRun.Cash,
                bucketControl.X,
                musicMuted,
                sfxMuted,
                drops
            );
            bool shouldExitAfterReview = reviewTools.SaveArtReviewFrameAndAdvance(canvas);

            // 3. Scale the 320x180 canvas crisply to a centered 16:9 viewport with nearest-neighbor filtering.
            Rectangle destination = GetCanvasDestinationRect();
            GraphicsDevice.Clear(Color.Black);
            GraphicsDevice.ScissorRectangle = destination;
            Matrix skyTransform = Matrix.CreateScale(destination.Width / 320f, destination.Height / 180f, 1f) * Matrix.CreateTranslation(destination.X, destination.Y, 0f);

            // Clouds move at display resolution, avoiding whole game-pixel jumps.
            spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: skyRasterizer, transformMatrix: skyTransform);
            DrawSky();
            spriteBatch.End();
            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            spriteBatch.Draw(canvas, destination, Color.White);
            spriteBatch.End();
            base.Draw(gameTime);
            if (shouldExitAfterReview)
            {
                Exit();
            }
        }

        private void DrawCatchingBucket(Vector2 root)
        {
            int x = (int)root.X - 14;
            int y = (int)root.Y - 44 + bucketFeedback.ImpactOffset;
            spriteBatch.Draw(bucketSprite, new Vector2(x, y), Color.White);
            float progress = bucketFeedback.CatchProgress;
            int count = bucketFeedback.Notes.Count;
            for (int i = 0; i < count; i++)
            {
                var paper = bucketFeedback.Placements[i];
                bool entering = i == count - 1 && progress < 1f;
                float offset = paper.Offset;
                float height = 1f + paper.Depth;
                float tilt = paper.Tilt;
                float scale = 2f / 3f;
                if (entering)
                {
                    // Drop near the catch point, then flop onto the pile.
                    // Most movement is downward; no sideways trip to an assigned slot.
                    float fall = Math.Min(1f, progress / .6f);
                    float landing = Math.Clamp((progress - .6f) / .4f, 0f, 1f);
                    offset = MathHelper.Lerp(bucketFeedback.EntryOffset * .65f, paper.Offset, progress);
                    height = MathHelper.Lerp(-4f, height, fall * fall) - MathF.Sin(landing * MathF.PI) * 1.4f;
                    tilt += MathF.Sin(progress * MathF.PI * 2f) * .22f;
                    scale = MathHelper.Lerp(.9f, 2f / 3f, fall);
                }

                int sourceX = (bucketFeedback.Notes[i] * NoteCatalog.FramesPerNote + paper.Frame) * NoteFrameWidth;
                Vector2 paperPosition = new Vector2(x + 14 + MathF.Round(offset), y + MathF.Round(height));
                Rectangle paperSource = new Rectangle(sourceX, 0, NoteFrameWidth, NoteFrameHeight);
                Vector2 paperScale = new Vector2(scale, .625f);

                // Rotation/downsampling can erase the source's one-pixel border.
                // Restore a continuous outline on the final native pixel grid.
                for (int edge = 0; edge < 4; edge++)
                {
                    Vector2 borderOffset = edge switch
                    {
                        0 => new Vector2(-1, 0),
                        1 => new Vector2(1, 0),
                        2 => new Vector2(0, -1),
                        _ => new Vector2(0, 1)
                    };
                    spriteBatch.Draw(
                        paperSilhouette,
                        paperPosition + borderOffset,
                        paperSource,
                        PanelInkColor,
                        tilt,
                        new Vector2(6f, 4f),
                        paperScale,
                        SpriteEffects.None,
                        0f
                    );
                }

                spriteBatch.Draw(
                    cashSprite,
                    paperPosition,
                    paperSource,
                    Color.White,
                    tilt,
                    new Vector2(6f, 4f),
                    paperScale,
                    SpriteEffects.None,
                    0f
                );
            }

            spriteBatch.Draw(bucketSprite, new Vector2(x, y), new Rectangle(0, 0, 4, 7), Color.White);
            spriteBatch.Draw(bucketSprite, new Vector2(x + 24, y), new Rectangle(24, 0, 4, 7), Color.White);

            // The front rim and body occlude the paper as it drops inside.
            spriteBatch.Draw(bucketSprite, new Vector2(x, y + 5), new Rectangle(0, 5, 28, 11), Color.White);
        }

        private void DrawSky()
        {
            float night = SkyMotion.NightAmount(runSeconds);
            Color top = Color.Lerp(new Color(111, 164, 184), new Color(34, 36, 65), night);
            Color bottom = Color.Lerp(new Color(218, 191, 162), new Color(76, 61, 90), night);
            float dusk = 4f * night * (1f - night);
            bottom = Color.Lerp(bottom, new Color(180, 124, 137), dusk * .4f);
            Color middle = Color.Lerp(new Color(149, 177, 188), new Color(53, 46, 79), night);
            for (int y = 0; y < 180; y++)
            {
                Color band = y < 72 ? Color.Lerp(top, middle, (y / 6) / 12f) : Color.Lerp(middle, bottom, ((y - 72) / 6) / 17f);
                spriteBatch.Draw(pixel, new Rectangle(0, y, 320, 1), band);
            }

            for (int i = 0; i < CloudShapes.Length; i++)
            {
                Rectangle source = CloudShapes[i];
                float x = SkyMotion.CloudX(sceneSeconds, source.X, .45f + i * .1f);
                spriteBatch.Draw(
                    skyClouds,
                    new Vector2(x, source.Y),
                    source,
                    Color.Lerp(new Color(223, 222, 216), new Color(81, 77, 107), night)
                );
            }
        }

        private void DrawScene()
        {
            spriteBatch.Draw(background, Vector2.Zero, Color.Lerp(Color.White, new Color(158, 161, 205), SkyMotion.NightAmount(runSeconds)));

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
                    spriteBatch.Draw(
                        cashSprite,
                        position,
                        sourceRect,
                        Color.White,
                        motion.Rotation,
                        new Vector2(6f, 4f),
                        1f,
                        SpriteEffects.None,
                        0f
                    );
                }
                else
                {
                    Rectangle sourceRect = new Rectangle(frame * 16, 0, 16, 16);
                    spriteBatch.Draw(
                        bombSprite,
                        position,
                        sourceRect,
                        Color.White,
                        0f,
                        new Vector2(4f, 8f),
                        1f,
                        SpriteEffects.None,
                        0f
                    );
                }
            }

            // Billu and his bucket
            Vector2 characterRoot = new Vector2((int)bucketControl.X, PixelArt.GroundTop);
            if (CurrentPhase != RoundPhase.GameOver)
            {
                int frame = billuAnimator.SpriteFrame;
                spriteBatch.Draw(
                    billuSheet,
                    characterRoot,
                    new Rectangle(frame * 32, 48, 32, 48),
                    Color.White,
                    0f,
                    new Vector2(16f, 44f),
                    1f,
                    SpriteEffects.None,
                    0f
                );
                DrawCatchingBucket(characterRoot);
            }
            else
            {
                int blinkFrame = (int)(sceneSeconds * 2f) % 7 == 0 ? 1 : 0;
                spriteBatch.Draw(
                    burnedSprite,
                    characterRoot,
                    new Rectangle(blinkFrame * 24, 0, 24, 32),
                    Color.White,
                    0f,
                    new Vector2(12f, 32f),
                    1f,
                    SpriteEffects.None,
                    0f
                );

                // After the blast, the dropped bucket falls from overhead onto the roof.
                float fallProgress = MathHelper.Clamp((ExplosionDurationSeconds - explosionCountdown) / 0.48f, 0f, 1f);
                int bucketBottom = (int)MathHelper.Lerp(PixelArt.GroundTop - 28, PixelArt.GroundTop, fallProgress * fallProgress);
                spriteBatch.Draw(
                    burnedBucketSprite,
                    new Vector2((int)bucketControl.X, bucketBottom),
                    null,
                    Color.White,
                    0f,
                    new Vector2(14f, 16f),
                    1f,
                    SpriteEffects.None,
                    0f
                );

                // Small rising smoke wisps
                for (int i = 0; i < 3; i++)
                {
                    int rise = (int)((sceneSeconds * 8f + i * 5f) % 16f);
                    int size = 2 + i;
                    float smokeOpacity = 0.5f - rise * 0.025f;
                    DrawRect(
                        (int)bucketControl.X - 4 + i * 3,
                        PixelArt.GroundTop - 7 - rise,
                        size,
                        size,
                        SmokePuffColor * smokeOpacity
                    );
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

            if (CurrentPhase == RoundPhase.GameOver && explosionCountdown > 0f)
            {
                float progress = (ExplosionDurationSeconds - explosionCountdown) / ExplosionDurationSeconds;
                int explosionFrame = Math.Min(7, (int)(progress * 8f));
                spriteBatch.Draw(
                    explosionSheet,
                    new Vector2((int)bucketControl.X, PixelArt.GroundTop - 31),
                    new Rectangle(explosionFrame * 48, 0, 48, 48),
                    Color.White,
                    0f,
                    new Vector2(24f, 24f),
                    1f,
                    SpriteEffects.None,
                    0f
                );
            }

            if (screenFlashStrength > 0f)
            {
                DrawRect(0, 0, PixelArt.FieldWidth, PixelArt.Height, WarmTitleColor * screenFlashStrength);
            }

            DrawNightLighting();
            DrawHud();
            if (roundFlow.IsLeaderboardOpen)
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
            else if (CurrentPhase == RoundPhase.GameOver && explosionCountdown <= 0f)
            {
                DrawGameOverScreen();
            }
        }

        private void DrawNightLighting()
        {
            float night = SkyMotion.NightAmount(runSeconds);
            if (night <= 0f)
            {
                return;
            }

            // One shared veil dims sky, paper, bombs, Billu and buildings equally.
            // It precedes the HUD so text and controls retain their contrast.
            DrawRect(0, 0, 320, 180, new Color(17, 20, 39) * (night * .22f));
            float light = Math.Clamp((night - .25f) / .5f, 0f, 1f);

            // Brief ignition flicker during dusk; a steady bulb through the short night.
            if (runSeconds % 240f < 150f && light > 0f && light < 1f && (int)(runSeconds * 9f) % 4 == 0)
            {
                light *= .35f;
            }

            // Elliptical falloff follows the hanging bulb; no rectangular halo bands.
            for (int y = 136; y <= 152; y++)
            {
                for (int x = 21; x <= 35; x++)
                {
                    float dx = (x - 28f) / 7f, dy = (y - 144f) / 8f;
                    float falloff = Math.Max(0f, 1f - dx * dx - dy * dy);
                    DrawRect(x, y, 1, 1, new Color(240, 166, 80) * (falloff * falloff * .23f * light));
                }
            }

            // Keep the original oval housing and its dark outline visible.
            for (int y = 141; y <= 146; y++)
            {
                int inset = y == 141 || y == 146 ? 1 : 0;
                DrawRect(26 + inset, y, 5 - inset * 2, 1, new Color(204, 133, 66) * light);
            }

            DrawRect(27, 142, 3, 4, new Color(239, 195, 105) * light);
            DrawRect(28, 142, 1, 2, new Color(255, 235, 168) * light);
        }
    }
}
