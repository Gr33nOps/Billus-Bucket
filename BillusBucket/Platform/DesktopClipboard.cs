#if !BLAZORGL
using System;
using System.Runtime.InteropServices;

namespace BillusBucket
{
    internal static class DesktopClipboard
    {
        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SDL_HasClipboardText();

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetClipboardText();

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_free(IntPtr mem);

        public static bool TryGetText(out string text)
        {
            text = null;
            try
            {
                if (SDL_HasClipboardText() == 0)
                {
                    return false;
                }

                IntPtr ptr = SDL_GetClipboardText();
                if (ptr == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    text = Marshal.PtrToStringUTF8(ptr) ?? Marshal.PtrToStringAnsi(ptr);
                    return !string.IsNullOrEmpty(text);
                }
                finally
                {
                    SDL_free(ptr);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
