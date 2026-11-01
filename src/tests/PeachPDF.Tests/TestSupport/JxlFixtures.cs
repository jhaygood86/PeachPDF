using System.IO;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Loads the small JPEG XL files committed under <c>TestSupport/Jxl</c> (copied to the test output
    /// directory; PeachImage can decode JPEG XL but has no encoder, so they cannot be generated).
    /// </summary>
    internal static class JxlFixtures
    {
        public static byte[] Load(string name) =>
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestSupport", "Jxl", name + ".jxl"));

        public static string DataUri(string name) => "data:image/jxl;base64," + Convert.ToBase64String(Load(name));
    }
}
