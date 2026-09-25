// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Svg;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// Low level handling of Html Renderer logic.<br/>
    /// Allows html layout and rendering without association to actual control, those allowing to handle html rendering on any graphics object.<br/>
    /// Using this class will require the client to handle all propagation's of mouse/keyboard events, layout/paint calls, scrolling offset, 
    /// location/size/rectangle handling and UI refresh requests.<br/>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MaxSize and ActualSize:</b><br/>
    /// The max width and height of the rendered html.<br/>
    /// The max width will effect the html layout wrapping lines, resize images and tables where possible.<br/>
    /// The max height does NOT effect layout, but will not render outside it (clip).<br/>
    /// <see cref="ActualSize"/> can be exceed the max size by layout restrictions (unwrap-able line, set image size, etc.).<br/>
    /// Set zero for unlimited (width/height separately).<br/>
    /// </para>
    /// </remarks>
    internal sealed class HtmlContainerInt : IDisposable
    {
        #region Fields and Consts


        /// <summary>
        /// the top margin between the page start and the text
        /// </summary>
        private double _marginTop;

        /// <summary>
        /// the bottom margin between the page end and the text
        /// </summary>
        private double _marginBottom;

        /// <summary>
        /// the left margin between the page start and the text
        /// </summary>
        private double _marginLeft;

        /// <summary>
        /// the right margin between the page end and the text
        /// </summary>
        private double _marginRight;

        /// <summary>
        /// Document-level named string storage for CSS GCPM string-set property.
        /// Stores named strings in document order to support first/last retrieval.
        /// </summary>
        private readonly List<NamedString> _namedStrings = new();

        private readonly List<NamedPageElement> _namedPageElements = new();

        /// <summary>
        /// Document-level running-element storage for css-gcpm-3's <c>position: running()</c>, in
        /// document order (the order <see cref="Dom.CssBox.LayoutBlockChildren"/> registers them, which
        /// is document order for the same reason <see cref="_namedStrings"/> is).
        /// </summary>
        private readonly List<RunningElement> _runningElements = new();

        /// <summary>
        /// Every css-gcpm-3 <c>float: footnote</c> call in the document, in document order - built once
        /// by <c>DomParser.DetachFootnoteBodies</c> when the tree is parsed (unlike
        /// <see cref="_runningElements"/>, footnote calls are a structural, parse-time fact about the
        /// tree, not something re-registered live on every layout pass, since a footnote body's own
        /// content never moves in the tree once detached). Cleared and rebuilt on every
        /// <see cref="SetHtml"/>/re-parse, never mid-layout.
        /// </summary>
        internal List<CssBoxFootnoteCall> FootnoteCalls { get; } = [];

        /// <summary>
        /// Every <c>display: contents</c> element of the document, in document order - recorded by the
        /// cascade (<c>DomParser.CascadeApplyStyles</c>, the one walk that visits every box) and spliced
        /// out of the box tree by <c>DomParser.FlattenDisplayContents</c>. A shell is in no
        /// <see cref="Dom.CssBox.Boxes"/> list, so this flat list is the only way to reach the element for
        /// everything that addresses it rather than lays it out: id lookups and the links, bookmarks and
        /// cross-references built on them, <c>string-set</c>, the tagged-PDF structure tree, and the canvas
        /// background of a <c>&lt;body&gt;</c>. Rebuilt with every parse.
        /// </summary>
        internal List<Dom.CssBox> DisplayContentsShells { get; } = [];

        /// <summary>
        /// Whether this document has any <c>float: footnote</c> call at all - gates
        /// <see cref="PerformLayout"/>'s footnote convergence loop so a document that doesn't use the
        /// feature pays nothing for it. A plain list-count check, not a tree walk, since
        /// <see cref="FootnoteCalls"/> is already maintained.
        /// </summary>
        internal bool HasFootnotes => FootnoteCalls.Count > 0;

        /// <summary>
        /// How many resolve passes the footnote convergence loop in <see cref="PerformLayout"/> used on its
        /// last run - the cap means it stopped without settling.
        /// </summary>
        internal int FootnoteResolvePasses { get; private set; }

        /// <summary>Whether the last pass of that loop found nothing left to change.</summary>
        internal bool FootnoteLoopSettled { get; private set; }

        /// <summary>
        /// This layout attempt's discovered footnote-area reservation, in points of layout space, per
        /// pagination slot that has at least one footnote landing on it - the amount
        /// <see cref="LayoutDocument"/> seeds into that slot's <see cref="Fragmentation.FragmentainerContext"/>
        /// via <c>ReserveBandEnd</c> before laying its content out. Empty (not merely absent) for a
        /// document that has never resolved footnotes yet, so <see cref="ResolveFootnotesForThisAttempt"/>'s
        /// very first call - seeded from nothing, per the "layout depends on layout" convergence shape
        /// this shares with the <c>target-counter(_, page)</c> loop in <see cref="PerformLayout"/> - reads
        /// zero for every slot rather than throwing.
        /// </summary>
        internal Dictionary<int, double> FootnoteAreaHeightsBySlot { get; private set; } = [];

        /// <summary>
        /// Every CSS Page Floats <c>float: top/bottom/top-bottom/snap</c> box in the document, in document
        /// order - built once by <c>DomParser.CollectPageFloats</c> when the tree is parsed. Unlike
        /// <c>float: footnote</c>, a page float is never detached from the tree (it has no numbered call
        /// to leave behind), so this is a plain discovery list, not a structural rewrite - cleared and
        /// rebuilt on every <see cref="SetHtml"/>/re-parse, never mid-layout.
        /// </summary>
        internal List<CssBox> PageFloats { get; } = [];

        /// <summary>
        /// Whether this document has any page float at all - gates <see cref="PerformLayout"/>'s page-float
        /// convergence loop the same way <see cref="HasFootnotes"/> gates the footnote one.
        /// </summary>
        internal bool HasPageFloats => PageFloats.Count > 0;

        /// <summary>
        /// This layout attempt's discovered reservation for page floats pinned to a page's block-start
        /// edge (<c>float: top</c>, or <c>top-bottom</c>/<c>snap</c> resolving to <c>top</c>), per slot -
        /// the band-start analogue of <see cref="FootnoteAreaHeightsBySlot"/>. Seeded into that slot's
        /// <see cref="Fragmentation.FragmentainerContext"/> via <c>ReserveBandStart</c> in
        /// <see cref="LayoutDocument"/>. Only page-level: a <c>float-reference: column</c> page float is
        /// reserved in its column instead, see <see cref="TopFloatAreaHeightsByColumn"/>.
        /// </summary>
        internal Dictionary<int, double> TopFloatAreaHeightsBySlot { get; private set; } = [];

        /// <summary>
        /// This layout attempt's discovered reservation for page floats pinned to a page's block-end edge
        /// (<c>float: bottom</c>, or <c>top-bottom</c>/<c>snap</c> resolving to <c>bottom</c>), per slot.
        /// Composed with <see cref="FootnoteAreaHeightsBySlot"/> into one <c>ReserveBandEnd</c> call in
        /// <see cref="LayoutDocument"/> - a page float sits outermost (closest to the physical page edge),
        /// the footnote area innermost (closest to flow content), a placement choice this implementation
        /// makes since neither module specifies how the two interact.
        /// </summary>
        internal Dictionary<int, double> BottomFloatAreaHeightsBySlot { get; private set; } = [];

        /// <summary>
        /// Every page float's decided final position, once <see cref="ResolvePageFloatsForThisAttempt"/>
        /// has resolved it - the document-space Y its content-box top belongs at. Read by
        /// <see cref="CssLayoutEngine.FloatBoxPageArea"/>: absent (the common case on a document's first
        /// layout attempt, before any reservation has been discovered) leaves the box at the ordinary
        /// block-flow position <see cref="CssLayoutEngine.FloatBox"/> already resolved for it, which is
        /// exactly the position <see cref="ResolvePageFloatsForThisAttempt"/> reads to discover which page
        /// it lands on.
        /// </summary>
        internal Dictionary<CssBox, double> PageFloatPlacements { get; private set; } = [];

        /// <summary>
        /// Scroll containers that were allowed to break across pages but whose content ran past their own
        /// end in an earlier attempt of the current layout. <see cref="Fragmentation.MonolithicContent"/>
        /// keeps each of them in one piece for the rest of that layout; the next one starts empty.
        /// </summary>
        internal HashSet<CssBox> ScrollContainersThatClip { get; } = [];

        /// <summary>
        /// How many times <see cref="PerformLayout"/> lays the document out again for boxes newly added to
        /// <see cref="ScrollContainersThatClip"/>. Each attempt can only add boxes, and one is almost always
        /// enough; the bound covers a box that starts clipping only once another is kept whole.
        /// </summary>
        private const int MaxClippingRelayouts = 3;

        private bool _aScrollContainerStartedClipping;

        /// <summary>
        /// Records that <paramref name="box"/>, an <c>overflow: hidden</c> box that broke like a plain block,
        /// clips its content, so the document is laid out again with it kept in one piece. A break among its
        /// clipped lines would end the pass past the box's end and lose the content after it.
        /// </summary>
        /// <param name="box">the box whose content runs past its padding edge</param>
        internal void NoteScrollContainerClips(CssBox box)
        {
            if (_scrollContainerClipsFrozen) return;
            if (ScrollContainersThatClip.Add(box)) _aScrollContainerStartedClipping = true;
        }

        /// <summary>
        /// Set for the last attempt <see cref="LayoutDocument"/> allows: a box noted then would never be laid
        /// out monolithic, yet every later reader of <see cref="ScrollContainersThatClip"/> (the emitter's
        /// materialization, paint) would treat it as monolithic. So the set stays as that attempt laid it out.
        /// </summary>
        private bool _scrollContainerClipsFrozen;

        /// <summary>
        /// The room a <c>float-reference: column</c> page float pinned to a column's block-start edge
        /// needs in that column, as resolved on the previous attempt - the column-scoped counterpart of
        /// <see cref="TopFloatAreaHeightsBySlot"/>, seeded into that column's own
        /// <c>ReserveBandStart</c> by the multi-column engine. Absent when no page float landed there.
        /// </summary>
        internal Dictionary<Fragmentation.ColumnAreaKey, double> TopFloatAreaHeightsByColumn { get; private set; } = [];

        /// <summary>
        /// The block-end counterpart of <see cref="TopFloatAreaHeightsByColumn"/>. Composed with the
        /// column's footnote area (<see cref="ColumnFootnoteInsetFor"/>) into one <c>ReserveBandEnd</c>.
        /// </summary>
        internal Dictionary<Fragmentation.ColumnAreaKey, double> BottomFloatAreaHeightsByColumn { get; private set; } = [];

        /// <summary>How much of a column's block-start edge a page float has claimed, as resolved on the previous attempt.</summary>
        internal double ColumnPageFloatTopInsetFor(Fragmentation.ColumnAreaKey key) =>
            TopFloatAreaHeightsByColumn.GetValueOrDefault(key, 0);

        /// <summary>How much of a column's block-end edge a page float has claimed, as resolved on the previous attempt.</summary>
        internal double ColumnPageFloatBottomInsetFor(Fragmentation.ColumnAreaKey key) =>
            BottomFloatAreaHeightsByColumn.GetValueOrDefault(key, 0);

        /// <summary>
        /// The column each <c>float-reference: column</c> page float was last laid out in, as the column's
        /// own fragmentainer said so at the moment (<see cref="NotePageFloatColumn"/>). A float's
        /// <see cref="CssBox.Location"/> cannot answer this after the fact: layout moves it to its resolved
        /// edge, and a container that spans pages lays the float out in a slot other than the one an earlier
        /// pass left it in.
        /// </summary>
        private readonly Dictionary<CssBox, Fragmentation.ColumnAreaKey> PageFloatColumns = [];

        /// <summary>
        /// Records which column <paramref name="box"/> is being laid out in, from the fragmentainer being
        /// filled. Outside any column - page-level flow - it forgets an earlier answer, since the float has
        /// moved to a place where the page is its reference. A measurement pass has no fragmentainer and
        /// leaves the record alone, so the last real placement stands.
        /// </summary>
        internal void NotePageFloatColumn(CssBox box)
        {
            if (box.FloatReference.Value != FloatReference.Column || CurrentFragmentainer is not { } filling) return;

            if (filling.ColumnKey is { } key)
            {
                PageFloatColumns[box] = key;
            }
            else
            {
                PageFloatColumns.Remove(box);
            }
        }

        /// <summary>
        /// Whether <paramref name="box"/> is a page float that resolves against the column it sits in
        /// (<c>float-reference: column</c>, laid out inside one of <paramref name="columnsBox"/>'s columns)
        /// rather than against the page.
        /// </summary>
        internal bool IsColumnScopedPageFloat(CssBox box, CssBox columnsBox) =>
            HasRealPageGrid && ColumnRecordForPageFloat(box) is { } record && ReferenceEquals(record.ColumnsBox, columnsBox);

        /// <summary>
        /// The total band-end reservation <paramref name="slot"/> needs seeded into its
        /// <see cref="Fragmentation.FragmentainerContext"/> - a page float pinned to the bottom edge
        /// composed with any footnote area on the same slot, since <c>ReserveBandEnd</c> takes one amount
        /// and the two must never be seeded separately (they would then compose a second time inside the
        /// context and double-count).
        /// </summary>
        internal double TotalBandEndReservationFor(int slot) =>
            FootnoteAreaHeightsBySlot.GetValueOrDefault(slot) + BottomFloatAreaHeightsBySlot.GetValueOrDefault(slot);

        /// <summary>
        /// Lazily-built id -&gt; box index backing <see cref="GetBoxById(CssBox, string)"/>
        /// (<c>target-counter()</c>/<c>target-text()</c> resolution, which can look up many ids across
        /// one document - see <see cref="DomUtils.BuildIdIndex"/>). Rebuilt whenever the tree's topmost
        /// box identity has changed since the last build (covers the <c>@container</c> convergence
        /// loop's own re-parse, which produces a new tree), never invalidated otherwise - ids don't
        /// change mid-pass.
        /// </summary>
        private Dictionary<string, CssBox>? _idIndex;

        private CssBox? _idIndexRoot;

        /// <summary>
        /// The document's own resolved <c>&lt;base href&gt;</c> backing <see cref="DocumentBaseUri"/>, or
        /// null when it declares none - a document's <c>&lt;base&gt;</c> does not change mid-pass, and a
        /// re-parse produces a new tree, so this is memoized against the tree's topmost box identity in
        /// the same shape as <see cref="_idIndex"/>.
        /// </summary>
        private RUri? _documentBase;

        private CssBox? _documentBaseRoot;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        public HtmlContainerInt(RAdapter adapter)
        {
            ArgumentNullException.ThrowIfNull(adapter);

            Adapter = adapter;
            CssParser = new CssParser(adapter, this);
            PageGeometry = new PageGeometryTable(this);
        }

        /// <summary>
        /// The per-page band-geometry table behind CSS Paged Media's page-box model — consulted by
        /// the grid helpers (<see cref="PageIndexOf"/>/<see cref="PageTopOf"/>/<see cref="PageBandHeightOf"/>)
        /// whenever a per-page <c>@page</c> rule overrides top/bottom margins; otherwise those helpers
        /// stay on the closed-form uniform arithmetic.
        /// </summary>
        internal PageGeometryTable PageGeometry { get; }

        /// <summary>
        /// 
        /// </summary>
        internal RAdapter Adapter { get; }

        /// <summary>
        /// parser for CSS data
        /// </summary>
        internal CssParser CssParser { get; }

        /// <summary>
        /// the parsed stylesheet data used for handling the html
        /// </summary>
        public CssData? CssData { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating if anti-aliasing should be avoided for geometry like backgrounds and borders (default - false).
        /// </summary>
        public bool AvoidGeometryAntialias { get; set; }

        /// <summary>
        /// Gets the document-level named strings in document order.
        /// Used by CSS GCPM string-set and string() functions.
        /// </summary>
        internal IReadOnlyList<NamedString> NamedStrings => _namedStrings;

        /// <summary>
        /// Assigns the <c>string-set</c> of every <c>display: contents</c> element, which no layout hook
        /// ever reaches (a shell is in no box's children, so it is never laid out). GCPM 3 §1.1.1 assigns a
        /// named string "at the point when the content box of the element is first created (or would have
        /// been created ...)" - for such an element, where its content begins
        /// (<see cref="DomUtils.ResolveGeometryBox"/>), and this runs once the document's geometry is final,
        /// before <see cref="LayoutMarginBoxes"/> (the only reader of <see cref="NamedStrings"/>).
        /// </summary>
        /// <remarks>
        /// <c>string()</c> and its <c>first</c>/<c>last</c>/<c>start</c> keywords select by position in
        /// <see cref="NamedStrings"/>, which layout fills in document order - so each new entry is inserted
        /// before the first one laid out below it, not appended.
        /// </remarks>
        private void RegisterDisplayContentsNamedStrings()
        {
            // Where the previous shell's string went, so two shells whose content begins at the same Y (an
            // element and the element it directly wraps) keep their document order between them.
            var floor = 0;
            var floorY = double.NaN;

            foreach (var shell in DisplayContentsShells)
            {
                if (shell.StringSet is not { Length: > 0 } stringSet || stringSet == Keywords.None) continue;

                if (shell.NamedStrings.Count > 0)
                {
                    UnregisterNamedStrings(shell.NamedStrings.Values);
                    shell.NamedStrings.Clear();
                }

                // Evaluates and registers (appending); taken back out to be placed by position instead.
                CssNamedStringEngine.ApplyStringSet(shell);
                UnregisterNamedStrings(shell.NamedStrings.Values);

                var geometryBox = DomUtils.ResolveGeometryBox(shell);
                var top = CommonUtils.GetFirstValueOrDefault(geometryBox.Rectangles, geometryBox.Bounds).Top;

                foreach (var namedString in shell.NamedStrings.Values)
                {
                    namedString.Y = top;

                    // Before whatever else begins at this same Y: that is the shell's own content (a wrapper
                    // and the heading it wraps), and a wrapper's string comes first.
                    var start = Math.Abs(floorY - top) <= PageBoundaryEpsilon ? floor : 0;
                    var index = _namedStrings.FindIndex(start, n => n.Y >= top - PageBoundaryEpsilon);
                    index = index < 0 ? _namedStrings.Count : index;
                    _namedStrings.Insert(index, namedString);

                    floor = index + 1;
                    floorY = top;
                }
            }
        }

        /// <summary>
        /// Registers a named string at the document level in document order.
        /// Used by CSS GCPM string-set property.
        /// </summary>
        /// <param name="namedString">The named string to register</param>
        internal void RegisterNamedString(NamedString namedString)
        {
            _namedStrings.Add(namedString);
        }

        /// <summary>
        /// Clears all document-level named strings.
        /// </summary>
        internal void ClearNamedStrings()
        {
            _namedStrings.Clear();
        }

        /// <summary>
        /// Withdraws named strings a box registered on an earlier run of its own prologue, so that
        /// re-running it replaces them rather than registering a second set.
        /// </summary>
        /// <remarks>
        /// <see cref="RegisterNamedString"/> appends, and a box's prologue can legitimately run more
        /// than once inside a single <see cref="LayoutDocument"/> invocation — a break decision taken
        /// against a finished box re-lays it out at its new position
        /// (<c>CssBox.PerformLayoutEpilogue</c>). Only <see cref="ClearNamedStrings"/> stood between
        /// registrations before, and that runs per invocation, not per re-entry. Removal is by
        /// reference: <c>CssNamedStringEngine.ApplyStringSet</c> stores one shared instance in both
        /// this list and the box's own map, which is what lets the box name its own entries.
        /// </remarks>
        internal void UnregisterNamedStrings(IEnumerable<NamedString> namedStrings)
        {
            foreach (var namedString in namedStrings)
            {
                _namedStrings.Remove(namedString);
            }
        }

        /// <summary>
        /// Gets the document-level running elements (css-gcpm-3 <c>position: running()</c>) in document
        /// order. Consulted by margin-box layout to resolve <c>content: element(name[, keyword])</c>.
        /// </summary>
        internal IReadOnlyList<RunningElement> RunningElements => _runningElements;

        /// <summary>
        /// Registers <paramref name="box"/> as the current occupant of <paramref name="name"/> at
        /// document position <paramref name="y"/>. Called from <see cref="Dom.CssBox.LayoutBlockChildren"/>
        /// in place of ordinarily placing/laying out the child - see <see cref="RunningElement"/>.
        /// </summary>
        internal RunningElement RegisterRunningElement(string name, Dom.CssBox box, double y)
        {
            var element = new RunningElement(name, box, y);
            _runningElements.Add(element);
            return element;
        }

        /// <summary>
        /// Withdraws a running-element registration a box made on an earlier run of its own skip-and-
        /// register hook, mirroring <see cref="UnregisterNamedStrings"/>'s same withdraw-before-register
        /// discipline (see the 2026-08-02 string-set/named-page reflow-corruption fix this mirrors):
        /// the block-children loop that registers a running box can be re-entered more than once inside
        /// one layout (a restart rewinding to before it), so registering without first withdrawing the
        /// previous entry would accumulate stale duplicates pointing at superseded document positions.
        /// </summary>
        internal void UnregisterRunningElement(RunningElement element)
        {
            _runningElements.Remove(element);
        }

        /// <summary>
        /// Clears all document-level running elements.
        /// </summary>
        internal void ClearRunningElements()
        {
            _runningElements.Clear();
        }

        internal IReadOnlyList<NamedPageElement> NamedPageElements => _namedPageElements;

        /// <summary>
        /// The most recently registered explicit <c>page</c> name in document/flow order (or
        /// <see cref="string.Empty"/> if none has been registered yet at this point in layout) - the
        /// "currently active" named page a box with no <c>page</c> value of its own would carry
        /// forward, per CSS2.1 §13.2. Used by <see cref="Dom.CssBox.PerformLayoutImp"/> to detect a
        /// forced page break when an element's own explicit <c>page</c> value differs from it.
        /// </summary>
        internal string ActivePageName => _namedPageElements.Count > 0 ? _namedPageElements[^1].Name : string.Empty;

        internal NamedPageElement RegisterNamedPageElement(string name, double y)
        {
            var element = new NamedPageElement(name, y);
            _namedPageElements.Add(element);
            // Only slots starting at/after this Y could select differently under the new name.
            PageGeometry.InvalidateFrom(y);
            return element;
        }

        /// <summary>
        /// Moves an already-registered named-page element to a new document Y (an ancestor reposition
        /// via <c>CssBox.OffsetTop</c>), invalidating every geometry slot either position could have
        /// influenced.
        /// </summary>
        internal void MoveNamedPageElement(NamedPageElement element, double newY)
        {
            var oldY = element.Y;
            element.Y = newY;
            PageGeometry.InvalidateFrom(Math.Min(oldY, newY));
        }

        /// <summary>
        /// Withdraws a named-page registration a box made on an earlier run of its own prologue.
        /// </summary>
        /// <remarks>
        /// The twin of <see cref="UnregisterNamedStrings"/>, and the more consequential of the two:
        /// <see cref="ActivePageName"/> reads the <i>last</i> entry, so a stale duplicate left behind by
        /// a re-entered prologue answers "which named page is in effect" with a position the box no
        /// longer occupies — which then decides whether the next box forces a page break at all.
        /// Dropping the box's own reference (as the prologue used to do alone) orphans the entry
        /// without removing it.
        /// </remarks>
        internal void UnregisterNamedPageElement(NamedPageElement element)
        {
            if (!_namedPageElements.Remove(element)) return;

            // Same rule as registering one: every slot from here on could have selected differently.
            PageGeometry.InvalidateFrom(element.Y);
        }

        internal void ClearNamedPageElements() => _namedPageElements.Clear();

        /// <summary>
        /// Pagination slots a directional forced break (css-break-3 §3.1 <c>left</c>/<c>right</c>/
        /// <c>recto</c>/<c>verso</c>) deliberately stepped over, so the content after the break lands on
        /// a page of the required side. Keyed by the box that took the break, by reference.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keyed by owner rather than held as a bare set of indices because a reservation has to be
        /// <i>retractable</i>: the keep-with-next retry in <c>CssBox.PerformLayoutEpilogue</c> clears
        /// <c>_prologueDone</c> and re-runs the prologue within the same layout generation, at a new
        /// position, where the same box may legitimately reach a different answer. A set would keep the
        /// stale one forever.
        /// </para>
        /// <para>
        /// Write-only during layout: nothing layout does reads this, and its sole consumer is
        /// <see cref="FragmentEmitter.Finish"/>, which runs once after the last
        /// <see cref="LayoutDocument"/>. So it can never feed back into box positions, and cannot
        /// destabilise the per-page-width reflow loop — it annotates the final layout rather than
        /// informing it.
        /// </para>
        /// </remarks>
        private readonly Dictionary<CssBox, int> _reservedBlankSlots = [];

        /// <summary>
        /// The slot a trailing directional <c>break-after</c> pads the document with, if any. Held
        /// separately from <see cref="_reservedBlankSlots"/> rather than under the last box's key: that
        /// box may itself have taken a <c>break-before</c> and reserved a slot, and one key per box
        /// would silently drop whichever reservation was made second.
        /// </summary>
        private int? _trailingBlankSlot;

        /// <summary>
        /// Records (or, with a null <paramref name="slotIndex"/>, retracts) the blank slot
        /// <paramref name="owner"/>'s forced break steps over.
        /// </summary>
        internal void SetBlankSlotReservation(CssBox owner, int? slotIndex)
        {
            if (slotIndex is { } slot)
                _reservedBlankSlots[owner] = slot;
            else
                _reservedBlankSlots.Remove(owner);
        }

        /// <summary>
        /// Whether some directional forced break deliberately left slot <paramref name="slotIndex"/>
        /// empty, so the fragment tree must materialize it as a real (blank) page.
        /// </summary>
        internal bool IsReservedBlankSlot(int slotIndex) =>
            _trailingBlankSlot == slotIndex || _reservedBlankSlots.ContainsValue(slotIndex);

        /// <summary>
        /// The last deliberately-blank slot, or null when there is none. A trailing
        /// <c>break-after</c> can reserve a slot past the laid-out content, which is why the fragment
        /// builder's slot walk cannot be bounded by the document height alone.
        /// </summary>
        internal int? MaxReservedBlankSlot =>
            _reservedBlankSlots.Count == 0
                ? _trailingBlankSlot
                : Math.Max(_reservedBlankSlots.Values.Max(), _trailingBlankSlot ?? -1);

        internal void ClearBlankSlotReservations()
        {
            _reservedBlankSlots.Clear();
            _trailingBlankSlot = null;
        }

        /// <summary>
        /// When true, <see cref="CssLayoutEngine.FlowBox"/>'s per-word page-break-avoidance check
        /// (<see cref="Dom.CssRect.WouldStraddleFragmentainer"/>, which stops the pass and records an
        /// <see cref="Fragmentation.InlineBreakToken"/> rather than placing a word that would straddle
        /// a fragmentainer boundary) is skipped. Set around a throwaway/measurement-only layout pass
        /// performed at a temporary, not-yet-final position - see
        /// <see cref="Dom.CssLayoutEngineFlex"/>'s <c>MeasureItem</c>/<c>ResizeItem</c>/stretch
        /// cross-size re-layout, which lay a flex item out at <c>(ClientLeft, ClientTop)</c> purely
        /// to measure its natural size before <c>AssignLocations</c> translates it (via
        /// <c>CssBox.OffsetTop</c>) to its real final position. Without this, a word whose temporary
        /// position happens to straddle a page boundary would end the measurement pass with a break
        /// token recorded against coordinates that have nothing to do with where the item actually
        /// ends up, and that pass is never resumed - permanently desyncing the word from its own box.
        /// <para>
        /// Also set, for a different reason, around a monolithic subtree's own children
        /// (<see cref="Dom.CssBox.LayoutContents"/>, #350): css-break-3 §2 forbids breaking such content
        /// at all, so a forced break inside it must not take effect either. <see cref="Dom.CssBox.ForcedBreakTopFor"/>
        /// reads this flag (rather than <see cref="IsFragmenting"/>, which is equally false once a pass
        /// has simply finished with none running at all) to tell "inside a suppressed subtree" from that.
        /// </para>
        /// </summary>
        internal bool SuppressWordPageBreaks { get; set; }

        /// <summary>
        /// The fragmentainer the current layout pass is filling, or null outside
        /// <see cref="LayoutDocument"/> (and for the unpaginated/measurement pass, which has no grid to
        /// break against). Layout reads this rather than the page grid directly wherever it needs to
        /// know which fragmentainer it is in.
        /// </summary>
        internal FragmentainerContext? CurrentFragmentainer { get; private set; }

        /// <summary>
        /// Makes <paramref name="nested"/> the fragmentainer being filled, returning the one it displaces
        /// for the caller to hand back to <see cref="LeaveNestedFragmentainer"/>.
        /// </summary>
        /// <remarks>
        /// A multi-column column is a fragmentainer in its own right
        /// (<see href="https://www.w3.org/TR/css-break-3/#fragmentainer">§2</see>), established by the
        /// columns engine inside the page the container sits on. A save/restore pair rather than a
        /// disposable scope for the same reason <see cref="DetachFragmentainer"/> is one: every
        /// call site is an <c>async</c> method, where a <c>ref struct</c> cannot live across an
        /// <c>await</c>.
        /// </remarks>
        internal FragmentainerContext? EnterNestedFragmentainer(FragmentainerContext nested)
        {
            var previous = CurrentFragmentainer;
            CurrentFragmentainer = nested;
            return previous;
        }

        internal void LeaveNestedFragmentainer(FragmentainerContext? previous) =>
            CurrentFragmentainer = previous;

        /// <summary>
        /// Whether a break may be taken for the content being laid out right now. False during a
        /// measurement pass at a provisional position, inside monolithic content, and outside a
        /// <see cref="LayoutDocument"/> pass, where there is no fragmentainer to break against.
        /// </summary>
        internal bool IsFragmenting => CurrentFragmentainer?.IsFragmenting ?? false;

        /// <summary>
        /// Detaches the fragmentainer for the duration of a measurement pass or a monolithic subtree, so
        /// nothing laid out inside can ask a fragmentation question at all. Hand the returned value back
        /// to <see cref="RestoreFragmentainer"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>An absence rather than a suppressed flag.</b> A box laid out at a provisional position — a
        /// flex or grid item being measured at the container's content origin, anything inside a
        /// monolithic subtree — has coordinates it is about to be moved away from, so every question
        /// about a fragmentainer is meaningless there, not merely unanswerable. Leaving a context in place
        /// with breaking switched off answered the "may I break?" question correctly while still letting
        /// <i>which</i> fragmentainer, and its band, be read: <c>CurrentFragmentainer is { HasOwnBand:
        /// true }</c> is asked in several places that never consult
        /// <see cref="IsFragmenting"/> at all, so a table cell inside a column could raise a column break
        /// from inside a pass that was supposed to be suppressed.
        /// </para>
        /// <para>
        /// A save/restore pair rather than a disposable scope because every call site is an
        /// <c>async</c> method, where a <c>ref struct</c> cannot live across an <c>await</c>.
        /// </para>
        /// </remarks>
        internal FragmentainerContext? DetachFragmentainer()
        {
            var previous = CurrentFragmentainer;
            CurrentFragmentainer = null;
            return previous;
        }

        /// <inheritdoc cref="DetachFragmentainer"/>
        internal void RestoreFragmentainer(FragmentainerContext? previous) => CurrentFragmentainer = previous;

        /// <summary>
        /// The inline flows currently walking their content (<see cref="CssLayoutEngine.CreateLineBoxes"/>
        /// between its start and the moment its lines are final), outermost first.
        /// </summary>
        /// <remarks>
        /// An absolutely positioned box inside a float, or inside an inline-block holding block-level
        /// content, is laid out as part of that box's own content - while the inline flow that owns the
        /// box's positioned inline ancestor is still mid-walk and that inline has no line fragments yet.
        /// Knowing which flows are open lets such a box be handed to the flow that owns its containing
        /// block, to be laid out once that flow's fragments exist
        /// (<see cref="CssLayoutEngine.TryDeferToEnclosingInlineFlow"/>).
        /// </remarks>
        internal List<CssLineBoxCoordinates> ActiveInlineFlows { get; } = [];

        /// <summary>
        /// Increments once per <see cref="LayoutDocument"/> invocation. A box records the generation it
        /// last laid out in, so resumption state left behind by an earlier invocation — the
        /// unrestricted-width double layout, the per-page-width reflow loop, <c>ShrinkToFit</c>'s
        /// re-layout — is recognised as stale instead of being resumed into.
        /// </summary>
        internal int LayoutGeneration { get; private set; }

        /// <summary>
        /// How many fragmentainer passes the last <see cref="LayoutDocument"/> ran — one, plus one per
        /// break it had to resume from. A document whose content never overflows a fragmentainer takes a
        /// single pass, which is what keeps the common case as cheap as the old single-flow model.
        /// </summary>
        internal int FragmentainerPasses { get; private set; }

        /// <summary>
        /// How many times the last <see cref="LayoutDocument"/> reached
        /// <see cref="LayoutTheRemainderMonolithically"/> — the last rung of
        /// <see cref="BreakRelaxation"/>'s ladder. Normally zero; a non-zero value says the document
        /// contains at least one fragmentainer layout could not advance past, so its remainder was
        /// laid out on one overflowing fragmentainer rather than being dropped.
        /// </summary>
        internal int LastResortRelayouts { get; private set; }

        /// <summary>
        /// How many times the last <see cref="LayoutDocument"/> went back to a pass it had already
        /// finished, to lay a keep-with-next run out again at its destination
        /// (<see href="https://www.w3.org/TR/css-break-3/#possible-breaks">§4.3</see>). Zero for a document
        /// whose corrections all fit inside the pass that discovered them — which is what makes "a pass was
        /// really re-entered" assertable rather than assumed, the same job
        /// <see cref="FragmentainerPasses"/> does for resumption.
        /// </summary>
        internal int PassRewinds { get; private set; }

        /// <summary>
        /// How many times the last <see cref="LayoutDocument"/> asked a fragmentation question about
        /// content whose own band (per <see cref="BandStartingAt"/>) disagreed with
        /// <see cref="CurrentFragmentainer"/>'s — i.e. how often a pass placed content past the
        /// fragmentainer it says it is filling without ever recording that as a break. Zero is the
        /// property <see href="https://github.com/jhaygood86/PeachPDF/issues/435">#435</see>
        /// establishes: every mechanism that can move flow into a later band (an inline tolerance
        /// spill, unbreakable overflow, a table row-loop jump, a flex/grid line relocation) must also
        /// advance <see cref="Fragmentation.FragmentainerContext.StepOverTo"/>, so the cursor never goes
        /// stale relative to where content actually landed. A non-zero value names the mechanism that
        /// does not yet do so — see <see cref="BandBeingFilled"/>.
        /// </summary>
        internal int CursorSpills { get; private set; }

        /// <summary>
        /// Defensive backstop on the driver loop, mirroring the cap the fragment-tree slot walk uses.
        /// </summary>
        private const int MaxFragmentainers = 100_000;

        /// <summary>
        /// Test-only override for <see cref="MaxFragmentainers"/>, so a unit test can drive the driver
        /// loop to exhaustion without actually running 100,000 passes. Null in production.
        /// </summary>
        internal int? MaxFragmentainersOverride { get; set; }

        /// <summary>
        /// Test-only override for <see cref="FragmentEmitter.VerifyPruningAgainstFullWalk"/>, so one
        /// render can be checked against the unpruned walk without turning the check on process-wide.
        /// Null in production, where the environment variable alone decides.
        /// </summary>
        /// <remarks>
        /// Per instance rather than static because the test suite runs its collections in parallel:
        /// a test that flipped a global would silently change what every concurrently-running test was
        /// doing. The environment variable stays as the way to run the <i>whole</i> suite under it.
        /// </remarks>
        internal bool? VerifyFragmentPruningOverride { get; set; }

#if DEBUG
        /// <summary>
        /// Test-only opt-in for <see cref="Fragmentation.FragmentEmitter"/>'s per-slot word-claim ledger
        /// (issue #1047's own diagnostic - see that type's remarks on <c>_wordClaimSite</c>). Off by
        /// default even in a DEBUG build: the ledger's own investigation found the same
        /// <c>ClaimsLine</c>/<c>FallsPast</c> straddle tie-break it targets already disagreeing with a
        /// handful of OTHER, unrelated fixtures that this diagnostic was never meant to adjudicate - each
        /// a real, separate finding worth its own follow-up, not evidence against the ledger itself.
        /// Scoping the check to opt-in keeps it precise and false-positive-free for the ordinary
        /// block-flow shape #1047 is about, without either turning those other fixtures into unplanned bug
        /// reports or trying to special-case every one of them away here.
        /// </summary>
        /// <remarks>
        /// Defaults from <c>PEACHPDF_VERIFY_WORD_CLAIMS=1</c> in the environment, the same idiom
        /// <see cref="Fragmentation.FragmentEmitter.VerifyPruningAgainstFullWalk"/> uses, so the ledger can
        /// be run over the *whole* suite in one pass rather than only the handful of fixtures that
        /// currently set this per-instance. A full-suite run with the env var set (after #1202's fix for
        /// #1047) still finds two of the three fixtures #1200 originally listed as live-but-out-of-scope
        /// live: table rowspan continuation (<see href="https://github.com/jhaygood86/PeachPDF/issues/1210">#1210</see>)
        /// and a <c>break-inside:avoid</c> box taller than one band, whose translate fallback trips the
        /// identical gap (<see href="https://github.com/jhaygood86/PeachPDF/issues/1211">#1211</see>) - both
        /// through <c>FragmentEmitter.Finish()</c>'s own stale-slot replay loop, which calls
        /// <c>EmitSlot</c> directly and so never sets <c>_currentPassFromSlot</c>, leaving the tie-break
        /// unconditional there exactly as it was before #1202. The multi-column no-progress-backstop and
        /// flex/grid wrapping-column shapes #1200 also listed did not reproduce anywhere in that same
        /// full-suite run.
        /// </remarks>
        internal bool VerifyWordClaims { get; set; } =
            Environment.GetEnvironmentVariable("PEACHPDF_VERIFY_WORD_CLAIMS") == "1";
#endif

        /// <summary>
        /// The top-left most location of the rendered html.<br/>
        /// This will offset the top-left corner of the rendered html.
        /// </summary>
        public RPoint Location { get; set; }

        /// <summary>
        /// The max width and height of the rendered html.<br/>
        /// The max width will effect the html layout wrapping lines, resize images and tables where possible.<br/>
        /// The max height does NOT effect layout, but will not render outside it (clip).<br/>
        /// <see cref="ActualSize"/> can be exceed the max size by layout restrictions (unwrapable line, set image size, etc.).<br/>
        /// Set zero for unlimited (width\height separately).<br/>
        /// </summary>
        public RSize MaxSize { get; set; }

        /// <summary>
        /// The actual size of the rendered html (after layout)
        /// </summary>
        public RSize ActualSize { get; set; }

        /// <summary>
        /// Whether any box in the current document has <c>float: left/right</c>. Computed once per
        /// <see cref="PerformLayout"/> call and used to let float-intersection lookups
        /// (<see cref="Utils.DomUtils.GetFirstIntersectingFloatBox"/>) skip their tree walk entirely for
        /// the common case of a document with no floated content at all.
        /// </summary>
        internal bool HasFloatedBoxes { get; private set; }

        /// <summary>
        /// How many float-intersection lookups (<see cref="Utils.DomUtils.GetFirstIntersectingFloatBox"/>)
        /// layout made during the current <see cref="PerformLayout"/> call, counted before the
        /// <see cref="HasFloatedBoxes"/> short-circuit is consulted. Reset at the start of each
        /// <see cref="PerformLayout"/>, so it describes one layout rather than the container's lifetime -
        /// and note that one <see cref="PerformLayout"/> can invoke <c>LayoutDocument</c> several times
        /// (the per-page reflow loop re-runs it until the box-to-page assignment settles), so this is the
        /// total across all of them rather than a per-pass figure.
        /// Exists so a regression test can assert the *complexity* of the float scan - the work it does per
        /// box - deterministically, instead of timing a render and hoping a contended CI runner cooperates.
        /// </summary>
        internal long FloatScanCalls { get; private set; }

        /// <summary>
        /// How many boxes those lookups actually examined during the current <see cref="PerformLayout"/>
        /// call. The short-circuit makes this exactly zero for a document with no floats; without it, each
        /// lookup walks to the root re-scanning every preceding sibling's whole subtree, so the total grows
        /// with the square of the document size. A bound of this against the document's box count is
        /// therefore the O(n) vs O(n²) assertion itself, and it does not depend on the clock.
        /// </summary>
        internal long FloatScanBoxVisits { get; private set; }

        /// <summary>
        /// Records that one float-intersection lookup was made. Called by
        /// <see cref="Utils.DomUtils.GetFirstIntersectingFloatBox"/>.
        /// </summary>
        internal void RecordFloatScanCall() => FloatScanCalls++;

        /// <summary>
        /// Records how many boxes one float-intersection lookup examined. Called by
        /// <see cref="Utils.DomUtils.GetFirstIntersectingFloatBox"/>.
        /// </summary>
        /// <param name="boxesVisited">Number of boxes the lookup's tree walk examined.</param>
        internal void RecordFloatScanBoxVisits(int boxesVisited) => FloatScanBoxVisits += boxesVisited;

        /// <summary>
        /// How many times <see cref="Fragmentation.FragmentEmitter"/>'s <c>BuildDraft</c> walk visited a
        /// box during the current <see cref="PerformLayoutOnePass"/> call - i.e. across every
        /// fragmentainer pass's own emission plus every <c>CatchUpStaleSlotsBehind</c> re-walk. Reset
        /// alongside <see cref="FloatScanCalls"/>, for the same reason: a regression test can assert the
        /// *complexity* of fragment emission - the work it repeats per page - deterministically, rather
        /// than timing a render (<see href="https://github.com/jhaygood86/PeachPDF/issues/917">#917</see>,
        /// the analogue of the float-scan guard for <see href="https://github.com/jhaygood86/PeachPDF/issues/482">#482</see>).
        /// </summary>
        internal long BuildDraftCalls { get; private set; }

        /// <summary>
        /// Records that <c>FragmentEmitter.BuildDraft</c> visited one box. Called by
        /// <see cref="Fragmentation.FragmentEmitter"/>.
        /// </summary>
        internal void RecordBuildDraftCall() => BuildDraftCalls++;

        /// <summary>
        /// Whether any box in the current document asks for <c>box-decoration-break: clone</c>. Reserving room
        /// for a cloned border and padding at a break (css-break-3 §6.2) means asking, for a great many words,
        /// what a box's ancestors declared; this settles the answer once so a document that clones nothing —
        /// almost every document — never pays for the walk.
        /// </summary>
        internal bool HasCloneDecorations { get; private set; }

        /// <summary>
        /// Whether any box in the current document establishes a <c>@container</c> size query container
        /// (<c>container-type: size</c>/<c>inline-size</c>). Gates the container-query convergence loop
        /// in <see cref="PerformLayout"/> - a document with none (the overwhelming majority) never pays
        /// for more than this one settling walk plus the one unconditional baseline layout pass every
        /// document already took before this feature existed.
        /// </summary>
        internal bool HasSizeContainers { get; private set; }

        /// <summary>
        /// Whether any box in the current document is out-of-flow (floated, absolutely positioned, or
        /// fixed). Computed alongside <see cref="HasFloatedBoxes"/> and used by
        /// <see cref="Paint.FragmentPainter"/> to decide whether Bounds-based page-visibility pruning is safe (an
        /// out-of-flow descendant's visual position can fall outside its "invisible" ancestor's own
        /// Bounds, so that pruning is only safe with none anywhere in the document).
        /// </summary>
        internal bool HasOutOfFlowBoxes { get; private set; }

        /// <summary>
        /// Whether any non-root box in the current document either is out-of-flow or establishes its own
        /// stacking context (see <see cref="Utils.DomUtils.IsStackingContextBox"/> - position+z-index,
        /// fixed/sticky, a flex item with z-index, opacity &lt; 1, or a non-identity transform). Computed
        /// alongside <see cref="HasFloatedBoxes"/>/<see cref="HasOutOfFlowBoxes"/> and used by
        /// <see cref="Paint.StackingOrder.Flatten"/> to skip searching for stacking-context
        /// participants to hoist past normal-flow wrapper boxes entirely when there's nothing to hoist.
        /// </summary>
        internal bool HasStackingHoistCandidates { get; private set; }

        public RSize PageSize { get; set; }

        /// <summary>
        /// Page size (width × height) in PDF points derived from the CSS @page { size: ... } rule.
        /// Null when no size rule is present. Stored in PDF points, not internal pixel units.
        /// </summary>
        public XSize? CssPageSize { get; set; }

        /// <summary>
        /// All @page rules parsed from the document's stylesheets, in cascade order.
        /// </summary>
        public IReadOnlyList<PageRule> PageRules { get; internal set; } = [];

        /// <summary>
        /// Custom properties registered with <c>@property</c> at-rules, keyed by name (case-sensitive, per
        /// CSS custom-property naming). Supplies typed custom properties' <c>initial-value</c> during var()
        /// resolution and governs their <c>inherits</c> behavior. Rebuilt each parse pass.
        /// </summary>
        internal IReadOnlyDictionary<string, RegisteredProperty> RegisteredProperties { get; set; }
            = new Dictionary<string, RegisteredProperty>(StringComparer.Ordinal);

        /// <summary>
        /// Palette overrides registered with <c>@font-palette-values</c> at-rules, keyed by
        /// <c>(name, normalized-family)</c>. Consulted when resolving <c>font-palette: &lt;dashed-ident&gt;</c>
        /// for a COLR/CPAL color font. Rebuilt each parse pass.
        /// </summary>
        internal IReadOnlyDictionary<(string Name, string Family), RegisteredFontPalette> FontPaletteValues { get; set; }
            = new Dictionary<(string, string), RegisteredFontPalette>();

        /// <summary>
        /// Named OpenType feature-value aliases registered with <c>@font-feature-values</c> at-rules,
        /// keyed by <c>(normalized-family, block-kind, name)</c>. Consulted when resolving
        /// <c>font-variant-alternates</c> functions (<c>styleset()</c>, <c>character-variant()</c>, etc.).
        /// Rebuilt each parse pass.
        /// </summary>
        internal IReadOnlyDictionary<(string Family, FontFeatureValueBlockKind Kind, string Name), RegisteredFontFeatureValues> FontFeatureValues { get; set; }
            = new Dictionary<(string, FontFeatureValueBlockKind, string), RegisteredFontFeatureValues>();

        /// <summary>
        /// The relative-unit resolution context for per-page <c>@page</c> margins, captured by
        /// <c>DomParser.CascadeApplyPageStyles</c> on every parse pass (see
        /// <see cref="Entities.PageLengthContext"/> for why capture-at-parse). Null until a document
        /// is parsed; consumers fall back to absolute-only resolution then.
        /// </summary>
        internal Entities.PageLengthContext? PageLengthContext { get; set; }

        /// <summary>
        /// the top margin between the page start and the text
        /// </summary>
        public double MarginTop
        {
            get => _marginTop;
            set
            {
                if (value > -1)
                    _marginTop = value;
            }
        }

        /// <summary>
        /// the bottom margin between the page end and the text
        /// </summary>
        public double MarginBottom
        {
            get => _marginBottom;
            set
            {
                if (value > -1)
                    _marginBottom = value;
            }
        }

        /// <summary>
        /// the left margin between the page start and the text
        /// </summary>
        public double MarginLeft
        {
            get => _marginLeft;
            set
            {
                if (value > -1)
                    _marginLeft = value;
            }
        }

        /// <summary>
        /// the right margin between the page end and the text
        /// </summary>
        public double MarginRight
        {
            get => _marginRight;
            set
            {
                if (value > -1)
                    _marginRight = value;
            }
        }

        /// <summary>
        /// the root css box of the parsed html
        /// </summary>
        internal CssBox? Root { get; private set; }

        private SvgClipPathRegistry? _svgClipPathRegistry;

        /// <summary>
        /// The document-wide <c>&lt;clipPath&gt;</c> id index an HTML element's <c>clip-path: url(#id)</c>
        /// (<see cref="CssClipPathResolver"/>) resolves against - see <see cref="SvgClipPathRegistry"/>'s
        /// own remarks for why this needs to be document-wide rather than per-<c>&lt;svg&gt;</c>.
        /// </summary>
        internal SvgClipPathRegistry SvgClipPaths => _svgClipPathRegistry ??= new SvgClipPathRegistry(this);

        /// <summary>
        /// Decoded image resources resolved by <see cref="Handlers.ImageLoadHandler"/>, keyed by resolved
        /// absolute source URI (<see cref="RUri.AbsoluteUri"/>) - shared by every <c>ImageLoadHandler</c>
        /// this render creates (<c>&lt;img&gt;</c>, <c>&lt;object&gt;</c>, <c>background-image</c>/
        /// <c>list-style-image</c>/<c>content: url()</c>) so the same source referenced more than once is
        /// only fetched and decoded once. Deliberately NOT cleared by <see cref="Clear"/>: the container-
        /// query convergence loop disposes and rebuilds <see cref="Root"/> wholesale between passes within
        /// one render (see <see cref="SetHtml"/>), and a rebuilt box tree's fresh handlers should still hit
        /// this cache rather than re-decoding. Owns the cached <see cref="RImage"/> instances - disposed
        /// only in <see cref="Dispose(bool)"/>, once, at the true end of this render.
        /// </summary>
        private readonly Dictionary<string, (RImage? Image, SvgDocument? SvgDocument)> _resolvedImageResources = new();

        /// <summary>
        /// Looks up a previously resolved image/SVG resource by its resolved absolute source URI.
        /// </summary>
        internal bool TryGetResolvedImageResource(string absoluteUri, out (RImage? Image, SvgDocument? SvgDocument) resource)
            => _resolvedImageResources.TryGetValue(absoluteUri, out resource);

        /// <summary>
        /// Records a successfully resolved image/SVG resource under its resolved absolute source URI, so a
        /// later <see cref="Handlers.ImageLoadHandler"/> for the same source reuses it instead of decoding
        /// again. Every current caller checks <see cref="TryGetResolvedImageResource"/> first and never
        /// reaches here on a hit, so this never actually overwrites a live entry today - but disposes one
        /// if it ever does, rather than silently orphaning it undisposed until this render's teardown.
        /// </summary>
        internal void CacheResolvedImageResource(string absoluteUri, RImage? image, SvgDocument? svgDocument)
        {
            if (_resolvedImageResources.TryGetValue(absoluteUri, out var existing) && !ReferenceEquals(existing.Image, image))
            {
                existing.Image?.Dispose();
            }

            _resolvedImageResources[absoluteUri] = (image, svgDocument);
        }

        /// <summary>
        /// The document's root (<c>&lt;html&gt;</c>) element's own resolved <c>writing-mode</c>, defaulting
        /// to the CSS initial <see cref="WritingMode.HorizontalTb"/> when there is no document yet. Set
        /// once per document parse (<c>DomParser.GenerateCssTree</c>, alongside <see cref="DocumentLanguage"/>)
        /// rather than looked up on demand, since <c>vi</c>/<c>vb</c> unit resolution
        /// (<see cref="Dom.CssBox.GetViewportUnitBasis"/>) reads this on effectively every CSS length
        /// parsed in the document - re-walking the DOM for it each time would be a real perf regression.
        /// </summary>
        internal WritingMode RootWritingMode { get; set; } = WritingMode.HorizontalTb;

        /// <summary>
        /// The URI relative references in this document resolve against: its own <c>&lt;base href&gt;</c>
        /// when it declares a usable one, else <see cref="RAdapter.BaseUri"/>. The single place that rule
        /// lives - <see cref="CommonUtils.ResolveAgainstDocumentBase"/> (images, stylesheets),
        /// <c>HtmlContainer.ResolveHref</c> (links, bookmark targets) and
        /// <c>PdfGenerator.HandleRunningElementLinks</c> all read it from here.
        /// <para>
        /// The <c>&lt;base&gt;</c> lookup is memoized per box tree because it is a
        /// <see cref="DomUtils.GetBoxByTagName"/> walk that cannot short-circuit when the document
        /// declares no <c>&lt;base&gt;</c> at all - it visits every box in the tree - while callers ask
        /// once per reference rather than once per document: a link inside a css-gcpm-3 running element is
        /// re-resolved on every page its element was selected onto. Rendering a 188-page report with one
        /// such link spent 5 525 506 box visits in that walk; it now spends 79 849, worth about 5% of the
        /// whole render. The <see cref="RAdapter.BaseUri"/> fallback is deliberately re-read on each
        /// access rather than frozen into the memo - it is a cheap property on the adapter, not a walk,
        /// and a document that declares no base should keep answering whatever the adapter currently says.
        /// </para>
        /// <para>
        /// The memo is keyed on <see cref="Root"/>'s object identity, which is replaced wholesale by every
        /// re-parse (<see cref="SetHtml"/> clears first, and the <c>@container</c> convergence loop and
        /// <c>PdfGenerator.SetContent</c>'s shrink-to-fit path both go through it) - not on
        /// <see cref="Dom.CssBox.Id"/>, which is deliberately stable across those two back-to-back parses.
        /// Unlike <see cref="_idIndex"/>, which walks up from an arbitrary box so that it also works while
        /// <see cref="Root"/> is still null mid-parse, this gives up on a null root: see below.
        /// </para>
        /// </summary>
        internal RUri? DocumentBaseUri
        {
            get
            {
                // Root is only assigned once SetHtml returns, so a <link>/@import stylesheet - loaded from
                // inside DomParser.GenerateCssTree - resolves with no document base and falls back to the
                // adapter's. Long-standing behaviour, kept as-is. (An <img> is not in that group: images
                // are resolved during layout, via CssBoxImage.MeasureWordsSize, by which point Root is
                // assigned and <base> is honoured.) The early return is only a fast path - the memo key
                // already handles a null root, since the next read against a real tree fails
                // ReferenceEquals and re-walks.
                if (Root is null) return Adapter.BaseUri;

                if (!ReferenceEquals(_documentBaseRoot, Root))
                {
                    var href = DomUtils.GetBoxByTagName(Root, "base")?.HtmlTag?.TryGetAttribute("href", "");
                    // A malformed href still throws from exactly where it did before - and, since the key
                    // is only committed afterwards, from every later read too rather than just the first.
                    _documentBase = string.IsNullOrWhiteSpace(href) ? null : new RUri(href);
                    _documentBaseRoot = Root;
                }

                return _documentBase ?? Adapter.BaseUri;
            }
        }

        /// <summary>
        /// Metadata extracted from the HTML head elements.
        /// </summary>
        internal HtmlDocumentMetadata? DocumentMetadata { get; private set; }

        /// <summary>
        /// The document's language, from the root <c>&lt;html lang="..."&gt;</c> attribute — <c>null</c>
        /// if the document declares none. Used by <c>hyphens: auto</c> (per the CSS Text spec, automatic
        /// hyphenation requires knowing the language; when unknown, a spec-compliant renderer doesn't
        /// hyphenate). <see cref="PdfGenerateConfig.DefaultLanguage"/> can supply an app-level fallback
        /// when a document declares none, applied by the caller (see <see cref="PdfGenerator"/>) — this
        /// property itself only ever reflects what the document actually declared.
        /// </summary>
        internal string? DocumentLanguage { get; set; }

        /// <summary>
        /// The CSS media type the cascade matches <c>@media</c> rules against (see
        /// <see cref="PdfGenerateConfig.Media"/>). Defaults to <c>"print"</c>; set by
        /// <c>PdfGenerator.SetContent</c> before the DOM/CSS tree is generated.
        /// </summary>
        internal string Media { get; set; } = "print";

        /// <summary>
        /// The color scheme reported to <c>@media (prefers-color-scheme: ...)</c> queries (see
        /// <see cref="PdfGenerateConfig.PreferredColorScheme"/>). Defaults to
        /// <see cref="PdfColorScheme.Light"/>; set by <c>PdfGenerator.SetContent</c>.
        /// </summary>
        internal PdfColorScheme PreferredColorScheme { get; set; } = PdfColorScheme.Light;

        /// <summary>
        /// When true, the document's own author style sheets (<c>&lt;style&gt;</c>/<c>&lt;link&gt;</c>) are
        /// not collected during CSS parsing (see <see cref="PdfGenerateConfig.IgnoreAuthorStyleSheets"/>).
        /// Set by <c>PdfGenerator.SetContent</c> before the DOM/CSS tree is generated.
        /// </summary>
        internal bool IgnoreAuthorStyleSheets { get; set; }

        /// <summary>
        /// Orchestrates tagged-PDF structure-tree/MCID bookkeeping during painting. Set by
        /// <c>PdfGenerator</c> before the page-render loop only when
        /// <c>PdfGenerateConfig.EnableTaggedPdf</c> is set; null (the default) means tagging is
        /// disabled and <c>CssBox.PaintImp</c>'s tagging wrapper is a no-op.
        /// </summary>
        internal Handlers.StructureTagBuilder? StructureTagBuilder { get; set; }

        /// <summary>
        /// Orchestrates AcroForm field/widget bookkeeping during PDF generation. Set by
        /// <c>PdfGenerator</c> before painting only when
        /// <c>PdfGenerateConfig.EnableInteractivePdfForms</c> is set; null (the default) means
        /// interactive forms are disabled and both the form-field content painter and
        /// <c>PdfGenerator.HandleFormFields</c> are no-ops.
        /// </summary>
        internal Handlers.FormFieldBuilder? FormFieldBuilder { get; set; }

        /// <summary>
        /// Init with optional document and stylesheet.
        /// </summary>
        /// <param name="htmlSource">the html to init with, init empty if not given</param>
        /// <param name="baseCssData">optional: the stylesheet to init with, init default if not given</param>
        /// <param name="containerSizes">Every eligible <c>@container</c> query container's resolved size
        /// from the previous layout pass - see <see cref="DomParser.GenerateCssTree"/>. <c>null</c> for
        /// every caller except <see cref="PerformLayout"/>'s own container-query convergence loop, which
        /// re-invokes this method between passes via <see cref="_lastHtmlSource"/>/<see cref="_lastBaseCssData"/>.</param>
        public async Task SetHtml(string htmlSource, CssData? baseCssData = null, ContainerQuerySizes? containerSizes = null)
        {
            _lastHtmlSource = htmlSource;
            _lastBaseCssData = baseCssData;

            Clear();
            if (string.IsNullOrEmpty(htmlSource)) return;

            CssData = baseCssData ?? await Adapter.GetDefaultCssData();

            DomParser parser = new(CssParser);
            (Root, CssData, DocumentMetadata) = await parser.GenerateCssTree(htmlSource, this, CssData, containerSizes);
        }

        // Remembered so PerformLayout's container-query convergence loop can re-invoke SetHtml itself
        // between passes (a full re-parse + re-cascade, the same "wholesale redo" PdfGenerator.SetContent's
        // ShrinkToFit path already trusts) without its caller having to hold onto the original html/cssData.
        private string? _lastHtmlSource;
        private CssData? _lastBaseCssData;

        /// <summary>
        /// Clear the content of the HTML container releasing any resources used to render previously existing content.
        /// </summary>
        public void Clear()
        {
            if (Root == null) return;

            Root.Dispose();
            Root = null;
            // Built once at the end of layout and read only by PdfGenerator (container.HtmlContainerInt.FragmentTree?.Fragmentainers,
            // already null-safe there) - but its BoxFragments reference the same CssBox tree Root does,
            // so leaving this set would keep the whole disposed tree reachable until the next document's
            // own layout overwrites it, the same reference-leak shape ClearBlankSlotReservations() and
            // the footnote tracking resets below already exist to avoid.
            FragmentTree = null;
            DocumentLanguage = null;
            // Dropped with the tree it was keyed on, so a disposed tree is not kept reachable by the memo
            // until the next document happens to read through it.
            _documentBase = null;
            _documentBaseRoot = null;
            // Keyed to the (now-disposed) Root it indexed - a stale registry would resolve clip-path:
            // url(#id) against the previous document's clipPath ids instead of the new one's.
            _svgClipPathRegistry = null;
            // Same shape again: GetBoxById only rebuilds this when _idIndexRoot != the current Root
            // (already null-safe either side of that comparison), but left alone it would hold every
            // id-tagged box of the disposed tree - plausibly the largest of these leaks, since any
            // document with ids anywhere builds one - until the next GetBoxById call for the new
            // document happens to rebuild it.
            _idIndex = null;
            _idIndexRoot = null;
            // Read only by PdfGenerator during this same render's own paint phase, always after
            // ResolveCanvasBackground has freshly reassigned it - never read in the window between
            // Clear() and the next completed layout, so nulling it here is safe as well as consistent.
            CanvasBackgroundBox = null;
            ClearNamedStrings();
            ClearRunningElements();
            ClearNamedPageElements();
            FootnoteCalls.Clear();
            // Not reachable from the tree Root.Dispose() above just released, so disposed on their own.
            foreach (var shell in DisplayContentsShells) shell.Dispose();
            DisplayContentsShells.Clear();
            FootnoteAreaHeightsBySlot = [];
            _footnoteAreasBySlot = [];
            FootnoteNumberContext = null;
            _footnoteNumberingSignature = string.Empty;
            ColumnFragmentainers.Clear();
            FootnoteAreaHeightsByColumn = [];
            PageFloats.Clear();
            TopFloatAreaHeightsBySlot = [];
            BottomFloatAreaHeightsBySlot = [];
            TopFloatAreaHeightsByColumn = [];
            BottomFloatAreaHeightsByColumn = [];
            PageFloatColumns.Clear();
            // Keyed by CssBox, same reference-leak reason as the footnote dictionaries above.
            PageFloatPlacements = [];
            // Keyed by CssBox/CssBoxFootnoteCall, same reason ClearBlankSlotReservations() below is -
            // dropping the tree without this would keep every box in it (and everything it in turn
            // reaches via ParentBox/Boxes) reachable until the next document's own footnotes happened to
            // reach ResolveFootnotesForThisAttempt, the only other place these are cleared.
            foreach (var box in _footnotePolicyForcedBreakBoxes)
            {
                box.FootnotePolicyForcedBreakBefore = false;
            }
            _footnotePolicyForcedBreakBoxes.Clear();
            FootnotePolicyForcedLineCalls.Clear();
            FootnotePolicyLineBreaksTakenThisPass.Clear();
            // New content means new @page rules: drop cached slot geometry, the vertical-override
            // scan, and the captured relative-unit context so nothing consults the previous
            // document's bands before the next layout pass (which resets again defensively).
            PageGeometry.Reset();
            PageLengthContext = null;
            // Keyed by CssBox, so dropping the tree without this would retain every box in it.
            ClearBlankSlotReservations();
        }

        /// <summary>
        /// Attaches an already-built, already-styled <paramref name="root"/> as this container's document
        /// root, for the declarative document-building API - the structural counterpart of <see cref="SetHtml"/>
        /// for a tree that was constructed directly in C# (<see cref="Layout.DocumentBuilder"/>) rather
        /// than parsed from an HTML string. <see cref="SetHtml"/>'s own <c>DomParser.GenerateCssTree</c>
        /// does a large amount of HTML-correction/cascade work (anonymous table fixup, text-box
        /// splitting, pseudo-element synthesis, the cascade itself) that has nothing to do for a
        /// hand-built tree whose boxes already carry their own final, directly-assigned style - this
        /// performs only the structural bookkeeping such a tree still needs.
        /// </summary>
        /// <param name="root">
        /// The already-built tree, rooted at a block-level <see cref="CssBox"/> constructed via
        /// <see cref="CssBox.CreateBlock()"/>/<see cref="CssBox.CreateBox(CssBox,HtmlTag?,CssBox?)"/> with
        /// every box's own <see cref="CssBox.InheritStyle"/> already called in tree order. Text boxes must
        /// have their <see cref="CssBox.Text"/> set but must NOT have had <see cref="CssBox.ParseToWords"/>
        /// called yet - this method calls it, after bidi levels are assigned across the whole tree, so
        /// word-splitting sees the correct per-box level array (mirrors <c>DomParser.GenerateCssTree</c>'s
        /// own AssignBidiLevels-before-CorrectTextBoxes ordering; splitting into words before bidi levels
        /// exist would visibly mis-order a mixed LTR/RTL paragraph).
        /// </param>
        /// <param name="documentLanguage">
        /// The document's language (for <c>hyphens: auto</c> and the PDF's own <c>/Lang</c>), since a
        /// declarative document has no <c>&lt;html lang&gt;</c> to read one from automatically. Null leaves
        /// <see cref="DocumentLanguage"/> unset.
        /// </param>
        /// <param name="stylesheet">
        /// A document-level stylesheet attached via <see cref="Layout.IDocumentBuilder.Stylesheet"/>, or null
        /// for a declarative document styled purely by direct
        /// <see cref="Utils.CssPropertyFactory.Set(CssBox, string, string)"/> calls (the only behavior before
        /// this parameter existed). When non-null: its <c>@font-face</c> rules are registered
        /// (<see cref="DomParser.CascadeApplyStyleFonts"/>), its <c>@property</c>/<c>@font-palette-values</c>
        /// rules populate <see cref="RegisteredProperties"/>/<see cref="FontPaletteValues"/>, and its
        /// ordinary style rules are matched and applied against the whole <paramref name="root"/> tree
        /// (<see cref="DomParser.ApplyDeclarativeStylesheet"/>) - all before bidi levels are assigned,
        /// mirroring <see cref="DomParser.GenerateCssTree"/>'s own cascade-before-bidi ordering, so a
        /// stylesheet-set <c>direction</c> is visible to bidi assignment. Its own <c>@page</c> rules are
        /// handled separately by <see cref="PdfGenerator.AddDeclarativePage"/> (whole-document page geometry
        /// needs the page's own <c>PageSize</c>/margins already resolved, and this method runs before that).
        /// </param>
        /// <param name="fragmentDisplayContentsShells">The <c>display: contents</c> elements
        /// <c>IContainer.Html(...)</c> fragments already spliced out of their own trees while the document was
        /// built (<see cref="Utils.CssPropertyFactory.DisplayContentsShells"/>); adopted into
        /// <see cref="DisplayContentsShells"/> so they stay addressable.</param>
        internal async ValueTask SetDeclarativeRoot(CssBox root, string? documentLanguage, PeachPdfCssContent? stylesheet = null, IReadOnlyList<CssBox>? fragmentDisplayContentsShells = null)
        {
            Clear();
            CssBox.ClearCounter();

            // Fragments spliced their own display: contents elements out before they were grafted in, so
            // this is the only place left that still knows about them.
            if (fragmentDisplayContentsShells is not null)
                DisplayContentsShells.AddRange(fragmentDisplayContentsShells);

            root.IsRoot = true;
            root.HtmlContainer = this;
            Root = root;

            // v1 scope: no vertical writing modes for the declarative API (a separate, much larger
            // feature than bidi/RTL, and orthogonal to it) - always the initial value, exactly as
            // DomParser.GenerateCssTree only ever resolves this from a real <html> box's own
            // writing-mode declaration, which a declarative document has no equivalent of.
            RootWritingMode = WritingMode.HorizontalTb;
            DocumentLanguage = documentLanguage;

            // Mirrors SetHtml's own "use the caller's CssData directly" idiom (no clone) - there is nothing
            // appended to it on this path (no <style>/<link> discovery step exists for a declarative tree),
            // so there is nothing a clone would need to protect the caller's PeachPdfCssContent instance from.
            CssData = stylesheet?.CssData ?? await Adapter.GetDefaultCssData();

            if (stylesheet is not null)
            {
                await DomParser.CascadeApplyStyleFonts(CssData, Adapter);

                var cssValueParser = new CssValueParser(Adapter);
                RegisteredProperties = RegisteredProperty.BuildRegistry(CssData, cssValueParser);
                FontPaletteValues = RegisteredFontPalette.BuildRegistry(CssData, cssValueParser);
                FontFeatureValues = RegisteredFontFeatureValues.BuildRegistry(CssData);

                var media = MediaQueryContext.FromContainer(this, Media);
                DomParser.ApplyDeclarativeStylesheet(root, CssData, media, Adapter, DisplayContentsShells);
            }

            // Mirrors DomParser.GenerateCssTree's own ordering (AssignBidiLevels, then the tree walk that
            // calls ParseToWords on every text box) - see this method's own <paramref name="root"/> remarks
            // for why the order matters.
            CssBidiParagraphResolver.AssignBidiLevels(root);

            // After bidi, as in GenerateCssTree: a stylesheet-assigned display: contents is spliced only
            // once the passes that read the as-authored tree are done. A fragment's own shells were
            // already spliced when it was built, and are skipped.
            DomParser.FlattenDisplayContents(DisplayContentsShells, new CssValueParser(Adapter));

            ParseToWordsRecursive(root);
        }

        private static void ParseToWordsRecursive(CssBox box)
        {
            if (box.Text != null)
            {
                box.ParseToWords();
            }

            foreach (var child in box.Boxes)
            {
                ParseToWordsRecursive(child);
            }
        }

        /// <summary>
        /// Get all the links in the HTML with the element rectangle and href data, additionally
        /// collecting every bookmark-candidate box (<c>bookmark-level != none</c>) into
        /// <paramref name="bookmarkBoxes"/> from the same tree walk when non-null (see
        /// <see cref="Utils.DomUtils.GetAllLinkAndBookmarkBoxes"/>) - the only caller is
        /// <see cref="HtmlContainer.GetLinks(List{CssBox}?)"/>, which is where the public,
        /// bookmark-agnostic <see cref="HtmlContainer.GetLinks()"/> contract lives.
        /// </summary>
        internal List<LinkElementData<RRect>> GetLinks(List<CssBox>? bookmarkBoxes)
        {
            var linkBoxes = new List<CssBox>();
            if (bookmarkBoxes is null)
                DomUtils.GetAllLinkBoxes(Root, linkBoxes);
            else
                DomUtils.GetAllLinkAndBookmarkBoxes(Root, linkBoxes, bookmarkBoxes);

            var linkElements = new List<LinkElementData<RRect>>();
            foreach (var box in linkBoxes)
            {
                linkElements.Add(new LinkElementData<RRect>(box.GetAttribute("id"), box.GetAttribute("href"), CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds), box));
            }

            // The elements the walk above cannot reach. A link on a display:contents <a> is still a link
            // (only the box tree is affected, CSS Display 3 §2.5), and it covers what its content covers:
            // one rectangle per lifted child. A bookmark is placed by where its content begins.
            var bookmarkFloor = 0;
            var bookmarkFloorY = double.NaN;

            foreach (var shell in DisplayContentsShells)
            {
                if (shell is { IsClickable: true, Visibility.Value: Visibility.Visible })
                {
                    foreach (var child in shell.ContentChildren)
                    {
                        if (child.ParentBox is null || child.DerivedStyle.ActualDisplay == Keywords.None) continue;

                        linkElements.Add(new LinkElementData<RRect>(shell.GetAttribute("id"), shell.GetAttribute("href"), CommonUtils.GetFirstValueOrDefault(child.Rectangles, child.Bounds), shell));
                    }
                }

                if (bookmarkBoxes is not null && shell.BookmarkLevel is { Length: > 0 } level && level != Keywords.None)
                {
                    DomUtils.InsertByDocumentPosition(bookmarkBoxes, shell, ref bookmarkFloor, ref bookmarkFloorY);
                }
            }

            var svgLinks = new List<(RRect Rect, string Href)>();
            DomUtils.GetAllSvgLinks(Root, svgLinks);

            foreach (var (rect, href) in svgLinks)
            {
                linkElements.Add(new LinkElementData<RRect>(string.Empty, href, rect));
            }

            return linkElements;
        }

        /// <summary>
        /// Get the rectangle of html element as calculated by html layout.<br/>
        /// Element if found by id (id attribute on the html element).<br/>
        /// Note: to get the screen rectangle you need to adjust by the hosting control.<br/>
        /// </summary>
        /// <param name="elementId">the id of the element to get its rectangle</param>
        /// <returns>the rectangle of the element or null if not found</returns>
        public RRect? GetElementRectangle(string elementId)
        {
            ArgChecker.AssertArgNotNullOrEmpty(elementId, "elementId");

            var box = DomUtils.GetBoxById(Root, elementId.ToLower());
            if (box is null) return null;

            box = DomUtils.ResolveGeometryBox(box);
            return CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds);
        }

        /// <summary>
        /// Cached id -&gt; box lookup for <c>target-counter()</c>/<c>target-text()</c> resolution
        /// (<see cref="CssContentEngine.AppendTargetCounter"/>/<see cref="CssContentEngine.ResolveTargetText"/>),
        /// which can call this many times per document (once per resolved reference, across up to 4
        /// content-resolution passes) - unlike <see cref="GetElementRectangle"/>'s single-lookup use,
        /// repeating <see cref="DomUtils.GetBoxById"/>'s own uncached O(n) walk here would be O(n*m) for a
        /// table of contents with m entries.
        /// </summary>
        /// <param name="fromBox">
        /// Any box in the tree to search - its topmost ancestor is walked to and used as the search root,
        /// rather than this container's own <see cref="Root"/>. <c>ApplyContent</c>'s very first
        /// resolution pass runs from <c>DomParser.CorrectTextBoxes</c>, which happens *inside*
        /// <c>DomParser.GenerateCssTree</c> - <see cref="Root"/> is only assigned by <see cref="SetHtml"/>
        /// once that whole method returns, so it is always null at that point and a lookup keyed off it
        /// would silently never resolve anything on the pass that matters most. Walking from the box
        /// actually being resolved works at every call site (pre-layout DOM construction, the target-page
        /// convergence loop, and any later re-resolution) with no ordering dependency.
        /// </param>
        /// <param name="elementId">the id to find the box by</param>
        internal CssBox? GetBoxById(CssBox fromBox, string elementId)
        {
            var actualRoot = fromBox;
            while (actualRoot.ParentBox != null)
            {
                actualRoot = actualRoot.ParentBox;
            }

            if (!ReferenceEquals(_idIndexRoot, actualRoot))
            {
                _idIndex = DomUtils.BuildIdIndex(actualRoot);
                _idIndexRoot = actualRoot;
            }

            return _idIndex != null && _idIndex.TryGetValue(elementId, out var box) ? box : null;
        }

        /// <summary>
        /// The fragmentainer-slot -&gt; materialized-page map <see cref="CssContentEngine.AppendTargetCounter"/>
        /// resolves <c>target-counter(_, page)</c> against, built fresh each iteration of
        /// <see cref="PerformLayoutOnePass"/>'s target-page convergence loop from that iteration's own
        /// (still provisional) fragment tree. Null before the loop's first iteration has run at all - a
        /// box resolving <c>target-counter(_, page)</c> against a null map emits the same placeholder
        /// <c>counter(page)</c> already silently produces outside margin boxes today (see
        /// <see cref="Dom.CssBox.HasPendingTargetPageContent"/>), and the loop revisits it once a real map
        /// exists.
        /// </summary>
        internal (IReadOnlyDictionary<int, int> SlotToPage, int MaxMappedSlot, int FallbackPageCount)? TargetPageMap { get; private set; }

        /// <summary>
        /// The page a <c>position: running()</c> element is currently being laid out for, set
        /// only for the duration of one <see cref="Dom.RunningElementLayout.LayoutRunningElementFor"/>
        /// call from <see cref="LayoutMarginBoxes"/>. <c>counter(page)</c>/<c>counter(pages)</c> inside a
        /// running element resolve against this rather than through <c>CssCounterEngine</c>, which knows
        /// nothing about pagination and answers 1 for both.
        /// </summary>
        /// <remarks>
        /// The margin box's own <c>content: counter(page)</c> has always paginated - <see cref="Dom.MarginBoxRenderer.ResolveContent"/>
        /// is handed the page number directly. A running element bypasses that path entirely
        /// (<c>content: element(name)</c> short-circuits before it), so its descendants kept whatever
        /// <c>ApplyContent</c> resolved at DOM-construction time and every page read "Page 1 of 1".
        /// Because the running element is genuinely re-laid-out per page, refreshing its counter text
        /// first is all that is needed - and it must be refreshed before layout, since "Page 9 of 14" is
        /// wider than "Page 1 of 1" and the difference changes line breaking inside the band.
        /// </remarks>
        internal (int Page, int Pages)? RunningElementPageContext { get; set; }

        /// <summary>
        /// Measures the bounds of box and children, recursively.
        /// </summary>
        /// <param name="g">Device context to draw</param>
        /// <summary>
        /// Lays out the document, then - if it declares any <c>@container</c> size query container -
        /// runs a bounded convergence loop on top: <see cref="PerformLayoutOnePass"/>'s first invocation
        /// is the correct bootstrap (no container size data yet, so every <c>@container</c> condition
        /// evaluates false, exactly today's pre-fix behavior), after which every size container's
        /// resolved content-box size is read off the finished tree and, if it differs from the previous
        /// pass's (or this is the first refinement pass), a full re-parse + re-cascade + re-layout is run
        /// against the new sizes - the same "wholesale redo" <see cref="PdfGenerator.SetContent"/>'s
        /// ShrinkToFit path already trusts, just re-triggered per container size rather than once per
        /// global scale factor. Capped at 3 refinement passes (4 total, matching
        /// <see cref="UseVariableInlineMeasure"/>'s own established 3-iteration cap): on cap exceeded, the
        /// last pass's result is accepted silently, the same bounded-not-perfect stance already used
        /// there for a structurally identical layout-depends-on-layout problem. A document with no size
        /// container anywhere - the overwhelming majority - pays for exactly one extra tree walk
        /// (<see cref="HasSizeContainers"/>) beyond what layout already cost before this feature existed.
        /// </summary>
        public async ValueTask PerformLayout(RGraphics g)
        {
            ArgumentNullException.ThrowIfNull(g);

            await PerformLayoutOnePass(g);

            if (!HasSizeContainers) return;

            var previous = BuildContainerQuerySizes();
            const int maxRefinementPasses = 3;
            for (var pass = 0; pass < maxRefinementPasses; pass++)
            {
                if (_lastHtmlSource is null) break; // SetHtml was never actually called with real content

                await SetHtml(_lastHtmlSource, _lastBaseCssData, previous);
                await PerformLayoutOnePass(g);

                var current = BuildContainerQuerySizes();
                if (previous.SizesEqual(current)) break;
                previous = current;
            }
        }

        /// <summary>Builds the container-size-by-Id map <see cref="PerformLayout"/>'s convergence loop
        /// compares pass-over-pass and feeds into the next pass's cascade - see
        /// <see cref="ContainerQuerySizes"/>'s own remarks on why <see cref="Dom.CssBox.Id"/> is a safe
        /// cross-pass key.</summary>
        private ContainerQuerySizes BuildContainerQuerySizes()
        {
            var byId = new Dictionary<uint, ContainerQueryContext>();
            var pixelsPerPoint = (Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;

            void Walk(CssBox box)
            {
                if (box.ContainerType.Value is CSS.ContainerType.Size or CSS.ContainerType.InlineSize)
                {
                    var isSize = box.ContainerType.Value == CSS.ContainerType.Size;
                    var widthPt = box.ClientRight - box.ClientLeft;
                    var heightPt = box.ClientBottom - box.ClientTop;

                    // inline-size/block-size (CSS Writing Modes 4 §7.1) rotate onto the orthogonal
                    // physical axis under a vertical-rl/vertical-lr container - width/height/aspect-ratio/
                    // orientation stay physical regardless (CSS Containment 3 §7.3).
                    var isVertical = box.WritingMode.Value is CSS.WritingMode.VerticalRl or CSS.WritingMode.VerticalLr;

                    byId[box.Id] = new ContainerQueryContext(
                        WidthPt: widthPt,
                        HeightPt: isSize ? heightPt : null,
                        InlineSizePt: isVertical ? heightPt : widthPt,
                        BlockSizePt: isSize ? (isVertical ? widthPt : heightPt) : null,
                        ContainerName: box.ContainerName,
                        PixelsPerPoint: pixelsPerPoint);
                }

                foreach (var child in box.Boxes)
                    Walk(child);
            }

            if (Root is not null) Walk(Root);
            return new ContainerQuerySizes(byId);
        }

        /// <summary>
        /// The initial containing block's own width to seed <see cref="Root"/>'s <see cref="CssBox.Size"/>
        /// with for a layout pass, given <paramref name="fallback"/> — the historical, page-unaware value
        /// the caller would otherwise use. Per css-page-3 §3, "the edges of the page area on the first
        /// page establish... the initial containing block" - the width analogue of
        /// <see cref="Dom.CssLayoutEngine.GetBoxHeight"/>'s existing pin to
        /// <c>PageGeometry.GetPage(0).BandHeight</c> (issue #201). Only substitutes <paramref name="fallback"/>
        /// when <see cref="UseVariableInlineMeasure"/> is on AND <see cref="MaxSize"/>'s width isn't itself
        /// an explicit constraint distinct from the page area — a caller-imposed <see cref="MaxSize"/>
        /// still wins over page geometry, the same precedence it already has everywhere else it's read.
        /// <see cref="PdfGenerator"/>'s own normal per-page rendering path sets <see cref="MaxSize"/>'s
        /// width to exactly <see cref="PageSize"/>'s width, which this recognizes as "no constraint beyond
        /// the page itself" rather than a real external override.
        /// </summary>
        private double IcbWidthSeed(double fallback) =>
            UseVariableInlineMeasure && (MaxSize.Width <= 0 || Math.Abs(MaxSize.Width - PageSize.Width) < 0.01)
                ? PageGeometry.GetPage(0).BandWidth
                : fallback;

        private async ValueTask PerformLayoutOnePass(RGraphics g)
        {
            ActualSize = RSize.Empty;

            // Which boxes clip is a fact about one layout: a box widened since the last one may fit under
            // its cap now, and must be allowed to break again.
            ScrollContainersThatClip.Clear();
            _aScrollContainerStartedClipping = false;

            FloatScanCalls = 0;
            FloatScanBoxVisits = 0;
            BuildDraftCalls = 0;
            if (Root is null) return;

            // A document with per-page left/right margins settles its box->page assignment in the reflow
            // loop below, and until that loop has run, a box's page is provisional.
            _pageWidthsSettled = false;

            // includeStackingHoistCandidates: false here - the stacking-context predicate is only settled
            // once layout has run. It used to read IsTransformed, which lazily computes and permanently
            // caches ActualTransformMatrix against the box's own border-box size on first access (before
            // layout every box's size is still unset, so that would cache a wrong matrix forever - see the
            // "actualTransformComputed" cache in DerivedStyle.ActualTransformMatrix). It reads the computed
            // value (HasTransform) now, but the flag is only consumed after layout, so it stays deferred.
            (HasFloatedBoxes, HasOutOfFlowBoxes, _) = ComputeFlowFlags(Root, includeStackingHoistCandidates: false);

            // Depends on cascaded style only, and layout itself consults it, so it has to be settled before
            // the first pass rather than alongside the post-layout flags below.
            HasCloneDecorations = DomUtils.AnyBoxClonesDecorations(Root);

            // Also cascade-only, and cheap enough to recompute every pass (a document declaring a size
            // container is rare, and PerformLayout's own convergence loop already needs this exact
            // answer to decide whether to run at all).
            HasSizeContainers = DomUtils.AnyBoxEstablishesSizeContainer(Root);

            // Also purely cascade-driven, and also needed *during* the passes now that fragments are emitted
            // as each one ends: CSS Paged Media 3 §3.2's content-empty-page rule reads
            // SuppressOwnBackgroundPaint, and resolving the canvas background afterwards would make the
            // promoted root background look like real content spanning the whole document. (Acid2 grew 2->3
            // pages the last time the ordering of these two was wrong.)
            ResolveCanvasBackground();

            // Layout only reaches (and so only loads the images of) boxes in the tree, and a boxless
            // <body>'s background-image is painted on the canvas all the same.
            if (CanvasBackgroundBox is { IsDisplayContentsShell: true } canvasShell)
                await canvasShell.EnsureAuxiliaryImagesLoadedAsync();

            // if width is not restricted we set it to large value to get the actual later
            Root.Size = new RSize(IcbWidthSeed(MaxSize.Width > 0 ? MaxSize.Width : PageSize.Width), 0);
            Root.Location = Location;

            await LayoutDocument(g);

            if (MaxSize.Width <= 0.1)
            {
                // in case the width is not restricted we need to double layout, first will find the width so second can layout by it (center alignment)
                Root.Size = new RSize((int)Math.Ceiling(ActualSize.Width), 0);
                ActualSize = RSize.Empty;
                await LayoutDocument(g);
            }

            // Per-page horizontal reflow (CSS Paged Media 3: "the edges of the page area act as a
            // containing block for layout that occurs between page breaks"). The pass(es) above laid
            // every box out against page 0's own measure - a box's width is resolved (CssBox.PerformLayoutImp
            // via CssLayoutEngine.GetBoxWidth) BEFORE its Location is assigned, so on the first pass no
            // box's own page was yet known. RunLayoutToSettledMeasure re-runs layout so each auto-width
            // box now keys its width off its own page via its previous-pass Location.Y (GetBoxWidth ->
            // PageContentRightOf), repeating until the box->page assignment stops changing - see that
            // method's own remarks for the full reasoning (including why the cap stays at 3) and why
            // TryApplyDimensionChangingPageCorrection's own speculative/fallback passes call it too
            // rather than a bespoke single LayoutDocument call. Gated the same way it always was
            // (UseVariableInlineMeasure false skips this ENTIRELY, unlike the correction passes' own
            // unconditional calls below): a document with no per-page horizontal override has nothing
            // for this to settle, and re-running LayoutDocument again anyway is observably not a no-op
            // for some documents (e.g. named-page break suppression, repeated-header state) even when
            // its own result would otherwise be discarded - see RunLayoutToSettledMeasure's own remarks.
            if (UseVariableInlineMeasure)
            {
                var reflowRootWidth = IcbWidthSeed(MaxSize.Width > 0 ? MaxSize.Width : Math.Ceiling(ActualSize.Width));
                await RunLayoutToSettledMeasure(g, reflowRootWidth);
            }

            // css-gcpm-3's float: footnote needs each page's footnote-area height fed back into that same
            // page's own available content band before the flow content there is judged to fit - the same
            // "layout depends on layout" shape as UseVariableInlineMeasure's reflow loop above, solved the same
            // way: resolve every page's footnotes against the geometry the layout above already produced
            // (ResolveFootnotesForThisAttempt), re-run layout with the newly-discovered reservation seeded
            // in (see LayoutDocument's own ReserveBandEnd seed), and repeat until a round's per-page totals
            // stop changing. Runs before the target-counter(_, page)/ReapplyPseudoElementContent work below
            // so those resolve against footnotes' own, already-settled page breaks rather than the other
            // way around - and the target-counter loop resolves footnotes again after each of its own
            // reflows, since a resolved page number can move a footnote call across a page break
            // (issue #757). Only entered for documents that actually use float: footnote or a page float -
            // HasFootnotes/HasPageFloats are plain list-count checks, not tree walks, so this costs
            // nothing for the common case.
            //
            // Page floats (css-page-floats' float: top/bottom/top-bottom/snap) share this exact loop
            // rather than getting one of their own: a page float and a footnote can legitimately land on
            // the same page's bottom edge, and BottomFloatAreaHeightsBySlot/FootnoteAreaHeightsBySlot are
            // composed into one ReserveBandEnd call in LayoutDocument, so both must be resolved against
            // the same settled layout before either seeds the next attempt.
            if (HasFootnotes || HasPageFloats)
            {
                var footnoteRootWidth = IcbWidthSeed(MaxSize.Width > 0 ? MaxSize.Width : Math.Ceiling(ActualSize.Width));
                const int maxFootnotePasses = 6;

                BeginFootnoteConvergence();

                for (var pass = 0; pass < maxFootnotePasses; pass++)
                {
                    FootnoteResolvePasses = pass + 1;
                    // Gated independently, not just by the outer HasFootnotes || HasPageFloats: a document
                    // using only one of the two features must not pay the other resolver's own dictionary
                    // allocations and bookkeeping on every one of up to 6 passes.
                    var footnotesChanged = HasFootnotes && await ResolveFootnotesForThisAttempt(g);
                    var pageFloatsChanged = HasPageFloats && ResolvePageFloatsForThisAttempt();
                    var changed = footnotesChanged || pageFloatsChanged;

                    // The bound below is a last resort, not the ordinary exit - resolving must always be
                    // the last thing this loop does before FootnoteAreaHeightsBySlot/_footnoteAreasBySlot/
                    // TopFloatAreaHeightsBySlot/BottomFloatAreaHeightsBySlot/PageFloatPlacements are read,
                    // or they describe a Root tree an earlier LayoutDocument call already left behind:
                    // reaching the cap with changed still true and relaying out one further time,
                    // unresolved, is exactly what would do that.
                    FootnoteLoopSettled = !changed;

                    if (!changed || pass == maxFootnotePasses - 1) break;

                    Root.Size = new RSize(footnoteRootWidth, 0);
                    Root.Location = Location;
                    ActualSize = RSize.Empty;
                    await LayoutDocument(g);
                }
            }

            // After layout, re-apply content to pseudo-elements now that named strings are set
            ReapplyPseudoElementContent(Root);

            // css-content-3's target-counter(target, page) needs the final page a target box landed on,
            // which only exists once pagination has run - but the resolved page-number text's own width
            // can change line-breaking, which can change pagination. Same "layout depends on layout"
            // shape as UseVariableInlineMeasure's reflow loop above and PerformLayout's own @container
            // refinement loop, solved the same way: materialize a provisional fragment tree just far
            // enough to build a slot->page map, re-resolve every target-counter(_, page) against it, and
            // repeat (capped at the same 3 iterations those two loops already established) until a round
            // resolves to the same text the previous round did. Only entered for documents that actually
            // use target-counter(_, page) - AnyBoxHasTargetPageContent is the same cascade-only precheck
            // cost class as AnyBoxClonesDecorations/AnyBoxEstablishesSizeContainer above.
            if (DomUtils.AnyBoxHasTargetPageContent(Root))
            {
                var targetPageRootWidth = IcbWidthSeed(MaxSize.Width > 0 ? MaxSize.Width : Math.Ceiling(ActualSize.Width));

                // Seeded from the state already on hand (the placeholder "1" text every target-counter(_,
                // page) box currently holds), mirroring UseVariableInlineMeasure's own reflow loop above -
                // this only lets the loop exit after a single round in the edge case where a target
                // genuinely resolves to page 1 (placeholder happens to already be correct), but costs
                // nothing to seed correctly rather than from null.
                var previousSignature = TargetPageContentSignature(Root);

                // Footnote and page-float state has to describe the layout that is finally emitted
                // (issue #757), so it is resolved again after every reflow below, and the loop is not done
                // while that resolution still moves. The cap is the footnote loop's own when either feature is
                // in use: a resolve that changes what the next pass seeds needs the room to settle.
                var footnotesOrPageFloatsInUse = HasFootnotes || HasPageFloats;
                var maxTargetPagePasses = footnotesOrPageFloatsInUse ? 6 : 3;

                for (var pass = 0; pass < maxTargetPagePasses; pass++)
                {
                    // Speculative: LayoutDocument (below) replaces _emitter with a fresh one on its next
                    // call, so finishing this one early to read its tree and discarding the result costs
                    // nothing - only the reflow loop's own final Finish() (further down) is ever assigned
                    // to FragmentTree.
                    var provisionalTree = _emitter?.Finish() ?? new FragmentTree([]);
                    var (slotToPage, maxMappedSlot) = PageAnchorResolver.BuildSlotToPageMap(provisionalTree.Fragmentainers);
                    TargetPageMap = (slotToPage, maxMappedSlot, provisionalTree.Fragmentainers.Count);
                    ResolveTargetPageContent(Root);
                    var currentSignature = TargetPageContentSignature(Root);

                    // Always reflow after resolving, even when this round's text matches the previous
                    // round's (checked below): a leader()-bearing box's content is rebuilt via
                    // ParseToWordsWithLeaders on every resolve, which always creates fresh CssRectLeader
                    // instances (Width 0) regardless of whether the resolved text actually changed - only
                    // this pass's own LayoutDocument call (CssLayoutEngine.ApplyLeaderFill) gives them a
                    // real width. Skipping it whenever text happened to match would leave those particular
                    // boxes' leaders permanently zero-width.
                    Root.Size = new RSize(targetPageRootWidth, 0);
                    Root.Location = Location;
                    ActualSize = RSize.Empty;
                    await LayoutDocument(g);
                    ReapplyPseudoElementContent(Root);

                    // The reflow may have moved a footnote call or a page float across a page break (the
                    // resolved page number can change line-breaking), so the reservations and note areas
                    // the footnote loop above settled describe a layout that no longer exists. Resolving is
                    // the last thing each pass does, so what AttachFootnoteAreas reads is always the state of
                    // the layout just produced, including on the pass that ends the loop by the cap.
                    var footnoteStateChanged = false;

                    if (footnotesOrPageFloatsInUse)
                    {
                        var footnotesChanged = HasFootnotes && await ResolveFootnotesForThisAttempt(g);
                        var pageFloatsChanged = HasPageFloats && ResolvePageFloatsForThisAttempt();
                        footnoteStateChanged = footnotesChanged || pageFloatsChanged;
                    }

                    if (currentSignature == previousSignature && !footnoteStateChanged) break;
                    previousSignature = currentSignature;
                }
            }

            // Recompute after layout in case pseudo-element reapplication added any out-of-flow boxes.
            // Every box's size is final now, so it's safe to also compute HasStackingHoistCandidates.
            (HasFloatedBoxes, HasOutOfFlowBoxes, HasStackingHoistCandidates) =
                ComputeFlowFlags(Root, includeStackingHoistCandidates: true);

            // Only once the reflow loop has settled, since it reads the final document height.
            ReserveTrailingDirectionalBreak();

            // A trailing break-after reserves a page past everything layout reached, so it is the one slot
            // no pass could have stated.
            _emitter?.EmitReservedBlankSlots();

            // Layout's final phase: materialize the fragments the passes emitted into the immutable tree
            // everything downstream consumes. Only the last LayoutDocument invocation's emitter survives the
            // reflow loop, so the tree can never describe an intermediate invocation's geometry.
            var tree = _emitter?.Finish() ?? new FragmentTree([]);

            // Issue #1041's narrow, relayout-gated residual on top of PageGeometryTable.ResolveForMaterializedPage's
            // existing zero-relayout correction (LayoutMarginBoxes, below) - see
            // TryApplyDimensionChangingPageCorrection's own remarks for the gate and the fallback-safety
            // guarantee. A no-op (returns tree unchanged) for the overwhelming majority of documents.
            tree = await TryApplyDimensionChangingPageCorrection(g, tree);

            // css-gcpm-3's content: element() needs the final page list/count (first/start/last/first-except
            // selection is page-index-based), so it runs here, after the tree above is built, rather than
            // as part of the emitter's own per-pass work.
            RegisterDisplayContentsNamedStrings();

            var treeWithMarginBoxes = await LayoutMarginBoxes(g, tree);

            // Pure bookkeeping (the footnote convergence loop above already produced final geometry) - see
            // AttachFootnoteAreas's own remarks for why this runs here, alongside LayoutMarginBoxes.
            FragmentTree = AttachFootnoteAreas(treeWithMarginBoxes);
        }

        /// <summary>
        /// Issue #1041's narrow, relayout-gated residual left after
        /// <see cref="PageGeometryTable.ResolveForMaterializedPage"/>'s existing zero-relayout
        /// correction (<see cref="LayoutMarginBoxes"/>, which this always runs before): a page-side
        /// <c>:first</c>/<c>:left</c>/<c>:right</c> override that itself changes the slot's own content-box
        /// WIDTH or HEIGHT (not both), combined with a content-empty gap earlier in the document that
        /// shifted this slot's materialized page number off its raw grid number. Content already
        /// wrapped/fragmented against the grid-numbered dimensions during the layout pass(es) above, so
        /// simply substituting different dimensions into the fragment tree here (the way
        /// <c>ResolveForMaterializedPage</c> safely does when the dimensions AGREE) would size or clip
        /// content differently from what it actually is - unsafe without a genuine relayout.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Cheap in the overwhelming common case: the same override-flag short-circuit
        /// <see cref="LayoutMarginBoxes"/> already uses, then one probe per fragmentainer
        /// (<see cref="PageGeometryTable.ProbeDimensionChangingCorrection"/>) bounded by page count. Only
        /// pays for an actual extra <see cref="LayoutDocument"/> pass when at least one slot probes
        /// eligible.
        /// </para>
        /// <para>
        /// When one or more slots ARE eligible: pins those slots' rule selection to their materialized
        /// page number (<see cref="PageGeometryTable.MaterializedNumberOverrides"/>) and runs ONE more
        /// full layout pass, then verifies ALL of: the content-emptiness signature (the ordered list of
        /// <see cref="Fragments.FragmentainerFragment.SlotIndex"/> across <paramref name="tree"/> -
        /// which slots survived, and in what order) matches what it was before the pass;
        /// <see cref="PageAssignmentSignature"/> (every box's own page/name assignment) also matches; and
        /// every gated slot's <see cref="PageBandGeometry.BandWidth"/>/
        /// <see cref="PageBandGeometry.BandHeight"/> now actually equals what was
        /// predicted. Content-emptiness and page-assignment matching despite the corrected pass's
        /// different dimensions is exactly the evidence that the correction didn't itself perturb
        /// pagination elsewhere - the oscillation risk a dimension change genuinely carries (a page that
        /// got narrower could un-empty a slot, or vice versa).
        /// </para>
        /// <para>
        /// If ANY of that disagrees, the corrected pass is discarded entirely and layout is run a THIRD
        /// time with the override cleared - deterministically reproducing the original (declined) result,
        /// since layout is a pure function of its inputs and nothing else changed. A wrong "corrected"
        /// layout would be worse than the honest gap this leaves in place (see
        /// <c>.claude/accepted-gaps/left-right-page-geometry-vs-materialized-numbering.md</c>) - there is
        /// no partial application and no further retry: the cap is exactly one extra pass.
        /// </para>
        /// <para>
        /// Deliberately out of scope, gated out by <see cref="PageGeometryTable.ProbeDimensionChangingCorrection"/>
        /// itself: a slot where BOTH dimensions would change at once, and any slot under an active named
        /// page (<see cref="PageBandGeometry.ActiveName"/>) - a named-page run already
        /// drives its own bounded-not-guaranteed convergence loop
        /// (<c>.claude/accepted-gaps/named-page-run-convergence-loop-is-bounded-not-guaranteed.md</c>);
        /// stacking this mechanism on top of that one is out of scope for this issue.
        /// </para>
        /// <para>
        /// Two more gates live here rather than in the probe, because both are about the SPECULATIVE
        /// PASS'S validity rather than any one slot's own eligibility. First: a document using
        /// <c>float: footnote</c> is declined outright (<see cref="HasFootnotes"/>). A footnote body is
        /// wrapped by the separate footnote convergence loop above (<see cref="ResolveFootnotesForThisAttempt"/>)
        /// against the PRE-correction width, into state (<see cref="FootnoteAreaHeightsBySlot"/>/the
        /// per-slot call list) that lives outside <see cref="Root"/>'s own box tree - so neither the
        /// content-emptiness signature nor <see cref="PageAssignmentSignature"/> would ever notice a
        /// corrected pass leaving that state stale, and <see cref="AttachFootnoteAreas"/> (which runs
        /// after this method, against the POST-correction geometry) would then size a footnote-area
        /// divider for the new width around body text still wrapped for the old one. Re-resolving
        /// footnotes as part of the speculative pass would need genuinely more machinery than this
        /// narrow issue's own scope justifies, so the whole correction is declined instead whenever
        /// footnotes are in play at all - not just on the page(s) actually gated.
        /// </para>
        /// <para>
        /// Second: more than one slot probing eligible at once is declined entirely (no correction
        /// applied to ANY of them), not attempted one at a time. With exactly one gated slot, that
        /// slot's own <see cref="PageBandGeometry.Top"/> is unaffected by its own correction (a slot's
        /// Top depends only on EARLIER slots, none of which changed), so the probe's prediction is
        /// exactly what the real corrected pass will use. With two or more, an earlier gated slot's own
        /// HEIGHT change would shift every later slot's real <see cref="PageBandGeometry.Top"/> away
        /// from what an independently-run probe assumed for it - which could in principle select a
        /// different named page at a later slot whose <see cref="PageBandGeometry.BandWidth"/>/
        /// <see cref="PageBandGeometry.BandHeight"/> still happen to numerically match by coincidence,
        /// past both this method's own per-slot dimension check and <see cref="PageAssignmentSignature"/>'s
        /// page-index-and-name pairing. Restricting to exactly one eligible slot removes the scenario
        /// structurally rather than trying to detect it after the fact.
        /// </para>
        /// </remarks>
        private async ValueTask<FragmentTree> TryApplyDimensionChangingPageCorrection(RGraphics g, FragmentTree tree)
        {
            if (tree.Fragmentainers.Count == 0 || PageRules.Count == 0 || HasFootnotes)
                return tree;

            if (!PageGeometry.HasVerticalMarginOverrides && !PageGeometry.HasHorizontalMarginOverrides &&
                !PageGeometry.HasSizeOverrides && !PageGeometry.HasVerticalBorderPaddingOverrides &&
                !PageGeometry.HasHorizontalBorderPaddingOverrides)
                return tree;

            Dictionary<int, int>? overrides = null;
            Dictionary<int, PageBandGeometry>? expected = null;
            var probePageNumber = 0;

            foreach (var fragmentainer in tree.Fragmentainers)
            {
                probePageNumber++;
                var candidate = PageGeometry.ProbeDimensionChangingCorrection(fragmentainer.SlotIndex, probePageNumber);
                if (candidate is not { } geometry) continue;

                (overrides ??= [])[fragmentainer.SlotIndex] = probePageNumber;
                (expected ??= [])[fragmentainer.SlotIndex] = geometry;
            }

            // The common case: no slot both disagrees on materialized number AND changes dimension -
            // ResolveForMaterializedPage's own zero-relayout path (LayoutMarginBoxes) already covers
            // everything there is to cover. More than one eligible slot at once is ALSO declined (see
            // this method's own remarks) - not attempted for a subset of them.
            if (overrides is not { Count: 1 })
                return tree;

            var beforeContentEmptiness = tree.Fragmentainers.Select(f => f.SlotIndex).ToList();
            var beforePageAssignment = PageAssignmentSignature();
            var rootWidth = IcbWidthSeed(MaxSize.Width > 0 ? MaxSize.Width : Math.Ceiling(ActualSize.Width));

            FragmentTree candidateTree;
            PageGeometry.MaterializedNumberOverrides = overrides;
            try
            {
                candidateTree = await RunLayoutPassForPageCorrection(g, rootWidth);
            }
            finally
            {
                // Cleared unconditionally - every pass after this one, including the fallback restore
                // pass below, must resolve every slot against its plain grid number exactly as before
                // this feature existed.
                PageGeometry.MaterializedNumberOverrides = null;
            }

            var afterContentEmptiness = candidateTree.Fragmentainers.Select(f => f.SlotIndex).ToList();
            var afterPageAssignment = PageAssignmentSignature();

            var converged = afterContentEmptiness.SequenceEqual(beforeContentEmptiness)
                && afterPageAssignment.SequenceEqual(beforePageAssignment);

            if (converged)
            {
                // Belt-and-suspenders: given the signature agreement just above, Compute's own purity,
                // and that only a gated slot's RULE SELECTION (never its Top, which only an earlier
                // slot's own height could move, and no earlier slot is ever overridden) differs between
                // this pass and the original, a gated slot's geometry canNOT actually disagree with
                // what was predicted once both signatures already match - but this is exactly the kind
                // of invariant a future change to either signature or to Compute could quietly break,
                // and this issue's own fallback-safety requirement (never bake in a partially-converged
                // result) is worth the extra check even though no known fixture reaches it.
                foreach (var (slotIndex, expectedGeometry) in expected!)
                {
                    var actual = PageGeometry.GetPage(slotIndex);
                    if (Math.Abs(actual.BandWidth - expectedGeometry.BandWidth) < 0.01 &&
                        Math.Abs(actual.BandHeight - expectedGeometry.BandHeight) < 0.01)
                        continue;

                    converged = false;
                    break;
                }
            }

            if (converged)
                return candidateTree;

            // Fallback-safety: discard the corrected pass and reproduce the original, declined result
            // exactly - one more full layout pass with the override cleared, deterministic because
            // nothing else about the document changed since the pass that produced the ORIGINAL tree.
            return await RunLayoutPassForPageCorrection(g, rootWidth);
        }

        /// <summary>
        /// The shared tail of one speculative layout pass for
        /// <see cref="TryApplyDimensionChangingPageCorrection"/> - mirrors the exact sequence
        /// <see cref="PerformLayoutOnePass"/> itself runs after its own last <see cref="LayoutDocument"/>
        /// call (settle the per-page measure, recompute flow flags, reserve a trailing directional
        /// break, emit reserved blank slots, materialize the tree) so a speculative pass run here is
        /// indistinguishable, to everything downstream, from an ordinary one. Uses
        /// <see cref="RunLayoutToSettledMeasure"/> rather than a single bare <see cref="LayoutDocument"/>
        /// call specifically so a speculative/fallback pass never bypasses <see cref="UseVariableInlineMeasure"/>'s
        /// own bounded convergence loop - see that method's own remarks.
        /// </summary>
        private async ValueTask<FragmentTree> RunLayoutPassForPageCorrection(RGraphics g, double rootWidth)
        {
            await RunLayoutToSettledMeasure(g, rootWidth);

            (HasFloatedBoxes, HasOutOfFlowBoxes, HasStackingHoistCandidates) =
                ComputeFlowFlags(Root!, includeStackingHoistCandidates: true);

            ReserveTrailingDirectionalBreak();
            _emitter?.EmitReservedBlankSlots();

            return _emitter?.Finish() ?? new FragmentTree([]);
        }

        /// <summary>
        /// Runs one authentic layout attempt to a settled per-page measure: the same
        /// <see cref="UseVariableInlineMeasure"/> bounded convergence loop (up to 3 iterations,
        /// comparing <see cref="PageAssignmentSignature"/> pass over pass, then one final settle pass
        /// with <see cref="_pageWidthsSettled"/> raised) when the document needs it, or a single plain
        /// <see cref="LayoutDocument"/> call when it does not (<see cref="UseVariableInlineMeasure"/>
        /// false - nothing here depends on a per-page measure at all).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="PerformLayoutOnePass"/>'s own call site guards this with the SAME
        /// <see cref="UseVariableInlineMeasure"/> check externally, exactly as the inline version of
        /// this logic always has - a document with no per-page horizontal override skips this whole
        /// method there, rather than reaching its own "single plain call" branch, because re-running
        /// <see cref="LayoutDocument"/> even once more is observably not a no-op for some documents
        /// (named-page break suppression, repeated-header state) despite nothing here needing it to
        /// happen. <see cref="TryApplyDimensionChangingPageCorrection"/>'s own speculative and
        /// fallback-restore passes call this UNCONDITIONALLY instead (they always need at least one
        /// fresh pass, to apply or clear <see cref="PageGeometryTable.MaterializedNumberOverrides"/>),
        /// which is what actually exercises the "single plain call" branch below.
        /// </para>
        /// <para>
        /// Sharing this one method between <see cref="PerformLayoutOnePass"/>'s own reflow and
        /// <see cref="TryApplyDimensionChangingPageCorrection"/>'s passes means neither of the latter
        /// two can silently diverge from what a genuine layout attempt does: a one-shot
        /// <see cref="LayoutDocument"/> call in their place could bake in an auto-width box's
        /// not-yet-converged width (the convergence loop exists precisely because one pass is not
        /// always enough), or leave <see cref="_pageWidthsSettled"/> raised without the genuine
        /// convergence <see cref="Dom.CssBox.OrphansAndWidowsMayMoveABreak"/> assumes it implies - both
        /// invisible to <see cref="TryApplyDimensionChangingPageCorrection"/>'s own content-emptiness/
        /// page-assignment/band-geometry checks, since none of them inspect a box's WIDTH directly or
        /// which pass set <c>_pageWidthsSettled</c>.
        /// </para>
        /// </remarks>
        private async ValueTask RunLayoutToSettledMeasure(RGraphics g, double rootWidth)
        {
            if (UseVariableInlineMeasure)
            {
                var previous = PageAssignmentSignature();
                for (var i = 0; i < 3; i++)
                {
                    Root!.Size = new RSize(rootWidth, 0);
                    Root.Location = Location;
                    ActualSize = RSize.Empty;
                    await LayoutDocument(g);

                    var current = PageAssignmentSignature();
                    if (current.SequenceEqual(previous)) break;
                    previous = current;
                }

                // css-break-3 §5.4's two line minimums are decided only once the assignment above has
                // settled, in the one final pass below - see PerformLayoutOnePass's own call site for
                // why (taking one while the loop is still settling would feed back into the very thing
                // it is trying to settle).
                _pageWidthsSettled = true;
            }

            Root!.Size = new RSize(rootWidth, 0);
            Root.Location = Location;
            ActualSize = RSize.Empty;
            await LayoutDocument(g);
        }

        /// <summary>
        /// Despite the name, this now does two things to every page, both only possible once the final
        /// materialized page sequence is known (i.e. here, on <paramref name="tree"/> as
        /// <see cref="FragmentEmitter.Finish"/> just produced it): (1) corrects each
        /// <see cref="Fragments.FragmentainerFragment.Geometry"/> against its materialized page number
        /// when a content-empty gap skipped earlier left it disagreeing with its raw grid slot number
        /// on <c>:first</c>/<c>:left</c>/<c>:right</c> parity (issue #148 - see
        /// <see cref="PageGeometryTable.ResolveForMaterializedPage"/>), baking the correction into the
        /// fragment tree so every downstream paint-time reader (<c>PdfGenerator</c>'s page loop,
        /// <c>PageAnchorResolver.ResolveRectToPage</c> for links/bookmarks, <c>HandleFormFields</c>)
        /// reads one already-correct value rather than each re-deriving it; and (2), only when the
        /// document actually declares running elements, lays out every page's
        /// <c>content: element(name[, keyword])</c> margin-box content for real (css-gcpm-3) and
        /// captures it into <paramref name="tree"/>, so paint stays fragment-driven for this content
        /// the same way it already is for everything else - see
        /// <see cref="Fragments.FragmentainerFragment.MarginBoxes"/>. Plain string/counter/<c>string()</c>/
        /// image margin-box content is untouched: it stays on <see cref="MarginBoxRenderer"/>'s existing,
        /// separate, fragment-tree-free pipeline, since there is no formatting/descendant-element fidelity
        /// to gain by moving already-plain-text content through real <see cref="CssBox"/> layout - it
        /// still reads the corrected <c>Geometry</c> from (1), just via <c>PdfGenerator</c> rather than
        /// through this method's own margin-box loop.
        /// </summary>
        private async ValueTask<FragmentTree> LayoutMarginBoxes(RGraphics g, FragmentTree tree)
        {
            // PageRules.Count == 0 is a safe skip for (1) too: with no @page rule at all, geometry
            // can never vary by page number, so there is nothing ResolveForMaterializedPage could ever
            // correct (see its own HasVerticalMarginOverrides/HasHorizontalMarginOverrides/
            // HasSizeOverrides guard).
            if (tree.Fragmentainers.Count == 0 || PageRules.Count == 0)
                return tree;

            var ppp = (Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
            var sheetSizePt = new XSize(PageSheetWidth / ppp, PageSheetHeight / ppp);
            var totalPages = tree.Fragmentainers.Count;
            var remPt = PageLengthContext?.RemPt ?? DefaultFontResolver.FontSize;
            var hasRunningElements = _runningElements.Count > 0;

            var updated = new List<FragmentainerFragment>(totalPages);
            var pageNumber = 0;

            foreach (var fragmentainer in tree.Fragmentainers)
            {
                pageNumber++;

                // Issue #148: a content-empty gap skipped earlier can leave this slot's raw grid
                // number and its materialized pageNumber disagreeing on :first/:left/:right parity.
                // Corrected here, once, for the whole fragment tree - the fragment tree stays the
                // single source of truth paint-time code (PdfGenerator, link/bookmark/form-field
                // resolution) reads Geometry off directly, rather than each re-deriving it.
                var geom = PageGeometry.ResolveForMaterializedPage(fragmentainer.SlotIndex, pageNumber)
                    ?? fragmentainer.Geometry;

                List<MarginBoxFragment>? marginBoxes = null;

                if (hasRunningElements)
                {
                    var pageY = geom.Top;
                    var activeName = PageRuleResolver.ActiveNameAtPageEnd(_namedPageElements, pageY, geom.BandHeight);
                    var applicableMargins = PageRuleResolver.SelectApplicableMarginRules(PageRules, pageNumber, activeName);
                    var applicablePageStyle = PageRuleResolver.SelectApplicablePageStyle(PageRules, pageNumber, activeName);

                    foreach (var marginRule in applicableMargins)
                    {
                        var boxName = marginRule.Selector?.Text?.Trim().ToLowerInvariant();
                        if (string.IsNullOrEmpty(boxName)) continue;

                        var contentValue = marginRule.Style.Content;
                        if (string.IsNullOrEmpty(contentValue)) continue;

                        if (!MarginBoxRenderer.TryParseElementFunction(contentValue, out var name, out var keyword))
                            continue;

                        var currentPageIndex = SlotStartingAt(pageY);
                        var runningBox = MarginBoxRenderer.ResolveRunningElement(
                            name, keyword, currentPageIndex, SlotStartingAt, _runningElements);
                        if (runningBox is null) continue;

                        var rectPt = MarginBoxRenderer.GetMarginBoxRect(
                            boxName, sheetSizePt, geom.MarginLeftPt, geom.MarginTopPt, geom.MarginRightPt, geom.MarginBottomPt,
                            applicableMargins, applicablePageStyle, remPt);
                        // The same margin/padding the text path applies (see
                        // MarginBoxRenderer.ApplyBoxModel). Without it a margin box holding element()
                        // ignores its own box model, which is how a footer band on a shallow page margin
                        // has no way to grow upward over the content the way a browser's overlay does.
                        rectPt = MarginBoxRenderer.ApplyBoxModel(rectPt, marginRule, applicablePageStyle, remPt,
                            MarginBoxRenderer.MarginAreaWidth(boxName, sheetSizePt, geom.MarginLeftPt, geom.MarginRightPt),
                            MarginBoxRenderer.MarginAreaHeight(boxName, sheetSizePt, geom.MarginTopPt, geom.MarginBottomPt));
                        if (rectPt.Width <= 0 || rectPt.Height <= 0) continue;

                        var pixelRect = new RRect(rectPt.X * ppp, rectPt.Y * ppp, rectPt.Width * ppp, rectPt.Height * ppp);

                        // Scoped to this one call so counter(page)/counter(pages) inside the
                        // running element resolve against the page it is being laid out for. Cleared in a
                        // finally so an exception mid-layout cannot leak a page number into the ordinary
                        // document-counter path.
                        RunningElementPageContext = (pageNumber, totalPages);
                        try
                        {
                            await RunningElementLayout.LayoutRunningElementFor(g, runningBox, pixelRect, this);
                        }
                        finally
                        {
                            RunningElementPageContext = null;
                        }

                        var content = MarginBoxContentFragmentBuilder.Build(runningBox);

                        (marginBoxes ??= []).Add(new MarginBoxFragment(boxName, content));
                    }
                }

                updated.Add(marginBoxes is null && geom.Equals(fragmentainer.Geometry)
                    ? fragmentainer
                    : fragmentainer with { Geometry = geom, MarginBoxes = marginBoxes ?? fragmentainer.MarginBoxes });
            }

            return tree with { Fragmentainers = updated };
        }

        /// <summary>
        /// Attaches each page's css-gcpm-3 <c>float: footnote</c> note area to the fragment tree, from
        /// bodies the footnote convergence loop in <see cref="PerformLayout"/> already laid out (see
        /// <see cref="ResolveFootnotesForThisAttempt"/>) - pure fragment-tree bookkeeping, no layout of its
        /// own, the same division of labor <see cref="LayoutMarginBoxes"/> has relative to
        /// <see cref="RunningElementLayout.LayoutRunningElementFor"/>. Called alongside
        /// <see cref="LayoutMarginBoxes"/> from <see cref="PerformLayout"/>, once the final page list is
        /// known.
        /// </summary>
        private FragmentTree AttachFootnoteAreas(FragmentTree tree)
        {
            if (_footnoteAreasBySlot.Count == 0) return tree;

            var updated = new List<FragmentainerFragment>(tree.Fragmentainers.Count);

            foreach (var fragmentainer in tree.Fragmentainers)
            {
                if (!_footnoteAreasBySlot.TryGetValue(fragmentainer.SlotIndex, out var areas) || areas.Count == 0)
                {
                    updated.Add(fragmentainer);
                    continue;
                }

                // Bodies were laid out (and are still positioned) in document space - the coordinate
                // space ReserveBandEnd/PageBottomOf's own reservation math needs, since it has to agree
                // with ordinary flow content's own document-space geometry. Painting, unlike layout, gets
                // a fresh XGraphics per page with its own page-local origin (matching how the emitter
                // makes every *other* fragment's rectangles fragmentainer-local - "local.Y = documentY -
                // (PageTopOf(k) - MarginTop)", see Fragment.cs's own remarks) - MarginBoxFragment content
                // never hits this because MarginBoxRenderer.GetMarginBoxRect computes a page-local rect
                // directly, with no document-space coordinate ever entering the picture. Translate once,
                // here, right before building the paint-facing fragments - this runs exactly once (unlike
                // ResolveFootnotesForThisAttempt, which the convergence loop can call several times), so a
                // permanent OffsetTop on each detached body is safe.
                //
                // That one-shot translation is also why the areas must PARTITION the slot's calls: a call
                // reachable through two of them would be offset twice and paint in the wrong place. See
                // PartitionFootnoteAreas.
                var localOriginY = fragmentainer.LocalOriginY;
                var fragments = new List<FootnoteAreaFragment>(areas.Count);

                foreach (var area in areas)
                {
                    if (area.TotalHeight <= 0 || area.Calls.Count == 0) continue;

                    foreach (var call in area.Calls)
                    {
                        call.Body.OffsetTop(-localOriginY);
                    }

                    // Every coordinate here was stated by the resolve pass rather than recomputed, so the
                    // divider can never be drawn at a different Y than the band that was reserved - it
                    // depends on the bodies' own natural height, which is not recoverable at this point.
                    var dividerRect = new RRect(
                        area.AreaLeft, area.DividerTop - localOriginY, area.AreaWidth, area.DividerThickness);

                    var bodies = area.Calls.Select(call => MarginBoxContentFragmentBuilder.Build(call.Body)).ToList();

                    fragments.Add(new FootnoteAreaFragment(
                        dividerRect, bodies, area.DividerColor,
                        area.Column is null ? FootnoteAreaScope.Page : FootnoteAreaScope.Column));
                }

                updated.Add(fragments.Count == 0
                    ? fragmentainer
                    : fragmentainer with { FootnoteAreas = fragments });
            }

            return tree with { Fragmentainers = updated };
        }

        /// <summary>
        /// Honors a directional <c>break-after</c> on the last box of the flow, per
        /// <see href="https://www.w3.org/TR/css-break-3/#break-between">css-break-3 §3.1</see>: pads the
        /// document with a blank page where one is needed for the page <i>after</i> it to fall on the
        /// requested side. The book idiom — a volume that must end so the next one opens recto.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This needs its own pass because a break between siblings is taken by the box that follows it,
        /// and at the end of the flow there is no such box. The rule is deliberately the same one: only
        /// the slot <i>stepped over</i> is reserved, never the slot the (non-existent) following content
        /// would occupy. So the document gains at most one page.
        /// </para>
        /// <para>
        /// The value is taken from the last in-flow descendant chain. Full §3.1 break-value propagation
        /// up out of a subtree is a separate matter; this is the narrow last-box case.
        /// </para>
        /// </remarks>
        private void ReserveTrailingDirectionalBreak()
        {
            if (Root is null || !HasRealPageGrid)
                return;

            var last = LastInFlowDescendant(Root);
            var side = PageSide.Any;

            for (var box = last; box is not null && side is PageSide.Any; box = box.ParentBox)
            {
                side = BreakValues.RequiredSide(null, box.BreakAfter.Value);
            }

            if (side is PageSide.Any)
                return;

            var lastSlot = SlotEndingAt(MarginTop + ActualSize.Height);

            if (!BreakValues.SlotIsOn(lastSlot + 1, side))
                _trailingBlankSlot = lastSlot + 1;
        }

        /// <summary>
        /// The deepest last in-flow box of <paramref name="box"/>'s subtree, or null when it has none.
        /// </summary>
        private static CssBox? LastInFlowDescendant(CssBox box)
        {
            CssBox? last = null;

            for (var i = box.Boxes.Count - 1; i >= 0; i--)
            {
                var child = box.Boxes[i];

                if (child.DerivedStyle.ActualDisplay == Keywords.None
                    || child.Position.Value is PositionMode.Absolute or PositionMode.Fixed or PositionMode.Running
                    || child.IsFloated || child.IsPageFloated)
                {
                    continue;
                }

                last = LastInFlowDescendant(child) ?? child;
                break;
            }

            return last;
        }

        /// <summary>
        /// Lays the document out once from the root size and location the caller set, and again for each
        /// scroll container found clipping its content (<see cref="NoteScrollContainerClips"/>).
        /// </summary>
        /// <remarks>
        /// A scroll container capped only by <c>max-height</c> breaks like a plain block (css-break-3 §2),
        /// unless its content turns out to overflow the cap, which only a layout can tell. Such a box is kept in
        /// one piece for the rest of the layout and the document laid out again; within a layout a box only
        /// ever joins the set, so this settles. It is done here rather than once in <see cref="PerformLayout"/>
        /// because every caller lays out at its own geometry: a per-page width reflow, the footnote and page
        /// float loop or a <c>target-counter</c> reflow can make a box clip that the first layout did not.
        /// </remarks>
        /// <param name="g">the graphics to measure with</param>
        private async ValueTask LayoutDocument(RGraphics g)
        {
            var rootSize = Root!.Size;
            var rootLocation = Root.Location;

            await LayoutDocumentOnce(g);

            try
            {
                for (var attempt = 0; attempt < MaxClippingRelayouts && _aScrollContainerStartedClipping; attempt++)
                {
                    _aScrollContainerStartedClipping = false;
                    _scrollContainerClipsFrozen = attempt == MaxClippingRelayouts - 1;
                    Root.Size = rootSize;
                    Root.Location = rootLocation;
                    ActualSize = RSize.Empty;
                    await LayoutDocumentOnce(g);
                }
            }
            finally
            {
                _scrollContainerClipsFrozen = false;
                _aScrollContainerStartedClipping = false;
            }
        }

        /// <summary>
        /// Lays the document out once, filling one fragmentainer at a time: a pass targets a
        /// fragmentainer, and where content does not fit it records where it stopped so the next pass
        /// can resume from exactly that point
        /// (<see href="https://www.w3.org/TR/css-break-3/#breaking-controls">CSS Fragmentation Level 3
        /// §2/§4.4</see>).
        /// </summary>
        /// <remarks>
        /// One attempt of <see cref="LayoutDocument"/>, the atom the three re-layout loops in
        /// <see cref="PerformLayout"/> and <c>PdfGenerator</c>'s <c>ShrinkToFit</c> pass all share. The named-page registry and page
        /// geometry table are reset here, once per invocation and never per fragmentainer — a
        /// document's registrations accumulate <i>across</i> its fragmentainers, and only a whole new
        /// layout invalidates them.
        /// </remarks>
        /// <param name="g">the graphics to measure with</param>
        private async ValueTask LayoutDocumentOnce(RGraphics g)
        {
            LayoutGeneration++;
            FragmentainerPasses = 0;
            LastResortRelayouts = 0;
            PassRewinds = 0;
            CursorSpills = 0;

            // Registrations append (they aren't idempotent), so without this each re-layout accumulated
            // duplicates and began with a stale ActivePageName from the PREVIOUS invocation's last
            // element, which could spuriously force or suppress the first named-page break. Blank-slot
            // reservations go the same way: each invocation re-decides every one of them from scratch,
            // and only the last invocation's set describes the layout the fragment tree is built from.
            ClearNamedPageElements();
            PageGeometry.Reset();
            ClearBlankSlotReservations();

            // One footnote-policy: line break per call per LayoutDocument invocation - without this, a
            // call forced off its landing page keeps re-triggering as this same pass's own fragmentainer
            // walk resumes it onto each successive page in turn (FootnotePolicyForcedLineCalls itself
            // isn't cleared until the next ResolveFootnotesForThisAttempt, since it must survive resuming
            // onto the NEXT page within this call), forcing it one further page every time - not bounded
            // by PerformLayout's own footnote-convergence pass cap at all, since it never leaves this one
            // LayoutDocument call.
            FootnotePolicyLineBreaksTakenThisPass.Clear();

            // Both per layout, not per document: ShrinkToFit and the per-page reflow loop each re-run this
            // method, and a record kept across them would describe passes that no longer exist while the
            // latch silently disabled the correction on every layout after the first (#320). Every box's
            // own _placedByPass stamp is guarded by a LayoutGeneration check of its own
            // (CssBox.PlacedByPassIfStillValid), so nothing here has to walk the tree resetting those -
            // clearing _passInvalidations is still needed so it does not grow without bound across a
            // document's ShrinkToFit/reflow iterations.
            _passEntries.Clear();
            _passEntrySet.Clear();
            _passesRewoundFor.Clear();
            _passInvalidations.Clear();

            // Each invocation re-decides the whole document, so the fragments an earlier one emitted
            // describe a layout that no longer exists.
            var emitter = new FragmentEmitter(this);
            _emitter = emitter;

            BreakToken? token = null;

            try
            {
                var slot = 0;
                var maxPasses = MaxFragmentainersOverride ?? MaxFragmentainers;

                // Set before every `break` below, so the code after the loop can tell "the loop reached a
                // real conclusion" apart from "the loop ran out of passes" - a bare `break` only exits the
                // loop, it does not skip the statements that follow it.
                var concluded = false;

                for (var pass = 0; pass < maxPasses; pass++)
                {
                    var context = new FragmentainerContext(this, Root!, slot);
                    CurrentFragmentainer = context;

                    // Seeded from the previous attempt's own resolution (ResolveFootnotesForThisAttempt/
                    // ResolvePageFloatsForThisAttempt) - a footnote's or page float's presence on this slot
                    // is discovered only once its natural Location has already settled, so the very first
                    // attempt always seeds zero here; the convergence loop in PerformLayout re-enters
                    // LayoutDocument with the newly-discovered amount until it stops changing. No
                    // RestoreBandEnd/RestoreBandStart: this context is fresh, discarded when the pass ends,
                    // so there is nothing to unwind.
                    var bandEndReservation = TotalBandEndReservationFor(slot);
                    if (bandEndReservation > 0)
                    {
                        context.ReserveBandEnd(slot, bandEndReservation);
                    }

                    if (TopFloatAreaHeightsBySlot.TryGetValue(slot, out var topFloatAreaHeight) && topFloatAreaHeight > 0)
                    {
                        context.ReserveBandStart(slot, topFloatAreaHeight);
                    }

                    // A translation, refill, or rectangle reset since the last iteration may have reopened an
                    // already-frozen slot behind this one without moving the driver back to it (see
                    // FragmentEmitter.CatchUpStaleSlotsBehind's own remarks) - heal it now, one pass before it
                    // would otherwise wait for Finish()'s far more expensive replay.
                    _emitter?.CatchUpStaleSlotsBehind(slot);

                    // What this pass was entered with, so a decision discovered on a later one can send the
                    // driver back to it. Recorded before the pass runs, since that is the state re-entering
                    // it needs and nothing the pass produces can be part of it.
                    RecordPassEntry(slot, token);

                    Root!.ResumeAt(token, resumeTopOverride: null);
                    await Root.PerformLayout(g);
                    FragmentainerPasses++;

                    // §5.4's widows is the one decision that reaches backwards: the pass that has just
                    // ended kept lines a later fragment needs, and it has to be run again with fewer.
                    // Nothing it produced is kept, so this is done before the pass is frozen.
                    if (_widowsRewind is { } rewind && TryRewindForWidows(rewind, ref token))
                    {
                        _widowsRewind = null;
                        // A footnote-policy: line forced break this now-discarded pass took is exactly
                        // the kind of thing "nothing it produced is kept" (the comment above) refers to -
                        // the replayed pass needs its own fresh one-shot budget, or a call whose word
                        // happens to be re-placed during the replay (forced or not, for reasons entirely
                        // unrelated to it - any widows violation anywhere on the page triggers this) would
                        // silently see its footnote-policy: line request as already spent and place
                        // normally instead.
                        FootnotePolicyLineBreaksTakenThisPass.Clear();
                        continue;
                    }

                    _widowsRewind = null;

                    // §4.3's keep-with-next run pull reaches back further than one pass: the run head was
                    // placed in a fragmentainer the driver has already left, so the pass that placed it is
                    // re-entered rather than this one.
                    if (_runPullRewind is { } pull && TryRewindForRunPull(pull, ref token, ref slot))
                    {
                        _runPullRewind = null;
                        // Same reasoning as the widows rewind above.
                        FootnotePolicyLineBreaksTakenThisPass.Clear();
                        continue;
                    }

                    _runPullRewind = null;

                    var next = context.OutgoingToken;

                    if (next is null)
                    {
                        emitter.EmitPass(slot, LastSlotAnyGeometryTouches(context.SlotIndex), token, null);
                        concluded = true;
                        break;
                    }

                    if (next == token || HasAlreadyBeenEntered(next))
                    {
                        // The run has arrived somewhere it has already been — at the record this very
                        // pass was handed (a one-pass cycle), or at one an earlier pass was entered with
                        // (a longer one). Either way every pass from here reproduces the ones between,
                        // so re-entering would resume at the same points forever. Lay the remainder out
                        // monolithically instead: an overflowing fragmentainer is a far better outcome
                        // than dropped content, and it is what css-break-3 §4.3's own last-resort
                        // relaxation amounts to.
                        //
                        // Deliberately not reported: the only channel there is builds an exception
                        // (RenderError), and this recovery used to sit after a call that threw one -
                        // which is why every statement of it was dead code, and why any input reaching
                        // here failed the whole render instead of producing a document with one bad
                        // page. LastResortRelayouts is what says it happened.
                        await LayoutTheRemainderMonolithically(g, emitter, token!, slot);
                        concluded = true;
                        break;
                    }

                    // The gap between the slot this pass started filling and the one the next resumes in.
                    // It is not always empty: a monolithic subtree is laid out in a single pass and can
                    // cover bands past the one it started in, and a box placed far down the document means
                    // the next pass's slot is not this one plus one.
                    //
                    // How far this pass reached is the context's own cursor to answer, not something to
                    // re-derive from the record it stopped with: a forced break steps a pass over slots
                    // without ending it (FragmentainerContext.StepOverTo), so the pass can have placed
                    // content well past the slot it opened with.
                    emitter.EmitPass(slot, Math.Max(context.SlotIndex, next.ResumeSlotIndex - 1), token, next);

                    token = next;
                    slot = next.ResumeSlotIndex;
                }

                // Running out of passes without a detected cycle is a no-progress condition too: a
                // genuine walk that keeps producing a (slot, token) pair it has never been entered with
                // before never trips HasAlreadyBeenEntered, so it would otherwise fall out of the loop
                // silently, with whatever the last pass produced never emitted. Route it through the
                // same last-resort recovery a detected cycle uses rather than dropping it. Guarded on
                // `concluded` because a `break` above only exits the loop - it does not skip this
                // statement, which sits after the loop rather than inside it.
                if (!concluded && token is not null)
                    await LayoutTheRemainderMonolithically(g, emitter, token, slot);
            }
            finally
            {
                CurrentFragmentainer = null;
            }
        }

        /// <summary>
        /// The last rung of <see cref="BreakRelaxation"/>'s ladder: lays everything
        /// <paramref name="token"/> still names out in one go, in fragmentainer
        /// <paramref name="slot"/>, without allowing another break
        /// (<see href="https://www.w3.org/TR/css-break-3/#possible-breaks">css-break-3 §4.3</see>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reached only when a pass has reproduced the record it was handed, which means re-entering it
        /// would resume at the same point for ever. Every tier above this one is allowed to refuse
        /// precisely because this one cannot: the fragmentainer it fills overflows, and a page whose
        /// content runs off it is a far better answer than a page that never gets written — which is
        /// what a caller got while this recovery sat behind a call that always threw.
        /// </para>
        /// <para>
        /// The context it runs under <b>exists but does not fragment</b>. That is not the same absence a
        /// measurement pass has (<see cref="DetachFragmentainer"/>): this is a real pass filling a real
        /// slot the emitter reads, and the only thing it must not do is break again — which is exactly
        /// what <c>suppressed</c> says.
        /// </para>
        /// <para>
        /// It re-emits a slot an earlier pass already froze, which
        /// <see cref="FragmentEmitter.EmitPass"/> supports by design — the later emission is the one that
        /// describes the layout being kept. What it does not reach is a slot <i>below</i>
        /// <paramref name="slot"/>: a box this pass places back above the fragmentainer being filled is
        /// rescued only if it already had a fragment there, in which case repositioning it un-freezes that
        /// band through <see cref="InvalidateEmittedFragmentsFor"/>.
        /// </para>
        /// <para>
        /// It records no <c>_passEntries</c> entry and clears neither rewind request, unlike every other
        /// exit from the driver loop. Nothing can reach it: <c>widows</c> and the keep-with-next run pull
        /// are both gated on breaking being live, and this pass has it suppressed. That is a property of
        /// <i>their</i> guards rather than of this method, which is why it is written down.
        /// </para>
        /// </remarks>
        private async ValueTask LayoutTheRemainderMonolithically(
            RGraphics g, FragmentEmitter emitter, BreakToken token, int slot)
        {
            LastResortRelayouts++;

            var relaxed = new FragmentainerContext(this, Root!, slot, suppressed: true);
            CurrentFragmentainer = relaxed;

            // A fresh context starts with no reservation of its own - without this, content laid out by
            // this last-resort fallback would be unaware of a footnote/page-float area reserved for this
            // same slot (see LayoutDocument's own identical seed) and could overlap it.
            var relaxedBandEndReservation = TotalBandEndReservationFor(slot);
            if (relaxedBandEndReservation > 0)
            {
                relaxed.ReserveBandEnd(slot, relaxedBandEndReservation);
            }

            if (TopFloatAreaHeightsBySlot.TryGetValue(slot, out var relaxedTopFloatAreaHeight) && relaxedTopFloatAreaHeight > 0)
            {
                relaxed.ReserveBandStart(slot, relaxedTopFloatAreaHeight);
            }

            Root!.ResumeAt(token, resumeTopOverride: null);
            await Root.PerformLayout(g);
            FragmentainerPasses++;

            // No outgoing record: this pass takes no break, so everything it produced from this
            // fragmentainer on belongs to the bands it reaches, cut at each boundary as monolithic
            // content already is. How far it reached is the context's own cursor to answer - a forced
            // break steps a pass over slots without ending it (FragmentainerContext.StepOverTo).
            emitter.EmitPass(slot, LastSlotAnyGeometryTouches(relaxed.SlotIndex), token, null);
        }

        /// <summary>
        /// A box's request to re-run the pass that produced its previous fragment, keeping at most
        /// <c>Budget</c> line boxes there so its last fragment reaches its <c>widows</c> minimum.
        /// </summary>
        private (CssBox Box, int Budget)? _widowsRewind;

        /// <summary>
        /// What each fragmentainer pass was entered with, indexed by pass. The one thing re-entering an
        /// earlier pass needs and cannot re-derive: which slot it was filling and which resumption record
        /// it picked up from.
        /// </summary>
        private readonly List<(int Slot, BreakToken? Token)> _passEntries = [];

        /// <summary>
        /// The same entries as a set, so "have we been here before?" is one lookup rather than a scan of
        /// every pass a long document has run.
        /// </summary>
        private readonly HashSet<(int Slot, BreakToken Token)> _passEntrySet = [];

        /// <summary>
        /// Every <see cref="TruncatePassEntries"/> event so far this layout, scoped by which
        /// <see cref="_passEntries"/> index it discarded from — the retraction history
        /// <see cref="CssBox.PlacedByPassIfStillValid"/> checks a box's own recorded pass index against.
        /// Reuses <see cref="Fragmentation.InvalidationHistory"/> rather than a single bumped counter for
        /// the same reason that type itself replaced one (see its own remarks): a truncation at index 5
        /// says nothing about a box stamped with pass index 2, and a plain counter cannot tell the two
        /// apart from one that actually retires it.
        /// </summary>
        private readonly InvalidationHistory _passInvalidations = new();

        /// <summary>
        /// The index into <see cref="_passEntries"/> of the fragmentainer pass currently running, for a
        /// box being placed <i>right now</i> to stamp itself with (<see cref="CssBox.CommitBlockChildOffset"/>).
        /// </summary>
        internal int CurrentPassIndex => _passEntries.Count - 1;

        /// <summary>
        /// How many <see cref="_passInvalidations"/> events have been recorded so far, for a box to stamp
        /// itself with alongside <see cref="CurrentPassIndex"/> (<see cref="CssBox.CommitBlockChildOffset"/>).
        /// </summary>
        internal int PassInvalidationCount => _passInvalidations.Count;

        /// <summary>
        /// Whether a box's own recorded pass index is still trustworthy — see
        /// <see cref="CssBox.PlacedByPassIfStillValid"/>, the only caller.
        /// </summary>
        internal bool PassEntryStillValid(int recordedAt, int placedByPass) =>
            _passInvalidations.StillSafe(recordedAt, placedByPass);

        /// <summary>
        /// Notes that a fragmentainer pass is about to run at <paramref name="slot"/> with
        /// <paramref name="token"/>.
        /// </summary>
        private void RecordPassEntry(int slot, BreakToken? token)
        {
            _passEntries.Add((slot, token));

            if (token is not null) _passEntrySet.Add((slot, token));
        }

        /// <summary>
        /// Forgets every pass entry from <paramref name="index"/> on, because those passes are about to
        /// run again and the record of them describes a layout that no longer exists.
        /// </summary>
        private void TruncatePassEntries(int index)
        {
            _passEntries.RemoveRange(index, _passEntries.Count - index);

            // Every index a box stamped itself with (CssBox._placedByPass) that named a slot at or past
            // this truncation is retired by it, whether it still fits inside the shortened list or not:
            // the passes that follow are about to run again, and a later one can fill this same index
            // with a different pass entirely (see PlacedByPassIfStillValid's own remarks). Recorded by
            // index rather than a bare bump so an unrelated truncation elsewhere in the document does not
            // retire a stamp this one never touched.
            _passInvalidations.Record(index);

            _passEntrySet.Clear();

            foreach (var (slot, token) in _passEntries)
            {
                if (token is not null) _passEntrySet.Add((slot, token));
            }
        }

        /// <summary>
        /// Whether some pass of this layout has already been entered at the fragmentainer
        /// <paramref name="next"/> names, with <paramref name="next"/> itself — so resuming from it would
        /// re-run a stretch the driver has already run and arrive back here.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The equality test this one sits beside in <see cref="LayoutDocument"/> compares a pass's
        /// outgoing record with the <i>incoming</i> record of that same pass, so it recognises a run
        /// that gets nowhere only when
        /// the cycle is exactly one pass long. A cycle of two or more slips through it however correct
        /// the equality is — the real case that found this was a table continuation that alternated
        /// between one finished cell and none — and what the driver then does is not recover but spin to
        /// <see cref="MaxFragmentainers"/> and truncate the document without saying so. A pass is a
        /// function of the slot it fills and the record it resumes from, so arriving at a pair the run
        /// has already been entered with is the general statement of "this cannot advance", and the
        /// consecutive test is the special case of it.
        /// </para>
        /// <para>
        /// Asked of the <i>entries</i>, which is why <see cref="TruncatePassEntries"/> exists: a
        /// keep-with-next run pull deliberately re-enters an earlier pass with the pair it was first
        /// entered with, and rolls the box tree back so that pass produces something different. Those
        /// entries describe passes that are being replaced rather than repeated, and leaving them behind
        /// would make a legitimate rewind look like a stall.
        /// </para>
        /// </remarks>
        private bool HasAlreadyBeenEntered(BreakToken next) =>
            _passEntrySet.Contains((next.ResumeSlotIndex, next));

        /// <summary>
        /// A request to re-enter the pass that placed <c>Head</c>, with the box re-placed at <c>Top</c>.
        /// </summary>
        private (CssBox Head, double Top)? _runPullRewind;

        /// <summary>
        /// The boxes already rewound to in this layout — one rewind per head, so a run that keeps reaching
        /// the same conclusion cannot cycle.
        /// </summary>
        private readonly HashSet<CssBox> _passesRewoundFor = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Records a request to re-enter the pass that placed <paramref name="head"/>, so a keep-with-next
        /// run whose head belongs to an already-filled fragmentainer can be <i>laid out again</i> at
        /// <paramref name="top"/> rather than translated there
        /// (<see href="https://www.w3.org/TR/css-break-3/#possible-breaks">§4.3</see>). Returns whether it
        /// was taken; the caller falls back to the translation when it was not.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Declined once per head per layout — not per pass. The rewound pass reaches the same epilogue
        /// again, and where the correction has done its job the second answer is "nothing to do"; where it
        /// has not, repeating it walks the group down the document one pass at a time against the driver's
        /// own cap. This is the same shape, and the same reason, as the <c>orphans</c> break conversion's
        /// latch.
        /// </para>
        /// <para>
        /// Also declined where no recorded pass was filling the head's own slot, which is the honest way to
        /// ask "is there a pass to go back to": the head's position is the only thing that says which
        /// fragmentainer it was placed in. Preferring the head's own <see cref="CssBox.PlacedByPassIfStillValid"/>
        /// stamp over deriving the slot from <see cref="CssBox.Location"/> is what answers this correctly
        /// for a head a forced break stepped a still-open pass forward onto: that pass recorded its entry
        /// at the slot it <i>opened</i> at, not the one its cursor reached, so a lookup keyed on the head's
        /// own slot would find nothing there at all (issue #384).
        /// </para>
        /// </remarks>
        internal bool RequestPassRewind(CssBox head, double top)
        {
            // One pending request at a time, one per head per layout, a real page grid to name a pass
            // against, and not inside a column - a column's fill is driven by the columns engine rather
            // than by this driver, so sending it back would rewind a pass that is not the one filling that
            // fragmentainer. And a pass that was filling the head's own fragmentainer has to exist for
            // there to be one to name.
            if (_runPullRewind is not null
                || !HasRealPageGrid
                || CurrentFragmentainer is { HasOwnBand: true }
                || _passesRewoundFor.Contains(head)
                || PassEntryFor(head) < 0)
            {
                return false;
            }

            _runPullRewind = (head, top);
            return true;
        }

        /// <summary>
        /// Re-enters the pass that placed <paramref name="pull"/>'s head, with the head re-placed at the
        /// break's target so the run and everything after it is laid out again rather than moved.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only the head is told where to go. Every other member of the run, and every box after it,
        /// re-derives its position from the sibling above it as the re-entered pass reaches it — which is
        /// exactly what <c>TryRestartAt</c> does inside a single pass, and the reason neither has to compute
        /// a group offset.
        /// </para>
        /// <para>
        /// The fragments from the head's own fragmentainer on are un-frozen first: that pass is about to
        /// describe different geometry, and everything after it is about to be laid out again.
        /// </para>
        /// <para>
        /// So is the layout those passes produced (<see cref="PassRewind.RollBackTo"/>). Un-freezing a
        /// fragment says only that it will be built again; it does not undo what the box tree holds, and
        /// the box the re-entered pass resumes into holds line boxes those passes appended. Its prologue
        /// is once per layout and a resumed flow deliberately does not clear its line list, so without the
        /// rollback <c>CssLayoutEngine.FinalizeLineBoxes</c> hands them to
        /// <see cref="CssLineBox.AssignRectanglesToBoxes"/> a second time and the per-line rectangle they
        /// already carry throws.
        /// </para>
        /// </remarks>
        private bool TryRewindForRunPull((CssBox Head, double Top) pull, ref BreakToken? token, ref int slot)
        {
            var entry = PassEntryFor(pull.Head);

            if (entry < 0) return false;

            var (rewoundSlot, rewoundToken) = _passEntries[entry];

            _passesRewoundFor.Add(pull.Head);
            PassRewinds++;
            _emitter?.InvalidateFrom(rewoundSlot, pull.Head);

            PassRewind.RollBackTo(rewoundToken, Root!.Boxes);

            // The passes from this one on are about to run again, so the record of them describes a layout
            // that no longer exists.
            TruncatePassEntries(entry);

            slot = rewoundSlot;
            token = rewoundToken;

            pull.Head.ResumeAt(null, pull.Top);
            return true;
        }

        /// <summary>
        /// The index of the pass that was filling <paramref name="slot"/>, or -1 when none was.
        /// </summary>
        /// <remarks>
        /// The <i>first</i> such pass, since a slot re-entered more than once is one whose earliest pass is
        /// the one that placed the content now being reconsidered.
        /// </remarks>
        private int PassEntryFilling(int slot) => _passEntries.FindIndex(entry => entry.Slot == slot);

        /// <summary>
        /// The index of the pass that placed <paramref name="head"/>, or -1 when none can be identified.
        /// </summary>
        /// <remarks>
        /// Prefers <paramref name="head"/>'s own <see cref="CssBox.PlacedByPassIfStillValid"/> stamp — set
        /// directly by the pass that actually placed it (<see cref="CssBox.CommitBlockChildOffset"/>) —
        /// over <see cref="PassEntryFilling"/>'s slot-keyed lookup, which cannot find a pass whose own
        /// cursor stepped past the slot it opened at without ending the pass (a forced break stepping a
        /// still-open pass forward, issue #384): that pass's one <see cref="_passEntries"/> entry names
        /// only the slot it opened <i>at</i>, not every slot it went on to fill. Falls back to the slot
        /// lookup when the stamp is absent or stale, which keeps every caller correct for content this
        /// stamp does not (yet) cover.
        /// </remarks>
        private int PassEntryFor(CssBox head)
        {
            var stamped = head.PlacedByPassIfStillValid(this);
            return stamped >= 0 ? stamped : PassEntryFilling(SlotStartingAt(head.Location.Y));
        }

        /// <summary>
        /// Records <paramref name="box"/>'s request to re-run the pass it is completing, with its previous
        /// fragment cut down to <paramref name="budget"/> line boxes
        /// (<see href="https://www.w3.org/TR/css-break-3/#widows-orphans">§5.4</see>). Returns whether the
        /// request was taken.
        /// </summary>
        /// <remarks>
        /// One request per pass. Two boxes cannot both be rewound at once — the second's own fragment would
        /// be rebuilt by the first's rewind anyway, and it asks again on the pass that follows.
        /// </remarks>
        internal bool RequestWidowsRewind(CssBox box, int budget)
        {
            if (_widowsRewind is not null) return false;

            _widowsRewind = (box, budget);
            return true;
        }

        /// <summary>
        /// Re-enters the pass that has just ended with <paramref name="rewind"/>'s box cut down to its line
        /// budget, so the fragment after the break gets the lines <c>widows</c> asks for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The pass is re-run rather than the whole document: in block flow the box whose inline flow
        /// stopped is the <b>last</b> content in the fragmentainer it stopped in, so keeping fewer lines
        /// there shortens that fragmentainer's tail and moves nothing else in it. Everything that follows
        /// the box does move, and that is exactly what re-running this pass onwards lays out again.
        /// </para>
        /// <para>
        /// Undone the same way the other two pass re-entries do it (<see cref="PassRewind.RollBackTo"/>):
        /// the box's own line boxes past the budget go, and everything the pass placed after it — laid out
        /// from the start, exactly as on a starting pass — is reset outright and laid out again by the
        /// re-entered pass. The fragment the earlier pass froze goes too, because it is about to describe
        /// different content. And the resumption record the pass was entered with is rebuilt to name the
        /// budget, since that record is the only thing that says where the flow picks up.
        /// </para>
        /// <para>
        /// Widening this from a narrower rewind of only the box's own lines was tried once and measurably
        /// <i>lost content</i> — 16 words of <c>paged_media_horizontal_reflow</c> — and filed as issue #440.
        /// The actual cause was a pre-existing, unrelated defect: a word a stopped flow never reached still
        /// carried document Y 0, which lies inside the <i>first</i> page's own band, so an earlier
        /// fragment wrongly claimed it (issue #433, fixed by the block's own inline flow now saying
        /// <see cref="CssBox.AwaitPlacement"/> of itself before it starts). Once that was fixed, the shared
        /// rollback stopped losing anything — confirmed with real Georgia metrics on Windows (the platform
        /// the loss was originally measured on) via <c>git bisect</c> against the full showcase corpus.
        /// </para>
        /// <para>
        /// Declined where the record does not name the box — the box is only rewindable when it is the one
        /// the previous pass stopped at, which is what "its previous fragment is the pass being re-run"
        /// means. The caller then falls back to the whole-box push.
        /// </para>
        /// </remarks>
        private bool TryRewindForWidows(
            (CssBox Box, int Budget) rewind, ref BreakToken? token)
        {
            if (token is null || !TryRebuildForBudget(token, rewind.Box, rewind.Budget, out var rebuilt))
                return false;

            InvalidateEmittedFragmentsFor(rewind.Box, rewind.Box.LineBoxes[0].LineTop);
            PassRewind.RollBackTo(rebuilt, Root!.Boxes);

            token = rebuilt;
            return true;
        }

        /// <summary>
        /// The same resumption chain with the inline link for <paramref name="box"/> resuming at line
        /// <paramref name="budget"/> instead of wherever it did, or false when the chain does not end in
        /// that box's inline flow.
        /// </summary>
        internal static bool TryRebuildForBudget(
            BreakToken token, CssBox box, int budget, out BreakToken rebuilt)
        {
            rebuilt = token;

            switch (token)
            {
                case InlineBreakToken inline when ReferenceEquals(inline.Box, box):
                    rebuilt = inline with
                    {
                        // The walk position of the first line the budget gives up, which is where the flow
                        // is to pick up instead.
                        ResumeWordIndex = box.LineBoxes[budget].StartOrdinal,
                        CompletedLineCount = budget,
                        LinesKeptHere = budget - (inline.CompletedLineCount - inline.LinesKeptHere),
                        // hyphenate-limit-lines (CSS Text 4 §6.3.5): the rewind can give back a line the
                        // original ConsecutiveHyphenatedLines count was itself computed through, so the
                        // count has to be re-derived from what the budget actually keeps rather than kept
                        // as-is - the same "stale unless recomputed" hazard
                        // EnforceHyphenateLimitLastBeforeBreak's own reset exists for, just reached by a
                        // rewind giving lines back instead of a hyphen being undone.
                        ConsecutiveHyphenatedLines = TrailingHyphenatedLineCount(box, budget - 1)
                    };
                    return true;

                case BlockBreakToken { ChildToken: { } child } block
                    when TryRebuildForBudget(child, box, budget, out var rebuiltChild):
                    rebuilt = block with { ChildToken = rebuiltChild };
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// How many of <paramref name="box"/>'s own line boxes, counting back from
        /// <paramref name="lastKeptIndex"/>, end in a hyphenation split - i.e. what
        /// <see cref="Entities.CssLineBoxCoordinates.ConsecutiveHyphenatedLines"/> would hold if layout
        /// had stopped exactly after that line rather than wherever the original pass actually did.
        /// </summary>
        /// <remarks>
        /// A hyphenated line's last word is always a split's prefix (<c>TryHyphenateWord</c>'s own
        /// construction sets <see cref="CssRectWord.PreSplitWord"/> on it), so this needs no text/glyph
        /// comparison - unlike the character a custom <c>hyphenate-character</c> might render, which
        /// data alone this method has access to.
        /// </remarks>
        private static int TrailingHyphenatedLineCount(CssBox box, int lastKeptIndex)
        {
            var count = 0;

            for (var i = lastKeptIndex; i >= 0; i--)
            {
                if (box.LineBoxes[i].Words is not [.., CssRectWord { PreSplitWord: not null }])
                    break;

                count++;
            }

            return count;
        }

        /// <summary>
        /// The highest pagination slot any of the document's geometry reaches, never below
        /// <paramref name="from"/>. The one geometric question the emission model still has to ask, and only
        /// after the final pass: content that no break record names — a monolithic subtree, a box that simply
        /// overflows its page — extends past the slot the pass that laid it out was filling.
        /// </summary>
        private int LastSlotAnyGeometryTouches(int from) =>
            Math.Max(from, SlotEndingAt(MarginTop + ActualSize.Height));

        /// <summary>
        /// The fragments the current (or last) <see cref="LayoutDocument"/> invocation's passes emitted.
        /// </summary>
        private FragmentEmitter? _emitter;

        /// <summary>
        /// Un-freezes every already-emitted fragmentainer from the one containing <paramref name="documentY"/>
        /// on, because <paramref name="box"/>'s geometry there is about to change — see
        /// <see cref="FragmentEmitter.InvalidateFrom"/>. A no-op in the ordinary forward case, and during an
        /// unpaginated/measurement pass, which has no grid to name a slot against.
        /// </summary>
        /// <remarks>
        /// Also a no-op for a repeating <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>'s detached source subtree
        /// (<see cref="CssBox.IsInDetachedRepeatingGroup"/>): its own <see cref="CssBox.Location"/> is
        /// relocated once per page it repeats onto purely so the <i>next</i> page's proxy captures the
        /// right snapshot - every already-emitted page read that content through its <i>own</i> frozen
        /// <c>BoxGeometrySnapshot</c>, taken when that page was built, never through the source's live,
        /// currently-relocating position, so nothing already emitted is stale. Left ungated, a long
        /// repeating header relocating on every page it spans turned a real, document-wide re-walk into a
        /// per-page event throughout the header's own span - reported as issue #917's dominant cost,
        /// confirmed independent of forced breaks by a single repeating-header table with no forced break
        /// anywhere in it.
        /// </remarks>
        internal void InvalidateEmittedFragmentsFor(CssBox box, double documentY)
        {
            // The box question first, deliberately: this runs on every block-axis reposition in the document,
            // and PageIndexOf under a per-page @page geometry table is a forward-incremental walk rather than
            // arithmetic. Nothing should be asked of the page grid for a box that cannot need re-emitting.
            if (_emitter is null || !_emitter.HoldsFragmentsFor(box) || !HasRealPageGrid) return;
            if (box.IsInDetachedRepeatingGroup) return;

            _emitter.InvalidateFrom(PageIndexOf(documentY), box);
        }

        /// <summary>
        /// Un-freezes the already-emitted fragmentainers an absolutely positioned <paramref name="box"/>
        /// has just been laid out into, so they are emitted again with the box in them. Called once the box
        /// has its final position and height; a no-op for every box that lands where layout has not yet
        /// emitted anything, which is every placement in forward layout.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="InvalidateEmittedFragmentsFor"/> covers a box that already holds fragments and moves.
        /// An absolutely positioned box can instead land behind the pass that places it without ever having
        /// been emitted: its containing block is laid out on an earlier fragmentainer than the box itself,
        /// most often the initial containing block on the first page (CSS 2.1 §10.1), while the box is
        /// reached in the tree on a later pass. Nothing re-opened that fragmentainer, so the box was drawn
        /// on no page (#1349).
        /// </para>
        /// <para>
        /// Only the fragmentainers the box's border box reaches are re-opened, not everything after them:
        /// the box is out of flow, so nothing else moved. A box a frozen fragmentainer already holds is
        /// left to <see cref="InvalidateEmittedFragmentsFor"/>, and one entirely above the first page (a
        /// skip link at <c>top: -9999px</c>) has nowhere to be drawn. Without those limits a document with
        /// one badge per paragraph re-emitted every page for each badge on every pass, more than doubling
        /// its layout time.
        /// </para>
        /// </remarks>
        internal void InvalidateEmittedFragmentainersReceiving(CssBox box)
        {
            if (_emitter is null || !HasRealPageGrid) return;
            if (box.IsInDetachedRepeatingGroup || _emitter.HoldsFragmentsFor(box)) return;
            if (box.ActualBottom <= 0) return;

            var first = PageIndexOf(Math.Max(box.Location.Y, 0) + PageBoundaryEpsilon);
            // Content that overflows the box (overflow: visible) is drawn past its border box, on pages the
            // border box does not reach; those have to be re-opened too, or the overflowing lines are lost.
            var bottom = box.Overflow.Value == PeachPDF.CSS.Overflow.Visible
                ? CssBox.GetMaximumBottom(box, box.ActualBottom)
                : box.ActualBottom;
            var last = PageIndexOf(Math.Max(bottom - PageBoundaryEpsilon, 0));

            _emitter.InvalidateFrom(Math.Max(first, 0), box, throughSlot: Math.Max(last, first));
        }

        /// <summary>
        /// Hands one nested fragmentainer — a multi-column column,
        /// <see href="https://www.w3.org/TR/css-break-3/#fragmentainer">§2</see>'s other kind — over to the
        /// emitter, with the geometry the subtree had while it was being filled.
        /// </summary>
        /// <remarks>
        /// The one place layout <i>states</i> a fragment's geometry rather than leaving the emitter to read
        /// it off the boxes. It has to, because a nested fragmentainer differs from its neighbours in the
        /// <b>inline</b> axis: a box continuing into the next column is laid out again at that column's own
        /// position, so its live geometry describes only the last fragment it produced.
        /// </remarks>
        internal void RecordCapturedInstance(
            CssBox contextRoot,
            int slot,
            (double Top, double Bottom) band,
            (double Left, double Right) inline,
            BoxGeometrySnapshot geometry,
            IReadOnlySet<CssBox> continuing,
            FragmentainerContext self,
            FragmentainerContext? parentContext) =>
            _emitter?.RecordCapturedInstance(contextRoot, slot, band, inline, geometry, continuing, self, parentContext);

        /// <summary>
        /// Discards what <paramref name="contextRoot"/> recorded in <paramref name="slot"/> — or, with no
        /// slot, in every slot — for a fill being attempted afresh.
        /// </summary>
        internal void ClearCapturedInstances(CssBox contextRoot, int? slot = null)
        {
            _emitter?.ClearCapturedInstances(contextRoot, slot);
            ColumnFragmentainers.RemoveAll(r => ReferenceEquals(r.ColumnsBox, contextRoot) && (slot is null || r.Slot == slot));
        }

        /// <summary>
        /// Publishes one filled column's identity - see
        /// <see cref="Fragmentation.ColumnFragmentainerRecord"/>. Called from the same place the emitter's
        /// own captured instance is recorded, and truncated by the same two methods, so the two can never
        /// disagree about which columns a balance retry discarded.
        /// </summary>
        internal void RecordColumnFragmentainer(Fragmentation.ColumnFragmentainerRecord record) =>
            ColumnFragmentainers.Add(record);

        /// <summary>
        /// How much room a column-scoped note area needs in the column identified by
        /// <paramref name="key"/>, as resolved on the previous attempt - zero when none landed there.
        /// </summary>
        internal double ColumnFootnoteInsetFor(Fragmentation.ColumnAreaKey key) =>
            FootnoteAreaHeightsByColumn.GetValueOrDefault(key, 0);

        /// <summary>
        /// Discards only what <paramref name="contextRoot"/> recorded in <paramref name="slot"/> from
        /// index <paramref name="keepFirst"/> onward, leaving an earlier <c>column-span: all</c> run's
        /// already-finished columns in the same slot untouched.
        /// </summary>
        internal void ClearCapturedInstancesFrom(CssBox contextRoot, int slot, int keepFirst)
        {
            _emitter?.ClearCapturedInstancesFrom(contextRoot, slot, keepFirst);

            // Lockstep with the emitter's own truncation rather than a parallel cleanup: keepFirst counts
            // this contextRoot's instances in this slot, so the column records are dropped by the same
            // index.
            var seen = 0;
            ColumnFragmentainers.RemoveAll(r =>
                ReferenceEquals(r.ColumnsBox, contextRoot) && r.Slot == slot && seen++ >= keepFirst);
        }

        /// <summary>
        /// Hands the emitter one repeating <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> instance's captured
        /// geometry for the page it was just laid out on — see
        /// <see cref="FragmentEmitter.RecordRepeatingGroupInstance"/>.
        /// </summary>
        internal void RecordRepeatingGroupInstance(
            CssBox tableBox,
            int slot,
            (double Top, double Bottom) band,
            BoxGeometrySnapshot geometry,
            CssBox sourceRoot,
            FragmentainerContext self,
            FragmentainerContext? parentContext) =>
            _emitter?.RecordRepeatingGroupInstance(tableBox, slot, band, geometry, sourceRoot, self, parentContext);

        /// <summary>
        /// States that <paramref name="box"/> occupies <paramref name="rect"/> in the fragmentainer that
        /// rectangle falls in while holding none of its content there — see
        /// <see cref="FragmentEmitter.RecordContinuationShell"/>.
        /// </summary>
        /// <remarks>
        /// Null on a measurement or detached-fragmentainer run, which has no emitter to state anything to.
        /// </remarks>
        /// <param name="box">the box whose continuation this is</param>
        /// <param name="slot">the slot layout believed it was filling, for the sweep below</param>
        /// <param name="rect">the box's border box there, in document space</param>
        internal void RecordContinuationShell(CssBox box, int slot, RRect rect) =>
            _emitter?.RecordContinuationShell(box, slot, rect);

        /// <summary>
        /// Discards what <paramref name="box"/> stated from <paramref name="fromSlot"/> on — or, with no
        /// slot, in every slot — see <see cref="FragmentEmitter.ClearContinuationShells"/>.
        /// </summary>
        internal void ClearContinuationShells(CssBox box, int? fromSlot = null) =>
            _emitter?.ClearContinuationShells(box, fromSlot);

        /// <summary>
        /// States that <paramref name="box"/> and its subtree draw <paramref name="shift"/> lower in
        /// <paramref name="slot"/>, confined to <paramref name="band"/> — see
        /// <see cref="FragmentEmitter.RecordFragmentDisplacement"/>.
        /// </summary>
        /// <remarks>
        /// Null on a measurement or detached-fragmentainer run, which has no emitter to state anything to.
        /// </remarks>
        /// <param name="box">the root of the run being sliced</param>
        /// <param name="slot">the fragmentainer this displacement applies to</param>
        /// <param name="shift">how far lower the box draws there</param>
        /// <param name="band">the content band the fragment is confined to, in document space</param>
        internal void RecordFragmentDisplacement(CssBox box, int slot, double shift, RRect band) =>
            _emitter?.RecordFragmentDisplacement(box, slot, shift, band);

        /// <summary>
        /// Discards what <paramref name="box"/> stated from <paramref name="fromSlot"/> on — or, with no
        /// slot, in every slot — see <see cref="FragmentEmitter.ClearFragmentDisplacements"/>.
        /// </summary>
        internal void ClearFragmentDisplacements(CssBox box, int? fromSlot = null) =>
            _emitter?.ClearFragmentDisplacements(box, fromSlot);

        /// <summary>
        /// The box whose background fills the whole page canvas, per
        /// <see href="https://www.w3.org/TR/css-backgrounds-3/#root-background">CSS Backgrounds 3 §2.11.2</see>:
        /// the root <c>html</c> element's own background if it declares one, else the background
        /// propagated from its <c>body</c> child, else none. Null when neither declares a background.
        /// </summary>
        internal CssBox? CanvasBackgroundBox { get; private set; }

        /// <summary>
        /// Resolves <see cref="CanvasBackgroundBox"/> and marks the chosen box so its own ordinary
        /// background pass is suppressed — otherwise the same background would be painted twice, once
        /// across the page and again at the box's own (possibly much smaller) laid-out rectangle.
        /// </summary>
        /// <remarks>
        /// Purely cascade-driven, so this belongs to layout rather than paint: nothing here reads
        /// geometry, and the pagination rule that decides which pages exist needs the answer.
        /// </remarks>
        private void ResolveCanvasBackground()
        {
            var html = DomUtils.GetBoxByTagName(Root, "html");

            // A <body style="display:contents"> generates no box, but its background still propagates to
            // the canvas (CSS Backgrounds 3 §2.11.2 excludes only display:none from that rule) - so it is
            // looked for among the shells as well as the tree.
            var body = DomUtils.GetBoxByTagName(Root, "body")
                ?? DisplayContentsShells.Find(s => s.HtmlTag?.Name.Equals("body", StringComparison.OrdinalIgnoreCase) == true);

            // Cleared first so a re-layout can never leave a previous winner suppressed.
            if (html is not null) html.SuppressOwnBackgroundPaint = false;
            if (body is not null) body.SuppressOwnBackgroundPaint = false;

            // The root's own background is the canvas background; body's is propagated only when the
            // root's background-image is none and its background-color is transparent (§2.11.2).
            CanvasBackgroundBox = html is { HasOwnBackground: true } ? html
                : body is { HasOwnBackground: true } ? body
                : null;

            if (CanvasBackgroundBox is not null)
                CanvasBackgroundBox.SuppressOwnBackgroundPaint = true;
        }

        /// <summary>
        /// The immutable output of the last <see cref="PerformLayout"/> — the document sliced into
        /// per-page <see cref="Fragments.BoxFragment"/>s (CSS Fragmentation Level 3 §2). Null until the
        /// document has been laid out. This is the layout↔paint contract: paint reads geometry from
        /// here, never from <see cref="Dom.CssBox"/>.
        /// </summary>
        internal FragmentTree? FragmentTree { get; private set; }

        /// <summary>
        /// Recursively checks whether any box in the tree is floated, out-of-flow, and/or (when
        /// <paramref name="includeStackingHoistCandidates"/> is true, and excluding the root itself,
        /// which trivially always establishes a stacking context) needs hoisting for stacking-context
        /// purposes, short-circuiting once every requested flag has been confirmed true.
        /// </summary>
        private static (bool HasFloated, bool HasOutOfFlow, bool HasStackingHoistCandidates) ComputeFlowFlags(
            CssBox box, bool includeStackingHoistCandidates)
        {
            var hasFloated = false;
            var hasOutOfFlow = false;
            var hasStackingHoistCandidates = false;
            ComputeFlowFlags(box, isRoot: true, includeStackingHoistCandidates,
                ref hasFloated, ref hasOutOfFlow, ref hasStackingHoistCandidates);
            return (hasFloated, hasOutOfFlow, hasStackingHoistCandidates);
        }

        private static void ComputeFlowFlags(CssBox box, bool isRoot, bool includeStackingHoistCandidates,
            ref bool hasFloated, ref bool hasOutOfFlow, ref bool hasStackingHoistCandidates)
        {
            if (box.IsFloated) hasFloated = true;
            if (box.IsOutOfFlow) hasOutOfFlow = true;
            if (includeStackingHoistCandidates && !isRoot && DomUtils.NeedsStackingHoist(box))
                hasStackingHoistCandidates = true;

            // A repeating <thead>/<tfoot>'s proxy stands in for its source row group, detached from the
            // live tree by CssLayoutEngineTable.RemoveHeaderFooterFromTree - so a float, an out-of-flow
            // box, or a stacking-context box that exists only inside a repeated header/footer is invisible
            // to the ordinary Boxes walk below. FragmentEmitter.ChildrenOf unwraps the same proxy the same
            // way for fragment-building; this is that pattern's paint/layout-flags counterpart.
            if (box is CssProxyBox proxy)
            {
                ComputeFlowFlags(proxy.SourceBox, false, includeStackingHoistCandidates,
                    ref hasFloated, ref hasOutOfFlow, ref hasStackingHoistCandidates);
            }

            foreach (var childBox in box.Boxes)
            {
                if (hasFloated && hasOutOfFlow && (!includeStackingHoistCandidates || hasStackingHoistCandidates))
                    return;
                ComputeFlowFlags(childBox, false, includeStackingHoistCandidates,
                    ref hasFloated, ref hasOutOfFlow, ref hasStackingHoistCandidates);
            }
        }

        /// <summary>
        /// A snapshot of every box's pagination-slot assignment — <see cref="PageIndexOf"/> of its
        /// laid-out top, paired with that slot's own active named page (<see cref="PageBandGeometry.ActiveName"/>)
        /// — in a fixed tree-walk order, used by <see cref="PerformLayout"/>'s per-page horizontal-reflow
        /// loop to detect a fixpoint: once a re-pass leaves every box on the same page (AND that page
        /// still carries the same active name) it was on before, the per-page widths it resolved against
        /// are self-consistent and the loop stops. The name is paired with the index, not just checked
        /// alongside it, so two passes that agree on numeric page index but disagree on which named-page
        /// rule is active there (issue #202: a named run whose own width→height feedback shifts which
        /// physical page a later name-transition boundary falls on) are correctly told apart rather than
        /// wrongly accepted as converged. Rebuilt from scratch each call — the loop runs at most a few
        /// iterations, and only for the rare document that carries a per-page left/right <c>@page</c>
        /// margin override.
        /// </summary>
        private List<(int PageIndex, string? ActiveName)> PageAssignmentSignature()
        {
            List<(int, string?)> signature = [];
            if (Root is not null)
                CollectPageAssignments(Root, signature);
            return signature;
        }

        private void CollectPageAssignments(CssBox box, List<(int PageIndex, string? ActiveName)> signature)
        {
            var pageIndex = PageIndexOf(box.Location.Y);
            signature.Add((pageIndex, PageGeometry.GetPage(pageIndex).ActiveName));
            foreach (var childBox in box.Boxes)
                CollectPageAssignments(childBox, signature);
        }

        /// <summary>
        /// Recursively re-applies content to pseudo-elements after layout completes.
        /// This ensures pseudo-elements can access named strings set during layout.
        /// </summary>
        private void ReapplyPseudoElementContent(CssBox box)
        {
            foreach (var childBox in box.Boxes)
            {
                if (childBox.IsPseudoElement && !string.IsNullOrEmpty(childBox.Content) && childBox.Content != Keywords.None && childBox.Content != Keywords.Normal)
                {
                    // Check if content contains string() function
                    if (childBox.Content.Contains("string("))
                    {
                        CssContentEngine.ApplyContent(childBox);
                        // Re-parse words after content changes - re-resolve bidi levels for the new
                        // text first (see CssBidiParagraphResolver.ResolveOwnTextAsParagraph's own
                        // remarks), since childBox.BidiLevels would otherwise still reflect whatever
                        // (possibly now-stale) content the last resolution saw.
                        CssBidiParagraphResolver.ResolveOwnTextAsParagraph(childBox);
                        if (!string.IsNullOrEmpty(childBox.Text))
                        {
                            childBox.ParseToWords();
                        }
                    }
                }
                ReapplyPseudoElementContent(childBox);
            }
        }

        /// <summary>
        /// This attempt's resolved note areas, grouped by the pagination slot they sit on - what
        /// <see cref="ResolveFootnotesForThisAttempt"/> laid out, kept around so
        /// <see cref="AttachFootnoteAreas"/> can build each <see cref="FootnoteAreaFragment"/> from
        /// bodies already laid out rather than re-deriving which calls landed where. A slot holds more
        /// than one area whenever any <c>float-reference: column</c> call landed on it.
        /// </summary>
        private Dictionary<int, List<FootnoteAreaGroup>> _footnoteAreasBySlot = [];

        /// <summary>
        /// Containing blocks <see cref="ResolveFootnotesForThisAttempt"/> has asked for a
        /// <c>footnote-policy: block</c> forced break-before on, so it can clear
        /// <see cref="CssBox.FootnotePolicyForcedBreakBefore"/> on each before re-deciding fresh next
        /// pass rather than letting a stale request from an earlier pass linger.
        /// </summary>
        private readonly HashSet<CssBox> _footnotePolicyForcedBreakBoxes = [];

        /// <summary>
        /// Footnote calls <see cref="ResolveFootnotesForThisAttempt"/> has asked for a
        /// <c>footnote-policy: line</c> forced break on - consulted by <see cref="CssLayoutEngine.FlowBox"/>'s
        /// per-word placement loop, the same way <see cref="_footnotePolicyForcedBreakBoxes"/> is consulted
        /// by <see cref="CssBox.PerformLayoutPrologue"/>. Cleared and re-decided fresh every pass.
        /// </summary>
        internal readonly HashSet<CssBoxFootnoteCall> FootnotePolicyForcedLineCalls = [];

        /// <summary>
        /// The one-shot-per-<see cref="LayoutDocument"/>-invocation counterpart to
        /// <see cref="FootnotePolicyForcedLineCalls"/>: which calls have already taken their forced
        /// <c>footnote-policy: line</c> break this pass, so <see cref="CssLayoutEngine.FlowBox"/> stops
        /// asking for one again as this same pass resumes the call onto each successive page in turn -
        /// see <see cref="LayoutDocument"/>'s own remarks on why this is cleared there, not alongside
        /// <see cref="FootnotePolicyForcedLineCalls"/>.
        /// </summary>
        internal readonly HashSet<CssBoxFootnoteCall> FootnotePolicyLineBreaksTakenThisPass = [];

        /// <summary>
        /// The footnote number currently being applied to a <see cref="CssBoxFootnoteCall"/> or its
        /// marker, and null outside that one moment. <see cref="CssContentEngine"/> consults it before
        /// falling through to <see cref="CssCounterEngine"/>, which is what makes an author's
        /// <c>content: counter(footnote)</c> resolve to the live, pagination-resolved number.
        /// </summary>
        /// <remarks>
        /// The same ambient, scoped shape as <see cref="RunningElementPageContext"/>, and for the same
        /// reason: a footnote's number depends on which page its call landed on, which the DOM-position
        /// counter engine has no notion of. Set inside a try/finally around the apply, so nothing outside
        /// that window ever sees it - <c>counter(footnote)</c> on an ordinary element still resolves
        /// through the document counter exactly as before.
        /// </remarks>
        internal int? FootnoteNumberContext { get; set; }

        /// <summary>
        /// Every column every multi-column container filled on this layout attempt, published by
        /// <see cref="Dom.CssLayoutEngineColumns"/> as each one is recorded. Read only by
        /// <see cref="ResolveFootnotesForThisAttempt"/>, to attribute a footnote call to the column it
        /// landed in - which geometry alone cannot say, since every column shares one block band.
        /// </summary>
        internal List<Fragmentation.ColumnFragmentainerRecord> ColumnFragmentainers { get; } = [];

        /// <summary>
        /// The per-column analogue of <see cref="FootnoteAreaHeightsBySlot"/>: how much room each
        /// column-scoped note area needs, seeded back into that column's own
        /// <see cref="Fragmentation.FragmentainerContext.ReserveBandEnd"/> on the next attempt.
        /// </summary>
        internal Dictionary<Fragmentation.ColumnAreaKey, double> FootnoteAreaHeightsByColumn { get; private set; } = [];

        /// <summary>
        /// Every footnote number and the exact call/marker text it produced on the previous convergence
        /// pass, in document order - the fixpoint test for numbering, alongside the reserved-height one.
        /// </summary>
        private string _footnoteNumberingSignature = string.Empty;

        /// <summary>
        /// Space between two stacked footnote bodies on the same page. Stays here rather than moving to
        /// <see cref="FootnoteAreaRule"/> with the area's other spacing: this is how bodies stack
        /// <em>within</em> the area's content box, not part of the box model <c>@footnote</c> styles.
        /// </summary>
        private const double FootnoteBodySpacing = 4;

        /// <summary>
        /// Resolves css-gcpm-3's <c>@footnote</c> area rule for pagination slot <paramref name="slot"/> -
        /// the single place both <see cref="ResolveFootnotesForThisAttempt"/> (which needs it for height
        /// math) and <see cref="AttachFootnoteAreas"/> (which needs the identical numbers to build a
        /// byte-for-byte matching rect, per that method's own remarks on why the two must never disagree)
        /// resolve it. Falls back to <see cref="FootnoteAreaRule.Ua"/> per longhand, exactly as an unset
        /// property falls back to its initial value in a real cascade - an author who declares only
        /// <c>border-top</c> still gets the default top padding and divider-to-body gap.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Uses <see cref="PageRuleResolver.ActiveNameAtSlotStart"/> (not <c>ActiveNameAtPageEnd</c>,
        /// which paint-time margin-box resolution uses) and <c>slot + 1</c> as the page number - the same
        /// slot-start attribution <c>PageGeometryTable</c>'s own per-page geometry resolution uses, since
        /// resolving this during the footnote convergence loop (before the fragment tree, and so before a
        /// materialized page number, exists) has only a raw slot to work from. This can disagree with the
        /// true materialized page number only in the narrow case a content-empty page was skipped earlier
        /// in the document (issue #148) - a pre-existing limitation of the whole footnote-slot subsystem
        /// (<see cref="ResolveFootnotesForThisAttempt"/> already groups footnote calls by raw slot the
        /// same way), not a new one this introduces.
        /// </para>
        /// <para>
        /// <c>height</c>/<c>max-height</c> are block-axis lengths, so a percentage resolves against the
        /// page's own content band, not <paramref name="contentWidth"/> - which stays the basis for
        /// <c>margin-top</c>/<c>padding-top</c>, whose percentages are inline-axis per the CSS box model.
        /// </para>
        /// </remarks>
        internal FootnoteAreaRule ResolveFootnoteAreaRule(int slot, double contentWidth)
        {
            if (PageRules.Count == 0) return FootnoteAreaRule.Ua;

            var pageNumber = slot + 1;
            var activeName = PageRuleResolver.ActiveNameAtSlotStart(_namedPageElements, PageTopOf(slot));
            var rule = PageRuleResolver.SelectApplicableMarginRules(PageRules, pageNumber, activeName)
                .FirstOrDefault(m => string.Equals(m.Selector?.Text?.Trim(), "footnote", StringComparison.OrdinalIgnoreCase));

            if (rule is null) return FootnoteAreaRule.Ua;

            var pageStyle = PageRuleResolver.SelectApplicablePageStyle(PageRules, pageNumber, activeName);
            var remPt = PageLengthContext?.RemPt ?? DefaultFontResolver.FontSize;

            var topPadding = string.IsNullOrWhiteSpace(rule.Style.MarginTop)
                ? FootnoteAreaRule.DefaultTopPadding
                : MarginBoxRenderer.MarginExtent(rule, pageStyle, remPt, contentWidth, horizontal: false).Start;

            var dividerThickness = string.IsNullOrWhiteSpace(rule.Style.BorderTopStyle)
                ? FootnoteAreaRule.DefaultDividerThickness
                : MarginBoxRenderer.BorderExtent(rule, pageStyle, remPt, horizontal: false).Start;

            var dividerToBodyGap = string.IsNullOrWhiteSpace(rule.Style.PaddingTop)
                ? FootnoteAreaRule.DefaultDividerToBodyGap
                : MarginBoxRenderer.PaddingExtent(rule, pageStyle, remPt, contentWidth, horizontal: false).Start;

            var emPt = MarginBoxRenderer.ResolveFontSizePt(rule.Style, pageStyle);
            var blockBasis = PageBottomOf(slot) - PageTopOf(slot);

            var height = ResolveFootnoteAreaBlockLength(rule.Style.Height, emPt, remPt, blockBasis);
            var maxHeight = ResolveFootnoteAreaBlockLength(rule.Style.MaxHeight, emPt, remPt, blockBasis);

            // A divider only paints when @footnote declares a real border-top (a zero thickness handles
            // the "declared but none/hidden/zero-width" cases); null means "use the UA default black" the
            // same way an ordinary box's unset border-*-color falls back through currentcolor.
            var dividerColor = string.IsNullOrWhiteSpace(rule.Style.BorderTopStyle)
                ? null
                : rule.Style.BorderTopColor;

            var step = ResolveFootnoteCounterStep(rule);

            return new FootnoteAreaRule(topPadding, dividerThickness, dividerToBodyGap, height, maxHeight, step, dividerColor);
        }

        /// <summary>
        /// One <c>@footnote</c> block-axis length (<c>height</c>/<c>max-height</c>), or null when it is
        /// undeclared, <c>auto</c>, or unparseable - all of which mean "sized by content" for a note area.
        /// </summary>
        private static double? ResolveFootnoteAreaBlockLength(string? declaration, double emPt, double remPt, double blockBasis)
        {
            if (string.IsNullOrWhiteSpace(declaration)) return null;
            if (declaration!.Trim().Equals(Keywords.Auto, StringComparison.OrdinalIgnoreCase)) return null;

            // ParseLengthToPdfPoints already answers null for anything it cannot resolve, which is the
            // same "sized by content" outcome as auto.
            return DomParser.ParseLengthToPdfPoints(declaration, new PageLengthContext(emPt, remPt, blockBasis));
        }

        /// <summary>
        /// How much the footnote counter advances per note on a page whose <c>@footnote</c> rule is
        /// <paramref name="rule"/>, read from its own <c>counter-increment</c> exactly as the cascade
        /// states it: a bare <c>footnote</c> steps by one, <c>footnote &lt;n&gt;</c> by n, and a
        /// declaration that does not mention the counter at all (including <c>none</c>) steps by zero,
        /// so every note on that page shares one number. An absent <c>@footnote</c> rule falls back to
        /// the UA default of one.
        /// </summary>
        private static int ResolveFootnoteCounterStep(MarginStyleRule rule)
        {
            var declaration = rule.Style.CounterIncrement;

            if (string.IsNullOrWhiteSpace(declaration)) return FootnoteAreaRule.DefaultStep;

            return CounterListGrammar.TryGetValue(declaration, Keywords.Footnote, FootnoteAreaRule.DefaultStep, out var step)
                ? step
                : 0;
        }

        /// <summary>
        /// What the footnote counter resets to at the start of pagination slot <paramref name="slot"/>,
        /// or null when that page resets nothing - which is what gives numbering that runs continuously
        /// across pages.
        /// </summary>
        /// <remarks>
        /// This is ordinary cascade, not a special case. <c>counter-reset</c> is one property, so an
        /// author declaration on an applicable <c>@page</c> replaces the UA sheet's own
        /// <c>counter-reset: footnote</c> wholesale - which is why <c>counter-reset: none</c>, and
        /// equally a <c>counter-reset</c> that only names some other counter, both stop the per-page
        /// reset and give continuous numbering. A page with no applicable rule at all falls back to the
        /// UA default of resetting to zero, so the first note on it is numbered one.
        /// </remarks>
        private int? ResolveFootnoteCounterResetForSlot(int slot)
        {
            if (PageRules.Count == 0) return 0;

            var pageNumber = slot + 1;
            var activeName = PageRuleResolver.ActiveNameAtSlotStart(_namedPageElements, PageTopOf(slot));
            var declaration = PageRuleResolver.SelectApplicablePageStyle(PageRules, pageNumber, activeName)?.CounterReset;

            if (string.IsNullOrWhiteSpace(declaration)) return 0;

            return CounterListGrammar.TryGetValue(declaration, Keywords.Footnote, 0, out var reset)
                ? reset
                : null;
        }

        /// <summary>
        /// Applies <paramref name="number"/> to <paramref name="call"/> and its marker, and records both
        /// in <paramref name="signature"/> for the convergence loop's own change detection.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="FootnoteNumberContext"/> is set across the apply and restored after, so an author's
        /// <c>content: counter(footnote)</c> on either pseudo-element resolves against this number, and
        /// nothing outside the window can see it.
        /// </para>
        /// <para>
        /// See <see cref="ReparseFootnoteText"/> for the two things that have to happen around the
        /// re-parse, both of which have thrown for real when they were missing.
        /// </para>
        /// </remarks>
        private void ApplyFootnoteNumber(CssBoxFootnoteCall call, int number, StringBuilder signature)
        {
            var marker = (CssBoxFootnoteMarker)call.Body.Boxes[0];

            var previous = FootnoteNumberContext;
            FootnoteNumberContext = number;
            try
            {
                call.ApplyNumber(number);
                marker.ApplyNumber(number);
            }
            finally
            {
                FootnoteNumberContext = previous;
            }

            ReparseFootnoteText(call);
            ReparseFootnoteText(marker);

            signature.Append(number).Append('')
                     .Append(call.Text).Append('')
                     .Append(marker.Text).Append('');
        }

        /// <summary>
        /// Lays one note area's bodies out relative to a y=0 baseline, packing them onto shared rows for
        /// <c>footnote-display: inline</c>/<c>compact</c>, and returns the area's total reserved height
        /// alongside the bodies' own natural (content) height.
        /// </summary>
        private async ValueTask<(double TotalHeight, double NaturalContentHeight)> StackFootnoteBodies(
            RGraphics g, FootnoteAreaGroup area, FootnoteAreaRule areaRule)
        {
            var y = 0d;
            var rowStarted = false;
            var rowX = 0d;
            var rowHeight = 0d;

            foreach (var call in area.Calls)
            {
                    var displayMode = call.Body.FootnoteDisplay.Value;
                    double? naturalWidth = null;

                    if (displayMode is FootnoteDisplayMode.Inline or FootnoteDisplayMode.Compact)
                    {
                        // Measurement pass: lay the body out at the full content width first, to learn
                        // whether its own content fits on one line - and if so, exactly how wide that
                        // one line naturally is - before committing to its final row/position below.
                        // Safe to lay the same box out twice within one generation (see
                        // RunningElementLayout.LayoutRunningElementFor's own remarks on
                        // ResetRectanglesRecursively - this is exactly the reuse it documents).
                        var measureRect = new RRect(area.AreaLeft, 0, area.AreaWidth, 100_000);
                        await FootnoteBodyLayout.LayoutFootnoteBodyFor(g, call.Body, measureRect, this);

                        if (call.Body.LineBoxes.Count == 1 && call.Body.LineBoxes[0].Words.Count > 0)
                        {
                            // The line's own ContentRight/ContentLeft are the bounds it was WRAPPED
                            // against (the full available width), not where its content actually ends
                            // - the real "ink" extent is the span between the first and last word's own
                            // rendered edges, the same quantity ApplyCenterAlignment/ApplyRightAlignment
                            // read off a line's Words to compute their own alignment shift.
                            var line = call.Body.LineBoxes[0];
                            naturalWidth = line.Words[^1].Right - line.Words[0].Left;
                        }
                    }

                    // "compact" is inline only when the body is short enough to fit on one line at the
                    // full content width - the spec's own UA-discretion wording ("the user agent
                    // determines whether a given footnote element is placed as a block element or an
                    // inline element"), operationalized as "fits without wrapping": a body that already
                    // needed more than one line at full width gains nothing from a narrower one, so it
                    // takes its own full-width row regardless of the declared mode.
                    var packsInline = displayMode is FootnoteDisplayMode.Inline or FootnoteDisplayMode.Compact
                        && naturalWidth is { } natural && natural > 0 && natural <= area.AreaWidth;

                    var bodyWidth = packsInline ? naturalWidth!.Value : area.AreaWidth;

                    if (!packsInline || !rowStarted || rowX + bodyWidth > area.AreaWidth)
                    {
                        // Starts a new row: this body isn't inline-packable, no row is open yet, or the
                        // open row doesn't have enough width left for it.
                        if (rowStarted)
                        {
                            y += rowHeight + FootnoteBodySpacing;
                        }

                        rowX = 0;
                        rowHeight = 0;
                        rowStarted = true;
                    }

                    // Unconstrained height (a large finite sentinel, not double.MaxValue - the running-
                    // element layout path does incidental arithmetic on this rect that a true MaxValue
                    // could push to Infinity): a footnote body never fragments in this codebase (an
                    // accepted gap - see docs), so its natural, single-pass content height is exactly
                    // what's reserved, however tall that turns out to be.
                    var bodyRect = new RRect(area.AreaLeft + rowX, y, bodyWidth, 100_000);
                    await FootnoteBodyLayout.LayoutFootnoteBodyFor(g, call.Body, bodyRect, this);

                    rowHeight = Math.Max(rowHeight, call.Body.ActualBottom - call.Body.Location.Y);
                    rowX += bodyWidth + FootnoteBodySpacing;
                }

                if (rowStarted)
                {
                    y += rowHeight + FootnoteBodySpacing;
            }

            if (rowStarted)
            {
                y += rowHeight + FootnoteBodySpacing;
            }

            // The stacked bodies are the note area's content box; a declared @footnote height replaces
            // their natural height as the band reserved for them, so a short stack still reserves the
            // whole declared band (the slack falls below the last body) and a tall one overflows it.
            var naturalContentHeight = y - FootnoteBodySpacing;
            return (areaRule.TotalHeight(naturalContentHeight), naturalContentHeight);
        }

        /// <summary>
        /// Splits one pagination slot's footnote calls into the areas they belong to: the page's own note
        /// area, plus one per column any <c>float-reference: column</c> call landed in.
        /// </summary>
        /// <remarks>
        /// The returned groups partition <paramref name="calls"/> exactly - every call appears in one and
        /// only one. That matters beyond tidiness: <see cref="AttachFootnoteAreas"/> translates each body
        /// out of document space with a permanent, one-shot <c>OffsetTop</c>, so a call reachable twice
        /// would be translated twice and paint in the wrong place.
        /// </remarks>
        private List<FootnoteAreaGroup> PartitionFootnoteAreas(
            int slot,
            List<CssBoxFootnoteCall> calls,
            IReadOnlyDictionary<CssBoxFootnoteCall, (double Top, double Left)> callAnchors,
            double pageContentLeft,
            double pageContentWidth)
        {
            List<FootnoteAreaGroup> areas = [];
            FootnoteAreaGroup? pageArea = null;
            Dictionary<Fragmentation.ColumnAreaKey, FootnoteAreaGroup> columnAreas = [];

            foreach (var call in calls)
            {
                if (ColumnAreaFor(slot, call, callAnchors) is { } record)
                {
                    if (!columnAreas.TryGetValue(record.Key, out var columnArea))
                    {
                        columnArea = new FootnoteAreaGroup(
                            record.Key, record.InlineLeft, record.InlineRight - record.InlineLeft,
                            record.BandBottom, record.BandBottom - record.BandTop);
                        columnAreas[record.Key] = columnArea;
                        areas.Add(columnArea);
                    }

                    columnArea.Calls.Add(call);
                    continue;
                }

                if (pageArea is null)
                {
                    pageArea = new FootnoteAreaGroup(
                        Column: null, pageContentLeft, pageContentWidth,
                        PageBottomOf(slot), PageBottomOf(slot) - PageTopOf(slot));
                    areas.Add(pageArea);
                }

                pageArea.Calls.Add(call);
            }

            return areas;
        }

        /// <summary>
        /// The column <paramref name="call"/> belongs to, or null when it belongs to the page's own note
        /// area - which is the answer for <c>float-reference</c>'s other three values, and for a
        /// <c>column</c> call that turns out not to be inside a multi-column container after all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// css-page-floats says a <c>column</c> reference falls back to the anchor's line box when the
        /// anchor is not inside a column; for a footnote, which has no inline note area to fall back to,
        /// that degenerates to the page's. <c>inline</c> is the property's initial value, so it must mean
        /// today's behaviour or every existing footnote document changes, and <c>region</c> has nothing
        /// to resolve against since PeachPDF implements no part of CSS Regions.
        /// </para>
        /// <para>
        /// The match is by inline span as well as block band, never by block position alone: every column
        /// of a container shares one block band, so the call's own Y says nothing about which column it
        /// is in.
        /// </para>
        /// </remarks>
        private Fragmentation.ColumnFragmentainerRecord? ColumnAreaFor(
            int slot,
            CssBoxFootnoteCall call,
            IReadOnlyDictionary<CssBoxFootnoteCall, (double Top, double Left)> callAnchors)
        {
            if (call.Body.FloatReference.Value != FloatReference.Column) return null;
            if (!callAnchors.TryGetValue(call, out var anchor)) return null;

            return ColumnRecordAt(slot, anchor.Top, anchor.Left);
        }

        /// <summary>
        /// The column of pagination slot <paramref name="slot"/> whose inline span holds
        /// <paramref name="left"/> and whose block band holds <paramref name="top"/>, or null when the point
        /// is in no column.
        /// </summary>
        private Fragmentation.ColumnFragmentainerRecord? ColumnRecordAt(int slot, double top, double left)
        {
            if (ColumnFragmentainers.Count == 0) return null;
            if (double.IsNaN(left)) return null;

            foreach (var record in ColumnFragmentainers)
            {
                if (record.Slot != slot) continue;
                if (!record.ContainsInline(left)) continue;
                if (!record.ContainsBlock(top)) continue;

                return record;
            }

            return null;
        }

        /// <summary>
        /// Rebuilds one renumbered footnote box's words from its new text.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The bidi re-resolve is mandatory, not cosmetic: <c>BidiLevels</c> is indexed against whatever
        /// text was there before, so a number that merely changes width - "9" to "10", which continuous
        /// numbering reaches immediately and per-page numbering reaches on any page with ten notes -
        /// indexes past the end of that array inside <c>ParseToWords</c> and throws
        /// <see cref="IndexOutOfRangeException"/>.
        /// </para>
        /// <para>
        /// The emptiness guard mirrors <c>DomParser.DetachOneFootnoteBody</c>'s own: a <c>content</c>
        /// override resolving to an image or to <c>none</c> leaves <c>Text</c> null, and the word-building
        /// walk dereferences it - which threw <see cref="NullReferenceException"/> on every pass after the
        /// first before this guard existed.
        /// </para>
        /// </remarks>
        private static void ReparseFootnoteText(CssBox box)
        {
            if (string.IsNullOrEmpty(box.Text)) return;

            CssBidiParagraphResolver.ResolveOwnTextAsParagraph(box);
            box.ParseToWords();
        }

        /// <summary>
        /// This layout attempt's per-page footnote resolution. For every pagination slot at least one
        /// <see cref="CssBoxFootnoteCall"/> landed on (by its own, now-settled <c>Location.Y</c>):
        /// numbers its footnotes in document order starting at 1 (the UA default's implicit per-page
        /// reset - author <c>counter-reset</c>/<c>counter-increment: footnote</c> aren't supported, see
        /// the "Footnotes" section of docs/html-css-support.md), lays each footnote's body out against
        /// that page's own content width via <see cref="FootnoteBodyLayout.LayoutFootnoteBodyFor"/>
        /// stacked in document order, and positions the stacked group flush with that page's content-band
        /// bottom. Records the total reserved height per slot into <see cref="FootnoteAreaHeightsBySlot"/>
        /// for <see cref="LayoutDocument"/> to seed into that slot's own
        /// <see cref="Fragmentation.FragmentainerContext.ReserveBandEnd"/> on the *next* attempt - the
        /// same "resolve once layout settles, feed the result back in, repeat" shape
        /// <see cref="ResolveTargetPageContent"/>'s target-counter(_, page) loop already uses, and for the
        /// same reason: a footnote's own page assignment (this method's input) depends on how much room
        /// earlier footnotes on the same page already reserved (this method's output). Returns whether any
        /// slot's reserved height changed since the previous call, for <see cref="PerformLayout"/>'s
        /// footnote convergence loop to check.
        /// </summary>
        private async ValueTask<bool> ResolveFootnotesForThisAttempt(RGraphics g)
        {
            var previous = FootnoteAreaHeightsBySlot;
            var previousByColumn = FootnoteAreaHeightsByColumn;
            var current = new Dictionary<int, double>();
            var currentByColumn = new Dictionary<Fragmentation.ColumnAreaKey, double>();
            var areasBySlot = new Dictionary<int, List<FootnoteAreaGroup>>();

            // Cleared and re-decided fresh every pass, rather than left to accumulate or grow stale - see
            // the two fields' own remarks.
            foreach (var box in _footnotePolicyForcedBreakBoxes)
            {
                box.FootnotePolicyForcedBreakBefore = false;
            }
            _footnotePolicyForcedBreakBoxes.Clear();
            FootnotePolicyForcedLineCalls.Clear();
            var policyChanged = false;
            var numberingSignature = new StringBuilder();

            if (HasRealPageGrid)
            {
                var bySlot = new Dictionary<int, List<CssBoxFootnoteCall>>();
                var callAnchors = new Dictionary<CssBoxFootnoteCall, (double Top, double Left)>();

                foreach (var call in FootnoteCalls)
                {
                    if (!IsInRenderedTree(call)) continue;

                    // Not call.Location.Y - an in-flow inline box's own Location stays at a bogus
                    // line-local value layout never updates; its real document position lives in its
                    // per-line Rectangles (CssBox.OwnGeometryTop's own remarks).
                    //
                    // Both coordinates are captured HERE, in the one pass that runs before any call is
                    // renumbered. ApplyNumber re-parses the call's words, which replaces its CssRects with
                    // fresh ones sitting at their unset defaults until a later inline layout would place
                    // them - and none comes once this loop has exited. Reading the anchor afterwards
                    // therefore yields (0, 0), which silently attributed every column-scoped note to
                    // whichever column happened to contain the origin.
                    var anchor = (Top: call.OwnGeometryTop(), Left: call.OwnGeometryLeft());
                    callAnchors[call] = anchor;

                    var slot = PageIndexOf(anchor.Top);
                    if (!bySlot.TryGetValue(slot, out var list))
                        bySlot[slot] = list = [];
                    list.Add(call);
                }


                // Ascending slot order, and every slot up to the last one carrying a call - not
                // `foreach (var (slot, calls) in bySlot)`. A Dictionary enumerates in insertion order,
                // which is document order of *calls* and so not slot order once a footnote-policy forced
                // break has moved one; a running counter across pages makes that ordering load-bearing.
                // Visiting slots with no footnotes of their own matters too: a counter-reset declared on
                // such a page still takes effect there, as it would for any other counter.
                var maxSlot = bySlot.Count == 0 ? -1 : bySlot.Keys.Max();
                var runningNumber = 0;

                for (var slot = 0; slot <= maxSlot; slot++)
                {
                    if (ResolveFootnoteCounterResetForSlot(slot) is { } resetTo) runningNumber = resetTo;

                    if (!bySlot.TryGetValue(slot, out var calls)) continue;

                    var pageContentLeft = MarginLeft;
                    var pageContentWidth = PageContentRightOf(PageTopOf(slot)) - pageContentLeft;

                    // The step is a property of the page own @footnote rule, so it is resolved once per
                    // slot and applies to every note landing on it, wherever that note is placed.
                    var slotRule = ResolveFootnoteAreaRule(slot, pageContentWidth);

                    // Numbering runs over the slot calls in document order, BEFORE they are partitioned
                    // into areas: float-reference is a placement property, and nothing in css-page-floats
                    // makes it touch a counter. Numbering per column would also put two ones on a single
                    // page and, worse, let a call migrating between columns across convergence passes
                    // change its own number - turning a placement wobble into a text-content one that
                    // feeds back into line breaking.
                    foreach (var call in calls)
                    {
                        runningNumber += slotRule.Step;
                        ApplyFootnoteNumber(call, runningNumber, numberingSignature);
                    }

                    var areas = PartitionFootnoteAreas(slot, calls, callAnchors, pageContentLeft, pageContentWidth);
                    areasBySlot[slot] = areas;

                    foreach (var area in areas)
                    {
                        // A column area resolves @footnote against its own width, so a percentage
                        // margin/padding on the rule means what it says for the box it applies to.
                        var areaRule = area.Column is null ? slotRule : ResolveFootnoteAreaRule(slot, area.AreaWidth);

                        var (totalHeight, naturalContentHeight) = await StackFootnoteBodies(g, area, areaRule);
                        if (totalHeight <= 0) continue;

                        // css-gcpm-3 footnote-policy: "cannot be placed on the current page due to lack
                        // of space", operationalized three ways - an author-declared @footnote max-height,
                        // content overflowing an author-declared height, or the area own natural height
                        // alone already exceeding the band it sits in (the same extreme case the "auto"
                        // default already documents as overflowing). Deliberately NOT "does it leave room
                        // for the flow content already above it" - that would need reasoning about a call
                        // own position relative to content still being laid out around it. Only acted on
                        // for a call whose own footnote-policy asks for it below; "auto" is unaffected.
                        var usedContentHeight = areaRule.UsedContentHeight(naturalContentHeight);
                        var doesntFit = totalHeight > area.BandHeight
                                        // max-height and height refer to the same box (the content box),
                                        // so they are compared against the same quantity - otherwise
                                        // height: 50pt with max-height: 50pt would report an area that
                                        // cannot fit the band the author just sized exactly.
                                        || (areaRule.MaxHeight is { } maxH && usedContentHeight > maxH)
                                        || (areaRule.Height is { } fixedHeight && naturalContentHeight > fixedHeight + 0.01);

                        if (doesntFit)
                        {
                            foreach (var call in area.Calls)
                            {
                                switch (call.Body.FootnotePolicy.Value)
                                {
                                    case FootnotePolicyMode.Block:
                                        // AnchorForBreakBefore, not the containing block itself:
                                        // css-break-3 propagation means a forced break-before on a box
                                        // that is the first in-flow child of its own parent is taken by
                                        // that parent instead - an author break-before on the containing
                                        // block would be hoisted the same way, and this needs to travel
                                        // identically or a decorated wrapper around a single-paragraph
                                        // containing block would never itself relocate.
                                        var target = BreakPropagation.AnchorForBreakBefore(FootnotePolicyContainingBlockOf(call));
                                        if (_footnotePolicyForcedBreakBoxes.Add(target))
                                        {
                                            target.FootnotePolicyForcedBreakBefore = true;
                                            policyChanged = true;
                                        }
                                        break;
                                    case FootnotePolicyMode.Line:
                                        if (FootnotePolicyForcedLineCalls.Add(call))
                                        {
                                            policyChanged = true;
                                        }
                                        break;
                                }
                            }
                        }

                        // The stacking loop laid every body out relative to a y=0 baseline; translate the
                        // whole group down so the area own bottom edge lands flush with the band it
                        // belongs to - this page content-band bottom, or this column own band bottom.
                        var areaTop = area.BandBottom - totalHeight;
                        var finalBodiesTop = areaRule.BodiesTopFor(areaTop);
                        foreach (var call in area.Calls)
                        {
                            call.Body.OffsetTop(finalBodiesTop);
                        }

                        area.TotalHeight = totalHeight;
                        area.AreaTop = areaTop;
                        area.DividerTop = areaRule.DividerTopFor(areaTop);
                        area.DividerThickness = areaRule.DividerThickness;
                        area.DividerColor = areaRule.DividerColor;

                        if (area.Column is { } columnKey)
                        {
                            currentByColumn[columnKey] = totalHeight;
                        }
                        else
                        {
                            current[slot] = totalHeight;
                        }
                    }
                }
            }

            // Detects a state the loop has been in before and, from then on, only lets a reservation grow
            // (issue #1270) - see ColumnReservationGuard.
            _footnoteGuard.Apply(current, currentByColumn);

            // areasBySlot is simply empty when there is no real page grid, so this one assignment covers
            // both arms.
            _footnoteAreasBySlot = areasBySlot;
            FootnoteAreaHeightsBySlot = current;
            FootnoteAreaHeightsByColumn = currentByColumn;

            var previousNumbering = _footnoteNumberingSignature;
            _footnoteNumberingSignature = numberingSignature.ToString();

            // A number that changes width ("9" -> "10") changes the CALL's own inline width, which moves
            // the flow around it and so can move the call itself onto another page - but it need not
            // change any note area's reserved height at all, so the comparison below would see nothing
            // and let the loop exit a pass early with stale numbers. Same fixpoint discipline as the
            // target-counter loop's own text signature.
            if (!string.Equals(previousNumbering, _footnoteNumberingSignature, StringComparison.Ordinal)) return true;

            // policyChanged: a footnote-policy forced break was newly requested this pass, which a bare
            // height comparison would not otherwise catch - the request itself hasn't yet moved anything
            // (that happens on the NEXT LayoutDocument call), so this pass's own FootnoteAreaHeightsBySlot
            // can look identical to the previous pass's even though PerformLayout's convergence loop must
            // still re-enter LayoutDocument to act on it.
            if (policyChanged) return true;
            if (currentByColumn.Count != previousByColumn.Count) return true;
            foreach (var (key, height) in currentByColumn)
            {
                if (!previousByColumn.TryGetValue(key, out var previousHeight) || Math.Abs(previousHeight - height) > 0.01)
                    return true;
            }

            if (current.Count != previous.Count) return true;
            foreach (var (slot, height) in current)
            {
                if (!previous.TryGetValue(slot, out var previousHeight) || Math.Abs(previousHeight - height) > 0.01)
                    return true;
            }
            return false;
        }

        private readonly ColumnReservationGuard _footnoteGuard = new();
        private readonly ColumnReservationGuard _topFloatGuard = new();
        private readonly ColumnReservationGuard _bottomFloatGuard = new();

        /// <summary>
        /// Starts a fresh convergence run: forgets the states an earlier run visited and any floor it
        /// latched. Called once before <see cref="PerformLayout"/>'s footnote and page-float loop.
        /// </summary>
        private void BeginFootnoteConvergence()
        {
            _footnoteGuard.Begin();
            _topFloatGuard.Begin();
            _bottomFloatGuard.Begin();
        }

        /// <summary>
        /// Makes a loop terminate by construction when reserving room and re-flowing feed each other
        /// (issue #1270): the first time a state repeats that is not the previous one - a real cycle, not a
        /// settled loop - each column's reservation is held at the largest it was anywhere in the cycle, and can
        /// only grow after.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A column-scoped note area (<c>float-reference: column</c>) shortens its column, which can push the
        /// paragraph carrying the call into the next column; the reservation then moves there and the first
        /// column is long again, and the paragraph is pulled back. The loop's own six-pass cap ended that, but
        /// left the note area describing whichever state it happened to stop in, and a dense document of
        /// column notes hit the cap every time. A column-scoped page float's strip does the same to the
        /// paragraph before its anchor, so it gets one guard per edge.
        /// </para>
        /// <para>
        /// Holding a column's reservation once the paragraph has left it keeps the paragraph where it went, so
        /// the seeds are monotone and termination follows without reasoning about the column balancer: the
        /// floors only grow, and take values from a finite set (sums of the notes' or floats' heights). The
        /// cost is a blank strip in a column that lost its call or its float. Only column reservations are
        /// held. A page-level reservation does not take part in the feedback edge, and holding one would keep
        /// a state whose shape is gone: a column note is resolved against the page before the columns exist to
        /// route it, and that page-level area must not outlive the state it belonged to. Nothing changes
        /// before the first repeated state, so a document that settles on its own is laid out exactly as
        /// before; the six-pass cap remains as a backstop.
        /// </para>
        /// </remarks>
        private sealed class ColumnReservationGuard
        {
            private readonly List<(string Signature, Dictionary<Fragmentation.ColumnAreaKey, double> ByColumn)> _states = [];
            private bool _latched;
            private readonly Dictionary<Fragmentation.ColumnAreaKey, double> _floors = [];

            internal void Begin()
            {
                _states.Clear();
                _latched = false;
                _floors.Clear();
            }

            /// <summary>
            /// Records this pass's state and, once a cycle has been seen, raises <paramref name="byColumn"/> to the
            /// held floors. <paramref name="bySlot"/> only distinguishes states; it is never changed.
            /// </summary>
            internal void Apply(Dictionary<int, double> bySlot, Dictionary<Fragmentation.ColumnAreaKey, double> byColumn)
            {
                if (!_latched)
                {
                    var signature = Signature(bySlot, byColumn);
                    var firstVisit = _states.FindIndex(state => state.Signature == signature);

                    // The same state as the previous pass is a loop that has settled, not a cycle.
                    if (firstVisit < 0 || firstVisit == _states.Count - 1)
                    {
                        _states.Add((signature, new Dictionary<Fragmentation.ColumnAreaKey, double>(byColumn)));
                        return;
                    }

                    // The states from the first visit to now are the cycle; only its column reservations are held.
                    _latched = true;

                    for (var i = firstVisit; i < _states.Count; i++)
                    {
                        Raise(_states[i].ByColumn);
                    }
                }

                Raise(byColumn);

                foreach (var (key, floor) in _floors)
                {
                    byColumn[key] = Math.Max(byColumn.GetValueOrDefault(key), floor);
                }
            }

            private void Raise(Dictionary<Fragmentation.ColumnAreaKey, double> byColumn)
            {
                foreach (var (key, height) in byColumn)
                {
                    _floors[key] = Math.Max(_floors.GetValueOrDefault(key), height);
                }
            }

            private static string Signature(
                Dictionary<int, double> bySlot, Dictionary<Fragmentation.ColumnAreaKey, double> byColumn)
            {
                var signature = new StringBuilder();

                foreach (var (slot, height) in bySlot.OrderBy(kv => kv.Key))
                {
                    signature.Append('s').Append(slot).Append('=').Append(Math.Round(height, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                }

                foreach (var (key, height) in byColumn.OrderBy(kv => kv.Key.ToString(), StringComparer.Ordinal))
                {
                    signature.Append('c').Append(key).Append('=').Append(Math.Round(height, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                }

                return signature.ToString();
            }
        }

        /// <summary>
        /// This layout attempt's page-float resolution (css-page-floats' <c>float: top/bottom/top-bottom/
        /// snap</c>). For every pagination slot at least one page float's own, now-settled
        /// <see cref="CssBox.Location"/> landed on: decides which block edge each one belongs to, stacks
        /// same-edge floats in document order, and records each float's final position into
        /// <see cref="PageFloatPlacements"/> plus the total room each edge needs into
        /// <see cref="TopFloatAreaHeightsBySlot"/>/<see cref="BottomFloatAreaHeightsBySlot"/> for
        /// <see cref="LayoutDocument"/> to seed via <c>ReserveBandStart</c>/<c>ReserveBandEnd</c> on the
        /// *next* attempt - the same "resolve once layout settles, feed the result back in, repeat" shape
        /// <see cref="ResolveFootnotesForThisAttempt"/> already uses, and for the same reason: which edge
        /// a <c>top-bottom</c>/<c>snap</c> float belongs on, and how much room it needs, both depend on
        /// every page float's own measured height, which is only known once a full attempt has already
        /// laid it out.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unlike a footnote body, a page float is never detached from the tree and never rewritten by a
        /// separate layout pass of its own - the first attempt simply leaves it at the ordinary block-flow
        /// position <see cref="CssLayoutEngine.FloatBox"/> already resolved for it (the position it would
        /// have if <c>float</c> were <c>none</c>), which is exactly the position read here to discover
        /// which page it lands on. <see cref="CssLayoutEngine.FloatBoxPageArea"/> moves it to its final,
        /// decided position on the next attempt, before its own content lays out - so no synthetic
        /// containing block or detached re-layout is needed the way a footnote body's is.
        /// </para>
        /// <para>
        /// A <c>float-reference: column</c> float resolves against the column its anchor sits in
        /// (<see cref="ColumnRecordForPageFloat"/>) and everything else against the page, including the
        /// initial <c>inline</c> - see
        /// <c>.claude/accepted-gaps/page-float-keywords-ignore-an-inline-float-reference.md</c>.
        /// </para>
        /// </remarks>
        private bool ResolvePageFloatsForThisAttempt()
        {
            var previousTop = TopFloatAreaHeightsBySlot;
            var previousBottom = BottomFloatAreaHeightsBySlot;
            var previousTopByColumn = TopFloatAreaHeightsByColumn;
            var previousBottomByColumn = BottomFloatAreaHeightsByColumn;
            var previousPlacements = PageFloatPlacements;

            var currentTop = new Dictionary<int, double>();
            var currentBottom = new Dictionary<int, double>();
            var currentTopByColumn = new Dictionary<Fragmentation.ColumnAreaKey, double>();
            var currentBottomByColumn = new Dictionary<Fragmentation.ColumnAreaKey, double>();
            var currentPlacements = new Dictionary<CssBox, double>();

            if (HasRealPageGrid)
            {
                // One area per page slot that holds a page-referenced float, and one per column that holds a
                // column-referenced one (css-page-floats-3 section 3.1: a float goes to the edge of the
                // reference it names, here the column its anchor sits in).
                var pageAreas = new Dictionary<int, PageFloatArea>();
                var columnAreas = new Dictionary<Fragmentation.ColumnAreaKey, PageFloatArea>();

                foreach (var box in PageFloats)
                {
                    if (!IsInRenderedTree(box)) continue;

                    // SlotStartingAt, not PageIndexOf: Location.Y is a top edge (see CssBox's own floor-
                    // clamp, which reads the resulting slot the same way) - PageIndexOf applies no boundary
                    // convention and a top edge exactly on a boundary belongs to the slot it opens.
                    var slot = SlotStartingAt(box.Location.Y);

                    if (ColumnRecordForPageFloat(box) is { } column)
                    {
                        if (!columnAreas.TryGetValue(column.Key, out var columnArea))
                        {
                            columnAreas[column.Key] = columnArea = new PageFloatArea(
                                slot, column.Key, column.BandTop, column.BandBottom, ColumnFootnoteInsetFor(column.Key));
                        }

                        columnArea.Boxes.Add(box);
                        continue;
                    }

                    if (!pageAreas.TryGetValue(slot, out var pageArea))
                    {
                        // A footnote area on this same slot (if any) already claims room at the true bottom
                        // edge, resolved earlier this same attempt (ResolveFootnotesForThisAttempt runs first
                        // in PerformLayout's convergence loop) - page floats stack outside it (see the class
                        // remarks and TotalBandEndReservationFor), so both the top-bottom fit check and the
                        // bottom-edge anchor below have to know how much of the bottom it has already spent.
                        pageAreas[slot] = pageArea = new PageFloatArea(
                            slot, null, PageTopOf(slot), PageBottomOf(slot), FootnoteAreaHeightsBySlot.GetValueOrDefault(slot));
                    }

                    pageArea.Boxes.Add(box);
                }

                foreach (var area in pageAreas.Values.Concat(columnAreas.Values))
                {
                    var boxes = area.Boxes;
                    var pageTop = area.Top;
                    var pageBottom = area.Bottom;
                    var bandHeight = pageBottom - pageTop;
                    var footnoteHeight = area.FootnoteHeight;
                    var bottomEdge = pageBottom - footnoteHeight;

                    // First pass, in document order: decide which edge each float belongs to and total up
                    // both edges. top-bottom's "does it fit at top" and snap's "which edge is nearer" both
                    // need this walked in document order, since top-bottom's decision for one float depends
                    // on every earlier float in the same area (either edge) already having claimed its
                    // share - the check below is against the room actually left over both edges, not
                    // against the whole band as if nothing else in this area existed.
                    var atBottom = new Dictionary<CssBox, bool>();
                    double topTotal = 0, bottomTotal = 0;

                    // A float taller than the room the page has for one is not reserved for and is not
                    // stacked: an edge strip as tall as the band leaves no room for anything else on the page,
                    // so a reservation that large stalls the flow (see CssLayoutEngineColumns.FillColumns's
                    // note on the same shape), and a bottom placement would put its top above the page, where
                    // its first lines are drawn on no page at all (issue #1332). It starts at the top of its
                    // page instead and its content carries on past the page's foot like any tall block's,
                    // css-break-3 §4.4's "avoid losing content off the edge of the fragmentainer".
                    var oversized = new HashSet<CssBox>();

                    foreach (var box in boxes)
                    {
                        var height = Math.Max(0, box.ActualBottom - box.Location.Y);
                        if (height <= 0) continue;

                        if (height > bandHeight - footnoteHeight)
                        {
                            oversized.Add(box);
                            continue;
                        }

                        var placeAtBottom = box.Float.Value switch
                        {
                            Floating.Bottom => true,
                            Floating.Top => false,
                            // Prince's documented float-placement behavior for this historical keyword set
                            // (no current TR defines top-bottom/snap under these names - see
                            // docs/html-css-support.md's float row): try top; fall back to bottom once the
                            // top edge has no room left for it - "room left" meaning what remains of the
                            // whole band once every other top float, bottom float, and the footnote area
                            // already in this area have claimed theirs.
                            Floating.TopBottom => topTotal + height + bottomTotal + footnoteHeight > bandHeight,
                            // Whichever edge the float's own natural (static) position is nearer to.
                            Floating.Snap => (box.Location.Y - pageTop) > (pageBottom - box.Location.Y),
                            _ => false
                        };

                        atBottom[box] = placeAtBottom;
                        if (placeAtBottom) bottomTotal += height; else topTotal += height;
                    }

                    // Second pass: place each float within its edge's own strip, in document order - the
                    // first float on an edge sits closest to the flow content (the area's own top edge for
                    // a top float, the boundary with whatever else already reserves this area's bottom -
                    // a footnote area, see TotalBandEndReservationFor - for a bottom float), later floats
                    // on the same edge stack further from flow content, and the last one is flush with the
                    // area's own physical edge (or, for a bottom float, the footnote area's own top edge,
                    // via bottomEdge above).
                    double topRunning = 0, bottomRunning = 0;

                    foreach (var box in boxes)
                    {
                        var height = Math.Max(0, box.ActualBottom - box.Location.Y);
                        if (height <= 0) continue;

                        if (oversized.Contains(box))
                        {
                            currentPlacements[box] = pageTop;
                            continue;
                        }

                        if (atBottom[box])
                        {
                            currentPlacements[box] = bottomEdge - bottomTotal + bottomRunning;
                            bottomRunning += height;
                        }
                        else
                        {
                            currentPlacements[box] = pageTop + topRunning;
                            topRunning += height;
                        }
                    }

                    if (area.Column is { } key)
                    {
                        if (topTotal > 0) currentTopByColumn[key] = topTotal;
                        if (bottomTotal > 0) currentBottomByColumn[key] = bottomTotal;
                    }
                    else
                    {
                        if (topTotal > 0) currentTop[area.Slot] = topTotal;
                        if (bottomTotal > 0) currentBottom[area.Slot] = bottomTotal;
                    }
                }
            }

            // A strip that moves the paragraph before its float into the next column moves with it: the same
            // feedback a column note area has, held in check the same way (see ColumnReservationGuard).
            _topFloatGuard.Apply(currentTop, currentTopByColumn);
            _bottomFloatGuard.Apply(currentBottom, currentBottomByColumn);

            TopFloatAreaHeightsBySlot = currentTop;
            BottomFloatAreaHeightsBySlot = currentBottom;
            TopFloatAreaHeightsByColumn = currentTopByColumn;
            BottomFloatAreaHeightsByColumn = currentBottomByColumn;
            PageFloatPlacements = currentPlacements;

            var changed = DictionaryValuesChanged(previousTop, currentTop)
                          || DictionaryValuesChanged(previousBottom, currentBottom)
                          || DictionaryValuesChanged(previousTopByColumn, currentTopByColumn)
                          || DictionaryValuesChanged(previousBottomByColumn, currentBottomByColumn)
                          || DictionaryValuesChanged(previousPlacements, currentPlacements);

            return changed;
        }

        /// <summary>
        /// The page floats sharing one reference: a page slot's content area, or a column's band.
        /// <see cref="FootnoteHeight"/> is what a footnote area has already spent at the block-end edge of
        /// that same reference.
        /// </summary>
        private sealed class PageFloatArea(int slot, Fragmentation.ColumnAreaKey? column, double top, double bottom, double footnoteHeight)
        {
            internal int Slot { get; } = slot;
            internal Fragmentation.ColumnAreaKey? Column { get; } = column;
            internal double Top { get; } = top;
            internal double Bottom { get; } = bottom;
            internal double FootnoteHeight { get; } = footnoteHeight;
            internal List<CssBox> Boxes { get; } = [];
        }

        /// <summary>
        /// The column <paramref name="box"/> resolves against, or null when it resolves against the page -
        /// <c>float-reference</c>'s other values, and a <c>column</c> float that turns out not to be inside a
        /// multi-column container at all (css-page-floats-3 section 3.1: the reference then falls back to
        /// the one the float would otherwise have, which for a page float is the page).
        /// </summary>
        private Fragmentation.ColumnFragmentainerRecord? ColumnRecordForPageFloat(CssBox box)
        {
            if (!PageFloatColumns.TryGetValue(box, out var key)) return null;

            foreach (var record in ColumnFragmentainers)
            {
                if (record.Key == key) return record;
            }

            return null;
        }

        /// <summary>
        /// Whether <paramref name="current"/> differs from <paramref name="previous"/> - a different key
        /// count, or any shared key's value moved by more than the convergence-loop tolerance every
        /// resolver in this file settles for (footnote areas, page-float areas/placements) - for
        /// <see cref="PerformLayout"/>'s convergence loop to know whether another attempt is needed.
        /// </summary>
        private static bool DictionaryValuesChanged<TKey>(Dictionary<TKey, double> previous, Dictionary<TKey, double> current)
            where TKey : notnull
        {
            if (current.Count != previous.Count) return true;

            foreach (var (key, value) in current)
            {
                if (!previous.TryGetValue(key, out var previousValue) || Math.Abs(previousValue - value) > 0.01)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The nearest block-level ancestor of <paramref name="call"/>'s own structural position -
        /// css-gcpm-3 §2.8's "the paragraph that contains the footnote reference" for
        /// <c>footnote-policy: block</c>. Walks up from the call's real <see cref="CssBox.ParentBox"/>
        /// (not <see cref="CssBox.FootnoteSourceBox"/>, which is for selector re-matching only) rather than
        /// the call itself, since the call is always inline (<see cref="CssBoxFootnoteCall"/>'s own
        /// remarks) and so is never itself the answer.
        /// </summary>
        private static CssBox FootnotePolicyContainingBlockOf(CssBoxFootnoteCall call)
        {
            var box = call.ParentBox!;

            while (box.IsInline && box.ParentBox is { } parent)
            {
                box = parent;
            }

            return box;
        }

        /// <summary>
        /// Whether <paramref name="box"/> and every one of its ancestors are actually rendered (no
        /// <c>display: none</c> between it and the document root, and not on the box's own computed
        /// display either - an author <c>::footnote-call { display: none }</c> override reaches this too)
        /// - a footnote call that never lays out at all would otherwise have its stale/default
        /// <c>Location</c> misread as landing on whatever pagination slot that default coordinate happens
        /// to fall in.
        /// </summary>
        private static bool IsInRenderedTree(CssBox box)
        {
            if (box.DerivedStyle.ActualDisplay == Keywords.None) return false;

            for (var ancestor = box.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox)
            {
                if (ancestor.DerivedStyle.ActualDisplay == Keywords.None) return false;
            }

            return true;
        }

        /// <summary>
        /// Re-resolves <c>target-counter(_, page)</c> for every box <c>CssContentEngine.AppendTargetCounter</c>
        /// has ever flagged (<see cref="CssBox.HasPendingTargetPageContent"/>) against whatever
        /// <see cref="TargetPageMap"/> currently holds - the target-page convergence loop's per-round
        /// work. Mirrors <see cref="ReapplyPseudoElementContent"/>'s own "apply then re-parse words if
        /// there's text" contract; a leader-bearing box's content is fully handled inside
        /// <c>ApplyContent</c> itself (it calls <c>ParseToWordsWithLeaders</c> directly and leaves
        /// <c>Text</c> null), so the guard below naturally no-ops for it.
        /// </summary>
        private static void ResolveTargetPageContent(CssBox box)
        {
            if (box.HasPendingTargetPageContent)
            {
                CssContentEngine.ApplyContent(box);
                CssBidiParagraphResolver.ResolveOwnTextAsParagraph(box);
                if (!string.IsNullOrEmpty(box.Text))
                {
                    box.ParseToWords();
                }
            }

            foreach (var childBox in box.Boxes)
            {
                ResolveTargetPageContent(childBox);
            }
        }

        /// <summary>
        /// Separators <see cref="AppendTargetPageContentSignature"/> uses to keep one word's text from
        /// silently concatenating into its neighbour's (e.g. "1" + "2" needs to stay distinguishable from
        /// "12") and to mark a <see cref="CssRectLeader"/> (whose own <c>Text</c> is always null) as
        /// present. U+0001/U+0002 rather than a printable character since neither can appear in ordinary
        /// resolved content.
        /// </summary>
        private const string LeaderSignatureMarker = "\u0001";

        private const char SignatureSeparator = '\u0002';

        /// <summary>
        /// A cheap, order-sensitive signature of every <see cref="CssBox.HasPendingTargetPageContent"/>
        /// box's currently-resolved content, for the target-page convergence loop's fixpoint check
        /// (mirrors <see cref="PageAssignmentSignature"/>'s own "compare a signature, not the whole tree"
        /// shape). Built from <see cref="CssBox.Words"/> rather than <see cref="CssBox.Text"/> so it
        /// works uniformly whether or not the box also contains a <c>leader()</c> (which leaves
        /// <c>Text</c> permanently null - see <see cref="CssContentEngine.ApplyContent"/>).
        /// </summary>
        private static string TargetPageContentSignature(CssBox box)
        {
            var sb = new StringBuilder();
            AppendTargetPageContentSignature(box, sb);
            return sb.ToString();
        }

        private static void AppendTargetPageContentSignature(CssBox box, StringBuilder sb)
        {
            if (box.HasPendingTargetPageContent)
            {
                sb.Append(SignatureSeparator);
                foreach (var word in box.Words)
                {
                    sb.Append(word is CssRectLeader ? LeaderSignatureMarker : word.Text).Append(SignatureSeparator);
                }
            }

            foreach (var childBox in box.Boxes)
            {
                AppendTargetPageContentSignature(childBox, sb);
            }
        }

        /// <summary>
        /// Per-page paint-window override set by <c>PdfGenerator.AddPdfPages</c>'s page loop, so a
        /// page whose margins are overridden by a per-page <c>@page</c> rule (e.g. <c>:first { margin: 0 }</c>) gets a
        /// window matching its own margins instead of the base-margin <see cref="PageBoxRect"/>. This is
        /// the CONTENT clip - the page area inside the margins (css-page-3's own term for it), not a fixed
        /// box's own wider containing block (the page box, margins included, per CSS2.1 §10.1): a
        /// <c>position: fixed</c> box's own paint reaches past this via
        /// <c>RGraphics.SuspendClipping</c>, which always unwinds the whole clip stack down to the single
        /// unconditionally-infinite clip <see cref="PeachPDF.Adapters.GraphicsAdapter"/>'s constructor
        /// establishes before this is ever pushed - so this is the only page-level clip that needs to
        /// exist, provided it is pushed through <c>RGraphics.PushClip</c> and not intersected directly on
        /// the raw graphics object (see <c>FragmentPainter.Paint</c>'s own remarks, and issue #880).
        /// <see cref="PerformPaint"/> falls back to <see cref="PageBoxRect"/> when unset.
        /// </summary>
        internal RRect? PageClipOverride { get; set; }

        /// <summary>
        /// Words this render DREW and then truncated with a clip - see <see cref="PeachPDF.ClipReport"/>.
        /// </summary>
        /// <remarks>
        /// Per RENDER, not per page, which is why it lives here rather than on <c>FragmentPainter</c>:
        /// the painter is constructed fresh for each page (<see cref="PerformPaint"/>) and holds
        /// per-page state deliberately. <see cref="PageClipOverride"/> immediately above is the same
        /// shape of per-render slot in the other direction - written by <c>PdfGenerator</c>'s page loop
        /// and read by the painter; this one is written by the painter and drained by
        /// <c>PdfGenerator.AddPdfPages</c> once the loop is done.
        /// </remarks>
        internal PeachPDF.ClipReport ClipReport { get; } = new();

        /// <summary>
        /// The page/viewport rect, in the same fragmentainer-local coordinate space every painted
        /// fragment uses - the same rect <see cref="PerformPaint"/>
        /// pushes as the top-level page clip, also used as the background positioning area for a
        /// <c>background-attachment: fixed</c> layer (CSS Backgrounds 3 §3.9).
        /// </summary>
        internal RRect PageBoxRect => MaxSize.Height > 0
            ? new RRect(Location.X, Location.Y, Math.Min(MaxSize.Width, PageSize.Width),
                Math.Min(MaxSize.Height, PageSize.Height))
            : new RRect(MarginLeft, MarginTop, PageSize.Width + MarginRight, PageSize.Height);

        /// <summary>
        /// The zero-based pagination-slot index containing document Y-coordinate <paramref name="y"/>,
        /// on the "shifted grid" every page-boundary decision needs to agree on: slot <c>k</c> occupies
        /// <c>[k·PageSize.Height + MarginTop, (k+1)·PageSize.Height + MarginTop)</c>. This is the
        /// convention the painter's own per-page clip/translation (<c>PdfGenerator.AddPdfPages</c>) and
        /// the fragment-tree builder's own slot walk already uses - <see cref="PageSize"/>'s <c>Height</c> is
        /// already margin-free (<c>PdfGenerator.SetContent</c> subtracts both margins from the raw page
        /// height up front), so every page's real content band starts <see cref="MarginTop"/> past each
        /// raw multiple of <see cref="PageSize"/>'s height, not at the raw multiple itself. Callers must
        /// only invoke this when <see cref="PageSize"/>'s <c>Height</c> is a real, finite, positive
        /// value - unpaginated/measurement passes use a <c>double.MaxValue</c> sentinel and must guard
        /// around this the same way existing raw-<c>PageSize.Height</c> call sites already do.
        /// </summary>
        internal int PageIndexOf(double y) => UseVariablePageGeometry
            ? PageGeometry.PageIndexOf(y)
            : (int)((y - MarginTop) / PageSize.Height);

        /// <summary>
        /// The pagination slot a <b>top</b> edge at <paramref name="y"/> starts in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="PageIndexOf"/> applies no boundary convention: it answers about a coordinate, and a
        /// coordinate exactly on a slot boundary is ambiguous — it is both the end of one band and the
        /// start of the next. An <i>edge</i> is not ambiguous, and which way it resolves depends on which
        /// edge it is, so the two cases have a name each rather than a
        /// <see cref="PageBoundaryEpsilon"/> spelt out at every call site (there were a dozen, and getting
        /// the sign wrong lands a box a whole page out).
        /// </para>
        /// <para>
        /// A top edge flush on a boundary — or within the epsilon <i>above</i> it, which is the arithmetic
        /// noise a relocated or fragmented box's Y accumulates — begins the later slot: it is the first
        /// thing in that band, not the last thing in the one before.
        /// </para>
        /// </remarks>
        internal int SlotStartingAt(double y) => PageIndexOf(y + PageBoundaryEpsilon);

        /// <summary>
        /// The pagination slot a <b>bottom</b> edge at <paramref name="y"/> ends in.
        /// </summary>
        /// <remarks>
        /// The mirror of <see cref="SlotStartingAt"/>: a bottom edge flush on a boundary — or within the
        /// epsilon below it — ends the earlier slot, because content ending exactly at a band's bottom fits
        /// wholly inside that band and has not entered the next one.
        /// </remarks>
        internal int SlotEndingAt(double y) => PageIndexOf(y - PageBoundaryEpsilon);

        /// <summary>
        /// The block-axis band pagination slot <paramref name="slot"/> occupies.
        /// </summary>
        internal PageBand BandOfSlot(int slot) => new(PageTopOf(slot), PageBottomOf(slot));

        /// <summary>
        /// The band of the fragmentainer a <b>top</b> edge at <paramref name="y"/> starts in.
        /// </summary>
        internal PageBand BandStartingAt(double y) => BandOfSlot(SlotStartingAt(y));

        /// <summary>
        /// The band a bottom-edge straddle question about content beginning at <paramref name="top"/>
        /// is answered against: the fragmentainer <see cref="CurrentFragmentainer"/> is actually filling,
        /// per its own cursor (<c>FragmentainerContext.SlotIndex</c>) — <paramref name="gridBand"/> only
        /// where no fragmentainer is named (a measurement pass, per <see cref="DetachFragmentainer"/>),
        /// inside a column (which has a band of its own), or for the driver's suppressed last-resort pass
        /// (which may place content anywhere and must not break again).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Safe since <see href="https://github.com/jhaygood86/PeachPDF/issues/435">#435</see>'s stage 1
        /// closed every production mechanism that could leave the two disagreeing — an unforced §5.2
        /// flush placement, unbreakable word/block overflow, a table row-loop band jump, a flex/grid line
        /// relocation — each now steps <c>FragmentainerContext.StepOverTo</c> to match. Before that, this
        /// answered <paramref name="gridBand"/> unconditionally; converting it directly, without stage 1
        /// closing those mechanisms first, is recorded (in the issue and in
        /// <c>.claude/recent-fixes/</c>) as changing 63 of 69 showcases, with visibly overlapping content.
        /// </para>
        /// <para>
        /// <see cref="CursorSpills"/> remains wired here as the permanent regression guard: any future
        /// mechanism that reopens the gap this closes shows up as a non-zero count rather than a silent
        /// re-divergence between the two bands this method used to be able to return.
        /// </para>
        /// </remarks>
        internal PageBand BandBeingFilled(double top, PageBand gridBand)
        {
            if (CurrentFragmentainer is not { IsFragmenting: true, HasOwnBand: false } filling) return gridBand;

            // Diagnostic only - never a behavioural fallback. A mismatch here means either a mechanism
            // stage 1 did not close (there should be none left in production), or this word is exactly
            // the tolerance case stage 1 deliberately left for this conversion to turn into a break: its
            // own top has drifted into a later grid band while nothing advanced the cursor, and asking
            // FallsPast against filling.Band (not gridBand) is what makes that word straddle now.
            if (filling.SlotIndex != SlotStartingAt(top)) CursorSpills++;

            return filling.Band;
        }

        /// <summary>
        /// Whether a bottom edge at <paramref name="bottom"/> falls past <paramref name="band"/> — the
        /// one question "did this cross out of its fragmentainer?" is asked as.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Stated against a band rather than as a comparison of two slot indices, because a slot index is
        /// a fact about the page grid while a band is a fact about a <i>fragmentainer</i> — and a column
        /// has one of those and no slot of its own. This is the form both of
        /// <c>CssRect.WouldStraddleFragmentainer</c>'s arms can be written in, and the shape the rest of
        /// the block-flow path has to move to before a child's layout can stop reading its own absolute Y
        /// (see the staging in the fragmentainer-relative issue).
        /// </para>
        /// <para>
        /// The epsilon is <see cref="SlotEndingAt"/>'s convention, in coordinates: a bottom edge flush on
        /// the band's own bottom has not left it, and neither has one within the tolerance past it.
        /// </para>
        /// </remarks>
        internal static bool FallsPast(double bottom, PageBand band) =>
            bottom - PageBoundaryEpsilon >= band.Bottom;

        /// <summary>
        /// The document Y-coordinate of the content-top of pagination slot <paramref name="pageIndex"/>,
        /// per the same shifted-grid convention as <see cref="PageIndexOf"/> - the inverse operation.
        /// </summary>
        internal double PageTopOf(int pageIndex) => UseVariablePageGeometry
            ? PageGeometry.GetPage(pageIndex).Top
            : pageIndex * PageSize.Height + MarginTop;

        /// <summary>
        /// Whether the grid helpers consult the variable <see cref="PageGeometry"/> table instead of
        /// the closed-form uniform arithmetic: only when the page band is real (not the
        /// <c>double.MaxValue</c> measurement sentinel, not unset) AND some per-page <c>@page</c>
        /// rule actually overrides a top/bottom margin OR the sheet size itself - the vertical mirror of
        /// <see cref="UseVariableInlineMeasure"/>'s own reasoning: a size-only override (e.g. a landscape
        /// named page, which changes height as well as width, typically with no margin override at all)
        /// changes a slot's own band height exactly as a margin override does, so both have to gate the
        /// same variable-geometry table lookup or pagination silently falls back to the base band height
        /// for a slot whose sheet is genuinely a different size. Keeping uniform documents on the literal
        /// historical arithmetic eliminates any float-drift risk for the overwhelmingly common case.
        /// </summary>
        private bool UseVariablePageGeometry =>
            HasRealPageGrid && (PageGeometry.HasVerticalMarginOverrides || PageGeometry.HasSizeOverrides ||
                                 PageGeometry.HasVerticalBorderPaddingOverrides);

        /// <summary>
        /// The horizontal analogue of <see cref="UseVariablePageGeometry"/>: whether layout should
        /// re-wrap content to each page's own content-box width. True only when the page width is real
        /// (not the <c>double.MaxValue</c> measurement sentinel, not unset) AND some per-page
        /// <c>@page</c> rule actually overrides a left/right margin OR the sheet size itself (a mixed
        /// page-size/orientation document, e.g. a landscape page for a wide table) - either changes the
        /// page's own content-box width the same way, so both gate the same reflow. When false,
        /// <see cref="PageContentRightOf"/> returns the base measure and
        /// <see cref="CssLayoutEngine.GetBoxWidth(RGraphics, CssBox, double?)"/> runs its exact historical single-width arithmetic —
        /// zero change for the overwhelmingly common case.
        /// </summary>
        internal bool UseVariableInlineMeasure =>
            PageSize.Width > 0 && PageSize.Width < double.MaxValue - 1
            && (PageGeometry.HasHorizontalMarginOverrides || PageGeometry.HasSizeOverrides ||
                PageGeometry.HasHorizontalBorderPaddingOverrides);

        /// <summary>
        /// Whether the per-page reflow loop has settled which page each box is on, so a decision taken
        /// against a box's page is a decision about where it will actually be
        /// (<see cref="CssBox.OrphansAndWidowsMayMoveABreak"/>). Always true for a document with no
        /// per-page left/right margins, which has no such loop and therefore nothing provisional.
        /// </summary>
        /// <remarks>
        /// Per <see cref="PerformLayout"/> invocation, not per <see cref="LayoutDocument"/> one: the loop
        /// runs several layouts, and it is the loop rather than any one layout that settles the question.
        /// <c>ShrinkToFit</c> re-enters <see cref="PerformLayout"/> and so re-settles it from scratch.
        /// </remarks>
        internal bool PageWidthsSettled => !UseVariableInlineMeasure || _pageWidthsSettled;

        /// <inheritdoc cref="PageWidthsSettled"/>
        private bool _pageWidthsSettled;

        /// <summary>
        /// Whether content laid out for pagination slot <paramref name="from"/> would wrap identically in
        /// slot <paramref name="to"/> — that is, whether the two pages share one measure.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The final layout that takes the §5.4 corrections keys every width off the <i>settled</i>
        /// assignment, so a box it moves keeps the measure of the page it was measured for. Where the two
        /// pages share a measure that is exactly right and the correction is free; where they do not, taking
        /// it leaves the box wrapped to its old page's measure — the defect the reflow loop exists to
        /// remove, and a far worse one than the line minimum it was serving. So the correction is declined
        /// there instead, which is §4.3's own last rung: the constraint is given up rather than traded for a
        /// worse violation.
        /// </para>
        /// <para>
        /// <b>Measured, not reasoned.</b> This guard was written, argued away on the grounds that the
        /// retroactive whole-box push makes the same trade already, and put back when CI failed on
        /// <c>windows-latest</c> — a block wrapped to a full-bleed first page's 812pt measure, sitting on a
        /// 412pt page. The push does make that trade in principle, but it did not fire on the fixtures that
        /// caught this, and the break-point conversion does.
        /// </para>
        /// <para>
        /// Note mirrored inner/outer margins are <i>not</i> an instance: the two pages' content widths are
        /// equal and only the left origin differs, and it is the width the wrap depends on.
        /// </para>
        /// </remarks>
        internal bool MeasureIsSharedBetween(int from, int to) =>
            !UseVariableInlineMeasure
            || from == to
            || Math.Abs(PageContentRightOf(PageTopOf(from) + PageBoundaryEpsilon)
                        - PageContentRightOf(PageTopOf(to) + PageBoundaryEpsilon)) < 0.01;


        /// <summary>
        /// The document X-coordinate (layout px, at the <b>base</b> left origin) of the right edge of the
        /// content box on the pagination slot containing document Y <paramref name="y"/> — the per-page
        /// wrapping measure, the horizontal analogue of <see cref="PageBandHeightOf"/>. Content stays
        /// anchored at the base <see cref="MarginLeft"/> in layout space (exactly as the variable-height
        /// machinery keeps document space anchored at the base content origin); the painter's per-page
        /// <c>deltaX</c> translate then moves it to that page's own physical left edge. Because the width
        /// is already the page's own width, the right edge lands correctly with no extra paint work.
        /// When <see cref="UseVariableInlineMeasure"/> is off this returns the base
        /// <c>MarginLeft + PageSize.Width</c>, so callers can invoke it unconditionally.
        /// The per-page left/right margins come from the same <see cref="PageGeometry"/> table layout
        /// paginated against; they resolve in true points, scaled into layout space by
        /// <c>PixelsPerPoint</c> exactly once (issue-#113 discipline).
        /// </summary>
        internal double PageContentRightOf(double y)
        {
            if (!UseVariableInlineMeasure)
                return MarginLeft + PageSize.Width;

            // The slot's own resolved band width (including its own degenerate-override fallback) lives on
            // PageGeometryTable.PageBandGeometry.BandWidth now - content stays anchored at the base
            // MarginLeft in layout space (the painter's per-page deltaX translate moves it to the page's
            // own physical left edge later), so this method's own contribution is just that anchoring.
            return MarginLeft + PageGeometry.GetPage(PageIndexOf(y)).BandWidth;
        }

        /// <summary>
        /// Tolerance for comparing a document Y-coordinate against a pagination-slot boundary — the
        /// several arithmetic steps a relocated/fragmented box's Y goes through can land it a hair on
        /// either side of the exact boundary value (same class of noise <c>MarginBoxRenderer</c>'s
        /// named-string page attribution guards against, which shares this constant).
        /// </summary>
        internal const double PageBoundaryEpsilon = 0.5;

        /// <summary>
        /// Whether the page grid describes real fragmentainers, as against the
        /// <see cref="double.MaxValue"/> page-height sentinel an unpaginated measurement pass uses.
        /// </summary>
        /// <remarks>
        /// The "same sentinel caveat" the grid accessors above each mention, stated once. Every caller
        /// that turns a coordinate into a slot and back — rather than merely reading a slot's geometry —
        /// has to ask this first: under the sentinel every Y maps to slot 0, so
        /// <see cref="PageTopOf"/><c>(1)</c> is not a coordinate any box can be placed at.
        /// </remarks>
        internal bool HasRealPageGrid => PageSize.Height is > 0 and < double.MaxValue - 1;

        /// <summary>
        /// The content-band height of pagination slot <paramref name="pageIndex"/>. On the uniform
        /// grid every slot's band is <see cref="PageSize"/>'s <c>Height</c>; kept as a per-slot lookup
        /// so per-page <c>@page</c> margin geometry has a single seam to vary it through. Same
        /// sentinel caveat as <see cref="PageIndexOf"/>.
        /// </summary>
        internal double PageBandHeightOf(int pageIndex) => UseVariablePageGeometry
            ? PageGeometry.GetPage(pageIndex).BandHeight
            : PageSize.Height;

        /// <summary>
        /// The whole page box's height — the sheet, its margins included — in layout space.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="PageBandHeightOf"/> answers "how much room does content get here", which varies per
        /// slot once per-page <c>@page</c> margins do. This answers "how big is the page", which does not:
        /// only the margins vary, and the sheet is what they are taken out of. So there is no slot
        /// parameter, and <c>PageGeometryTable</c> recovers the same quantity the same way when it needs a
        /// band — <c>PdfGenerator.SetContent</c> subtracts both margins from the sheet, and the public
        /// wrappers scale <see cref="PageSize"/> and the margins by the same <c>PixelsPerPoint</c>.
        /// </para>
        /// <para>
        /// Wanted where a specification says "the page height" and means the page box rather than the
        /// content area — css-tables-3 §6.2's cap on repeating a <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>
        /// is the one caller today. Same sentinel caveat as <see cref="PageIndexOf"/>: ask
        /// <see cref="HasRealPageGrid"/> first.
        /// </para>
        /// </remarks>
        internal double PageSheetHeight => PageSize.Height + MarginTop + MarginBottom;

        /// <summary>The whole page box's width, in layout space - the horizontal mirror of <see cref="PageSheetHeight"/>.</summary>
        internal double PageSheetWidth => PageSize.Width + MarginLeft + MarginRight;

        /// <summary>
        /// The document Y-coordinate one past the bottom of pagination slot <paramref name="pageIndex"/>'s
        /// content band — bands are contiguous, so this equals <see cref="PageTopOf"/> of the next slot.
        /// </summary>
        internal double PageBottomOf(int pageIndex) => PageTopOf(pageIndex) + PageBandHeightOf(pageIndex);

        /// <summary>
        /// The document Y-coordinate of the content-top of the pagination slot after the one containing
        /// <paramref name="y"/> — the universal "start of the next page" target every page-break
        /// relocation uses. Same sentinel caveat as <see cref="PageIndexOf"/>.
        /// </summary>
        internal double NextPageTopOf(double y) => PageTopOf(PageIndexOf(y) + 1);

        /// <summary>
        /// Paints one fragmentainer — one page. Everything painted comes from
        /// <paramref name="fragmentainer"/>'s fragment subtree, whose coordinates are already local to
        /// this page, so the painter never consults the box tree for geometry.
        /// </summary>
        /// <param name="g">the device to use</param>
        /// <param name="fragmentainer">the page to paint</param>
        public void PerformPaint(RGraphics g, FragmentainerFragment fragmentainer)
        {
            ArgumentNullException.ThrowIfNull(g);
            ArgumentNullException.ThrowIfNull(fragmentainer);

            // A painter instance owns one page's paint, so all per-page paint state lives and dies
            // with it rather than on the container or the box tree.
            new FragmentPainter(this).Paint(g, fragmentainer);
        }

        /// <summary>
        /// Given the list of available media types, returns the "best" one
        /// </summary>
        /// <param name="mediaTypesAvailable"></param>
        /// <returns></returns>
        internal string GetCssMediaType(IEnumerable<string> mediaTypesAvailable)
        {
            return Adapter.GetCssMediaType(mediaTypesAvailable);
        }

        /// <summary>
        /// Builds the exception that fails the render — <paramref name="exception"/> (if any) wrapped in
        /// an <see cref="HtmlRenderException"/> naming the pipeline phase it was raised from. Always used
        /// as <c>throw container.RenderError(…)</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It <b>returns</b> the exception rather than throwing it, so that every call site has to spell
        /// the <c>throw</c> out. This was called <c>ReportError</c> and threw: a name that reads as
        /// advisory, on a method after which no statement can run. Two callers were written expecting it
        /// to return — the driver's own no-progress recovery
        /// (<see cref="LayoutTheRemainderMonolithically"/>, which had therefore never once run) and
        /// <c>StylesheetLoadHandler.LoadStylesheet</c>'s failure return — and the compiler flagged
        /// neither, because it does not treat <c>[DoesNotReturn]</c> as ending a code path. After a
        /// literal <c>throw</c> it does, so that whole class of dead code is now a build warning.
        /// </para>
        /// <para>
        /// PeachPDF has no non-fatal diagnostic channel: a condition that must not fail the render is
        /// degraded silently instead, as the recovery above and <c>SvgTreeBuilder</c>'s image prefetch
        /// both do.
        /// </para>
        /// <para>
        /// Several callers reach this through <c>if (box.HtmlContainer is { } container) throw …</c>. The
        /// guard is a <i>policy</i> rather than a dependency — nothing here reads the container — and the
        /// policy is that a box with no container swallows its failure, which is the behaviour those
        /// call sites have always had through <c>?.</c>.
        /// </para>
        /// </remarks>
        /// <param name="type">the pipeline phase the error was raised from</param>
        /// <param name="message">the error message</param>
        /// <param name="exception">optional: the exception that occured</param>
        internal HtmlRenderException RenderError(HtmlRenderErrorType type, string message, Exception? exception = null)
        {
            return new HtmlRenderException(message, type, exception);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            Dispose(true);
        }


        #region Private methods

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        private void Dispose(bool all)
        {
            try
            {
                CssData = null;
                Root?.Dispose();
                Root = null;

                foreach (var shell in DisplayContentsShells) shell.Dispose();
                DisplayContentsShells.Clear();
            }
            catch
            { }

            // Separate from the box-tree disposal above: an exception walking Root must not skip releasing
            // the cached images, which are owned here regardless of how the box tree fared. Each image is
            // disposed in its own try/catch, not one try around the whole loop, so one image throwing
            // can't stop the rest of the loop from running - the dictionary is cleared in finally either way.
            try
            {
                foreach (var (image, _) in _resolvedImageResources.Values)
                {
                    try
                    {
                        image?.Dispose();
                    }
                    catch
                    { }
                }
            }
            finally
            {
                _resolvedImageResources.Clear();
            }
        }

        #endregion
    }
}