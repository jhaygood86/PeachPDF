using System.Reflection;

namespace PeachPDF.Cli;

/// <summary>
/// Reads the license and third-party acknowledgement text embedded into the CLI assembly (from the
/// repository's <c>LICENSE</c> and <c>THIRD-PARTY-LICENSES.md</c>), so a self-contained binary can
/// print them for <c>--show-license</c> and <c>--credits</c>.
/// </summary>
internal static class LicenseInfo
{
    private const string LicenseResource = "PeachPDF.Cli.LICENSE";
    private const string ThirdPartyResource = "PeachPDF.Cli.THIRD-PARTY-LICENSES.md";

    /// <summary>The BSD license text (for <c>--show-license</c>).</summary>
    public static string License => ReadResource(LicenseResource);

    /// <summary>The license text followed by the third-party acknowledgements (for <c>--credits</c>).</summary>
    public static string Credits =>
        $"{ReadResource(LicenseResource)}{Environment.NewLine}{Environment.NewLine}{ReadResource(ThirdPartyResource)}";

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
