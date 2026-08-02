using System;

namespace PeachPDF.SourceGenerators.Model
{
    /// <summary>One settable member found on a real target type (<c>CssBox</c>/<c>SvgElement</c>), for PPG010/PPG011.</summary>
    internal readonly struct TargetMember : IEquatable<TargetMember>
    {
        public string Name { get; }
        public string TypeDisplay { get; }
        public bool HasSetter { get; }

        public TargetMember(string name, string typeDisplay, bool hasSetter)
        {
            Name = name;
            TypeDisplay = typeDisplay;
            HasSetter = hasSetter;
        }

        public bool Equals(TargetMember other) =>
            Name == other.Name && TypeDisplay == other.TypeDisplay && HasSetter == other.HasSetter;

        public override bool Equals(object? obj) => obj is TargetMember other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Name.GetHashCode();
                hash = hash * 31 + TypeDisplay.GetHashCode();
                hash = hash * 31 + HasSetter.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// A tiny, value-equatable projection of the real shape of <c>CssBox</c> and <c>SvgElement</c>,
    /// captured once from the <see cref="Microsoft.CodeAnalysis.Compilation"/> per pipeline run.
    /// Deliberately NOT a raw symbol/Compilation in the incremental pipeline's output — those change
    /// identity on every keystroke and would defeat caching for every downstream stage. See
    /// <c>CssPropertyGenerator.CaptureTargetTypes</c>.
    /// </summary>
    internal sealed class TargetTypeModel : IEquatable<TargetTypeModel>
    {
        public bool CssBoxFound { get; }
        public EquatableArray<TargetMember> CssBoxMembers { get; }
        public bool SvgElementFound { get; }
        public EquatableArray<TargetMember> SvgElementMembers { get; }

        public TargetTypeModel(bool cssBoxFound, EquatableArray<TargetMember> cssBoxMembers,
            bool svgElementFound, EquatableArray<TargetMember> svgElementMembers)
        {
            CssBoxFound = cssBoxFound;
            CssBoxMembers = cssBoxMembers;
            SvgElementFound = svgElementFound;
            SvgElementMembers = svgElementMembers;
        }

        public bool TryGetCssBoxMember(string name, out TargetMember member) => TryGet(CssBoxMembers, name, out member);
        public bool TryGetSvgElementMember(string name, out TargetMember member) => TryGet(SvgElementMembers, name, out member);

        private static bool TryGet(EquatableArray<TargetMember> members, string name, out TargetMember member)
        {
            foreach (var m in members)
            {
                if (m.Name == name)
                {
                    member = m;
                    return true;
                }
            }

            member = default;
            return false;
        }

        public bool Equals(TargetTypeModel? other) =>
            other is not null && CssBoxFound == other.CssBoxFound && CssBoxMembers.Equals(other.CssBoxMembers)
            && SvgElementFound == other.SvgElementFound && SvgElementMembers.Equals(other.SvgElementMembers);

        public override bool Equals(object? obj) => Equals(obj as TargetTypeModel);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = CssBoxFound.GetHashCode();
                hash = hash * 31 + CssBoxMembers.GetHashCode();
                hash = hash * 31 + SvgElementFound.GetHashCode();
                hash = hash * 31 + SvgElementMembers.GetHashCode();
                return hash;
            }
        }
    }
}
