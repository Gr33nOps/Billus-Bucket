using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using static System.Reflection.Metadata.BlobBuilder;

namespace Fruit_Frenzy
{
    public class GameLogics
    {
        private CashTextures cashTextures;
        private CoinTextures coinTextures;
        private Texture2D bombTexture;

        private List<Coin> coin;
        private List<Cash> cash;
        private List<Bomb> bomb;

        private Random randomPicker;
        private int screenWidth;
        private int screenHeight;
        private float timeUntilNextApple;
        private SpriteFont font;
        private int score;
        private float totalGameTime;
        private float screenBottom;
        private bool gameOver = false;

        public GameLogics(CashTextures cashTextures, CoinTextures coinTextures, Texture2D bombTexture, int width, int height, SpriteFont font)
        {
            this.cashTextures = cashTextures;
            this.coinTextures = coinTextures;
            this.bombTexture = bombTexture;

            coin = new List<Coin>();
            cash = new List<Cash>();
            bomb = new List<Bomb>();

            randomPicker = new Random();
            screenWidth = width;
            screenHeight = height;
            timeUntilNextApple = 0f;
            this.font = font;
            score = 0;
            totalGameTime = 0f;
            screenBottom = height; // Initialize screenBottom
        }

        public void Update(GameTime gameTime, Basket basket)
        {
            if (gameOver)
            {
                return;
            }

            float timePassed = (float)gameTime.ElapsedGameTime.TotalSeconds;
            totalGameTime += timePassed;

            // Match gravity calculation with Coins class
            float initialGravity = 100f;
            float currentGravity = MathHelper.Min(initialGravity + 10f * totalGameTime, 3000f);

            // Calculate spawn interval inversely proportional to gravity
            float baseInterval = 1.0f; // Starting interval (1 item per second)
            float minInterval = 0.3f;  // Minimum interval (10 items per second max)
            float spawnInterval = MathHelper.Max(baseInterval * (initialGravity / currentGravity), minInterval);

            // Spawn new items
            timeUntilNextApple += timePassed;
            if (timeUntilNextApple >= spawnInterval)
            {
                SpawnNewItem();
                timeUntilNextApple -= spawnInterval;
            }

            // Update bomb and check collisions
            for (int i = bomb.Count - 1; i >= 0; i--)
            {
                bomb[i].Update(gameTime, totalGameTime);
                if (bomb[i].isFalling && bomb[i].CollisionBox.Intersects(basket.CollisionBox))
                {
                    bomb[i].isFalling = false;
                    bomb[i].wasCaught = true;
                    gameOver = true;
                    score = 0; 
                }

                if (!bomb[i].isFalling)
                {
                    bomb.RemoveAt(i);
                }
            }

            // Update cash and check collisions
            for (int i = cash.Count - 1; i >= 0; i--)
            {
                cash[i].Update(gameTime, totalGameTime);
                if (cash[i].isFalling && cash[i].CollisionBox.Intersects(basket.CollisionBox))
                {
                    cash[i].isFalling = false;
                    cash[i].wasCaught = true;
                    score++; // Increment score for cash
                }

                if (!cash[i].isFalling)
                {
                    cash.RemoveAt(i);
                }
            }

            // Update coins and check collisions
            for (int i = coin.Count - 1; i >= 0; i--)
            {
                coin[i].Update(gameTime, totalGameTime);
                if (coin[i].isFalling && coin[i].CollisionBox.Intersects(basket.CollisionBox))
                {
                    coin[i].isFalling = false;
                    coin[i].wasCaught = true;
                    score++; // Increment score for coin (or adjust score value)
                }

                if (!coin[i].isFalling)
                {
                    coin.RemoveAt(i);
                }
            }
        }

        private void SpawnNewItem()
        {
            int frameWidth = 70;   // Adjust per your item sizes
            int frameHeight = 70;

            int randomX = randomPicker.Next(0, screenWidth - frameWidth);
            float centerX = randomX + frameWidth / 2f;
            float centerY = -frameHeight / 2f; // Spawn just above the screen
            Vector2 startPosition = new Vector2(centerX, centerY);

            double roll = randomPicker.NextDouble(); // Random value between 0.0 and 1.0

            if (roll < 0.5)
            {
                coin.Add(new Coin(coinTextures, startPosition, screenHeight)); // 50% chance
            }
            else if (roll < 0.8)
            {
                cash.Add(new Cash(cashTextures, startPosition, screenHeight)); // 30% chance
            }
            else
            {
                bomb.Add(new Bomb(bombTexture, startPosition, screenHeight)); // 20% chance
            }
        }

        public void DrawUI(SpriteBatch spriteBatch)
        {
            if (gameOver)
            {
                string gameOverText = "GAME OVER";
                Vector2 textSize = font.MeasureString(gameOverText);
                Vector2 position = new Vector2(
                    (screenWidth - textSize.X) / 2,
                    (screenHeight - textSize.Y) / 2
                );
                spriteBatch.DrawString(font, gameOverText, position, Color.Red);
            }

            // Always draw score
            spriteBatch.DrawString(font, $"Score: {score}", new Vector2(10, 10), Color.White);
        }

        public List<Cash> GetCash()
        {
            return cash;
        }
        public List<Coin> GetCoins()
        {
            return coin;
        }
        public List<Bomb> GetBomb()
        {
            return bomb;
        }
    }
}