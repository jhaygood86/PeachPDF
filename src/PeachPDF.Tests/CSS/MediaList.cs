namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System;
    using Xunit;

    public class CssMediaListTests : CssConstructionFunctions
    {
        [Fact]
        public void SimpleScreenMediaList()
        {
            var source = @"@media screen {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("screen", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(1, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void MediaListAtIllegal()
        {
            var source = @"@media @screen {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.Equal(RuleType.Media, sheet.Rules[0].Type);
            var media = sheet.Rules[0] as MediaRule;
            Assert.Equal("not all", media.ConditionText);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void MediaListInterrupted()
        {
            var source = @"@media screen; h1 { color: green }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var h1 = (StyleRule)sheet.Rules[0];
            Assert.Equal("h1", h1.SelectorText);
            var style = h1.Style;
            Assert.Equal("rgb(0, 128, 0)", style.Color);
        }

        [Fact]
        public void SimpleScreenTvMediaList()
        {
            var source = @"@media screen,tv {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("screen, tv", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(2, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void SimpleScreenTvSpacesMediaList()
        {
            var source = @"@media              screen ,          tv {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("screen, tv", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(2, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void OnlyScreenTvMediaList()
        {
            var source = @"@media only screen,tv {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("only screen, tv", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(2, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void NotScreenTvMediaList()
        {
            var source = @"@media not screen,tv {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("not screen, tv", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(2, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void FeatureMinWidthMediaList()
        {
            var source = @"@media (min-width:30px) {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("(min-width: 30px)", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(1, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void OnlyFeatureWidthMediaList()
        {
            var source = @"@media only (width: 640px) {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("only (width: 640px)", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(1, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void NotFeatureDeviceWidthMediaList()
        {
            var source = @"@media not (device-width: 640px) {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("not (device-width: 640px)", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(1, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void AllFeatureMaxWidthMediaListMissingAnd()
        {
            var source = @"@media all (max-width:30px) {
    h1 { color: red }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.Equal(RuleType.Media, sheet.Rules[0].Type);
            var media = sheet.Rules[0] as MediaRule;
            Assert.Equal("not all", media.ConditionText);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void NoMediaQueryGivenSkip()
        {
            var source = @"@media {
    h1 { color: red }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.Equal(RuleType.Media, sheet.Rules[0].Type);
            var media = sheet.Rules[0] as MediaRule;
            Assert.Equal("", media.ConditionText);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void NotNoMediaTypeOrExpressionSkip()
        {
            var source = @"@media not {
    h1 { color: red }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.Equal(RuleType.Media, sheet.Rules[0].Type);
            var media = sheet.Rules[0] as MediaRule;
            Assert.Equal("not all", media.ConditionText);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void OnlyNoMediaTypeOrExpressionSkip()
        {
            var source = @"@media only {
    h1 { color: red }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.Equal(RuleType.Media, sheet.Rules[0].Type);
            var media = sheet.Rules[0] as MediaRule;
            Assert.Equal("not all", media.ConditionText);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void MediaFeatureMissingSkip()
        {
            var source = @"@media () {
    h1 { color: red }
}";

            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.Equal(RuleType.Media, sheet.Rules[0].Type);
            var media = sheet.Rules[0] as MediaRule;
            Assert.Equal("not all", media.ConditionText);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void MediaFeatureMissingSkipReadNext()
        {
            var source = @"@media () {
    h1 { color: red }
}
h1 { color: green }";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(2, sheet.Rules.Length);
            Assert.Equal(RuleType.Media, sheet.Rules[0].Type);
            Assert.IsType<StyleRule>(sheet.Rules[1]);
            var style = (StyleRule)sheet.Rules[1];
            Assert.Equal("rgb(0, 128, 0)", style.Style.Color);
            Assert.Equal("h1", style.SelectorText);
        }

        [Fact]
        public void FeatureMaxWidthMediaListMissingConnectedAnd()
        {
            var source = @"@media (max-width:30px) (min-width:10px) {
    h1 { color: red }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.Equal(RuleType.Media, sheet.Rules[0].Type);
            var media = sheet.Rules[0] as MediaRule;
            Assert.Equal("not all", media.ConditionText);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void TvScreenMediaListMissingComma()
        {
            var source = @"@media tv screen {
    h1 { color: red }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.Equal(RuleType.Media, sheet.Rules[0].Type);
            var media = sheet.Rules[0] as MediaRule;
            Assert.Equal("not all", media.ConditionText);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void AllFeatureMaxWidthMediaListWithAndKeyword()
        {
            var source = @"@media all and (max-width:30px) {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("all and (max-width: 30px)", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(1, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void FeatureAspectRatioMediaList()
        {
            var source = @"@media (aspect-ratio: 16/9) {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("(aspect-ratio: 16/9)", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(1, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void PrintFeatureMaxWidthAndMinDeviceWidthMediaList()
        {
            var source = @"@media print and (max-width:30px) and (min-device-width:100px) {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("print and (max-width: 30px) and (min-device-width: 100px)", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(1, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void AllFeatureMinWidthAndMinDeviceWidthScreenMediaList()
        {
            var source = @"@media all and (min-width:0) and (min-device-width:100px), screen {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("all and (min-width: 0) and (min-device-width: 100px), screen", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(2, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void ImplicitAllFeatureResolutionMediaList()
        {
            var source = @"@media (resolution:72dpi) {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("(resolution: 72dpi)", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(1, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void ImplicitAllFeatureMinResolutionAndMaxResolutionMediaList()
        {
            var source = @"@media (min-resolution:72dpi) and (max-resolution:140dpi) {
    h1 { color: green }
}";
            var sheet = ParseStyleSheet(source);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = (MediaRule)sheet.Rules[0];
            Assert.Equal("(min-resolution: 72dpi) and (max-resolution: 140dpi)", media.Media.MediaText);
            var list = media.Media;
            Assert.Equal(1, list.Length);
            Assert.Equal(1, media.Rules.Length);
        }

        [Fact]
        public void CssMediaListApiWithAppendDeleteAndTextShouldWork()
        {
            var media = new[] { "handheld", "screen", "only screen and (max-device-width: 480px)" };
            var p = new StylesheetParser();
            var m = new MediaList(p);
            Assert.Equal(0, m.Length);

            m.Add(media[0]);
            m.Add(media[1]);
            m.Add(media[2]);

            m.Remove(media[1]);

            Assert.Equal(2, m.Length);
            Assert.Equal(media[0], m[0]);
            Assert.Equal(media[2], m[1]);
            Assert.Equal(String.Concat(media[0], ", ", media[2]), m.MediaText);
        }

        [Fact]
        public void CombinedConditionMediaQueriesLevel4()
        {
            const string source = @"/* Traditionelle Syntax */
@media (min-height: 500px) and (max-height: 800px) {
  /* Styles */
  h1 { color: rgb(255, 0, 0); }
}

/* Mit Vergleichsoperatoren */
@media (height >= 500px) and (height <= 800px) {
  /* Gleiche Styles */
  h1 { color: rgb(255, 0, 0); }
}";
            var result = ParseStyleSheet(source);
            Assert.Equal(source, result.StylesheetText.Text);
            Assert.Equal(2, result.Rules.Length);
            var rule1 = result.Rules[0] as MediaRule;
            var rule2 = result.Rules[1] as MediaRule;
            Assert.NotNull(rule1);
            Assert.NotNull(rule2);
            Assert.Equal("(min-height: 500px) and (max-height: 800px)", rule1.ConditionText);
            Assert.Equal("(height >= 500px) and (height <= 800px)", rule2.ConditionText);
            Assert.Equal("@media (min-height: 500px) and (max-height: 800px) { h1 { color: rgb(255, 0, 0) } }", rule1.ToCss());
            Assert.Equal("@media (height >= 500px) and (height <= 800px) { h1 { color: rgb(255, 0, 0) } }", rule2.ToCss());
        }
    }
}







