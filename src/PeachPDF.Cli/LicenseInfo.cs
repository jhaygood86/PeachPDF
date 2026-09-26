using System.Reflection;

namespace PeachPDF.Cli;

/// <summary>
/// Reads the license and third-party acknowledgement text embedded into the CLI assembly (from the
/// repository's <c>LICENSE</c> and the two <c>THIRD-PARTY-LICENSES.md</c> files), so a self-contained binary can
/// print them for <c>--show-license</c> and <c>--credits</c>.
/// </summary>
internal static class LicenseInfo
{
    private const string LicenseResource = "PeachPDF.Cli.LICENSE";
    private const string ThirdPartyResource = "PeachPDF.Cli.THIRD-PARTY-LICENSES.md";
    private const string TextEngineThirdPartyResource = "PeachPDF.Cli.PeachDrawing.Text.THIRD-PARTY-LICENSES.md";
    private const string FreeTypeLicenseResource = "PeachPDF.Cli.FTL.TXT";

    /// <summary>The BSD license text (for <c>--show-license</c>).</summary>
    public static string License => ReadResource(LicenseResource);

    /// <summary>
    /// The license text followed by the third-party acknowledgements (for <c>--credits</c>): PeachPDF's own, then the
    /// text engine's, since a self-contained binary contains both, and last the FreeType Project License in full, which
    /// the binary carries because the text engine's TrueType interpreter is ported from FreeType.
    /// </summary>
    public static string Credits =>
        $"{ReadResource(LicenseResource)}{Environment.NewLine}{Environment.NewLine}{ReadResource(ThirdPartyResource)}" +
        $"{Environment.NewLine}{Environment.NewLine}{ReadResource(TextEngineThirdPartyResource)}" +
        $"{Environment.NewLine}{Environment.NewLine}{ReadResource(FreeTypeLicenseResource)}";

    private static string ReadResource(string name)
    {
        var assembly = typeof(LicenseInfo).Assembly;
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return $"(embedded resource '{name}' not found)";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
