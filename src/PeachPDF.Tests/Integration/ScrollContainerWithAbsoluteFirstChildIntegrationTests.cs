using System.Text.RegularExpressions;
using PeachPDF.Adapters;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration;

/// <summary>
/// An auto-height scroll container whose first child is an absolutely positioned box breaks between its
/// lines like any other block, and every word of it is drawn once.
/// </summary>
/// <remarks>
/// A scroll container that held an absolute box used to be kept unbreakable, because the box's own break ended
/// the pass of the container around it. The box now runs as passes of its own, so the container has no reason to
/// stay whole. Kept whole, it was cut into slices with the lines at the page edges lost once the paragraphs after
/// the box were placed at the container's top, where CSS 2.1 §9.3.1 puts them.
/// </remarks>
public class ScrollContainerWithAbsoluteFirstChildIntegrationTests
{
    // A fuzz reduction, kept with its own words because the line breaks decide which line meets a page edge.
    [Fact]
    public async Task ScrollContainerWhoseFirstChildIsAbsolute_DrawsEveryWordOnce()
    {
        const string body = """
            <p>w32_24 w32_25 w32_26 w32_27 w32_28 w32_29 w32_30 w32_31 w32_32 w32_33 w32_34 w32_35 w32_36 w32_37 w32_38 w32_39 w32_40 w32_41 w32_42 w32_43 w32_44 w32_45 w32_46 w32_47 w32_48 w32_49 w32_50 w32_51 w32_52 w32_53 w32_54 w32_55 w32_56</p>
            <div>
            <div>w32_96<br>w32_97 w32_98 w32_99</div>
            <p>w32_72 w32_73 w32_74 w32_75 w32_76 w32_77 w32_78 w32_79 w32_80 w32_81 w32_82 w32_83 w32_84 w32_85 w32_86 w32_87 w32_88 w32_89 w32_90 w32_91 w32_92 w32_93 w32_94 w32_95</p>
            <p>w32_57 w32_58 w32_59 w32_60 w32_61 w32_62 w32_63 w32_64 w32_65 w32_66 w32_67 w32_68 w32_69 w32_70 w32_71</p>
            </div>
            <div style='overflow:auto'>
            <div style='position:absolute;top:24pt;left:16pt;width:75pt;background:#eef;'>w32_183 w32_184 w32_185</div>
            <p>w32_103 w32_104 w32_105 w32_106 w32_107 w32_108 w32_109 w32_110 w32_111 w32_112 w32_113 w32_114 w32_115 w32_116 w32_117 w32_118 w32_119 w32_120 w32_121 w32_122 w32_123 w32_124 w32_125 w32_126 w32_127 w32_128 w32_129 w32_130 w32_131 w32_132</p>
            <p>w32_133 w32_134 w32_135 w32_136 w32_137 w32_138 w32_139 w32_140 w32_141 w32_142 w32_143 w32_144 w32_145 w32_146 w32_147 w32_148 w32_149 w32_150 w32_151 w32_152 w32_153 w32_154 w32_155 w32_156 w32_157 w32_158 w32_159 w32_160 w32_161 w32_162 w32_163 w32_164 w32_165</p>
            <div>w32_166 w32_167<br>w32_168 w32_169 w32_170<br>w32_171 w32_172 w32_173<br>w32_174 w32_175<br>w32_176 w32_177<br>w32_178 w32_179<br>w32_180 w32_181 w32_182</div>
            </div>
            <p>w32_186 w32_187 w32_188 w32_189 w32_190 w32_191 w32_192 w32_193 w32_194 w32_195 w32_196 w32_197 w32_198 w32_199 w32_200 w32_201 w32_202 w32_203 w32_204 w32_205 w32_206 w32_207 w32_208 w32_209 w32_210 w32_211 w32_212 w32_213 w32_214 w32_215 w32_216 w32_217</p>
            <div>
            <p>w32_240 w32_241 w32_242 w32_243 w32_244 w32_245 w32_246 w32_247 w32_248 w32_249 w32_250 w32_251 w32_252 w32_253 w32_254 w32_255 w32_256 w32_257 w32_258 w32_259 w32_260 w32_261 w32_262 w32_263 w32_264 w32_265 w32_266 w32_267 w32_268 w32_269 w32_270 w32_271 w32_272 w32_273 w32_274 w32_275</p>
            <div style='position:absolute;top:89pt;width:51pt;background:#eef;'>
            <p>w32_283 w32_284 w32_285 w32_286 w32_287 w32_288 w32_289 w32_290 w32_291 w32_292</p>
            <p>w32_293 w32_294 w32_295 w32_296 w32_297 w32_298 w32_299</p>
            </div>
            </div>
            """;

        var html = "<!DOCTYPE html><html><head><style>@page { size: 300pt 160pt; margin: 20pt } " +
                   "body { margin:0; font: 10pt/12pt sans-serif } p { margin:0 0 4pt }</style></head><body>" + body + "</body></html>";

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
