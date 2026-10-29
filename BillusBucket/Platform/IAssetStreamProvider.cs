using System;
using System.Collections.Generic;
using System.IO;

namespace BillusBucket
{
    /// <summary>
    /// Opens read-only streams for runtime PNG and WAV assets.
    /// Desktop reads directly from the Assets folder; WebAssembly reads preloaded byte buffers.
    /// </summary>
    public interface IAssetStreamProvider
    {
        Stream OpenRead(string relativeAssetPath);
    }

    public sealed class DesktopAssetStreamProvider : IAssetStreamProvider
    {
        private readonly string assetsRoot;

        public DesktopAssetStreamProvider(string assetsRoot = null)
        {
            this.assetsRoot = string.IsNullOrWhiteSpace(assetsRoot)
                ? Path.Combine(AppContext.BaseDirectory, "Assets")
                : assetsRoot;
        }

        public Stream OpenRead(string relativeAssetPath)
        {
            if (string.IsNullOrWhiteSpace(relativeAssetPath))
            {
                throw new ArgumentException("Asset path cannot be empty.", nameof(relativeAssetPath));
            }

            string normalized = relativeAssetPath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(assetsRoot, normalized);
            return File.OpenRead(fullPath);
        }
    }

    public sealed class InMemoryAssetStreamProvider : IAssetStreamProvider
    {
        private readonly Dictionary<string, byte[]> assets =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public static readonly string[] RequiredAssets =
        {
            "rooftop.png",
            "rupee-notes.png",
            "bomb-sheet.png",
            "bucket-burned.png",
            "billu-carry.png",
            "billu-burned.png",
            "explosion-sheet.png",
            "ui-sign.png",
            "ui-button.png",
            "ui-audio.png",
            "Audio/rooftop-loop.wav",
            "Audio/catch.wav",
            "Audio/start.wav",
            "Audio/pause.wav",
            "Audio/resume.wav",
            "Audio/explode.wav",
            "Audio/sad.wav",
            "Audio/toggle.wav"
        };

        public void Add(string relativeAssetPath, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(relativeAssetPath))
            {
                throw new ArgumentException("Asset path cannot be empty.", nameof(relativeAssetPath));
            }

            assets[NormalizeKey(relativeAssetPath)] = data ?? throw new ArgumentNullException(nameof(data));
        }

        public Stream OpenRead(string relativeAssetPath)
        {
            string key = NormalizeKey(relativeAssetPath);
            if (!assets.TryGetValue(key, out byte[] bytes))
            {
                throw new FileNotFoundException("Preloaded web asset not found: " + key, key);
            }

            return new MemoryStream(bytes, writable: false);
        }

        private static string NormalizeKey(string path)
        {
            return (path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
        }
    }
}
