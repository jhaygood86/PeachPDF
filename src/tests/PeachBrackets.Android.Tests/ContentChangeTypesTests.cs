using PeachBrackets.Accessibility;
using PeachBrackets.Android.Logic;

namespace PeachBrackets.Android.Tests;

public class ContentChangeTypesTests
{
    [Fact]
    public void APartThatCameOrWentChangesTheSubtree()
    {
        Assert.Equal(1, AccessibilityMapping.ContentChangeTypes([AccessibilityChangeKind.Added]));
        Assert.Equal(1, AccessibilityMapping.ContentChangeTypes([AccessibilityChangeKind.Removed]));
    }

    [Fact]
    public void ANameOrDescriptionChangesTheText()
    {
        Assert.Equal(2, AccessibilityMapping.ContentChangeTypes([AccessibilityChangeKind.NameChanged]));
        Assert.Equal(2, AccessibilityMapping.ContentChangeTypes([AccessibilityChangeKind.DescriptionChanged]));
    }

    [Fact]
    public void AStateOrANumberChangesTheStateDescription()
    {
        Assert.Equal(64, AccessibilityMapping.ContentChangeTypes([AccessibilityChangeKind.StateChanged]));
        Assert.Equal(64, AccessibilityMapping.ContentChangeTypes([AccessibilityChangeKind.ValueChanged]));
    }

    [Fact]
    public void SeveralKindsAreCombinedAndNothingIsNothing()
    {
        Assert.Equal(1 | 2 | 64, AccessibilityMapping.ContentChangeTypes(
            [AccessibilityChangeKind.Added, AccessibilityChangeKind.NameChanged, AccessibilityChangeKind.StateChanged, AccessibilityChangeKind.Added]));
        Assert.Equal(0, AccessibilityMapping.ContentChangeTypes([]));
    }
}
