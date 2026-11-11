using System;
using System.Collections.Generic;

namespace BillusBucket
{
    /// <summary>
    /// Remembers the visible pile and the short landing animation after a catch.
    /// This is visual feedback only; BillusBucketRun holds the actual cash total.
    /// </summary>
    public sealed class BucketFeedback
    {
        private readonly List<int> notes = new List<int>(5);
        private readonly List<PaperPlacement> placements = new List<PaperPlacement>(5);
        private int catchNumber;
        private float catchAge = 1f;
        public IReadOnlyList<PaperPlacement> Placements => placements;

        // A small data container describing where one visible note rests in the bucket.
        public readonly record struct PaperPlacement(float Offset, float Tilt, int Frame, int Depth);
        public IReadOnlyList<int> Notes => notes;
        public float EntryOffset { get; private set; }
        public float CatchProgress => Math.Clamp(catchAge / .28f, 0f, 1f);

        // Paper settles inside the rim without bouncing the bucket or Billu.
        public int ImpactOffset => 0;

        public void Collect(int noteIndex, float offset)
        {
            if (noteIndex < 0 || noteIndex >= NoteCatalog.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(noteIndex));
            }

            // Keep the latest five visible notes. The full score still includes every catch.
            if (notes.Count == 5)
            {
                notes.RemoveAt(0);
                placements.RemoveAt(0);
            }

            notes.Add(noteIndex);
            EntryOffset = float.IsFinite(offset) ? Math.Clamp(offset, -8f, 8f) : 0f;

            // Stable per-paper placement: existing notes never shuffle into new slots.
            int variation = (++catchNumber * 7) % 11;
            placements.Add(new PaperPlacement(
                Math.Clamp(EntryOffset * .45f + (variation - 5) * .7f, -5f, 5f),
                (variation - 5) * .055f,
                0,
                catchNumber % 3 == 0 ? 1 : 0
            ));
            catchAge = 0f;
        }

        public void Update(float seconds)
        {
            if (float.IsFinite(seconds) && seconds > 0f)
            {
                catchAge = Math.Min(1f, catchAge + seconds);
            }
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
