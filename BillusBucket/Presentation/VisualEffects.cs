using Microsoft.Xna.Framework;

namespace BillusBucket
{
    /// <summary>
    /// A single 1x1 pixel spark created during a bomb explosion.
    /// </summary>
    internal sealed class Particle
    {
        private readonly Vector2 velocity;

        public Vector2 Position { get; private set; }
        public Color Color { get; }
        public float Life { get; private set; }

        public float Opacity => MathHelper.Clamp(Life * 2f, 0f, 1f);

        public Particle(Vector2 position, Vector2 velocity, Color color, float life)
        {
            Position = position;
            this.velocity = velocity;
            Color = color;
            Life = life;
        }

        public void Update(float dt)
        {
            Position += velocity * dt;
            Life -= dt;
        }
    }

    /// <summary>
    /// Floating "+RS ..." text that rises above the bucket when Billu catches a note.
    /// </summary>
    internal sealed class Popup
    {
        private const float DefaultDurationSeconds = 0.8f;
        private const float RiseSpeedPixelsPerSecond = 25f;

        public string Text { get; }
        public Color Color { get; }
        public Vector2 Position { get; private set; }
        public float Life { get; private set; } = DefaultDurationSeconds;

        public float Opacity => MathHelper.Clamp(Life / DefaultDurationSeconds, 0f, 1f);

        public Popup(string text, Vector2 position, Color color)
        {
            Text = text;
            Position = position;
            Color = color;
        }

        public void Update(float dt)
        {
            Position -= new Vector2(0f, RiseSpeedPixelsPerSecond * dt);
            Life -= dt;
        }
    }
}
