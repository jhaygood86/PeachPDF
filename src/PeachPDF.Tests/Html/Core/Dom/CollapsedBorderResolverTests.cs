using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// CSS 2.1 §17.6.2 border-conflict resolution, tested in isolation from layout/paint -
    /// <see cref="CollapsedBorderResolver"/> is a pure function of candidate values.
    /// </summary>
    public class CollapsedBorderResolverTests
    {
        private static CollapsedBorderCandidate Candidate(
            LineStyle style,
            double width,
            CollapsedBorderOrigin origin = CollapsedBorderOrigin.Cell,
            int row = 0,
            int column = 0,
            byte colorSeed = 0) =>
            new(style, width, RColor.FromArgb(colorSeed, colorSeed, colorSeed), origin, row, column);

        [Fact]
        public void StylePriority_MatchesCss21Order()
        {
            // §17.6.2's own order - deliberately NOT LineStyle's declaration order.
            var ordered = new[]
            {
                LineStyle.None, LineStyle.Inset, LineStyle.Groove, LineStyle.Outset, LineStyle.Ridge,
                LineStyle.Dotted, LineStyle.Dashed, LineStyle.Solid, LineStyle.Double, LineStyle.Hidden
            };

            for (var i = 1; i < ordered.Length; i++)
            {
                Assert.True(
                    CollapsedBorderResolver.StylePriority(ordered[i]) > CollapsedBorderResolver.StylePriority(ordered[i - 1]),
                    $"{ordered[i]} should outrank {ordered[i - 1]}");
            }
        }

        [Fact]
        public void NoCandidates_ResolvesToNone()
        {
            var result = CollapsedBorderResolver.Resolve(ReadOnlySpan<CollapsedBorderCandidate>.Empty, leftToRight: true);

            Assert.Equal(CollapsedBorder.None, result);
            Assert.False(result.IsPainted);
        }

        [Fact]
        public void Hidden_SuppressesEveryOtherCandidate_EvenAWiderMoreSpecificOne()
        {
            var candidates = new[]
            {
                Candidate(LineStyle.Hidden, 1, CollapsedBorderOrigin.Table),
                Candidate(LineStyle.Solid, 10, CollapsedBorderOrigin.Cell),
            };

            var result = CollapsedBorderResolver.Resolve(candidates, leftToRight: true);

            Assert.Equal(LineStyle.Hidden, result.Style);
            Assert.False(result.IsPainted);
        }

        [Fact]
        public void None_NeverParticipates_UnlessEveryCandidateIsNone()
        {
            var candidates = new[]
            {
                Candidate(LineStyle.None, 5),
                Candidate(LineStyle.Solid, 1),
            };

            var result = CollapsedBorderResolver.Resolve(candidates, leftToRight: true);

            Assert.Equal(LineStyle.Solid, result.Style);
            Assert.Equal(1, result.Width);
        }

        [Fact]
        public void AllNone_ResolvesToNone()
        {
            var candidates = new[] { Candidate(LineStyle.None, 5), Candidate(LineStyle.None, 3) };

            var result = CollapsedBorderResolver.Resolve(candidates, leftToRight: true);

            Assert.Equal(CollapsedBorder.None, result);
        }

        [Fact]
        public void WiderWins_RegardlessOfStyleAndOrigin()
        {
            var candidates = new[]
            {
                Candidate(LineStyle.Dotted, 5, CollapsedBorderOrigin.Table),
                Candidate(LineStyle.Double, 3, CollapsedBorderOrigin.Cell),
            };

            var result = CollapsedBorderResolver.Resolve(candidates, leftToRight: true);

            Assert.Equal(LineStyle.Dotted, result.Style);
            Assert.Equal(5, result.Width);
        }

        [Fact]
        public void EqualWidth_HigherStylePriorityWins()
        {
            // §17.6.2 order, adjacent pairs: each earlier style must outrank the one after it.
            var order = new[]
            {
                LineStyle.Double, LineStyle.Solid, LineStyle.Dashed, LineStyle.Dotted,
                LineStyle.Ridge, LineStyle.Outset, LineStyle.Groove, LineStyle.Inset
            };

            for (var i = 0; i < order.Length - 1; i++)
            {
                var higher = order[i];
                var lower = order[i + 1];

                var candidates = new[]
                {
                    Candidate(lower, 2, CollapsedBorderOrigin.Cell),
                    Candidate(higher, 2, CollapsedBorderOrigin.Table),
                };

                var result = CollapsedBorderResolver.Resolve(candidates, leftToRight: true);

                Assert.True(result.Style == higher, $"{higher} should have outranked {lower}, got {result.Style}");
            }
        }

        [Fact]
        public void EqualWidthAndStyle_MoreSpecificOriginWins()
        {
            var origins = new[]
            {
                CollapsedBorderOrigin.Cell, CollapsedBorderOrigin.Row, CollapsedBorderOrigin.RowGroup,
                CollapsedBorderOrigin.Column, CollapsedBorderOrigin.ColumnGroup, CollapsedBorderOrigin.Table
            };

            for (var i = 0; i < origins.Length; i++)
            {
                for (var j = i + 1; j < origins.Length; j++)
                {
                    var moreSpecific = origins[i];
                    var lessSpecific = origins[j];

                    var winnerColor = RColor.FromArgb(1, 1, 1);
                    var loserColor = RColor.FromArgb(2, 2, 2);
                    var candidates = new[]
                    {
                        new CollapsedBorderCandidate(LineStyle.Solid, 2, loserColor, lessSpecific, 0, 0),
                        new CollapsedBorderCandidate(LineStyle.Solid, 2, winnerColor, moreSpecific, 0, 0),
                    };

                    var result = CollapsedBorderResolver.Resolve(candidates, leftToRight: true);

                    Assert.True(result.Color == winnerColor,
                        $"{moreSpecific} should have outranked {lessSpecific}");
                }
            }
        }

        [Fact]
        public void DifferOnlyInColor_MoreSpecificOriginStillDecides()
        {
            var cellColor = RColor.FromArgb(255, 0, 0);
            var tableColor = RColor.FromArgb(0, 0, 255);
            var candidates = new[]
            {
                new CollapsedBorderCandidate(LineStyle.Solid, 2, tableColor, CollapsedBorderOrigin.Table, 0, 0),
                new CollapsedBorderCandidate(LineStyle.Solid, 2, cellColor, CollapsedBorderOrigin.Cell, 0, 0),
            };

            var result = CollapsedBorderResolver.Resolve(candidates, leftToRight: true);

            Assert.Equal(cellColor, result.Color);
        }

        [Fact]
        public void SameOrigin_Ltr_PrefersLeftmostColumnThenTopmostRow()
        {
            var candidates = new[]
            {
                Candidate(LineStyle.Solid, 2, CollapsedBorderOrigin.Cell, row: 5, column: 3),
                Candidate(LineStyle.Solid, 2, CollapsedBorderOrigin.Cell, row: 1, column: 1),
            };

            var result = CollapsedBorderResolver.Resolve(candidates, leftToRight: true);

            // Both candidates resolve to the same style/width; assert via a distinguishing color instead.
            var withColor = new[]
            {
                new CollapsedBorderCandidate(LineStyle.Solid, 2, RColor.FromArgb(1, 1, 1), CollapsedBorderOrigin.Cell, 5, 3),
                new CollapsedBorderCandidate(LineStyle.Solid, 2, RColor.FromArgb(2, 2, 2), CollapsedBorderOrigin.Cell, 1, 1),
            };
            var resolved = CollapsedBorderResolver.Resolve(withColor, leftToRight: true);

            Assert.Equal(RColor.FromArgb(2, 2, 2), resolved.Color); // column 1 beats column 3
        }

        [Fact]
        public void SameOrigin_Rtl_PrefersRightmostColumn()
        {
            var candidates = new[]
            {
                new CollapsedBorderCandidate(LineStyle.Solid, 2, RColor.FromArgb(1, 1, 1), CollapsedBorderOrigin.Cell, 0, 3),
                new CollapsedBorderCandidate(LineStyle.Solid, 2, RColor.FromArgb(2, 2, 2), CollapsedBorderOrigin.Cell, 0, 1),
            };

            var resolved = CollapsedBorderResolver.Resolve(candidates, leftToRight: false);

            Assert.Equal(RColor.FromArgb(1, 1, 1), resolved.Color); // column 3 beats column 1 under rtl
        }

        [Fact]
        public void SameOriginAndColumn_PrefersTopmostRow()
        {
            var candidates = new[]
            {
                new CollapsedBorderCandidate(LineStyle.Solid, 2, RColor.FromArgb(1, 1, 1), CollapsedBorderOrigin.Cell, 5, 0),
                new CollapsedBorderCandidate(LineStyle.Solid, 2, RColor.FromArgb(2, 2, 2), CollapsedBorderOrigin.Cell, 1, 0),
            };

            var resolved = CollapsedBorderResolver.Resolve(candidates, leftToRight: true);

            Assert.Equal(RColor.FromArgb(2, 2, 2), resolved.Color); // row 1 beats row 5
        }

        [Fact]
        public void HiddenWithZeroWidth_StillSuppresses()
        {
            // A computed border-width is forced to 0 for style "none" but NOT for "hidden" - a
            // hidden border with an explicit width of 0 must still short-circuit via clause 1, not be
            // skipped as if it were unpainted.
            var candidates = new[]
            {
                Candidate(LineStyle.Hidden, 0),
                Candidate(LineStyle.Solid, 5),
            };

            var result = CollapsedBorderResolver.Resolve(candidates, leftToRight: true);

            Assert.Equal(LineStyle.Hidden, result.Style);
        }

        [Fact]
        public void Css21WorkedExample_RowWins()
        {
            // CSS 2.1 §17.6.2's own example: "a table cell has a 4px solid red border with a table
            // that has a 10px double blue border" resolves to... the wider one; here transcribed with
            // a cell 3px solid vs. a row 5px dashed - the row wins purely on width despite lower style
            // priority and lower origin priority, exactly clause 3 overriding clauses 4 and 5.
            var candidates = new[]
            {
                Candidate(LineStyle.Solid, 3, CollapsedBorderOrigin.Cell),
                Candidate(LineStyle.Dashed, 5, CollapsedBorderOrigin.Row),
            };

            var result = CollapsedBorderResolver.Resolve(candidates, leftToRight: true);

            Assert.Equal(LineStyle.Dashed, result.Style);
            Assert.Equal(5, result.Width);
        }
    }
}
