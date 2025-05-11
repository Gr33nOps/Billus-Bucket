using fruit_frenzy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Fruit_Frenzy
{
    public class Bomb
    {
        private Texture2D bombTexture;
        private AnimationManager animation;

        private Vector2 position;
        private int frameWidth = 70;
        private int frameHeight =70;

        private float screenHeight;
        public bool isFalling;
        private float fallSpeed;
        public float startX;
        public bool wasCaught;

        public Bomb(Texture2D bombTexture, Vector2 position, float screenHeight) 
        {
            this.bombTexture = bombTexture;
            this.position = position;
            this.startX = position.X;
            isFalling = true;
            fallSpeed = 0f;
            this.screenHeight = screenHeight;
            this.animation = new AnimationManager(2,2, new Vector2(76, 70), 0.4f);
        }

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

        public void Update(GameTime gameTime, float totalGameTime) 
        {
            float timepassed = (float)gameTime.ElapsedGameTime.TotalSeconds;

            animation.Update(gameTime);

            float currentGravity = MathHelper.Min(500 + 2f * totalGameTime, 1500f);
            fallSpeed += currentGravity * timepassed;

            position.Y += fallSpeed * timepassed;

            if (position.Y - animation.GetFrame().Height /2f > screenHeight) 
            {
                isFalling = false;
            }
        }

        public void Draw(SpriteBatch spriteBatch) 
        {
            Rectangle source = animation.GetFrame();
            spriteBatch.Draw(
                bombTexture, 
                position,
                source,
                Color.White);
        }
    }
}
