using fruit_frenzy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Fruit_Frenzy
{
    public class CashTextures
    {
        public Texture2D Blue { get; set; }
        public Texture2D Orange { get; set; }
        public Texture2D Green { get; set; }

        public CashTextures(Texture2D blue, Texture2D orange, Texture2D green)
        {
            Blue = blue;
            Orange = orange;
            Green = green;
        }
    }

    public class Cash
    {
        private Texture2D TexutureToSpawn; // This is the selected random texture
        private Texture2D blueCashTexture, orangeCashTexture, greenCashTexture;
        private AnimationManager animation;

        public Vector2 position; // Represents the center of the coin
        private int frameWidth = 80; // Updated to match sprite sheet
        private int frameHeight = 70; // Updated to match sprite sheet

        public bool isFalling;
        private float fallSpeed;
        private int screenBottom;
        public bool wasCaught = false;
        private float driftAmplitude;
        private float driftFrequency;
        private float startX;

        public Rectangle CollisionBox
        {
            get
            {
                return new Rectangle(
                    (int)(position.X - frameWidth / 2f),
                    (int)(position.Y - frameHeight / 2f),
                    frameWidth,
                    frameHeight
                );
            }
        }

        public Cash(CashTextures textures, Vector2 Position, int screenHeight)
        {
            Random rand = new Random();

            this.blueCashTexture = textures.Blue;
            this.orangeCashTexture = textures.Orange;
            this.greenCashTexture = textures.Green;

            int choice = rand.Next(3); // 0, 1, or 2

            switch (choice)
            {
                case 0:
                    TexutureToSpawn = blueCashTexture;
                    break;
                case 1:
                    TexutureToSpawn = orangeCashTexture;
                    break;
                case 2:
                    TexutureToSpawn = greenCashTexture;
                    break;
            }

            this.position = Position;
            this.startX = Position.X;
            this.screenBottom = screenHeight;
            this.isFalling = true;
            this.fallSpeed = 0f;
            this.animation = new AnimationManager(12, 4, new Vector2(80, 70),0.1f);
            this.driftAmplitude = (float)(new Random().NextDouble() * 50f + 50f);
            this.driftFrequency = (float)(new Random().NextDouble() * 1.5f + 0.55f);
        }

        public void Update(GameTime gameTime, float totalGameTime)
        {
            float timePassed = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Update animation
            animation.Update(gameTime); // Add this to advance animation frames

            // Clamped gravity: starts at 100, increases by 2 per second, caps at 500
            float currentGravity = MathHelper.Min(50f + 2f * totalGameTime, 500f);
            fallSpeed += currentGravity * timePassed;
            position.Y += fallSpeed * timePassed;
            position.X = startX + driftAmplitude * (float)Math.Sin(driftFrequency * totalGameTime);


            // Check if the top of the coin is below the screen bottom
            if (position.Y - animation.GetFrame().Height / 2f > screenBottom)
            {
                isFalling = false;
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Rectangle source = animation.GetFrame();
            Vector2 origin = new Vector2(source.Width / 2f, source.Height / 2f);
            spriteBatch.Draw(
                TexutureToSpawn,
                position,
                source,
                Color.White,
                0f,
                origin,
                1f,
                SpriteEffects.None,
                0f
            );
        }
    }
}