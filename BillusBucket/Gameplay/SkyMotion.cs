using System;

namespace BillusBucket
{
    public static class SkyMotion
    {
        public static float CloudX(float seconds, float start, float speed) =>
            ((start + seconds * speed + 48f) % 368f) - 48f;
    }
}
