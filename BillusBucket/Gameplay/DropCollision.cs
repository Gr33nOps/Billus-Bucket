using System;

namespace BillusBucket
{
    public static class DropCollision
    {
        public static bool TryFindCatch(float startX, float startY, float endX, float endY,
            float bucketStartX, float bucketEndX, float rimY, float halfWidth, float halfHeight, out float time)
        {
            float enter = 0f, exit = 1f;
            float relativeX = startX - bucketStartX;
            float relativeEndX = endX - bucketEndX;
            bool hit = Clip(relativeX, relativeEndX - relativeX, halfWidth, ref enter, ref exit) &&
                Clip(startY - rimY, endY - startY, halfHeight, ref enter, ref exit);
            time = enter;
            return hit;
        }

        private static bool Clip(float position, float delta, float halfExtent, ref float enter, ref float exit)
        {
            if (!float.IsFinite(position) || !float.IsFinite(delta) || !float.IsFinite(halfExtent) || halfExtent < 0f)
                return false;
            if (Math.Abs(delta) < 0.000001f) return Math.Abs(position) <= halfExtent;
            float first = (-halfExtent - position) / delta;
            float last = (halfExtent - position) / delta;
            if (first > last) (first, last) = (last, first);
            enter = Math.Max(enter, first);
            exit = Math.Min(exit, last);
            return enter <= exit;
        }
    }
}
