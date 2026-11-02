using System;

namespace BillusBucket
{
    public static class SkyMotion
    {
        public static float NightAmount(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) return 0f;
            float phase = seconds % 125f;
            float amount = phase < 45f ? 0f : phase < 65f ? (phase - 45f) / 20f
                : phase < 105f ? 1f : (125f - phase) / 20f;
            return amount * amount * (3f - 2f * amount);
        }
        public static float CloudX(float seconds, float start, float speed) =>
            ((start + seconds * speed + 48f) % 368f) - 48f;
    }
}
