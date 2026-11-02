using System;

namespace BillusBucket
{
    /// <summary>Calculates the day/night blend and the slow cloud positions.</summary>
    public static class SkyMotion
    {
        public static float NightAmount(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f)
            {
                return 0f;
            }

            // % wraps the clock back to zero after each four-minute cycle.
            float phase = seconds % 240f;
            float amount;
            if (phase < 120f)
            {
                amount = 0f; // Day: 0 to 120 seconds.
            }
            else if (phase < 150f)
            {
                amount = (phase - 120f) / 30f; // Dusk: 120 to 150 seconds.
            }
            else if (phase < 210f)
            {
                amount = 1f; // Night: 150 to 210 seconds.
            }
            else
            {
                amount = (240f - phase) / 30f; // Dawn: 210 to 240 seconds.
            }

            // Ease the blend at each end instead of switching brightness abruptly.
            return amount * amount * (3f - 2f * amount);
        }

        public static float CloudX(float seconds, float start, float speed)
        {
            // Travel across the sky, including an offscreen margin, then wrap to the left.
            return ((start + seconds * speed + 48f) % 368f) - 48f;
        }
    }
}
