namespace BilluKiBalti
{
    /// <summary>
    /// Native pixel-art canvas dimensions and playable rooftop boundaries.
    /// </summary>
    internal static class PixelArt
    {
        public const int FieldWidth = 320;
        public const int Height = 180;
        public const int DisplayScale = 4;

        public const int WindowWidth = FieldWidth * DisplayScale;
        public const int WindowHeight = Height * DisplayScale;

        public const int GroundTop = 165;
        public const int PlayLeft = 50;
        public const int PlayRight = 268;
        public const int PlayWidth = PlayRight - PlayLeft;
    }
}
