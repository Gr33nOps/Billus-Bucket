using System;

namespace BillusBucket
{
    public static class SkyMotion
    {
        public static float NightAmount(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) return 0f;
            float phase = seconds % 240f;
            float amount = phase < 120f ? 0f : phase < 150f ? (phase - 120f) / 30f
                : phase < 210f ? 1f : (240f - phase) / 30f;
            return amount * amount * (3f - 2f * amount);
        }
        public static float CloudX(float seconds, float start, float speed) =>
            ((start + seconds * speed + 48f) % 368f) - 48f;
    }
}
