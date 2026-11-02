using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>The form-state pseudo-classes, asked of the box tree directly: each element kind and each way an element can be disabled or editable.</summary>
    public class FormStateSelectorsTests
    {
        private static async Task<CssBox> BoxAsync(string html, string id)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            return LayoutHarness.FindById(root, id) ?? throw new InvalidOperationException("no element " + id);
        }

        private static async Task<bool> MatchesAsync(string html, string id, string pseudoClass) =>
            FormStateSelectors.Matches(pseudoClass, await BoxAsync(html, id));

        [Fact]
        public void OnlyTheSevenNamesAreHandled()
        {
            foreach (var name in new[] { PseudoClassNames.Checked, PseudoClassNames.Disabled, PseudoClassNames.Enabled, PseudoClassNames.Required, PseudoClassNames.Optional, PseudoClassNames.ReadOnly, PseudoClassNames.ReadWrite })
                Assert.True(FormStateSelectors.Handles(name));

            Assert.False(FormStateSelectors.Handles(PseudoClassNames.Valid));
            Assert.False(FormStateSelectors.Handles(PseudoClassNames.Hover));
        }

        [Theory]
        [InlineData("<input id=x type=checkbox checked>", true)]
        [InlineData("<input id=x type=radio checked>", true)]
        [InlineData("<input id=x type=checkbox>", false)]
        [InlineData("<input id=x type=text checked>", false)]
        [InlineData("<select><option id=x selected>a</option></select>", true)]
        [InlineData("<select><option id=x>a</option></select>", false)]
        [InlineData("<p id=x checked>t</p>", false)]
        public async Task CheckedIsACheckedBoxOrRadioOrASelectedOption(string html, bool expected) =>
            Assert.Equal(expected, await MatchesAsync(html, "x", PseudoClassNames.Checked));

        [Theory]
        [InlineData("<button id=x disabled>b</button>", true)]
        [InlineData("<button id=x>b</button>", false)]
        [InlineData("<textarea id=x disabled></textarea>", true)]
        [InlineData("<fieldset id=x disabled></fieldset>", true)]
        [InlineData("<fieldset disabled><input id=x></fieldset>", true)]
        [InlineData("<fieldset disabled><div><input id=x></div></fieldset>", true)]
        [InlineData("<fieldset disabled><legend><input id=x></legend></fieldset>", false)]
        [InlineData("<fieldset disabled><legend>first</legend><legend><input id=x></legend></fieldset>", true)]
        [InlineData("<fieldset disabled><fieldset><input id=x></fieldset></fieldset>", true)]
        [InlineData("<fieldset><input id=x></fieldset>", false)]
        [InlineData("<select><optgroup disabled><option id=x>a</option></optgroup></select>", true)]
        [InlineData("<select><optgroup><option id=x>a</option></optgroup></select>", false)]
        [InlineData("<select><option id=x disabled>a</option></select>", true)]
        [InlineData("<select><optgroup id=x disabled></optgroup></select>", true)]
        public async Task DisabledFollowsTheControlItsFieldsetAndItsGroup(string html, bool disabled)
        {
            Assert.Equal(disabled, await MatchesAsync(html, "x", PseudoClassNames.Disabled));
            Assert.Equal(!disabled, await MatchesAsync(html, "x", PseudoClassNames.Enabled));
        }

        [Fact]
        public async Task ElementsThatCannotBeDisabledAreNeitherDisabledNorEnabled()
        {
            Assert.False(await MatchesAsync("<p id=x disabled>t</p>", "x", PseudoClassNames.Disabled));
            Assert.False(await MatchesAsync("<p id=x disabled>t</p>", "x", PseudoClassNames.Enabled));
        }

        [Theory]
        [InlineData("<input id=x required>", true, false)]
        [InlineData("<input id=x>", false, true)]
        [InlineData("<select id=x required></select>", true, false)]
        [InlineData("<textarea id=x></textarea>", false, true)]
        [InlineData("<input id=x type=checkbox required>", true, false)]
        [InlineData("<input id=x type=hidden required>", false, false)]
        [InlineData("<input id=x type=submit required>", false, false)]
        [InlineData("<input id=x type=range required>", false, false)]
        [InlineData("<p id=x required>t</p>", false, false)]
        public async Task RequiredAndOptionalApplyOnlyToTheControlsThatCanBeRequired(string html, bool required, bool optional)
        {
            Assert.Equal(required, await MatchesAsync(html, "x", PseudoClassNames.Required));
            Assert.Equal(optional, await MatchesAsync(html, "x", PseudoClassNames.Optional));
        }

        [Theory]
        [InlineData("<input id=x>", true)]
        [InlineData("<input id=x type=email>", true)]
        [InlineData("<input id=x type=date>", true)]
        [InlineData("<input id=x readonly>", false)]
        [InlineData("<input id=x disabled>", false)]
        [InlineData("<input id=x type=checkbox>", false)]
        [InlineData("<input id=x type=button>", false)]
        [InlineData("<textarea id=x></textarea>", true)]
        [InlineData("<textarea id=x readonly></textarea>", false)]
        [InlineData("<textarea id=x disabled></textarea>", false)]
        [InlineData("<p id=x contenteditable>t</p>", true)]
        [InlineData("<p id=x contenteditable=true>t</p>", true)]
        [InlineData("<p id=x contenteditable=false>t</p>", false)]
        [InlineData("<p id=x>t</p>", false)]
        public async Task ReadWriteIsWhatTheUserCanEditAndReadOnlyIsTheRest(string html, bool readWrite)
        {
            Assert.Equal(readWrite, await MatchesAsync(html, "x", PseudoClassNames.ReadWrite));
            Assert.Equal(!readWrite, await MatchesAsync(html, "x", PseudoClassNames.ReadOnly));
        }

        [Fact]
        public void ABoxWithNoElementMatchesNothing()
        {
            var anonymous = new CssBox(null, null);

            Assert.False(FormStateSelectors.Matches(PseudoClassNames.Checked, anonymous));
            Assert.False(FormStateSelectors.Matches(PseudoClassNames.ReadOnly, anonymous));
        }

        [Fact]
        public async Task ANameThatIsNotHandledMatchesNothing() =>
            Assert.False(FormStateSelectors.Matches(PseudoClassNames.Valid, await BoxAsync("<input id=x>", "x")));
    }
}
