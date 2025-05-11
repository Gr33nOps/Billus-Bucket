using fruit_frenzy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Fruit_Frenzy
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager graphics;
        private SpriteBatch spriteBatch;
        private Texture2D basketTexture;
        private GameLogics gameLogics;
        private Basket basket;
        private int screenWidth = 800;
        private int screenHeight = 600;
        private SpriteFont font;

        Texture2D greenCashTexture;
        Texture2D goldCoinTexture;

        public Bomb bomb;
        public Game1()
        {
            graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            graphics.PreferredBackBufferWidth = screenWidth;
            graphics.PreferredBackBufferHeight = screenHeight;
            graphics.ApplyChanges();
            base.Initialize();
        }

        protected override void LoadContent()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);

            basketTexture = Content.Load<Texture2D>("basket");
            font = Content.Load<SpriteFont>("Fonts/font1");

            var blueCash = Content.Load<Texture2D>("blue cash");
            var orangeCash = Content.Load<Texture2D>("orange cash");
            var greenCash = Content.Load<Texture2D>("green cash");
            var goldCoin = Content.Load<Texture2D>("gold coin spritesheet");
            var silverCoin = Content.Load<Texture2D>("silver coin spritesheet");
            var copperCoin = Content.Load<Texture2D>("copper coin spritesheet");
            var bombTexture = Content.Load<Texture2D>("bomb");

            var cashTextures = new CashTextures(blueCash, orangeCash, greenCash);
            var coinTextures = new CoinTextures(goldCoin, silverCoin, copperCoin);

            basket = new Basket(basketTexture, new Vector2(screenWidth / 2, screenHeight - basketTexture.Height - 10), screenWidth);
            gameLogics = new GameLogics(cashTextures, coinTextures, bombTexture, screenWidth, screenHeight, font);
        }

        protected override void Update(GameTime gameTime)
        {
            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            gameLogics.Update(gameTime, basket);
            basket.Update(gameTime);
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);
            spriteBatch.Begin();

            // Draw basket
            basket.Draw(spriteBatch);

            // Draw cash
            foreach (var cash in gameLogics.GetCash())
            {
                cash.Draw(spriteBatch);
            }

            // Draw coins
            foreach (var coin in gameLogics.GetCoins()) // Add GetCoins method
            {
                coin.Draw(spriteBatch);
            }

            foreach (var bomb in gameLogics.GetBomb()) // Add GetCoins method
            {
                bomb.Draw(spriteBatch);
            }

            // Draw score
            gameLogics.DrawUI(spriteBatch);

            spriteBatch.End();
            base.Draw(gameTime);
        }
    }
}