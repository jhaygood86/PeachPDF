using System;
using PeachDrawing.Text.Internal.Fonts;

namespace PeachDrawing.Text
{
    /// <summary>
    /// The faces that share one family name in a <see cref="FontSet"/>.
    /// </summary>
    public sealed class TypefaceFamily
    {
        private readonly FontSet _set;

        internal TypefaceFamily(FontSet set, string name)
        {
            _set = set;
            Name = name;
        }

        /// <summary>The name of the family, as it was registered or as the installed fonts declare it.</summary>
        public string Name { get; }

        /// <summary>
        /// Finds the face of this family that matches a query best (CSS Fonts 4 section 5).
        /// </summary>
        /// <remarks>
        /// Of the faces that qualify, the query's slant is matched first, then its width, then its weight, taking the
        /// nearest when none is exact; where faces tie, the one added last wins. When the query names a character to
        /// cover, only faces that cover it qualify.
        /// </remarks>
        /// <param name="query">What is wanted.</param>
        /// <param name="match">The best face, and what has to be faked because it falls short of the query.</param>
        /// <returns><see langword="false"/> when the query asks for a character that no face of the family covers.</returns>
        /// <exception cref="InvalidOperationException">
        /// The face that matched is not a font this library can parse. A font is read in full when it is first matched and not
        /// when it is added, so a set does not pay for the fonts a caller never uses.
        /// </exception>
        public bool TryMatch(in TypefaceQuery query, out TypefaceMatch match)
        {
            if (query.MustCover is not null && _set.Resolver.ResolveFace(Name, new FaceRequest(query.Weight, query.IsItalic, query.WidthPercent ?? WidthClasses.ToPercent(query.Width), query.MustCover)) is null)
            {
                match = default;
                return false;
            }

            match = _set.MatchCore(Name, query);
            return true;
        }
    }
}
