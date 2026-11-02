using PeachBrackets.Accessibility;

namespace PeachBrackets.Tests;

public class AccessibilitySvgMathTests
{
    private static async Task<AccessibilityTree> TreeAsync(string body)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body), new HtmlDocumentOptions { Media = "screen" });
        return document.GetAccessibilityTree();
    }

    private static async Task<AccessibilityNode> MathAsync(string markup)
    {
        var tree = await TreeAsync($"<math>{markup}</math>");
        return Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.Math);
    }

    // ---- SVG -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnSvgIsNamedByItsTitleAndDescribedByItsDesc()
    {
        var tree = await TreeAsync(
            "<svg width=40 height=40 viewBox='0 0 40 40'><title>  A red\n square </title><desc>It sits in the middle</desc><rect x=5 y=5 width=30 height=30 fill=red /></svg>");

        var svg = Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.GraphicsDocument);
        Assert.Equal("A red square", svg.Name);
        Assert.Equal("It sits in the middle", svg.Description);
    }

    [Fact]
    public async Task AnAriaLabelBeatsTheTitle_AndAnExplicitRoleIsUsed()
    {
        var tree = await TreeAsync(
            "<svg role='img' aria-label='Label wins' width=20 height=20><title>Title loses</title></svg>"
            + "<svg width=20 height=20><title>Only a title</title></svg>");

        var svgs = tree.Nodes.Where(n => n.Role is AccessibilityRole.Image or AccessibilityRole.GraphicsDocument).ToArray();
        Assert.Equal([AccessibilityRole.Image, AccessibilityRole.GraphicsDocument], svgs.Select(s => s.Role).ToArray());
        Assert.Equal(["Label wins", "Only a title"], svgs.Select(s => s.Name).ToArray());
    }

    [Fact]
    public async Task AnSvgWithNoTitleHasNoName_AndOneMarkedHiddenIsNotThere()
    {
        var tree = await TreeAsync("<svg width=20 height=20><rect width=5 height=5 /></svg><svg aria-hidden='true' width=20 height=20><title>hidden</title></svg>");

        var svg = Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.GraphicsDocument);
        Assert.Equal(string.Empty, svg.Name);
        Assert.Equal(string.Empty, svg.Description);
    }

    [Fact]
    public async Task AnSvgCanBeNamedByAnotherElement()
    {
        var tree = await TreeAsync("<p id=cap>Quarterly sales</p><svg aria-labelledby='cap' width=20 height=20><title>ignored</title></svg>");

        Assert.Equal("Quarterly sales", Assert.Single(tree.Nodes, n => n.Role == AccessibilityRole.GraphicsDocument).Name);
    }

    // ---- MathML --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AFormulaIsNamedByWhatTheAuthorSaid_ThenByWhatItSays()
    {
        var tree = await TreeAsync(
            "<math alttext='Alt text'><mi>x</mi></math>"
            + "<math aria-label='Aria label' alttext='ignored'><mi>y</mi></math>"
            + "<math><mi>z</mi></math>");

        Assert.Equal(["Alt text", "Aria label", "z"], tree.Nodes.Where(n => n.Role == AccessibilityRole.Math).Select(n => n.Name).ToArray());
    }

    [Theory]
    [InlineData("<msup><mi>x</mi><mn>2</mn></msup><mo>+</mo><mn>1</mn>", "x squared plus 1")]
    [InlineData("<msup><mi>x</mi><mn>3</mn></msup>", "x cubed")]
    [InlineData("<msup><mi>x</mi><mi>n</mi></msup>", "x to the n power")]
    [InlineData("<msup><mi>e</mi><mrow><mi>x</mi><mo>+</mo><mn>1</mn></mrow></msup>", "e to the power x plus 1, end power")]
    [InlineData("<mfrac><mi>a</mi><mi>b</mi></mfrac>", "a over b")]
    [InlineData("<mfrac><mrow><mi>a</mi><mo>+</mo><mi>b</mi></mrow><mi>c</mi></mfrac>", "the fraction with numerator a plus b and denominator c")]
    [InlineData("<msqrt><mi>x</mi></msqrt>", "the square root of x")]
    [InlineData("<msqrt><mrow><mi>x</mi><mo>+</mo><mn>1</mn></mrow></msqrt>", "the square root of x plus 1, end root")]
    [InlineData("<mroot><mi>x</mi><mn>3</mn></mroot>", "the cube root of x")]
    [InlineData("<mroot><mi>x</mi><mn>5</mn></mroot>", "the 5th root of x")]
    [InlineData("<msub><mi>a</mi><mi>i</mi></msub>", "a sub i")]
    [InlineData("<msubsup><mi>x</mi><mi>i</mi><mn>2</mn></msubsup>", "x sub i squared")]
    [InlineData("<mi>π</mi><msup><mi>r</mi><mn>2</mn></msup>", "pi r squared")]
    [InlineData("<mi>a</mi><mo>≤</mo><mi>b</mi><mo>≠</mo><mi>c</mi>", "a is less than or equal to b is not equal to c")]
    [InlineData("<munderover><mo>∑</mo><mrow><mi>i</mi><mo>=</mo><mn>1</mn></mrow><mi>n</mi></munderover><msub><mi>a</mi><mi>i</mi></msub>", "sum from i equals 1 to n a sub i")]
    [InlineData("<munder><mo>∑</mo><mi>S</mi></munder>", "sum from S")]
    [InlineData("<mover><mi>x</mi><mo>¯</mo></mover>", "x with ¯ over it")]
    [InlineData("<mfenced><mi>a</mi><mi>b</mi></mfenced>", "left parenthesis a comma b right parenthesis")]
    [InlineData("<mfenced open='[' close=']' separators=';'><mn>1</mn><mn>2</mn></mfenced>", "left bracket 1 semicolon 2 right bracket")]
    [InlineData("<mo>(</mo><mi>a</mi><mo>)</mo><mo>!</mo>", "left parenthesis a right parenthesis factorial")]
    [InlineData("<mtext>for all</mtext><mi>x</mi><mo>∈</mo><mi>ℝ</mi>", "for all x is an element of the real numbers")]
    [InlineData("<mtable><mtr><mtd><mi>a</mi></mtd><mtd><mi>b</mi></mtd></mtr><mtr><mtd><mi>c</mi></mtd><mtd><mi>d</mi></mtd></mtr></mtable>",
        "table with 2 rows and 2 columns; row 1: a, b; row 2: c, d")]
    [InlineData("<mi>x</mi><mspace width='1em'/><mphantom><mi>hidden</mi></mphantom><mi>y</mi>", "x y")]
    [InlineData("<mstyle><mrow><mi>a</mi><mo>&#x2062;</mo><mi>b</mi></mrow></mstyle>", "a b")]
    [InlineData("<mmultiscripts><mi>X</mi><mi>a</mi><mi>b</mi></mmultiscripts>", "X with scripts a b")]
    [InlineData("<menclose notation='box'><mi>x</mi></menclose>", "x")]
    public async Task AFormulaIsSaidInEnglish(string markup, string expected)
    {
        Assert.Equal(expected, (await MathAsync(markup)).Name);
    }

    [Fact]
    public async Task APlainTextAnnotationIsTheAuthorsOwnReading()
    {
        var node = await MathAsync("<semantics><mrow><mi>x</mi><mo>+</mo><mi>y</mi></mrow><annotation encoding='text/plain'>the sum of x and y</annotation></semantics>");

        Assert.Equal("the sum of x and y", node.Name);
    }

    [Fact]
    public async Task WithoutAPlainTextAnnotationTheFirstChildOfSemanticsIsSaid()
    {
        var node = await MathAsync("<semantics><mi>x</mi><annotation encoding='application/x-tex'>x</annotation></semantics>");

        Assert.Equal("x", node.Name);
    }
}
