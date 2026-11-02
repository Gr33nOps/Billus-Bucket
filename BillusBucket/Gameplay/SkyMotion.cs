using System;

namespace BillusBucket
{
    public static class SkyMotion
    {
        public static float NightAmount(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) return 0f;
            float phase = seconds % 60f;
            float amount = phase < 30f ? 0f : phase < 42f ? (phase - 30f) / 12f
                : phase < 51f ? 1f : (60f - phase) / 9f;
            return amount * amount * (3f - 2f * amount);
        }
        public static float CloudX(float seconds, float start, float speed) =>
            ((start + seconds * speed + 48f) % 368f) - 48f;
    }
}
