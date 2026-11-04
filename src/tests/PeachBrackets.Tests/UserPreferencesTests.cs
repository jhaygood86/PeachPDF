using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class UserPreferencesTests
{
    // Each query turns the box's cursor to a distinct value when it matches, so the cursor read back says which queries matched.
    private const string Styles = """
        <style>
        div { height: 20px }
        @media (prefers-reduced-motion: reduce) { #motion { cursor: wait } }
        @media (prefers-reduced-motion: no-preference) { #motion { cursor: help } }
        @media (prefers-contrast: more) { #contrast { cursor: zoom-in } }
        @media (prefers-contrast: less) { #contrast { cursor: zoom-out } }
        @media (prefers-contrast: custom) { #contrast { cursor: move } }
        @media (prefers-contrast: no-preference) { #contrast { cursor: crosshair } }
        @media (forced-colors: active) { #forced { cursor: grab } }
        @media (forced-colors: none) { #forced { cursor: grabbing } }
        @media (inverted-colors: inverted) { #inverted { cursor: copy } }
        @media (inverted-colors: none) { #inverted { cursor: alias } }
        @media (prefers-reduced-transparency: reduce) { #transparency { cursor: not-allowed } }
        @media (prefers-reduced-transparency: no-preference) { #transparency { cursor: text } }
        @media (prefers-reduced-data: reduce) { #data { cursor: progress } }
        @media (prefers-reduced-data: no-preference) { #data { cursor: pointer } }
        @media (prefers-contrast) { #boolean { cursor: cell } }
        </style>
        """;

    private const string Body = "<div id=motion></div><div id=contrast></div><div id=forced></div><div id=inverted></div><div id=transparency></div><div id=data></div><div id=boolean></div>";

    private static async Task<IReadOnlyDictionary<string, CursorShape>> CursorsAsync(UserPreferences? preferences)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Body, Styles), new HtmlDocumentOptions { Preferences = preferences, MediaType = MediaType.Screen });
        var result = new Dictionary<string, CursorShape>();
        foreach (var id in new[] { "motion", "contrast", "forced", "inverted", "transparency", "data", "boolean" })
        {
            var bounds = document.GetElementBounds(id)!.Value;
            result[id] = document.GetCursorAt(0, bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2)).Shape;
        }

        return result;
    }

    [Fact]
    public async Task ByDefaultTheDocumentReportsTheSettingsOfAStaticPage()
    {
        var cursors = await CursorsAsync(null);

        Assert.Equal(CursorShape.Wait, cursors["motion"]);
        Assert.Equal(CursorShape.Crosshair, cursors["contrast"]);
        Assert.Equal(CursorShape.Grabbing, cursors["forced"]);
        Assert.Equal(CursorShape.Alias, cursors["inverted"]);
        Assert.Equal(CursorShape.Text, cursors["transparency"]);
        Assert.Equal(CursorShape.Pointer, cursors["data"]);
        Assert.Equal(CursorShape.Default, cursors["boolean"]);
    }

    [Fact]
    public async Task EverySettingIsReportedWhenTheHostAsksForIt()
    {
        var cursors = await CursorsAsync(new UserPreferences
        {
            ReducedMotion = false,
            Contrast = ContrastPreference.More,
            ForcedColors = true,
            InvertedColors = true,
            ReducedTransparency = true,
            ReducedData = true,
        });

        Assert.Equal(CursorShape.Help, cursors["motion"]);
        Assert.Equal(CursorShape.ZoomIn, cursors["contrast"]);
        Assert.Equal(CursorShape.Grab, cursors["forced"]);
        Assert.Equal(CursorShape.Copy, cursors["inverted"]);
        Assert.Equal(CursorShape.NotAllowed, cursors["transparency"]);
        Assert.Equal(CursorShape.Progress, cursors["data"]);
        Assert.Equal(CursorShape.Cell, cursors["boolean"]);
    }

    [Theory]
    [InlineData(ContrastPreference.Less, CursorShape.ZoomOut)]
    [InlineData(ContrastPreference.Custom, CursorShape.Move)]
    [InlineData(ContrastPreference.NoPreference, CursorShape.Crosshair)]
    public async Task ContrastHasFourValues(ContrastPreference contrast, CursorShape expected)
    {
        var cursors = await CursorsAsync(new UserPreferences { Contrast = contrast });

        Assert.Equal(expected, cursors["contrast"]);
    }

    private const string SpacingBody = "<p id=p>one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty</p><p id=q>after</p>";

    private const string SpacingStyles = "<style>p { font: 12pt serif; line-height: 1; letter-spacing: 0; margin: 0 }</style>";

    private static async Task<(double Height, double NextTop)> MeasureAsync(TextSpacingOverrides? spacing)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(SpacingBody, SpacingStyles), new HtmlDocumentOptions { TextSpacing = spacing, MediaType = MediaType.Screen });
        var p = document.GetElementBounds("p")!.Value;
        var q = document.GetElementBounds("q")!.Value;
        return (p.Height, q.Y);
    }

    [Fact]
    public async Task TextSpacingIsLeftAloneUnlessAsked()
    {
        var (height, _) = await MeasureAsync(null);
        var (again, _) = await MeasureAsync(null);

        Assert.Equal(height, again);
    }

    [Fact]
    public async Task TextSpacingWinsOverTheDocumentsOwnRules()
    {
        var (plainHeight, plainNext) = await MeasureAsync(null);
        var (spacedHeight, spacedNext) = await MeasureAsync(TextSpacingOverrides.Wcag);

        // Line height 1 becomes 1.5, so the same lines are half as tall again, and the second paragraph starts two font sizes lower.
        Assert.True(spacedHeight >= plainHeight * 1.5 - 0.01, $"{spacedHeight} should be at least 1.5 times {plainHeight}");
        Assert.True(spacedNext - spacedHeight > plainNext - plainHeight + 20, "paragraph spacing should add about two font sizes");
    }

    [Fact]
    public void TheWcagMinimumsAreTheOnesTheCriterionNames()
    {
        var wcag = TextSpacingOverrides.Wcag;

        Assert.Equal((1.5, 2, 0.12, 0.16), (wcag.LineHeight, wcag.ParagraphSpacing, wcag.LetterSpacing, wcag.WordSpacing));
    }
}
