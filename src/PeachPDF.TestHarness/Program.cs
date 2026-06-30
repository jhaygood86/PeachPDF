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
