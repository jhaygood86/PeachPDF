using PeachPDF;
using PeachPDF.PdfSharpCore;

PdfGenerateConfig pdfConfig = new()
{
    PageSize = PageSize.A4,
    PageOrientation = PageOrientation.Portrait,
    ShrinkToFit = true
};

PdfGenerator generator = new();
var stream = new MemoryStream();

static string Swatch(string desc, string css) =>
    "<td>" +
    $"<div class=\"box\" style=\"background-image: {css}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{css}</div>" +
    "</td>";

static string RadiusSwatch(string desc, string borderRadiusCss, string boxCss = "") =>
    "<td>" +
    $"<div class=\"rbox\" style=\"{boxCss}border-radius: {borderRadiusCss}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">border-radius: {borderRadiusCss}</div>" +
    "</td>";

static string Row(params string[] cells) =>
    $"<table class=\"sw\"><tr>{string.Join("", cells)}</tr></table>";

const string Css = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    p.intro { margin: 0 0 0.7em; color: #555 }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .box { height: 48px; border: 1px solid #000; margin-bottom: 3px }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var html = "<!DOCTYPE html><html><head>" + Css + "</head><body>" +

    "<h1>CSS Gradient Test Page</h1>" +

    "<h2>1 — linear-gradient: Direction &amp; Angle</h2>" +
    Row(
        Swatch("default (to bottom)", "linear-gradient(red, blue)"),
        Swatch("to top", "linear-gradient(to top, red, blue)"),
        Swatch("to right", "linear-gradient(to right, red, blue)"),
        Swatch("45deg", "linear-gradient(45deg, red, blue)")
    ) +

    "<h2>2 — linear-gradient: Multi-Stop &amp; Positions</h2>" +
    Row(
        Swatch("3 stops", "linear-gradient(to right, red, yellow, blue)"),
        Swatch("abs lengths", "linear-gradient(to right, red 0, yellow 40px, blue 80px)"),
        Swatch("hard stop", "linear-gradient(to right, red 0 50%, blue 50% 100%)"),
        Swatch("color hint", "linear-gradient(to right, red, 30%, blue)")
    ) +

    "<h2>3 — linear-gradient: Alpha Transparency</h2>" +
    Row(
        Swatch("transparent → color", "linear-gradient(to right, rgba(255,0,0,0), red)"),
        Swatch("color → transparent", "linear-gradient(to right, rgba(0,128,255,1), rgba(0,128,255,0))"),
        Swatch("rgba() 80% opacity", "linear-gradient(to right, rgba(255,0,0,0.8), rgba(0,0,255,0.8))"),
        Swatch("multi-stop alpha", "linear-gradient(to right, red, rgba(255,255,0,0.5), rgba(0,0,255,0))")
    ) +

    "<h2>4 — repeating-linear-gradient</h2>" +
    Row(
        Swatch("stripes 20px", "repeating-linear-gradient(to right, red 0 10px, blue 10px 20px)"),
        Swatch("45deg stripes", "repeating-linear-gradient(45deg, red 0 8px, white 8px 16px)"),
        Swatch("fade repeat", "repeating-linear-gradient(to right, red 0, blue 30px)"),
        Swatch("full span = no repeat", "repeating-linear-gradient(to right, red, blue)")
    ) +

    "<h2>5 — radial-gradient: Basic</h2>" +
    Row(
        Swatch("default (ellipse center)", "radial-gradient(red, blue)"),
        Swatch("circle", "radial-gradient(circle, red, blue)"),
        Swatch("at 25% 25%", "radial-gradient(at 25% 25%, red, blue)"),
        Swatch("circle at 50% 25%", "radial-gradient(circle at 50% 25%, yellow, orange, red)")
    ) +

    "<h2>6 — radial-gradient: Size Keywords &amp; Explicit</h2>" +
    Row(
        Swatch("farthest-corner", "radial-gradient(farthest-corner at 30% 30%, red, blue)"),
        Swatch("closest-side", "radial-gradient(closest-side at 30% 30%, red, blue)"),
        Swatch("farthest-side", "radial-gradient(farthest-side at 30% 30%, red, blue)"),
        Swatch("explicit 30px", "radial-gradient(30px at center, red, blue)")
    ) +

    "<h2>7 — radial-gradient: Alpha</h2>" +
    Row(
        Swatch("transparent center", "radial-gradient(rgba(255,0,0,0), rgba(255,0,0,1))"),
        Swatch("spotlight", "radial-gradient(circle, rgba(255,255,255,0.9), rgba(255,255,255,0))"),
        Swatch("sunset", "radial-gradient(circle, #fff7e6, #ff6b35, #1a1a2e)"),
        Swatch("multi-stop alpha", "radial-gradient(circle, rgba(255,0,0,1), rgba(255,255,0,0.5), rgba(0,0,255,0))")
    ) +

    "<h2>8 — repeating-radial-gradient</h2>" +
    Row(
        Swatch("rings", "repeating-radial-gradient(circle, red 0 10px, blue 10px 20px)"),
        Swatch("fade rings", "repeating-radial-gradient(circle, red 0, blue 25px)"),
        Swatch("ellipse rings", "repeating-radial-gradient(red 0 8px, white 8px 16px)"),
        Swatch("with alpha", "repeating-radial-gradient(circle, rgba(255,0,0,0.8) 0 10px, rgba(0,0,255,0.8) 10px 20px)")
    ) +

    "<h2>9 — conic-gradient: Basic</h2>" +
    Row(
        Swatch("default", "conic-gradient(red, blue)"),
        Swatch("3 stops", "conic-gradient(red, yellow, blue)"),
        Swatch("from 90deg", "conic-gradient(from 90deg, red, blue)"),
        Swatch("at 25% 75%", "conic-gradient(at 25% 75%, red, green, blue)")
    ) +

    "<h2>10 — conic-gradient: Stop Positions</h2>" +
    Row(
        Swatch("angle stops", "conic-gradient(red 0deg, blue 180deg, green 360deg)"),
        Swatch("percent stops", "conic-gradient(red 0%, blue 50%, green 100%)"),
        Swatch("hard stop", "conic-gradient(red 0 90deg, blue 90deg 180deg, green 180deg 360deg)"),
        Swatch("pie chart", "conic-gradient(#e74c3c 0 25%, #3498db 25% 65%, #2ecc71 65% 100%)")
    ) +

    "<h2>11 — conic-gradient: Alpha &amp; From+At</h2>" +
    Row(
        Swatch("with alpha", "conic-gradient(rgba(255,0,0,0), red)"),
        Swatch("from 45deg at 30% 70%", "conic-gradient(from 45deg at 30% 70%, red, blue)"),
        Swatch("color wheel", "conic-gradient(red, yellow, lime, cyan, blue, magenta, red)"),
        Swatch("starburst", "conic-gradient(gold 0 10%, white 10% 20%, gold 20% 30%, white 30% 40%, gold 40% 50%, white 50% 60%, gold 60% 70%, white 70% 80%, gold 80% 90%, white 90% 100%)")
    ) +

    "<h2>12 — repeating-conic-gradient</h2>" +
    Row(
        Swatch("60deg tile", "repeating-conic-gradient(red 0deg 30deg, blue 30deg 60deg)"),
        Swatch("from 45deg", "repeating-conic-gradient(from 45deg, red 0deg, blue 60deg)"),
        Swatch("checkerboard-like", "repeating-conic-gradient(#000 0 25%, #fff 25% 50%)"),
        Swatch("narrow slice", "repeating-conic-gradient(red 0 5deg, blue 5deg 10deg)")
    ) +

    "<h2>13 — Color Space Interpolation: in oklab</h2>" +
    Row(
        Swatch("sRGB red→blue", "linear-gradient(to right, red, blue)"),
        Swatch("oklab red→blue", "linear-gradient(in oklab to right, red, blue)"),
        Swatch("oklab red→green", "linear-gradient(in oklab to right, red, green)"),
        Swatch("oklab red→yellow→blue", "linear-gradient(in oklab to right, red, yellow, blue)")
    ) +

    "<h2>14 — Color Space Interpolation: Polar (HSL, OKLch)</h2>" +
    Row(
        Swatch("hsl shorter red→blue", "linear-gradient(in hsl, red, blue)"),
        Swatch("hsl longer red→blue", "linear-gradient(in hsl longer hue, red, blue)"),
        Swatch("oklch shorter red→blue", "linear-gradient(in oklch, red, blue)"),
        Swatch("oklch longer red→blue", "linear-gradient(in oklch longer hue, red, blue)")
    ) +

    "<h2>15 — Color Space Interpolation: Lab, LCH, sRGB-linear</h2>" +
    Row(
        Swatch("lab red→blue", "linear-gradient(in lab to right, red, blue)"),
        Swatch("lch red→blue", "linear-gradient(in lch to right, red, blue)"),
        Swatch("srgb-linear red→blue", "linear-gradient(in srgb-linear to right, red, blue)"),
        Swatch("display-p3 red→blue", "linear-gradient(in display-p3 to right, red, blue)")
    ) +

    "<h2>16 — Color Space: Radial &amp; Conic</h2>" +
    Row(
        Swatch("radial oklab", "radial-gradient(in oklab circle, red, blue)"),
        Swatch("radial oklch", "radial-gradient(in oklch circle, red, blue)"),
        Swatch("conic oklab", "conic-gradient(in oklab, red, blue)"),
        Swatch("conic oklch longer", "conic-gradient(in oklch longer hue, red, blue)")
    ) +

    "</body></html>";

var document = await generator.GeneratePdf(html, pdfConfig);
document.Save(stream);

File.Delete("test_gradients.pdf");
File.WriteAllBytes("test_gradients.pdf", stream.ToArray());
Console.WriteLine("Saved test_gradients.pdf");

File.Delete("test_gradients.html");
File.WriteAllText("test_gradients.html", html);
Console.WriteLine("Saved test_gradients.html");

// --- Border-radius showcase ---

const string RadiusCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    p.intro { margin: 0 0 0.7em; color: #555 }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .rbox { height: 60px; background: steelblue; border: 2px solid #1a6b8a; margin-bottom: 3px }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var radiusHtml = "<!DOCTYPE html><html><head>" + RadiusCss + "</head><body>" +

    "<h1>CSS border-radius Test Page</h1>" +

    "<h2>1 — Shorthand: 1–4 values</h2>" +
    Row(
        RadiusSwatch("1 value — all equal", "20px"),
        RadiusSwatch("2 values — opposite pairs", "10px 30px"),
        RadiusSwatch("3 values — TL / TR+BL / BR", "8px 20px 35px"),
        RadiusSwatch("4 values — each corner", "5px 15px 30px 45px")
    ) +

    "<h2>2 — Individual Longhands</h2>" +
    Row(
        RadiusSwatch("top-left only", "0", "border-top-left-radius: 30px; "),
        RadiusSwatch("bottom-right only", "0", "border-bottom-right-radius: 30px; "),
        RadiusSwatch("TL + BR set", "0", "border-top-left-radius: 25px; border-bottom-right-radius: 25px; "),
        RadiusSwatch("all 4 longhands", "0", "border-top-left-radius: 10px; border-top-right-radius: 20px; border-bottom-right-radius: 30px; border-bottom-left-radius: 15px; ")
    ) +

    "<h2>3 — Elliptical Corners (/ syntax)</h2>" +
    Row(
        RadiusSwatch("flat wide (40px / 10px)", "40px / 10px"),
        RadiusSwatch("tall narrow (10px / 40px)", "10px / 40px"),
        RadiusSwatch("uniform ellipse (30px / 20px)", "30px / 20px"),
        RadiusSwatch("asymmetric", "20px 0 / 0 20px")
    ) +

    "<h2>4 — Percentage Values</h2>" +
    Row(
        RadiusSwatch("50% — pill/ellipse", "50%"),
        RadiusSwatch("25% — quarter round", "25%"),
        RadiusSwatch("50% / 25%", "50% / 25%"),
        RadiusSwatch("25% / 50%", "25% / 50%")
    ) +

    "<h2>5 — Overlapping Radii Reduction</h2>" +
    "<p class=\"intro\">Each box is 80px wide. A radius larger than half the box is automatically scaled down.</p>" +
    Row(
        RadiusSwatch("60px on 80px box", "60px", "width: 80px; "),
        RadiusSwatch("100px (auto-capped)", "100px", "width: 80px; "),
        RadiusSwatch("50% on tall box", "50%", "width: 80px; height: 80px; "),
        RadiusSwatch("no overlap — 20px", "20px", "width: 80px; ")
    ) +

    "<h2>6 — Combined Styles</h2>" +
    Row(
        RadiusSwatch("solid border + bg", "15px"),
        RadiusSwatch("dashed border", "15px", "border-style: dashed; "),
        RadiusSwatch("dotted border", "15px", "border-style: dotted; "),
        RadiusSwatch("no border, bg only", "15px", "border: none; ")
    ) +

    "</body></html>";

var radiusStream = new MemoryStream();
var radiusDocument = await generator.GeneratePdf(radiusHtml, pdfConfig);
radiusDocument.Save(radiusStream);

File.Delete("test_border_radius.pdf");
File.WriteAllBytes("test_border_radius.pdf", radiusStream.ToArray());
Console.WriteLine("Saved test_border_radius.pdf");

File.Delete("test_border_radius.html");
File.WriteAllText("test_border_radius.html", radiusHtml);
Console.WriteLine("Saved test_border_radius.html");
