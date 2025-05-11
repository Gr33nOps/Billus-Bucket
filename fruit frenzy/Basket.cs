using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace Fruit_Frenzy
{
    public class Basket
    {
        public Texture2D picture;
        public Vector2 position;
        private int screenWidth;

        // Movement parameters
        private float maxSpeed = 800f;      // Top speed in pixels per second
        private float acceleration = 2500f; // Acceleration in pixels per second squared
        private float deceleration = 2000f; // Deceleration (friction) in pixels per second squared
        private float velocity = 0f;        // Current horizontal speed

        // Collision box for the basket
        public Rectangle CollisionBox => new Rectangle(
            (int)position.X,
            (int)position.Y,
            picture.Width,
            picture.Height
        );

        // Constructor
        public Basket(Texture2D basketPicture, Vector2 startPosition, int screenWidth)
        {
            picture = basketPicture;
            position = startPosition;
            this.screenWidth = screenWidth;
        }

        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            var kb = Keyboard.GetState();

            // Step 1: Determine input direction
            float inputDirection = 0;
            if (kb.IsKeyDown(Keys.Left))
            {
                inputDirection = -1; // Move left
            }
            else if (kb.IsKeyDown(Keys.Right))
            {
                inputDirection = 1;  // Move right
            }

            // Step 2: Update velocity based on input
            if (inputDirection != 0)
            {
                velocity += inputDirection * acceleration * dt; // Accelerate
            }
            else
            {
                // Decelerate towards zero when no keys are pressed
                if (velocity > 0)
                {
                    velocity = Math.Max(0, velocity - deceleration * dt);
                }
                else if (velocity < 0)
                {
                    velocity = Math.Min(0, velocity + deceleration * dt);
                }
            }

            // Step 3: Limit the speed
            velocity = MathHelper.Clamp(velocity, -maxSpeed, maxSpeed);

            // Step 4: Move the basket
            position.X += velocity * dt;

            // Step 5: Keep the basket on the screen
            position.X = MathHelper.Clamp(position.X, 0, screenWidth - picture.Width);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            spriteBatch.Draw(picture, position, Color.White);
        }
    }
}