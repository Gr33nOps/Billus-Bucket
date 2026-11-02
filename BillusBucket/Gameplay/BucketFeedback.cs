using System;
using System.Collections.Generic;

namespace BillusBucket
{
    /// <summary>Small, bounded visual memory of caught notes and their landing.</summary>
    public sealed class BucketFeedback
    {
        private readonly List<int> notes = new List<int>(5);
        private float catchAge = 1f;
        public IReadOnlyList<int> Notes => notes;
        public float EntryOffset { get; private set; }
        public float CatchProgress => Math.Clamp(catchAge / .28f, 0f, 1f);
        public int ImpactOffset => catchAge >= .12f && catchAge < .19f ? 1
            : catchAge >= .19f && catchAge < .24f ? -1 : 0;

        public void Collect(int noteIndex, float offset)
        {
            if (noteIndex < 0 || noteIndex >= NoteCatalog.Count)
                throw new ArgumentOutOfRangeException(nameof(noteIndex));
            if (notes.Count == 5) notes.RemoveAt(0);
            notes.Add(noteIndex);
            EntryOffset = float.IsFinite(offset) ? Math.Clamp(offset, -8f, 8f) : 0f;
            catchAge = 0f;
        }

        public void Update(float seconds)
        {
            if (float.IsFinite(seconds) && seconds > 0f)
                catchAge = Math.Min(1f, catchAge + seconds);
        }

        public void Reset()
        {
            notes.Clear();
            catchAge = 1f;
            EntryOffset = 0f;
        }
    }
}
