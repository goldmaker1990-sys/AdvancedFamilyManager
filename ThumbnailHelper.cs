using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AdvancedFamilyManager
{
    public static class ThumbnailHelper
    {
        private static readonly Dictionary<string, (DateTime Timestamp, ImageSource? Thumbnail)> _cache
            = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _cacheLock = new();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [In][MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            [In] IntPtr pbc,
            [In][MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out][MarshalAs(UnmanagedType.Interface, IidParameterIndex = 2)] out IShellItemImageFactory ppv);

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage([In, MarshalAs(UnmanagedType.Struct)] SIZE size, [In] int flags, [Out] out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject([In] IntPtr hObject);

        /// <summary>
        /// Returns a frozen <see cref="ImageSource"/> for the given .rfa file.
        /// Results are cached in-memory keyed by full path + last-write timestamp,
        /// so this method is safe and cheap to call repeatedly (including off the UI thread).
        /// </summary>
        public static ImageSource? GetThumbnail(string filePath, int width = 128, int height = 128)
        {
            if (string.IsNullOrEmpty(filePath)) return null;

            DateTime lastWrite;
            try { lastWrite = File.GetLastWriteTimeUtc(filePath); }
            catch { return null; }

            lock (_cacheLock)
            {
                if (_cache.TryGetValue(filePath, out var cached) && cached.Timestamp == lastWrite)
                    return cached.Thumbnail;
            }

            ImageSource? result = LoadThumbnailCore(filePath, width, height);

            lock (_cacheLock)
            {
                _cache[filePath] = (lastWrite, result);
            }

            return result;
        }

        private static ImageSource? LoadThumbnailCore(string filePath, int width, int height)
        {
            try
            {
                Guid shellItem2Guid = new Guid("7E9FB0D3-919F-4307-AB2E-9B1860310C93");
                SHCreateItemFromParsingName(filePath, IntPtr.Zero, shellItem2Guid, out IShellItemImageFactory factory);

                SIZE size = new SIZE { cx = width, cy = height };
                int hr = factory.GetImage(size, 0x0, out IntPtr hBitmap);

                if (hr == 0 && hBitmap != IntPtr.Zero)
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    DeleteObject(hBitmap);
                    source.Freeze();
                    return source;
                }
            }
            catch
            {
                // Windows hasn't cached a preview or shell extraction failed
            }
            return null;
        }
    }
}