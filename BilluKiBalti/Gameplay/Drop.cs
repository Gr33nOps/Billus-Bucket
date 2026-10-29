using System;

namespace BilluKiBalti
{
    /// <summary>
    /// Represents a single falling object (either a rupee note or a bomb) in the scene.
    /// </summary>
    public sealed class Drop
    {
        public DropMotion Motion { get; }
        public int NoteIndex { get; }

        public int Value => Motion.Kind == DropKind.Cash
            ? NoteCatalog.ValueAt(NoteIndex)
            : 0;

        public Drop(DropMotion motion, int noteIndex = 0)
        {
            Motion = motion ?? throw new ArgumentNullException(nameof(motion));
            if (noteIndex < 0 || noteIndex >= NoteCatalog.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(noteIndex));
            }

            NoteIndex = noteIndex;
        }
    }
}
