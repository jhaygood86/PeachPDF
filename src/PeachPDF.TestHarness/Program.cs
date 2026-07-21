using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

PdfGenerateConfig pdfConfig = new()
{
    PageSize = PageSize.A4,
    PageOrientation = PageOrientation.Portrait,
    ShrinkToFit = true
};

PdfGenerator generator = new();

// Optional first argument: directory to write showcase output into (used by the
// docs-site build to publish /showcase). Defaults to the current directory,
// matching the historical local workflow.
var outputDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
Directory.CreateDirectory(outputDir);

List<ShowcaseEntry> showcaseManifest = [];

// Every showcase goes through here so the manifest (showcases.json) that drives
// the website's /showcase page always matches the files actually written.
async Task SaveShowcaseAsync(string slug, string category, string cardTitle, string cardDescription, string sourceHtml, PdfGenerateConfig renderConfig)
{
    var showcaseDocument = await generator.GeneratePdf(sourceHtml, renderConfig);
    using var pdfStream = new MemoryStream();
    showcaseDocument.Save(pdfStream);
    File.WriteAllBytes(Path.Combine(outputDir, $"{slug}.pdf"), pdfStream.ToArray());
    File.WriteAllText(Path.Combine(outputDir, $"{slug}.html"), sourceHtml);
    showcaseManifest.Add(new ShowcaseEntry(slug, category, cardTitle, cardDescription, $"{slug}.pdf", $"{slug}.html"));
    Console.WriteLine($"Saved {slug}.pdf + {slug}.html");
}

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

static string BorderStyleSwatch(string desc, string style) =>
    "<td>" +
    $"<div class=\"bsbox\" style=\"border: 16px {style} #4a90d9\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">border-style: {style}</div>" +
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
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; break-after: avoid }
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

await SaveShowcaseAsync("gradients", "Backgrounds & Borders", "CSS Gradients",
    "linear-gradient, radial-gradient, and conic-gradient: directions, angles, multi-stop and hard-stop color lists, and CSS Color Level 4 interpolation spaces.",
    html, pdfConfig);

// --- Border-radius showcase ---

const string RadiusCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; break-after: avoid }
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

await SaveShowcaseAsync("border_radius", "Backgrounds & Borders", "Border Radius",
    "border-radius from simple uniform rounding to per-corner and elliptical radii, on filled and bordered boxes.",
    radiusHtml, pdfConfig);

// --- background-origin + background-clip showcase ---

const string OriginCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
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

    "<h2>10 — Multi-Layer background-origin / background-clip</h2>" +
    "<p class=\"intro\">Comma-separated background-origin/background-clip values cycle per background-image layer, one value per layer, just like background-position/background-size.</p>" +
    Row(
        OriginSwatch("2 layers: content-box, border-box",
            "background-image: linear-gradient(to right, red, blue), linear-gradient(to bottom, green, yellow); background-origin: content-box, border-box; background-clip: content-box, border-box",
            "origin/clip: content-box, border-box"),
        OriginSwatch("2 layers: padding-box, content-box",
            "background-image: linear-gradient(to right, teal, gold), radial-gradient(circle, crimson, navy); background-origin: padding-box, content-box; background-clip: padding-box, content-box",
            "origin/clip: padding-box, content-box"),
        OriginSwatch("3 layers cycling 2 values",
            "background-image: linear-gradient(red, blue), linear-gradient(green, yellow), linear-gradient(orange, purple); background-origin: border-box, content-box; background-clip: border-box, content-box",
            "3 layers, origin/clip cycle: border-box, content-box, border-box"),
        OriginSwatch("color uses LAST clip entry",
            "background-color: coral; background-clip: border-box, content-box",
            "background-clip: border-box, content-box (no images — color clips to content-box, the last entry)")
    ) +

    "<h2>11 — Multi-Layer background-repeat</h2>" +
    "<p class=\"intro\">A comma-separated background-repeat value also cycles per layer — each background-image layer can repeat independently.</p>" +
    Row(
        OriginSwatch("layer 1: no-repeat, layer 2: repeat",
            "background-image: radial-gradient(circle, red, blue 70%), radial-gradient(circle, green, yellow 70%); background-size: 16px, 16px; background-repeat: no-repeat, repeat",
            "background-repeat: no-repeat, repeat"),
        OriginSwatch("layer 1: repeat-x, layer 2: repeat-y",
            "background-image: linear-gradient(to right, red, blue), linear-gradient(to bottom, green, yellow); background-size: 16px 16px, 16px 16px; background-repeat: repeat-x, repeat-y",
            "background-repeat: repeat-x, repeat-y")
    ) +

    "</body></html>";

await SaveShowcaseAsync("background_origin_clip", "Backgrounds & Borders", "Background Origin & Clip",
    "background-origin and background-clip controlling where a background paints relative to the border, padding, and content boxes.",
    originHtml, pdfConfig);

// --- background-position + background-size showcase ---

const string PositionCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .obox { height: 60px; border: 1px solid #333; background-color: #eee; background-repeat: no-repeat; margin-bottom: 3px; box-sizing: border-box }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

const string Dot = "radial-gradient(circle, crimson, darkred)";

// Gradients have no intrinsic size/ratio (they're "generated images" per spec), so a two-value
// background-size (e.g. "20px 20px") is used throughout to get an actual 20x20 square marker -
// a single-value size like "20px" would set width=20px but fall back to the FULL container height
// for the auto height axis (there's no ratio to compute a proportional height from), per spec.
const string DotSize = "20px 20px";

// A small square (1:1 intrinsic ratio, viewBox 0 0 20 20) vector SVG, reused below as an SVG
// background-image/list-style-image url() source - unlike Dot above, this has a real intrinsic
// size/ratio, so cover/contain/auto resolve against it exactly like a raster image would.
var svgDotMarkup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20"><rect width="20" height="20" fill="#c0392b"/><circle cx="10" cy="10" r="8" fill="#f1c40f"/></svg>""";
var svgDotDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svgDotMarkup));

var positionHtml = "<!DOCTYPE html><html><head>" + PositionCss + "</head><body>" +

    "<h1>CSS background-position &amp; background-size Test Page</h1>" +
    "<p class=\"intro\">Each box is 60px tall with a light gray fill; the red dot is a 20x20 background-image whose placement/size is what's under test.</p>" +

    "<h2>1 — background-position: Keywords</h2>" +
    Row(
        OriginSwatch("top left", $"background-image: {Dot}; background-size: {DotSize}; background-position: top left", "background-position: top left"),
        OriginSwatch("top right", $"background-image: {Dot}; background-size: {DotSize}; background-position: top right", "background-position: top right"),
        OriginSwatch("bottom left", $"background-image: {Dot}; background-size: {DotSize}; background-position: bottom left", "background-position: bottom left"),
        OriginSwatch("bottom right", $"background-image: {Dot}; background-size: {DotSize}; background-position: bottom right", "background-position: bottom right")
    ) +

    "<h2>2 — background-position: Center &amp; Reversed Keyword Order</h2>" +
    Row(
        OriginSwatch("center", $"background-image: {Dot}; background-size: {DotSize}; background-position: center", "background-position: center"),
        OriginSwatch("bottom center", $"background-image: {Dot}; background-size: {DotSize}; background-position: bottom center", "background-position: bottom center"),
        OriginSwatch("center right (reversed)", $"background-image: {Dot}; background-size: {DotSize}; background-position: center right", "background-position: center right"),
        OriginSwatch("right (single keyword)", $"background-image: {Dot}; background-size: {DotSize}; background-position: right", "background-position: right (Y implied center)")
    ) +

    "<h2>3 — background-position: Percentages &amp; Lengths</h2>" +
    Row(
        OriginSwatch("25% 75%", $"background-image: {Dot}; background-size: {DotSize}; background-position: 25% 75%", "background-position: 25% 75% (not centered)"),
        OriginSwatch("10px 10px", $"background-image: {Dot}; background-size: {DotSize}; background-position: 10px 10px", "background-position: 10px 10px"),
        OriginSwatch("calc(50% - 10px) center", $"background-image: {Dot}; background-size: {DotSize}; background-position: calc(50% - 10px) center", "background-position: calc(50% - 10px) center"),
        OriginSwatch("0 0 (top left)", $"background-image: {Dot}; background-size: {DotSize}; background-position: 0 0", "background-position: 0 0")
    ) +

    "<h2>4 — background-position: 4-Value Edge Offset Syntax</h2>" +
    Row(
        OriginSwatch("right 10px bottom 10px", $"background-image: {Dot}; background-size: {DotSize}; background-position: right 10px bottom 10px", "background-position: right 10px bottom 10px"),
        OriginSwatch("right 20px top", $"background-image: {Dot}; background-size: {DotSize}; background-position: right 20px top", "background-position: right 20px top"),
        OriginSwatch("left bottom 20px", $"background-image: {Dot}; background-size: {DotSize}; background-position: left bottom 20px", "background-position: left bottom 20px"),
        OriginSwatch("bottom 5px right 5px", $"background-image: {Dot}; background-size: {DotSize}; background-position: bottom 5px right 5px", "background-position: bottom 5px right 5px")
    ) +

    "<h2>5 — background-size: cover / contain / auto (no intrinsic ratio)</h2>" +
    "<p class=\"intro\">Gradients are \"generated images\" with no intrinsic size or ratio, so per spec cover/contain/auto all resolve identically to 100% 100% (full box) - contrasted here with an explicit stretched size.</p>" +
    Row(
        OriginSwatch("cover (= full box)", $"background-image: {Dot}; background-repeat: no-repeat; background-size: cover", "background-size: cover"),
        OriginSwatch("contain (= full box)", $"background-image: {Dot}; background-repeat: no-repeat; background-size: contain", "background-size: contain"),
        OriginSwatch("auto (= full box)", $"background-image: {Dot}; background-repeat: no-repeat; background-size: auto", "background-size: auto"),
        OriginSwatch("100% 100% (same result)", $"background-image: {Dot}; background-repeat: no-repeat; background-size: 100% 100%", "background-size: 100% 100%")
    ) +

    "<h2>6 — background-size: Explicit Lengths &amp; Percentages</h2>" +
    Row(
        OriginSwatch("40px 40px", $"background-image: {Dot}; background-repeat: no-repeat; background-position: center; background-size: 40px 40px", "background-size: 40px 40px"),
        OriginSwatch("50% 50%", $"background-image: {Dot}; background-repeat: no-repeat; background-position: center; background-size: 50% 50%", "background-size: 50% 50%"),
        OriginSwatch("20px (width only, height fills box)", $"background-image: {Dot}; background-repeat: no-repeat; background-position: top left; background-size: 20px", "background-size: 20px (no ratio - height defaults to full box)"),
        OriginSwatch("gradient background-size, tiled", "background-image: linear-gradient(to right, red, blue); background-repeat: repeat; background-size: 50%", "gradient background-size: 50% (tiles)")
    ) +

    "<h2>7 — Multi-Layer background-position / background-size</h2>" +
    "<p class=\"intro\">Comma-separated background-position/background-size values cycle per background-image layer, same as background-image itself.</p>" +
    Row(
        OriginSwatch("2 dots, different corners",
            $"background-image: {Dot}, {Dot}; background-repeat: no-repeat; background-size: {DotSize}, {DotSize}; background-position: top left, bottom right",
            "background-position: top left, bottom right"),
        OriginSwatch("2 dots, different sizes",
            $"background-image: {Dot}, {Dot}; background-repeat: no-repeat; background-position: center; background-size: 40px 40px, 15px 15px",
            "background-size: 40px 40px, 15px 15px (cycled per layer)"),
        OriginSwatch("3 layers cycling 2 positions",
            $"background-image: {Dot}, {Dot}, {Dot}; background-repeat: no-repeat; background-size: 16px 16px; background-position: top left, bottom right",
            "3 layers, position cycles: top left, bottom right, top left")
    ) +

    "<h2>8 — SVG background-image</h2>" +
    "<p class=\"intro\">A url() background-image source can now be an SVG - rendered as real vector content (a reusable Form XObject tile), not rasterized, exactly like &lt;img src=\"x.svg\"&gt; already was.</p>" +
    Row(
        OriginSwatch("basic (auto = SVG's own viewBox size)",
            $"background-image: url('{svgDotDataUri}'); background-repeat: no-repeat; background-position: center",
            "background-image: url(x.svg) (no background-size)"),
        OriginSwatch("background-size: cover",
            $"background-image: url('{svgDotDataUri}'); background-repeat: no-repeat; background-size: cover",
            "background-size: cover (uses the SVG's 1:1 intrinsic ratio)"),
        OriginSwatch("background-size: contain",
            $"background-image: url('{svgDotDataUri}'); background-repeat: no-repeat; background-size: contain",
            "background-size: contain"),
        OriginSwatch("background-repeat: repeat (tiled)",
            $"background-image: url('{svgDotDataUri}'); background-size: 20px 20px; background-repeat: repeat",
            "background-repeat: repeat (one vector tile, reused per copy)")
    ) +

    "</body></html>";

await SaveShowcaseAsync("background_position_size", "Backgrounds & Borders", "Background Position & Size",
    "background-position and background-size combinations: keywords, lengths, percentages, cover, and contain.",
    positionHtml, pdfConfig);

// --- list-style-image showcase ---

// A small vector SVG sized to match a marker box (roughly one font-height square) rather than the
// larger svgDotDataUri used above for the background-image showcase's 60px-tall boxes - reusing that
// larger one here would render at its full 20x20 intrinsic size (list-style-image has no analogue to
// background-size), overflowing the ~10pt marker box and clipping down to just its solid center.
var svgMarkerMarkup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="#c0392b"/><circle cx="5" cy="5" r="4" fill="#f1c40f"/></svg>""";
var svgMarkerDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svgMarkerMarkup));

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
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
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

    "<h2>7 — SVG list-style-image</h2>" +
    "<p class=\"intro\">A url() list-style-image source can now be an SVG - rendered as real vector content, not rasterized, same as the SVG background-image support above.</p>" +
    Row(
        ListSwatch("SVG url() image", $"list-style-image: url('{svgMarkerDataUri}');"),
        ListSwatch("shorthand: inside + SVG", $"list-style: inside url('{svgMarkerDataUri}');"),
        ListSwatch("shorthand: outside + SVG", $"list-style: outside url('{svgMarkerDataUri}');"),
        ListSwatch("missing SVG file (graceful fallback)", "list-style-image: url('nonexistent.svg');")
    ) +

    "</body></html>";

await SaveShowcaseAsync("list_style_image", "Lists & Generated Content", "List Style Image",
    "Custom list bullets supplied through list-style-image.",
    listHtml, pdfConfig);

// --- ::marker styling showcase ---

// ::marker rules can't be expressed via an inline style="..." attribute (pseudo-elements aren't
// targetable that way), so each swatch gets its own scoped <style> block instead - mirrors
// ContentSwatch's own approach below for the same reason. The scoping class goes on the list
// container (matching ListSwatch's own convention above), so the selector needs a descendant
// combinator ("li::marker", not "::marker" directly on the class) - ::marker's own compound-
// selector matching checks the non-pseudo part against the marker's *parent* (the <li>), not
// against whatever box the class happens to be on.
static string MarkerSwatch(string desc, string markerCss, string tag = "ul", string itemLabel = "Item") =>
    "<td>" +
    $"<style>.mk-{desc.GetHashCode() & 0x7FFFFFFF} li::marker {{ {markerCss} }}</style>" +
    $"<{tag} class=\"mk-{desc.GetHashCode() & 0x7FFFFFFF}\" style=\"margin: 0; padding-left: 2em;\">" +
    $"<li>{itemLabel} one</li><li>{itemLabel} two</li><li>{itemLabel} three</li>" +
    $"</{tag}>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">::marker {{ {markerCss} }}</div>" +
    "</td>";

var markerHtml = "<!DOCTYPE html><html><head>" + ListCss + "</head><body>" +

    "<h1>CSS ::marker Test Page</h1>" +

    "<h2>1 — color and font styling</h2>" +
    "<p class=\"intro\">::marker is a real, independently styled box now - its color/font are its own, not the list item's.</p>" +
    Row(
        MarkerSwatch("red marker, black text", "color: red;", "ol"),
        MarkerSwatch("large bold marker", "font-size: 13pt; font-weight: bold;", "ol"),
        MarkerSwatch("marker color vs. text color", "color: green;", "ul"),
        MarkerSwatch("both color and font-size", "color: #8e44ad; font-size: 13pt;", "ol")
    ) +

    "<h2>2 — content overrides</h2>" +
    "<p class=\"intro\">An explicit content value fully replaces the automatic bullet/number - no automatic \".\" suffix, so any spacing/punctuation must be in the string itself.</p>" +
    Row(
        MarkerSwatch("custom string", "content: \"→  \";", "ul"),
        MarkerSwatch("counter() override", "content: counter(list-item) \")  \";", "ol"),
        MarkerSwatch("content: none (suppressed)", "content: none;", "ul"),
        MarkerSwatch("content: normal (baseline)", "content: normal;", "ol")
    ) +

    "<h2>3 — direction</h2>" +
    "<p class=\"intro\">The marker's own direction is independent of the list item's own text direction.</p>" +
    Row(
        MarkerSwatch("marker: rtl, text: ltr", "direction: rtl;", "ol"),
        MarkerSwatch("marker: ltr (baseline)", "direction: ltr;", "ol")
    ) +

    "<h2>4 — list-style-position: inside vs outside</h2>" +
    "<p class=\"intro\">An \"inside\" marker is a real first inline child of the list item's own content, flowing (and wrapping) like ordinary text; an \"outside\" marker (the default) hangs to the left, never affecting the item's own line-wrap.</p>" +
    Row(
        "<td>" +
        "<ul style=\"margin: 0; padding-left: 2em; list-style-position: outside\">" +
        "<li>Outside position (default): this list item has enough text in it to wrap onto a second line, so the effect on the hanging indent is visible.</li>" +
        "</ul>" +
        "<div class=\"desc\">outside (default)</div>" +
        "<div class=\"css\">list-style-position: outside</div>" +
        "</td>",
        "<td>" +
        "<ul style=\"margin: 0; padding-left: 2em; list-style-position: inside\">" +
        "<li>Inside position: this list item has enough text in it to wrap onto a second line, so the marker's effect on where the wrapped line starts is visible.</li>" +
        "</ul>" +
        "<div class=\"desc\">inside</div>" +
        "<div class=\"css\">list-style-position: inside</div>" +
        "</td>",
        // The reserved width for an "inside" marker follows the marker's own (possibly overridden)
        // font, not the item's - this large marker font visibly pushes the wrapped line further right.
        "<td>" +
        "<style>.mk-inside-big li::marker { font-size: 20pt; }</style>" +
        "<ol class=\"mk-inside-big\" style=\"margin: 0; padding-left: 2em; list-style-position: inside\">" +
        "<li>Inside position with a much larger marker font-size: this item's text also wraps, showing the reserved width grow with it.</li>" +
        "</ol>" +
        "<div class=\"desc\">inside + large marker font-size</div>" +
        "<div class=\"css\">list-style-position: inside; ::marker { font-size: 20pt }</div>" +
        "</td>"
    ) +

    "</body></html>";

await SaveShowcaseAsync("marker_styling", "Lists & Generated Content", "::marker Styling",
    "Styling list markers with the ::marker pseudo-element, including real per-item numbering via the list-item counter.",
    markerHtml, pdfConfig);

// --- content image showcase ---

static string ContentSwatch(string desc, string contentValue, string pseudoElement = "before", string width = "40px", string height = "28px", string? cssLabel = null) =>
    "<td>" +
    $"<style>.ci-{pseudoElement}-{desc.GetHashCode() & 0x7FFFFFFF}::{ pseudoElement} {{ content: {contentValue}; display: inline-block; width: {width}; height: {height}; }}</style>" +
    $"<div class=\"ci-{pseudoElement}-{desc.GetHashCode() & 0x7FFFFFFF}\"></div>" +
    $"<div class=\"desc\">{desc}</div>" +
    // A data: URI content value can be thousands of unbroken base64 characters - showing it
    // verbatim here (as every other swatch does) isn't just unreadable, it's a real trigger for a
    // word-break:break-all measurement bug (a single unbreakable token that long inflates
    // ShrinkToFit's measured page width and squashes the whole page - see cssLabel callers below).
    $"<div class=\"css\">::{pseudoElement} {{ content: {cssLabel ?? contentValue} }}</div>" +
    "</td>";

// A hand-drawn vector peach (two overlapping gradient-filled circles form the characteristic
// cleft, plus a pair of small leaves) - used below to prove a url() content-image source can be
// an SVG, rendered as real vector content, on-brand for PeachPDF's own showcase.
var peachMarkup = """
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
      <defs>
        <radialGradient id="peachL" cx="30%" cy="30%" r="80%">
          <stop offset="0%" stop-color="#ffdca8"/>
          <stop offset="60%" stop-color="#ffab6b"/>
          <stop offset="100%" stop-color="#f4784a"/>
        </radialGradient>
        <radialGradient id="peachR" cx="35%" cy="30%" r="80%">
          <stop offset="0%" stop-color="#ffd0c2"/>
          <stop offset="60%" stop-color="#ff8f7d"/>
          <stop offset="100%" stop-color="#e85d4a"/>
        </radialGradient>
      </defs>
      <circle cx="12" cy="18" r="10" fill="url(#peachL)"/>
      <circle cx="20" cy="18" r="10" fill="url(#peachR)"/>
      <path d="M16 10 C14 5 10 3 7 4 C9 7 12 9 15 9 Z" fill="#5cb85c"/>
      <path d="M16 10 C18 5 22 3 25 4 C23 7 20 9 17 9 Z" fill="#4a9e4a"/>
    </svg>
    """;
var peachDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(peachMarkup));

// Regression: a <style> element nested in the document BODY (as every other swatch on this page
// uses, one per <td>) that defines a pseudo-element rule whose content resolves to a REAL loaded
// url() image (SVG or raster) previously corrupted ShrinkToFit's width measurement pass - a
// display:none box's content (the <style> tag's own hidden text, which is unbroken for thousands
// of base64 characters) was still being deep-scanned for the page's "longest word" during table
// column-width measurement. Now that display:none subtrees are correctly skipped, these rules are
// defined per-<td> like every other swatch on this page rather than hoisted into <head>.
static string PeachTd(string desc, string className, string peachDataUri, string elementHtml, string pseudoElement, string cssLabel, string extraStyle = "") =>
    "<td>" +
    $"<style>.{className}::{pseudoElement} {{ content: url('{peachDataUri}'); display: inline-block; width: 32px; height: 32px;{extraStyle} }}</style>" +
    elementHtml +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{cssLabel}</div>" +
    "</td>";

static string ContentSwatchInline(string desc, string pseudoElement, string inlineCss, string? cssLabel = null) =>
    "<td>" +
    $"<style>.cci-{desc.GetHashCode() & 0x7FFFFFFF}::{pseudoElement} {{ {inlineCss} }}</style>" +
    $"<p class=\"cci-{desc.GetHashCode() & 0x7FFFFFFF}\">Text</p>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">::{pseudoElement} {{ {cssLabel ?? inlineCss} }}</div>" +
    "</td>";

const string ContentCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
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

    "<h2>7 — SVG url() content image</h2>" +
    "<p class=\"intro\">A url() content-image source can also be an SVG, rendered as real vector content (not rasterized) - same as background-image and list-style-image. Note the image paints at the SVG's own intrinsic viewBox size (32x32 here), independent of the box size reserved by display:inline-block's width/height.</p>" +
    Row(
        PeachTd("::before peach", "peach-before", peachDataUri, "<div class=\"peach-before\"></div>", "before", "::before { content: url('data:image/svg+xml;base64,…') }"),
        PeachTd("::after peach", "peach-after", peachDataUri, "<div class=\"peach-after\"></div>", "after", "::after { content: url('data:image/svg+xml;base64,…') }"),
        PeachTd("peach + text (::after)", "peach-inline", peachDataUri, "<p class=\"peach-inline\">Text</p>", "after", "::after { content: url('data:image/svg+xml;base64,…') }", " vertical-align: middle;"),
        ContentSwatch("missing SVG (no crash)", "url('nonexistent.svg')", "before", "32px", "32px")
    ) +

    "</body></html>";

await SaveShowcaseAsync("content_image", "Lists & Generated Content", "Generated Content Images",
    "Images inserted through the CSS content property on generated content.",
    contentHtml, pdfConfig);

// ─── CSS Paged Media showcase ───────────────────────────────────────────────

var pagedMediaHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: A4 portrait;
      margin: 25mm 20mm 25mm 20mm;
      @top-left-corner { content: url('{{PEACH_DATA_URI}}'); }
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
    h2 { font-size: 16pt; margin: 24pt 0 8pt; border-bottom: 1px solid #999; padding-bottom: 4pt; break-after: avoid }
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
    """.Replace("{{PEACH_DATA_URI}}", peachDataUri);

await SaveShowcaseAsync("paged_media", "Paged Media", "Paged Media",
    "@page rules with margin boxes, running headers and footers, and page counters, including a " +
    "url() logo image in a margin box (@top-left-corner).",
    pagedMediaHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

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
    h2 { font-size: 13pt; margin: 18pt 0 6pt; string-set: section content(); break-after: avoid }
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

await SaveShowcaseAsync("paged_media_named_strings", "Paged Media", "Named Strings",
    "Running headers populated from document content with string-set and string().",
    namedStringsHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ── Per-page margin variation showcase ─────────────────────────────────────
// Per-page top/bottom margin overrides are layout-affecting (CSS Paged Media 3 page-box
// model): each page's own margins define its content band, so the flowing text genuinely
// breaks at different heights per page - the deep-margined first page holds visibly fewer
// paragraphs, mirrored :left/:right pages trade extra top space for extra bottom space,
// and the running furniture follows each page's own margins. The :first margins use
// relative units (% of the page width, em against the root font) - per-page rules resolve
// them against the same bases the base rule uses (#150), not just absolute lengths.
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
        margin-top: 38%;      /* of the page width: ~80mm on A4 */
        margin-bottom: 10em;  /* against the root font size */
        @top-center { content: none; }
    }
    @page :left  { margin-top: 45mm; margin-bottom: 15mm; }
    @page :right { margin-top: 15mm; margin-bottom: 45mm; }
    body { font: 11pt Arial; }
    h1 { font-size: 22pt; text-align: center; margin: 0 0 4mm; }
    .note { text-align: center; font-size: 10pt; color: #555; }
    </style>
    </head>
    <body>
      <h1>Per-Page Margin Variation</h1>
      <p class="note">Every page's own @page margins define its content band: the first page's deep
      margins (declared in relative units - 38% of the page width on top, 10em on the bottom) fit
      only a few paragraphs, then :right pages (15mm top / 45mm bottom) and :left pages (45mm top /
      15mm bottom) alternate mirrored bands - watch where the text starts, where it breaks, and how
      many paragraphs fit on each page.</p>
    """ +
    string.Concat(Enumerable.Range(1, 60).Select(i =>
        $"<p>Paragraph {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
        "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>")) +
    """
    </body>
    </html>
    """;

await SaveShowcaseAsync("paged_media_per_page_margins", "Paged Media", "Per-page Margins",
    "Layout-affecting per-page margins: each page's own @page margins define its content band, so text flows into visibly different-height pages (deep first page, mirrored :left/:right bands) - with relative units (%, em) supported in per-page rules too.",
    perPageMarginsHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

// ── Full-bleed page showcase ───────────────────────────────────────────────
// The headline capability behind layout-affecting per-page margins: a `margin: 0` first
// page whose content band is the entire physical sheet. The cover plate is sized to the
// full A4 sheet (210mm x 297mm) and paints to all four paper edges - corner registration
// marks prove every corner carries ink - while the forced break lands the second page back
// on ordinary 20mm margins with its running footer.
var fullBleedHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
        size: A4;
        margin: 20mm;
        @bottom-center { content: "Page " counter(page) " of " counter(pages); font: 8pt Arial; }
    }
    @page :first {
        margin: 0;
        @bottom-center { content: none; }
    }
    body { font: 11pt Arial; margin: 0; }
    /* Corner registration marks: ink in all four sheet corners proves true 4-edge bleed.
       Drawn as no-repeat background strips (two per corner) layered over the plate
       gradients, so every corner of the physical sheet demonstrably carries paint. */
    .cover {
      width: 210mm;
      height: 297mm;
      background-image:
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        linear-gradient(#f2c14e, #f2c14e),
        radial-gradient(circle at 20% 15%, rgba(255, 255, 255, 0.18), transparent 45%),
        linear-gradient(155deg in oklch, #0f2b46, #1d5c8a 55%, #2e8bc0);
      background-position: top left, top left, top right, top right,
        bottom left, bottom left, bottom right, bottom right, center, center;
      background-size: 14mm 3mm, 3mm 14mm, 14mm 3mm, 3mm 14mm,
        14mm 3mm, 3mm 14mm, 14mm 3mm, 3mm 14mm, auto, auto;
      background-repeat: no-repeat;
      color: #f2f7fb; text-align: center;
      display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 10mm;
      page-break-after: always;
    }
    .cover h1 { font-size: 30pt; letter-spacing: 6pt; word-spacing: 10pt; text-transform: uppercase; font-weight: normal; margin: 0; }
    .cover p { margin: 0; letter-spacing: 2pt; color: rgba(242, 247, 251, 0.75); }
    h2 { margin-top: 0; }
    </style>
    </head>
    <body>
    <div class="cover">
      <h1>Full Bleed</h1>
      <p>@page :first &#123; margin: 0 &#125; &mdash; the content band is the whole sheet</p>
      <p>Corner marks touch all four paper edges</p>
    </div>
    <h2>Back to ordinary margins</h2>
    <p>The cover's forced break lands this page on the base 20mm margins, with the running
    footer restored. Per-page top and bottom margins are layout-affecting: each page's own
    @page margins define its content band, so an edge-to-edge cover and a conventionally
    margined document coexist in one PDF.</p>
    </body>
    </html>
    """;

await SaveShowcaseAsync("full_bleed", "Paged Media", "Full-Bleed Pages",
    "An edge-to-edge cover via @page :first { margin: 0 } - the first page's content band is the entire sheet (corner marks touch all four paper edges), followed by a normally margined page.",
    fullBleedHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

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

await SaveShowcaseAsync("paged_media_named_pages", "Paged Media", "Named Pages",
    "Routing content to differently-styled pages with named @page rules and the page property.",
    namedPagesHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

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

await SaveShowcaseAsync("paged_media_margin_box_sizing", "Paged Media", "Margin Box Sizing",
    "Explicit width and height sizing of @page margin boxes.",
    marginBoxSizingHtml, new PdfGenerateConfig { PageSize = PageSize.A4 });

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
    h2 { font-size: 9pt; margin: 0.7em 0 0.2em; padding-bottom: 2px; border-bottom: 1px solid #ccc; color: #333; break-after: avoid }
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

    FSection("15 — Replaced elements (img/svg) mixed with block siblings",
        "<tr><td colspan='3' style='padding:2px 4px 6px;font:7pt Arial;color:#555'>" +
        "A flex container mixing an inline-level replaced element (an &lt;img&gt; or inline &lt;svg&gt;) " +
        "with a block-level sibling wraps the replaced element in an anonymous box per CSS Flexbox §4 — " +
        "it must still be measured, positioned, and painted like any other flex item." +
        "</td></tr>" +
        "<tr><td style=\"font:7pt Arial;color:#333;padding:2px 4px 2px 0;white-space:nowrap\">img + block title</td>" +
        "<td style=\"padding:2px\"><div style=\"display:flex;align-items:center;gap:8px;border:1px solid #bbb;background:#f8f8f8;padding:4px;\">" +
        "<img src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==\" width=\"48\" height=\"48\" style=\"background:#e74c3c\" />" +
        "<div style=\"font:bold 9pt Arial;color:#222\">Account Statement</div>" +
        "</div></td>" +
        "<td style=\"font:5.5pt Arial;color:#888;padding:2px 4px\">img width=48 height=48</td></tr>" +
        "<tr><td style=\"font:7pt Arial;color:#333;padding:2px 4px 2px 0;white-space:nowrap\">inline svg + block title</td>" +
        "<td style=\"padding:2px\"><div style=\"display:flex;align-items:center;gap:8px;border:1px solid #bbb;background:#f8f8f8;padding:4px;\">" +
        "<svg width=\"32\" height=\"32\"><circle cx=\"16\" cy=\"16\" r=\"16\" fill=\"#27ae60\" /></svg>" +
        "<div style=\"font:bold 9pt Arial;color:#222\">Status: Active</div>" +
        "</div></td>" +
        "<td style=\"font:5.5pt Arial;color:#888;padding:2px 4px\">inline &lt;svg&gt;</td></tr>"
    ) +

    "</body></html>";

await SaveShowcaseAsync("flexbox", "Layout", "Flexbox",
    "Flexbox layout: direction, wrapping, justification, alignment, gaps, flexible item sizing, and replaced elements (img/svg) as flex items.",
    flexHtml, pdfConfig);

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
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
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

await SaveShowcaseAsync("custom_properties", "CSS Values & Functions", "Custom Properties",
    "CSS custom properties resolved through var(), including fallbacks and cascading overrides.",
    varHtml, pdfConfig);

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
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; break-after: avoid }
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

await SaveShowcaseAsync("transform", "Graphics & Effects", "Transforms",
    "CSS transforms - translate, rotate, scale, skew - with transform-origin control.",
    transformHtml, pdfConfig);

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
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; break-after: avoid }
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

await SaveShowcaseAsync("calc", "CSS Values & Functions", "calc() & Math Functions",
    "calc(), min(), max(), and clamp() expressions resolving against real box dimensions.",
    calcHtml, pdfConfig);

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
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
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

// Small synthesized raster PNG (8x8 colorful checker) for the <image> element showcase. A
// hand-picked minimal PNG isn't reliably decodable by the StbImageSharp-based decoder PeachPDF
// uses internally - writing one with the matching StbImageWriteSharp encoder (already a transitive
// dependency via the PeachPDF project reference) is.
static string MakeRasterDataUri()
{
    const int size = 8;
    var palette = new (byte R, byte G, byte B)[]
    {
        (231, 76, 60), (241, 196, 15), (46, 204, 113), (52, 152, 219)
    };
    var pixels = new byte[size * size * 4];
    for (var y = 0; y < size; y++)
    {
        for (var x = 0; x < size; x++)
        {
            var (r, g, b) = palette[(x + y) % palette.Length];
            var i = (y * size + x) * 4;
            pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = 255;
        }
    }

    using var ms = new MemoryStream();
    new StbImageWriteSharp.ImageWriter().WritePng(pixels, size, size, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, ms);
    return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
}
var rasterDataUri = MakeRasterDataUri();

// A data:image/svg+xml payload for <image href="...">, proving that path stays real vector content
// (never rasterized) - unlike the raster PNG swatch above.
var nestedVectorMarkup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><defs><radialGradient id="nvg" gradientUnits="userSpaceOnUse" cx="50" cy="50" r="50"><stop offset="0" stop-color="#fceabb"/><stop offset="1" stop-color="#f8b500"/></radialGradient></defs><circle cx="50" cy="50" r="45" fill="url(#nvg)"/></svg>""";
var nestedVectorDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(nestedVectorMarkup));

// Two data:image/svg+xml <img> payloads for the <style> showcase below - a nested <style> element
// inside an INLINE <svg> has a known limitation (MimeKit's HTML tokenizer can hoist it out of the
// <svg> before SvgTreeBuilder ever sees it), so <style>-element demos deliberately use the
// standalone/<img> path instead, exactly like the automated test suite works around the same gap.
var styleClassMarkup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="80" height="80"><style>.hi{fill:#2980b9}.lo{fill:#bdc3c7}</style><rect x="10" y="10" width="35" height="80" class="lo"/><rect x="55" y="10" width="35" height="80" class="hi"/></svg>""";
var styleClassDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(styleClassMarkup));
var styleIdMarkup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="80" height="80"><style>#target{fill:#c0392b}</style><circle id="target" cx="50" cy="50" r="35" fill="#bdc3c7"/></svg>""";
var styleIdDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(styleIdMarkup));

var svgHtml = "<!DOCTYPE html><html><head>" + SvgShowcaseCss + "</head><body>" +

    "<h1>SVG Test Page</h1>" +
    "<p class=\"intro\">PeachPDF renders SVG through its own vector scene graph, reusing the same PDF path/fill/stroke/gradient/clip primitives already used for CSS backgrounds and borders — SVG content is never rasterized to a bitmap. Full SVG 1.0 coverage is now supported (minus a handful of PDF-incompatible features such as animation, scripting, and filters): all basic shapes, fill-rule, stroke dash/cap/join, rotate()/skewX()/skewY() transforms, full preserveAspectRatio and nested viewports, objectBoundingBox/spreadMethod/currentColor gradients, the style cascade (style= and &lt;style&gt;), &lt;switch&gt;/&lt;a&gt; links, &lt;marker&gt;, &lt;pattern&gt;, &lt;mask&gt;, &lt;image&gt;, and &lt;text&gt;/&lt;tspan&gt;/&lt;tref&gt;. See <a href=\"https://github.com/jhaygood86/PeachPDF/blob/main/docs/supported-svg-features.md\">supported-svg-features.md</a> for the full compatibility matrix.</p>" +

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

    "<h2>7 — Basic Shapes: rect, ellipse, line &amp; polyline</h2>" +
    Row(
        SvgSwatch("rect with rounded corners",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect x="15" y="15" width="70" height="70" rx="15" fill="#3498db"/></svg>""",
            "rect x y width height rx=15"),
        SvgSwatch("ellipse (percentage rx)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><ellipse cx="50" cy="50" rx="35%" ry="25" fill="#e67e22"/></svg>""",
            "ellipse rx=\"35%\" (percentage length)"),
        SvgSwatch("line, round linecap",
            """<svg viewBox="0 0 100 100" width="80" height="80"><line x1="10" y1="90" x2="90" y2="10" stroke="#c0392b" stroke-width="8" stroke-linecap="round"/></svg>""",
            "line x1 y1 x2 y2 stroke-linecap=\"round\""),
        SvgSwatch("polyline (open, unclosed)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><polyline points="10,80 30,20 50,70 70,15 90,60" fill="none" stroke="#16a085" stroke-width="4" stroke-linejoin="round"/></svg>""",
            "polyline points=\"...\" fill=\"none\"")
    ) +

    "<h2>8 — Stroke: Dash Arrays, Caps &amp; Joins</h2>" +
    Row(
        SvgSwatch("stroke-dasharray + dashoffset",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M10,50 L90,50" stroke="#8e44ad" stroke-width="8" stroke-dasharray="14,8" stroke-dashoffset="4" fill="none"/></svg>""",
            "stroke-dasharray=\"14,8\" stroke-dashoffset=\"4\""),
        SvgSwatch("stroke-linecap: butt/round/square",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g stroke-width="12"><line x1="20" y1="15" x2="20" y2="85" stroke="#2c3e50" stroke-linecap="butt"/><line x1="50" y1="15" x2="50" y2="85" stroke="#2980b9" stroke-linecap="round"/><line x1="80" y1="15" x2="80" y2="85" stroke="#c0392b" stroke-linecap="square"/></g></svg>""",
            "stroke-linecap: butt, round, square"),
        SvgSwatch("stroke-linejoin: miter/round/bevel",
            """<svg viewBox="0 0 140 100" width="90" height="64"><g fill="none" stroke-width="8"><path d="M5,75 L25,25 L45,75" stroke="#2c3e50" stroke-linejoin="miter"/><path d="M50,75 L70,25 L90,75" stroke="#2980b9" stroke-linejoin="round"/><path d="M95,75 L115,25 L135,75" stroke="#c0392b" stroke-linejoin="bevel"/></g></svg>""",
            "stroke-linejoin: miter, round, bevel"),
        SvgSwatch("dashed rounded-rect border",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect x="15" y="15" width="70" height="70" rx="12" fill="none" stroke="#16a085" stroke-width="5" stroke-dasharray="6,4" stroke-linecap="round" stroke-linejoin="round"/></svg>""",
            "dasharray + linecap + linejoin combined")
    ) +

    "<h2>9 — Fill Rule &amp; Opacity</h2>" +
    Row(
        SvgSwatch("fill-rule=\"nonzero\" (default)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M20,20 L80,20 L80,80 L20,80 Z M35,35 L65,35 L65,65 L35,65 Z" fill="#8e44ad" fill-rule="nonzero"/></svg>""",
            "same-direction inner square: solid (no hole)"),
        SvgSwatch("fill-rule=\"evenodd\"",
            """<svg viewBox="0 0 100 100" width="80" height="80"><path d="M20,20 L80,20 L80,80 L20,80 Z M35,35 L65,35 L65,65 L35,65 Z" fill="#8e44ad" fill-rule="evenodd"/></svg>""",
            "identical path, evenodd: donut (hole visible)"),
        SvgSwatch("fill-opacity, stroke fully opaque",
            """<svg viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35" fill="#e74c3c" fill-opacity="0.4" stroke="#c0392b" stroke-width="6"/></svg>""",
            "fill-opacity=\"0.4\" (stroke unaffected)"),
        SvgSwatch("stroke-opacity, fill fully opaque",
            """<svg viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35" fill="#e74c3c" stroke="#2c3e50" stroke-width="10" stroke-opacity="0.4"/></svg>""",
            "stroke-opacity=\"0.4\" (fill unaffected)")
    ) +

    "<h2>10 — Transforms: rotate() &amp; skew()</h2>" +
    Row(
        SvgSwatch("rotate(angle, cx, cy)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g transform="rotate(30,50,50)"><rect x="25" y="35" width="50" height="30" fill="#3498db"/></g></svg>""",
            "transform=\"rotate(30,50,50)\""),
        SvgSwatch("rotate(-angle, cx, cy)",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><g transform="rotate(-45,50,50)"><polygon points="{PentagonPoints}" fill="#e67e22"/></g></svg>""",
            "transform=\"rotate(-45,50,50)\""),
        SvgSwatch("skewX()",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g transform="skewX(20)"><rect x="15" y="25" width="50" height="50" fill="#16a085"/></g></svg>""",
            "transform=\"skewX(20)\""),
        SvgSwatch("skewY()",
            """<svg viewBox="0 0 100 100" width="80" height="80"><g transform="skewY(-20)"><rect x="25" y="15" width="50" height="50" fill="#9b59b6"/></g></svg>""",
            "transform=\"skewY(-20)\"")
    ) +

    "<h2>11 — Advanced Gradients: objectBoundingBox, spreadMethod &amp; Radial Focus</h2>" +
    Row(
        SvgSwatch("objectBoundingBox (default)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><linearGradient id="obbGrad" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#43cea2"/><stop offset="1" stop-color="#185a9d"/></linearGradient></defs><rect x="15" y="30" width="70" height="40" fill="url(#obbGrad)"/></svg>""",
            "no gradientUnits: x1/x2 are 0..1 fractions of the rect's own box"),
        SvgSwatch("spreadMethod=\"repeat\"",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><linearGradient id="spreadRepeat" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="20" y2="0" spreadMethod="repeat"><stop offset="0" stop-color="#ff9966"/><stop offset="1" stop-color="#ff5e62"/></linearGradient></defs><rect x="10" y="10" width="80" height="80" fill="url(#spreadRepeat)"/></svg>""",
            "narrow x1..x2 range tiles across the rect"),
        SvgSwatch("spreadMethod=\"reflect\"",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><linearGradient id="spreadReflect" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="20" y2="0" spreadMethod="reflect"><stop offset="0" stop-color="#00c9ff"/><stop offset="1" stop-color="#92fe9d"/></linearGradient></defs><rect x="10" y="10" width="80" height="80" fill="url(#spreadReflect)"/></svg>""",
            "same idea, mirrored at each repeat"),
        SvgSwatch("radial fx/fy off-center highlight",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><radialGradient id="fxfy" gradientUnits="userSpaceOnUse" cx="50" cy="50" r="40" fx="30" fy="30"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#34495e"/></radialGradient></defs><circle cx="50" cy="50" r="40" fill="url(#fxfy)"/></svg>""",
            "fx/fy offset from cx/cy: sphere-shading highlight")
    ) +

    "<h2>12 — preserveAspectRatio &amp; Nested Viewports</h2>" +
    Row(
        SvgSwatch("default: xMidYMid meet",
            """<svg viewBox="0 0 200 100" width="80" height="80"><rect x="0" y="0" width="100" height="100" fill="#3498db"/><rect x="100" y="0" width="100" height="100" fill="#e74c3c"/></svg>""",
            "2:1 viewBox into a square box: letterboxed"),
        SvgSwatch("xMidYMid slice",
            """<svg viewBox="0 0 200 100" width="80" height="80" preserveAspectRatio="xMidYMid slice"><rect x="0" y="0" width="100" height="100" fill="#3498db"/><rect x="100" y="0" width="100" height="100" fill="#e74c3c"/></svg>""",
            "same viewBox, slice: cropped, fills the box"),
        SvgSwatch("preserveAspectRatio=\"none\"",
            """<svg viewBox="0 0 200 100" width="80" height="80" preserveAspectRatio="none"><rect x="0" y="0" width="100" height="100" fill="#3498db"/><rect x="100" y="0" width="100" height="100" fill="#e74c3c"/></svg>""",
            "stretched independently per axis: distorted"),
        SvgSwatch("nested &lt;svg&gt;, own viewport",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect x="0" y="0" width="100" height="100" fill="#ecf0f1"/><svg x="15" y="15" width="70" height="70" viewBox="0 0 10 10"><circle cx="5" cy="5" r="5" fill="#8e44ad"/></svg></svg>""",
            "nested svg establishes its own coordinate system")
    ) +

    "<h2>13 — Style Cascade &amp; currentColor</h2>" +
    Row(
        SvgSwatch("style= overrides presentation attribute",
            """<svg viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35" fill="red" style="fill:#2ecc71"/></svg>""",
            "fill=\"red\" style=\"fill:#2ecc71\": style wins"),
        SvgSwatch("currentColor",
            """<span style="color:#e67e22"><svg viewBox="0 0 100 100" width="80" height="80"><circle cx="50" cy="50" r="35" fill="currentColor"/></svg></span>""",
            "fill=\"currentColor\" resolves the ancestor's CSS color"),
        "<td>" +
            $"<div class=\"sbox\"><img src=\"{styleClassDataUri}\" width=\"80\" height=\"80\"/></div>" +
            "<div class=\"desc\">&lt;style&gt; class selector</div>" +
            "<div class=\"css\">.hi/.lo rules, via &lt;img&gt; (see note below)</div>" +
        "</td>",
        "<td>" +
            $"<div class=\"sbox\"><img src=\"{styleIdDataUri}\" width=\"80\" height=\"80\"/></div>" +
            "<div class=\"desc\">&lt;style&gt; id selector</div>" +
            "<div class=\"css\">#target rule, via &lt;img&gt; (see note below)</div>" +
        "</td>"
    ) +
    "<p class=\"intro\">Note: a &lt;style&gt; element nested inside an <em>inline</em> &lt;svg&gt; has a known limitation in PeachPDF's HTML tokenizer, so the two &lt;style&gt; swatches above use the standalone &lt;img src=\"data:image/svg+xml\"&gt; path, which is unaffected — see supported-svg-features.md.</p>" +

    "<h2>14 — &lt;switch&gt; &amp; &lt;a&gt; Links</h2>" +
    Row(
        SvgSwatch("switch: first child wins",
            """<svg viewBox="0 0 100 100" width="80" height="80"><switch><rect x="20" y="20" width="60" height="60" fill="#e74c3c"/><circle cx="50" cy="50" r="35" fill="#3498db"/></switch></svg>""",
            "no requiredFeatures evaluation: always shows child 1"),
        SvgSwatch("switch: skips an unbuildable child",
            """<svg viewBox="0 0 100 100" width="80" height="80"><switch><metadata>ignored</metadata><circle cx="50" cy="50" r="35" fill="#27ae60"/></switch></svg>""",
            "&lt;metadata&gt; isn't renderable, so child 2 is used"),
        SvgSwatch("&lt;a href&gt;: real PDF link",
            """<svg viewBox="0 0 100 100" width="80" height="80"><a href="https://github.com/jhaygood86/PeachPDF"><circle cx="50" cy="50" r="35" fill="#2980b9" stroke="#1a5276" stroke-width="3"/></a></svg>""",
            "becomes a clickable PDF link annotation"),
        SvgSwatch("&lt;a&gt; with no href",
            """<svg viewBox="0 0 100 100" width="80" height="80"><a><rect x="20" y="20" width="60" height="60" fill="#7f8c8d"/></a></svg>""",
            "renders children normally, just isn't a link")
    ) +

    "<h2>15 — Markers</h2>" +
    Row(
        SvgSwatch("marker-end, orient=\"auto\"",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><marker id="arrow1" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 Z" fill="#c0392b"/></marker></defs><path d="M10,80 Q50,10 90,80" fill="none" stroke="#c0392b" stroke-width="3" marker-end="url(#arrow1)"/></svg>""",
            "marker rotates to follow the path's tangent"),
        SvgSwatch("marker-start/mid/end on every vertex",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><marker id="dot1" markerWidth="6" markerHeight="6" refX="3" refY="3" markerUnits="userSpaceOnUse"><circle cx="3" cy="3" r="3" fill="#2980b9"/></marker></defs><polyline points="10,80 30,20 50,70 70,15 90,60" fill="none" stroke="#95a5a6" stroke-width="2" marker-start="url(#dot1)" marker-mid="url(#dot1)" marker-end="url(#dot1)"/></svg>""",
            "one marker def, placed at start/mid/end vertices"),
        SvgSwatch("markerUnits=\"strokeWidth\" (default)",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><marker id="mScale" markerWidth="4" markerHeight="4" refX="2" refY="2" markerUnits="strokeWidth"><circle cx="2" cy="2" r="2" fill="#8e44ad"/></marker></defs><line x1="15" y1="30" x2="85" y2="30" stroke="#8e44ad" stroke-width="2" marker-end="url(#mScale)"/><line x1="15" y1="70" x2="85" y2="70" stroke="#8e44ad" stroke-width="10" marker-end="url(#mScale)"/></svg>""",
            "same marker, scales with each line's own stroke-width"),
        SvgSwatch("orient: fixed angle",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><marker id="tri" markerWidth="8" markerHeight="8" refX="4" refY="4" orient="45"><path d="M0,0 L8,0 L4,8 Z" fill="#16a085"/></marker></defs><polyline points="15,15 85,15 15,85 85,85" fill="none" stroke="none" marker-start="url(#tri)" marker-mid="url(#tri)" marker-end="url(#tri)"/></svg>""",
            "orient=\"45\": same fixed rotation at every vertex")
    ) +

    "<h2>16 — Pattern Fill</h2>" +
    Row(
        SvgSwatch("checkerboard",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><pattern id="checker" patternUnits="userSpaceOnUse" width="20" height="20"><rect width="10" height="10" fill="#ecf0f1"/><rect x="10" width="10" height="10" fill="#95a5a6"/><rect y="10" width="10" height="10" fill="#95a5a6"/><rect x="10" y="10" width="10" height="10" fill="#ecf0f1"/></pattern></defs><rect x="10" y="10" width="80" height="80" fill="url(#checker)"/></svg>""",
            "pattern of 4 &lt;rect&gt; tiles, patternUnits=\"userSpaceOnUse\""),
        SvgSwatch("polka dots",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><pattern id="dots" patternUnits="userSpaceOnUse" width="16" height="16"><circle cx="8" cy="8" r="4" fill="#e67e22"/></pattern></defs><circle cx="50" cy="50" r="40" fill="url(#dots)"/></svg>""",
            "dot pattern clipped to the circle's own geometry"),
        SvgSwatch("patternTransform=\"rotate(45)\"",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><pattern id="stripes" patternUnits="userSpaceOnUse" width="12" height="12" patternTransform="rotate(45)"><rect width="6" height="12" fill="#2ecc71"/></pattern></defs><rect x="10" y="10" width="80" height="80" fill="url(#stripes)"/></svg>""",
            "stripe tile rotated 45° via patternTransform"),
        SvgSwatch("pattern filling a star shape",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><defs><pattern id="grid" patternUnits="userSpaceOnUse" width="10" height="10"><rect width="10" height="10" fill="#fdebd0"/><rect width="10" height="2" fill="#e67e22"/><rect width="2" height="10" fill="#e67e22"/></pattern></defs><polygon points="{StarPoints}" fill="url(#grid)"/></svg>""",
            "pattern respects the star's own fill geometry")
    ) +

    "<h2>17 — Mask</h2>" +
    Row(
        SvgSwatch("linear-gradient luminance fade",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><linearGradient id="fadeGrad" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#000000"/></linearGradient><mask id="fadeMask" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><rect x="0" y="0" width="100" height="100" fill="url(#fadeGrad)"/></mask></defs><rect x="0" y="0" width="100" height="100" fill="#f1c40f"/><rect x="0" y="0" width="100" height="100" fill="#8e44ad" mask="url(#fadeMask)"/></svg>""",
            "gradient mask fades the purple rect over a yellow backdrop"),
        SvgSwatch("radial vignette",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><radialGradient id="vignetteGrad" gradientUnits="objectBoundingBox" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#000000"/></radialGradient><mask id="vignetteMask" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><rect x="0" y="0" width="100" height="100" fill="url(#vignetteGrad)"/></mask></defs><rect x="0" y="0" width="100" height="100" fill="#c0392b" mask="url(#vignetteMask)"/></svg>""",
            "radial mask: spotlight/vignette fade"),
        SvgSwatch("vector shape as mask",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><defs><mask id="starMask" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><polygon points="{StarPoints}" fill="#ffffff"/></mask></defs><rect x="0" y="0" width="100" height="100" fill="#16a085" mask="url(#starMask)"/></svg>""",
            "a shape, not just a gradient, as the mask's luminance"),
        SvgSwatch("&lt;text&gt; as mask content",
            """<svg viewBox="0 0 100 100" width="80" height="80"><defs><linearGradient id="textGrad" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ff9966"/><stop offset="1" stop-color="#ff5e62"/></linearGradient><mask id="textMask" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><text x="6" y="65" font-size="46" font-weight="bold" fill="#ffffff">PDF</text></mask></defs><rect x="0" y="0" width="100" height="100" fill="url(#textGrad)" mask="url(#textMask)"/></svg>""",
            "gradient shows only through the letter shapes")
    ) +

    "<h2>18 — &lt;image&gt; Element</h2>" +
    Row(
        SvgSwatch("raster data:image/png",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><image x="10" y="10" width="80" height="80" href="{rasterDataUri}"/></svg>""",
            "a real embedded raster image XObject"),
        SvgSwatch("vector data:image/svg+xml",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><image x="0" y="0" width="100" height="100" href="{nestedVectorDataUri}"/></svg>""",
            "stays real vector content, never rasterized"),
        SvgSwatch("preserveAspectRatio=\"none\" on &lt;image&gt;",
            $"""<svg viewBox="0 0 100 60" width="80" height="48"><image x="0" y="0" width="100" height="60" preserveAspectRatio="none" href="{rasterDataUri}"/></svg>""",
            "raster image stretched non-uniformly to fit its box"),
        SvgSwatch("&lt;image&gt; with clip-path",
            $"""<svg viewBox="0 0 100 100" width="80" height="80"><defs><circle id="imgClip" cx="50" cy="50" r="40"/><clipPath id="clipImg"><use xlink:href="#imgClip"/></clipPath></defs><image x="5" y="5" width="90" height="90" href="{rasterDataUri}" clip-path="url(#clipImg)"/></svg>""",
            "clip-path applies to &lt;image&gt; like any other element")
    ) +

    "<h2>19 — Text, tspan &amp; tref</h2>" +
    Row(
        SvgSwatch("&lt;text x y fill font-size&gt;",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect x="0" y="0" width="100" height="100" fill="#2c3e50"/><text x="10" y="55" font-size="20" fill="#ecf0f1">Peach</text></svg>""",
            "plain positioned text, baseline at (x, y)"),
        SvgSwatch("text-anchor: start/middle/end",
            """<svg viewBox="0 0 100 100" width="80" height="80"><line x1="50" y1="5" x2="50" y2="95" stroke="#bdc3c7" stroke-dasharray="3,3"/><text x="50" y="30" font-size="12" fill="#2980b9" text-anchor="start">start</text><text x="50" y="55" font-size="12" fill="#c0392b" text-anchor="middle">middle</text><text x="50" y="80" font-size="12" fill="#27ae60" text-anchor="end">end</text></svg>""",
            "all three anchored at x=50 (dashed guideline)"),
        SvgSwatch("&lt;tspan&gt; restyles mid-run",
            """<svg viewBox="0 0 100 100" width="80" height="80"><rect width="100" height="100" fill="#fdf2e9"/><text x="8" y="45" font-size="14" fill="#2c3e50">Hello <tspan fill="#c0392b" font-weight="bold">World</tspan></text></svg>""",
            "tspan flows right after \"Hello \", own fill+weight"),
        SvgSwatch("&lt;tspan x y&gt;: new line + font variety",
            """<svg viewBox="0 0 100 100" width="80" height="80"><text x="10"><tspan x="10" y="30" font-size="16" font-weight="bold" fill="#2980b9">Bold</tspan><tspan x="10" y="55" font-size="14" font-style="italic" fill="#c0392b">Italic</tspan><tspan x="10" y="80" font-size="12" fill="#16a085">Regular</tspan></text></svg>""",
            "own x/y starts a new line; size/weight/style vary")
    ) +

    "<h2>20 — Inline &lt;svg&gt; vs &lt;img src=\"data:image/svg+xml\"&gt;</h2>" +
    "<p class=\"intro\">The identical SVG markup rendered two ways: embedded directly in the HTML, and encoded as a base64 data: URI on an &lt;img&gt; tag. Both go through the same vector renderer.</p>" +
    "<table class=\"sw\"><tr>" +
    SvgSwatch("inline &lt;svg&gt;", parityMarkup, "&lt;svg&gt;...&lt;/svg&gt; inline in the HTML body") +
    "<td>" +
        $"<div class=\"sbox\"><img src=\"{parityDataUri}\" width=\"80\" height=\"80\"/></div>" +
        "<div class=\"desc\">&lt;img src=\"data:...\"&gt;</div>" +
        "<div class=\"css\">same markup, base64 data: URI</div>" +
    "</td>" +
    "</tr></table>" +

    "<h2>21 — Peach Showcase</h2>" +
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
    "<table class=\"sw\"><tr>" +
    PeachSwatch("Peach Branch &amp; Blossoms",
        """
        <svg viewBox="0 0 200 200" width="150" height="150">
          <defs>
            <marker id="blossom" markerWidth="8" markerHeight="8" refX="4" refY="4" markerUnits="userSpaceOnUse">
              <circle cx="4" cy="4" r="3.5" fill="#ffd1e3" stroke="#ff8fab" stroke-width="0.6"/>
            </marker>
            <marker id="leafTip" markerWidth="10" markerHeight="10" refX="9" refY="5" orient="auto">
              <path d="M0,5 C3,0 7,0 10,5 C7,10 3,10 0,5 Z" fill="#4caf50"/>
            </marker>
          </defs>
          <path d="M30,170 C50,130 70,90 110,55" fill="none" stroke="#6d4c30" stroke-width="6" stroke-linecap="round"
                marker-mid="url(#blossom)" marker-end="url(#leafTip)"/>
          <polyline points="55,140 75,120 95,100 110,55" fill="none" stroke="none" marker-mid="url(#blossom)"/>
          <text x="100" y="192" font-size="13" font-style="italic" fill="#6d4c30" text-anchor="middle">Prunus persica</text>
        </svg>
        """,
        "&lt;marker&gt; blossoms/leaf tip along a curved branch, italic &lt;text&gt; caption") +
    PeachSwatch("Peach Basket",
        """
        <svg viewBox="0 0 200 200" width="150" height="150">
          <defs>
            <pattern id="weave" patternUnits="userSpaceOnUse" width="14" height="14" patternTransform="rotate(20)">
              <rect width="14" height="14" fill="#c98a4b"/>
              <rect width="14" height="6" fill="#a86b34"/>
            </pattern>
            <radialGradient id="basketPeach" gradientUnits="userSpaceOnUse" cx="0" cy="-15" r="45">
              <stop offset="0" stop-color="#fff3b0"/>
              <stop offset="0.5" stop-color="#ffb347"/>
              <stop offset="1" stop-color="#d1495b"/>
            </radialGradient>
            <linearGradient id="basketShadow" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stop-color="#000000"/>
              <stop offset="1" stop-color="#000000" stop-opacity="0"/>
            </linearGradient>
            <mask id="basketShadowMask" maskUnits="objectBoundingBox" x="0" y="0" width="1" height="1">
              <rect x="20" y="150" width="160" height="30" fill="url(#basketShadow)"/>
            </mask>
          </defs>
          <ellipse cx="100" cy="165" rx="80" ry="15" fill="#000000" opacity="0.25" mask="url(#basketShadowMask)"/>
          <rect x="30" y="110" width="140" height="70" rx="10" fill="url(#weave)" stroke="#7a4a1e" stroke-width="3"/>
          <circle cx="70" cy="105" r="28" fill="url(#basketPeach)" transform="translate(0,0) rotate(-8,70,105)"/>
          <circle cx="120" cy="100" r="30" fill="url(#basketPeach)"/>
          <circle cx="150" cy="115" r="24" fill="url(#basketPeach)" transform="rotate(10,150,115)"/>
        </svg>
        """,
        "pattern-woven basket, rotate() on individual peaches, gradient drop-shadow via mask") +
    "</tr></table>" +

    "</body></html>";

await SaveShowcaseAsync("svg", "Graphics & Effects", "SVG",
    "Inline and embedded SVG rendered as true vector PDF content: shapes, paths, gradients, patterns, masks, and text.",
    svgHtml, pdfConfig);

// --- opacity showcase ---

static string OpacitySwatch(string desc, string bodyHtml, string cssLabel) =>
    "<td>" +
    $"<div class=\"stage\">{bodyHtml}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{cssLabel}</div>" +
    "</td>";

const string OpacityCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .stage { height: 90px; background: repeating-linear-gradient(45deg, #eee 0 8px, #fff 8px 16px); border: 1px solid #999; position: relative; overflow: hidden }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var opacityHtml = "<!DOCTYPE html><html><head>" + OpacityCss + "</head><body>" +

    "<h1>CSS opacity &amp; SVG group opacity Test Page</h1>" +
    "<p class=\"intro\">Each stage has a checkerboard-ish backdrop so translucency is visible. This exercises the isolated-transparency-group implementation (docs/html-css-support.md#opacity, docs/supported-svg-features.md).</p>" +

    "<h2>1 — Basic translucent box</h2>" +
    "<table class=\"sw\"><tr>" +
    OpacitySwatch("opacity: 1 (baseline)",
        "<div style=\"position:absolute;left:10px;top:10px;width:70px;height:70px;background:#e74c3c;\"></div>",
        "opacity: 1") +
    OpacitySwatch("opacity: 0.7",
        "<div style=\"position:absolute;left:10px;top:10px;width:70px;height:70px;background:#e74c3c;opacity:0.7;\"></div>",
        "opacity: 0.7") +
    OpacitySwatch("opacity: 0.3",
        "<div style=\"position:absolute;left:10px;top:10px;width:70px;height:70px;background:#e74c3c;opacity:0.3;\"></div>",
        "opacity: 0.3") +
    OpacitySwatch("opacity: 0 (invisible)",
        "<div style=\"position:absolute;left:10px;top:10px;width:70px;height:70px;background:#e74c3c;opacity:0;\"></div>",
        "opacity: 0") +
    "</tr></table>" +

    "<h2>2 — Overlapping children under one parent opacity (double-blend proof)</h2>" +
    "<p class=\"intro\">The overlap should look like a single flat blend, not a darker double-blend - this is the isolated transparency-group fix in action.</p>" +
    "<table class=\"sw\"><tr>" +
    OpacitySwatch("two opaque overlapping boxes (baseline)",
        "<div style=\"position:absolute;left:10px;top:15px;width:50px;height:50px;background:#e74c3c;\"></div>" +
        "<div style=\"position:absolute;left:35px;top:35px;width:50px;height:50px;background:#3498db;\"></div>",
        "no opacity - opaque overlap") +
    OpacitySwatch("parent opacity:0.5, opaque children",
        "<div style=\"position:absolute;left:0;top:0;width:100%;height:100%;opacity:0.5;\">" +
        "<div style=\"position:absolute;left:10px;top:15px;width:50px;height:50px;background:#e74c3c;\"></div>" +
        "<div style=\"position:absolute;left:35px;top:35px;width:50px;height:50px;background:#3498db;\"></div>" +
        "</div>",
        "parent opacity: 0.5 (children opaque)") +
    OpacitySwatch("nested opacity compounding",
        "<div style=\"position:absolute;left:0;top:0;width:100%;height:100%;opacity:0.6;\">" +
        "<div style=\"position:absolute;left:10px;top:15px;width:70px;height:60px;background:#e74c3c;opacity:0.6;\"></div>" +
        "</div>",
        "parent 0.6 &times; child 0.6 = 0.36 effective") +
    OpacitySwatch("opacity + transform combined",
        "<div style=\"position:absolute;left:20px;top:20px;width:60px;height:50px;background:#8e44ad;opacity:0.5;transform:rotate(15deg);\"></div>",
        "opacity: 0.5; transform: rotate(15deg)") +
    "</tr></table>" +

    "<h2>3 — Opacity over images and gradients</h2>" +
    "<table class=\"sw\"><tr>" +
    OpacitySwatch("opacity on a box containing a raster &lt;img&gt;",
        "<div style=\"position:absolute;left:10px;top:10px;width:70px;height:70px;opacity:0.5;\">" +
        "<img src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==\" width=\"70\" height=\"70\" />" +
        "</div>",
        "opacity: 0.5 (img child)") +
    OpacitySwatch("opacity on a gradient background",
        "<div style=\"position:absolute;left:10px;top:10px;width:70px;height:70px;background:linear-gradient(to right,#e74c3c,#3498db);opacity:0.5;\"></div>",
        "opacity: 0.5 (gradient background)") +
    "</tr></table>" +

    "<h2>4 — SVG &lt;g opacity&gt; (the SVG equivalent)</h2>" +
    "<p class=\"intro\">SVG group opacity now uses the same isolated-transparency-group compositing as CSS opacity - the overlap below should also look like a single flat blend.</p>" +
    "<table class=\"sw\"><tr>" +
    OpacitySwatch("&lt;g opacity=\"0.5\"&gt; with overlapping shapes",
        """
        <svg viewBox="0 0 100 100" width="90" height="90">
          <g opacity="0.5">
            <rect x="10" y="15" width="50" height="50" fill="#e74c3c"/>
            <rect x="35" y="35" width="50" height="50" fill="#3498db"/>
          </g>
        </svg>
        """,
        "&lt;g opacity=\"0.5\"&gt;, two overlapping rects") +
    OpacitySwatch("leaf fill-opacity vs group opacity",
        """
        <svg viewBox="0 0 100 100" width="90" height="90">
          <g opacity="0.6">
            <rect x="10" y="10" width="80" height="80" fill="#8e44ad" fill-opacity="0.5"/>
          </g>
        </svg>
        """,
        "group opacity 0.6 &times; leaf fill-opacity 0.5") +
    "</tr></table>" +

    "</body></html>";

await SaveShowcaseAsync("opacity", "Graphics & Effects", "Opacity",
    "Element opacity composited as real group transparency over text, images, and nested content.",
    opacityHtml, pdfConfig);

// --- stacking context showcase ---
//
// These three cases were all previously silently broken by the stacking-context algorithm only
// recognizing position+z-index (not opacity/transform), plus two pre-existing paint-order bugs it
// had: (1) any box establishing its own stacking context - including plain position:relative;
// z-index siblings - was dropped and never painted at all; (2) an out-of-flow stacking-context
// descendant nested a few plain wrapper divs deep painted during the wrong z-index layer's timing.
// See docs/html-css-support.md#stacking-context.

static string StackingSwatch(string desc, string bodyHtml, string cssLabel) =>
    "<td>" +
    $"<div class=\"stage\">{bodyHtml}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{cssLabel}</div>" +
    "</td>";

const string StackingCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 33% }
    .stage { height: 130px; border: 1px solid #999; position: relative; }
    .chip { position: absolute; width: 90px; height: 90px; color: #fff; font-size: 8pt;
            text-align: center; line-height: 90px; }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    .css { font-size: 5.5pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var stackingHtml = "<!DOCTYPE html><html><head>" + StackingCss + "</head><body>" +

    "<h1>Stacking Context Test Page</h1>" +
    "<p class=\"intro\">Exercises the CSS stacking-context algorithm (MDN: Positioned layout &gt; Stacking context) - z-index ordering, opacity/transform establishing a stacking context, and out-of-flow content escaping a plain wrapper to compete at its true enclosing stacking context.</p>" +

    "<h2>1 &mdash; z-index siblings, negative and positive</h2>" +
    "<table class=\"sw\"><tr>" +
    StackingSwatch("both siblings paint, in z-index order",
        "<div class=\"chip\" style=\"position:relative;left:10px;top:10px;z-index:1;background:#c0392b;\">z: 1 (back)</div>" +
        "<div class=\"chip\" style=\"position:relative;left:55px;top:-55px;z-index:2;background:#2980b9;\">z: 2 (front)</div>",
        "position:relative;z-index:1 / z-index:2") +
    "</tr></table>" +

    "<h2>2 &mdash; opacity establishes a stacking context</h2>" +
    "<table class=\"sw\"><tr>" +
    StackingSwatch("absolutely-positioned child fades with its opacity parent",
        "<div style=\"position:absolute;left:5px;top:15px;width:180px;height:100px;opacity:0.5;background:#27ae60;\">" +
        "<div class=\"chip\" style=\"left:50px;top:15px;background:#e67e22;\">abs child</div>" +
        "</div>",
        "opacity:0.5 parent, position:absolute child (no z-index)") +
    StackingSwatch("transform establishes a stacking context",
        "<div style=\"position:absolute;left:5px;top:15px;width:180px;height:100px;transform:rotate(6deg);background:#27ae60;\">" +
        "<div class=\"chip\" style=\"left:50px;top:15px;background:#e67e22;\">abs child</div>" +
        "</div>",
        "transform:rotate(6deg) parent, position:absolute child (no z-index)") +
    "</tr></table>" +

    "<h2>3 &mdash; escaping a non-stacking-context wrapper</h2>" +
    "<p class=\"intro\">The nested box's own z-index:-1 competes against its true enclosing stacking context (the page), not just its immediate position:absolute parent (which has no z-index of its own, so does not itself establish a stacking context) - it must paint behind the sibling, not be trapped painting whenever the wrapper happens to.</p>" +
    "<table class=\"sw\"><tr>" +
    StackingSwatch("z-index:-1 box nested in a plain absolute wrapper",
        "<div class=\"chip\" style=\"position:relative;left:10px;top:10px;z-index:0;background:#8e44ad;\">sibling z: 0</div>" +
        "<div style=\"position:absolute;left:70px;top:10px;width:90px;height:90px;\">" +
        "<div class=\"chip\" style=\"position:relative;z-index:-1;background:#7f4020;\">nested z: -1</div>" +
        "</div>",
        "position:absolute wrapper (no z-index) &gt; position:relative;z-index:-1") +
    "</tr></table>" +

    "</body></html>";

await SaveShowcaseAsync("stacking_context", "Graphics & Effects", "Stacking Contexts",
    "z-index and stacking contexts: paint order across positioned, floated, and transformed boxes.",
    stackingHtml, pdfConfig);

// --- text-transform showcase ---

static string TextTransformSwatch(string desc, string text, string cssValue) =>
    "<td>" +
    $"<div class=\"ttbox\" style=\"text-transform: {cssValue}\">{text}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">text-transform: {cssValue}</div>" +
    "</td>";

static string TextTransformSwatchHtml(string desc, string bodyHtml, string cssLabel) =>
    "<td>" +
    $"<div class=\"ttbox\">{bodyHtml}</div>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">{cssLabel}</div>" +
    "</td>";

const string TextTransformCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .ttbox { font-size: 11pt; border: 1px solid #999; background: #f7f7f7; padding: 8px; margin-bottom: 3px; min-height: 1.4em }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin: 2px 0 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var textTransformHtml = "<!DOCTYPE html><html><head>" + TextTransformCss + "</head><body>" +

    "<h1>CSS text-transform Test Page</h1>" +

    "<h2>1 — Core Keywords</h2>" +
    Row(
        TextTransformSwatch("none (baseline)", "Hello World", "none"),
        TextTransformSwatch("uppercase", "hello world", "uppercase"),
        TextTransformSwatch("lowercase", "HELLO WORLD", "lowercase"),
        TextTransformSwatch("capitalize", "hello world", "capitalize")
    ) +

    "<h2>2 — Capitalize: Word-Boundary Edge Cases</h2>" +
    "<p class=\"intro\">Capitalize only uppercases the first letter of each whitespace-delimited word - a hyphenated compound stays one word, not three separately-capitalized fragments.</p>" +
    Row(
        TextTransformSwatch("hyphenated compound", "editor-in-chief", "capitalize"),
        TextTransformSwatch("already mixed case", "hELLO wORLD", "capitalize"),
        TextTransformSwatch("leading punctuation", "&#39;twas the night", "capitalize"),
        TextTransformSwatch("multiple spaces", "one   two   three", "capitalize")
    ) +

    "<h2>3 — Inheritance</h2>" +
    "<p class=\"intro\">text-transform is an inherited property - a child inline element picks it up from its parent unless it sets its own value.</p>" +
    Row(
        TextTransformSwatchHtml("child inherits parent's uppercase",
            "<div style=\"text-transform: uppercase\">parent <span>child inherits</span></div>",
            "div { text-transform: uppercase } span (no override)"),
        TextTransformSwatchHtml("child overrides to none",
            "<div style=\"text-transform: uppercase\">parent <span style=\"text-transform: none\">child overrides</span></div>",
            "span { text-transform: none }"),
        TextTransformSwatchHtml("child overrides to capitalize",
            "<div style=\"text-transform: lowercase\">PARENT <span style=\"text-transform: capitalize\">child override</span></div>",
            "div { text-transform: lowercase } span { text-transform: capitalize }"),
        TextTransformSwatchHtml("baseline, no transform anywhere",
            "<div>no transform <span>plain child</span></div>",
            "(none set)")
    ) +

    "<h2>4 — Combined With Other Text Properties</h2>" +
    Row(
        TextTransformSwatchHtml("uppercase + bold", "<div style=\"text-transform: uppercase; font-weight: bold\">important notice</div>", "text-transform: uppercase; font-weight: bold"),
        TextTransformSwatchHtml("capitalize + centered", "<div style=\"text-transform: capitalize; text-align: center\">quarterly report</div>", "text-transform: capitalize; text-align: center"),
        TextTransformSwatchHtml("uppercase + underline", "<div style=\"text-transform: uppercase; text-decoration: underline\">click here</div>", "text-transform: uppercase; text-decoration: underline"),
        TextTransformSwatchHtml("capitalize list marker text", "<ul style=\"margin:0;padding-left:1.2em;text-transform: capitalize\"><li>first item</li><li>second item</li></ul>", "ul { text-transform: capitalize }")
    ) +

    "<h2>5 — Long-Form Paragraph (Layout Correctness)</h2>" +
    "<p class=\"intro\">Confirms word-wrapping and width measurement operate on the transformed text, not the original - the paragraph below wraps the same as untransformed text of equal length would.</p>" +
    Row(
        TextTransformSwatchHtml("uppercase paragraph, narrow column",
            "<p style=\"text-transform: uppercase; width: 140px; margin: 0;\">the quick brown fox jumps over the lazy dog near the riverbank at dawn.</p>",
            "text-transform: uppercase; width: 140px"),
        TextTransformSwatchHtml("capitalize paragraph, narrow column",
            "<p style=\"text-transform: capitalize; width: 140px; margin: 0;\">the quick brown fox jumps over the lazy dog near the riverbank at dawn.</p>",
            "text-transform: capitalize; width: 140px")
    ) +

    "</body></html>";

await SaveShowcaseAsync("text_transform", "Typography & Text", "text-transform",
    "text-transform variants - uppercase, lowercase, capitalize - applied at render time.",
    textTransformHtml, pdfConfig);

// --- CSS Multi-column Layout showcase ---

static string McSection(string title, string bodyHtml) =>
    $"<h2>{title}</h2>{bodyHtml}";

static string McEntry(string term, string body) =>
    $"<p><b>{term}</b> — {body}</p>";

static string McEntries(int count, string prefix = "entry") =>
    string.Concat(Enumerable.Range(1, count).Select(i =>
        McEntry($"{prefix}-{i}", $"a short definition for {prefix} number {i}, just long enough to wrap onto a second line in a narrow column.")));

const string MulticolCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    .mc p { margin: 0 0 0.4em; text-align: justify }
    .frame { border: 1px solid #bbb; padding: 6px; margin-bottom: 8px; background: #fafafa }
    .label { font-size: 6.5pt; color: #666; margin-bottom: 3px }
    </style>
    """;

var multicolHtml = "<!DOCTYPE html><html><head>" + MulticolCss + "</head><body>" +

    "<h1>CSS Multi-column Layout Test Page</h1>" +
    "<p class=\"intro\">Each frame below is one multi-column container. Compare column count/width/rule placement against the label.</p>" +

    McSection("1 &mdash; column-count: 2, short entries",
        "<div class=\"frame\"><div class=\"label\">columns: 2; column-rule: 0.5pt solid #000</div>" +
        "<div class=\"mc\" style=\"columns:2;column-rule:0.5pt solid #000\">" + McEntries(10) + "</div></div>") +

    McSection("2 &mdash; column-count: 3, colored rule",
        "<div class=\"frame\"><div class=\"label\">column-count: 3; column-rule: 2px dashed #c0392b; column-gap: 20px</div>" +
        "<div class=\"mc\" style=\"column-count:3;column-rule:2px dashed #c0392b;column-gap:20px\">" + McEntries(9) + "</div></div>") +

    McSection("3 &mdash; column-width (auto column count)",
        "<div class=\"frame\"><div class=\"label\">column-width: 140px; column-rule: 1px dotted #2980b9 (as many columns as fit)</div>" +
        "<div class=\"mc\" style=\"column-width:140px;column-rule:1px dotted #2980b9\">" + McEntries(8) + "</div></div>") +

    McSection("4 &mdash; page-spanning columns",
        "<div class=\"frame\"><div class=\"label\">columns: 2; enough entries to overflow onto a second page, still 2 columns wide there</div>" +
        "<div class=\"mc\" style=\"columns:2;column-rule:0.5pt solid #000\">" + McEntries(40) + "</div></div>") +

    McSection("5 &mdash; dictionary-style (mirrors css4.pub's Icelandic dictionary)",
        "<div class=\"frame\"><div class=\"label\">columns: 2; column-rule: 0.2pt solid black; text-align: justify; large centered heading as first child</div>" +
        "<div class=\"mc\" style=\"columns:2;column-rule:0.2pt solid black\">" +
        "<h2 style=\"font-size:28pt;text-align:center;margin:0 0 0.2em;border:none\">A</h2>" +
        McEntries(14, "word") + "</div></div>") +

    "</body></html>";

await SaveShowcaseAsync("multicol", "Layout", "Multi-column Layout",
    "CSS multi-column layout: column counts, widths, gaps, and column rules.",
    multicolHtml, pdfConfig);

// --- hyphens: auto multi-language showcase ---
// Document language is a whole-container setting (<html lang>, see CssBox/HtmlContainerInt), so
// each language gets its own small document rather than one page per language like the other
// showcases above.

const string HyphenationCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 9pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    p.intro { margin: 0 0 0.7em; color: #555; font-size: 7.5pt; break-after: avoid }
    .col { width: 90px; border: 1px solid #bbb; padding: 6px; hyphens: auto; text-align: justify; float: left; margin-right: 10px }
    </style>
    """;

// tag: the document's <html lang>. words: a few long words known to have real hyphenation
// points in that language's pattern set, forced to wrap in a narrow column so any hyphenation
// is actually exercised (a real fix for the ASCII-only alphabet gate bug — see
// HyphenationEngine's Unicode-letter check — would have shipped correct-looking pattern data
// that silently never activated for non-Latin scripts like Russian below).
(string Tag, string Title, string[] Words)[] hyphenationShowcases =
[
    ("en-US", "English (en-US)", ["antidisestablishmentarianism", "internationalization", "hyphenation"]),
    ("de-DE", "German (de-DE, reformed orthography default)", ["Rechtsschutzversicherungsgesellschaften", "Konstitution", "Donaudampfschifffahrt"]),
    ("fr", "French (fr)", ["anticonstitutionnellement", "extraordinairement"]),
    ("ru", "Russian (ru, Cyrillic script)", ["предпринимательство", "информационный", "образовательного"])
];

foreach (var (tag, title, words) in hyphenationShowcases)
{
    var wordsHtml = string.Concat(words.Select(w => $"<p>{w}</p>"));
    var hyphenationHtml = $"<!DOCTYPE html><html lang=\"{tag}\">" +
        "<head>" + HyphenationCss + "</head><body>" +
        $"<h1>hyphens: auto — {title}</h1>" +
        "<p class=\"intro\">Each narrow column below forces these long words to wrap; hyphens:auto should split them at real language-appropriate break points instead of just overflowing/wrapping whole.</p>" +
        $"<div class=\"col\">{wordsHtml}</div>" +
        "</body></html>";

    var fileTag = tag.ToLowerInvariant().Replace("-", "_");
    await SaveShowcaseAsync($"hyphenation_{fileTag}", "Typography & Text", $"Hyphenation \u2014 {title}",
        "Automatic hyphenation (hyphens: auto) splitting long words at real, language-appropriate break points in a narrow justified column.",
        hyphenationHtml, pdfConfig);
}

// --- Tagged PDF (PDF/UA) showcase ---
// Tags are invisible in a normal page render, so this showcase's value is manual inspection of
// the structure tree (e.g. Acrobat's Tags panel, or another PDF/UA-aware checker) rather than
// visual comparison - it's included anyway per this repo's convention that a new visible
// capability gets a showcase, since several real rendering bugs elsewhere have only ever been
// caught by someone actually opening a showcase's output (see CLAUDE.md's Testing conventions).

const string TaggedPdfCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 10pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 16pt }
    h2 { font-size: 11pt; margin-top: 1.2em; break-after: avoid }
    table { border-collapse: collapse; width: 100% }
    td, th { border: 0.5pt solid #999; padding: 4px; text-align: left }
    .pull-quote { border-left: 3px solid #999; padding-left: 8px; color: #555; margin: 0.6em 0 }
    </style>
    """;

// A 1x1 transparent PNG - same fixture used by TaggedPdfStructureTreeTests.
const string TaggedPdfTinyPngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

var taggedPdfHtml = "<!DOCTYPE html><html lang=\"en-US\"><head>" + TaggedPdfCss + "</head><body>" +

    "<h1>Tagged PDF (PDF/UA) Showcase</h1>" +
    "<p>This document exercises PeachPDF's optional tagged-PDF output (<code>PdfGenerateConfig.EnableTaggedPdf</code>): headings, a paragraph, lists, a table, an image with alt text, a link, HTML5 sectioning elements, and both an explicit <code>-peachpdf-pdf-tag-type</code> override and a <code>none</code> suppression.</p>" +

    "<section>" +
    "<h2>Lists</h2>" +
    "<ul><li>First item</li><li>Second item</li></ul>" +
    "<ol><li>Step one</li><li>Step two</li></ol>" +
    "</section>" +

    "<section>" +
    "<h2>Table</h2>" +
    "<table><thead><tr><th>Name</th><th>Role</th></tr></thead>" +
    "<tbody>" +
    "<tr><td>Ada Lovelace</td><td>Mathematician</td></tr>" +
    "<tr><td>Alan Turing</td><td>Computer Scientist</td></tr>" +
    "</tbody></table>" +
    "</section>" +

    "<section>" +
    "<h2>Image and link</h2>" +
    $"<img src=\"data:image/png;base64,{TaggedPdfTinyPngBase64}\" alt=\"A small red square\" width=\"16\" height=\"16\" />" +
    "<p><a href=\"https://github.com/jhaygood86/PeachPDF\">PeachPDF on GitHub</a> - the underlying Link annotation is cross-referenced with its /Link structure element in both directions (/OBJR and /StructParent).</p>" +
    "</section>" +

    "<section>" +
    "<h2>-peachpdf-pdf-tag-type overrides</h2>" +
    "<div class=\"pull-quote\" style=\"-peachpdf-pdf-tag-type: BlockQuote\">This &lt;div&gt; is promoted to /BlockQuote via an explicit -peachpdf-pdf-tag-type override.</div>" +
    "<div style=\"-peachpdf-pdf-tag-type: none\"><p>This paragraph's wrapper &lt;div&gt; is tagged none, so it is invisible in the structure tree - the &lt;p&gt; below attaches directly to the nearest tagged ancestor instead.</p></div>" +
    "</section>" +

    "<blockquote>A real &lt;blockquote&gt;, tagged /BlockQuote by the default stylesheet (no override needed).</blockquote>" +

    "</body></html>";

var taggedPdfConfig = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
await SaveShowcaseAsync("tagged_pdf", "Standards & Accessibility", "Tagged PDF (PDF/UA)",
    "Accessible, tagged PDF output: a logical structure tree with headings, lists, tables, alt text, links, and tag-type overrides.",
    taggedPdfHtml, taggedPdfConfig);

// --- border-style showcase ---

const string BorderStyleCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .bsbox { height: 48px; background: #eee; margin-bottom: 3px }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var borderStyleHtml = "<!DOCTYPE html><html><head>" + BorderStyleCss + "</head><body>" +

    "<h1>CSS1 border-style Test Page</h1>" +

    "<h2>All CSS1 border-style keywords</h2>" +
    Row(
        BorderStyleSwatch("none", "none"),
        BorderStyleSwatch("hidden", "hidden"),
        BorderStyleSwatch("solid", "solid"),
        BorderStyleSwatch("dotted", "dotted")
    ) +
    Row(
        BorderStyleSwatch("dashed", "dashed"),
        BorderStyleSwatch("double", "double"),
        BorderStyleSwatch("groove", "groove"),
        BorderStyleSwatch("ridge", "ridge")
    ) +
    Row(
        BorderStyleSwatch("inset", "inset"),
        BorderStyleSwatch("outset", "outset")
    ) +

    "</body></html>";

await SaveShowcaseAsync("border_style", "Backgrounds & Borders", "Border Styles",
    "The full set of CSS border styles, from solid and dashed to groove, ridge, inset, and outset.",
    borderStyleHtml, pdfConfig);

// --- vertical-align showcase ---

static string VerticalAlignSwatch(string desc, string va) =>
    "<td>" +
    "<p class=\"vabox\">" +
    "<span class=\"tall\">TALL</span> " +
    $"<span class=\"target\" style=\"vertical-align:{va}\">aligned</span>" +
    "</p>" +
    $"<div class=\"desc\">{desc}</div>" +
    $"<div class=\"css\">vertical-align: {va}</div>" +
    "</td>";

const string VerticalAlignCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 8.5pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
    table.sw td { padding: 3px; vertical-align: top; width: 25% }
    .vabox { border: 1px solid #999; margin-bottom: 3px; white-space: nowrap }
    .tall { font-size: 30pt }
    .target { font-size: 10pt; background: #ffe58a }
    .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    .css { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
    </style>
    """;

var verticalAlignHtml = "<!DOCTYPE html><html><head>" + VerticalAlignCss + "</head><body>" +

    "<h1>CSS1 vertical-align Test Page</h1>" +
    "<p class=\"intro\" style=\"font-size:7pt;color:#555\">Each swatch shows a 30pt \"TALL\" span establishing the line's height, with a highlighted 10pt \"aligned\" span positioned per the labeled value.</p>" +

    "<h2>All CSS1 vertical-align keywords</h2>" +
    Row(
        VerticalAlignSwatch("baseline (default)", "baseline"),
        VerticalAlignSwatch("sub", "sub"),
        VerticalAlignSwatch("super", "super"),
        VerticalAlignSwatch("top", "top")
    ) +
    Row(
        VerticalAlignSwatch("middle", "middle"),
        VerticalAlignSwatch("bottom", "bottom"),
        VerticalAlignSwatch("text-top", "text-top"),
        VerticalAlignSwatch("text-bottom", "text-bottom")
    ) +

    "</body></html>";

await SaveShowcaseAsync("vertical_align", "Typography & Text", "Vertical Align",
    "vertical-align behaviors for inline content, from baseline and middle to explicit offsets.",
    verticalAlignHtml, pdfConfig);

// --- letter-spacing / word-spacing showcase ---

const string SpacingCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 12pt Arial, sans-serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; break-after: avoid }
    .row { margin-bottom: 0.6em }
    .label { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
    </style>
    """;

var spacingHtml = "<!DOCTYPE html><html><head>" + SpacingCss + "</head><body>" +

    "<h1>CSS1 letter-spacing / word-spacing Test Page</h1>" +

    "<h2>letter-spacing</h2>" +
    "<div class=\"row\"><div class=\"label\">normal</div><div style=\"letter-spacing:normal\">The quick brown fox jumps over the lazy dog</div></div>" +
    "<div class=\"row\"><div class=\"label\">1px</div><div style=\"letter-spacing:1px\">The quick brown fox jumps over the lazy dog</div></div>" +
    "<div class=\"row\"><div class=\"label\">4px</div><div style=\"letter-spacing:4px\">The quick brown fox jumps over the lazy dog</div></div>" +
    "<div class=\"row\"><div class=\"label\">-1px (tightened)</div><div style=\"letter-spacing:-1px\">The quick brown fox jumps over the lazy dog</div></div>" +

    "<h2>word-spacing (for contrast)</h2>" +
    "<div class=\"row\"><div class=\"label\">normal</div><div style=\"word-spacing:normal\">The quick brown fox jumps over the lazy dog</div></div>" +
    "<div class=\"row\"><div class=\"label\">10px</div><div style=\"word-spacing:10px\">The quick brown fox jumps over the lazy dog</div></div>" +
    "<div class=\"row\"><div class=\"label\">25px</div><div style=\"word-spacing:25px\">The quick brown fox jumps over the lazy dog</div></div>" +

    "<h2>Combined (inherited from parent, per CSS1)</h2>" +
    "<div class=\"row\" style=\"letter-spacing:2px; word-spacing:8px\">" +
    "<div class=\"label\">div has letter-spacing:2px; word-spacing:8px - nested span inherits both</div>" +
    "<span>The quick brown fox</span> <b>jumps over</b> the lazy dog" +
    "</div>" +

    "</body></html>";

await SaveShowcaseAsync("letter_word_spacing", "Typography & Text", "Letter & Word Spacing",
    "letter-spacing and word-spacing adjustments across text runs.",
    spacingHtml, pdfConfig);

// --- ::first-letter showcase ---

const string FirstLetterCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 11pt Georgia, serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em; font-family: Arial, sans-serif }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; font-family: Arial, sans-serif; break-after: avoid }
    p { margin: 0 0 0.8em }
    .dropcap::first-letter { font-size: 300%; float: left; color: crimson; font-weight: bold; line-height: 0.8; padding-right: 4px }
    .colored::first-letter { color: rgb(0,102,204); font-size: 150% }
    </style>
    """;

var firstLetterHtml = "<!DOCTYPE html><html><head>" + FirstLetterCss + "</head><body>" +

    "<h1>CSS1 ::first-letter Test Page</h1>" +

    "<h2>Classic drop cap</h2>" +
    "<p class=\"dropcap\">Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation.</p>" +

    "<h2>Nested inline content</h2>" +
    "<p class=\"colored\"><em>Emphasized</em> text starts this paragraph, and the very first letter should still pick up the first-letter color even though it begins inside the nested &lt;em&gt;.</p>" +

    "<h2>Leading punctuation</h2>" +
    "<p class=\"colored\">&#8220;Quoted&#8221; text at the start of a paragraph includes the opening quote mark as part of the first-letter unit, per CSS1 &sect;1.2.</p>" +

    "</body></html>";

await SaveShowcaseAsync("first_letter", "Typography & Text", "::first-letter",
    "The ::first-letter pseudo-element: drop-cap style initial letter formatting.",
    firstLetterHtml, pdfConfig);

// --- ::first-line showcase ---

const string FirstLineCss = """
    <style>
    @page { size: a4; margin: 15mm }
    body { font: 11pt Georgia, serif; margin: 0 }
    h1 { font-size: 15pt; margin: 0 0 0.3em; font-family: Arial, sans-serif }
    h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999; font-family: Arial, sans-serif; break-after: avoid }
    p { margin: 0 0 0.8em; width: 350px }
    .lede::first-line { font-weight: bold; color: darkslateblue; font-variant: small-caps }
    .bigfont::first-line { font-size: 200% }
    .allseven::first-line {
      font-style: italic;
      color: rgb(180,60,0);
      background-color: rgb(255,240,200);
      text-decoration: underline;
      word-spacing: 6px;
      letter-spacing: 1px;
      text-transform: uppercase;
    }
    </style>
    """;

var firstLineHtml = "<!DOCTYPE html><html><head>" + FirstLineCss + "</head><body>" +

    "<h1>CSS2.1 ::first-line Test Page</h1>" +

    "<h2>Non-width-affecting (font-weight/color/small-caps)</h2>" +
    "<p class=\"lede\">Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua, only the first line should look different from the rest of this paragraph.</p>" +

    "<h2>Width-affecting (bigger first-line font-size, wrap point shifts)</h2>" +
    "<p class=\"bigfont\">Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat, and this line wraps once the bigger first-line font runs out of room.</p>" +

    "<h2>All 7 supported properties at once</h2>" +
    "<p class=\"allseven\">Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur, demonstrating font-style, color, background, decoration, spacing, and text-transform together on line one only.</p>" +

    "</body></html>";

await SaveShowcaseAsync("first_line", "Typography & Text", "::first-line",
    "The ::first-line pseudo-element styling the first rendered line of a paragraph.",
    firstLineHtml, pdfConfig);

// --- CSS1 canvas background showcase ---

var canvasBackgroundHtml = "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm }" +
    "body { font: 11pt Georgia, serif; margin: 0; background-color: rgb(230,240,255) }" +
    "h1 { font-size: 15pt; margin: 0; padding: 15mm 15mm 0.3em; font-family: Arial, sans-serif }" +
    "p { margin: 0 15mm 0.8em }" +
    "</style></head><body>" +
    "<h1>CSS1 Canvas Background Test Page</h1>" +
    "<p>The pale blue background here comes from &lt;body&gt;'s own background-color, but it fills the " +
    "whole page canvas (per CSS2.1 &sect;14.2) - not just the height of this short paragraph, which is " +
    "nowhere near a full page tall.</p>" +
    "</body></html>";

await SaveShowcaseAsync("canvas_background", "Backgrounds & Borders", "Canvas Background",
    "CSS1 canvas background propagation: body and html backgrounds covering the full page.",
    canvasBackgroundHtml, pdfConfig);

// ─── Font resolution showcase (CSS font-resolution compliance pass) ───────
// Uses only real, already-installed system fonts, referenced via @font-face src: local()
// fallback chains (matched by full font name) - each chain lists the docs build machine's
// face (Roboto, installed by pages.yml) first, then the local-Windows equivalent (Segoe UI /
// Arial). A rule whose local() candidates all miss simply doesn't register, so every section
// renders on both platforms from whichever real faces exist, with no bundled binary assets.

var fontShowcase = new StringBuilder();

// Nearest-weight matching (CSS Fonts Level 4 §5.2): "WeightDemo" is assembled from real
// multi-weight faces (Roboto's six weights on the docs build machine, Segoe UI's five on
// Windows) - requested weights with no exact face show the nearest-weight search.
fontShowcase.Append("<style>" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 100; src: local('Roboto Thin'); }" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 300; src: local('Roboto Light'), local('Segoe UI Light'); }" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 400; src: local('Roboto'), local('Segoe UI'), local('Arial'); }" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 500; src: local('Roboto Medium'); }" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 600; src: local('Segoe UI Semibold'); }" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 700; src: local('Roboto Bold'), local('Segoe UI Bold'), local('Arial Bold'); }" +
    "@font-face { font-family: 'WeightDemo'; font-weight: 900; src: local('Roboto Black'), local('Segoe UI Black'); }" +
    "</style>");
fontShowcase.Append("<h2>Nearest-weight matching (CSS Fonts Level 4 &sect;5.2)</h2>");
fontShowcase.Append("<p class=\"note\">\"WeightDemo\" is built via <code>@font-face src: local()</code> from whichever real installed faces this machine provides (Roboto's six weights on the docs build machine, Segoe UI's five on Windows). Requested weights with no exact face are matched to the nearest real one per the &sect;5.2 search order.</p>");
foreach (var weight in new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900 })
{
    fontShowcase.Append($"<p style=\"font-family: WeightDemo; font-weight: {weight}\">font-weight: {weight} &mdash; The quick brown fox jumps over the lazy dog</p>");
}

fontShowcase.Append("<h2>Faux-bold / faux-italic synthesis</h2>");
fontShowcase.Append("<p class=\"note\">\"SynthDemo\" is registered via @font-face from a single Regular-only face - bold/italic requests have no real matching face to use, so the renderer synthesizes them (fill+stroke render mode for bold, glyph shear for italic).</p>");
fontShowcase.Append("<style>@font-face { font-family: 'SynthDemo'; src: local('Roboto'), local('Segoe UI'), local('Arial'); }</style>");
fontShowcase.Append("<p style=\"font-family: SynthDemo\">Regular (the one real registered face)</p>");
fontShowcase.Append("<p style=\"font-family: SynthDemo; font-weight: bold\">Bold (synthesized: fill+stroke)</p>");
fontShowcase.Append("<p style=\"font-family: SynthDemo; font-style: italic\">Italic (synthesized: fixed default shear)</p>");
fontShowcase.Append("<p style=\"font-family: SynthDemo; font-weight: bold; font-style: italic\">Bold Italic (both synthesized)</p>");

fontShowcase.Append("<h2>CSS Fonts Level 4 <code>oblique &lt;angle&gt;</code></h2>");
fontShowcase.Append("<p class=\"note\">Each line requests a different explicit oblique angle against the same Regular-only face - the synthesized shear follows the declared angle exactly, not a fixed default.</p>");
foreach (var angle in new[] { 0, 8, 14, 20, 30 })
{
    fontShowcase.Append($"<p style=\"font-family: SynthDemo; font-style: oblique {angle}deg\">oblique {angle}deg</p>");
}

// Arial Narrow ships with Office, not base Windows - on a machine with neither condensed
// face installed, the condensed rule below silently doesn't register and both lines render
// identically. The docs build machine always has Roboto Condensed (installed by pages.yml).
fontShowcase.Append("<h2>font-stretch face selection</h2>");
fontShowcase.Append("<p class=\"note\">\"StretchDemo\" registers two real, differently-shaped faces under one family via two @font-face rules with declared <code>font-stretch</code> descriptors - selecting condensed picks the genuinely narrower face (Roboto Condensed on the docs build machine, Arial Narrow on Windows), not just a coincidentally-matching one.</p>");
fontShowcase.Append("<style>@font-face { font-family: 'StretchDemo'; font-stretch: normal; src: local('Roboto'), local('Arial'); } " +
    "@font-face { font-family: 'StretchDemo'; font-stretch: condensed; src: local('Roboto Condensed'), local('Arial Narrow'); }</style>");
fontShowcase.Append("<p style=\"font-family: StretchDemo; font-stretch: normal\">font-stretch: normal</p>");
fontShowcase.Append("<p style=\"font-family: StretchDemo; font-stretch: condensed\">font-stretch: condensed</p>");

// Generic families + system-ui: whichever real installed substitute each resolves to on
// the OS actually generating this showcase - fontconfig aliases on the docs build machine
// (cursive -> Great Vibes, fantasy -> Cabin Sketch, set up by pages.yml), the
// Chromium-matched platform table on Windows/macOS/Android (see GenericFontFamilyResolver /
// CssConstants.DefaultFont).
fontShowcase.Append("<h2>Generic families (platform-matched)</h2>");
fontShowcase.Append("<p class=\"note\">Each generic family resolves to a real installed font: via fontconfig on the docs build machine, via the Chromium-matched platform table elsewhere.</p>");
foreach (var generic in new[] { "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui" })
{
    fontShowcase.Append($"<p style=\"font-family: {generic}\">{generic}: The quick brown fox jumps over the lazy dog</p>");
}

var fontShowcaseHtml = "<!DOCTYPE html><html><head><style>" +
    "@page { size: a4; margin: 15mm } body { margin: 0; font-size: 13pt }" +
    "h1 { font: bold 18pt Arial, sans-serif; margin: 0 0 8px }" +
    "h2 { font: bold 13pt Arial, sans-serif; margin: 18px 0 4px }" +
    "p { margin: 3px 0 } p.note { font: italic 10pt Arial, sans-serif; color: #555; margin: 0 0 8px }" +
    "</style></head><body>" +
    "<h1>Font Resolution Showcase</h1>" +
    fontShowcase +
    "</body></html>";

await SaveShowcaseAsync("font_resolution_showcase", "Typography & Text", "Font Resolution",
    "CSS font selection compliance: nearest-weight matching across a six-weight family, faux-bold/italic synthesis, oblique angles, font-stretch face selection, and generic family resolution.",
    fontShowcaseHtml, pdfConfig);

// --- Acid2 showcase ---
// The real, unmodified Acid2 test (http://acid2.acidtests.org/) - PeachPDF's non-interactive/static
// subset compliance target. See CLAUDE.md and docs/html-css-support.md for what "compliance" means
// for a static PDF renderer (no :hover/:active, no scripting).
var acid2Html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "acid2.html"));

await SaveShowcaseAsync("acid2", "Standards & Accessibility", "Acid2",
    "The unmodified Acid2 test rendered by PeachPDF - the classic CSS compliance smiley.",
    acid2Html, pdfConfig);

// ─── Real-World Documents: Quarterly Sales Ledger (repeating table headers) ───
// A 60-row <table> spanning three US Letter pages - the <thead> repeats automatically on
// every page the table spans. The grand total is deliberately a styled final <tbody> row,
// not a <tfoot>: a repeating <tfoot> currently renders none of its content
// (https://github.com/jhaygood86/PeachPDF/issues/124). Row data is deterministic
// (index-based formulas, no Random) so the rendered PDF is stable across builds.

string[] ledgerRegions = ["Southeast", "Northeast", "Midwest", "Southwest", "West Coast", "Mid-Atlantic"];
string[] ledgerProducts = ["Meridian Core", "Meridian Analytics", "Atlas Connect", "Pulse Monitor", "Vertex API"];
(string Label, string CssClass)[] ledgerStatuses = [("Paid", "paid"), ("Pending", "pending"), ("Paid", "paid"), ("Paid", "paid"), ("Overdue", "overdue")];

static int LedgerUnits(int i) => 4 + i * 7 % 38;
static int LedgerUnitPrice(int i) => 120 + i * 53 % 480;

var ledgerRows = string.Join("\n", Enumerable.Range(1, 60).Select(i =>
{
    var (statusLabel, statusClass) = ledgerStatuses[i * 2 % ledgerStatuses.Length];
    var alt = i % 2 == 0 ? " class=\"alt\"" : "";
    var total = LedgerUnits(i) * LedgerUnitPrice(i);
    return $"<tr{alt}><td class=\"num\">INV-2026-{i:0000}</td><td>{ledgerRegions[i % ledgerRegions.Length]}</td><td>{ledgerProducts[i * 3 % ledgerProducts.Length]}</td>" +
           $"<td class=\"r\">{LedgerUnits(i)}</td><td class=\"r\">${LedgerUnitPrice(i)}.00</td><td class=\"r\">${total.ToString("N0", CultureInfo.InvariantCulture)}.00</td>" +
           $"<td><span class=\"badge {statusClass}\">{statusLabel}</span></td></tr>";
}));

var ledgerGrandTotal = Enumerable.Range(1, 60).Sum(i => LedgerUnits(i) * LedgerUnitPrice(i));

var ledgerHtml = $$"""
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: letter portrait;
      margin: 0.85in 0.6in 0.8in 0.6in;
      @top-left { content: "Peachtree Analytics, Inc."; font: bold 8pt Arial; color: #0b4f6c; }
      @top-right { content: "FY2026 Sales Ledger"; font: 8pt Arial; color: #777; }
      @bottom-center { content: "Page " counter(page) " of " counter(pages); font: 8pt Arial; color: #777; }
    }
    body { font: 9pt Arial, sans-serif; color: #1c2733; margin: 0; }
    h1 { font-size: 19pt; margin: 0 0 2pt; color: #0b4f6c; }
    .sub { color: #667; margin: 0 0 14pt; font-size: 9.5pt; }
    table { border-collapse: collapse; width: 100%; }
    thead th {
      background: linear-gradient(180deg, #11698e, #0b4f6c);
      color: #fff; font-size: 8pt; text-transform: uppercase; letter-spacing: 0.8pt; word-spacing: 2pt;
      padding: 7pt 8pt; text-align: left; border-bottom: 2.5pt solid #073b52;
    }
    thead th.r { text-align: right; }
    tbody td { padding: 5.5pt 8pt; border-bottom: 0.75pt solid #dde4ea; }
    tbody tr.alt td { background: #f2f6f9; }
    td.num { font-family: monospace; color: #345; }
    td.r { text-align: right; }
    .badge { font-size: 7pt; font-weight: bold; text-transform: uppercase; letter-spacing: 0.5pt;
      padding: 2pt 6pt; border-radius: 8pt; }
    .badge.paid { background: #d9f2e4; color: #14683c; }
    .badge.pending { background: #fdf0d3; color: #8a6410; }
    .badge.overdue { background: #fbdfdb; color: #a02318; }
    tr.grand td {
      background: #0b4f6c; color: #fff; font-weight: bold; padding: 7pt 8pt;
      border-bottom: none;
    }
    </style>
    </head>
    <body>
    <h1>Quarterly Sales Ledger</h1>
    <p class="sub">60 invoices &mdash; the table header repeats automatically on every page the table spans.</p>
    <table>
    <thead>
    <tr><th>Invoice</th><th>Region</th><th>Product</th><th class="r">Units</th><th class="r">Unit Price</th><th class="r">Total</th><th>Status</th></tr>
    </thead>
    <tbody>
    {{ledgerRows}}
    <tr class="grand"><td colspan="5">Grand total &mdash; 60 invoices</td><td class="r">${{ledgerGrandTotal.ToString("N0", CultureInfo.InvariantCulture)}}.00</td><td></td></tr>
    </tbody>
    </table>
    </body>
    </html>
    """;

await SaveShowcaseAsync("table_header_repeat", "Real-World Documents", "Repeating Table Headers",
    "A 60-row sales ledger spanning three pages - the table header repeats automatically on every page, framed by @page margin-box running headers and page counters.",
    ledgerHtml, new PdfGenerateConfig { PageSize = PageSize.Letter });

// ─── Real-World Documents: modern invoice ───
// A one-page, full-bleed (base-rule @page margin: 0) US Letter invoice in a dark green
// palette exercising the modern-CSS feature set: custom properties, calc()/clamp(),
// oklch-interpolated + radial + repeating gradients, gradient-border cards, CSS-counter
// line numbers, a generated-content gradient bullet, a rotated transform stamp, an inline
// SVG logo, and a full-height column-flex body whose footer anchors via margin-top: auto.
// Workarounds baked in (each tracks a filed issue): line numbers use "0" counter(line)
// because decimal-leading-zero renders nothing (#128); word-spacing accompanies every
// letter-spacing because tracked-out text otherwise loses its word gaps (#129); body
// height is calc(11in - 1mm) because an exact 11in flex body overflows to a blank page;
// card depth is faked with gradient borders because box-shadow is a silent no-op (#132).

var invoiceHtml = """
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    :root {
      --ink: #13342a;
      --ink-2: #1e5540;
      --muted: #5e6e66;
      --accent: #1d6a52;
      --accent-2: #d9b64a;
      --panel: #f2f6f2;
      --radius: 10pt;
    }
    @page { size: letter portrait; margin: 0; }
    body {
      font: 10pt Arial, sans-serif; color: var(--ink); margin: 0;
      height: calc(11in - 1mm); display: flex; flex-direction: column;
    }
    .band {
      background:
        radial-gradient(circle at 85% -40%, rgba(217, 182, 74, 0.5), transparent 60%),
        linear-gradient(115deg in oklch, #0f2a21, #1e5540 55%, #5d9673);
      color: #fff; padding: 11mm 14mm 8mm;
    }
    .band-row { display: flex; justify-content: space-between; align-items: flex-start; }
    .brand { display: flex; align-items: center; gap: 9pt; }
    .brand h1 { font-size: 17pt; margin: 0; letter-spacing: 2.5pt; word-spacing: 6pt; text-transform: uppercase; }
    .brand p { margin: 1pt 0 0; font-size: 8pt; color: rgba(255,255,255,0.75); letter-spacing: 1pt; word-spacing: 3pt; }
    .doc-meta { text-align: right; }
    .doc-meta .kind {
      font-size: clamp(20pt, 18pt + 24%, 28pt); font-weight: bold; letter-spacing: 5pt; margin: 0;
      color: var(--accent-2);
    }
    .doc-meta p { margin: 2pt 0 0; font-size: 9pt; color: rgba(255,255,255,0.85); }
    .rule {
      height: 3.5pt; background: repeating-linear-gradient(90deg,
        var(--accent-2) 0 14pt, var(--accent) 14pt 28pt);
    }
    main { padding: 8mm 14mm 0; }
    .parties { display: flex; gap: 6mm; margin-bottom: 6mm; }
    .party {
      flex: 1 1 0; background: linear-gradient(135deg, var(--accent), var(--accent-2));
      border-radius: var(--radius); padding: 1.6pt;
    }
    .party-inner { background: var(--panel); border-radius: calc(var(--radius) - 1.6pt); padding: 8pt 10pt; }
    .party h3 {
      margin: 0 0 4pt; font-size: 7.5pt; text-transform: uppercase; letter-spacing: 1.5pt; word-spacing: 3.5pt; color: var(--accent);
    }
    .party p { margin: 0; line-height: 1.45; font-size: 9pt; }
    table.items { border-collapse: collapse; width: 100%; counter-reset: line; }
    table.items thead th {
      font-size: 7.5pt; text-transform: uppercase; letter-spacing: 1.2pt; word-spacing: 3pt; color: var(--muted);
      border-bottom: 1.5pt solid var(--ink); padding: 0 6pt 5pt; text-align: left;
    }
    table.items thead th.r, table.items td.r { text-align: right; }
    table.items tbody td { padding: 5.5pt 6pt; border-bottom: 0.75pt solid #e3e0db; vertical-align: top; }
    table.items tbody td.n::before {
      counter-increment: line; content: counter(line, decimal-leading-zero);
      font-weight: bold; color: var(--accent); font-size: 8pt;
    }
    .item-name { font-weight: bold; }
    .item-desc { color: var(--muted); font-size: 8pt; margin-top: 1.5pt; }
    .totals { display: flex; justify-content: flex-end; margin-top: 4mm; }
    .totals-box { width: 62mm; }
    .trow { display: flex; justify-content: space-between; padding: 3.5pt 6pt; font-size: 9.5pt; }
    .trow.muted { color: var(--muted); }
    .trow.grand {
      margin-top: 4pt; background: linear-gradient(115deg, var(--ink), var(--ink-2));
      color: #fff; border-radius: 6pt; padding: 7pt 10pt; font-size: 12pt; font-weight: bold;
    }
    .stamp {
      position: absolute; top: 172mm; left: 102mm; z-index: 3; transform: rotate(-11deg);
      border: 2.5pt solid #14683c; color: #14683c; border-radius: 6pt;
      font: bold 15pt Arial; letter-spacing: 4pt; padding: 4pt 12pt; opacity: 0.45;
      text-transform: uppercase;
    }
    footer { margin-top: auto; padding: 5mm 14mm 6mm; background: var(--panel); }
    footer .cols { display: flex; gap: 8mm; }
    footer h4 { margin: 0 0 3pt; font-size: 7.5pt; text-transform: uppercase; letter-spacing: 1.5pt; word-spacing: 3.5pt; color: var(--accent); }
    footer p { margin: 0; font-size: 8pt; color: var(--muted); line-height: 1.5; }
    .thanks { margin-top: 4mm; font-size: 8.5pt; color: var(--ink); }
    .thanks::before {
      content: linear-gradient(135deg, var(--accent), var(--accent-2));
      display: inline-block; width: 7pt; height: 7pt; margin-right: 5pt; border-radius: 2pt;
    }
    </style>
    </head>
    <body>
    <div class="band">
      <div class="band-row">
        <div class="brand">
          <svg width="46" height="46" viewBox="0 0 46 46" xmlns="http://www.w3.org/2000/svg">
    <defs><linearGradient id="lg" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="#d9b64a"/><stop offset="1" stop-color="#4fa77f"/></linearGradient></defs>
    <circle cx="23" cy="23" r="21" fill="none" stroke="url(#lg)" stroke-width="4"/>
    <path d="M13 29 L23 12 L33 29 Z" fill="url(#lg)"/>
    </svg>
          <div><h1>Solstice Studio</h1><p>DESIGN &amp; ENGINEERING</p></div>
        </div>
        <div class="doc-meta">
          <p class="kind">Invoice</p>
          <p>No. SS-2026-0117</p>
          <p>Issued July 12, 2026 &middot; Due August 11, 2026</p>
        </div>
      </div>
    </div>
    <div class="rule"></div>
    <main>
      <div class="parties">
        <div class="party"><div class="party-inner">
          <h3>Billed To</h3>
          <p><b>Halcyon Robotics, Inc.</b><br/>1200 Congress Ave, Suite 300<br/>Austin, TX 78701<br/>EIN 74-2201457</p>
        </div></div>
        <div class="party"><div class="party-inner">
          <h3>From</h3>
          <p><b>Solstice Studio LLC</b><br/>384 Peachtree St NE, Suite 900<br/>Atlanta, GA 30308<br/>EIN 88-4113550</p>
        </div></div>
        <div class="party"><div class="party-inner">
          <h3>Project</h3>
          <p><b>Beacon telemetry dashboard</b><br/>Statement of work #4<br/>PO HR-2026-088<br/>Period: May &ndash; June 2026</p>
        </div></div>
      </div>
      <div class="stamp">Paid</div>
      <table class="items">
        <thead><tr><th></th><th>Description</th><th class="r">Qty</th><th class="r">Rate</th><th class="r">Amount</th></tr></thead>
        <tbody>
          <tr><td class="n"></td><td><div class="item-name">Discovery &amp; UX research</div><div class="item-desc">Stakeholder interviews, telemetry audit, journey mapping</div></td><td class="r">3 days</td><td class="r">$1,150.00</td><td class="r">$3,450.00</td></tr>
          <tr><td class="n"></td><td><div class="item-name">Design system &amp; component library</div><div class="item-desc">Tokens, dark mode, 42 documented components</div></td><td class="r">6 days</td><td class="r">$1,150.00</td><td class="r">$6,900.00</td></tr>
          <tr><td class="n"></td><td><div class="item-name">Real-time dashboard implementation</div><div class="item-desc">Streaming charts, alerting views, fleet map</div></td><td class="r">9 days</td><td class="r">$1,280.00</td><td class="r">$11,520.00</td></tr>
          <tr><td class="n"></td><td><div class="item-name">Accessibility &amp; performance pass</div><div class="item-desc">WCAG 2.2 AA audit, remediation, load-time budget</div></td><td class="r">2 days</td><td class="r">$1,150.00</td><td class="r">$2,300.00</td></tr>
          <tr><td class="n"></td><td><div class="item-name">On-site handover workshop</div><div class="item-desc">Austin, 14 people, incl. travel</div></td><td class="r">1 day</td><td class="r">$1,650.00</td><td class="r">$1,650.00</td></tr>
        </tbody>
      </table>
      <div class="totals">
        <div class="totals-box">
          <div class="trow muted"><span>Subtotal</span><span>$25,820.00</span></div>
          <div class="trow muted"><span>Early-payment discount (2%)</span><span>&minus;$516.40</span></div>
          <div class="trow muted"><span>Sales tax (services, exempt)</span><span>$0.00</span></div>
          <div class="trow grand"><span>Total Due</span><span>$25,303.60</span></div>
        </div>
      </div>
    </main>
    <footer>
      <div class="cols">
        <div><h4>Payment</h4><p>Truist Bank, Atlanta<br/>Routing 061000104 &middot; Acct 1000223344<br/>Ref. SS-2026-0117</p></div>
        <div><h4>Terms</h4><p>Payment due within 30 days.<br/>Late balances accrue 1.5% per month.<br/>Checks payable to Solstice Studio LLC.</p></div>
        <div><h4>Contact</h4><p>billing@solstice.studio<br/>+1 (404) 555-0117<br/>solstice.studio</p></div>
      </div>
      <p class="thanks">Thank you for building with us &mdash; we would love to work on statement of work #5.</p>
    </footer>
    </body>
    </html>
    """;

await SaveShowcaseAsync("invoice", "Real-World Documents", "Modern Invoice",
    "A one-page, full-bleed invoice in a dark green palette: custom properties, oklch gradient interpolation, gradient-border cards, zero-padded line numbers via counter(line, decimal-leading-zero), a transform stamp, and an inline SVG logo.",
    invoiceHtml, new PdfGenerateConfig { PageSize = PageSize.Letter });

// ─── Real-World Documents: ten-page print catalog ───
// A US Letter catalog designed as a book: full-bleed gradient cover (page 1), eight item
// pages, and a colophon (page 10). Running furniture mirrors between left- and right-hand
// pages via :left/:right page selectors - the string-set item name at the top outside
// corner, the SVG logo as a real margin-box image at top-center (box width pinned to the
// image so it truly centers; margin-box images ignore box alignment, see
// https://github.com/jhaygood86/PeachPDF/issues/140), and folios in the inside (gutter)
// bottom corners. The cover is a true four-edge full-bleed plate via
// "@page :first { margin: 0 }": per-page top/bottom margin overrides are layout-affecting
// (each page gets its own content band per CSS Paged Media 3's page-box model), so page 1's
// band is the entire 8.5in x 11in sheet and the plate fills it exactly - the forced break
// after it lands item No. 01 at the top of the normally-margined page 2 with no blank page
// (exact-boundary forced-break rule, css-break-3). The colophon's named page is the last
// page only, because named-page styles leak onto subsequent auto pages (#126).

const string catalogLogoSvg = "<svg width=\"20\" height=\"20\" viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\">\n" +
    "<defs><linearGradient id=\"ml\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">\n" +
    "<stop offset=\"0\" stop-color=\"#d9a441\"/><stop offset=\"1\" stop-color=\"#c97b4a\"/></linearGradient></defs>\n" +
    "<path d=\"M12 2 L20 20 L15 20 L12 13 L9 20 L4 20 Z\" fill=\"url(#ml)\"/>\n" +
    "<circle cx=\"12\" cy=\"5.5\" r=\"2.1\" fill=\"none\" stroke=\"url(#ml)\" stroke-width=\"1.4\"/>\n" +
    "</svg>";

const string coverLogoSvg = "<svg width=\"120\" height=\"120\" viewBox=\"0 0 24 24\" xmlns=\"http://www.w3.org/2000/svg\">\n" +
    "<path d=\"M12 2 L20 20 L15 20 L12 13 L9 20 L4 20 Z\" fill=\"#f0c975\"/>\n" +
    "<circle cx=\"12\" cy=\"5.5\" r=\"2.1\" fill=\"none\" stroke=\"#d9834f\" stroke-width=\"1.4\"/>\n" +
    "</svg>";

var catalogLogoDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(catalogLogoSvg));

// Stylized geometric product art. Shapes to avoid (see the filed issues): a stroked
// <rect rx> loses its bottom edge and a stroked <ellipse> loses its stroke entirely
// (#134), so the shelf frame has square corners and the mirror ring is a <circle>; and
// the art panel wrapper must not combine border-radius with border/padding, which drops
// SVG gradient strokes (#135) - the breathing room lives in the viewBox instead.
static string CatalogItemArt(string accent1, string accent2, string kind, int idx)
{
    var defs = $"<defs>\n<linearGradient id=\"g{idx}\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">\n" +
        $"<stop offset=\"0\" stop-color=\"{accent1}\"/><stop offset=\"1\" stop-color=\"{accent2}\"/></linearGradient>\n" +
        $"<radialGradient id=\"h{idx}\" cx=\"0.5\" cy=\"0.35\" r=\"0.8\">\n" +
        $"<stop offset=\"0\" stop-color=\"{accent1}\" stop-opacity=\"0.35\"/><stop offset=\"1\" stop-color=\"{accent1}\" stop-opacity=\"0\"/></radialGradient>\n" +
        "</defs>";
    var shape = kind switch
    {
        "chair" => $$"""<path d="M60 30 Q60 20 70 20 L110 20 Q120 20 120 30 L120 95 L60 95 Z" fill="url(#g{{idx}})"/><rect x="52" y="95" width="76" height="12" rx="6" fill="{{accent2}}"/><rect x="60" y="107" width="8" height="38" rx="3" fill="#3a3a3a"/><rect x="112" y="107" width="8" height="38" rx="3" fill="#3a3a3a"/>""",
        "table" => $$"""<ellipse cx="90" cy="55" rx="62" ry="16" fill="url(#g{{idx}})"/><rect x="85" y="66" width="10" height="60" fill="{{accent2}}"/><path d="M60 145 L90 120 L120 145 Z" fill="#3a3a3a"/>""",
        "lamp" => $$"""<path d="M65 25 L115 25 L100 70 L80 70 Z" fill="url(#g{{idx}})"/><rect x="87" y="70" width="6" height="55" fill="#3a3a3a"/><ellipse cx="90" cy="130" rx="28" ry="8" fill="{{accent2}}"/><circle cx="90" cy="86" r="9" fill="{{accent1}}" opacity="0.5"/>""",
        "shelf" => $$"""<rect x="45" y="20" width="90" height="120" fill="none" stroke="url(#g{{idx}})" stroke-width="8"/><rect x="53" y="58" width="74" height="6" fill="{{accent2}}"/><rect x="53" y="96" width="74" height="6" fill="{{accent2}}"/><rect x="60" y="34" width="14" height="20" fill="{{accent1}}"/><rect x="94" y="72" width="20" height="20" fill="{{accent1}}" opacity="0.7"/>""",
        "desk" => $$"""<rect x="40" y="55" width="100" height="10" rx="4" fill="url(#g{{idx}})"/><rect x="48" y="65" width="8" height="60" fill="#3a3a3a"/><rect x="124" y="65" width="8" height="60" fill="#3a3a3a"/><rect x="96" y="65" width="36" height="26" rx="3" fill="{{accent2}}"/><circle cx="103" cy="78" r="2.6" fill="#fff"/>""",
        "sofa" => $$"""<rect x="40" y="60" width="100" height="42" rx="10" fill="url(#g{{idx}})"/><rect x="32" y="52" width="18" height="52" rx="8" fill="{{accent2}}"/><rect x="130" y="52" width="18" height="52" rx="8" fill="{{accent2}}"/><rect x="46" y="102" width="10" height="18" rx="4" fill="#3a3a3a"/><rect x="124" y="102" width="10" height="18" rx="4" fill="#3a3a3a"/>""",
        "stool" => $$"""<path d="M58 40 Q90 26 122 40 L116 52 Q90 42 64 52 Z" fill="url(#g{{idx}})"/><path d="M68 52 L60 130" stroke="#3a3a3a" stroke-width="7" stroke-linecap="round"/><path d="M112 52 L120 130" stroke="#3a3a3a" stroke-width="7" stroke-linecap="round"/><path d="M90 50 L90 130" stroke="{{accent2}}" stroke-width="7" stroke-linecap="round"/>""",
        "mirror" => $$"""<circle cx="90" cy="72" r="50" fill="url(#h{{idx}})" stroke="url(#g{{idx}})" stroke-width="8"/><path d="M66 52 Q80 34 102 40" stroke="#fff" stroke-width="5" fill="none" stroke-linecap="round" opacity="0.7"/><path d="M74 132 L106 132 L98 148 L82 148 Z" fill="{{accent2}}"/>""",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown catalog art kind"),
    };
    return $"<svg width=\"216\" height=\"186\" viewBox=\"-18 -18 216 186\" xmlns=\"http://www.w3.org/2000/svg\">" +
        $"{defs}<rect x=\"-18\" y=\"-18\" width=\"216\" height=\"186\" fill=\"url(#h{idx})\"/>{shape}</svg>";
}

(string Name, string Kind, string Tagline, string Price, string Accent, string AccentDeep, string Material, string Dimensions, string Weight, string Finish)[] catalogItems =
[
    ("Blue Ridge Lounge Chair", "chair", "A low-slung chair for long evenings", "1,240", "#c97b4a", "#8a5a3b", "Steam-bent white oak, wool boucl&eacute;", "27 &times; 29 &times; 32 in", "31 lb", "Natural oil"),
    ("Savannah Dining Table", "table", "Six seats, one continuous grain", "2,890", "#7d8c6f", "#55614b", "Solid ash, brass levelers", "87 &times; 37 &times; 29 in", "137 lb", "Matte lacquer"),
    ("Firefly Pendant Lamp", "lamp", "Warm light, folded like paper", "480", "#d9a441", "#a8762a", "Spun aluminum, oak stem", "&Oslash; 18 &times; 15 in", "4.6 lb", "Powder coat"),
    ("Piedmont Bookshelf", "shelf", "Open storage that breathes", "1,680", "#5f7a8c", "#3e5260", "Birch ply, steel frame", "35 &times; 13 &times; 75 in", "84 lb", "Soap finish"),
    ("Oconee Writing Desk", "desk", "A quiet place to think", "1,450", "#8c6f7d", "#5f4a55", "Walnut, leather inlay", "51 &times; 26 &times; 30 in", "64 lb", "Hard wax oil"),
    ("Sweetwater Two-Seat Sofa", "sofa", "Deep seats, honest stitching", "3,420", "#a8582f", "#753d20", "Kiln-dried beech, linen", "66 &times; 35 &times; 31 in", "106 lb", "Removable covers"),
    ("Tybee Counter Stool", "stool", "Three legs, perfect balance", "390", "#6f8c85", "#4a615c", "Turned oak, cork seat", "&Oslash; 14 &times; 26 in", "11 lb", "Natural oil"),
    ("Magnolia Wall Mirror", "mirror", "Morning light, doubled", "720", "#b08d57", "#7d6238", "Brass ring, float glass", "&Oslash; 31 &times; 1.5 in", "20 lb", "Brushed brass"),
];

var catalogSections = string.Join("\n", catalogItems.Select((item, index) =>
{
    var n = index + 1;
    var art = CatalogItemArt(item.Accent, item.AccentDeep, item.Kind, n);
    var nameWords = item.Name.Split(' ');
    var family = string.Join(' ', nameWords[..Math.Max(1, nameWords.Length - 2)]);
    return $$"""
        <section class="item" style="--accent: {{item.Accent}}; --accent-deep: {{item.AccentDeep}};">
          <div class="brand-band"><span class="wordmark">AURORA &amp; PINE</span><span class="rule"></span><span class="season-tag">F/W 2026</span></div>
          <p class="item-no">No. {{n:00}} <span>/ 08</span></p>
          <h2>{{item.Name}}</h2>
          <p class="tagline">{{item.Tagline}}</p>
          <div class="item-body">
            <div class="art-panel">{{art}}</div>
            <div class="item-info">
              <p class="desc">Each {{family}} piece is bench-made in our Savannah workshop from Georgia
              longleaf pine and certified Appalachian hardwoods, assembled without visible fasteners and finished
              by hand. Designed to be repaired rather than replaced, it carries a 25-year structural guarantee.</p>
              <table class="specs">
                <tr><th>Material</th><td>{{item.Material}}</td></tr>
                <tr><th>Dimensions</th><td>{{item.Dimensions}}</td></tr>
                <tr><th>Weight</th><td>{{item.Weight}}</td></tr>
                <tr><th>Finish</th><td>{{item.Finish}}</td></tr>
              </table>
              <ul class="features">
                <li>Bench-made in Savannah, Georgia</li>
                <li>FSC-certified timber only</li>
                <li>Ships flat, assembles without tools</li>
              </ul>
            </div>
          </div>
          <div class="price-row">
            <div class="price-ring"><div class="price-inner">${{item.Price}}</div></div>
            <p class="order-note">Order code AP-{{100 + n * 7}} &middot; lead time 6 weeks &middot; aurorapine.com/no{{n:00}}</p>
          </div>
        </section>
        """;
}));

var catalogHtml = $$"""
    <!DOCTYPE html>
    <html>
    <head>
    <style>
    @page {
      size: letter portrait;
      margin: 0.95in 0.7in 0.85in 0.7in;
    }
    /* Book-style running furniture, mirrored between left- and right-hand pages:
       the product name sits at the top on the matching (outside) side, aligned to
       that edge; the company logo (a real image in a margin box) sits at top-center
       with the box width pinned to the image so it truly centers; the folio sits in
       the inside (gutter) bottom corner */
    @page :right {
      @top-right { content: string(item-name); font: italic 8.5pt Georgia, serif; color: #666; }
      @top-center { content: url("{{catalogLogoDataUri}}"); width: 20px; }
      @bottom-left { content: counter(page); font: 9pt Georgia, serif; color: #8a5a3b; }
    }
    @page :left {
      @top-left { content: string(item-name); font: italic 8.5pt Georgia, serif; color: #666; }
      @top-center { content: url("{{catalogLogoDataUri}}"); width: 20px; }
      @bottom-right { content: counter(page); font: 9pt Georgia, serif; color: #8a5a3b; }
    }
    @page :first {
      margin: 0;
      @top-left { content: none; }
      @top-center { content: none; }
      @top-right { content: none; }
      @bottom-left { content: none; }
    }
    @page colophon {
      @top-left { content: none; }
      @top-center { content: none; }
      @top-right { content: none; }
    }
    body { font: 10pt Georgia, serif; color: #2b2620; margin: 0; }

    /* ── Cover: true four-edge full-bleed plate via @page :first { margin: 0 }.
          The margin-0 first page's content band is the whole 8.5in × 11in sheet
          (per-page top/bottom margins are layout-affecting), so the plate simply
          sizes to the sheet: width 8.5in overflows the base layout width to the
          physical right edge, and height 11in fills page 1's own band exactly -
          ending flush on the pagination boundary, which the exact-boundary
          forced-break rule treats as already-satisfied (no blank page 2). ── */
    .cover {
      width: 8.5in;
      height: 11in;
      background:
        radial-gradient(circle at 30% 20%, rgba(240, 201, 117, 0.35), transparent 55%),
        radial-gradient(circle at 80% 85%, rgba(201, 123, 74, 0.4), transparent 60%),
        linear-gradient(160deg in oklch, #221a12, #3d2c1c 55%, #59402a);
      color: #f4e9d8; text-align: center;
      display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8mm;
      page-break-after: always;
    }
    .cover h1 { font-size: 34pt; margin: 0; letter-spacing: 8pt; word-spacing: 12pt; text-transform: uppercase; font-weight: normal; }
    .cover .amp { color: #f0c975; }
    .cover .strap { font: 9.5pt Arial; letter-spacing: 4pt; word-spacing: 8pt; text-transform: uppercase; color: rgba(244,233,216,0.7); margin: 0; }
    .cover .season {
      font: bold 10pt Arial; letter-spacing: 2pt; word-spacing: 5pt; text-transform: uppercase; color: #221a12;
      background: linear-gradient(100deg, #f0c975, #d9834f); border-radius: 20pt; padding: 6pt 18pt;
    }
    .cover .edition { font-style: italic; color: rgba(244,233,216,0.65); margin: 0; }
    .cover .cover-mark { text-align: center; }

    /* ── Per-page brand band: in-flow wordmark strip at the top of each item
          page's content (the graphical logo itself is a margin-box image above) ── */
    .brand-band { display: flex; align-items: center; gap: 6pt; margin-bottom: 7mm; }
    .brand-band .wordmark {
      font: bold 8.5pt Georgia, serif; letter-spacing: 2.5pt; word-spacing: 5pt; color: #8a5a3b;
    }
    .brand-band .rule {
      flex: 1 1 0; height: 1.5pt;
      background: repeating-linear-gradient(90deg, #d9a441 0 8pt, transparent 8pt 13pt);
    }
    .brand-band .season-tag { font: italic 8pt Georgia, serif; color: #a0937f; }

    /* ── Item pages ── */
    .item { page-break-after: always; }
    .item h2 { font-size: 24pt; font-weight: normal; margin: 0 0 2pt; color: var(--accent-deep); string-set: item-name content(); }
    .item-no { font: bold 8pt Arial; letter-spacing: 2.5pt; word-spacing: 4pt; color: var(--accent); margin: 0 0 6pt; text-transform: uppercase; }
    .item-no span { color: #b5a894; }
    .tagline { font-style: italic; color: #6d6154; margin: 0 0 8mm; font-size: 11.5pt; }
    .item-body { display: flex; gap: 8mm; align-items: flex-start; }
    /* NOTE: no padding/border here — combined with border-radius they trigger a
       renderer bug that drops SVG gradient strokes (see the filed issue); the
       breathing room lives in the SVG viewBox instead */
    .art-panel {
      flex: 0 0 auto; border-radius: 10pt;
      background: linear-gradient(165deg, #faf6ef, #efe6d8);
    }
    .item-info { flex: 1 1 0; }
    .desc { margin: 0 0 6mm; line-height: 1.65; }
    table.specs { border-collapse: collapse; width: 100%; margin-bottom: 6mm; font-size: 9pt; }
    table.specs th {
      text-align: left; font: bold 7.5pt Arial; text-transform: uppercase; letter-spacing: 1.2pt;
      color: var(--accent-deep); padding: 4pt 8pt 4pt 0; border-bottom: 0.75pt solid #e0d4c2; width: 30mm;
    }
    table.specs td { padding: 4pt 0; border-bottom: 0.75pt solid #e0d4c2; }
    ul.features { margin: 0; padding: 0 0 0 14pt; }
    ul.features li { margin-bottom: 3pt; }
    ul.features li::marker { content: "\25C6  "; color: var(--accent); }
    .price-row { display: flex; align-items: center; gap: 7mm; margin-top: 9mm; }
    .price-ring {
      width: 34mm; height: 34mm; border-radius: 50%;
      background: conic-gradient(var(--accent), var(--accent-deep), var(--accent));
      display: flex; align-items: center; justify-content: center;
    }
    .price-inner {
      width: 28mm; height: 28mm; border-radius: 50%; background: #fff;
      display: flex; align-items: center; justify-content: center;
      font: bold 13pt Arial; color: var(--accent-deep);
    }
    .order-note { font: 8.5pt Arial; color: #8c8272; margin: 0; letter-spacing: 0.4pt; word-spacing: 1.5pt; }

    /* ── Colophon ── */
    .colophon {
      page: colophon; text-align: center; padding-top: 60mm;
    }
    .colophon .mark { margin-bottom: 8mm; }
    .colophon h2 { font-size: 13pt; font-weight: normal; letter-spacing: 5pt; word-spacing: 9pt; text-transform: uppercase; margin: 0 0 10mm; color: #59402a; }
    .colophon p { font-size: 8.5pt; color: #6d6154; line-height: 1.9; margin: 0 auto 6mm; width: 110mm; }
    .colophon .fine { font-size: 7.5pt; color: #a0937f; }
    </style>
    </head>
    <body>

    <div class="cover">
      <div class="cover-mark">{{coverLogoSvg}}</div>
      <h1>Aurora <span class="amp">&amp;</span> Pine</h1>
      <p class="strap">Handcrafted Furniture &middot; Savannah, Georgia</p>
      <p class="season">Fall / Winter 2026 Collection</p>
      <p class="edition">Catalog No. 14 &mdash; eight pieces, made to last a lifetime</p>
    </div>

    {{catalogSections}}

    <div class="colophon">
      <div class="mark">{{catalogLogoSvg}}</div>
      <h2>Aurora &amp; Pine</h2>
      <p>All pieces designed and bench-made at our workshop at 212 East Broad Street,
      Savannah, Georgia. Timber sourced from certified Georgia and Appalachian forests.
      Textiles woven in the Carolinas; brass fittings cast in Macon.</p>
      <p>&copy; 2026 Aurora &amp; Pine Co. All rights reserved. No part of this catalog may be
      reproduced without written permission. Prices in U.S. dollars, valid through
      February 28, 2027; applicable sales tax added at order.</p>
      <p class="fine">Catalog No. 14 &middot; Printed October 2026 on 100% recycled stock &middot;
      Proudly set in Georgia (the typeface) in Georgia (the state) &middot; aurorapine.com</p>
    </div>

    </body>
    </html>
    """;

await SaveShowcaseAsync("print_catalog", "Real-World Documents", "Print Catalog",
    "A ten-page furniture catalog designed as a book: true four-edge full-bleed gradient cover via @page :first { margin: 0 }, per-item SVG art, string-set running headers, a margin-box image logo, and mirrored gutter folios via :left/:right page selectors.",
    catalogHtml, new PdfGenerateConfig { PageSize = PageSize.Letter });

// @font-face unicode-range: font matching is per-character. The monospaced webfont is declared only
// for the digit range (U+0030-0039), so digits render in it while letters in the same run fall back to
// the serif family - a per-character split within one text run, honoring the unicode-range descriptor.
var monoDigitsFontUri = "data:font/truetype;base64," +
    Convert.ToBase64String(File.ReadAllBytes("LiberationMono-Regular.ttf"));
var unicodeRangeHtml = $$"""
    <html>
    <head>
    <style>
        @font-face {
            font-family: 'MonoDigits';
            src: url('{{monoDigitsFontUri}}') format('truetype');
            unicode-range: U+0030-0039;
        }
        body { font-family: serif; margin: 40px; }
        h1 { font-size: 20pt; }
        .demo { font-family: 'MonoDigits', serif; font-size: 32pt; }
        .note { color: #666; font-size: 11pt; }
    </style>
    </head>
    <body>
        <h1>@font-face unicode-range</h1>
        <p class="demo">Invoice 2024-00731 - Total 1,299</p>
        <p class="note">The digits come from the monospaced webfont (unicode-range: U+0030-0039);
        every letter falls back to serif within the same run - per-character font matching.</p>
    </body>
    </html>
    """;

await SaveShowcaseAsync("unicode_range", "Fonts & Text", "@font-face unicode-range",
    "Per-character font matching: a monospaced webfont declared only for the digit range (U+0030-0039) supplies the digits, while letters in the same text run fall back to serif - each character resolved to the family whose unicode-range (or glyph coverage) covers it.",
    unicodeRangeHtml, pdfConfig);

// The manifest that drives the website's /showcase page (see docs/showcase.html and
// .github/workflows/pages.yml). Field names are camelCased for Liquid (site.data.showcases).
var manifestJson = JsonSerializer.Serialize(showcaseManifest,
    new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
File.WriteAllText(Path.Combine(outputDir, "showcases.json"), manifestJson);
Console.WriteLine($"Saved showcases.json ({showcaseManifest.Count} showcases)");

record ShowcaseEntry(string Slug, string Category, string Title, string Description, string Pdf, string Html);
