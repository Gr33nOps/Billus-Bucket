using fruit_frenzy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Fruit_Frenzy
{
    public class CoinTextures
    {
        public Texture2D Gold { get; set; }
        public Texture2D Silver { get; set; }
        public Texture2D Copper { get; set; }

        public CoinTextures(Texture2D gold, Texture2D silver, Texture2D copper)
        {
            Gold = gold;
            Silver = silver;
            Copper = copper;
        }
    }

    public class Coin
    {
        private Texture2D TexutureToSpawn;
        private Texture2D goldCoinTexture, silverCoinTexture, copperCoinTexture;
        private AnimationManager animation;
        public Vector2 position; // Represents the center of the coin
        public bool isFalling;
        private float fallSpeed;
        private int screenBottom;
        public bool wasCaught = false;

        private int frameWidth = 80; // Updated to match sprite sheet
        private int frameHeight = 70; // Updated to match sprite sheet
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

        public Coin(CoinTextures textures, Vector2 startPosition, int screenHeight)
        {
            Random rand = new Random();

            this.goldCoinTexture = textures.Gold;
            this.silverCoinTexture = textures.Silver;
            this.copperCoinTexture = textures.Copper;


            int choice = rand.Next(3); // 0, 1, or 2

            switch (choice)
            {
                case 0:
                    TexutureToSpawn = goldCoinTexture;
                    break;
                case 1:
                    TexutureToSpawn = silverCoinTexture;
                    break;
                case 2:
                    TexutureToSpawn = copperCoinTexture;
                    break;
            }


            this.position = startPosition;
            this.startX = startPosition.X;
            this.screenBottom = screenHeight;
            this.isFalling = true;
            this.fallSpeed = 0f;
            this.animation = new AnimationManager(10, 5, new Vector2(48, 43), 0.1f);
        }

        public void Update(GameTime gameTime, float totalGameTime)
        {
            float timePassed = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Update animation
            animation.Update(gameTime); // Add this to advance animation frames

            // Clamped gravity: starts at 100, increases by 2 per second, caps at 500
            float currentGravity = MathHelper.Min(500f + 2f * totalGameTime, 1500f);
            fallSpeed += currentGravity * timePassed;
            position.Y += fallSpeed * timePassed;

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