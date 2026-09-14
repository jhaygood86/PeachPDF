using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Builds one page-section's own root <see cref="CssBox"/> tree and records its geometry overrides,
    /// for <see cref="PdfGenerator"/> to resolve after <see cref="IDocumentBuilder.Page"/>'s handler
    /// returns (each unset geometry override falls back to the document's own <see cref="PdfGenerateConfig"/>).
    /// </summary>
    internal sealed class PageDescriptorBuilder(CssPropertyFactory properties) : IPageDescriptor
    {
        private PageSize? _pageSize;
        private (PdfLength Width, PdfLength Height)? _explicitSize;
        private PageOrientation? _orientation;
        private PdfLength? _marginTop;
        private PdfLength? _marginBottom;
        private PdfLength? _marginLeft;
        private PdfLength? _marginRight;
        private PdfColor? _background;
        private Action<ITextStyle>? _defaultTextStyleHandler;
        private Action<IContainer>? _headerHandler;
        private Action<IContainer>? _footerHandler;
        private CssBox? _rootBox;
        private readonly List<PageRule> _pageRules = [];

        public IPageDescriptor Size(PageSize size)
        {
            _pageSize = size;
            _explicitSize = null;
            return this;
        }

        public IPageDescriptor Size(PdfLength width, PdfLength height)
        {
            _explicitSize = (width, height);
            _pageSize = null;
            return this;
        }

        public IPageDescriptor Orientation(PageOrientation orientation)
        {
            _orientation = orientation;
            return this;
        }

        public IPageDescriptor Margin(PdfLength value)
        {
            _marginTop = _marginBottom = _marginLeft = _marginRight = value;
            return this;
        }

        public IPageDescriptor MarginHorizontal(PdfLength value)
        {
            _marginLeft = _marginRight = value;
            return this;
        }

        public IPageDescriptor MarginVertical(PdfLength value)
        {
            _marginTop = _marginBottom = value;
            return this;
        }

        public IPageDescriptor MarginTop(PdfLength value)
        {
            _marginTop = value;
            return this;
        }

        public IPageDescriptor MarginBottom(PdfLength value)
        {
            _marginBottom = value;
            return this;
        }

        public IPageDescriptor MarginLeft(PdfLength value)
        {
            _marginLeft = value;
            return this;
        }

        public IPageDescriptor MarginRight(PdfLength value)
        {
            _marginRight = value;
            return this;
        }

        public IPageDescriptor Background(PdfColor color)
        {
            _background = color;
            return this;
        }

        public IPageDescriptor DefaultTextStyle(Action<ITextStyle> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _defaultTextStyleHandler = handler;
            return this;
        }

        public IPageDescriptor Header(Action<IContainer> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _headerHandler = handler;
            return this;
        }

        public IPageDescriptor Footer(Action<IContainer> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _footerHandler = handler;
            return this;
        }

        public void Content(Action<IContainer> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            // The document root - CssBox.CreateBlock() (no parent) is the same primitive
            // RunningElementLayout/DomParser's own tree-root construction uses; it needs no
            // InheritStyle() call since there is no parent to inherit from.
            var root = CssBox.CreateBlock();

            if (_background is { } backgroundColor)
            {
                properties.Set(root, "background-color", backgroundColor);
            }

            if (_defaultTextStyleHandler is { } styleHandler)
            {
                styleHandler(new TextStyleApplier(root, properties));
            }

            if (_headerHandler is { } headerHandler)
            {
                BuildRunningElement(root, headerHandler, "peachpdf-header", "top-center");
            }

            if (_footerHandler is { } footerHandler)
            {
                BuildRunningElement(root, footerHandler, "peachpdf-footer", "bottom-center");
            }

            _rootBox = root;
            handler(new ContainerBuilder(root, properties));
        }

        /// <summary>
        /// Builds <paramref name="handler"/>'s content as an ordinary, normal-flow child of
        /// <paramref name="root"/>, marked <c>position: running(&lt;name&gt;)</c> (css-gcpm-3) so the
        /// layout engine excludes it from the page's own flow and registers it as that name's current
        /// occupant instead (<see cref="CssBox.RegisterAsRunningElement"/>, already called generically for
        /// any normal-flow child with <see cref="CssBox.IsRunningPositioned"/> - no new discovery code
        /// needed here). <paramref name="marginBoxSelector"/>'s own <c>content: element(&lt;name&gt;)</c>
        /// (a <c>@page { @top-center { ... } }</c> margin box, built directly rather than parsed from a
        /// caller-authored stylesheet - the one narrow, non-user-facing exception to this feature's
        /// "never route declarative content through the CSS parser" rule) then repaints that occupant at
        /// the top/bottom of every page, exactly like a real HTML document's own named margin box already
        /// does for repeating running content.
        /// </summary>
        private void BuildRunningElement(CssBox root, Action<IContainer> handler, string name, string marginBoxSelector)
        {
            var runningBox = CssPropertyFactory.CreateAnonymousBox(root);
            properties.Set(runningBox, "display", "block");
            properties.Set(runningBox, "position", $"running({name})");
            handler(new ContainerBuilder(runningBox, properties));

            // One shared, unconditional (no selector) PageRule for the whole page - PageRuleResolver's
            // own rule selection treats a selector-less PageRule as "the" base rule, keeping only the
            // last one seen (there is exactly one to match against, unlike a named/pseudo-class rule,
            // which can coexist with others by selector). A second base PageRule (one per Header/Footer
            // call) would silently discard the first's margin boxes rather than merging with them.
            if (_pageRules.Count == 0)
            {
                _pageRules.Add(new PageRule(new StylesheetParser()));
            }

            var pageRule = _pageRules[0];
            var marginRule = new MarginStyleRule(pageRule.Parser) { SelectorText = marginBoxSelector };
            marginRule.Style.Content = $"element({name})";
            pageRule.AppendChild(marginRule);
        }

        /// <summary>The page's own root box, built by <see cref="Content"/>.</summary>
        internal CssBox RootBox => _rootBox
            ?? throw new InvalidOperationException("IPageDescriptor.Content(...) must be called to supply the page's content.");

        /// <summary>
        /// The <c>@page</c> rules built for this page's <see cref="Header"/>/<see cref="Footer"/> (empty
        /// when neither was called) - <see cref="PdfGenerator"/> assigns these to
        /// <see cref="Html.Core.HtmlContainerInt.PageRules"/> before layout.
        /// </summary>
        internal IReadOnlyList<PageRule> PageRules => _pageRules;

        /// <summary>This page's own explicit sheet size override, or null to fall back to the document's <see cref="PdfGenerateConfig"/>.</summary>
        internal XSize? PageSizeOverride
        {
            get
            {
                if (_pageSize is { } size) return PageSizeConverter.ToSize(size);
                if (_explicitSize is { } explicitSize) return new XSize(explicitSize.Width.ToPoints(), explicitSize.Height.ToPoints());
                return null;
            }
        }

        internal PageOrientation? OrientationOverride => _orientation;
        internal double? MarginTopOverride => _marginTop?.ToPoints();
        internal double? MarginBottomOverride => _marginBottom?.ToPoints();
        internal double? MarginLeftOverride => _marginLeft?.ToPoints();
        internal double? MarginRightOverride => _marginRight?.ToPoints();
    }
}
