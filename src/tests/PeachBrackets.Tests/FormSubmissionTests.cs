using System.Text;
using PeachBrackets.Updates;

namespace PeachBrackets.Tests;

public class FormSubmissionTests
{
    private static async Task<HtmlDocument> LoadAsync(string body)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, "<style>@page { margin: 0 }</style>"), new HtmlDocumentOptions { MediaType = MediaType.Screen, PaintFormControls = true });
        return document;
    }

    private const string Everything =
        "<form id=f action='/send?old=1#top' method=post>"
        + "<input name=text value='a b&c=é'><input name=empty>"
        + "<input type=checkbox name=on checked><input type=checkbox name=off><input type=checkbox name=custom value=yes checked>"
        + "<input type=radio name=r value=1><input type=radio name=r value=2 checked>"
        + "<select name=s><option value=x>X</option><option value=y selected>Y</option></select>"
        + "<select name=first><option value=p>P</option><option value=q>Q</option></select>"
        + "<select name=many multiple><option value=1 selected>1</option><option value=2>2</option><option value=3 selected>3</option></select>"
        + "<textarea name=area>one\ntwo</textarea>"
        + "<input type=hidden name=h value=secret><input name=off2 value=x disabled>"
        + "<fieldset disabled><input name=inside value=x></fieldset>"
        + "<input value=unnamed>"
        + "<input type=submit id=go name=go value='Send it'><input type=submit id=other name=other value=nope>"
        + "<button id=b name=b value=B>Press</button><button id=plain type=button name=plain>Plain</button>"
        + "</form><input id=outside name=outside value=o form=f><input id=stray name=stray value=s>";

    [Fact]
    public async Task ASubmissionHoldsEveryControlThatTakesPartInDocumentOrder()
    {
        var document = await LoadAsync(Everything);

        var submission = document.CreateFormSubmission(document.GetElement("go")!)!;

        var entries = submission.Entries.Select(e => (e.Name, e.Value)).ToList();
        Assert.Equal(
            [
                ("text", "a b&c=é"), ("empty", ""), ("on", "on"), ("custom", "yes"), ("r", "2"), ("s", "y"), ("first", "p"), ("many", "1"), ("many", "3"),
                ("area", "one\r\ntwo"), ("h", "secret"), ("go", "Send it"), ("outside", "o"),
            ],
            entries.Select(e => (e.Name, (string?)e.Value)));
        Assert.Equal(("post", "application/x-www-form-urlencoded"), (submission.Method, submission.EncType));
        Assert.EndsWith("/send?old=1#top", submission.Action);
    }

    [Fact]
    public async Task OnlyTheButtonThatWasUsedIsSent()
    {
        var document = await LoadAsync(Everything);

        var viaButton = document.CreateFormSubmission(document.GetElement("b")!)!;

        Assert.Contains(viaButton.Entries, e => e.Name == "b" && e.Value == "B");
        Assert.DoesNotContain(viaButton.Entries, e => e.Name is "go" or "other" or "plain");
    }

    [Fact]
    public async Task OnlyASubmitButtonInAFormMakesASubmission()
    {
        var document = await LoadAsync(Everything + "<button id=lone>Lone</button><input type=submit id=off disabled>");

        Assert.Null(document.CreateFormSubmission(document.GetElement("plain")!));
        Assert.Null(document.CreateFormSubmission(document.GetElement("stray")!));
        Assert.Null(document.CreateFormSubmission(document.GetElement("lone")!));
        Assert.Null(document.CreateFormSubmission(document.GetElement("f")!));
    }

    [Fact]
    public async Task UrlEncodingTurnsSpacesIntoPlusesAndEscapesTheRest()
    {
        var document = await LoadAsync(Everything);
        var submission = document.CreateFormSubmission(document.GetElement("go")!)!;

        var (type, body) = submission.Serialize();
        var text = Encoding.ASCII.GetString(body);

        Assert.Equal("application/x-www-form-urlencoded", type);
        Assert.StartsWith("text=a+b%26c%3D%C3%A9&empty=&on=on&custom=yes&r=2&s=y&first=p&many=1&many=3&area=one%0D%0Atwo&h=secret&go=Send+it&outside=o", text);
    }

    [Fact]
    public async Task AGetFormPutsItsEntriesInTheQueryOfTheAction()
    {
        var document = await LoadAsync("<form action='/search?old=1#frag'><input name=q value='x y'><button id=go name=go value=1>Go</button></form>");
        var submission = document.CreateFormSubmission(document.GetElement("go")!)!;

        Assert.Equal("get", submission.Method);
        Assert.EndsWith("/search?q=x+y&go=1#frag", submission.GetUri());
    }

    [Fact]
    public async Task AButtonCanOverrideTheMethodAddressAndEncoding()
    {
        var document = await LoadAsync("<form action='/a' method=get><input name=q value=1><button id=go formmethod=post formaction='/b' formenctype='text/plain'>Go</button></form>");

        var submission = document.CreateFormSubmission(document.GetElement("go")!)!;

        Assert.Equal(("post", "text/plain"), (submission.Method, submission.EncType));
        Assert.EndsWith("/b", submission.Action);
        var (type, body) = submission.Serialize();
        Assert.Equal("text/plain", type);
        Assert.Equal("q=1\r\n", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task AnUnknownMethodAndEncodingFallBackAndADialogSendsNothing()
    {
        var document = await LoadAsync("<form method=bogus enctype=bogus><input name=q value=1><button id=a>Go</button></form><form method=dialog><input name=d value=2><button id=b>Close</button></form>");

        var fallback = document.CreateFormSubmission(document.GetElement("a")!)!;
        var dialog = document.CreateFormSubmission(document.GetElement("b")!)!;

        Assert.Equal(("get", "application/x-www-form-urlencoded", string.Empty), (fallback.Method, fallback.EncType, fallback.Action));
        Assert.Equal("dialog", dialog.Method);
        Assert.Empty(dialog.Serialize().Body);
        Assert.Equal(dialog.Action, dialog.GetUri());
    }

    [Fact]
    public async Task AMultipartBodyCarriesTheFilesTheHostChose()
    {
        var document = await LoadAsync("<form method=post enctype='multipart/form-data'><input name=title value='a\"b'><input id=file type=file name=upload multiple><input id=none type=file name=nothing><button id=go>Go</button></form>");
        var content = new byte[] { 1, 2, 3, 0, 255 };
        Assert.True(document.SetFiles(document.GetElement("file")!, [new ChosenFile("a.bin", "application/x-bin", content), new ChosenFile("b.txt", string.Empty, Encoding.UTF8.GetBytes("hello"))]).Applied);

        var submission = document.CreateFormSubmission(document.GetElement("go")!)!;
        var (type, body) = submission.Serialize();

        Assert.StartsWith("multipart/form-data; boundary=", type);
        var boundary = type["multipart/form-data; boundary=".Length..];
        var text = Encoding.Latin1.GetString(body);
        Assert.StartsWith("--" + boundary + "\r\n", text);
        Assert.EndsWith("--" + boundary + "--\r\n", text);
        Assert.Contains("Content-Disposition: form-data; name=\"title\"\r\n\r\na\"b\r\n", text);
        Assert.Contains("name=\"upload\"; filename=\"a.bin\"\r\nContent-Type: application/x-bin\r\n\r\n\u0001\u0002\u0003\0ÿ\r\n", text);
        Assert.Contains("filename=\"b.txt\"\r\nContent-Type: application/octet-stream\r\n\r\nhello\r\n", text);
        Assert.Contains("name=\"nothing\"; filename=\"\"\r\nContent-Type: application/octet-stream\r\n\r\n\r\n", text);
        Assert.Equal("a.bin, b.txt", document.GetFormState(document.GetElement("file")!)!.Value.Value);
    }

    [Fact]
    public async Task FilesAreReplacedClearedAndKeptByNameWhenOnlyNamesAreSet()
    {
        var document = await LoadAsync("<form method=post enctype='multipart/form-data'><input id=file type=file name=u multiple><button id=go>Go</button></form>");
        var file = document.GetElement("file")!;
        document.SetFiles(file, [new ChosenFile("keep.txt", "text/plain", [7, 7])]);

        document.SetFileNames(file, ["keep.txt", "new.txt"]);
        var kept = document.CreateFormSubmission(document.GetElement("go")!)!.Entries.Select(e => (e.File!.Name, e.File.Content.Length)).ToList();
        document.SetFiles(file, []);
        var cleared = document.CreateFormSubmission(document.GetElement("go")!)!.Entries.Single();
        document.SetFiles(file, [new ChosenFile("  ", "", [1])]);
        var blank = document.CreateFormSubmission(document.GetElement("go")!)!.Entries.Single();

        Assert.Equal([("keep.txt", 2), ("new.txt", 0)], kept);
        Assert.Equal(string.Empty, cleared.File!.Name);
        Assert.Equal(string.Empty, blank.File!.Name);
        Assert.False(document.SetFiles(document.GetElement("go")!, []).Applied);
    }

    [Fact]
    public async Task ASubmissionOfAnotherFormLeavesThisFormsControlsOut()
    {
        var document = await LoadAsync("<form id=a><input name=one value=1><button id=ga>A</button></form><form id=b><input name=two value=2><button id=gb>B</button></form>");

        Assert.Equal(["one"], document.CreateFormSubmission(document.GetElement("ga")!)!.Entries.Select(e => e.Name));
        Assert.Equal(["two"], document.CreateFormSubmission(document.GetElement("gb")!)!.Entries.Select(e => e.Name));
    }

    [Fact]
    public async Task MultipartNamesAreEscapedAndAFileFallsBackToItsNameInAQuery()
    {
        var document = await LoadAsync("<form method=get><input id=file type=file name='we\"ird'><button id=go>Go</button></form>" + "<form method=post enctype='multipart/form-data'><input name='q\"x' value=v><button id=send>Send</button></form>");
        document.SetFiles(document.GetElement("file")!, [new ChosenFile("f.txt", "text/plain", [1])]);

        Assert.EndsWith("?we%22ird=f.txt", document.CreateFormSubmission(document.GetElement("go")!)!.GetUri());
        Assert.Contains("name=\"q%22x\"", Encoding.Latin1.GetString(document.CreateFormSubmission(document.GetElement("send")!)!.Serialize().Body));
    }
}
