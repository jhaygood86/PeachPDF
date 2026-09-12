namespace PeachPDF.MathML
{
    /// <summary>The unit of a <see cref="MathLength"/> - MathML's own length grammar (MathML 3 §2.1.5),
    /// distinct from CSS's: it adds the "math unit" (<c>mu</c>, 1/18 of the current font's em, used by
    /// the named spacing keywords below) and drops CSS's viewport/container-relative units, which have
    /// no meaning inside a formula.</summary>
    internal enum MathLengthUnit
    {
        Em,
        Ex,
        Px,
        In,
        Cm,
        Mm,
        Pt,
        Pc,
        Percent,
        Mu,
    }

    /// <summary>A resolved MathML length: a numeric value plus <see cref="MathLengthUnit"/>. Resolving
    /// this to points against a specific font size/em happens in <c>MathLayoutEngine</c>, not here -
    /// this type only carries what the attribute text itself specified.</summary>
    internal readonly record struct MathLength(double Value, MathLengthUnit Unit)
    {
        public static readonly MathLength Zero = new(0, MathLengthUnit.Pt);
    }
}
