using PeachBrackets.Updates;

using PeachDrawing;

namespace PeachBrackets.Tests;

public class FormValidationAndResetTests
{
    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string body)
    {
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(body, "<style>@page { margin: 0 } input, button, select, textarea { display: block; margin: 0 }</style>"), new HtmlDocumentOptions { Media = "screen", PaintFormControls = true });
        return (context, document);
    }

    private static async Task<IReadOnlyList<(string? Id, ValidationKind Kind)>> ProblemsAsync(string controls, string formAttributes = "")
    {
        var (_, document) = await LoadAsync("<form " + formAttributes + ">" + controls + "<button id=go>Go</button></form>");
        return [.. document.GetValidationProblems(document.GetElement("go")!).Select(p => (p.Element.Id, p.Kind))];
    }

    [Theory]
    [InlineData("<input id=a required>", "a", ValidationKind.ValueMissing)]
    [InlineData("<input id=a required value=x>", null, null)]
    [InlineData("<textarea id=a required></textarea>", "a", ValidationKind.ValueMissing)]
    [InlineData("<input id=a type=checkbox required>", "a", ValidationKind.ValueMissing)]
    [InlineData("<input id=a type=checkbox required checked>", null, null)]
    [InlineData("<input id=a type=radio name=r required><input id=b type=radio name=r>", "a", ValidationKind.ValueMissing)]
    [InlineData("<input id=a type=radio name=r required><input id=b type=radio name=r checked>", null, null)]
    [InlineData("<input id=a type=radio required>", "a", ValidationKind.ValueMissing)]
    [InlineData("<select id=a required><option value=''>Choose</option><option value=1>One</option></select>", "a", ValidationKind.ValueMissing)]
    [InlineData("<select id=a required><option value=''>Choose</option><option value=1 selected>One</option></select>", null, null)]
    [InlineData("<select id=a required multiple><option value=1>One</option></select>", "a", ValidationKind.ValueMissing)]
    [InlineData("<select id=a><option value=''>Choose</option></select>", null, null)]
    [InlineData("<input id=a type=file required>", "a", ValidationKind.ValueMissing)]
    [InlineData("<input id=a minlength=5 value=abc>", "a", ValidationKind.TooShort)]
    [InlineData("<input id=a minlength=2 value=abc>", null, null)]
    [InlineData("<input id=a maxlength=2 value=abc>", "a", ValidationKind.TooLong)]
    [InlineData("<input id=a type=email value=nope>", "a", ValidationKind.TypeMismatch)]
    [InlineData("<input id=a type=email value='a@b.co'>", null, null)]
    [InlineData("<input id=a type=email multiple value='a@b.co, c@d.org'>", null, null)]
    [InlineData("<input id=a type=email multiple value='a@b.co, nope'>", "a", ValidationKind.TypeMismatch)]
    [InlineData("<input id=a type=url value='example'>", "a", ValidationKind.TypeMismatch)]
    [InlineData("<input id=a type=url value='https://example.com/x'>", null, null)]
    [InlineData("<input id=a pattern='[0-9]{3}' value=12>", "a", ValidationKind.PatternMismatch)]
    [InlineData("<input id=a pattern='[0-9]{3}' value=123>", null, null)]
    [InlineData("<input id=a pattern='(' value=anything>", null, null)]
    [InlineData("<input id=a type=number value=abc>", "a", ValidationKind.TypeMismatch)]
    [InlineData("<input id=a type=number min=5 value=3>", "a", ValidationKind.RangeUnderflow)]
    [InlineData("<input id=a type=number max=5 value=9>", "a", ValidationKind.RangeOverflow)]
    [InlineData("<input id=a type=number step=2 value=3>", "a", ValidationKind.StepMismatch)]
    [InlineData("<input id=a type=number min=1 step=2 value=3>", null, null)]
    [InlineData("<input id=a type=number step=any value=3.14159>", null, null)]
    [InlineData("<input id=a type=number value=2.5>", "a", ValidationKind.StepMismatch)]
    [InlineData("<input id=a type=date min=2026-01-01 value=2025-12-31>", "a", ValidationKind.RangeUnderflow)]
    [InlineData("<input id=a type=date max=2026-01-01 value=2026-01-02>", "a", ValidationKind.RangeOverflow)]
    [InlineData("<input id=a type=date value=garbage>", "a", ValidationKind.TypeMismatch)]
    [InlineData("<input id=a type=time min=09:00 value=08:30>", "a", ValidationKind.RangeUnderflow)]
    [InlineData("<input id=a type=month max=2026-06 value=2026-07>", "a", ValidationKind.RangeOverflow)]
    [InlineData("<input id=a type=datetime-local max=2026-01-01T10:00 value=2026-01-01T11:00>", "a", ValidationKind.RangeOverflow)]
    [InlineData("<input id=a type=range min=0 max=10 value=5>", null, null)]
    public async Task EachConstraintIsCheckedTheWayTheHtmlStandardSaysIt(string controls, string? id, ValidationKind? kind)
    {
        var problems = await ProblemsAsync(controls);

        if (id is null)
            Assert.Empty(problems);
        else
            Assert.Equal((id, kind!.Value), Assert.Single(problems));
    }

    [Fact]
    public async Task ControlsThatAreNotCheckedAreLeftOut()
    {
        var problems = await ProblemsAsync(
            "<input id=a required disabled><input id=b required readonly><input id=c type=hidden required><fieldset disabled><input id=d required></fieldset>"
            + "<button id=e required>X</button><input id=f required>");

        Assert.Equal([("f", ValidationKind.ValueMissing)], problems);
    }

    [Fact]
    public async Task NovalidateOnTheFormOrFormnovalidateOnTheButtonTurnsCheckingOff()
    {
        Assert.Empty(await ProblemsAsync("<input required>", "novalidate"));
        var (_, document) = await LoadAsync("<form><input required><button id=go formnovalidate>Go</button><button id=check>Check</button></form>");

        Assert.Empty(document.GetValidationProblems(document.GetElement("go")!));
        Assert.Single(document.GetValidationProblems(document.GetElement("check")!));
    }

    [Fact]
    public async Task EachControlHasOneProblemAndAFormAsAWholeCanBeAskedAbout()
    {
        var (_, document) = await LoadAsync("<form id=f><input id=a required minlength=3><input id=b type=email value=bad></form><input id=lone required>");

        var problems = document.GetValidationProblems(document.GetElement("f")!);

        Assert.Equal([("a", ValidationKind.ValueMissing), ("b", ValidationKind.TypeMismatch)], problems.Select(p => (p.Element.Id, p.Kind)));
        Assert.All(problems, p => Assert.False(string.IsNullOrWhiteSpace(p.Message)));
        Assert.Empty(document.GetValidationProblems(document.GetElement("lone")!));
        // Asked through one of its controls, the whole form is checked.
        Assert.Equal(2, document.GetValidationProblems(document.GetElement("a")!).Count);
    }

    [Fact]
    public async Task EnterInATextFieldSubmitsThroughTheDefaultButtonOrTheOnlyField()
    {
        var (_, document) = await LoadAsync(
            "<form id=a><input id=a1 name=q value=1><input id=a2 name=r value=2><button id=ab name=b value=B>Go</button><input type=submit id=ab2 name=c></form>"
            + "<form id=b><input id=b1 name=q value=1><input id=b2 name=r value=2></form>"
            + "<form id=c><input id=c1 name=q value=1><input type=checkbox id=c2 name=r checked></form>"
            + "<form id=d><input id=d1 type=checkbox><button id=d2 type=submit disabled>No</button></form>");

        var withButton = document.CreateImplicitSubmission(document.GetElement("a1")!)!;
        var twoFields = document.CreateImplicitSubmission(document.GetElement("b1")!);
        var oneField = document.CreateImplicitSubmission(document.GetElement("c1")!)!;

        Assert.Equal(["q", "r", "b"], withButton.Entries.Select(e => e.Name));
        Assert.Null(twoFields);
        Assert.Equal(["q", "r"], oneField.Entries.Select(e => e.Name));
        Assert.Null(document.CreateImplicitSubmission(document.GetElement("d1")!));
        Assert.Null(document.CreateImplicitSubmission(document.GetElement("a")!));
        Assert.Null(document.CreateImplicitSubmission(document.GetElement("ab")!));
    }

    [Fact]
    public async Task AnImageButtonSendsWhereItWasPressedInCssPixels()
    {
        var (_, document) = await LoadAsync("<form><input type=image id=img name=pic src=none.png style='width:100pt;height:40pt'><input type=image id=anon style='width:10pt;height:10pt'></form>");
        var bounds = document.GetElementBounds("img")!.Value;

        var pressed = document.CreateFormSubmission(document.GetElement("img")!, 0, bounds.X + 30, bounds.Y + 15)!;
        var byKey = document.CreateFormSubmission(document.GetElement("img")!)!;
        var anonymous = document.CreateFormSubmission(document.GetElement("anon")!)!;
        var elsewhere = document.CreateFormSubmission(document.GetElement("img")!, 4, 30, 15)!;

        Assert.Equal([("pic.x", "40"), ("pic.y", "20")], pressed.Entries.Select(e => (e.Name, e.Value)));
        Assert.Equal([("pic.x", "0"), ("pic.y", "0")], byKey.Entries.Select(e => (e.Name, e.Value)));
        Assert.Equal(["x", "y"], anonymous.Entries.Select(e => e.Name));
        Assert.Equal([("pic.x", "0"), ("pic.y", "0")], elsewhere.Entries.Select(e => (e.Name, e.Value)));
    }

    [Fact]
    public async Task ResettingAFormPutsEveryControlBackAsTheSourceHasIt()
    {
        var (context, document) = await LoadAsync(
            "<form id=f><input id=t name=t value=start><input id=c type=checkbox name=c checked><input id=r1 type=radio name=r value=1 checked><input id=r2 type=radio name=r value=2>"
            + "<select id=s name=s><option value=a selected>A</option><option value=b>B</option></select><textarea id=ta name=ta>original</textarea>"
            + "<input id=file type=file name=file><button id=reset type=reset>Reset</button></form><input id=outside name=outside value=keep>");
        using var measuring = context.CreateCanvas(1, 1);
        document.SetValue(document.GetElement("t")!, "changed");
        document.SetChecked(document.GetElement("c")!, false);
        document.SetChecked(document.GetElement("r2")!, true);
        document.SetValue(document.GetElement("s")!, "b");
        document.SetValue(document.GetElement("ta")!, "edited");
        document.SetFiles(document.GetElement("file")!, [new ChosenFile("x.txt", "text/plain", [1])]);
        document.SetValue(document.GetElement("outside")!, "mine");

        Assert.True(document.IsResetButton(document.GetElement("reset")!));
        Assert.False(document.IsResetButton(document.GetElement("t")!));
        var result = await document.ResetFormAsync(document.GetElement("reset")!, measuring);

        Assert.True(result.Applied);
        string Value(string id) => document.GetFormState(document.GetElement(id)!)!.Value.Value;
        bool Checked(string id) => document.GetFormState(document.GetElement(id)!)!.Value.Checked;
        Assert.Equal("start", Value("t"));
        Assert.True(Checked("c"));
        Assert.True(Checked("r1"));
        Assert.False(Checked("r2"));
        Assert.Equal("a", Value("s"));
        Assert.Equal("original", Value("ta"));
        Assert.Equal(string.Empty, Value("file"));
        Assert.Equal("mine", Value("outside"));
    }

    [Fact]
    public async Task OnlyAnElementInAFormCanBeReset()
    {
        var (context, document) = await LoadAsync("<form id=f><input id=in name=a></form><input id=lone><p id=p>text</p><form id=empty></form>");
        using var measuring = context.CreateCanvas(1, 1);

        Assert.False((await document.ResetFormAsync(document.GetElement("lone")!, measuring)).Applied);
        Assert.False((await document.ResetFormAsync(document.GetElement("empty")!, measuring)).Applied);
        Assert.True((await document.ResetFormAsync(document.GetElement("f")!, measuring)).Applied);
        Assert.True((await document.ResetFormAsync(document.GetElement("in")!, measuring)).Applied);
    }
}
