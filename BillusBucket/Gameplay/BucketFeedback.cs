using System;
using System.Collections.Generic;

namespace BillusBucket
{
    /// <summary>Small, bounded visual memory of caught notes and their landing.</summary>
    public sealed class BucketFeedback
    {
        private readonly List<int> notes = new List<int>(5);
        private readonly List<PaperPlacement> placements = new List<PaperPlacement>(5);
        private int catchNumber;
        private float catchAge = 1f;
        public IReadOnlyList<PaperPlacement> Placements => placements;
        public readonly record struct PaperPlacement(float Offset, float Tilt, int Frame, int Depth);
        public IReadOnlyList<int> Notes => notes;
        public float EntryOffset { get; private set; }
        public float CatchProgress => Math.Clamp(catchAge / .28f, 0f, 1f);
        public int ImpactOffset => catchAge >= .12f && catchAge < .19f ? 1
            : catchAge >= .19f && catchAge < .24f ? -1 : 0;

        public void Collect(int noteIndex, float offset)
        {
            if (noteIndex < 0 || noteIndex >= NoteCatalog.Count)
                throw new ArgumentOutOfRangeException(nameof(noteIndex));
            if (notes.Count == 5) { notes.RemoveAt(0); placements.RemoveAt(0); }
            notes.Add(noteIndex);
            EntryOffset = float.IsFinite(offset) ? Math.Clamp(offset, -8f, 8f) : 0f;
            // Stable per-paper placement: existing notes never shuffle into new slots.
            int variation = (++catchNumber * 7) % 11;
            placements.Add(new PaperPlacement(
                Math.Clamp(EntryOffset * .45f + (variation - 5) * .7f, -5f, 5f),
                (variation - 5) * .055f, catchNumber % 4, catchNumber % 3 == 0 ? 1 : 0));
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
            placements.Clear();
            catchNumber = 0;
            catchAge = 1f;
            EntryOffset = 0f;
        }
    }
}
