using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BillusBucket
{
    /// <summary>
    /// Hand-drawn rooftop lettering: rounded corners, sturdy strokes, and
    /// proportional spacing. Integer scaling always preserves square pixels.
    /// </summary>
    internal static class PixelFont
    {
        public const int Height = 6;

        private static readonly Dictionary<char, string[]> Glyphs = new()
        {
            ['A'] = new[] { "01110", "11011", "11011", "11111", "11011", "11011" },
            ['B'] = new[] { "11110", "11011", "11110", "11011", "11011", "11110" },
            ['C'] = new[] { "01111", "11000", "11000", "11000", "11000", "01111" },
            ['D'] = new[] { "11110", "11011", "11011", "11011", "11011", "11110" },
            ['E'] = new[] { "11111", "11000", "11110", "11000", "11000", "11111" },
            ['F'] = new[] { "11111", "11000", "11110", "11000", "11000", "11000" },
            ['G'] = new[] { "01110", "11000", "11000", "11011", "11011", "01110" },
            ['H'] = new[] { "11011", "11011", "11111", "11011", "11011", "11011" },
            ['I'] = new[] { "11", "11", "11", "11", "11", "11" },
            ['J'] = new[] { "00111", "00011", "00011", "00011", "11011", "01110" },
            ['K'] = new[] { "11011", "11011", "11110", "11110", "11011", "11011" },
            ['L'] = new[] { "1100", "1100", "1100", "1100", "1100", "1111" },
            ['M'] = new[] { "1100011", "1110111", "1111111", "1101011", "1100011", "1100011" },
            ['N'] = new[] { "110011", "111011", "111111", "110111", "110011", "110011" },
            ['O'] = new[] { "01110", "11011", "11011", "11011", "11011", "01110" },
            ['P'] = new[] { "11110", "11011", "11011", "11110", "11000", "11000" },
            ['Q'] = new[] { "01110", "11011", "11011", "11011", "01110", "00011" },
            ['R'] = new[] { "11110", "11011", "11011", "11110", "11011", "11011" },
            ['S'] = new[] { "01111", "11000", "01110", "00011", "00011", "11110" },
            ['T'] = new[] { "111111", "001100", "001100", "001100", "001100", "001100" },
            ['U'] = new[] { "11011", "11011", "11011", "11011", "11011", "01110" },
            ['V'] = new[] { "110011", "110011", "110011", "110011", "011110", "001100" },
            ['W'] = new[] { "1100011", "1100011", "1101011", "1111111", "1110111", "0100010" },
            ['X'] = new[] { "11011", "11011", "01110", "01110", "11011", "11011" },
            ['Y'] = new[] { "110011", "110011", "011110", "001100", "001100", "001100" },
            ['Z'] = new[] { "11111", "00011", "00110", "01100", "11000", "11111" },
            ['0'] = new[] { "01110", "11011", "11011", "11011", "11011", "01110" },
            ['1'] = new[] { "0110", "1110", "0110", "0110", "0110", "1111" },
            ['2'] = new[] { "01110", "11011", "00011", "00110", "01100", "11111" },
            ['3'] = new[] { "11110", "00011", "01110", "00011", "00011", "11110" },
            ['4'] = new[] { "11011", "11011", "11111", "00011", "00011", "00011" },
            ['5'] = new[] { "11111", "11000", "11110", "00011", "00011", "11110" },
            ['6'] = new[] { "01110", "11000", "11110", "11011", "11011", "01110" },
            ['7'] = new[] { "11111", "00011", "00110", "00110", "01100", "01100" },
            ['8'] = new[] { "01110", "11011", "01110", "11011", "11011", "01110" },
            ['9'] = new[] { "01110", "11011", "11011", "01111", "00011", "01110" },
            ['$'] = new[] { "00100", "01111", "11100", "00111", "11110", "00100" },
            [':'] = new[] { "00", "11", "11", "00", "11", "11" },
            ['/'] = new[] { "00011", "00011", "00110", "01100", "11000", "11000" },
            ['-'] = new[] { "0000", "0000", "1111", "1111", "0000", "0000" },
            ['+'] = new[] { "00000", "00100", "11111", "00100", "00000", "00000" },
            ['#'] = new[] { "01010", "11111", "01010", "11111", "01010", "00000" },
            ['_'] = new[] { "0000", "0000", "0000", "0000", "0000", "1111" },
            ['!'] = new[] { "11", "11", "11", "11", "00", "11" },
            ['.'] = new[] { "00", "00", "00", "00", "11", "11" },
            [','] = new[] { "00", "00", "00", "11", "11", "10" },
            ['\''] = new[] { "11", "11", "10", "00", "00", "00" },
            ['?'] = new[] { "11110", "00011", "00110", "01100", "00000", "01100" },
            [' '] = new[] { "000", "000", "000", "000", "000", "000" },
        };

        private static string[] Glyph(char character) =>
            Glyphs.TryGetValue(char.ToUpperInvariant(character), out var glyph)
                ? glyph : Glyphs['?'];

        private static (int Left, int Width) InkBounds(string value)
        {
            int left = int.MaxValue;
            int right = -1;
            int cursor = 0;
            foreach (char character in value ?? string.Empty)
            {
                string[] glyph = Glyph(character);
                for (int row = 0; row < Height; row++)
                    for (int column = 0; column < glyph[row].Length; column++)
                        if (glyph[row][column] == '1')
                        {
                            left = Math.Min(left, cursor + column);
                            right = Math.Max(right, cursor + column);
                        }
                cursor += glyph[0].Length + 1;
            }
            return right < 0 ? (0, 0) : (left, right - left + 1);
        }

        public static int Width(string value, int scale) =>
            InkBounds(value).Width * Math.Max(1, scale);

        public static void Draw(SpriteBatch batch, Texture2D pixel, string value,
            Vector2 position, Color color, int scale)
        {
            int size = Math.Max(1, scale);
            int cursor = (int)position.X - InkBounds(value).Left * size;
            foreach (char character in value ?? string.Empty)
            {
                string[] glyph = Glyph(character);
                for (int row = 0; row < Height; row++)
                    for (int column = 0; column < glyph[row].Length; column++)
                        if (glyph[row][column] == '1')
                            batch.Draw(pixel, new Rectangle(cursor + column * size,
                                (int)position.Y + row * size, size, size), color);
                cursor += (glyph[0].Length + 1) * size;
            }
        }
    }
}
