using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Html.Core.Fragments
{
    /// <summary>
    /// <c>FragmentEmitter</c> skips re-descending into a subtree it has already walked and observed to
    /// produce nothing, so that emitting a page costs something like the page rather than something like
    /// the document. That is only sound if it is <i>invisible</i>: it may decline to walk a subtree, but
    /// never change what comes out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each test here renders with <see cref="HtmlContainerInt.VerifyFragmentPruningOverride"/> on, which
    /// makes every slot build its draft tree twice — once pruned, once with pruning forced off — and
    /// throw unless the two are indistinguishable across all of a draft's members, the printable-content
    /// flag, and the set of boxes holding fragments. So the real assertion in every one of these is that
    /// laying out at all did not throw; the explicit assertions afterwards are there to prove the fixture
    /// still exercises what it claims to (a fixture that quietly stopped paginating would verify nothing).
    /// </para>
    /// <para>
    /// The fixtures are chosen for the cases where "this subtree is empty here" is hardest to conclude:
    /// content still ahead of the layout frontier, nested fragmentainers that visit one box once per
    /// column, a source subtree repeated through a proxy on every page, a mover that relocates content
    /// out of an already-frozen fragmentainer, and a descendant positioned nowhere near its ancestor.
    /// Setting the whole suite's <c>PEACHPDF_VERIFY_FRAGMENT_PRUNING=1</c> covers far more ground than
    /// this file does; these are the shapes worth pinning so a regression names itself.
    /// </para>
    /// </remarks>
    public class FragmentPruningParityTests
    {
        [Fact]
        public async Task ManyPagesOfPlainBlocks_PrunesWithoutChangingTheTree()
        {
            var blocks = string.Concat(Enumerable.Range(0, 60)
                .Select(i => $"<p id='p{i}' style='margin:0;line-height:20pt'>Paragraph {i}</p>"));

            var (_, container) = await Verified(LayoutHarness.Wrap(blocks), pageHeight: 120, margin: 0);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 5,
                $"fixture must paginate to exercise pruning, got {container.FragmentTree.Fragmentainers.Count} pages");
            Assert.Equal(60, DistinctBoxesWithWords(container).Count);
        }

        [Fact]
        public async Task ManyPagesOfMultiColumn_PrunesWithoutChangingTheTree()
        {
            // The container's children are visited once per column of the same slot, so none of them can
            // be observed individually - but the container itself can, and on a document that is mostly
            // multi-column that is the whole point.
            var items = string.Concat(Enumerable.Range(0, 80)
                .Select(i => $"<div id='i{i}' style='line-height:20pt'>Entry {i}</div>"));

            var (_, container) = await Verified(
                LayoutHarness.Wrap($"<div id='mc' style='columns:2;column-gap:0;width:200px'>{items}</div>"),
                pageHeight: 140, margin: 0);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 3,
                $"fixture must paginate to exercise pruning, got {container.FragmentTree.Fragmentainers.Count} pages");
        }

        [Fact]
        public async Task ARepeatingTableHeaderAcrossPages_PrunesWithoutChangingTheTree()
        {
            // One source subtree stands in for a header on every page, reached through a different proxy
            // each time - so the same box legitimately has several unrelated runs of fragments, and an
            // observation about it would be about the wrong one.
            var rows = string.Concat(Enumerable.Range(0, 40)
                .Select(i => $"<tr><td style='line-height:20pt'>Row {i}</td></tr>"));

            var (_, container) = await Verified(
                LayoutHarness.Wrap($"<table><thead><tr><th>Heading</th></tr></thead><tbody>{rows}</tbody></table>"),
                pageHeight: 140, margin: 0);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 2,
                $"fixture must paginate to exercise pruning, got {container.FragmentTree.Fragmentainers.Count} pages");
        }

        [Fact]
        public async Task ContentRelocatedOutOfAFrozenFragmentainer_PrunesWithoutChangingTheTree()
        {
            // break-inside: avoid moves a box the emitter may already have frozen a page for, which is
            // what re-opens an emitted fragmentainer - and every observation drawn before that describes
            // an arrangement that no longer exists.
            var filler = string.Concat(Enumerable.Range(0, 12)
                .Select(i => $"<p style='margin:0;line-height:20pt'>Filler {i}</p>"));

            var (_, container) = await Verified(
                LayoutHarness.Wrap(
                    filler
                    + "<div id='card' style='break-inside:avoid;line-height:20pt'>"
                    + "<p style='margin:0'>Card one</p><p style='margin:0'>Card two</p>"
                    + "<p style='margin:0'>Card three</p></div>"
                    + filler),
                pageHeight: 120, margin: 0);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 3,
                $"fixture must paginate to exercise pruning, got {container.FragmentTree.Fragmentainers.Count} pages");
        }

        [Fact]
        public async Task AnAbsolutelyPositionedDescendantPagesAway_IsNeverPrunedAway()
        {
            // The case the contiguity rule exists for: #host's own content is on page 1, but it contains
            // a descendant positioned far enough down to land pages later. Observing #host empty on an
            // intervening page and skipping it thereafter would drop that descendant entirely.
            var (_, container) = await Verified(
                LayoutHarness.Wrap(
                    "<div id='host' style='position:relative;line-height:20pt'>Host"
                    + "<div id='far' style='position:absolute;top:600pt;line-height:20pt'>Far away</div>"
                    + "</div>"),
                pageHeight: 120, margin: 0);

            var far = LayoutHarness.FindById(container.Root!, "far")!;

            Assert.Contains(AllBoxesInTheTree(container), b => ReferenceEquals(b, far));
        }

        [Fact]
        public async Task ForcedBreaksSteppingOverPages_PruneWithoutChangingTheTree()
        {
            // A directional break reserves slots nothing was ever flowed into, and those are emitted by a
            // path that runs after every pass is over - where an empty walk may not be concluded from.
            var (_, container) = await Verified(
                LayoutHarness.Wrap(
                    "<p style='margin:0;line-height:20pt'>One</p>"
                    + "<p style='margin:0;line-height:20pt;break-before:left'>Two</p>"
                    + "<p style='margin:0;line-height:20pt;break-before:left'>Three</p>"),
                pageHeight: 120, margin: 0);

            Assert.True(container.FragmentTree!.Fragmentainers.Count >= 3,
                $"fixture must paginate to exercise pruning, got {container.FragmentTree.Fragmentainers.Count} pages");
        }

        // Reductions from a generated corpus (issue 1487). Each is layout sensitive, so the markup is used
        // verbatim. Under PEACHPDF_VERIFY_FRAGMENT_PRUNING=1 all four report a difference, but only in
        // zero-word fragments (the unpruned walk also emits an empty sliver, or on a trailing slot a
        // BoundsEndAtItsContent flag over nothing) - the unpruned walk draws no extra word at any slot it
        // reports. So they are pinned on the property that matters: every authored word is in the tree,
        // exactly once, with pruning on.
        [Theory]
        [InlineData("<div style=\"columns:3\"><p style=\"margin:0 0 3pt\"> w227_6 w227_7 w227_8 w227_9 w227_10 w227_11 w227_12 w227_13 w227_14 w227_15 w227_16 </p><p style=\"\"> w227_25 w227_26 w227_27 w227_28 w227_29 w227_30 <b> w227_31 w227_32 w227_33 w227_34 w227_35 w227_36 </b> w227_51 </p></div><ul style=\"\"><li><ul><li> w227_162 w227_163 w227_164 w227_165 w227_166 w227_167 w227_168 </li><li> w227_169 w227_170 w227_171 w227_172 w227_173 w227_174 w227_175 w227_176 w227_177 w227_178 </li></ul></li><li> w227_179 w227_180 w227_181 w227_182 w227_183 w227_184 w227_185 w227_187 w227_188 w227_189 </li></ul>", 12, 200)]
        [InlineData("<div style=\"columns:3\"><p style=\"\"> w210_48 w210_49 w210_50 w210_51 w210_52 w210_53 w210_54 w210_55 w210_56 w210_57 </p><p style=\"\"> w210_80 w210_81 w210_82 w210_83 w210_84 w210_85 w210_86 w210_87 w210_88 w210_89 w210_90 w210_91 w210_92 w210_93 w210_94 w210_95 w210_96 <b> w210_99 </b></p></div><h1 style=\"\"> w210_219 </h1><div style=\"height:90pt\"></div><p style=\"\"> w210_227 w210_228 w210_229 w210_230 w210_231 w210_232 </p>", 10, 200)]
        [InlineData("<div style=\"display:flex;flex-direction:column\"><p style=\"\"> w276_35 </p> w276_53 <div style=\"padding:2pt;border:1pt dotted #888\"><div style=\"float:left;width:50%\"><p style=\"\"> w276_77 w276_78 w276_79 w276_80 w276_81 w276_82 w276_83 w276_84 w276_85 w276_86 w276_87 w276_88 w276_90 w276_91 w276_92 w276_93 w276_94 w276_95 w276_96 w276_97 w276_98 w276_99 w276_100 w276_101 w276_102 w276_103 w276_104 w276_105 w276_106 </p><p style=\"\"> w276_107 w276_108 w276_109 w276_110 w276_111 w276_112 w276_113 w276_114 w276_115 w276_116 w276_117 w276_118 w276_119 w276_120 w276_121 w276_122 w276_124 w276_125 w276_126 w276_127 w276_128 w276_129 w276_130 w276_135 w276_136 </p><p style=\"\"> w276_138 w276_139 w276_140 w276_141 w276_142 w276_143 w276_144 </p></div></div><div style=\"width:50%\"><p style=\"\"> w276_195 </p><p style=\"\"> w276_196 w276_197 w276_198 w276_199 w276_200 w276_201 w276_202 w276_203 w276_204 w276_205 w276_206 w276_207 w276_208 w276_209 w276_210 w276_211 w276_212 w276_213 w276_214 w276_215 w276_216 w276_217 w276_218 w276_219 w276_220 w276_221 <b> w276_222 </b></p></div></div>", 10, 200)]
        [InlineData("<div style=\"display:grid;grid-template-columns:1fr 1fr\"><p style=\"\"></p><div style=\"\"><div style=\"float:right;width:40%\"><p style=\"\"> w155_46 w155_47 w155_48 w155_49 w155_50 w155_51 w155_52 w155_53 w155_54 w155_55 w155_56 w155_57 w155_58 w155_59 </p><p style=\"\"> w155_61 w155_62 w155_63 w155_64 w155_65 </p><p style=\"\"> w155_69 w155_70 w155_71 w155_72 w155_73 w155_74 w155_75 w155_76 w155_77 w155_78 w155_79 w155_80 w155_81 w155_82 w155_83 w155_84 w155_85 w155_86 w155_87 w155_88 w155_89 </p></div></div><div style=\"\"><p style=\"\"> w155_220 w155_221 w155_222 w155_223 w155_224 w155_225 w155_226 w155_227 w155_228 w155_229 w155_230 w155_231 w155_232 w155_233 w155_234 w155_235 w155_236 w155_237 w155_238 w155_239 w155_240 w155_241 w155_242 w155_243 w155_244 w155_245 w155_246 w155_247 w155_248 w155_249 w155_250 w155_251 w155_252 w155_253 <b> w155_256 </b></p></div></div>", 12, 250)]
        public async Task GeneratedCorpusReductions_KeepEveryWordWithPruningOn(
            string body, int fontPt, int pageHeight)
        {
            var html = $"<!doctype html><html><head><meta charset=\"utf-8\"><style>body{{margin:0;font:{fontPt}pt Arial}}</style></head><body>{body}</body></html>";

            var (_, container) = await LayoutHarness.LayoutAsync(
                html, pageWidth: 300, pageHeight: pageHeight, margin: 20,
                prepare: root => root.HtmlContainer!.VerifyFragmentPruningOverride = false);

            var expected = System.Text.RegularExpressions.Regex.Matches(body, @"w\d+_\d+")
                .Select(m => m.Value).ToList();
            var drawn = new List<string>();
            foreach (var f in container.FragmentTree!.Fragmentainers) CollectWords(f.Root, drawn);

            Assert.Equal(expected.OrderBy(x => x), drawn.OrderBy(x => x));
        }

        private static void CollectWords(BoxFragment fragment, List<string> into)
        {
            foreach (var w in fragment.Words) if (w.Word.Text is { } t) into.Add(t.Trim());
            foreach (var c in fragment.Children) CollectWords(c, into);
        }

        /// <summary>
        /// Lays <paramref name="html"/> out with the pruned and unpruned walks compared at every slot.
        /// </summary>
        private static Task<(CssBox Root, HtmlContainerInt Container)> Verified(
            string html, double pageHeight, double margin) =>
            LayoutHarness.LayoutAsync(
                html,
                pageHeight: pageHeight,
                margin: margin,
                prepare: root => root.HtmlContainer!.VerifyFragmentPruningOverride = true);

        private static List<CssBox> AllBoxesInTheTree(HtmlContainerInt container)
        {
            var boxes = new List<CssBox>();

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                Collect(fragmentainer.Root, boxes);
            }

            return boxes;

            static void Collect(BoxFragment fragment, List<CssBox> into)
            {
                into.Add(fragment.Box);

                foreach (var child in fragment.Children)
                {
                    Collect(child, into);
                }
            }
        }

        private static HashSet<CssBox> DistinctBoxesWithWords(HtmlContainerInt container)
        {
            var boxes = new HashSet<CssBox>(ReferenceEqualityComparer.Instance as IEqualityComparer<CssBox>);

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                Collect(fragmentainer.Root, boxes);
            }

            return boxes;

            static void Collect(BoxFragment fragment, HashSet<CssBox> into)
            {
                if (fragment.Words.Count > 0) into.Add(fragment.Box.ParentBox ?? fragment.Box);

                foreach (var child in fragment.Children)
                {
                    Collect(child, into);
                }
            }
        }
    }
}
