using System.Text;
using PeachBrackets.Css;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Engine.Tests.CSS
{
    /// <summary>
    /// Pins the CSS-OM face of every longhand <see cref="PropertyFactory"/> registers: its flags
    /// (inherited / animatable / hashless / unitless) and how its converter answers the CSS-wide keywords,
    /// <c>var()</c> and a fixed probe set of values. The snapshot (<c>CssomLonghandBaseline.txt</c>) was captured
    /// from the hand-written property classes, and the classes generated from <c>css-properties.json</c> must keep
    /// it byte-identical; a row changes only for a deliberate spec fix, in the same commit as its test.
    /// Regenerate with <c>PEACH_UPDATE_BASELINE=1</c> and review the diff.
    /// </summary>
    public class CssomLonghandBaselineTests
    {
        private static readonly string[] Probes =
        [
            "initial", "inherit", "unset", "revert", "revert-layer", "var(--x)", "auto", "none", "normal",
            "0", "1", "1px", "50%", "red", "currentcolor", "solid", "bogus", "1px 2px", "10px / 2"
        ];

        private static IEnumerable<string> LonghandNames() =>
            typeof(PropertyNames).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Select(f => (string)f.GetValue(null)!)
                .Concat(CssPropertyRegistry.InitialValues.Keys)
                .Distinct(StringComparer.Ordinal)
                .Where(n => PropertyFactory.Instance.CreateLonghand(n) is not null)
                .OrderBy(n => n, StringComparer.Ordinal);

        private static string Describe(string name)
        {
            var parser = new StylesheetParser();
            var probe = PropertyFactory.Instance.CreateLonghand(name)!;
            var sb = new StringBuilder();
            sb.Append(name).Append(" | ").Append(probe.GetType().Name)
              .Append(" | inh=").Append(probe.CanBeInherited ? 1 : 0)
              .Append(" anim=").Append(probe.IsAnimatable ? 1 : 0)
              .Append(" hash=").Append(probe.CanBeHashless ? 1 : 0)
              .Append(" unit=").Append(probe.CanBeUnitless ? 1 : 0)
              .Append(" var=").Append(probe.AllowsVarSubstitution ? 1 : 0)
              .Append(" fact=").Append(PropertyFactory.Instance.IsAnimatable(name) ? 1 : 0);

            foreach (var text in Probes)
            {
                var property = PropertyFactory.Instance.CreateLonghand(name)!;
                var tokens = parser.ParseValue(text);
                var ok = tokens is not null && property.TrySetValue(tokens);
                sb.Append(" | ").Append(text).Append('=').Append(ok ? property.Value : "X");
            }

            return sb.ToString();
        }

        [Fact]
        public void EveryLonghand_MatchesTheBaselineSnapshot()
        {
            var actual = string.Join("\n", LonghandNames().Select(Describe)) + "\n";
            var path = Path.Combine(AppContext.BaseDirectory, "CSS", "CssomLonghandBaseline.txt");
            var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "CSS", "CssomLonghandBaseline.txt"));

            if (Environment.GetEnvironmentVariable("PEACH_UPDATE_BASELINE") == "1")
            {
                File.WriteAllText(source, actual, new UTF8Encoding(false));
                return;
            }

            var expected = File.ReadAllText(path).Replace("\r\n", "\n");
            var expectedLines = expected.TrimEnd('\n').Split('\n');
            var actualLines = actual.TrimEnd('\n').Split('\n');
            var diffs = actualLines.Except(expectedLines).Concat(expectedLines.Except(actualLines)).ToList();
            Assert.True(diffs.Count == 0, "CSS-OM baseline drift:\n" + string.Join("\n", diffs.Take(20)));
        }
    }
}
