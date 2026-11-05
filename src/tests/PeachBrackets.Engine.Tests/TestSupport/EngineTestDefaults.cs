using System.Runtime.CompilerServices;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Engine.Tests.TestSupport
{
    /// <summary>
    /// Keeps the layout fixtures on the default font they were written against. On the Apple platforms the product's default font is the
    /// system font, San Francisco, a variable font with no separate bold face and no Hebrew, whose widths differ from Arial's; a fixture
    /// whose geometry, wrapping or hyphen is calibrated to Arial would then test the host instead of the engine. Where Arial is
    /// installed the default for these tests is Arial, as it was before the system font became the default. What the default is on each
    /// platform is tested directly, through <c>DefaultFontResolver.DetermineDefaultFont</c>.
    /// </summary>
    internal static class EngineTestDefaults
    {
        [ModuleInitializer]
        internal static void KeepArialAsTheDefault()
        {
            if ((OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS())
                && PeachDrawing.Text.FontSet.InstalledFamilyNames.Contains("Arial", StringComparer.OrdinalIgnoreCase))
            {
                DefaultFontResolver.DefaultFont = "Arial";
            }
        }
    }
}
