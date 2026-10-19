using PeachDrawing.Core;
using PeachBrackets.Css;
using PeachBrackets.Building;
using PeachPDF.Rendering;
using System;
using System.Collections.Generic;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Builds one page-section's own root <c>CssBox</c> tree and records its geometry overrides,
    /// for <see cref="PdfGenerator"/> to resolve after <see cref="IDocumentBuilder.Page"/>'s handler
    /// returns (each unset geometry override falls back to the document's own <see cref="PdfGenerateConfig"/>).
    /// </summary>
    internal sealed class PageDescriptorBuilder(DocumentBuildContext context) : IPageDescriptor
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
        private DeclarativePage? _page;

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

            // The document root: a block with no parent, which needs no style inheritance since there is nothing to inherit from.
            var page = context.CreatePage();
            var root = page.Root;
            page.Overrides = Overrides;

            if (_background is { } backgroundColor)
            {
                root.SetStyle("background-color", backgroundColor);
            }

            if (_defaultTextStyleHandler is { } styleHandler)
            {
                styleHandler(new TextStyleApplier(root));
            }

            if (_headerHandler is { } headerHandler)
            {
                BuildRunningElement(page, root, headerHandler, "peachpdf-header", "top-center");
            }

            if (_footerHandler is { } footerHandler)
            {
                BuildRunningElement(page, root, footerHandler, "peachpdf-footer", "bottom-center");
            }

            _page = page;
            handler(new ContainerBuilder(root));
        }

        /// <summary>
        /// Builds <paramref name="handler"/>'s content as an ordinary, normal-flow child of
        /// <paramref name="root"/>, marked <c>position: running(&lt;name&gt;)</c> (css-gcpm-3) so the
        /// layout engine excludes it from the page's own flow and registers it as that name's current
        /// occupant instead (<c>CssBox.RegisterAsRunningElement</c>, already called generically for
        /// any normal-flow child with <c>CssBox.IsRunningPositioned</c> - no new discovery code
        /// needed here). <paramref name="marginBoxSelector"/>'s own <c>content: element(&lt;name&gt;)</c>
        /// (a <c>@page { @top-center { ... } }</c> margin box, built directly rather than parsed from a
        /// caller-authored stylesheet - the one narrow, non-user-facing exception to this feature's
        /// "never route declarative content through the CSS parser" rule) then repaints that occupant at
        /// the top/bottom of every page, exactly like a real HTML document's own named margin box already
        /// does for repeating running content.
        /// </summary>
        private void BuildRunningElement(DeclarativePage page, DocumentNode root, Action<IContainer> handler, string name, string marginBoxSelector)
        {
            var runningBox = root.AppendBlock();
            runningBox.SetStyle("display", "block");
            runningBox.SetStyle("position", $"running({name})");
            handler(new ContainerBuilder(runningBox));

            page.AddMarginBox(marginBoxSelector, $"element({name})");
        }

        private PageGeometryOverrides Overrides =>
            (_pageSize is not null || _explicitSize is not null ? PageGeometryOverrides.Size : PageGeometryOverrides.None)
            | (_marginTop is not null ? PageGeometryOverrides.MarginTop : PageGeometryOverrides.None)
            | (_marginRight is not null ? PageGeometryOverrides.MarginRight : PageGeometryOverrides.None)
            | (_marginBottom is not null ? PageGeometryOverrides.MarginBottom : PageGeometryOverrides.None)
            | (_marginLeft is not null ? PageGeometryOverrides.MarginLeft : PageGeometryOverrides.None);

        /// <summary>The page this descriptor built: its content tree, margin boxes and which geometry it set explicitly.</summary>
        internal DeclarativePage Page => _page
            ?? throw new InvalidOperationException("IPageDescriptor.Content(...) must be called to supply the page's content.");

        /// <summary>This page's own explicit sheet size override, or null to fall back to the document's <see cref="PdfGenerateConfig"/>.</summary>
        internal Size? PageSizeOverride
        {
            get
            {
                if (_pageSize is { } size) return PageSizeConverter.ToSize(size);
                if (_explicitSize is { } explicitSize) return new Size(explicitSize.Width.ToPoints(), explicitSize.Height.ToPoints());
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
