#nullable disable

using PeachPDF.Svg;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Shared, layer-agnostic grammar for the CSS <c>&lt;basic-shape&gt;</c> function values used by
    /// <c>clip-path</c> (CSS Masking Level 1 / CSS Shapes Level 1): <c>polygon()</c>, <c>inset()</c>,
    /// <c>circle()</c>, <c>ellipse()</c> and <c>path()</c>. Like <see cref="BackgroundPositionGrammar"/> /
    /// <see cref="BackgroundSizeGrammar"/>, it validates the grammar and captures the value's structure
    /// as <b>raw component strings / enums</b> (never resolved numbers), so both Layer A (the CSS-OM
    /// converter, which only needs to accept/reject and preserve the authored text) and Layer B (the
    /// render-time resolver in <c>PeachPDF.Html.Core</c>, which resolves each component against the
    /// element's reference box) share a single parser rather than re-implementing the grammar twice.
    /// <c>path()</c> is the one exception to "raw component strings": its argument is itself the SVG
    /// path-data mini-language, not a CSS length/percentage/keyword, so it is fully parsed here (via
    /// <see cref="SvgPathDataParser.TryParse"/>, the same parser <c>&lt;path d="..."&gt;</c> uses) into
    /// <see cref="PathSegment"/>s rather than deferred as text - per spec, a path string that doesn't
    /// conform to SVG 1.1's grammar (or that conforms but is empty) makes the whole <c>path()</c>
    /// invalid, which can only be checked by actually parsing it up front.
    /// </summary>
    internal static class BasicShapeGrammar
    {
        internal enum BasicShapeKind { None, Polygon, Inset, Circle, Ellipse, Path, Url }

        internal enum FillRule { NonZero, EvenOdd }

        internal enum ShapeRadiusKind { LengthPercentage, ClosestSide, FarthestSide }

        /// <summary>
        /// The <c>&lt;geometry-box&gt;</c> keyword (CSS Masking Level 1 §6.1) selecting which box a
        /// basic shape resolves against; defaults to <see cref="BorderBox"/> when absent.
        /// <see cref="FillBox"/>/<see cref="StrokeBox"/>/<see cref="ViewBox"/> are only meaningfully
        /// distinct from <see cref="BorderBox"/> for an element with an associated SVG bounding box;
        /// for a plain HTML box (the only kind <c>CssClipPathResolver</c> resolves against) CSS
        /// Masking 1 §7 itself specifies they compute to the used value of <see cref="BorderBox"/>.
        /// </summary>
        internal enum GeometryBoxKind { BorderBox, PaddingBox, ContentBox, MarginBox, FillBox, StrokeBox, ViewBox }

        /// <summary>The eight corner radii of an <c>inset(... round &lt;border-radius&gt;)</c> clause,
        /// each an authored length-percentage component string, in the same per-corner X/Y layout as
        /// <c>border-radius</c> itself (top-left, top-right, bottom-right, bottom-left).</summary>
        internal readonly struct CornerRadii
        {
            public string TLX { get; }
            public string TLY { get; }
            public string TRX { get; }
            public string TRY { get; }
            public string BRX { get; }
            public string BRY { get; }
            public string BLX { get; }
            public string BLY { get; }

            public CornerRadii(string tlx, string tly, string trx, string try_, string brx, string bry, string blx, string bly)
            {
                TLX = tlx; TLY = tly;
                TRX = trx; TRY = try_;
                BRX = brx; BRY = bry;
                BLX = blx; BLY = bly;
            }
        }

        /// <summary>A <c>&lt;shape-radius&gt;</c>: either an explicit length-percentage (component string
        /// in <see cref="Length"/>) or one of the <c>closest-side</c>/<c>farthest-side</c> keywords.</summary>
        internal readonly struct ShapeRadius
        {
            public ShapeRadiusKind Kind { get; }

            /// <summary>The length-percentage component string when <see cref="Kind"/> is
            /// <see cref="ShapeRadiusKind.LengthPercentage"/>; otherwise null.</summary>
            public string Length { get; }

            private ShapeRadius(ShapeRadiusKind kind, string length)
            {
                Kind = kind;
                Length = length;
            }

            public static readonly ShapeRadius ClosestSide = new(ShapeRadiusKind.ClosestSide, null);
            public static readonly ShapeRadius FarthestSide = new(ShapeRadiusKind.FarthestSide, null);
            public static ShapeRadius FromLength(string length) => new(ShapeRadiusKind.LengthPercentage, length);
        }

        /// <summary>A single <c>&lt;length-percentage&gt; &lt;length-percentage&gt;</c> polygon vertex,
        /// stored as the two authored component strings.</summary>
        internal readonly struct Point
        {
            public string X { get; }
            public string Y { get; }

            public Point(string x, string y)
            {
                X = x;
                Y = y;
            }
        }

        internal sealed record ParsedBasicShape
        {
            public BasicShapeKind Kind { get; private init; }

            /// <summary>The <c>&lt;geometry-box&gt;</c> keyword the shape resolves against (CSS Masking
            /// Level 1 §6.1); defaults to <see cref="GeometryBoxKind.BorderBox"/> when the value has no
            /// explicit keyword. Meaningful for every kind, including <see cref="BasicShapeKind.None"/>
            /// (a bare geometry-box with no additional shape function).</summary>
            public GeometryBoxKind GeometryBox { get; init; } = GeometryBoxKind.BorderBox;

            // --- polygon() ---
            public FillRule PolygonFillRule { get; private init; }
            public IReadOnlyList<Point> PolygonPoints { get; private init; }

            // --- inset() ---
            /// <summary>The four inset offsets in [top, right, bottom, left] order (CSS shorthand-filled),
            /// each an authored length-percentage component string.</summary>
            public IReadOnlyList<string> InsetEdges { get; private init; }

            /// <summary>The validated <c>&lt;border-radius&gt;</c> from an <c>inset(... round ...)</c>
            /// clause, or <see langword="null"/> when no <c>round</c> was present (the resolver draws a
            /// plain rectangle in that case).</summary>
            public CornerRadii? InsetRoundRadii { get; private init; }

            // --- circle() / ellipse() ---
            /// <summary>circle: the single radius. ellipse: the x-radius.</summary>
            public ShapeRadius RadiusX { get; private init; }
            /// <summary>ellipse: the y-radius. Unused for circle.</summary>
            public ShapeRadius RadiusY { get; private init; }
            /// <summary>Center x as an authored length-percentage component string (position keywords
            /// already resolved to <c>0%</c>/<c>50%</c>/<c>100%</c>). Default <c>50%</c>.</summary>
            public string CenterX { get; private init; }
            /// <summary>Center y, same convention as <see cref="CenterX"/>.</summary>
            public string CenterY { get; private init; }

            // --- path() ---
            /// <summary>The parsed SVG path-data segments (already validated non-empty/well-formed
            /// by <see cref="SvgPathDataParser.TryParse"/>), in the path's own coordinate system.</summary>
            public IReadOnlyList<PathSegment> PathSegments { get; private init; }
            public FillRule PathFillRule { get; private init; }

            // --- url() ---
            /// <summary>The fragment id (without the leading <c>#</c>) of a <c>url(#id)</c> clip source
            /// referencing an SVG <c>&lt;clipPath&gt;</c> element.</summary>
            public string UrlId { get; private init; }

            internal static ParsedBasicShape None(GeometryBoxKind geometryBox) => new()
            {
                Kind = BasicShapeKind.None,
                GeometryBox = geometryBox,
            };

            internal static ParsedBasicShape Polygon(FillRule fillRule, IReadOnlyList<Point> points) => new()
            {
                Kind = BasicShapeKind.Polygon,
                PolygonFillRule = fillRule,
                PolygonPoints = points,
            };

            internal static ParsedBasicShape Inset(IReadOnlyList<string> edges, CornerRadii? roundRadii) => new()
            {
                Kind = BasicShapeKind.Inset,
                InsetEdges = edges,
                InsetRoundRadii = roundRadii,
            };

            internal static ParsedBasicShape Circle(ShapeRadius radius, string centerX, string centerY) => new()
            {
                Kind = BasicShapeKind.Circle,
                RadiusX = radius,
                CenterX = centerX,
                CenterY = centerY,
            };

            internal static ParsedBasicShape Ellipse(ShapeRadius radiusX, ShapeRadius radiusY, string centerX, string centerY) => new()
            {
                Kind = BasicShapeKind.Ellipse,
                RadiusX = radiusX,
                RadiusY = radiusY,
                CenterX = centerX,
                CenterY = centerY,
            };

            internal static ParsedBasicShape Path(FillRule fillRule, IReadOnlyList<PathSegment> segments) => new()
            {
                Kind = BasicShapeKind.Path,
                PathFillRule = fillRule,
                PathSegments = segments,
            };

            internal static ParsedBasicShape Url(string urlId) => new()
            {
                Kind = BasicShapeKind.Url,
                UrlId = urlId,
            };
        }

        /// <summary>
        /// Parses a <c>clip-path</c> value's tokens into a <see cref="ParsedBasicShape"/>, or returns
        /// null when the value is not a valid basic shape - <b>including the literal <c>none</c></b>,
        /// which callers treat as "no clip". (Layer A therefore accepts <c>none</c> separately, since a
        /// null result here can't distinguish <c>none</c> from an invalid value.)
        /// <para>
        /// Per CSS Masking Level 1, the grammar is <c>&lt;clip-source&gt; | [ &lt;basic-shape&gt; ||
        /// &lt;geometry-box&gt; ] | none</c>: a <c>url(#id)</c> clip source (<see cref="BasicShapeKind.Url"/>)
        /// is its own alternative and never combines with a <c>&lt;geometry-box&gt;</c>; a basic-shape
        /// function and a <c>&lt;geometry-box&gt;</c> keyword may each appear alone or together, in
        /// either order (<c>||</c> is the "one or both, any order" combinator).
        /// </para>
        /// </summary>
        internal static ParsedBasicShape TryParse(IReadOnlyList<Token> tokens)
        {
            var significant = tokens.Where(t => t.Type != TokenType.Whitespace).ToArray();

            if (significant.Length == 0) return null;

            if (significant is [{ Type: TokenType.Url } urlToken])
                return ParsedBasicShape.Url(urlToken.Data.TrimStart('#'));

            GeometryBoxKind geometryBox = GeometryBoxKind.BorderBox;
            var foundGeometryBox = false;
            var remaining = new List<Token>(significant.Length);

            foreach (var token in significant)
            {
                if (token.Type == TokenType.Ident && TryGeometryBox(token, out var kind))
                {
                    if (foundGeometryBox) return null; // at most one <geometry-box> keyword
                    geometryBox = kind;
                    foundGeometryBox = true;
                }
                else
                {
                    remaining.Add(token);
                }
            }

            if (remaining.Count == 0)
                return foundGeometryBox ? ParsedBasicShape.None(geometryBox) : null;

            if (remaining is not [{ Type: TokenType.Function } function]) return null;

            var args = function.ArgumentTokens.Where(t => t.Type != TokenType.Whitespace).ToArray();

            ParsedBasicShape shape = null;
            if (function.Data.Isi(FunctionNames.Polygon)) shape = ParsePolygon(args);
            else if (function.Data.Isi(FunctionNames.Inset)) shape = ParseInset(args);
            else if (function.Data.Isi(FunctionNames.Circle)) shape = ParseCircle(args);
            else if (function.Data.Isi(FunctionNames.Ellipse)) shape = ParseEllipse(args);
            else if (function.Data.Isi(FunctionNames.Path)) shape = ParsePath(args);

            return shape is null ? null : shape with { GeometryBox = geometryBox };
        }

        private static bool TryGeometryBox(Token token, out GeometryBoxKind kind)
        {
            if (token.Data.Isi(Keywords.BorderBox)) { kind = GeometryBoxKind.BorderBox; return true; }
            if (token.Data.Isi(Keywords.PaddingBox)) { kind = GeometryBoxKind.PaddingBox; return true; }
            if (token.Data.Isi(Keywords.ContentBox)) { kind = GeometryBoxKind.ContentBox; return true; }
            if (token.Data.Isi(Keywords.MarginBox)) { kind = GeometryBoxKind.MarginBox; return true; }
            if (token.Data.Isi(Keywords.FillBox)) { kind = GeometryBoxKind.FillBox; return true; }
            if (token.Data.Isi(Keywords.StrokeBox)) { kind = GeometryBoxKind.StrokeBox; return true; }
            if (token.Data.Isi(Keywords.ViewBox)) { kind = GeometryBoxKind.ViewBox; return true; }

            kind = default;
            return false;
        }

        private static ParsedBasicShape ParsePolygon(IReadOnlyList<Token> args)
        {
            var groups = SplitByComma(args);
            if (groups.Count == 0) return null;

            var fillRule = FillRule.NonZero;
            var firstGroup = 0;

            // An optional leading fill-rule ident is its own comma-separated group: "polygon(evenodd, x y, ...)".
            if (groups[0].Count == 1 && TryFillRule(groups[0][0], out var parsedRule))
            {
                fillRule = parsedRule;
                firstGroup = 1;
            }

            var points = new List<Point>();

            for (var i = firstGroup; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group.Count != 2) return null;
                if (!IsLengthPercentage(group[0]) || !IsLengthPercentage(group[1])) return null;
                points.Add(new Point(group[0].ToValue(), group[1].ToValue()));
            }

            return points.Count == 0 ? null : ParsedBasicShape.Polygon(fillRule, points);
        }

        private static ParsedBasicShape ParseInset(IReadOnlyList<Token> args)
        {
            if (args.Count == 0) return null;

            // SplitAtKeyword returns false when "round" is found with nothing after it - the same
            // "keyword with no following radius" malformation ParseCircle/ParseEllipse already reject
            // for "at" via this same helper.
            if (!SplitAtKeyword(args, Keywords.Round, out var lengthTokens, out var hasRound, out var roundTokens))
                return null;

            if (lengthTokens.Count is < 1 or > 4) return null;
            if (lengthTokens.Any(t => !IsLengthPercentage(t))) return null;

            CornerRadii? radii = null;
            if (hasRound)
            {
                radii = ParseBorderRadius(roundTokens);
                // An invalid <border-radius> (wrong arity, invalid/negative token, stray "/") invalidates
                // the whole inset() - and therefore the whole clip-path value - per CSS Shapes Level 1,
                // the same as any other malformed component (issue #217 gap: this used to only check
                // "round" wasn't followed by nothing, silently accepting e.g. "round banana").
                if (radii is null) return null;
            }

            var values = lengthTokens.Select(t => t.ToValue()).ToArray();
            var (top, right, bottom, left) = ExpandFourValues(values);

            return ParsedBasicShape.Inset([top, right, bottom, left], radii);
        }

        /// <summary>
        /// Parses an <c>inset(... round &lt;border-radius&gt;)</c> clause's radius tokens as a real
        /// <c>&lt;border-radius&gt;</c> value: <c>&lt;length-percentage [0,∞]&gt;{1,4} [ / &lt;length-percentage
        /// [0,∞]&gt;{1,4} ]?</c>, via <see cref="ExpandFourValues"/> - the same 1-4-value expansion
        /// algorithm the <c>border-radius</c> shorthand's own grammar defines (just with corner rather
        /// than edge labels), reimplemented at the raw-token level here rather than routed through
        /// <c>BorderRadiusConverter</c>/<c>PeriodicValueConverter</c>, which return CSS-OM
        /// <c>IPropertyValue</c>s built for the full property-cascade pipeline clip-path's own grammar
        /// deliberately bypasses (see this file's own class doc comment).
        /// </summary>
        private static CornerRadii? ParseBorderRadius(IReadOnlyList<Token> tokens)
        {
            var slashIndex = -1;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Type == TokenType.Delim && tokens[i].Data.Is("/"))
                {
                    slashIndex = i;
                    break;
                }
            }

            var horizontalTokens = slashIndex >= 0 ? tokens.Take(slashIndex).ToArray() : tokens.ToArray();
            var verticalTokens = slashIndex >= 0 ? tokens.Skip(slashIndex + 1).ToArray() : null;

            if (horizontalTokens.Length is < 1 or > 4) return null;
            if (verticalTokens is { Length: < 1 or > 4 }) return null;
            if (!horizontalTokens.All(IsNonNegativeLengthPercentage)) return null;
            if (verticalTokens != null && !verticalTokens.All(IsNonNegativeLengthPercentage)) return null;

            var hValues = horizontalTokens.Select(t => t.ToValue()).ToArray();
            var (htl, htr, hbr, hbl) = ExpandFourValues(hValues);

            var vValues = verticalTokens != null ? verticalTokens.Select(t => t.ToValue()).ToArray() : hValues;
            var (vtl, vtr, vbr, vbl) = ExpandFourValues(vValues);

            return new CornerRadii(htl, vtl, htr, vtr, hbr, vbr, hbl, vbl);
        }

        private static ParsedBasicShape ParseCircle(IReadOnlyList<Token> args)
        {
            if (!SplitAtKeyword(args, Keywords.At, out var radiusTokens, out var hasAt, out var positionTokens))
                return null;

            ShapeRadius radius;
            switch (radiusTokens.Count)
            {
                case 0:
                    radius = ShapeRadius.ClosestSide;
                    break;
                case 1:
                    if (!TryShapeRadius(radiusTokens[0], out radius)) return null;
                    break;
                default:
                    return null;
            }

            if (!ResolveCenter(hasAt, positionTokens, out var centerX, out var centerY)) return null;

            return ParsedBasicShape.Circle(radius, centerX, centerY);
        }

        private static ParsedBasicShape ParseEllipse(IReadOnlyList<Token> args)
        {
            if (!SplitAtKeyword(args, Keywords.At, out var radiusTokens, out var hasAt, out var positionTokens))
                return null;

            ShapeRadius rx, ry;
            switch (radiusTokens.Count)
            {
                case 0:
                    rx = ShapeRadius.ClosestSide;
                    ry = ShapeRadius.ClosestSide;
                    break;
                case 2:
                    if (!TryShapeRadius(radiusTokens[0], out rx)) return null;
                    if (!TryShapeRadius(radiusTokens[1], out ry)) return null;
                    break;
                default:
                    return null;
            }

            if (!ResolveCenter(hasAt, positionTokens, out var centerX, out var centerY)) return null;

            return ParsedBasicShape.Ellipse(rx, ry, centerX, centerY);
        }

        /// <summary>
        /// Parses <c>path( [&lt;fill-rule&gt;,]? &lt;string&gt; )</c>. Per CSS Shapes Level 1, the
        /// string must be well-formed, non-empty SVG 1.1 path data or the whole <c>path()</c> - and
        /// therefore the whole <c>clip-path</c> value - is invalid; that conformance is checked here,
        /// up front, by actually running <see cref="SvgPathDataParser.TryParse"/> rather than
        /// deferring it, since a raw component string can't distinguish "well-formed" from
        /// "malformed" the way a length-percentage's calc() text can.
        /// </summary>
        private static ParsedBasicShape ParsePath(IReadOnlyList<Token> args)
        {
            var groups = SplitByComma(args);
            if (groups.Count is not (1 or 2)) return null;

            var fillRule = FillRule.NonZero;
            var stringGroupIndex = 0;

            if (groups.Count == 2)
            {
                if (groups[0].Count != 1 || !TryFillRule(groups[0][0], out fillRule)) return null;
                stringGroupIndex = 1;
            }

            var stringGroup = groups[stringGroupIndex];
            if (stringGroup.Count != 1 || stringGroup[0].Type != TokenType.String) return null;

            return SvgPathDataParser.TryParse(stringGroup[0].Data, out var segments)
                ? ParsedBasicShape.Path(fillRule, segments)
                : null;
        }

        /// <summary>Resolves the optional <c>at &lt;position&gt;</c> tail (via the shared
        /// <see cref="BackgroundPositionGrammar"/>) to center-x/center-y component strings; defaults to
        /// <c>50% 50%</c> when absent.</summary>
        private static bool ResolveCenter(bool hasAt, IReadOnlyList<Token> positionTokens, out string centerX, out string centerY)
        {
            centerX = "50%";
            centerY = "50%";

            if (!hasAt) return true;

            var parsed = BackgroundPositionGrammar.TryParse(positionTokens);
            if (parsed is null) return false;

            centerX = ComponentToLength(parsed.X, horizontal: true);
            centerY = ComponentToLength(parsed.Y, horizontal: false);
            return true;
        }

        private static string ComponentToLength(BackgroundPositionGrammar.Component c, bool horizontal)
        {
            switch (c.Keyword)
            {
                case BackgroundPositionGrammar.AxisKeyword.Center:
                    return "50%";
                case BackgroundPositionGrammar.AxisKeyword.Left:
                case BackgroundPositionGrammar.AxisKeyword.Top:
                    // "left"/"top" == 0% edge; "left 20px" == 20px in from that edge.
                    return c.Offset != null ? c.Offset.Value.ToValue() : "0%";
                case BackgroundPositionGrammar.AxisKeyword.Right:
                case BackgroundPositionGrammar.AxisKeyword.Bottom:
                    // "right"/"bottom" == 100% edge; "right 20px" == 20px in from the far edge.
                    return c.Offset != null ? $"calc(100% - {c.Offset.Value.ToValue()})" : "100%";
                default: // a bare length-percentage
                    return c.Offset!.Value.ToValue();
            }
        }

        /// <summary>Splits tokens at the first standalone ident equal to <paramref name="keyword"/>
        /// (e.g. <c>at</c>). Fails if the keyword appears with nothing after it.</summary>
        private static bool SplitAtKeyword(IReadOnlyList<Token> tokens, string keyword,
            out IReadOnlyList<Token> before, out bool found, out IReadOnlyList<Token> after)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Type == TokenType.Ident && tokens[i].Data.Isi(keyword))
                {
                    before = tokens.Take(i).ToArray();
                    after = tokens.Skip(i + 1).ToArray();
                    found = true;
                    return after.Count > 0;
                }
            }

            before = tokens.ToArray();
            after = [];
            found = false;
            return true;
        }

        private static List<List<Token>> SplitByComma(IReadOnlyList<Token> tokens)
        {
            var groups = new List<List<Token>>();
            var current = new List<Token>();

            foreach (var token in tokens)
            {
                if (token.Type == TokenType.Comma)
                {
                    groups.Add(current);
                    current = new List<Token>();
                }
                else
                {
                    current.Add(token);
                }
            }

            groups.Add(current);

            // A trailing/leading/doubled comma yields an empty group, which is always malformed here.
            return groups.Any(g => g.Count == 0) && tokens.Count > 0 ? [] : groups;
        }

        /// <summary>
        /// The CSS "1 to 4 values" box-expansion algorithm shared by <c>inset()</c>'s edges (labelled
        /// top/right/bottom/left below) and an <c>inset(... round &lt;border-radius&gt;)</c> clause's
        /// corners (<see cref="ParseBorderRadius"/> reuses this same expansion, just with corner rather
        /// than edge labels - it is the identical 1/2/3/4-value algorithm either way).
        /// </summary>
        private static (string top, string right, string bottom, string left) ExpandFourValues(IReadOnlyList<string> values) => values.Count switch
        {
            1 => (values[0], values[0], values[0], values[0]),
            2 => (values[0], values[1], values[0], values[1]),
            3 => (values[0], values[1], values[2], values[1]),
            _ => (values[0], values[1], values[2], values[3]),
        };

        private static bool TryFillRule(Token token, out FillRule fillRule)
        {
            if (token.Type == TokenType.Ident)
            {
                if (token.Data.Isi(Keywords.Nonzero)) { fillRule = FillRule.NonZero; return true; }
                if (token.Data.Isi(Keywords.Evenodd)) { fillRule = FillRule.EvenOdd; return true; }
            }

            fillRule = FillRule.NonZero;
            return false;
        }

        private static bool TryShapeRadius(Token token, out ShapeRadius radius)
        {
            if (token.Type == TokenType.Ident)
            {
                if (token.Data.Isi(Keywords.ClosestSide)) { radius = ShapeRadius.ClosestSide; return true; }
                if (token.Data.Isi(Keywords.FarthestSide)) { radius = ShapeRadius.FarthestSide; return true; }
                radius = default;
                return false;
            }

            if (IsLengthPercentage(token))
            {
                // A <shape-radius> is a non-negative <length-percentage> (CSS Shapes 1 §3.2); a negative
                // radius makes the whole clip-path value invalid.
                if (token is { Type: TokenType.Dimension or TokenType.Percentage, Value: < 0f } or
                    { Type: TokenType.Number, Value: < 0f })
                {
                    radius = default;
                    return false;
                }

                radius = ShapeRadius.FromLength(token.ToValue());
                return true;
            }

            radius = default;
            return false;
        }

        private static bool IsLengthPercentage(Token token)
        {
            if (token.Type is TokenType.Dimension or TokenType.Percentage) return true;
            // A calc()-family function computes to a <length-percentage> at used-value time. The render-time
            // resolver (CssClipPathResolver → CssValueParser.ParseLength) evaluates it, and the token's
            // ToValue() reconstructs the full "calc(…)" text, so accept it here rather than invalidating the
            // whole shape (this is what lets a Charts.css area/line polygon vertex be a calc() expression).
            if (token is { Type: TokenType.Function } function && CalcParser.IsCalcFamily(function.Data)) return true;
            // Unitless zero is a valid length.
            return token is { Type: TokenType.Number, Value: 0f };
        }

        /// <summary>A <c>&lt;length-percentage [0,∞]&gt;</c> - the grammar for a <c>border-radius</c>
        /// component (used by <see cref="ParseBorderRadius"/>): a literal negative <c>Dimension</c>/
        /// <c>Percentage</c>/<c>Number</c> is rejected, same as <see cref="TryShapeRadius"/>'s
        /// non-negative check for a circle/ellipse radius; a negative <c>calc()</c> can't be statically
        /// rejected here and is left to the render-time resolver, same existing precedent.</summary>
        private static bool IsNonNegativeLengthPercentage(Token token) =>
            IsLengthPercentage(token) && token is not (
                { Type: TokenType.Dimension or TokenType.Percentage, Value: < 0f } or
                { Type: TokenType.Number, Value: < 0f });
    }
}
