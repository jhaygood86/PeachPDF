using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS;

public class ClassSelectorTests
{
    [Fact]
    public async Task FindAllClassSelectorsThatMatchClassName()
    {
        // Arrange
        var css =
            @".sample-class { background-color: #101010 } .sample-class[type='input'] { background-color: #121212 }";
        var sheet = await new StylesheetParser().ParseAsync(css);

        // Act
        var list = sheet.StyleRules
            .Where(x =>
                (x.Selector is CompoundSelector selector &&
                 selector.Any(y => y is ClassSelector { Class: "sample-class" }))
                || x.Selector is ClassSelector { Class: "sample-class" }
            );

        // Assert
        Assert.Equal(2, list.Count());
    }
}






