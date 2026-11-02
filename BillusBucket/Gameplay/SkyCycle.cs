using System;

namespace BillusBucket
{
    public static class SkyCycle
    {
        public static float Night(float seconds) =>
            .5f - .5f * MathF.Cos((seconds % 180f) * (MathF.PI * 2f / 180f));
        public static float CloudX(float seconds, float start, float speed) =>
            ((start + seconds * speed + 48f) % 368f) - 48f;
    }
}
