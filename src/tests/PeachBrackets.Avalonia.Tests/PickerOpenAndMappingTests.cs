using Avalonia.Automation.Peers;
using Avalonia.Controls;
using PeachBrackets.Accessibility;
using PeachBrackets.Avalonia.Logic;
using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class PickerOpenTests
{
    private static string Field(string type) =>
        $"<input id='f' type='{type}' style='position:absolute;left:10px;top:10px;width:160px;height:24px'>";

    [Theory]
    [InlineData("time")]
    [InlineData("color")]
    [InlineData("datetime-local")]
    public async Task AClickOnAFieldWithAPickerOpensItsFlyout(string type)
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Field(type), 400, 300);
            await HtmlViewInput.ClickAsync(window, view, "f");
            Assert.True(view.HasOpenFlyout, $"the {type} picker should be open");
            Assert.IsType<Flyout>(view.OpenFlyout);

            // Another click elsewhere on the page closes it again.
            view.OpenFlyout!.Hide();
            await HtmlViewInput.SettleAsync(view);
            Assert.False(view.HasOpenFlyout);
            window.Close();
        });
    }

    [Fact]
    public async Task AClickOnAFileFieldAsksTheStorageProviderAndANoopProviderChangesNothing()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Field("file"), 400, 300);
            var failures = 0;
            view.DocumentFailed += (_, _) => failures++;
            await HtmlViewInput.ClickAsync(window, view, "f");
            Assert.False(view.HasOpenFlyout);
            Assert.Equal(0, failures);
            window.Close();
        });
    }

    [Theory]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.JPEG", "image/jpeg")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.svg", "image/svg+xml")]
    [InlineData("a.pdf", "application/pdf")]
    [InlineData("a.txt", "text/plain")]
    [InlineData("a.html", "text/html")]
    [InlineData("a.htm", "text/html")]
    [InlineData("a.css", "text/css")]
    [InlineData("a.json", "application/json")]
    [InlineData("a.csv", "text/csv")]
    [InlineData("a.xml", "application/xml")]
    [InlineData("a.zip", "application/zip")]
    public void AChosenFileIsSubmittedAsTheTypeItsExtensionGives(string name, string expected) =>
        Assert.Equal(expected, HtmlView.ContentTypeOf(name, string.Empty));

    [Fact]
    public void AnAcceptWildcardIsNotAMediaTypeToSubmitAs() =>
        Assert.Equal("application/octet-stream", HtmlView.ContentTypeOf("a.bin", "image/*"));
}

public class AccessibilityRoleMappingTests
{
    [Theory]
    [InlineData(AccessibilityRole.Document, AutomationControlType.Document)]
    [InlineData(AccessibilityRole.Option, AutomationControlType.ListItem)]
    [InlineData(AccessibilityRole.ListBox, AutomationControlType.List)]
    [InlineData(AccessibilityRole.Switch, AutomationControlType.CheckBox)]
    [InlineData(AccessibilityRole.MenuItemRadio, AutomationControlType.RadioButton)]
    [InlineData(AccessibilityRole.SearchBox, AutomationControlType.Edit)]
    [InlineData(AccessibilityRole.ComboBox, AutomationControlType.ComboBox)]
    [InlineData(AccessibilityRole.ScrollBar, AutomationControlType.ScrollBar)]
    [InlineData(AccessibilityRole.SpinButton, AutomationControlType.Spinner)]
    [InlineData(AccessibilityRole.Meter, AutomationControlType.ProgressBar)]
    [InlineData(AccessibilityRole.Image, AutomationControlType.Image)]
    [InlineData(AccessibilityRole.Grid, AutomationControlType.DataGrid)]
    [InlineData(AccessibilityRole.Row, AutomationControlType.DataItem)]
    [InlineData(AccessibilityRole.ColumnHeader, AutomationControlType.HeaderItem)]
    [InlineData(AccessibilityRole.Separator, AutomationControlType.Separator)]
    [InlineData(AccessibilityRole.Tab, AutomationControlType.TabItem)]
    [InlineData(AccessibilityRole.TabList, AutomationControlType.Tab)]
    [InlineData(AccessibilityRole.Menu, AutomationControlType.Menu)]
    [InlineData(AccessibilityRole.MenuBar, AutomationControlType.MenuBar)]
    [InlineData(AccessibilityRole.MenuItem, AutomationControlType.MenuItem)]
    [InlineData(AccessibilityRole.ToolBar, AutomationControlType.ToolBar)]
    [InlineData(AccessibilityRole.ToolTip, AutomationControlType.ToolTip)]
    [InlineData(AccessibilityRole.Tree, AutomationControlType.Tree)]
    [InlineData(AccessibilityRole.TreeItem, AutomationControlType.TreeItem)]
    [InlineData(AccessibilityRole.Paragraph, AutomationControlType.Text)]
    [InlineData(AccessibilityRole.BlockQuote, AutomationControlType.Text)]
    public void EveryKindOfPartIsToldAsTheControlTypeAReaderExpects(AccessibilityRole role, AutomationControlType expected) =>
        Assert.Equal(expected, AccessibilityMapping.ToControlType(role));

    [Theory]
    [InlineData(AccessibilityRole.Main, AutomationLandmarkType.Main)]
    [InlineData(AccessibilityRole.Search, AutomationLandmarkType.Search)]
    [InlineData(AccessibilityRole.Form, AutomationLandmarkType.Form)]
    [InlineData(AccessibilityRole.ContentInfo, AutomationLandmarkType.ContentInfo)]
    [InlineData(AccessibilityRole.Complementary, AutomationLandmarkType.Complementary)]
    [InlineData(AccessibilityRole.Region, AutomationLandmarkType.Region)]
    public void ALandmarkRoleHasItsLandmarkType(AccessibilityRole role, AutomationLandmarkType expected) =>
        Assert.Equal(expected, AccessibilityMapping.ToLandmarkType(role));

    [Fact]
    public void NoRoleIsLeftWithoutAControlType()
    {
        foreach (var role in Enum.GetValues<AccessibilityRole>())
            Assert.True(Enum.IsDefined(AccessibilityMapping.ToControlType(role)), role.ToString());
    }

    [Fact]
    public void AnExpandedFlagIsOpenClosedOrNotApplicable()
    {
        Assert.Equal(global::Avalonia.Automation.ExpandCollapseState.Expanded, AccessibilityMapping.ToExpandCollapseState(true));
        Assert.Equal(global::Avalonia.Automation.ExpandCollapseState.Collapsed, AccessibilityMapping.ToExpandCollapseState(false));
        Assert.Equal(global::Avalonia.Automation.ExpandCollapseState.LeafNode, AccessibilityMapping.ToExpandCollapseState(null));
    }
}
