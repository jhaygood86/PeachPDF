using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.Linq;
using System.Text;

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

static string OriginSwatch(string desc, string inlineCss, string cssLabel = "") =>
    "<td>" +
    $"<div class=\"obox\" style=\"{inlineCss}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{(string.IsNullOrEmpty(cssLabel) ? inlineCss : cssLabel)}</div>" +
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

// --- background-origin + background-clip showcase ---

const string OriginCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .obox { height: 60px; border: 8px solid #333; padding: 12px; margin-bottom: 3px; box-sizing: border-box }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var originHtml = "<!DOCTYPE html><html><head>" + OriginCss + "</head><body>" +

    "<h1>CSS background-origin &amp; background-clip Test Page</h1>" +
    "<p class=\"intro\">Each box has border: 8px solid #333 and padding: 12px. Three regions: border-box (full), padding-box (inside border), content-box (inside padding).</p>" +

    "<h2>1 — background-origin: Solid Color</h2>" +
    "<p class=\"intro\">Solid colors fill the clip area regardless of origin — this verifies no rendering errors.</p>" +
    Row(
        OriginSwatch("default (padding-box)", "background-color: steelblue"),
        OriginSwatch("border-box", "background-color: steelblue; background-origin: border-box", "background-origin: border-box"),
        OriginSwatch("padding-box", "background-color: steelblue; background-origin: padding-box", "background-origin: padding-box"),
        OriginSwatch("content-box", "background-color: steelblue; background-origin: content-box", "background-origin: content-box")
    ) +

    "<h2>2 — background-origin: Linear Gradient</h2>" +
    "<p class=\"intro\">Gradient coordinate space shifts with origin. border-box spans the full box; content-box spans only the content area.</p>" +
    Row(
        OriginSwatch("default (padding-box)", "background: linear-gradient(to right, red, blue)"),
        OriginSwatch("border-box", "background: linear-gradient(to right, red, blue); background-origin: border-box", "background-origin: border-box"),
        OriginSwatch("padding-box", "background: linear-gradient(to right, red, blue); background-origin: padding-box", "background-origin: padding-box"),
        OriginSwatch("content-box", "background: linear-gradient(to right, red, blue); background-origin: content-box", "background-origin: content-box")
    ) +

    "<h2>3 — background-origin: Radial Gradient</h2>" +
    "<p class=\"intro\">Radial center and radius are computed from the origin rect. content-box produces a more compressed gradient.</p>" +
    Row(
        OriginSwatch("default (padding-box)", "background: radial-gradient(circle, yellow, navy)"),
        OriginSwatch("border-box", "background: radial-gradient(circle, yellow, navy); background-origin: border-box", "background-origin: border-box"),
        OriginSwatch("padding-box", "background: radial-gradient(circle, yellow, navy); background-origin: padding-box", "background-origin: padding-box"),
        OriginSwatch("content-box", "background: radial-gradient(circle, yellow, navy); background-origin: content-box", "background-origin: content-box")
    ) +

    "<h2>4 — background-clip: Solid Color</h2>" +
    "<p class=\"intro\">background-clip controls where the background is painted. padding-box: no color behind border. content-box: color only in content area.</p>" +
    Row(
        OriginSwatch("default (border-box)", "background-color: coral"),
        OriginSwatch("border-box", "background-color: coral; background-clip: border-box", "background-clip: border-box"),
        OriginSwatch("padding-box", "background-color: coral; background-clip: padding-box", "background-clip: padding-box"),
        OriginSwatch("content-box", "background-color: coral; background-clip: content-box", "background-clip: content-box")
    ) +

    "<h2>5 — background-clip: Linear Gradient</h2>" +
    "<p class=\"intro\">Gradient fills the origin area but is clipped to the clip area.</p>" +
    Row(
        OriginSwatch("default (border-box)", "background: linear-gradient(to right, orange, purple)"),
        OriginSwatch("border-box", "background: linear-gradient(to right, orange, purple); background-clip: border-box", "background-clip: border-box"),
        OriginSwatch("padding-box", "background: linear-gradient(to right, orange, purple); background-clip: padding-box", "background-clip: padding-box"),
        OriginSwatch("content-box", "background: linear-gradient(to right, orange, purple); background-clip: content-box", "background-clip: content-box")
    ) +

    "<h2>6 — background-origin + background-clip: Combinations</h2>" +
    Row(
        OriginSwatch("origin: border, clip: border", "background: linear-gradient(to right, teal, gold); background-origin: border-box; background-clip: border-box", "origin: border-box; clip: border-box"),
        OriginSwatch("origin: padding, clip: padding", "background: linear-gradient(to right, teal, gold); background-origin: padding-box; background-clip: padding-box", "origin: padding-box; clip: padding-box"),
        OriginSwatch("origin: content, clip: content", "background: linear-gradient(to right, teal, gold); background-origin: content-box; background-clip: content-box", "origin: content-box; clip: content-box"),
        OriginSwatch("origin: border, clip: padding", "background: linear-gradient(to right, teal, gold); background-origin: border-box; background-clip: padding-box", "origin: border-box; clip: padding-box")
    ) +

    "<h2>7 — background-origin + background-clip: More Combinations</h2>" +
    Row(
        OriginSwatch("origin: border, clip: content", "background: linear-gradient(to right, crimson, lime); background-origin: border-box; background-clip: content-box", "origin: border-box; clip: content-box"),
        OriginSwatch("origin: padding, clip: content", "background: linear-gradient(to right, crimson, lime); background-origin: padding-box; background-clip: content-box", "origin: padding-box; clip: content-box"),
        OriginSwatch("origin: content, clip: padding", "background: linear-gradient(to right, crimson, lime); background-origin: content-box; background-clip: padding-box", "origin: content-box; clip: padding-box"),
        OriginSwatch("origin: content, clip: border", "background: linear-gradient(to right, crimson, lime); background-origin: content-box; background-clip: border-box", "origin: content-box; clip: border-box")
    ) +

    "<h2>8 — Shorthand with Origin/Clip Tokens</h2>" +
    "<p class=\"intro\">A single box-model keyword in the shorthand sets both origin and clip; two keywords set origin then clip.</p>" +
    Row(
        OriginSwatch("single keyword — padding", "background: steelblue padding-box", "background: steelblue padding-box"),
        OriginSwatch("single keyword — content", "background: coral content-box", "background: coral content-box"),
        OriginSwatch("gradient padding-box content-box", "background: linear-gradient(to right, teal, gold) padding-box content-box", "linear-gradient padding-box content-box"),
        OriginSwatch("gradient border-box padding-box", "background: linear-gradient(to right, teal, gold) border-box padding-box", "linear-gradient border-box padding-box")
    ) +

    "<h2>9 — Radial Gradient + Clip Combinations</h2>" +
    Row(
        OriginSwatch("radial, clip: border-box", "background: radial-gradient(circle at center, yellow, navy); background-clip: border-box", "radial; clip: border-box"),
        OriginSwatch("radial, clip: padding-box", "background: radial-gradient(circle at center, yellow, navy); background-clip: padding-box", "radial; clip: padding-box"),
        OriginSwatch("radial, clip: content-box", "background: radial-gradient(circle at center, yellow, navy); background-clip: content-box", "radial; clip: content-box"),
        OriginSwatch("radial, origin+clip: content", "background: radial-gradient(circle at center, yellow, navy); background-origin: content-box; background-clip: content-box", "origin: content-box; clip: content-box")
    ) +

    "</body></html>";

var originStream = new MemoryStream();
var originDocument = await generator.GeneratePdf(originHtml, pdfConfig);
originDocument.Save(originStream);

File.Delete("test_background_origin_clip.pdf");
File.WriteAllBytes("test_background_origin_clip.pdf", originStream.ToArray());
Console.WriteLine("Saved test_background_origin_clip.pdf");

File.Delete("test_background_origin_clip.html");
File.WriteAllText("test_background_origin_clip.html", originHtml);
Console.WriteLine("Saved test_background_origin_clip.html");

// --- list-style-image showcase ---

static string ListSwatch(string desc, string listCss, string itemLabel = "Item") =>
    "<td>" +
    $"<ul style=\"margin: 0; padding-left: 2em; {listCss}\">" +
    $"<li>{itemLabel} one</li><li>{itemLabel} two</li><li>{itemLabel} three</li>" +
    "</ul>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{listCss}</div>" +
    "</td>";

const string ListCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var listHtml = "<!DOCTYPE html><html><head>" + ListCss + "</head><body>" +

    "<h1>CSS list-style-image Test Page</h1>" +

    "<h2>1 — URL image (graceful fallback when missing)</h2>" +
    "<p class=\"intro\">A missing URL should produce no marker and no crash. The list items are still indented normally.</p>" +
    Row(
        ListSwatch("missing URL", "list-style-image: url('nonexistent.png');"),
        ListSwatch("none (baseline)", "list-style-image: none;"),
        ListSwatch("disc (no image)", "list-style-type: disc;"),
        ListSwatch("decimal (no image)", "list-style-type: decimal;")
    ) +

    "<h2>2 — linear-gradient markers</h2>" +
    "<p class=\"intro\">Each list item marker is a gradient-filled square, sized to the current font height.</p>" +
    Row(
        ListSwatch("to right red→blue", "list-style-image: linear-gradient(to right, red, blue);"),
        ListSwatch("to bottom green→yellow", "list-style-image: linear-gradient(to bottom, green, yellow);"),
        ListSwatch("45deg multi-stop", "list-style-image: linear-gradient(45deg, red, yellow, blue);"),
        ListSwatch("hard-stop stripes", "list-style-image: linear-gradient(to right, red 50%, blue 50%);")
    ) +

    "<h2>3 — radial-gradient markers</h2>" +
    Row(
        ListSwatch("circle center", "list-style-image: radial-gradient(circle, white, navy);"),
        ListSwatch("ellipse default", "list-style-image: radial-gradient(red, blue);"),
        ListSwatch("at 30% 30%", "list-style-image: radial-gradient(circle at 30% 30%, yellow, orange, red);"),
        ListSwatch("closest-side", "list-style-image: radial-gradient(closest-side circle at 50% 50%, lime, teal);")
    ) +

    "<h2>4 — conic-gradient markers</h2>" +
    Row(
        ListSwatch("default sweep", "list-style-image: conic-gradient(red, blue);"),
        ListSwatch("pie chart", "list-style-image: conic-gradient(#e74c3c 0 25%, #3498db 25% 65%, #2ecc71 65% 100%);"),
        ListSwatch("from 90deg", "list-style-image: conic-gradient(from 90deg, red, yellow, blue);"),
        ListSwatch("color wheel", "list-style-image: conic-gradient(red, yellow, lime, cyan, blue, magenta, red);")
    ) +

    "<h2>5 — list-style shorthand with gradient</h2>" +
    "<p class=\"intro\">A single list-style shorthand value setting both position and image.</p>" +
    Row(
        ListSwatch("inside linear", "list-style: inside linear-gradient(to right, purple, orange);"),
        ListSwatch("inside radial", "list-style: inside radial-gradient(circle, gold, crimson);"),
        ListSwatch("inside conic", "list-style: inside conic-gradient(red 0 33%, blue 33% 66%, green 66% 100%);"),
        ListSwatch("outside linear", "list-style: outside linear-gradient(45deg, teal, pink);")
    ) +

    "<h2>6 — Gradient marker alongside typed bullets</h2>" +
    "<p class=\"intro\">Left column uses a gradient list-style-image; right uses list-style-type only. Neither should interfere.</p>" +
    Row(
        ListSwatch("gradient image", "list-style-image: linear-gradient(to right, red, blue);"),
        ListSwatch("disc (no image)", "list-style-type: disc; list-style-image: none;"),
        ListSwatch("circle (no image)", "list-style-type: circle; list-style-image: none;"),
        ListSwatch("square (no image)", "list-style-type: square; list-style-image: none;")
    ) +

    "</body></html>";

var listStream = new MemoryStream();
var listDocument = await generator.GeneratePdf(listHtml, pdfConfig);
listDocument.Save(listStream);

File.Delete("test_list_style_image.pdf");
File.WriteAllBytes("test_list_style_image.pdf", listStream.ToArray());
Console.WriteLine("Saved test_list_style_image.pdf");

File.Delete("test_list_style_image.html");
File.WriteAllText("test_list_style_image.html", listHtml);
Console.WriteLine("Saved test_list_style_image.html");

// --- content image showcase ---

static string ContentSwatch(string desc, string contentValue, string pseudoElement = "before") =>
    "<td>" +
    $"<style>.ci-{pseudoElement}-{desc.GetHashCode() & 0x7FFFFFFF}::{ pseudoElement} {{ content: {contentValue}; display: inline-block; width: 40px; height: 28px; }}</style>" +
    $"<div class=\"ci-{pseudoElement}-{desc.GetHashCode() & 0x7FFFFFFF}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">::{pseudoElement} {{ content: {contentValue} }}</div>" +
    "</td>";

static string ContentSwatchInline(string desc, string pseudoElement, string inlineCss) =>
    "<td>" +
    $"<style>.cci-{desc.GetHashCode() & 0x7FFFFFFF}::{pseudoElement} {{ {inlineCss} }}</style>" +
    $"<p class=\"cci-{desc.GetHashCode() & 0x7FFFFFFF}\">Text</p>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">::{pseudoElement} {{ {inlineCss} }}</div>" +
    "</td>";

const string ContentCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var contentHtml = "<!DOCTYPE html><html><head>" + ContentCss + "</head><body>" +

    "<h1>CSS content Image Test Page</h1>" +
    "<p class=\"intro\">Demonstrates url() and gradient functions in the CSS content property on ::before and ::after pseudo-elements. Image values require display: inline-block with explicit width/height.</p>" +

    "<h2>1 — ::before linear-gradient</h2>" +
    Row(
        ContentSwatch("to right red→blue", "linear-gradient(to right, red, blue)"),
        ContentSwatch("to bottom green→yellow", "linear-gradient(to bottom, green, yellow)"),
        ContentSwatch("45deg multi-stop", "linear-gradient(45deg, red, yellow, blue)"),
        ContentSwatch("hard-stop stripes", "linear-gradient(to right, red 50%, blue 50%)")
    ) +

    "<h2>2 — ::before radial-gradient</h2>" +
    Row(
        ContentSwatch("circle center", "radial-gradient(circle, white, navy)"),
        ContentSwatch("ellipse default", "radial-gradient(red, blue)"),
        ContentSwatch("at 30% 30%", "radial-gradient(circle at 30% 30%, yellow, orange, red)"),
        ContentSwatch("closest-side", "radial-gradient(closest-side circle at 50% 50%, lime, teal)")
    ) +

    "<h2>3 — ::before conic-gradient</h2>" +
    Row(
        ContentSwatch("default sweep", "conic-gradient(red, blue)"),
        ContentSwatch("pie chart", "conic-gradient(#e74c3c 0 25%, #3498db 25% 65%, #2ecc71 65% 100%)"),
        ContentSwatch("from 90deg", "conic-gradient(from 90deg, red, yellow, blue)"),
        ContentSwatch("color wheel", "conic-gradient(red, yellow, lime, cyan, blue, magenta, red)")
    ) +

    "<h2>4 — ::after gradients and URL fallback</h2>" +
    Row(
        ContentSwatch("::after linear", "linear-gradient(to bottom, purple, orange)", "after"),
        ContentSwatch("::after radial", "radial-gradient(circle, gold, crimson)", "after"),
        ContentSwatch("::after conic", "conic-gradient(red 0 33%, blue 33% 66%, green 66% 100%)", "after"),
        ContentSwatch("missing URL (no crash)", "url('nonexistent.png')")
    ) +

    "<h2>5 — Repeating variants</h2>" +
    Row(
        ContentSwatch("repeating-linear", "repeating-linear-gradient(45deg, red, blue 10px)"),
        ContentSwatch("repeating-radial", "repeating-radial-gradient(circle, red, blue 10px)"),
        ContentSwatch("repeating-conic", "repeating-conic-gradient(red 0 10deg, blue 10deg 20deg)"),
        ContentSwatch("linear baseline", "linear-gradient(to right, teal, pink)")
    ) +

    "<h2>6 — Mixed: text content regression and image side-by-side</h2>" +
    "<p class=\"intro\">Text content (string literals, counters) must still work correctly alongside the new image code path.</p>" +
    Row(
        ContentSwatchInline("text bullet", "before", "content: \"• \"; color: red;"),
        ContentSwatchInline("gradient + text", "before", "content: linear-gradient(to right, red, blue); display: inline-block; width: 40px; height: 20px;"),
        ContentSwatchInline("::after text", "after", "content: \" ★\"; color: orange;"),
        ContentSwatchInline("none (no output)", "before", "content: none;")
    ) +

    "</body></html>";

var contentStream = new MemoryStream();
var contentDocument = await generator.GeneratePdf(contentHtml, pdfConfig);
contentDocument.Save(contentStream);

File.Delete("test_content_image.pdf");
File.WriteAllBytes("test_content_image.pdf", contentStream.ToArray());
Console.WriteLine("Saved test_content_image.pdf");

File.Delete("test_content_image.html");
File.WriteAllText("test_content_image.html", contentHtml);
Console.WriteLine("Saved test_content_image.html");

// ─── CSS Paged Media showcase ───────────────────────────────────────────────

var pagedMediaHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: A4 portrait;
      margin: 25mm 20mm 25mm 20mm;
      @top-left   { content: "Acme Corp"; font-size: 8pt; font-family: Arial; color: #555; }
      @top-center { content: "Annual Report 2025"; font-size: 8pt; font-family: Arial; font-weight: bold; }
      @top-right  { content: "Confidential"; font-size: 8pt; font-family: Arial; color: #cc0000; }
      @bottom-left   { content: "\A9 2025 Acme Corp"; font-size: 7pt; font-family: Arial; color: #888; }
      @bottom-center { content: "Page " counter(page) " of " counter(pages); font-size: 8pt; font-family: Arial; }
      @bottom-right  { content: "Internal Use Only"; font-size: 7pt; font-family: Arial; color: #888; }
    }
    @page :first {
      @top-left   { content: none; }
      @top-center { content: none; }
      @top-right  { content: none; }
    }
    body { font: 10pt Arial, sans-serif; margin: 0; }
    h1 { font-size: 28pt; text-align: center; margin: 60pt 0 20pt; }
    h2 { font-size: 16pt; margin: 24pt 0 8pt; border-bottom: 1px solid #999; padding-bottom: 4pt; }
    p  { margin: 0 0 8pt; line-height: 1.5; }
    .cover-subtitle { font-size: 14pt; text-align: center; color: #555; margin-bottom: 60pt; }
    .page-break { page-break-after: always; }
    </style>
    </head>
    <body>

    <!-- Cover page (page :first — no header) -->
    <div class="page-break">
      <h1>Annual Report 2025</h1>
      <p class="cover-subtitle">Acme Corporation — Confidential</p>
      <p style="text-align:center; color:#888; font-size:9pt;">
        This document demonstrates CSS Paged Media support in PeachPDF.<br/>
        The cover page has no running header; pages 2+ show header and footer.
      </p>
    </div>

    <!-- Page 2 -->
    <div class="page-break">
      <h2>Executive Summary</h2>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.</p>
      <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim.</p>
      <p>Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium doloremque laudantium, totam rem aperiam, eaque ipsa quae ab illo inventore veritatis et quasi architecto beatae vitae dicta.</p>
      <p>Nemo enim ipsam voluptatem quia voluptas sit aspernatur aut odit aut fugit, sed quia consequuntur magni dolores eos qui ratione voluptatem sequi nesciunt. Neque porro quisquam est, qui dolorem ipsum.</p>
      <p>At vero eos et accusamus et iusto odio dignissimos ducimus qui blanditiis praesentium voluptatum deleniti atque corrupti quos dolores et quas molestias excepturi sint occaecati cupiditate non provident.</p>
    </div>

    <!-- Page 3 -->
    <div>
      <h2>Financial Highlights</h2>
      <p>Temporibus autem quibusdam et aut officiis debitis aut rerum necessitatibus saepe eveniet ut et voluptates repudiandae sint et molestiae non recusandae.</p>
      <p>Itaque earum rerum hic tenetur a sapiente delectus, ut aut reiciendis voluptatibus maiores alias consequatur aut perferendis doloribus asperiores repellat.</p>
      <p>Nam libero tempore, cum soluta nobis est eligendi optio cumque nihil impedit quo minus id quod maxime placeat facere possimus, omnis voluptas assumenda est, omnis dolor repellendus.</p>
      <p>Quis autem vel eum iure reprehenderit qui in ea voluptate velit esse quam nihil molestiae consequatur, vel illum qui dolorem eum fugiat quo voluptas nulla pariatur.</p>
    </div>

    </body>
    </html>
    """;

var pagedMediaStream = new MemoryStream();
var pagedMediaDocument = await generator.GeneratePdf(pagedMediaHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });
pagedMediaDocument.Save(pagedMediaStream);

File.Delete("test_paged_media.pdf");
File.WriteAllBytes("test_paged_media.pdf", pagedMediaStream.ToArray());
Console.WriteLine("Saved test_paged_media.pdf");

File.Delete("test_paged_media.html");
File.WriteAllText("test_paged_media.html", pagedMediaHtml);
Console.WriteLine("Saved test_paged_media.html");

// ─── CSS Paged Media showcase — named strings (string-set / string()) ──────

var namedStringsHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: A4 portrait;
      margin: 25mm 20mm 25mm 20mm;
      @top-left   { content: string(chapter); font-size: 8pt; font-family: Arial; color: #333; font-style: italic; }
      @top-right  { content: string(section); font-size: 8pt; font-family: Arial; color: #555; }
      @bottom-center { content: "Page " counter(page) " of " counter(pages); font-size: 8pt; font-family: Arial; }
    }
    body { font: 10pt Arial, sans-serif; margin: 0; }
    h1 { font-size: 20pt; margin: 0 0 12pt; string-set: chapter content(); }
    h2 { font-size: 13pt; margin: 18pt 0 6pt; string-set: section content(); }
    p  { margin: 0 0 8pt; line-height: 1.5; }
    .page-break { page-break-after: always; }
    </style>
    </head>
    <body>

    <div class="page-break">
      <h1>Chapter 1: Introduction</h1>
      <h2>1.1 Background</h2>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. String-set captures the heading text and string() displays it in the top margin.</p>
      <p>Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. The chapter title appears top-left; the section title top-right.</p>
      <h2>1.2 Scope</h2>
      <p>Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.</p>
      <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.</p>
    </div>

    <div>
      <h1>Chapter 2: Methodology</h1>
      <h2>2.1 Approach</h2>
      <p>Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.</p>
      <p>At vero eos et accusamus et iusto odio dignissimos ducimus qui blanditiis praesentium voluptatum deleniti.</p>
      <h2>2.2 Results</h2>
      <p>Temporibus autem quibusdam et aut officiis debitis aut rerum necessitatibus saepe eveniet ut et voluptates.</p>
      <p>Nam libero tempore, cum soluta nobis est eligendi optio cumque nihil impedit quo minus id quod maxime.</p>
    </div>

    </body>
    </html>
    """;

var namedStringsStream = new MemoryStream();
var namedStringsDocument = await generator.GeneratePdf(namedStringsHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });
namedStringsDocument.Save(namedStringsStream);

File.Delete("test_paged_media_named_strings.pdf");
File.WriteAllBytes("test_paged_media_named_strings.pdf", namedStringsStream.ToArray());
Console.WriteLine("Saved test_paged_media_named_strings.pdf");

File.Delete("test_paged_media_named_strings.html");
File.WriteAllText("test_paged_media_named_strings.html", namedStringsHtml);
Console.WriteLine("Saved test_paged_media_named_strings.html");

// ── Per-page margin variation showcase ─────────────────────────────────────
var perPageMarginsHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
        size: A4;
        margin: 20mm;
        @top-center { content: "Running Header"; font: bold 9pt Arial; }
        @bottom-center { content: "Page " counter(page) " of " counter(pages); font: 8pt Arial; }
    }
    @page :first {
        margin-top: 50mm;
        @top-center { content: none; }
    }
    body { font: 11pt Arial; }
    h1 { font-size: 22pt; text-align: center; margin-top: 30mm; }
    </style>
    </head>
    <body>
      <h1>Per-Page Margin Variation</h1>
      <p style="text-align:center; font-size: 10pt; color: #555;">Page 1 has 50mm top margin (no header). Pages 2+ have 20mm.</p>
    """ +
    string.Concat(Enumerable.Range(1, 50).Select(i =>
        $"<p>Paragraph {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
        "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>")) +
    """
    </body>
    </html>
    """;

var perPageMarginsStream = new MemoryStream();
var perPageMarginsDoc = await generator.GeneratePdf(perPageMarginsHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });
perPageMarginsDoc.Save(perPageMarginsStream);

File.Delete("test_paged_media_per_page_margins.pdf");
File.WriteAllBytes("test_paged_media_per_page_margins.pdf", perPageMarginsStream.ToArray());
Console.WriteLine("Saved test_paged_media_per_page_margins.pdf");

// ── Named pages showcase ────────────────────────────────────────────────────
var namedPagesHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
        size: A4;
        margin: 20mm;
        @bottom-center { content: "Page " counter(page); font: 8pt Arial; }
    }
    @page chapter {
        @top-right { content: "Chapter Section"; font: italic 8pt Arial; color: #335; }
    }
    body { font: 11pt Arial; }
    h1 { font-size: 18pt; border-bottom: 2pt solid #336; padding-bottom: 4pt; }
    </style>
    </head>
    <body>
    """ +
    string.Concat(Enumerable.Range(1, 3).Select(i =>
        $"<div style=\"page: chapter\">" +
        $"<h1>Chapter {i}: Section Title</h1>" +
        string.Concat(Enumerable.Range(1, 18).Select(j =>
            $"<p>Chapter {i}, paragraph {j}: Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
            "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>")) +
        "</div>")) +
    """
    </body>
    </html>
    """;

var namedPagesStream = new MemoryStream();
var namedPagesDoc = await generator.GeneratePdf(namedPagesHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });
namedPagesDoc.Save(namedPagesStream);

File.Delete("test_paged_media_named_pages.pdf");
File.WriteAllBytes("test_paged_media_named_pages.pdf", namedPagesStream.ToArray());
Console.WriteLine("Saved test_paged_media_named_pages.pdf");

// ── Margin box explicit sizing showcase ────────────────────────────────────
var marginBoxSizingHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
        size: A4;
        margin: 25mm 20mm;
        @top-left   { content: "Narrow Left"; width: 80pt; font: 8pt Arial; }
        @top-center { content: "Wide Center (auto)"; font: bold 8pt Arial; }
        @top-right  { content: "Right"; width: 60pt; font: 8pt Arial; }
        @bottom-left  { content: "© 2025"; font: 7pt Arial; color: #888; }
        @bottom-center { content: "Page " counter(page) " of " counter(pages); font: 8pt Arial; }
        @bottom-right { content: "Confidential"; width: 70pt; font: 7pt Arial; color: #c00; }
    }
    body { font: 11pt Arial; }
    </style>
    </head>
    <body>
      <h1>Margin Box Sizing</h1>
      <p>Top row: left=80pt fixed, center=auto (gets remaining space), right=60pt fixed.</p>
      <p>Bottom row: left and center auto, right=70pt fixed.</p>
    """ +
    string.Concat(Enumerable.Range(1, 30).Select(i =>
        $"<p>Line {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>")) +
    """
    </body>
    </html>
    """;

var marginBoxSizingStream = new MemoryStream();
var marginBoxSizingDoc = await generator.GeneratePdf(marginBoxSizingHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });
marginBoxSizingDoc.Save(marginBoxSizingStream);

File.Delete("test_paged_media_margin_box_sizing.pdf");
File.WriteAllBytes("test_paged_media_margin_box_sizing.pdf", marginBoxSizingStream.ToArray());
Console.WriteLine("Saved test_paged_media_margin_box_sizing.pdf");

// ─── CSS Flexbox showcase ──────────────────────────────────────────────────

static string FItem(string label, string color, string extraCss = "") =>
    $"<div style=\"background:{color};color:#fff;font:bold 7pt Arial;padding:4px 6px;min-width:28px;text-align:center;{extraCss}\">{label}</div>";

static string FContainer(string desc, string containerCss, string itemsHtml) =>
    $"<tr><td style=\"font:7pt Arial;color:#333;padding:2px 4px 2px 0;white-space:nowrap\">{desc}</td>" +
    $"<td style=\"padding:2px\"><div style=\"display:flex;border:1px solid #bbb;background:#f8f8f8;min-height:22px;{containerCss}\">{itemsHtml}</div></td>" +
    $"<td style=\"font:5.5pt Arial;color:#888;padding:2px 4px;word-break:break-all\">{containerCss}</td></tr>";

static string FSection(string title, string rows) =>
    $"<h2>{title}</h2>" +
    "<table style=\"width:100%;border-collapse:collapse;margin-bottom:6px\">" +
    "<col style=\"width:90px\"><col><col style=\"width:100px\">" +
    rows + "</table>";

static string FItems3(string extraCss = "") =>
    FItem("A", "#e74c3c", extraCss) + FItem("B", "#3498db", extraCss) + FItem("C", "#27ae60", extraCss);


const string FlexCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 9pt; margin: 0.7em 0 0.2em; padding-bottom: 2px; border-bottom: 1px solid #ccc; color: #333 }
    </style>
    """;

var flexHtml = "<!DOCTYPE html><html><head>" + FlexCss + "</head><body>" +

    "<h1>CSS Flexbox Test Page</h1>" +

    FSection("1 — flex-direction",
        FContainer("row (default)", "flex-direction:row;gap:4px;", FItems3()) +
        FContainer("row-reverse", "flex-direction:row-reverse;gap:4px;", FItems3()) +
        FContainer("column", "flex-direction:column;gap:2px;width:60px;", FItems3()) +
        FContainer("column-reverse", "flex-direction:column-reverse;gap:2px;width:60px;", FItems3())
    ) +

    FSection("2 — justify-content (row, 240px container)",
        FContainer("flex-start", "justify-content:flex-start;width:240px;gap:4px;",   FItems3("width:50px;")) +
        FContainer("center",     "justify-content:center;width:240px;gap:4px;",        FItems3("width:50px;")) +
        FContainer("flex-end",   "justify-content:flex-end;width:240px;gap:4px;",      FItems3("width:50px;")) +
        FContainer("space-between","justify-content:space-between;width:240px;",       FItems3("width:50px;")) +
        FContainer("space-around", "justify-content:space-around;width:240px;",        FItems3("width:50px;")) +
        FContainer("space-evenly", "justify-content:space-evenly;width:240px;",        FItems3("width:50px;"))
    ) +

    FSection("3 — align-items (row, 80px container height)",
        FContainer("flex-start", "align-items:flex-start;height:80px;gap:4px;",  FItems3("width:50px;height:28px;")) +
        FContainer("center",     "align-items:center;height:80px;gap:4px;",       FItems3("width:50px;height:28px;")) +
        FContainer("flex-end",   "align-items:flex-end;height:80px;gap:4px;",     FItems3("width:50px;height:28px;")) +
        FContainer("stretch",    "align-items:stretch;height:80px;gap:4px;",      FItems3("width:50px;")) +
        FContainer("baseline (varying font sizes)", "align-items:baseline;gap:4px;",
            FItem("A", "#e74c3c", "font-size:8pt;width:50px;") +
            FItem("B", "#3498db", "font-size:16pt;width:50px;") +
            FItem("C", "#27ae60", "font-size:24pt;width:50px;"))
    ) +

    FSection("4 — flex-grow",
        FContainer("grow:0,0,0 (none)", "gap:4px;",
            FItem("A grow:0", "#e74c3c", "flex-grow:0;width:50px;") +
            FItem("B grow:0", "#3498db", "flex-grow:0;width:50px;") +
            FItem("C grow:0", "#27ae60", "flex-grow:0;width:50px;")) +
        FContainer("grow:1,1,1 (equal)", "gap:4px;",
            FItem("A 1", "#e74c3c", "flex-grow:1;") +
            FItem("B 1", "#3498db", "flex-grow:1;") +
            FItem("C 1", "#27ae60", "flex-grow:1;")) +
        FContainer("grow:1,2,3 (ratio)", "gap:4px;",
            FItem("A 1", "#e74c3c", "flex-grow:1;") +
            FItem("B 2", "#3498db", "flex-grow:2;") +
            FItem("C 3", "#27ae60", "flex-grow:3;"))
    ) +

    FSection("5 — flex-shrink &amp; flex-basis",
        FContainer("shrink:1,1 basis:120px (overflows 240px)", "width:240px;",
            FItem("A 120", "#e74c3c", "flex-basis:120px;flex-shrink:1;") +
            FItem("B 120", "#3498db", "flex-basis:120px;flex-shrink:1;") +
            FItem("C 120", "#27ae60", "flex-basis:120px;flex-shrink:1;")) +
        FContainer("shrink:1,0 (C does not shrink)", "width:240px;",
            FItem("A shr:1", "#e74c3c", "flex-basis:120px;flex-shrink:1;") +
            FItem("B shr:1", "#3498db", "flex-basis:120px;flex-shrink:1;") +
            FItem("C shr:0", "#27ae60", "flex-basis:80px;flex-shrink:0;")) +
        FContainer("flex:1 shorthand", "gap:4px;",
            FItem("flex:1", "#e74c3c", "flex:1;") +
            FItem("flex:2", "#3498db", "flex:2;") +
            FItem("flex:none", "#27ae60", "flex:none;width:50px;"))
    ) +

    FSection("6 — flex-wrap",
        FContainer("nowrap (overflow)", "flex-wrap:nowrap;width:200px;",
            FItems3("width:90px;")) +
        FContainer("wrap", "flex-wrap:wrap;width:200px;gap:4px;",
            FItems3("width:90px;height:24px;")) +
        FContainer("wrap-reverse", "flex-wrap:wrap-reverse;width:200px;gap:4px;",
            FItems3("width:90px;height:24px;"))
    ) +

    FSection("7 — align-self (overrides align-items)",
        FContainer("align-items:flex-start, item B → flex-end",
            "align-items:flex-start;height:80px;gap:4px;",
            FItem("A start", "#e74c3c", "width:50px;height:28px;") +
            FItem("B end", "#3498db", "width:50px;height:28px;align-self:flex-end;") +
            FItem("C center", "#27ae60", "width:50px;height:28px;align-self:center;"))
    ) +

    FSection("8 — order",
        FContainer("DOM order: A B C (order: 3 1 2)", "gap:4px;",
            FItem("A order:3", "#e74c3c", "order:3;width:50px;") +
            FItem("B order:1", "#3498db", "order:1;width:50px;") +
            FItem("C order:2", "#27ae60", "order:2;width:50px;"))
    ) +

    FSection("9 — Nested flex containers",
        "<tr><td colspan='3' style='padding:2px'>" +
        "<div style='display:flex;gap:8px;'>" +
        "  <div style='display:flex;flex-direction:column;gap:4px;flex:1;border:1px solid #bbb;padding:4px;background:#f8f8f8;'>" +
        "    <div style='background:#e74c3c;color:#fff;font:7pt Arial;padding:3px;text-align:center'>Col A row 1</div>" +
        "    <div style='background:#c0392b;color:#fff;font:7pt Arial;padding:3px;text-align:center'>Col A row 2</div>" +
        "  </div>" +
        "  <div style='display:flex;flex-direction:column;gap:4px;flex:2;border:1px solid #bbb;padding:4px;background:#f8f8f8;'>" +
        "    <div style='display:flex;gap:4px;'>" +
        "      <div style='background:#3498db;color:#fff;font:7pt Arial;padding:3px;flex:1;text-align:center'>B1</div>" +
        "      <div style='background:#2980b9;color:#fff;font:7pt Arial;padding:3px;flex:1;text-align:center'>B2</div>" +
        "    </div>" +
        "    <div style='background:#1abc9c;color:#fff;font:7pt Arial;padding:3px;text-align:center'>Col B row 2 (full width)</div>" +
        "  </div>" +
        "</div>" +
        "</td></tr>"
    ) +

    FSection("10 — inline-flex",
        "<tr><td colspan='3' style='padding:2px;font:8pt Arial'>" +
        "Text before " +
        "<span style='display:inline-flex;gap:3px;vertical-align:middle;border:1px solid #bbb;padding:2px;'>" +
        "  <span style='background:#e74c3c;color:#fff;font:6pt Arial;padding:2px 4px;'>R</span>" +
        "  <span style='background:#3498db;color:#fff;font:6pt Arial;padding:2px 4px;'>G</span>" +
        "  <span style='background:#27ae60;color:#fff;font:6pt Arial;padding:2px 4px;'>B</span>" +
        "</span>" +
        " text after — inline-flex sits in the text flow." +
        "</td></tr>"
    ) +

    FSection("11 — max-width / max-height clamping",
        FContainer("A max-width:80px, both grow:1", "gap:4px;",
            FItem("A", "#e74c3c", "flex-grow:1;max-width:80px;") +
            FItem("B", "#3498db", "flex-grow:1;")) +
        FContainer("column: A max-height:50px, both grow:1 (160px tall)", "flex-direction:column;height:160px;gap:4px;width:80px;",
            FItem("A", "#e74c3c", "flex-grow:1;max-height:50px;") +
            FItem("B", "#3498db", "flex-grow:1;"))
    ) +

    FSection("12 — Percentage flex-basis",
        FContainer("flex-basis:50% (240px container)", "width:240px;",
            FItem("A 50%", "#e74c3c", "flex-basis:50%;flex-grow:0;flex-shrink:0;")) +
        FContainer("column, auto height: flex-basis:50% falls back to content", "flex-direction:column;width:80px;",
            FItem("A 50%", "#e74c3c", "flex-basis:50%;"))
    ) +

    FSection("13 — flex-basis: content",
        FContainer("flex-basis:content ignores explicit width:150px", "gap:4px;",
            FItem("Hi", "#e74c3c", "flex-basis:content;width:150px;flex-grow:0;flex-shrink:0;"))
    ) +

    FSection("14 — Auto margins (main axis)",
        FContainer("margin-left:auto pushes item to the end", "width:240px;",
            FItem("A", "#e74c3c", "margin-left:auto;width:50px;")) +
        FContainer("margin:0 auto centers a single item", "width:240px;",
            FItem("A", "#e74c3c", "margin:0 auto;width:50px;")) +
        FContainer("second item margin-left:auto pushes items apart", "width:240px;",
            FItem("A", "#e74c3c", "width:50px;") +
            FItem("B", "#3498db", "margin-left:auto;width:50px;"))
    ) +

    "</body></html>";

var flexStream = new MemoryStream();
var flexDocument = await generator.GeneratePdf(flexHtml, pdfConfig);
flexDocument.Save(flexStream);

File.Delete("test_flexbox.pdf");
File.WriteAllBytes("test_flexbox.pdf", flexStream.ToArray());
Console.WriteLine("Saved test_flexbox.pdf");

File.Delete("test_flexbox.html");
File.WriteAllText("test_flexbox.html", flexHtml);
Console.WriteLine("Saved test_flexbox.html");

// ─── CSS Custom Properties (var()) showcase ─────────────────────────────────

static string VarSwatch(string desc, string boxCss, string valueLabel, string cssLabel) =>
    "<td>" +
    $"<div class=\"vbox\" style=\"{boxCss}\">Aa</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"val\">{valueLabel}</div>" +
    $"<div class=\"css\">{cssLabel}</div>" +
    "</td>";

const string VarCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .vbox { height: 44px; display: flex; align-items: center; justify-content: center; font: bold 9pt Arial; margin-bottom: 3px }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .val { font-size: 6pt; font-family: monospace; color: #b8860b; font-weight: bold; margin-bottom: 2px; word-break: break-all }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all }
    .card { border-radius: 8px; padding: 14px }
    .card h3 { margin: 0 0 6px; font-size: 11pt }
    .card p { margin: 0 0 8px; font-size: 8pt; line-height: 1.4 }
    .card button { border: none; border-radius: 4px; padding: 6px 14px; font: bold 8pt Arial }
    .cardval { font-size: 6pt; font-family: monospace; color: #b8860b; font-weight: bold; margin-bottom: 2px }
    </style>
    """;

var varHtml = "<!DOCTYPE html><html><head>" + VarCss + "</head><body>" +

    "<h1>CSS Custom Properties &amp; var() Test Page</h1>" +

    "<h2>1 — Basic Declaration &amp; Usage</h2>" +
    "<p class=\"intro\">--main-bg, --main-color and --main-border are declared once on the surrounding container and consumed via var() on each box.</p>" +
    "<div style=\"--main-bg: #2c3e50; --main-color: white; --main-border: 3px solid #1a252f;\">" +
    Row(
        VarSwatch("background + color + border", "background: var(--main-bg); color: var(--main-color); border: var(--main-border);", "--main-bg: #2c3e50 · --main-color: white · --main-border: 3px solid #1a252f", "background: var(--main-bg); color: var(--main-color); border: var(--main-border)"),
        VarSwatch("reused for border only", "border: var(--main-border); color: var(--main-bg); background: white;", "--main-border: 3px solid #1a252f", "border: var(--main-border)"),
        VarSwatch("reused for text color", "color: var(--main-bg); border: 1px solid #ccc; background: white;", "--main-bg: #2c3e50", "color: var(--main-bg)"),
        VarSwatch("literal (no var, for comparison)", "background: #2c3e50; color: white; border: 3px solid #1a252f;", "(no custom property used)", "background: #2c3e50 (literal)")
    ) +
    "</div>" +

    "<h2>2 — Fallback Values</h2>" +
    "<p class=\"intro\">Each box references a custom property that was never declared; the fallback (second argument to var()) is used instead.</p>" +
    Row(
        VarSwatch("color fallback", "background: var(--undefined-bg, #8e44ad); color: white;", "--undefined-bg: (not declared) → fallback #8e44ad", "background: var(--undefined-bg, #8e44ad)"),
        VarSwatch("length fallback", "background: #16a085; color: white; padding: var(--undefined-padding, 16px);", "--undefined-padding: (not declared) → fallback 16px", "padding: var(--undefined-padding, 16px)"),
        VarSwatch("nested fallback chain", "background: var(--undefined-a, var(--undefined-b, #d35400)); color: white;", "--undefined-a, --undefined-b: (not declared) → fallback #d35400", "var(--a, var(--b, #d35400))"),
        VarSwatch("no fallback (uses initial)", "background: var(--totally-undefined); border: 1px dashed #999;", "--totally-undefined: (not declared) → initial value", "background: var(--totally-undefined)")
    ) +

    "<h2>3 — Inheritance &amp; Local Override</h2>" +
    "<p class=\"intro\">--accent is declared once on the outer container. The second box overrides it locally; the override does not leak to its siblings.</p>" +
    "<div style=\"--accent: #2980b9;\">" +
    Row(
        VarSwatch("inherited (no override)", "background: var(--accent); color: white;", "--accent: #2980b9 (inherited)", "background: var(--accent) → inherited"),
        VarSwatch("local override", "--accent: #c0392b; background: var(--accent); color: white;", "--accent: #c0392b (local override)", "--accent: #c0392b (local)"),
        VarSwatch("sibling still inherits original", "background: var(--accent); color: white;", "--accent: #2980b9 (inherited, unaffected)", "background: var(--accent) → unaffected"),
        VarSwatch("--accent: unset (still inherits)", "--accent: unset; background: var(--accent); color: white;", "--accent: #2980b9 (via unset → inherit)", "--accent: unset; background: var(--accent)")
    ) +
    "</div>" +

    "<h2>4 — Cyclic References Resolve Safely</h2>" +
    "<p class=\"intro\">Per spec, a custom property that references itself (directly or through a chain) becomes invalid instead of looping forever; a fallback or the property's initial value is used.</p>" +
    Row(
        VarSwatch("direct cycle, with fallback", "--loop-a: var(--loop-b); --loop-b: var(--loop-a); background: var(--loop-a, #7f8c8d); color: white;", "--loop-a ↔ --loop-b: cyclic → invalid → fallback #7f8c8d", "--loop-a: var(--loop-b); --loop-b: var(--loop-a); background: var(--loop-a, #7f8c8d)"),
        VarSwatch("self-reference — always invalid", "--self: var(--self, #e74c3c); background: var(--self); border: 1px dashed #999;", "--self: self-referential → invalid (the fallback inside --self's OWN definition does not rescue it — matches Chrome/Firefox)", "--self: var(--self, #e74c3c); background: var(--self) (no fallback here)"),
        VarSwatch("one-directional chain (not cyclic)", "--chain-a: var(--chain-b); --chain-b: #27ae60; background: var(--chain-a); color: white;", "--chain-a → --chain-b: #27ae60 (resolved, not cyclic)", "--a: var(--b); --b: #27ae60 → resolves"),
        VarSwatch("multi-hop chain", "--x1: var(--x2); --x2: var(--x3); --x3: #f39c12; background: var(--x1); color: white;", "--x1 → --x2 → --x3: #f39c12 (resolved)", "--x1→--x2→--x3: #f39c12")
    ) +

    "<h2>5 — Real-World Example: Themeable Card Component</h2>" +
    "<p class=\"intro\">The same card markup and CSS rules render two different themes purely by changing custom property values on the wrapping element — no duplicated rules.</p>" +
    """
    <style>
    .card {
      --card-bg: white;
      --card-fg: #222;
      --card-accent: #2c3e50;
      --card-muted: #666;
      background: var(--card-bg);
      color: var(--card-fg);
      border: 1px solid var(--card-accent);
    }
    .card h3 { color: var(--card-accent); }
    .card p { color: var(--card-muted); }
    .card button { background: var(--card-accent); color: var(--card-bg); }
    </style>
    """ +
    "<table class=\"sw\"><tr>" +
    "<td style=\"width:50%\">" +
        "<div class=\"card\">" +
            "<h3>Light Theme</h3>" +
            "<p>This card uses the component's default custom property values.</p>" +
            "<button>Learn More</button>" +
        "</div>" +
        "<div class=\"cardval\">--card-bg: white · --card-fg: #222 · --card-accent: #2c3e50 · --card-muted: #666 (defaults)</div>" +
    "</td>" +
    "<td style=\"width:50%\">" +
        "<div class=\"card\" style=\"--card-bg: #1a1a2e; --card-fg: #eee; --card-accent: #e94560; --card-muted: #aaa;\">" +
            "<h3>Dark Theme</h3>" +
            "<p>Same markup and rules — only the custom property values differ.</p>" +
            "<button>Learn More</button>" +
        "</div>" +
        "<div class=\"cardval\">--card-bg: #1a1a2e · --card-fg: #eee · --card-accent: #e94560 · --card-muted: #aaa (overridden inline)</div>" +
    "</td>" +
    "</tr></table>" +

    "</body></html>";

var varStream = new MemoryStream();
var varDocument = await generator.GeneratePdf(varHtml, pdfConfig);
varDocument.Save(varStream);

File.Delete("test_custom_properties.pdf");
File.WriteAllBytes("test_custom_properties.pdf", varStream.ToArray());
Console.WriteLine("Saved test_custom_properties.pdf");

File.Delete("test_custom_properties.html");
File.WriteAllText("test_custom_properties.html", varHtml);
Console.WriteLine("Saved test_custom_properties.html");

// --- CSS transform / transform-origin showcase ---

static string TransformSwatch(string desc, string transformCss, string extraCss = "") =>
    "<td>" +
    $"<div class=\"tbox\" style=\"transform: {transformCss};{extraCss}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">transform: {transformCss}</div>" +
    "</td>";

const string TransformCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    p.intro { margin: 0 0 0.7em; color: #555 }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; padding-bottom: 150px; vertical-align: top; width: 25%; text-align: center }
    .tbox { width: 100px; height: 56px; background: steelblue; border: 2px solid #1a6b8a; margin: 75px auto 3px }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var transformHtml = "<!DOCTYPE html><html><head>" + TransformCss + "</head><body>" +

    "<h1>CSS transform / transform-origin Test Page</h1>" +

    "<h2>1 — Individual 2D Functions</h2>" +
    Row(
        TransformSwatch("translate", "translate(20px, 10px)"),
        TransformSwatch("scale", "scale(1.4)"),
        TransformSwatch("rotate", "rotate(25deg)"),
        TransformSwatch("skew", "skew(15deg, 5deg)")
    ) +

    "<h2>2 — Composition Order Matters</h2>" +
    "<p class=\"intro\">The same two functions, written in opposite order, produce visibly different results — the last-written function is applied first (closest to the element), the first-written function is applied last.</p>" +
    Row(
        TransformSwatch("translate then rotate — spins in place, then shifts", "translate(30px, 0) rotate(45deg)"),
        TransformSwatch("rotate then translate — orbits around the origin", "rotate(45deg) translate(30px, 0)"),
        TransformSwatch("scale then translate", "scale(1.3) translate(15px, 0)"),
        TransformSwatch("translate then scale", "translate(15px, 0) scale(1.3)")
    ) +

    "<h2>3 — transform-origin Pivot Point</h2>" +
    "<p class=\"intro\">The same rotate(45deg) pivoting around different origins.</p>" +
    Row(
        TransformSwatch("origin: center (default)", "rotate(45deg)"),
        TransformSwatch("origin: top left", "rotate(45deg)", "transform-origin: 0 0;"),
        TransformSwatch("origin: bottom right", "rotate(45deg)", "transform-origin: 100% 100%;"),
        TransformSwatch("origin: 25% 75%", "rotate(45deg)", "transform-origin: 25% 75%;")
    ) +

    "<h2>4 — matrix() Passthrough</h2>" +
    Row(
        TransformSwatch("identity matrix", "matrix(1, 0, 0, 1, 0, 0)"),
        TransformSwatch("translate via matrix", "matrix(1, 0, 0, 1, 20, 10)"),
        TransformSwatch("scale via matrix", "matrix(1.3, 0, 0, 1.3, 0, 0)"),
        TransformSwatch("skew via matrix", "matrix(1, 0.3, 0, 1, 0, 0)")
    ) +

    "<h2>5 — 3D Rotations (no perspective)</h2>" +
    "<p class=\"intro\">3D rotations project onto the flat page as an axis-aligned foreshortening (narrower/shorter), with no vanishing point - PeachPDF does not support perspective(), so there's never a tapered/trapezoidal look.</p>" +
    Row(
        TransformSwatch("rotateX(50deg)", "rotateX(50deg)"),
        TransformSwatch("rotateY(50deg)", "rotateY(50deg)"),
        TransformSwatch("rotate3d(1,1,0,45deg)", "rotate3d(1, 1, 0, 45deg)"),
        TransformSwatch("translateZ (no visible effect)", "translateZ(300px)")
    ) +

    "<h2>6 — Combined With Other Features</h2>" +
    Row(
        TransformSwatch("+ border-radius", "rotate(15deg)", "border-radius: 12px;"),
        TransformSwatch("+ gradient background", "rotate(-10deg) scale(1.2)", "background: linear-gradient(to right, #e74c3c, #3498db);"),
        "<td>" +
            "<div class=\"tbox\" style=\"transform: rotate(12deg); color: white; font-size: 7pt;\">Hello</div>" +
            "<div class=\"desc\">+ text content (whole subtree transforms)</div>" +
            "<div class=\"css\">transform: rotate(12deg)</div>" +
        "</td>",
        TransformSwatch("multi-function chain", "translate(10px, 0) rotate(20deg) scale(1.2)")
    ) +

    "</body></html>";

var transformStream = new MemoryStream();
var transformDocument = await generator.GeneratePdf(transformHtml, pdfConfig);
transformDocument.Save(transformStream);

File.Delete("test_transform.pdf");
File.WriteAllBytes("test_transform.pdf", transformStream.ToArray());
Console.WriteLine("Saved test_transform.pdf");

File.Delete("test_transform.html");
File.WriteAllText("test_transform.html", transformHtml);
Console.WriteLine("Saved test_transform.html");

// --- CSS calc() / min() / max() / clamp() showcase ---

static string CalcSwatch(string desc, string css) =>
    "<td>" +
    $"<div class=\"cbox\" style=\"{css}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{css}</div>" +
    "</td>";

const string CalcCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    p.intro { margin: 0 0 0.7em; color: #555 }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25%; text-align: center }
    .cbox { height: 40px; background: steelblue; border: 2px solid #1a6b8a; margin: 0 auto 3px }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    .wrap-200 { width: 200px; border: 1px dashed #999; margin: 0 auto; padding: 4px }
    </style>
    """;

var calcHtml = "<!DOCTYPE html><html><head>" + CalcCss + "</head><body>" +

    "<h1>CSS calc() / min() / max() / clamp() Test Page</h1>" +

    "<h2>1 — Basic Arithmetic</h2>" +
    Row(
        CalcSwatch("addition", "width: calc(100px + 40px);"),
        CalcSwatch("subtraction", "width: calc(200px - 60px);"),
        CalcSwatch("multiplication", "width: calc(20px * 4);"),
        CalcSwatch("division", "width: calc(200px / 4);")
    ) +

    "<h2>2 — Mixed Units &amp; Percentages</h2>" +
    "<p class=\"intro\">calc(1em + 5px) resolves the em against the element's own font-size; calc(100% - 40px) resolves the percentage against the 200px dashed container below.</p>" +
    Row(
        "<td>" +
            "<div class=\"wrap-200\"><div class=\"cbox\" style=\"font-size: 16px; width: calc(1em + 5px);\"></div></div>" +
            "<div class=\"desc\">1em + 5px @ 16px font</div>" +
            "<div class=\"css\">width: calc(1em + 5px)</div>" +
        "</td>",
        "<td>" +
            "<div class=\"wrap-200\"><div class=\"cbox\" style=\"width: calc(100% - 40px);\"></div></div>" +
            "<div class=\"desc\">100% - 40px in a 200px container</div>" +
            "<div class=\"css\">width: calc(100% - 40px)</div>" +
        "</td>"
    ) +

    "<h2>3 — Nested calc() and Parentheses</h2>" +
    Row(
        CalcSwatch("nested calc()", "width: calc(calc(50px + 50px) * 2);"),
        CalcSwatch("parenthesized grouping", "width: calc((50px + 50px) * 2);")
    ) +

    "<h2>4 — margin / padding / border-radius / height</h2>" +
    Row(
        CalcSwatch("margin-left", "margin-left: calc(20px + 10px); width: 60px;"),
        CalcSwatch("padding (widens box)", "padding: calc(5px + 5px); width: 60px;"),
        CalcSwatch("border-radius", "border-radius: calc(10px + 10px); width: 80px;"),
        CalcSwatch("height", "height: calc(20px + 20px); width: 80px;")
    ) +

    "<h2>5 — Negative Result</h2>" +
    "<p class=\"intro\">PeachPDF doesn't clamp a negative calc() result to zero, matching how a plain negative length is already handled.</p>" +
    Row(
        CalcSwatch("calc(50px - 100px)", "width: calc(50px - 100px); height: 20px; border: 1px dashed red;")
    ) +

    "<h2>6 — min() / max() / clamp()</h2>" +
    Row(
        CalcSwatch("min(150px, 100px)", "width: min(150px, 100px);"),
        CalcSwatch("max(150px, 100px)", "width: max(150px, 100px);"),
        CalcSwatch("clamp(50px, 300px, 150px)", "width: clamp(50px, 300px, 150px);"),
        CalcSwatch("clamp(50px, 10px, 150px)", "width: clamp(50px, 10px, 150px);")
    ) +

    "<h2>7 — calc() Combined With a Custom Property</h2>" +
    Row(
        "<td>" +
            "<div class=\"wrap-200\"><div class=\"cbox\" style=\"--gap: 20px; width: calc(100% - var(--gap));\"></div></div>" +
            "<div class=\"desc\">calc() referencing a custom property</div>" +
            "<div class=\"css\">width: calc(100% - var(--gap))</div>" +
        "</td>"
    ) +

    "</body></html>";

var calcStream = new MemoryStream();
var calcDocument = await generator.GeneratePdf(calcHtml, pdfConfig);
calcDocument.Save(calcStream);

File.Delete("test_calc.pdf");
File.WriteAllBytes("test_calc.pdf", calcStream.ToArray());
Console.WriteLine("Saved test_calc.pdf");

File.Delete("test_calc.html");
File.WriteAllText("test_calc.html", calcHtml);
Console.WriteLine("Saved test_calc.html");

// ─── SVG showcase ────────────────────────────────────────────────────────────

static string SvgSwatch(string desc, string svgMarkup, string label) =>
    "<td>" +
    $"<div class=\"sbox\">{svgMarkup}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{label}</div>" +
    "</td>";

static string PeachSwatch(string title, string svgMarkup, string blurb) =>
    "<td style=\"width:50%\">" +
    "<div style=\"page-break-inside:avoid\">" +
    $"<div class=\"peach-box\">{svgMarkup}</div>" +
    $"<h3 style=\"margin:4px 0 2px;font-size:9pt;text-align:center\">{title}</h3>" +
    $"<p style=\"margin:0;font-size:7pt;color:#666;text-align:center\">{blurb}</p>" +
    "</div>" +
    "</td>";

const string SvgShowcaseCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25%; text-align: center }
    .sbox { height: 90px; border: 1px solid #ccc; margin-bottom: 3px; display: flex; align-items: center; justify-content: center; background: #fafafa }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all; font-family: monospace }
    .peach-box { border: 1px solid #e0c9a6; margin-bottom: 4px; background: #fffdf7; text-align: center; padding: 10px 0 }
    </style>
    """;

// Shared point lists (5-point star / pentagon) reused across several swatches.
const string StarPoints = "50,12 58.8,37.9 86.1,38.3 64.3,54.6 72.3,80.7 50,65 27.7,80.7 35.7,54.6 13.9,38.3 41.2,37.9";
const string PentagonPoints = "50,12 86,38 72,81 28,81 14,38";

// Shared between the "inline <svg>" and "<img src=data:...>" swatches below, to prove both
// rendering paths take the exact same markup. Namespaces are declared (harmless for the inline
// case, which doesn't need them) because the <img> path parses this as standalone XML via
// XDocument, which requires the "xlink" prefix to be declared before xlink:href can be used.
var parityMarkup =
    $"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 100 100" width="80" height="80"><defs><polygon id="parityStar" points="{StarPoints}"/><linearGradient id="lgParity" gradientUnits="userSpaceOnUse" x1="10" y1="10" x2="90" y2="90"><stop offset="0" stop-color="#a1c4fd"/><stop offset="1" stop-color="#c2e9fb"/></linearGradient><clipPath id="clipParity"><use xlink:href="#parityStar"/></clipPath></defs><g clip-path="url(#clipParity)"><polygon points="0,0 100,0 100,100 0,100" fill="url(#lgParity)"/></g></svg>""";
var parityDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(parityMarkup));

var svgHtml = "<!DOCTYPE html><html><head>" + SvgShowcaseCss + "</head><body>" +

    "<h1>SVG Test Page</h1>" +
    "<p class=\"intro\">PeachPDF renders SVG through its own vector scene graph, reusing the same PDF path/fill/stroke/gradient/clip primitives already used for CSS backgrounds and borders — SVG content is never rasterized to a bitmap. Supported elements: svg, g, path (M/L/H/V/C/S/Q/T/A/Z), circle, polygon, use, defs, linearGradient/radialGradient/stop, clipPath. Not yet supported: rect, line, ellipse, polyline, text, pattern, mask, filter, and rotate()/skew() transforms.</p>" +

    "<h2>1 — Path Primitives: Lines, Curves &amp; Arcs</h2>" +
    Row(
        SvgSwatch("straight lines (M/L/Z)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M50,10 L90,90 L10,90 Z" fill="#3498db"/></svg>""",
            "path d=\"M50,10 L90,90 L10,90 Z\""),
        SvgSwatch("cubic Bézier (C)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M10,80 C10,20 90,20 90,80" fill="none" stroke="#e74c3c" stroke-width="6"/></svg>""",
            "path d=\"M10,80 C10,20 90,20 90,80\""),
        SvgSwatch("elliptical arc (A)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M10,60 A40,40 0 0 1 90,60" fill="none" stroke="#27ae60" stroke-width="6"/></svg>""",
            "path d=\"M10,60 A40,40 0 0 1 90,60\""),
        SvgSwatch("multiple subpaths",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M25,10 L40,25 L25,40 L10,25 Z M75,60 L90,75 L75,90 L60,75 Z" fill="#9b59b6"/></svg>""",
            "one path, two \"M...Z\" subpaths")
    ) +

    "<h2>2 — circle &amp; polygon</h2>" +
    Row(
        SvgSwatch("circle",
            """<svg viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35" fill="#f39c12"/></svg>""",
            "circle cx=50 cy=50 r=35"),
        SvgSwatch("polygon (pentagon)",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><polygon points="{PentagonPoints}" fill="#2ecc71"/></svg>""",
            "polygon points=\"...\""),
        SvgSwatch("overlapping circles + opacity",
            """<svg viewBox="0 0 100 100" width="80" height="80"><circle cx="38" cy="50" r="30" fill="#3498db" opacity="0.7"/><circle cx="62" cy="50" r="30" fill="#e74c3c" opacity="0.7"/></svg>""",
            "two circles, opacity=\"0.7\" each"),
        SvgSwatch("polygon (5-point star)",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><polygon points="{StarPoints}" fill="#f1c40f"/></svg>""",
            "polygon points=\"...\"")
    ) +

    "<h2>3 — Linear &amp; Radial Gradients</h2>" +
    Row(
        SvgSwatch("linearGradient, 2 stops",
            """<svg viewBox="0 0 100 100" width="80" height="60"><defs><linearGradient id="lg1" gradientUnits="userSpaceOnUse" x1="10" y1="50" x2="90" y2="50"><stop offset="0" stop-color="#ff5f6d"/><stop offset="1" stop-color="#ffc371"/></linearGradient></defs><polygon points="10,20 90,20 90,80 10,80" fill="url(#lg1)"/></svg>""",
            "linearGradient x1/y1/x2/y2, 2 stops"),
        SvgSwatch("linearGradient, 3 stops",
            """<svg viewBox="0 0 100 100" width="80" height="60"><defs><linearGradient id="lg2" gradientUnits="userSpaceOnUse" x1="10" y1="10" x2="90" y2="90"><stop offset="0" stop-color="#00c6ff"/><stop offset="0.5" stop-color="#8e54e9"/><stop offset="1" stop-color="#eb3941"/></linearGradient></defs><polygon points="10,20 90,20 90,80 10,80" fill="url(#lg2)"/></svg>""",
            "diagonal, 3 stops"),
        SvgSwatch("radialGradient, centered",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><radialGradient id="rg1" gradientUnits="userSpaceOnUse" cx="50" cy="50" r="40"><stop offset="0" stop-color="#fff9c4"/><stop offset="1" stop-color="#f57f17"/></radialGradient></defs><circle cx="50" cy="50" r="40" fill="url(#rg1)"/></svg>""",
            "radialGradient cx/cy/r"),
        SvgSwatch("radialGradient + gradientTransform",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><radialGradient id="rg2" gradientUnits="userSpaceOnUse" cx="50" cy="50" r="40" gradientTransform="matrix(1 0 0 0.5 0 25)"><stop offset="0" stop-color="#e0f7fa"/><stop offset="1" stop-color="#006064"/></radialGradient></defs><circle cx="50" cy="50" r="40" fill="url(#rg2)"/></svg>""",
            "gradientTransform squishes the radial into an ellipse")
    ) +

    "<h2>4 — Stroke Properties</h2>" +
    Row(
        SvgSwatch("stroke only, default miterlimit",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M20,80 L50,15 L80,80 Z" fill="none" stroke="#2c3e50" stroke-width="4" stroke-miterlimit="10"/></svg>""",
            "stroke-width=4 stroke-miterlimit=10"),
        SvgSwatch("thick stroke, low miterlimit",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M20,80 L50,15 L80,80 Z" fill="none" stroke="#2c3e50" stroke-width="10" stroke-miterlimit="1"/></svg>""",
            "stroke-width=10 stroke-miterlimit=1"),
        SvgSwatch("fill + stroke combined",
            """<svg viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35" fill="#1abc9c" stroke="#0e6655" stroke-width="6"/></svg>""",
            "fill and stroke on the same shape"),
        SvgSwatch("stroke-only star",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><polygon points="{StarPoints}" fill="none" stroke="#c0392b" stroke-width="3"/></svg>""",
            "fill=\"none\" stroke=\"#c0392b\"")
    ) +

    "<h2>5 — Group Opacity &amp; Transforms</h2>" +
    Row(
        SvgSwatch("nested group opacity (0.7 × 0.7)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><polygon points="10,10 90,10 90,90 10,90" fill="#f1c40f"/><g opacity="0.7"><g opacity="0.7"><circle cx="50" cy="50" r="35" fill="#2980b9"/></g></g></svg>""",
            "g opacity=\"0.7\" > g opacity=\"0.7\""),
        SvgSwatch("group transform: translate + scale",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g transform="translate(15,15) scale(0.6)"><path d="M50,10 L90,90 L10,90 Z" fill="#8e44ad"/></g></svg>""",
            "transform=\"translate(15,15) scale(0.6)\""),
        SvgSwatch("horizontal mirror via scale(-1,1)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g transform="scale(-1,1) translate(-100,0)"><path d="M20,80 L60,20 L90,80 Z" fill="#16a085"/></g></svg>""",
            "transform=\"scale(-1,1) translate(-100,0)\""),
        SvgSwatch("use + per-instance transform",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><circle id="dot2" cx="50" cy="50" r="15" fill="#9b59b6"/></defs><use xlink:href="#dot2" transform="scale(0.6)"/><use xlink:href="#dot2" transform="translate(40,20) scale(0.8)"/></svg>""",
            "two &lt;use&gt; of the same &lt;circle&gt;, each transformed")
    ) +

    "<h2>6 — clipPath + use</h2>" +
    "<p class=\"intro\">clip-path references a &lt;clipPath&gt; that itself contains a &lt;use&gt; of a shape defined once in &lt;defs&gt; — the same pattern used by the peach illustrations below.</p>" +
    Row(
        SvgSwatch("gradient clipped to a circle",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><circle id="clipCircle1" cx="50" cy="50" r="35"/><linearGradient id="lg3" gradientUnits="userSpaceOnUse" x1="10" y1="10" x2="90" y2="90"><stop offset="0" stop-color="#ff9a9e"/><stop offset="1" stop-color="#fecfef"/></linearGradient><clipPath id="clip1"><use xlink:href="#clipCircle1"/></clipPath></defs><g clip-path="url(#clip1)"><polygon points="0,0 100,0 100,100 0,100" fill="url(#lg3)"/></g></svg>""",
            "clipPath > use > circle"),
        SvgSwatch("gradient clipped to a star",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><defs><polygon id="clipStar" points="{StarPoints}"/><linearGradient id="lg4" gradientUnits="userSpaceOnUse" x1="10" y1="10" x2="90" y2="90"><stop offset="0" stop-color="#f6d365"/><stop offset="1" stop-color="#fda085"/></linearGradient><clipPath id="clip2"><use xlink:href="#clipStar"/></clipPath></defs><g clip-path="url(#clip2)"><polygon points="0,0 100,0 100,100 0,100" fill="url(#lg4)"/></g></svg>""",
            "clipPath > use > polygon"),
        SvgSwatch("use for simple repetition",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><circle id="dot" cx="0" cy="0" r="12" fill="#e67e22"/></defs><use xlink:href="#dot" x="30" y="50"/><use xlink:href="#dot" x="70" y="50"/></svg>""",
            "use xlink:href=\"#dot\" x=.. y=.."),
        SvgSwatch("clip + group opacity combined",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><circle id="clipCircle2" cx="50" cy="50" r="35"/><clipPath id="clip3"><use xlink:href="#clipCircle2"/></clipPath></defs><polygon points="0,0 100,0 100,100 0,100" fill="#34495e"/><g clip-path="url(#clip3)" opacity="0.8"><polygon points="0,0 100,0 100,100 0,100" fill="#e74c3c"/></g></svg>""",
            "g clip-path=\"url(#clip3)\" opacity=\"0.8\"")
    ) +

    "<h2>7 — Inline &lt;svg&gt; vs &lt;img src=\"data:image/svg+xml\"&gt;</h2>" +
    "<p class=\"intro\">The identical SVG markup rendered two ways: embedded directly in the HTML, and encoded as a base64 data: URI on an &lt;img&gt; tag. Both go through the same vector renderer.</p>" +
    "<table class=\"sw\"><tr>" +
    SvgSwatch("inline &lt;svg&gt;", parityMarkup, "&lt;svg&gt;...&lt;/svg&gt; inline in the HTML body") +
    "<td>" +
        $"<div class=\"sbox\"><img src=\"{parityDataUri}\" width=\"80\" height=\"80\"/></div>" +
        "<div class=\"desc\">&lt;img src=\"data:...\"&gt;</div>" +
        "<div class=\"css\">same markup, base64 data: URI</div>" +
    "</td>" +
    "</tr></table>" +

    "<h2>8 — Peach Showcase</h2>" +
    "<p class=\"intro\">Two original peach illustrations, built entirely from the elements above: cubic-curve paths, radial gradients for shading, and a clipPath + use for the cross-section.</p>" +
    "<table class=\"sw\"><tr>" +
    PeachSwatch("Whole Peach",
        """
        <svg viewBox="0 0 200 200" width="150" height="150">
          <defs>
            <radialGradient id="peachBody" gradientUnits="userSpaceOnUse" cx="80" cy="70" r="150">
              <stop offset="0" stop-color="#fff3b0"/>
              <stop offset="0.35" stop-color="#ffb347"/>
              <stop offset="0.7" stop-color="#ff6f61"/>
              <stop offset="1" stop-color="#d1495b"/>
            </radialGradient>
            <radialGradient id="cheekBlush" gradientUnits="userSpaceOnUse" cx="140" cy="130" r="55">
              <stop offset="0" stop-color="#ff4d6d" stop-opacity="0.55"/>
              <stop offset="1" stop-color="#ff4d6d" stop-opacity="0"/>
            </radialGradient>
          </defs>
          <path d="M100,82 C90,60 65,45 40,55 C10,67 3,112 18,146 C33,178 66,195 100,195 C134,195 167,178 182,146 C197,112 190,67 160,55 C135,45 110,60 100,82 Z" fill="url(#peachBody)"/>
          <circle cx="140" cy="130" r="55" fill="url(#cheekBlush)"/>
          <path d="M100,55 C112,35 140,28 155,40 C143,55 118,60 100,55 Z" fill="#4caf50"/>
          <path d="M100,55 L96,35" fill="none" stroke="#6d4c30" stroke-width="6"/>
        </svg>
        """,
        "path (M/C/Z) body + leaf + stroked stem, radialGradient shading, radial blush with alpha stops") +
    PeachSwatch("Peach Slice",
        """
        <svg viewBox="0 0 200 200" width="150" height="150">
          <defs>
            <circle id="sliceOuter" cx="100" cy="100" r="85"/>
            <radialGradient id="fleshGradient" gradientUnits="userSpaceOnUse" cx="100" cy="90" r="110">
              <stop offset="0" stop-color="#fff8e1"/>
              <stop offset="0.5" stop-color="#ffb74d"/>
              <stop offset="1" stop-color="#e65100"/>
            </radialGradient>
            <clipPath id="sliceClip">
              <use xlink:href="#sliceOuter"/>
            </clipPath>
          </defs>
          <g clip-path="url(#sliceClip)">
            <polygon points="0,0 200,0 200,200 0,200" fill="url(#fleshGradient)"/>
            <g opacity="0.35">
              <path d="M100,100 L100,20" fill="none" stroke="#e65100" stroke-width="2"/>
              <path d="M100,100 L170,60" fill="none" stroke="#e65100" stroke-width="2"/>
              <path d="M100,100 L170,140" fill="none" stroke="#e65100" stroke-width="2"/>
              <path d="M100,100 L100,180" fill="none" stroke="#e65100" stroke-width="2"/>
              <path d="M100,100 L30,140" fill="none" stroke="#e65100" stroke-width="2"/>
              <path d="M100,100 L30,60" fill="none" stroke="#e65100" stroke-width="2"/>
            </g>
          </g>
          <path d="M100,75 C120,75 135,90 135,108 C135,128 118,142 100,142 C82,142 65,128 65,108 C65,90 80,75 100,75 Z" fill="#8d5524" stroke="#5d3a1a" stroke-width="2"/>
          <use xlink:href="#sliceOuter" fill="none" stroke="#c0392b" stroke-width="6"/>
        </svg>
        """,
        "clipPath + use round the flesh, opacity-grouped striations, pit path, use for the skin outline") +
    "</tr></table>" +

    "</body></html>";

var svgStream = new MemoryStream();
var svgDocument = await generator.GeneratePdf(svgHtml, pdfConfig);
svgDocument.Save(svgStream);

File.Delete("test_svg.pdf");
File.WriteAllBytes("test_svg.pdf", svgStream.ToArray());
Console.WriteLine("Saved test_svg.pdf");

File.Delete("test_svg.html");
File.WriteAllText("test_svg.html", svgHtml);
Console.WriteLine("Saved test_svg.html");
