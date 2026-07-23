using System;
using System.IO;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Small (16x16), committed sample raster images in every format this project's native image codec
    /// work needs to exercise - JPEG, PNG, GIF, WebP, and AVIF. Each was generated from the same source
    /// image (a gradient with partial transparency) so decoding is comparable across formats.
    /// </summary>
    internal static class BundledImages
    {
        private static string ResolvePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Assets", "Images", fileName);

        internal static string Jpg => ResolvePath("sample.jpg");

        internal static string Png => ResolvePath("sample.png");

        internal static string Gif => ResolvePath("sample.gif");

        internal static string WebP => ResolvePath("sample.webp");

        internal static string Avif => ResolvePath("sample.avif");
    }
}
