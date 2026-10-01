using System.Text.RegularExpressions;
using PeachPDF.Adapters;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// A tall absolutely positioned box breaks in passes of its own, so that its break does not end the pass of the
/// block around it and the in-flow content after it keeps its words.
/// </summary>
public class AbsoluteBoxPassesIntegrationTests
{
    // A fuzz reduction, kept with its own words and markup because the line breaks decide which line meets a page
    // edge: a 32-line box at top: 14pt in a relative parent, then paragraphs. Laid out as part of its parent's pass,
    // the box's break left two of the following words on no page.
    [Fact]
    public async Task TallAbsoluteBoxInARelativeParent_LeavesTheContentAfterItItsWords()
    {
        const string body = """
            <div style='position:relative'>
            <div style='position:absolute;top:14pt;left:60pt;width:102pt;'>
            <div>w246_52 w246_53<br>w246_54 w246_55 w246_56<br>w246_57 w246_58<br>w246_59<br>w246_60<br>w246_61 w246_62 w246_63<br>w246_64 w246_65<br>w246_66<br>w246_67 w246_68<br>w246_69 w246_70<br>w246_71 w246_72 w246_73<br>w246_74 w246_75 w246_76<br>w246_77 w246_78<br>w246_79 w246_80<br>w246_81 w246_82 w246_83<br>w246_84 w246_85 w246_86<br>w246_87 w246_88<br>w246_89<br>w246_90<br>w246_91 w246_92<br>w246_93 w246_94 w246_95<br>w246_96 w246_97 w246_98<br>w246_99<br>w246_100<br>w246_101 w246_102<br>w246_103<br>w246_104 w246_105<br>w246_106<br>w246_107 w246_108<br>w246_109 w246_110 w246_111<br>w246_112 w246_113<br>w246_114 w246_115</div>
            </div>
            <p>w246_20 w246_21 w246_22 w246_23 w246_24 w246_25 w246_26 w246_27 w246_28 w246_29 w246_30 w246_31 w246_32 w246_33 w246_34 w246_35 w246_36 w246_37 w246_38 w246_39 w246_40 w246_41 w246_42 w246_43 w246_44 w246_45 w246_46</p>
            <p>w246_1 w246_2 w246_3 w246_4 w246_5 w246_6 w246_7 w246_8 w246_9 w246_10 w246_11 w246_12 w246_13 w246_14 w246_15 w246_16 w246_17 w246_18 w246_19</p>
            </div>
            <p>w246_477 w246_478 w246_479 w246_480 w246_481 w246_482 w246_483 w246_484 w246_485 w246_486 w246_487 w246_488 w246_489 w246_490 w246_491 w246_492 w246_493 w246_494 w246_495 w246_496 w246_497 w246_498 w246_499 w246_500 w246_501 w246_502 w246_503</p>
            <p>w246_504 w246_505 w246_506 w246_507 w246_508 w246_509 w246_510 w246_511 w246_512 w246_513 w246_514 w246_515 w246_516</p>
            <table>
            <p>w246_544 w246_545 w246_546 w246_547 w246_548 w246_549 w246_550 w246_551 w246_552 w246_553 w246_554 w246_555 w246_556 w246_557 w246_558 w246_559 w246_560 w246_561 w246_562 w246_563 w246_564 w246_565 w246_566 w246_567 w246_568 w246_569 w246_570 w246_571 w246_572 w246_573 w246_574 w246_575 w246_576 w246_577 w246_578</p>
            <td>/tr>
            </table>
            """;

        var html = "<!DOCTYPE html><html><head><style>@page { size: 300pt 240pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt sans-serif } p { margin:0 0 4pt } td { vertical-align: top }</style></head><body>" +
                   body.ReplaceLineEndings("") + "</body></html>";

        var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
            html, new PdfGenerateConfig(), BundledFonts.PinSansSerifAsync);

        List<string> painted = [];
        for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
        {
            using var graphics = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, graphics, page);
            painted.AddRange(graphics.Log.Where(op => op.Kind == PaintOpKind.DrawString).Select(op => op.Text!));
        }

        var expected = Regex.Matches(body, @"w\d+_\d+").Select(m => m.Value).Order().ToList();
        Assert.Equal(expected, painted.Where(word => Regex.IsMatch(word, @"^w\d+_\d+$")).Order().ToList());
    }
}
