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
using PeachPDF.Html.Core.Paint;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
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
        /// (<see cref="Dom.CssRect.BreakPage"/>, which permanently jumps a replaced-element word's
        /// <c>Top</c> to the next page) is skipped. Set around a throwaway/measurement-only layout
        /// pass performed at a temporary, not-yet-final position - see
        /// <see cref="Dom.CssLayoutEngineFlex"/>'s <c>MeasureItem</c>/<c>ResizeItem</c>/stretch
        /// cross-size re-layout, which lay a flex item out at <c>(ClientLeft, ClientTop)</c> purely
        /// to measure its natural size before <c>AssignLocations</c> translates it (via
        /// <c>CssBox.OffsetTop</c>) to its real final position. Without this, a word whose temporary
        /// position happens to straddle a page boundary gets pushed to the next page during
        /// measurement - a decision based on coordinates that have nothing to do with where the item
        /// actually ends up - and that jump silently survives the later translation (which only
        /// shifts by a delta computed from the box's own <c>Location</c>, not from where its words
        /// independently drifted to), permanently desyncing the word from its own box.
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
        /// Defensive backstop on the driver loop, mirroring the cap the fragment-tree slot walk uses.
        /// </summary>
        private const int MaxFragmentainers = 100_000;

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
        /// Whether any box in the current document asks for <c>box-decoration-break: clone</c>. Reserving room
        /// for a cloned border and padding at a break (css-break-3 §6.2) means asking, for a great many words,
        /// what a box's ancestors declared; this settles the answer once so a document that clones nothing —
        /// almost every document — never pays for the walk.
        /// </summary>
        internal bool HasCloneDecorations { get; private set; }

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
        /// Init with optional document and stylesheet.
        /// </summary>
        /// <param name="htmlSource">the html to init with, init empty if not given</param>
        /// <param name="baseCssData">optional: the stylesheet to init with, init default if not given</param>
        public async Task SetHtml(string htmlSource, CssData? baseCssData = null)
        {
            Clear();
            if (string.IsNullOrEmpty(htmlSource)) return;

            CssData = baseCssData ?? await Adapter.GetDefaultCssData();

            DomParser parser = new(CssParser);
            (Root, CssData, DocumentMetadata) = await parser.GenerateCssTree(htmlSource, this, CssData);
        }

        /// <summary>
        /// Clear the content of the HTML container releasing any resources used to render previously existing content.
        /// </summary>
        public void Clear()
        {
            if (Root == null) return;

            Root.Dispose();
            Root = null;
            DocumentLanguage = null;
            ClearNamedStrings();
            ClearNamedPageElements();
            // New content means new @page rules: drop cached slot geometry, the vertical-override
            // scan, and the captured relative-unit context so nothing consults the previous
            // document's bands before the next layout pass (which resets again defensively).
            PageGeometry.Reset();
            PageLengthContext = null;
            // Keyed by CssBox, so dropping the tree without this would retain every box in it.
            ClearBlankSlotReservations();
        }

        /// <summary>
        /// Get all the links in the HTML with the element rectangle and href data.
        /// </summary>
        /// <returns>collection of all the links in the HTML</returns>
        public List<LinkElementData<RRect>> GetLinks()
        {
            var linkBoxes = new List<CssBox>();
            DomUtils.GetAllLinkBoxes(Root, linkBoxes);

            var linkElements = new List<LinkElementData<RRect>>();
            foreach (var box in linkBoxes)
            {
                linkElements.Add(new LinkElementData<RRect>(box.GetAttribute("id"), box.GetAttribute("href"), CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds), box));
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
            return box != null ? CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds) : (RRect?)null;
        }

        /// <summary>
        /// Measures the bounds of box and children, recursively.
        /// </summary>
        /// <param name="g">Device context to draw</param>
        public async ValueTask PerformLayout(RGraphics g)
        {
            ArgumentNullException.ThrowIfNull(g);

            ActualSize = RSize.Empty;
            FloatScanCalls = 0;
            FloatScanBoxVisits = 0;
            if (Root is null) return;

            // A document with per-page left/right margins settles its box->page assignment in the reflow
            // loop below, and until that loop has run, a box's page is provisional.
            _pageWidthsSettled = false;

            // includeStackingHoistCandidates: false here - IsStackingContextBox reads IsTransformed,
            // which lazily computes and permanently caches ActualTransformMatrix against this box's own
            // border-box size on first access. Before layout, every box's size is still unset/default,
            // so triggering that computation this early would cache a wrong transform matrix forever
            // (this box never gets asked for its transform again once the cache is populated) - see the
            // "actualTransformComputed" cache in CssBoxProperties.ActualTransformMatrix.
            (HasFloatedBoxes, HasOutOfFlowBoxes, _) = ComputeFlowFlags(Root, includeStackingHoistCandidates: false);

            // Depends on cascaded style only, and layout itself consults it, so it has to be settled before
            // the first pass rather than alongside the post-layout flags below.
            HasCloneDecorations = DomUtils.AnyBoxClonesDecorations(Root);

            // Also purely cascade-driven, and also needed *during* the passes now that fragments are emitted
            // as each one ends: CSS Paged Media 3 §3.2's content-empty-page rule reads
            // SuppressOwnBackgroundPaint, and resolving the canvas background afterwards would make the
            // promoted root background look like real content spanning the whole document. (Acid2 grew 2->3
            // pages the last time the ordering of these two was wrong.)
            ResolveCanvasBackground();

            // if width is not restricted we set it to large value to get the actual later
            Root.Size = new RSize(MaxSize.Width > 0 ? MaxSize.Width : PageSize.Width, 0);
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
            // box's own page was yet known. Re-run layout so each auto-width box now keys its width off
            // its own page via its previous-pass Location.Y (GetBoxWidth -> PageContentRightOf), and
            // repeat until the box->page assignment stops changing. For :first/:left/:right the bands are
            // content-independent, so this converges in a single re-pass; the small cap bounds the
            // deferred width->height->page-name feedback case (named-page L/R reflow - an accepted gap).
            if (UseVariablePageWidth)
            {
                // The base seed the "real" pass(es) above used - NOT the current Root.Size.Width, which
                // GetBoxWidth has already replaced with page 0's own (seam-adjusted) measure.
                var rootWidth = MaxSize.Width > 0 ? MaxSize.Width : Math.Ceiling(ActualSize.Width);
                var previous = PageAssignmentSignature();
                for (var i = 0; i < 3; i++)
                {
                    Root.Size = new RSize(rootWidth, 0);
                    Root.Location = Location;
                    ActualSize = RSize.Empty;
                    await LayoutDocument(g);

                    var current = PageAssignmentSignature();
                    if (current.SequenceEqual(previous)) break;
                    previous = current;
                }

                // css-break-3 §5.4's two line minimums are decided here rather than inside the loop above.
                // A break they move changes which page a box lands on, and every box's width is keyed off
                // the page it landed on last time - so taken while the loop is still settling, the decision
                // feeds back into the very thing the loop is trying to settle, and the loop stops
                // converging (measured on windows-latest, whose font metrics put the fixture at a different
                // boundary: a paragraph left wrapped to a neighbouring page's measure, which is a far more
                // visible defect than the orphan it was avoiding).
                //
                // One final layout, entered once the assignment has settled, has no such feedback: every
                // width in it comes from the settled assignment, and nothing re-runs afterwards to be
                // disturbed by what the corrections move.
                _pageWidthsSettled = true;

                Root.Size = new RSize(rootWidth, 0);
                Root.Location = Location;
                ActualSize = RSize.Empty;
                await LayoutDocument(g);
            }

            // After layout, re-apply content to pseudo-elements now that named strings are set
            ReapplyPseudoElementContent(Root);

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
            FragmentTree = _emitter?.Finish() ?? new FragmentTree([]);
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
                side = BreakValues.RequiredSide(null, box.BreakAfter);
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

                if (child.Display == CssConstants.None
                    || child.Position is CssConstants.Absolute or CssConstants.Fixed
                    || child.IsFloated)
                {
                    continue;
                }

                last = LastInFlowDescendant(child) ?? child;
                break;
            }

            return last;
        }

        /// <summary>
        /// Lays the document out once, filling one fragmentainer at a time: a pass targets a
        /// fragmentainer, and where content does not fit it records where it stopped so the next pass
        /// can resume from exactly that point
        /// (<see href="https://www.w3.org/TR/css-break-3/#breaking-controls">CSS Fragmentation Level 3
        /// §2/§4.4</see>).
        /// </summary>
        /// <remarks>
        /// This is the atom the three re-layout loops in <see cref="PerformLayout"/> and
        /// <c>PdfGenerator</c>'s <c>ShrinkToFit</c> pass all share. The named-page registry and page
        /// geometry table are reset here, once per invocation and never per fragmentainer — a
        /// document's registrations accumulate <i>across</i> its fragmentainers, and only a whole new
        /// layout invalidates them.
        /// </remarks>
        private async ValueTask LayoutDocument(RGraphics g)
        {
            LayoutGeneration++;
            FragmentainerPasses = 0;
            LastResortRelayouts = 0;
            PassRewinds = 0;

            // Registrations append (they aren't idempotent), so without this each re-layout accumulated
            // duplicates and began with a stale ActivePageName from the PREVIOUS invocation's last
            // element, which could spuriously force or suppress the first named-page break. Blank-slot
            // reservations go the same way: each invocation re-decides every one of them from scratch,
            // and only the last invocation's set describes the layout the fragment tree is built from.
            ClearNamedPageElements();
            PageGeometry.Reset();
            ClearBlankSlotReservations();

            // Both per layout, not per document: ShrinkToFit and the per-page reflow loop each re-run this
            // method, and a record kept across them would describe passes that no longer exist while the
            // latch silently disabled the correction on every layout after the first.
            _passEntries.Clear();
            _passEntrySet.Clear();
            _passesRewoundFor.Clear();

            // Each invocation re-decides the whole document, so the fragments an earlier one emitted
            // describe a layout that no longer exists.
            var emitter = new FragmentEmitter(this);
            _emitter = emitter;

            BreakToken? token = null;

            try
            {
                var slot = 0;

                for (var pass = 0; pass < MaxFragmentainers; pass++)
                {
                    var context = new FragmentainerContext(this, Root!, slot);
                    CurrentFragmentainer = context;

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
                        continue;
                    }

                    _widowsRewind = null;

                    // §4.3's keep-with-next run pull reaches back further than one pass: the run head was
                    // placed in a fragmentainer the driver has already left, so the pass that placed it is
                    // re-entered rather than this one.
                    if (_runPullRewind is { } pull && TryRewindForRunPull(pull, ref token, ref slot))
                    {
                        _runPullRewind = null;
                        continue;
                    }

                    _runPullRewind = null;

                    var next = context.OutgoingToken;

                    if (next is null)
                    {
                        emitter.EmitPass(slot, LastSlotAnyGeometryTouches(context.SlotIndex), token, null);
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
        /// fragmentainer it was placed in.
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
                || PassEntryFilling(SlotStartingAt(head.Location.Y)) < 0)
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
            var entry = PassEntryFilling(SlotStartingAt(pull.Head.Location.Y));

            if (entry < 0) return false;

            var (rewoundSlot, rewoundToken) = _passEntries[entry];

            _passesRewoundFor.Add(pull.Head);
            PassRewinds++;
            _emitter?.InvalidateFrom(rewoundSlot);

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
        /// Three things have to be undone, which is #374's lesson in a different shape. The box's own line
        /// boxes past the budget go (<see cref="CssBox.DiscardLineBoxesFrom"/>, which also un-places their
        /// words, since the retry need not reach every one of them again). The fragment the earlier pass
        /// froze goes, because it is about to describe fewer lines than it did. And the resumption record
        /// the pass was entered with is rebuilt to name the budget, since that record is the only thing that
        /// says where the flow picks up.
        /// </para>
        /// <para>
        /// Deliberately <b>narrower</b> than <see cref="PassRewind.RollBackTo"/>, which the other two pass
        /// re-entries use: what the pass placed after the box is left exactly where it is, rather than
        /// being reset and laid out again. Widening it to the shared rollback was tried and <i>lost
        /// content</i> — 16 words of <c>paged_media_horizontal_reflow</c> — so what it would take is a
        /// question of its own (issue #440).
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
            rewind.Box.DiscardLineBoxesFrom(rewind.Budget);

            token = rebuilt;
            return true;
        }

        /// <summary>
        /// The same resumption chain with the inline link for <paramref name="box"/> resuming at line
        /// <paramref name="budget"/> instead of wherever it did, or false when the chain does not end in
        /// that box's inline flow.
        /// </summary>
        private static bool TryRebuildForBudget(
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
                        LinesKeptHere = budget - (inline.CompletedLineCount - inline.LinesKeptHere)
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
        internal void InvalidateEmittedFragmentsFor(CssBox box, double documentY)
        {
            // The box question first, deliberately: this runs on every block-axis reposition in the document,
            // and PageIndexOf under a per-page @page geometry table is a forward-incremental walk rather than
            // arithmetic. Nothing should be asked of the page grid for a box that cannot need re-emitting.
            if (_emitter is null || !_emitter.HoldsFragmentsFor(box) || !HasRealPageGrid) return;

            _emitter.InvalidateFrom(PageIndexOf(documentY));
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
        internal void RecordNestedFragmentainer(
            CssBox contextRoot,
            int slot,
            (double Top, double Bottom) band,
            (double Left, double Right) inline,
            BoxGeometrySnapshot geometry,
            IReadOnlySet<CssBox> continuing) =>
            _emitter?.RecordNestedFragmentainer(contextRoot, slot, band, inline, geometry, continuing);

        /// <summary>
        /// Discards what <paramref name="contextRoot"/> recorded in <paramref name="slot"/> — or, with no
        /// slot, in every slot — for a fill being attempted afresh.
        /// </summary>
        internal void ClearNestedFragmentainers(CssBox contextRoot, int? slot = null) =>
            _emitter?.ClearNestedFragmentainers(contextRoot, slot);

        /// <summary>
        /// The box whose background fills the whole page canvas, per
        /// <see href="https://www.w3.org/TR/CSS21/colors.html#background">CSS 2.1 §14.2</see>:
        /// <c>body</c>'s background if it declares one, else <c>html</c>'s, else none. Null when
        /// neither declares a background.
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
            var body = DomUtils.GetBoxByTagName(Root, "body");

            // Cleared first so a re-layout can never leave a previous winner suppressed.
            if (html is not null) html.SuppressOwnBackgroundPaint = false;
            if (body is not null) body.SuppressOwnBackgroundPaint = false;

            CanvasBackgroundBox = body is { HasOwnBackground: true } ? body
                : html is { HasOwnBackground: true } ? html
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

            foreach (var childBox in box.Boxes)
            {
                if (hasFloated && hasOutOfFlow && (!includeStackingHoistCandidates || hasStackingHoistCandidates))
                    return;
                ComputeFlowFlags(childBox, false, includeStackingHoistCandidates,
                    ref hasFloated, ref hasOutOfFlow, ref hasStackingHoistCandidates);
            }
        }

        /// <summary>
        /// A snapshot of every box's pagination-slot assignment (<see cref="PageIndexOf"/> of its
        /// laid-out top) in a fixed tree-walk order, used by <see cref="PerformLayout"/>'s per-page
        /// horizontal-reflow loop to detect a fixpoint: once a re-pass leaves every box on the same page
        /// it was on before, the per-page widths it resolved against are self-consistent and the loop
        /// stops. Rebuilt from scratch each call — the loop runs at most a few iterations, and only for
        /// the rare document that carries a per-page left/right <c>@page</c> margin override.
        /// </summary>
        private List<int> PageAssignmentSignature()
        {
            List<int> signature = [];
            if (Root is not null)
                CollectPageAssignments(Root, signature);
            return signature;
        }

        private void CollectPageAssignments(CssBox box, List<int> signature)
        {
            signature.Add(PageIndexOf(box.Location.Y));
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
                if (childBox.IsPseudoElement && !string.IsNullOrEmpty(childBox.Content) && childBox.Content != CssConstants.None && childBox.Content != CssConstants.Normal)
                {
                    // Check if content contains string() function
                    if (childBox.Content.Contains("string("))
                    {
                        CssContentEngine.ApplyContent(childBox);
                        // Re-parse words after content changes
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
        /// Per-page paint-window override set by <c>PdfGenerator.AddPdfPages</c>'s page loop, so a
        /// page whose margins are overridden by a per-page <c>@page</c> rule (e.g. <c>:first { margin: 0 }</c>) gets a
        /// window matching its own margins instead of the base-margin <see cref="PageBoxRect"/>.
        /// <see cref="PerformPaint"/> falls back to <see cref="PageBoxRect"/> when unset.
        /// </summary>
        internal RRect? PageClipOverride { get; set; }

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
        /// rule actually overrides a top/bottom margin. Keeping uniform documents on the literal
        /// historical arithmetic eliminates any float-drift risk for the overwhelmingly common case.
        /// </summary>
        private bool UseVariablePageGeometry =>
            HasRealPageGrid && PageGeometry.HasVerticalMarginOverrides;

        /// <summary>
        /// The horizontal analogue of <see cref="UseVariablePageGeometry"/>: whether layout should
        /// re-wrap content to each page's own content-box width. True only when the page width is real
        /// (not the <c>double.MaxValue</c> measurement sentinel, not unset) AND some per-page
        /// <c>@page</c> rule actually overrides a left/right margin. When false, <see cref="PageContentRightOf"/>
        /// returns the base measure and <see cref="CssLayoutEngine.GetBoxWidth"/> runs its exact
        /// historical single-width arithmetic — zero change for the overwhelmingly common case.
        /// </summary>
        internal bool UseVariablePageWidth =>
            PageSize.Width > 0 && PageSize.Width < double.MaxValue - 1 && PageGeometry.HasHorizontalMarginOverrides;

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
        internal bool PageWidthsSettled => !UseVariablePageWidth || _pageWidthsSettled;

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
            !UseVariablePageWidth
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
        /// When <see cref="UseVariablePageWidth"/> is off this returns the base
        /// <c>MarginLeft + PageSize.Width</c>, so callers can invoke it unconditionally.
        /// The per-page left/right margins come from the same <see cref="PageGeometry"/> table layout
        /// paginated against; they resolve in true points, scaled into layout space by
        /// <c>PixelsPerPoint</c> exactly once (issue-#113 discipline).
        /// </summary>
        internal double PageContentRightOf(double y)
        {
            if (!UseVariablePageWidth)
                return MarginLeft + PageSize.Width;

            var ppp = (Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
            var geom = PageGeometry.GetPage(PageIndexOf(y));
            // The raw sheet width in layout px, recovered by construction (mirror of PageGeometryTable's
            // sheetPx height recovery): PdfGenerator.SetContent subtracts both point-space margins from
            // the point-space sheet, and the public wrappers scale PageSize and margins by PixelsPerPoint.
            var sheetPx = PageSize.Width + MarginLeft + MarginRight;
            var bandWidth = sheetPx - (geom.MarginLeftPt + geom.MarginRightPt) * ppp;

            // Degenerate override (left+right margins consume the whole sheet): fall back to the base
            // measure, the horizontal mirror of PageGeometryTable.Compute's band-height clamp, so a
            // pathological @page rule can never collapse content to a zero/negative width.
            if (bandWidth < 1.0)
                return MarginLeft + PageSize.Width;

            return MarginLeft + bandWidth;
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
            }
            catch
            { }
        }

        #endregion
    }
}